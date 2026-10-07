namespace HAMMOR.Infrastructure.Ai.ClaudeCode;

/// <summary>Where HAMMOR stands with the user's own Claude Code installation.</summary>
public enum ClaudeCodeState
{
    /// <summary>No Claude Code executable was found.</summary>
    NotInstalled = 0,

    /// <summary>
    /// Only a script launcher (the npm <c>claude.cmd</c> or <c>claude.ps1</c>)
    /// was found. HAMMOR starts only a real executable, never a script through
    /// a shell.
    /// </summary>
    UnsupportedInstall = 1,

    /// <summary>Installed; Claude Code reports it is not signed in.</summary>
    NotSignedIn = 2,

    /// <summary>Installed and signed in.</summary>
    SignedIn = 3,

    /// <summary>Signed in, and the last request reached a plan usage limit.</summary>
    UsageLimited = 4,

    /// <summary>Claude Code rejected an option or command HAMMOR uses.</summary>
    NeedsUpdate = 5,

    /// <summary>Claude Code could not be run or answered with an error.</summary>
    Error = 6,
}

/// <summary>
/// A snapshot of the Claude Code integration. Never carries a credential:
/// HAMMOR only learns whether Claude Code is signed in and by which method.
/// </summary>
/// <param name="State">Overall state.</param>
/// <param name="Version">Claude Code version, when known (for example <c>2.1.292</c>).</param>
/// <param name="AuthMethod">
/// Claude Code's own name for its sign-in method (<c>claude.ai</c>,
/// <c>oauth_token</c>, <c>api_key</c>, <c>api_key_helper</c>,
/// <c>third_party</c>), when signed in.
/// </param>
/// <param name="Detail">
/// Short explanation for the UI, already redacted: Claude Code's own message
/// for a usage limit or an error, or the path of an unsupported launcher.
/// </param>
/// <param name="CheckedAt">When this snapshot was taken.</param>
public sealed record ClaudeCodeStatus(
    ClaudeCodeState State,
    string? Version,
    string? AuthMethod,
    string? Detail,
    DateTimeOffset CheckedAt)
{
    /// <summary>A snapshot that has never been checked.</summary>
    public static ClaudeCodeStatus Unknown { get; } =
        new(ClaudeCodeState.NotInstalled, null, null, null, DateTimeOffset.MinValue);

    /// <summary>True when a request can be sent.</summary>
    public bool CanSend => State is ClaudeCodeState.SignedIn or ClaudeCodeState.UsageLimited;

    /// <summary>True when Claude Code's executable was found.</summary>
    public bool IsInstalled => State is not (ClaudeCodeState.NotInstalled or ClaudeCodeState.UnsupportedInstall);
}
