using System.ComponentModel;
using HAMMOR.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace HAMMOR.Infrastructure.Ai.ClaudeCode;

/// <summary>
/// The user's Claude Code installation and sign-in, as far as HAMMOR needs to
/// know: installed or not, signed in or not, by which method, and whether the
/// plan's usage limit was reached. Never the credential.
/// </summary>
public interface IClaudeCodeAccount
{
    /// <summary>The last known status.</summary>
    ClaudeCodeStatus Current { get; }

    /// <summary>Raised when the status changes, on any thread.</summary>
    event EventHandler<ClaudeCodeStatus>? StatusChanged;

    /// <summary>Checks again now.</summary>
    Task<ClaudeCodeStatus> RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>The last status if it is younger than <paramref name="maxAge"/>, otherwise a fresh check.</summary>
    Task<ClaudeCodeStatus> GetStatusAsync(TimeSpan maxAge, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens Claude Code's own sign-in in its own window and waits for it to
    /// close, then checks again. The sign-in completes through Anthropic's
    /// flow; HAMMOR never sees it.
    /// </summary>
    Task<ClaudeCodeStatus> SignInAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs <c>claude auth logout</c>, then checks again.</summary>
    Task<ClaudeCodeStatus> SignOutAsync(CancellationToken cancellationToken = default);

    /// <summary>Records what a request revealed (a usage limit, a lost sign-in, a success).</summary>
    void Report(ClaudeCodeReply reply);
}

/// <inheritdoc cref="IClaudeCodeAccount"/>
public sealed class ClaudeCodeAccount : IClaudeCodeAccount
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    // Long enough to finish a browser sign-in; after it HAMMOR stops waiting.
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(15);

    private readonly IClaudeCodeCli _cli;
    private readonly ILogger<ClaudeCodeAccount> _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _statusLock = new();

    private ClaudeCodeStatus _current = ClaudeCodeStatus.Unknown;

    // The plan limit message from the last request, until a request succeeds
    // or the sign-in changes. Claude Code only reveals a limit when one is hit.
    private string? _usageLimit;

    public ClaudeCodeAccount(IClaudeCodeCli cli, ILogger<ClaudeCodeAccount> logger, TimeProvider? timeProvider = null)
    {
        _cli = cli ?? throw new ArgumentNullException(nameof(cli));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;
    }

    public event EventHandler<ClaudeCodeStatus>? StatusChanged;

    public ClaudeCodeStatus Current
    {
        get
        {
            lock (_statusLock)
            {
                return _current;
            }
        }
    }

    public async Task<ClaudeCodeStatus> GetStatusAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        var current = Current;
        if (current.CheckedAt != DateTimeOffset.MinValue && _time.GetUtcNow() - current.CheckedAt <= maxAge)
        {
            return current;
        }

        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClaudeCodeStatus> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var status = await ProbeAsync(cancellationToken).ConfigureAwait(false);
            Publish(status);
            return status;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ClaudeCodeStatus> SignInAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var exit = await _cli.RunInteractiveAsync(ClaudeCodeArguments.AuthLogin, SignInTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (exit is null)
            {
                _logger.LogInformation("Claude Code sign-in window still open; stopped waiting.");
            }
            else
            {
                _logger.LogInformation("Claude Code sign-in window closed with code {ExitCode}.", exit);
            }
        }
        catch (ClaudeCodeNotFoundException)
        {
            // Refresh reports it.
        }
        catch (Win32Exception ex)
        {
            _logger.LogWarning("Claude Code sign-in could not be started: {Reason}", SecretRedactor.Redact(ex.Message));
        }

        ClearUsageLimit();
        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClaudeCodeStatus> SignOutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var run = await _cli.RunAsync(ClaudeCodeArguments.AuthLogout, null, CommandTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (run.ExitCode != 0)
            {
                _logger.LogWarning("Claude Code sign-out exited with code {ExitCode}.", run.ExitCode);
            }
        }
        catch (ClaudeCodeNotFoundException)
        {
            // Refresh reports it.
        }
        catch (Win32Exception ex)
        {
            _logger.LogWarning("Claude Code sign-out could not be started: {Reason}", SecretRedactor.Redact(ex.Message));
        }

        ClearUsageLimit();
        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Report(ClaudeCodeReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        var current = Current;
        var now = _time.GetUtcNow();
        switch (reply.Failure)
        {
            case ClaudeCodeFailure.None:
                ClearUsageLimit();
                if (current.State == ClaudeCodeState.UsageLimited)
                {
                    Publish(current with { State = ClaudeCodeState.SignedIn, Detail = null, CheckedAt = now });
                }

                break;

            case ClaudeCodeFailure.UsageLimited:
                lock (_statusLock)
                {
                    _usageLimit = reply.Text;
                }

                Publish(current with { State = ClaudeCodeState.UsageLimited, Detail = reply.Text, CheckedAt = now });
                break;

            case ClaudeCodeFailure.NotSignedIn:
                ClearUsageLimit();
                Publish(current with { State = ClaudeCodeState.NotSignedIn, AuthMethod = null, Detail = reply.Text, CheckedAt = now });
                break;

            case ClaudeCodeFailure.NeedsUpdate:
                Publish(current with { State = ClaudeCodeState.NeedsUpdate, Detail = reply.Text, CheckedAt = now });
                break;

            // Timeouts, oversized answers and other errors are about one
            // request, not the account; the next check decides.
        }
    }

    private async Task<ClaudeCodeStatus> ProbeAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var location = _cli.Locate();
        if (location.ExecutablePath is null)
        {
            return location.ScriptLauncherPath is null
                ? new ClaudeCodeStatus(ClaudeCodeState.NotInstalled, null, null, null, now)
                : new ClaudeCodeStatus(ClaudeCodeState.UnsupportedInstall, null, null, location.ScriptLauncherPath, now);
        }

        try
        {
            var versionRun = await _cli.RunAsync(ClaudeCodeArguments.Version, null, CommandTimeout, cancellationToken)
                .ConfigureAwait(false);
            var version = ClaudeCodeOutput.ParseVersion(versionRun.StandardOutput);

            var authRun = await _cli.RunAsync(ClaudeCodeArguments.AuthStatus, null, CommandTimeout, cancellationToken)
                .ConfigureAwait(false);
            var auth = ClaudeCodeOutput.ParseAuthStatus(authRun);

            if (auth.NeedsUpdate)
            {
                return new ClaudeCodeStatus(ClaudeCodeState.NeedsUpdate, version, null, auth.Problem, now);
            }

            if (auth.LoggedIn is null)
            {
                return new ClaudeCodeStatus(ClaudeCodeState.Error, version, null, auth.Problem, now);
            }

            if (auth.LoggedIn is false)
            {
                ClearUsageLimit();
                return new ClaudeCodeStatus(ClaudeCodeState.NotSignedIn, version, null, null, now);
            }

            string? usageLimit;
            lock (_statusLock)
            {
                usageLimit = _usageLimit;
            }

            return usageLimit is null
                ? new ClaudeCodeStatus(ClaudeCodeState.SignedIn, version, auth.AuthMethod, null, now)
                : new ClaudeCodeStatus(ClaudeCodeState.UsageLimited, version, auth.AuthMethod, usageLimit, now);
        }
        catch (ClaudeCodeNotFoundException ex)
        {
            return ex.Location.ScriptLauncherPath is null
                ? new ClaudeCodeStatus(ClaudeCodeState.NotInstalled, null, null, null, now)
                : new ClaudeCodeStatus(ClaudeCodeState.UnsupportedInstall, null, null, ex.Location.ScriptLauncherPath, now);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            _logger.LogWarning("Claude Code could not be checked: {Reason}", SecretRedactor.Redact(ex.Message));
            return new ClaudeCodeStatus(ClaudeCodeState.Error, null, null, SecretRedactor.Redact(ex.Message), now);
        }
    }

    private void ClearUsageLimit()
    {
        lock (_statusLock)
        {
            _usageLimit = null;
        }
    }

    private void Publish(ClaudeCodeStatus status)
    {
        bool changed;
        lock (_statusLock)
        {
            changed = !SameState(_current, status);
            _current = status;
        }

        if (changed)
        {
            StatusChanged?.Invoke(this, status);
        }
    }

    private static bool SameState(ClaudeCodeStatus a, ClaudeCodeStatus b) =>
        a.State == b.State && a.Version == b.Version && a.AuthMethod == b.AuthMethod && a.Detail == b.Detail;
}
