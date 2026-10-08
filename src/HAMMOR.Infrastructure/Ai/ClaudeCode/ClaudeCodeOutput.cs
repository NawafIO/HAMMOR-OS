using System.Text.Json;
using System.Text.RegularExpressions;
using HAMMOR.Core.Ai;
using HAMMOR.Core.Diagnostics;

namespace HAMMOR.Infrastructure.Ai.ClaudeCode;

/// <summary>What one Claude Code process produced.</summary>
/// <param name="ExitCode">Process exit code; -1 when it was stopped.</param>
/// <param name="StandardOutput">Standard output, up to the output bound.</param>
/// <param name="StandardError">Standard error, up to the output bound.</param>
/// <param name="OutputLimitReached">The process was stopped for producing too much output.</param>
/// <param name="TimedOut">The process was stopped for taking too long.</param>
public sealed record ClaudeCodeRun(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool OutputLimitReached = false,
    bool TimedOut = false);

/// <summary>Why a Claude Code request did not produce an answer.</summary>
public enum ClaudeCodeFailure
{
    None = 0,
    NotSignedIn = 1,
    UsageLimited = 2,
    NeedsUpdate = 3,
    TimedOut = 4,
    OutputTooLarge = 5,
    Error = 6,
}

/// <summary>A parsed request result.</summary>
public sealed record ClaudeCodeReply(
    ClaudeCodeFailure Failure,
    string Text,
    string? Model,
    AiUsage? Usage,
    string? StopReason)
{
    public bool IsSuccess => Failure == ClaudeCodeFailure.None;

    internal static ClaudeCodeReply Failed(ClaudeCodeFailure failure, string message) =>
        new(failure, message, null, null, null);
}

/// <summary>A parsed <c>claude auth status --json</c> result.</summary>
/// <param name="LoggedIn">Claude Code's answer, or null when it gave none.</param>
/// <param name="AuthMethod">Its sign-in method when signed in; never a credential.</param>
/// <param name="NeedsUpdate">Claude Code did not know the command or an option.</param>
/// <param name="Problem">What went wrong when there was no answer, redacted.</param>
public sealed record ClaudeCodeAuth(bool? LoggedIn, string? AuthMethod, bool NeedsUpdate, string? Problem);

/// <summary>
/// Reads Claude Code's documented outputs. Never parses terminal text for an
/// answer: answers come only from the JSON result; standard error is used
/// only to explain a failure.
/// </summary>
/// <remarks>
/// Shapes verified against Claude Code 2.1.292: a request prints one JSON
/// object with <c>type</c> "result", <c>is_error</c>, <c>result</c>,
/// <c>stop_reason</c>, <c>api_error_status</c>, <c>usage</c> and
/// <c>modelUsage</c>, and exits non-zero on failure with the failure as the
/// <c>result</c>. An unknown option exits 1 with
/// <c>error: unknown option '…'</c> on standard error and nothing on standard
/// output. <c>auth status --json</c> prints <c>loggedIn</c> and
/// <c>authMethod</c> and exits 1 when not signed in.
/// </remarks>
public static partial class ClaudeCodeOutput
{
    private const int MaxMessageChars = 600;

    [GeneratedRegex(@"^\s*v?(\d+\.\d+\.\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"error:\s+unknown\s+(option|command)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnknownOptionPattern();

    [GeneratedRegex(@"hit your [^·\r\n]*limit|usage limit|spend limit|limit reached|out of (extra )?usage",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UsageLimitPattern();

    [GeneratedRegex(@"not logged in|please run /login|run /login|authentication required|login expired|"
        + @"invalid api key|oauth token|sign in again|signed out of claude|login was rejected|could not resolve authentication",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NotSignedInPattern();

    /// <summary>The version from <c>claude --version</c> ("2.1.292 (Claude Code)").</summary>
    public static string? ParseVersion(string? standardOutput)
    {
        var match = VersionPattern().Match(standardOutput ?? string.Empty);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static ClaudeCodeAuth ParseAuthStatus(ClaudeCodeRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.TimedOut)
        {
            return new ClaudeCodeAuth(null, null, false, "Claude Code did not answer in time.");
        }

        if (TryParseObject(run.StandardOutput) is { } json)
        {
            using (json)
            {
                var root = json.RootElement;
                if (root.TryGetProperty("loggedIn", out var loggedIn)
                    && loggedIn.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    var signedIn = loggedIn.GetBoolean();
                    var method = signedIn ? StringOrNull(root, "authMethod") : null;
                    return new ClaudeCodeAuth(signedIn, method == "none" ? null : method, false, null);
                }
            }
        }

        if (UnknownOptionPattern().IsMatch(run.StandardError))
        {
            return new ClaudeCodeAuth(null, null, true, Message(FirstLine(run.StandardError)));
        }

        return new ClaudeCodeAuth(null, null, false, Message(FirstLine(run.StandardError) ?? ExitedWith(run.ExitCode)));
    }

    public static ClaudeCodeReply ParseReply(ClaudeCodeRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.TimedOut)
        {
            return ClaudeCodeReply.Failed(ClaudeCodeFailure.TimedOut, "Claude Code did not answer in time.");
        }

        if (run.OutputLimitReached)
        {
            return ClaudeCodeReply.Failed(
                ClaudeCodeFailure.OutputTooLarge, "Claude Code's answer was larger than HAMMOR accepts.");
        }

        if (TryParseObject(run.StandardOutput) is not { } json)
        {
            // No JSON: the run did not start (unknown option, bad install).
            var line = FirstLine(run.StandardError);
            if (UnknownOptionPattern().IsMatch(run.StandardError))
            {
                return ClaudeCodeReply.Failed(ClaudeCodeFailure.NeedsUpdate, Message(line));
            }

            return ClaudeCodeReply.Failed(Classify(line, null), Message(line ?? ExitedWith(run.ExitCode)));
        }

        using (json)
        {
            var root = json.RootElement;
            var text = StringOrNull(root, "result") ?? string.Empty;
            var isError = run.ExitCode != 0
                || (root.TryGetProperty("is_error", out var flag) && flag.ValueKind == JsonValueKind.True);

            if (!isError)
            {
                return new ClaudeCodeReply(
                    ClaudeCodeFailure.None,
                    text,
                    FirstModel(root),
                    Usage(root),
                    StringOrNull(root, "stop_reason"));
            }

            int? apiStatus = root.TryGetProperty("api_error_status", out var status) && status.ValueKind == JsonValueKind.Number
                ? status.GetInt32()
                : null;
            var explanation = text.Length > 0 ? text : FirstLine(run.StandardError) ?? ExitedWith(run.ExitCode);
            return ClaudeCodeReply.Failed(Classify(explanation, apiStatus), Message(explanation));
        }
    }

    /// <summary>Sorts a failure message from Claude Code into what the user should do.</summary>
    public static ClaudeCodeFailure Classify(string? message, int? apiStatus)
    {
        var text = message ?? string.Empty;

        // "Server is temporarily limiting requests (not your usage limit)".
        if (text.Contains("not your usage limit", StringComparison.OrdinalIgnoreCase))
        {
            return ClaudeCodeFailure.Error;
        }

        if (UsageLimitPattern().IsMatch(text))
        {
            return ClaudeCodeFailure.UsageLimited;
        }

        if (apiStatus is 401 || NotSignedInPattern().IsMatch(text))
        {
            return ClaudeCodeFailure.NotSignedIn;
        }

        return ClaudeCodeFailure.Error;
    }

    private static JsonDocument? TryParseObject(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var trimmed = output.Trim();
        if (Parse(trimmed) is { } whole)
        {
            return whole;
        }

        // Tolerate a stray line before the result: take the last line that is an object.
        var lines = trimmed.Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var candidate = lines[i].Trim();
            if (candidate.StartsWith('{') && Parse(candidate) is { } document)
            {
                return document;
            }
        }

        return null;

        static JsonDocument? Parse(string text)
        {
            try
            {
                var document = JsonDocument.Parse(text);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    return document;
                }

                document.Dispose();
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    private static string? StringOrNull(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? FirstModel(JsonElement root)
    {
        if (root.TryGetProperty("modelUsage", out var models) && models.ValueKind == JsonValueKind.Object)
        {
            foreach (var model in models.EnumerateObject())
            {
                return model.Name;
            }
        }

        return null;
    }

    private static AiUsage? Usage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new AiUsage(Number(usage, "input_tokens"), Number(usage, "output_tokens"));

        static long Number(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;
    }

    private static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return null;
    }

    private static string ExitedWith(int exitCode) => $"Claude Code exited with code {exitCode}.";

    /// <summary>Redacted and bounded, ready for the UI or a log.</summary>
    private static string Message(string? text)
    {
        var redacted = SecretRedactor.Redact(text);
        return redacted.Length <= MaxMessageChars ? redacted : redacted[..MaxMessageChars] + "…";
    }
}
