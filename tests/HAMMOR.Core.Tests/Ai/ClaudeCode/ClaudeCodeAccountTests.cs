using HAMMOR.Core.Ai;
using HAMMOR.Infrastructure.Ai.ClaudeCode;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HAMMOR.Core.Tests.Ai.ClaudeCode;

/// <summary>
/// The Claude Code account as HAMMOR sees it, and the provider built on it:
/// status from Claude Code's own commands, sign-in in Claude Code's own
/// window, requests through print mode, never a credential.
/// </summary>
public sealed class ClaudeCodeAccountTests
{
    private readonly FakeClaudeCodeCli _cli = new() { Respond = ClaudeCodeSamples.SignedInMachine };
    private readonly ManualTimeProvider _time = new();

    private ClaudeCodeAccount CreateAccount() =>
        new(_cli, NullLogger<ClaudeCodeAccount>.Instance, _time);

    private ClaudeCodeAiProvider CreateProvider(ClaudeCodeAccount account) =>
        new(account, _cli, NullLogger<ClaudeCodeAiProvider>.Instance);

    [Fact]
    public async Task Not_installed_starts_nothing()
    {
        _cli.Location = ClaudeCodeLocation.None;

        var status = await CreateAccount().RefreshAsync();

        Assert.Equal(ClaudeCodeState.NotInstalled, status.State);
        Assert.Empty(_cli.Runs);
    }

    [Fact]
    public async Task Only_an_npm_launcher_is_an_unsupported_install()
    {
        var launcher = Path.Combine(Path.GetTempPath(), "npm", "claude.cmd");
        _cli.Location = new ClaudeCodeLocation(null, launcher);

        var status = await CreateAccount().RefreshAsync();

        Assert.Equal(ClaudeCodeState.UnsupportedInstall, status.State);
        Assert.Equal(launcher, status.Detail);
        Assert.Empty(_cli.Runs);
    }

    [Fact]
    public async Task Signed_in_reports_the_version_and_method_from_claude_code()
    {
        var status = await CreateAccount().RefreshAsync();

        Assert.Equal(ClaudeCodeState.SignedIn, status.State);
        Assert.Equal("2.1.292", status.Version);
        Assert.Equal("claude.ai", status.AuthMethod);
        Assert.Equal(1, _cli.CountRuns(ClaudeCodeArguments.Version));
        Assert.Equal(1, _cli.CountRuns(ClaudeCodeArguments.AuthStatus));
    }

    [Fact]
    public async Task Signed_out_is_not_signed_in()
    {
        _cli.Respond = (arguments, _) => arguments.SequenceEqual(ClaudeCodeArguments.AuthStatus)
            ? new ClaudeCodeRun(1, ClaudeCodeSamples.NotSignedIn, string.Empty)
            : new ClaudeCodeRun(0, ClaudeCodeSamples.Version, string.Empty);

        var status = await CreateAccount().RefreshAsync();

        Assert.Equal(ClaudeCodeState.NotSignedIn, status.State);
        Assert.Null(status.AuthMethod);
    }

    [Fact]
    public async Task An_old_claude_code_needs_an_update()
    {
        _cli.Respond = (arguments, _) => arguments.SequenceEqual(ClaudeCodeArguments.AuthStatus)
            ? new ClaudeCodeRun(1, string.Empty, "error: unknown option '--json'\n")
            : new ClaudeCodeRun(0, "1.0.0 (Claude Code)", string.Empty);

        var status = await CreateAccount().RefreshAsync();

        Assert.Equal(ClaudeCodeState.NeedsUpdate, status.State);
        Assert.Equal("1.0.0", status.Version);
    }

    [Fact]
    public async Task A_recent_status_is_reused_until_it_ages()
    {
        var account = CreateAccount();

        await account.GetStatusAsync(TimeSpan.FromMinutes(2));
        _time.Advance(TimeSpan.FromMinutes(1));
        await account.GetStatusAsync(TimeSpan.FromMinutes(2));
        Assert.Equal(1, _cli.CountRuns(ClaudeCodeArguments.AuthStatus));

        _time.Advance(TimeSpan.FromMinutes(2));
        await account.GetStatusAsync(TimeSpan.FromMinutes(2));
        Assert.Equal(2, _cli.CountRuns(ClaudeCodeArguments.AuthStatus));
    }

    [Fact]
    public async Task Sign_in_opens_claude_codes_own_window_then_checks_again()
    {
        var status = await CreateAccount().SignInAsync();

        var login = Assert.Single(_cli.InteractiveRuns);
        Assert.Equal(ClaudeCodeArguments.AuthLogin, login);
        Assert.Equal(ClaudeCodeState.SignedIn, status.State);
        Assert.Equal(1, _cli.CountRuns(ClaudeCodeArguments.AuthStatus));
    }

    [Fact]
    public async Task Sign_out_runs_logout_then_checks_again()
    {
        var signedOut = false;
        _cli.Respond = (arguments, input) =>
        {
            if (arguments.SequenceEqual(ClaudeCodeArguments.AuthLogout))
            {
                signedOut = true;
                return new ClaudeCodeRun(0, string.Empty, string.Empty);
            }

            return arguments.SequenceEqual(ClaudeCodeArguments.AuthStatus) && signedOut
                ? new ClaudeCodeRun(1, ClaudeCodeSamples.NotSignedIn, string.Empty)
                : ClaudeCodeSamples.SignedInMachine(arguments, input);
        };

        var status = await CreateAccount().SignOutAsync();

        Assert.Equal(1, _cli.CountRuns(ClaudeCodeArguments.AuthLogout));
        Assert.Equal(ClaudeCodeState.NotSignedIn, status.State);
    }

    [Fact]
    public async Task Changes_are_announced_once()
    {
        var account = CreateAccount();
        var announced = new List<ClaudeCodeState>();
        account.StatusChanged += (_, status) => announced.Add(status.State);

        await account.RefreshAsync();
        _time.Advance(TimeSpan.FromMinutes(5));
        await account.RefreshAsync();

        Assert.Equal(new[] { ClaudeCodeState.SignedIn }, announced);
    }

    [Fact]
    public async Task A_request_answers_from_the_json_result_with_history_on_stdin()
    {
        var provider = CreateProvider(CreateAccount());

        var response = await provider.CompleteAsync(new AiRequest
        {
            SystemPrompt = "You are HAMMOR.",
            Messages = [AiMessage.User("Hi"), AiMessage.Assistant("Hello."), AiMessage.User("How are you?")],
        });

        Assert.Equal("Hello from Claude.", response.Text);
        Assert.Equal("claude-haiku-4-5-20251001", response.Model);
        Assert.Equal(new AiUsage(12, 5), response.Usage);

        var request = Assert.Single(_cli.Runs, run => run.Arguments.Contains("-p"));
        Assert.Equal(new[] { "-p", "--output-format", "json", "--tools", "" }, request.Arguments.Take(5));
        Assert.StartsWith("--system-prompt=You are HAMMOR.", request.Arguments[^1], StringComparison.Ordinal);
        Assert.DoesNotContain(request.Arguments, argument => argument.Contains("How are you?", StringComparison.Ordinal));
        Assert.EndsWith("How are you?", request.Input, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_is_sent_while_signed_out()
    {
        _cli.Respond = (arguments, _) => arguments.SequenceEqual(ClaudeCodeArguments.AuthStatus)
            ? new ClaudeCodeRun(1, ClaudeCodeSamples.NotSignedIn, string.Empty)
            : new ClaudeCodeRun(0, ClaudeCodeSamples.Version, string.Empty);
        var provider = CreateProvider(CreateAccount());

        var error = await Assert.ThrowsAsync<AiProviderException>(
            () => provider.CompleteAsync(new AiRequest { Messages = [AiMessage.User("Hi")] }));

        Assert.Contains("not signed in", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_cli.Runs, run => run.Arguments.Contains("-p"));
    }

    [Fact]
    public async Task A_usage_limit_is_shown_until_a_request_succeeds()
    {
        var limited = true;
        _cli.Respond = (arguments, input) => arguments.Contains("-p") && limited
            ? new ClaudeCodeRun(1, ClaudeCodeSamples.Failure("You've hit your session limit · resets 3:45pm"), string.Empty)
            : ClaudeCodeSamples.SignedInMachine(arguments, input);
        var account = CreateAccount();
        var provider = CreateProvider(account);
        var request = new AiRequest { Messages = [AiMessage.User("Hi")] };

        var error = await Assert.ThrowsAsync<AiProviderException>(() => provider.CompleteAsync(request));
        Assert.Equal("You've hit your session limit · resets 3:45pm", error.Message);
        Assert.Equal(ClaudeCodeState.UsageLimited, account.Current.State);

        // Still sendable: only the next request can show the limit has reset.
        Assert.True((await provider.CheckAvailabilityAsync()).IsUsable);

        limited = false;
        await provider.CompleteAsync(request);
        Assert.Equal(ClaudeCodeState.SignedIn, account.Current.State);
    }

    [Fact]
    public async Task A_lost_sign_in_found_by_a_request_updates_the_account()
    {
        _cli.Respond = (arguments, input) => arguments.Contains("-p")
            ? new ClaudeCodeRun(1, ClaudeCodeSamples.Failure("Not logged in · Please run /login"), string.Empty)
            : ClaudeCodeSamples.SignedInMachine(arguments, input);
        var account = CreateAccount();

        await Assert.ThrowsAsync<AiProviderException>(
            () => CreateProvider(account).CompleteAsync(new AiRequest { Messages = [AiMessage.User("Hi")] }));

        Assert.Equal(ClaudeCodeState.NotSignedIn, account.Current.State);
    }

    [Fact]
    public void The_provider_is_text_only()
    {
        var provider = CreateProvider(CreateAccount());

        Assert.Equal("claude-code", provider.ProviderId);
        Assert.IsNotAssignableFrom<IToolCallingProvider>(provider);
    }

    [Theory]
    [InlineData(ClaudeCodeState.SignedIn, ProviderState.Ready)]
    [InlineData(ClaudeCodeState.UsageLimited, ProviderState.Ready)]
    [InlineData(ClaudeCodeState.NotSignedIn, ProviderState.NotConfigured)]
    [InlineData(ClaudeCodeState.NotInstalled, ProviderState.NotConfigured)]
    [InlineData(ClaudeCodeState.UnsupportedInstall, ProviderState.NotConfigured)]
    [InlineData(ClaudeCodeState.NeedsUpdate, ProviderState.Error)]
    [InlineData(ClaudeCodeState.Error, ProviderState.Error)]
    public void Every_state_maps_to_a_provider_state(ClaudeCodeState state, ProviderState expected)
    {
        var status = new ClaudeCodeStatus(state, "2.1.292", "claude.ai", "detail", DateTimeOffset.UnixEpoch);

        Assert.Equal(expected, ClaudeCodeAiProvider.ToAvailability(status).State);
    }
}
