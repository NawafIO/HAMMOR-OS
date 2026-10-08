using HAMMOR.Core.Ai;
using HAMMOR.Core.Diagnostics;
using HAMMOR.Infrastructure.Ai.ClaudeCode;
using Xunit;

namespace HAMMOR.Core.Tests.Ai.ClaudeCode;

/// <summary>
/// Reading Claude Code's documented outputs: answers only from the JSON
/// result, failures sorted into what the user should do, nothing secret
/// passed on.
/// </summary>
public sealed class ClaudeCodeOutputTests
{
    [Fact]
    public void A_success_gives_the_result_model_usage_and_stop_reason()
    {
        var reply = ClaudeCodeOutput.ParseReply(new ClaudeCodeRun(0, ClaudeCodeSamples.Success, string.Empty));

        Assert.True(reply.IsSuccess);
        Assert.Equal("Hello from Claude.", reply.Text);
        Assert.Equal("claude-haiku-4-5-20251001", reply.Model);
        Assert.Equal(new AiUsage(12, 5), reply.Usage);
        Assert.Equal("end_turn", reply.StopReason);
    }

    [Fact]
    public void A_failure_inside_the_run_explains_itself_in_the_result()
    {
        var reply = ClaudeCodeOutput.ParseReply(new ClaudeCodeRun(
            1, ClaudeCodeSamples.BadModel, "[claude-code:unrecognized_model] {\"model\":\"not-a-real-model-xyz\"}\n"));

        Assert.Equal(ClaudeCodeFailure.Error, reply.Failure);
        Assert.StartsWith("There's an issue with the selected model", reply.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("You've hit your session limit · resets 3:45pm")]
    [InlineData("You've hit your weekly limit · resets Mon 12:00am")]
    [InlineData("You've hit your Opus limit · resets 3:45pm")]
    [InlineData("You've hit your monthly spend limit · raise it at claude.ai/settings/usage")]
    [InlineData("You've hit your individual usage limit")]
    public void Plan_limits_are_usage_limits(string message)
    {
        var reply = ClaudeCodeOutput.ParseReply(new ClaudeCodeRun(1, ClaudeCodeSamples.Failure(message), string.Empty));

        Assert.Equal(ClaudeCodeFailure.UsageLimited, reply.Failure);
        Assert.Equal(message, reply.Text);
    }

    [Fact]
    public void Server_throttling_is_not_the_users_usage_limit()
    {
        Assert.Equal(
            ClaudeCodeFailure.Error,
            ClaudeCodeOutput.Classify("API Error: Server is temporarily limiting requests (not your usage limit)", 429));
    }

    [Theory]
    [InlineData("Not logged in · Please run /login")]
    [InlineData("Authentication required · Sign in again to continue")]
    [InlineData("OAuth token has expired")]
    [InlineData("Claude.ai login expired")]
    [InlineData("Invalid API key")]
    public void A_lost_sign_in_is_recognised(string message)
    {
        Assert.Equal(ClaudeCodeFailure.NotSignedIn, ClaudeCodeOutput.Classify(message, null));
    }

    [Fact]
    public void A_401_means_signed_out()
    {
        Assert.Equal(ClaudeCodeFailure.NotSignedIn, ClaudeCodeOutput.Classify("Request failed.", 401));
    }

    [Fact]
    public void An_unknown_option_means_claude_code_needs_an_update()
    {
        var reply = ClaudeCodeOutput.ParseReply(new ClaudeCodeRun(1, string.Empty, "error: unknown option '--safe-mode'\n"));

        Assert.Equal(ClaudeCodeFailure.NeedsUpdate, reply.Failure);
        Assert.Equal("error: unknown option '--safe-mode'", reply.Text);
    }

    [Fact]
    public void Timeouts_and_oversized_answers_are_failures_without_parsing()
    {
        Assert.Equal(
            ClaudeCodeFailure.TimedOut,
            ClaudeCodeOutput.ParseReply(new ClaudeCodeRun(-1, ClaudeCodeSamples.Success, string.Empty, TimedOut: true)).Failure);
        Assert.Equal(
            ClaudeCodeFailure.OutputTooLarge,
            ClaudeCodeOutput.ParseReply(new ClaudeCodeRun(-1, ClaudeCodeSamples.Success, string.Empty, OutputLimitReached: true)).Failure);
    }

    [Fact]
    public void Failure_messages_are_redacted()
    {
        const string key = "sk-ant-api03-AbCdEf1234567890XyZ_-abcdefghij";

        var reply = ClaudeCodeOutput.ParseReply(new ClaudeCodeRun(
            1, ClaudeCodeSamples.Failure("Request failed with key " + key), string.Empty));

        Assert.DoesNotContain(key, reply.Text, StringComparison.Ordinal);
        Assert.Contains(SecretRedactor.Mask, reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stray_line_before_the_result_is_tolerated()
    {
        var reply = ClaudeCodeOutput.ParseReply(new ClaudeCodeRun(
            0, "Warning: something minor\n" + ClaudeCodeSamples.Success + "\n", string.Empty));

        Assert.True(reply.IsSuccess);
        Assert.Equal("Hello from Claude.", reply.Text);
    }

    [Fact]
    public void Exit_code_one_is_a_failure_even_if_the_json_says_otherwise()
    {
        var reply = ClaudeCodeOutput.ParseReply(new ClaudeCodeRun(1, ClaudeCodeSamples.Success, string.Empty));

        Assert.False(reply.IsSuccess);
    }

    [Fact]
    public void Auth_status_reports_signed_in_and_the_method_only()
    {
        var auth = ClaudeCodeOutput.ParseAuthStatus(new ClaudeCodeRun(0, ClaudeCodeSamples.SignedIn, string.Empty));

        Assert.True(auth.LoggedIn);
        Assert.Equal("claude.ai", auth.AuthMethod);
        Assert.False(auth.NeedsUpdate);
    }

    [Fact]
    public void Auth_status_reports_signed_out_with_exit_code_one()
    {
        var auth = ClaudeCodeOutput.ParseAuthStatus(new ClaudeCodeRun(1, ClaudeCodeSamples.NotSignedIn, string.Empty));

        Assert.False(auth.LoggedIn);
        Assert.Null(auth.AuthMethod);
    }

    [Fact]
    public void An_old_claude_code_without_auth_status_needs_an_update()
    {
        var auth = ClaudeCodeOutput.ParseAuthStatus(new ClaudeCodeRun(1, string.Empty, "error: unknown option '--json'\n"));

        Assert.True(auth.NeedsUpdate);
        Assert.Null(auth.LoggedIn);
    }

    [Fact]
    public void Unreadable_auth_status_is_a_problem_not_a_guess()
    {
        var auth = ClaudeCodeOutput.ParseAuthStatus(new ClaudeCodeRun(2, "not json", "something broke\n"));

        Assert.Null(auth.LoggedIn);
        Assert.Equal("something broke", auth.Problem);
    }

    [Theory]
    [InlineData("2.1.292 (Claude Code)\n", "2.1.292")]
    [InlineData("v2.0.14", "2.0.14")]
    [InlineData("", null)]
    [InlineData("Claude Code", null)]
    public void The_version_is_read_from_its_first_line(string output, string? expected)
    {
        Assert.Equal(expected, ClaudeCodeOutput.ParseVersion(output));
    }
}
