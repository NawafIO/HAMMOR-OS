using HAMMOR.Infrastructure.Ai.ClaudeCode;

namespace HAMMOR.Core.Tests.Ai.ClaudeCode;

/// <summary>A machine with exactly the environment variables and files a test names.</summary>
internal sealed class FakeClaudeCodeHost(bool isWindows) : IClaudeCodeHost
{
    public Dictionary<string, string?> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Files { get; } = new(StringComparer.Ordinal);

    public bool IsWindows { get; } = isWindows;

    public string? GetEnvironmentVariable(string name) => Variables.TryGetValue(name, out var value) ? value : null;

    public bool FileExists(string path) => Files.Contains(path);
}

/// <summary>
/// Stands in for Claude Code: records every command line and answers from a
/// script. Starts no process.
/// </summary>
internal sealed class FakeClaudeCodeCli : IClaudeCodeCli
{
    public static readonly string ExecutablePath = Path.Combine(Path.GetTempPath(), "hammor-tests", "claude.exe");

    public ClaudeCodeLocation Location { get; set; } = new(ExecutablePath, null);

    public List<(IReadOnlyList<string> Arguments, string? Input)> Runs { get; } = [];

    public List<IReadOnlyList<string>> InteractiveRuns { get; } = [];

    /// <summary>Answers a hidden run; by default every command succeeds silently.</summary>
    public Func<IReadOnlyList<string>, string?, ClaudeCodeRun> Respond { get; set; } = (_, _) => new ClaudeCodeRun(0, string.Empty, string.Empty);

    public int? InteractiveExitCode { get; set; } = 0;

    public ClaudeCodeLocation Locate() => Location;

    public Task<ClaudeCodeRun> RunAsync(
        IReadOnlyList<string> arguments,
        string? standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!Location.Found)
        {
            throw new ClaudeCodeNotFoundException(Location);
        }

        Runs.Add((arguments, standardInput));
        return Task.FromResult(Respond(arguments, standardInput));
    }

    public Task<int?> RunInteractiveAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!Location.Found)
        {
            throw new ClaudeCodeNotFoundException(Location);
        }

        InteractiveRuns.Add(arguments);
        return Task.FromResult(InteractiveExitCode);
    }

    public int CountRuns(IReadOnlyList<string> arguments) =>
        Runs.Count(run => run.Arguments.SequenceEqual(arguments));
}

/// <summary>A clock that moves only when told to.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Claude Code outputs, shaped like those of Claude Code 2.1.292.</summary>
internal static class ClaudeCodeSamples
{
    public const string Version = "2.1.292 (Claude Code)\n";

    public const string SignedIn =
        """{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","analyticsDisabled":false}""";

    public const string NotSignedIn =
        """{"loggedIn":false,"authMethod":"none","apiProvider":"firstParty","analyticsDisabled":false}""";

    public const string Success =
        """{"type":"result","subtype":"success","is_error":false,"duration_ms":1534,"num_turns":1,"result":"Hello from Claude.","stop_reason":"end_turn","session_id":"7d1b","total_cost_usd":0.0012,"usage":{"input_tokens":12,"cache_creation_input_tokens":0,"cache_read_input_tokens":0,"output_tokens":5},"modelUsage":{"claude-haiku-4-5-20251001":{"inputTokens":12,"outputTokens":5}},"permission_denials":[],"api_error_status":null,"terminal_reason":"completed"}""";

    public const string BadModel =
        """{"type":"result","subtype":"success","is_error":true,"num_turns":1,"result":"There's an issue with the selected model (not-a-real-model-xyz). It may not exist or you may not have access to it.","stop_reason":"stop_sequence","api_error_status":404,"terminal_reason":"api_error"}""";

    public static string Failure(string message, int? apiStatus = null) =>
        "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":true,\"result\":"
        + System.Text.Json.JsonSerializer.Serialize(message)
        + ",\"api_error_status\":" + (apiStatus?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null") + "}";

    /// <summary>Answers the version and auth status commands as a signed-in Claude Code would.</summary>
    public static ClaudeCodeRun SignedInMachine(IReadOnlyList<string> arguments, string? input)
    {
        if (arguments.SequenceEqual(ClaudeCodeArguments.Version))
        {
            return new ClaudeCodeRun(0, Version, string.Empty);
        }

        if (arguments.SequenceEqual(ClaudeCodeArguments.AuthStatus))
        {
            return new ClaudeCodeRun(0, SignedIn, string.Empty);
        }

        return new ClaudeCodeRun(0, Success, string.Empty);
    }
}
