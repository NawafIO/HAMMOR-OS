using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HAMMOR.Infrastructure.Ai.ClaudeCode;

/// <summary>Runs the user's Claude Code executable.</summary>
public interface IClaudeCodeCli
{
    /// <summary>Looks for Claude Code now (it can be installed while HAMMOR runs).</summary>
    ClaudeCodeLocation Locate();

    /// <summary>
    /// Runs Claude Code hidden with <paramref name="arguments"/>, writes
    /// <paramref name="standardInput"/> to it, and collects its output.
    /// </summary>
    /// <exception cref="ClaudeCodeNotFoundException">No Claude Code executable was found.</exception>
    /// <exception cref="Win32Exception">The executable could not be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<ClaudeCodeRun> RunAsync(
        IReadOnlyList<string> arguments,
        string? standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs Claude Code in its own visible console window, for its own
    /// interactive flows (sign-in). HAMMOR reads nothing from it.
    /// </summary>
    /// <returns>The exit code, or null when it was still running after <paramref name="timeout"/>.</returns>
    /// <exception cref="ClaudeCodeNotFoundException">No Claude Code executable was found.</exception>
    /// <exception cref="Win32Exception">The executable could not be started.</exception>
    Task<int?> RunInteractiveAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// <summary>Raised when Claude Code is not installed (or only as an unsupported launcher).</summary>
public sealed class ClaudeCodeNotFoundException(ClaudeCodeLocation location)
    : Exception("Claude Code is not installed.")
{
    public ClaudeCodeLocation Location { get; } = location;
}

/// <summary>
/// Starts Claude Code directly: the executable itself, never <c>cmd.exe</c>,
/// PowerShell or a shell; arguments one per element; a HAMMOR-owned, empty
/// working directory; bounded time and output; and the whole process tree
/// stopped on timeout or cancellation.
/// </summary>
/// <remarks>
/// The environment is inherited unchanged: Claude Code chooses its own
/// sign-in method exactly as it would in the user's terminal. Prompts and
/// answers are never logged.
/// </remarks>
public sealed class ClaudeCodeCli : IClaudeCodeCli
{
    /// <summary>Most characters read from standard output before the run is stopped.</summary>
    public const int MaxOutputChars = 4_000_000;

    /// <summary>Most characters read from standard error.</summary>
    public const int MaxErrorChars = 64_000;

    private readonly IClaudeCodeHost _host;
    private readonly string _workingDirectory;
    private readonly ILogger<ClaudeCodeCli> _logger;

    public ClaudeCodeCli(IClaudeCodeHost host, string workingDirectory, ILogger<ClaudeCodeCli> logger)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Path.IsPathFullyQualified(workingDirectory))
        {
            throw new ArgumentException("The working directory must be a full path.", nameof(workingDirectory));
        }

        _workingDirectory = workingDirectory;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ClaudeCodeLocation Locate() => ClaudeCodeLocator.Locate(_host);

    public async Task<ClaudeCodeRun> RunAsync(
        IReadOnlyList<string> arguments,
        string? standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var executable = RequireExecutable();
        var startInfo = CreateStartInfo(executable, arguments);
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;
        startInfo.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(timeout);

        // On timeout or cancellation the process tree is stopped at once: that
        // closes its pipes, so pending reads end even if a pipe read does not
        // observe cancellation itself.
        using var stopOnCancel = lifetime.Token.Register(() => StopTree(process));

        // Output that outgrows its bound ends the run instead of growing memory.
        var output = new BoundedText(MaxOutputChars);
        var errors = new BoundedText(MaxErrorChars);
        var readOutput = output.ReadAsync(process.StandardOutput, () => StopTree(process), lifetime.Token);
        var readErrors = errors.ReadAsync(process.StandardError, () => StopTree(process), lifetime.Token);

        try
        {
            try
            {
                if (standardInput is not null)
                {
                    await process.StandardInput.WriteAsync(standardInput.AsMemory(), lifetime.Token).ConfigureAwait(false);
                }

                process.StandardInput.Close();
            }
            catch (IOException) when (process.HasExited)
            {
                // It exited before reading everything (for example an unknown
                // option); its output explains why.
            }

            await Task.WhenAll(readOutput, readErrors).ConfigureAwait(false);
            await process.WaitForExitAsync(lifetime.Token).ConfigureAwait(false);

            // A run stopped at its deadline is a timeout, whatever exit code
            // the stopped process left behind.
            lifetime.Token.ThrowIfCancellationRequested();

            var limited = output.LimitReached || errors.LimitReached;
            return new ClaudeCodeRun(limited ? -1 : process.ExitCode, output.Text, errors.Text, OutputLimitReached: limited);
        }
        catch (OperationCanceledException)
        {
            StopTree(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            _logger.LogWarning("Claude Code did not finish within {Timeout}.", timeout);
            return new ClaudeCodeRun(-1, output.Text, errors.Text, TimedOut: true);
        }
    }

    public async Task<int?> RunInteractiveAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var executable = RequireExecutable();

        // No redirection and a window: Claude Code gets its own console, and
        // its sign-in happens there and in the browser it opens, entirely
        // through Anthropic's own flow.
        var startInfo = CreateStartInfo(executable, arguments);
        startInfo.CreateNoWindow = false;

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(lifetime.Token).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Still open after the timeout: stop waiting, leave the user's window alone.
            return null;
        }
    }

    private string RequireExecutable()
    {
        var location = Locate();
        return location.ExecutablePath ?? throw new ClaudeCodeNotFoundException(location);
    }

    private ProcessStartInfo CreateStartInfo(string executable, IReadOnlyList<string> arguments)
    {
        Directory.CreateDirectory(_workingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = _workingDirectory,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void StopTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        catch (Win32Exception)
        {
            // Exiting at the same moment; nothing left to stop.
        }
    }

    /// <summary>Reads a stream up to a character bound.</summary>
    private sealed class BoundedText(int maxChars)
    {
        private readonly StringBuilder _text = new();

        public bool LimitReached { get; private set; }

        public string Text => _text.ToString();

        public async Task ReadAsync(StreamReader reader, Action onLimit, CancellationToken cancellationToken)
        {
            var buffer = new char[8192];
            while (true)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                var room = maxChars - _text.Length;
                if (read > room)
                {
                    _text.Append(buffer, 0, Math.Max(0, room));
                    LimitReached = true;
                    onLimit();
                    return;
                }

                _text.Append(buffer, 0, read);
            }
        }
    }
}
