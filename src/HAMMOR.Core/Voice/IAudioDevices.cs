namespace HAMMOR.Core.Voice;

/// <summary>
/// Enumerates audio hardware. Implemented in the Windows platform layer via
/// NAudio; Core stays portable.
/// </summary>
public interface IAudioDeviceProvider
{
    /// <summary>Capture devices. Empty when no microphone is present.</summary>
    IReadOnlyList<AudioDevice> GetInputDevices();

    /// <summary>Render devices. Empty when no output is present.</summary>
    IReadOnlyList<AudioDevice> GetOutputDevices();
}

/// <param name="Id">Stable identifier persisted in configuration.</param>
/// <param name="Name">Friendly name for the UI.</param>
/// <param name="IsDefault">Whether this is the system default device.</param>
public sealed record AudioDevice(string Id, string Name, bool IsDefault);

/// <summary>Plays synthesised audio. Implemented in the platform layer.</summary>
public interface IAudioPlayer
{
    /// <summary>True while audio is playing.</summary>
    bool IsPlaying { get; }

    /// <summary>
    /// Plays encoded audio to completion, honouring cancellation.
    /// </summary>
    /// <param name="audio">Encoded audio, e.g. MP3 from ElevenLabs.</param>
    /// <param name="contentType">MIME type of <paramref name="audio"/>.</param>
    /// <param name="deviceId">Output device, or null for the system default.</param>
    /// <param name="volume">Volume 0.0 to 1.0.</param>
    Task PlayAsync(
        ReadOnlyMemory<byte> audio,
        string contentType,
        string? deviceId = null,
        double volume = 1.0,
        CancellationToken cancellationToken = default);

    /// <summary>Stops playback immediately. A no-op when idle.</summary>
    void Stop();
}
