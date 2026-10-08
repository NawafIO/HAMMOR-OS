namespace HAMMOR.Infrastructure.Ai.ClaudeCode;

/// <summary>
/// The complete set of Claude Code command lines HAMMOR runs. Fixed lists,
/// passed one argument per element (never through a shell), with no way for
/// user input to add a flag.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is documented Claude Code behaviour (checked against
/// Claude Code 2.1.292). Requests run in print mode with JSON output, the
/// prompt on standard input, no built-in tools, no session saved to disk,
/// customizations off (hooks, plugins, MCP servers, CLAUDE.md) and only
/// MCP servers from the command line (none). <c>--bare</c> is deliberately
/// not used: it turns off sign-in with a Claude account.
/// </para>
/// <para>
/// Nothing here removes, disables or restricts a Claude Code sign-in method,
/// and HAMMOR does not change the environment Claude Code runs in. Never
/// <c>--dangerously-skip-permissions</c>; with no tools there is nothing to
/// permit.
/// </para>
/// </remarks>
public static class ClaudeCodeArguments
{
    /// <summary><c>claude --version</c></summary>
    public static IReadOnlyList<string> Version { get; } = ["--version"];

    /// <summary><c>claude auth status --json</c>: whether, and how, Claude Code is signed in.</summary>
    public static IReadOnlyList<string> AuthStatus { get; } = ["auth", "status", "--json"];

    /// <summary><c>claude auth login --claudeai</c>: Claude Code's own sign-in, with a Claude account.</summary>
    public static IReadOnlyList<string> AuthLogin { get; } = ["auth", "login", "--claudeai"];

    /// <summary><c>claude auth logout</c></summary>
    public static IReadOnlyList<string> AuthLogout { get; } = ["auth", "logout"];

    /// <summary>
    /// One request: <c>claude -p --output-format json --tools "" ...</c> with
    /// the prompt on standard input.
    /// </summary>
    /// <param name="systemPrompt">
    /// Passed as a single <c>--system-prompt=</c> argument, so its text can
    /// never be read as another option.
    /// </param>
    public static IReadOnlyList<string> Request(string systemPrompt)
    {
        ArgumentNullException.ThrowIfNull(systemPrompt);

        return
        [
            "-p",
            "--output-format",
            "json",
            "--tools",
            "",
            "--no-session-persistence",
            "--safe-mode",
            "--strict-mcp-config",
            "--system-prompt=" + systemPrompt,
        ];
    }
}
