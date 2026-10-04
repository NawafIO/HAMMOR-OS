using HAMMOR.Core.Ai;

namespace HAMMOR.Core.Voice;

/// <summary>
/// Transcribes speech to text. Phase 1 defines the contract and ships only a
/// provider that reports itself unavailable — no recognition is implemented
/// yet.
/// </summary>
public interface ISpeechToTextProvider
{
    /// <summary>Stable id matched against <c>VoiceSettings.SttProvider</c>.</summary>
    string ProviderId { get; }

    string DisplayName { get; }

    /// <summary>
    /// Reports whether transcription is usable. The Phase 1 provider always
    /// reports <see cref="ProviderState.NotConfigured"/>.
    /// </summary>
    Task<ProviderAvailability> CheckAvailabilityAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Transcribes recorded audio.</summary>
    /// <exception cref="NotSupportedException">
    /// Thrown by the Phase 1 placeholder provider. Callers must check
    /// <see cref="CheckAvailabilityAsync"/> first and disable voice input in
    /// the UI rather than catching this.
    /// </exception>
    Task<TranscriptionResult> TranscribeAsync(
        TranscriptionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record TranscriptionRequest
{
    public required ReadOnlyMemory<byte> Audio { get; init; }

    /// <summary>MIME type of <see cref="Audio"/>, e.g. <c>audio/wav</c>.</summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// Preferred language, e.g. <c>ar</c>. Null asks the provider to detect,
    /// which matters for mixed Arabic/English speech.
    /// </summary>
    public string? Language { get; init; }
}

/// <param name="Text">Recognised text.</param>
/// <param name="DetectedLanguage">Language the provider detected, when reported.</param>
/// <param name="Confidence">Confidence 0..1, when reported.</param>
public sealed record TranscriptionResult(
    string Text,
    string? DetectedLanguage,
    double? Confidence);
