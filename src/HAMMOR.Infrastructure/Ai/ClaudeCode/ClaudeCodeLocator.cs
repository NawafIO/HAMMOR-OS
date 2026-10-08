using System.Runtime.InteropServices;

namespace HAMMOR.Infrastructure.Ai.ClaudeCode;

/// <summary>Where Claude Code was found.</summary>
/// <param name="ExecutablePath">Full path of the Claude Code executable, or null.</param>
/// <param name="ScriptLauncherPath">
/// A script launcher found instead (the npm <c>claude.cmd</c> or
/// <c>claude.ps1</c>), reported so the UI can explain why it is not used.
/// </param>
public sealed record ClaudeCodeLocation(string? ExecutablePath, string? ScriptLauncherPath)
{
    public static ClaudeCodeLocation None { get; } = new(null, null);

    public bool Found => ExecutablePath is not null;
}

/// <summary>The few host facts the locator reads; replaceable in tests.</summary>
public interface IClaudeCodeHost
{
    bool IsWindows { get; }

    string? GetEnvironmentVariable(string name);

    bool FileExists(string path);
}

/// <summary>The real machine.</summary>
public sealed class SystemClaudeCodeHost : IClaudeCodeHost
{
    public static SystemClaudeCodeHost Instance { get; } = new();

    public bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public bool FileExists(string path) => File.Exists(path);
}

/// <summary>
/// Finds the user's own Claude Code executable: the native installer's
/// location first, then the PATH. Only a real executable is accepted.
/// </summary>
/// <remarks>
/// On Windows that means <c>claude.exe</c>. The npm launcher
/// (<c>claude.cmd</c>) runs through <c>cmd.exe</c>, which re-parses its
/// arguments, so it is reported but never started. PATH entries that are not
/// fully qualified (such as <c>.</c>) are skipped, so the current directory
/// can never supply the executable.
/// </remarks>
public static class ClaudeCodeLocator
{
    private static readonly string[] WindowsScriptLaunchers = ["claude.cmd", "claude.ps1", "claude.bat"];

    public static ClaudeCodeLocation Locate(IClaudeCodeHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var executableName = host.IsWindows ? "claude.exe" : "claude";

        // The native installer's documented location.
        var home = host.GetEnvironmentVariable(host.IsWindows ? "USERPROFILE" : "HOME");
        if (IsFullyQualified(home))
        {
            var native = Path.Combine(home!, ".local", "bin", executableName);
            if (host.FileExists(native))
            {
                return new ClaudeCodeLocation(native, null);
            }
        }

        // The PATH, which also carries WinGet's link to the executable.
        string? scriptLauncher = null;
        foreach (var directory in PathEntries(host))
        {
            var candidate = Path.Combine(directory, executableName);
            if (host.FileExists(candidate))
            {
                return new ClaudeCodeLocation(candidate, null);
            }

            if (host.IsWindows && scriptLauncher is null)
            {
                foreach (var launcher in WindowsScriptLaunchers)
                {
                    var path = Path.Combine(directory, launcher);
                    if (host.FileExists(path))
                    {
                        scriptLauncher = path;
                        break;
                    }
                }
            }
        }

        return new ClaudeCodeLocation(null, scriptLauncher);
    }

    private static IEnumerable<string> PathEntries(IClaudeCodeHost host)
    {
        var path = host.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        var separator = host.IsWindows ? ';' : ':';
        foreach (var raw in path.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var entry = raw.Trim('"');
            if (IsFullyQualified(entry) && entry.IndexOfAny(Path.GetInvalidPathChars()) < 0)
            {
                yield return entry;
            }
        }
    }

    private static bool IsFullyQualified(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path);
}
