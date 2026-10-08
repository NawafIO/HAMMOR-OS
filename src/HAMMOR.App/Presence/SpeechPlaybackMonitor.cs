using HAMMOR.Core.Voice;
using Microsoft.Extensions.Logging;

namespace HAMMOR.App.Presence;

/// <summary>
/// Wraps the platform audio player to observe when HAMMOR's speech is
/// actually playing, so the Living Core's Speaking state follows real
/// playback rather than the synthesis request.
/// </summary>
/// <remarks>
/// Observation only: every call is passed through unchanged, and an observer
/// that throws is logged and can never interrupt speech. Registered in the
/// app's composition root in place of <see cref="IAudioPlayer"/>; the platform
/// layer and Core are not modified.
/// </remarks>
public sealed class SpeechPlaybackMonitor : IAudioPlayer
{
    private readonly IAudioPlayer _inner;
    private readonly ILogger<SpeechPlaybackMonitor> _logger;

    // Number of PlayAsync calls in flight. A new utterance stops the previous
    // one, so two can overlap for a moment; speech is on while any is.
    private int _active;

    public SpeechPlaybackMonitor(IAudioPlayer inner, ILogger<SpeechPlaybackMonitor> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Raised on the playing thread whenever speech starts or stops. Handlers
    /// should read <see cref="IsSpeaking"/> on their own thread rather than
    /// trust the order of notifications.
    /// </summary>
    public event EventHandler<bool>? PlaybackChanged;

    /// <summary>True while any speech playback is in flight.</summary>
    public bool IsSpeaking => Volatile.Read(ref _active) > 0;

    public bool IsPlaying => _inner.IsPlaying;

    public async Task PlayAsync(
        ReadOnlyMemory<byte> audio,
        string contentType,
        string? deviceId = null,
        double volume = 1.0,
        CancellationToken cancellationToken = default)
    {
        if (audio.IsEmpty)
        {
            // Nothing will play; nothing to report.
            await _inner.PlayAsync(audio, contentType, deviceId, volume, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (Interlocked.Increment(ref _active) == 1)
        {
            Notify(true);
        }

        try
        {
            await _inner.PlayAsync(audio, contentType, deviceId, volume, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (Interlocked.Decrement(ref _active) == 0)
            {
                Notify(false);
            }
        }
    }

    public void Stop() => _inner.Stop();

    private void Notify(bool speaking)
    {
        try
        {
            PlaybackChanged?.Invoke(this, speaking);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A speech playback observer failed; playback is unaffected.");
        }
    }
}
