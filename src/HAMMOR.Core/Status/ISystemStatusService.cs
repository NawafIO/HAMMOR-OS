using HAMMOR.Core.Ai;

namespace HAMMOR.Core.Status;

/// <summary>
/// Aggregates the readiness signals the shell displays: Claude, ElevenLabs,
/// microphone, speaker, busy state and background task count.
/// </summary>
/// <remarks>
/// Event-driven by design. The UI subscribes to
/// <see cref="StatusChanged"/> and calls <see cref="RefreshAsync"/> on
/// demand; nothing polls on a timer, which keeps HAMMOR idle-cheap.
/// </remarks>
public interface ISystemStatusService
{
    SystemStatus Current { get; }

    /// <summary>
    /// Re-probes providers and devices, then raises
    /// <see cref="StatusChanged"/>.
    /// </summary>
    Task<SystemStatus> RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Marks HAMMOR busy or idle, e.g. around an agent turn.</summary>
    void SetBusy(bool isBusy);

    event EventHandler<SystemStatus>? StatusChanged;
}

/// <param name="Claude">Readiness of the primary AI provider.</param>
/// <param name="TextToSpeech">Readiness of the configured TTS provider.</param>
/// <param name="SpeechToText">
/// Readiness of STT. NotConfigured in Phase 1 — no provider is implemented.
/// </param>
/// <param name="MicrophoneAvailable">Whether any capture device exists.</param>
/// <param name="SpeakerAvailable">Whether any render device exists.</param>
/// <param name="IsBusy">Whether a turn is currently in flight.</param>
/// <param name="RunningTaskCount">Tasks currently in the Running state.</param>
public sealed record SystemStatus(
    ProviderAvailability Claude,
    ProviderAvailability TextToSpeech,
    ProviderAvailability SpeechToText,
    bool MicrophoneAvailable,
    bool SpeakerAvailable,
    bool IsBusy,
    int RunningTaskCount)
{
    /// <summary>
    /// True when HAMMOR can hold a conversation — the one capability that
    /// must work for the app to be useful.
    /// </summary>
    public bool IsReady => Claude.IsUsable && !IsBusy;

    public static SystemStatus Unknown { get; } = new(
        ProviderAvailability.NotConfigured("Not checked yet."),
        ProviderAvailability.NotConfigured("Not checked yet."),
        ProviderAvailability.NotConfigured("Not implemented."),
        MicrophoneAvailable: false,
        SpeakerAvailable: false,
        IsBusy: false,
        RunningTaskCount: 0);
}
