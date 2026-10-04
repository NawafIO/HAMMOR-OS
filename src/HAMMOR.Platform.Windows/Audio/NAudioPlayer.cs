using System.Runtime.Versioning;
using HAMMOR.Core.Voice;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace HAMMOR.Platform.Windows.Audio;

/// <summary>
/// Plays encoded audio (ElevenLabs returns MP3) through WASAPI.
/// </summary>
/// <remarks>
/// Decodes from an in-memory stream because the TTS provider buffers the whole
/// response — there is no streaming path to honour here. Playback is
/// serialised: a second call stops the first rather than overlapping, which is
/// what a single assistant voice should do.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class NAudioPlayer(ILogger<NAudioPlayer> logger) : IAudioPlayer, IDisposable
{
    private readonly ILogger<NAudioPlayer> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly SemaphoreSlim _playbackGate = new(1, 1);

    private CancellationTokenSource? _currentPlayback;

    public bool IsPlaying { get; private set; }

    public async Task PlayAsync(
        ReadOnlyMemory<byte> audio,
        string contentType,
        string? deviceId = null,
        double volume = 1.0,
        CancellationToken cancellationToken = default)
    {
        if (audio.IsEmpty)
        {
            return;
        }

        // Stop whatever is playing before queueing behind the gate, so a new
        // utterance interrupts rather than waiting for the old one.
        Stop();

        await _playbackGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _currentPlayback = linked;

        try
        {
            IsPlaying = true;
            await Task.Run(
                () => PlayBlocking(audio, contentType, deviceId, volume, linked.Token),
                linked.Token).ConfigureAwait(false);
        }
        finally
        {
            IsPlaying = false;
            _currentPlayback = null;
            _playbackGate.Release();
        }
    }

    public void Stop()
    {
        try
        {
            _currentPlayback?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Playback already finished and disposed its token source; nothing
            // to stop. Not an error worth surfacing.
        }
    }

    private void PlayBlocking(
        ReadOnlyMemory<byte> audio,
        string contentType,
        string? deviceId,
        double volume,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(audio.ToArray(), writable: false);
        using var reader = CreateReader(stream, contentType);
        using var device = CreateOutputDevice(deviceId);

        device.Init(reader);
        device.Volume = (float)Math.Clamp(volume, 0.0, 1.0);
        device.Play();

        // Poll at 50 ms. This loop only runs while audio is actually playing,
        // so it is not a background poll — HAMMOR stays idle-cheap when silent.
        while (device.PlaybackState == PlaybackState.Playing)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                device.Stop();
                break;
            }

            Thread.Sleep(50);
        }
    }

    /// <summary>
    /// Builds a decoder for the response's content type. MP3 is the expected
    /// case; WAV is supported because an alternative TTS provider may return it.
    /// </summary>
    private static WaveStream CreateReader(Stream stream, string contentType) =>
        contentType switch
        {
            "audio/mpeg" or "audio/mp3" => new Mp3FileReader(stream),
            "audio/wav" or "audio/x-wav" or "audio/wave" => new WaveFileReader(stream),

            // StreamMediaFoundationReader handles whatever else Media
            // Foundation can decode (e.g. audio/ogg, audio/mp4) and throws a
            // clear exception when it cannot, rather than playing silence.
            _ => new StreamMediaFoundationReader(stream),
        };

    private WasapiOut CreateOutputDevice(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return new WasapiOut(AudioClientShareMode.Shared, latency: 100);
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(deviceId);

            return new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 100);
        }
        catch (Exception ex)
        {
            // The configured device was unplugged or renamed. Falling back to
            // the system default keeps speech working, but the substitution is
            // logged so it is not invisible.
            _logger.LogWarning(
                ex,
                "Configured output device '{DeviceId}' is unavailable; using the system default.",
                deviceId);

            return new WasapiOut(AudioClientShareMode.Shared, latency: 100);
        }
    }

    public void Dispose()
    {
        Stop();
        _playbackGate.Dispose();
    }
}
