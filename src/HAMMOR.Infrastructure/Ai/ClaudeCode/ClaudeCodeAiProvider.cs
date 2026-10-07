using System.ComponentModel;
using HAMMOR.Core.Ai;
using HAMMOR.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace HAMMOR.Infrastructure.Ai.ClaudeCode;

/// <summary>
/// Claude through the user's own, officially installed Claude Code and their
/// own Claude account: no API key, and HAMMOR never handles the credential.
/// </summary>
/// <remarks>
/// <para>
/// Each turn is one documented print-mode request
/// (<see cref="ClaudeCodeArguments.Request"/>). Claude Code runs with no tools
/// at all, so it cannot act on the machine; HAMMOR's own tools, permission
/// engine and confirmations are not reachable through this provider. It is
/// text-only (it does not implement <see cref="IToolCallingProvider"/>), and
/// the agent loop falls back to its text path.
/// </para>
/// <para>
/// Anthropic's terms allow an end user to sign in to the unmodified Claude
/// Code binary with their own Claude subscription, and set conditions this
/// provider follows: the binary is run as published, none of its sign-in
/// methods is disabled, sign-in completes through Anthropic's own flow, and
/// HAMMOR never collects, stores or forwards a Claude credential. Usage is
/// the user's own, under their plan's limits.
/// </para>
/// </remarks>
public sealed class ClaudeCodeAiProvider : IAiProvider
{
    /// <summary>Id matched against <c>AiSettings.PrimaryProvider</c>.</summary>
    public const string Id = "claude-code";

    // Turns re-use a recent status instead of starting Claude Code twice more;
    // a sign-out made elsewhere still shows on the next request's answer.
    private static readonly TimeSpan StatusMaxAge = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    private readonly IClaudeCodeAccount _account;
    private readonly IClaudeCodeCli _cli;
    private readonly ILogger<ClaudeCodeAiProvider> _logger;

    public ClaudeCodeAiProvider(IClaudeCodeAccount account, IClaudeCodeCli cli, ILogger<ClaudeCodeAiProvider> logger)
    {
        _account = account ?? throw new ArgumentNullException(nameof(account));
        _cli = cli ?? throw new ArgumentNullException(nameof(cli));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderId => Id;

    public string DisplayName => "Claude Code";

    public async Task<ProviderAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await _account.GetStatusAsync(StatusMaxAge, cancellationToken).ConfigureAwait(false);
            return ToAvailability(status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ProviderAvailability.Error("Claude Code could not be checked: " + SecretRedactor.Redact(ex.Message));
        }
    }

    public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Messages.Count == 0)
        {
            throw new AiProviderException("The conversation is empty.");
        }

        var status = await _account.GetStatusAsync(StatusMaxAge, cancellationToken).ConfigureAwait(false);
        if (!status.CanSend)
        {
            throw new AiProviderException(ToAvailability(status).Detail);
        }

        var prompt = ClaudeCodeTranscript.Compose(request.SystemPrompt, request.Messages);

        ClaudeCodeRun run;
        try
        {
            run = await _cli.RunAsync(ClaudeCodeArguments.Request(prompt.SystemPrompt), prompt.Prompt, RequestTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ClaudeCodeNotFoundException)
        {
            await _account.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
            throw new AiProviderException("Claude Code is not installed.");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            throw new AiProviderException("Claude Code could not be started: " + SecretRedactor.Redact(ex.Message), ex);
        }

        var reply = ClaudeCodeOutput.ParseReply(run);
        _account.Report(reply);

        if (reply.IsSuccess)
        {
            return new AiResponse(reply.Text, reply.Model ?? Id, reply.Usage, reply.StopReason);
        }

        // The kind and exit code only: never the prompt or the answer.
        _logger.LogWarning("Claude Code request failed ({Failure}, exit code {ExitCode}).", reply.Failure, run.ExitCode);
        throw new AiProviderException(reply.Text);
    }

    /// <summary>The provider state the status bar and the agent loop see.</summary>
    public static ProviderAvailability ToAvailability(ClaudeCodeStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return status.State switch
        {
            ClaudeCodeState.SignedIn => ProviderAvailability.Ready(
                $"Signed in to Claude Code ({DescribeMethod(status.AuthMethod)})."),

            // Still sendable: Claude Code says when the limit resets, and the
            // next request is the only way to learn it has.
            ClaudeCodeState.UsageLimited => ProviderAvailability.Ready(
                $"Signed in to Claude Code; usage limit reached: {status.Detail}"),

            ClaudeCodeState.NotSignedIn => ProviderAvailability.NotConfigured(
                "Claude Code is installed but not signed in. Sign in from Settings."),

            ClaudeCodeState.UnsupportedInstall => ProviderAvailability.NotConfigured(
                "Only a script launcher for Claude Code was found (" + status.Detail
                + "). HAMMOR runs the native claude.exe; install it with the native installer or WinGet."),

            ClaudeCodeState.NeedsUpdate => ProviderAvailability.Error(
                "Claude Code rejected an option HAMMOR uses; update it with 'claude update'. " + status.Detail),

            ClaudeCodeState.Error => ProviderAvailability.Error(
                "Claude Code could not be checked: " + (status.Detail ?? "no details.")),

            _ => ProviderAvailability.NotConfigured("Claude Code is not installed."),
        };
    }

    /// <summary>A plain description of Claude Code's sign-in method; never a credential.</summary>
    public static string DescribeMethod(string? authMethod)
    {
        if (string.IsNullOrEmpty(authMethod))
        {
            return "unknown method";
        }

        return authMethod switch
        {
            "claude.ai" => "Claude subscription",
            "oauth_token" => "Claude subscription token",
            "api_key" or "api_key_helper" => "Anthropic API key, billed to the API",
            "third_party" => "cloud provider",
            _ => authMethod,
        };
    }
}
