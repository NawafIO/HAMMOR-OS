using HAMMOR.Core.Ai;

namespace HAMMOR.Core.Voice;

/// <summary>
/// Synthesises speech from text. ElevenLabs is the initial implementation;
/// the voice orchestrator only knows this interface.
/// </summary>
public interface ITextToSpeechProvider
{
    /// <summary>Stable id matched against <c>VoiceSettings.TtsProvider</c>.</summary>
    string ProviderId { get; }

    string DisplayName { get; }

    /// <summary>
    /// Whether this provider can stream audio incrementally. Phase 1's
    /// ElevenLabs provider returns false: it buffers the whole response, and
    /// claiming otherwise would misrepresent the pipeline.
    /// </summary>
    bool SupportsStreaming { get; }

    /// <summary>
    /// Reports whether credentials and settings are present. Must not throw.
    /// </summary>
    Task<ProviderAvailability> CheckAvailabilityAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Synthesises <paramref name="request"/> into audio bytes.</summary>
    /// <exception cref="VoiceProviderException">
    /// The provider is not configured, or the request failed.
    /// </exception>
    Task<SynthesisResult> SynthesiseAsync(
        SynthesisRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>A request to speak some text.</summary>
public sealed record SynthesisRequest
{
    public required string Text { get; init; }

    /// <summary>
    /// BCP-47 language hint, e.g. <c>ar</c> or <c>en</c>. Lets a multilingual
    /// voice model pick the right pronunciation for mixed-script input.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Voice id override. Null means "use the configured voice id", which is
    /// the normal path — callers must not embed a voice id themselves.
    /// </summary>
    public string? VoiceIdOverride { get; init; }
}

/// <param name="Audio">Encoded audio bytes.</param>
/// <param name="ContentType">MIME type, e.g. <c>audio/mpeg</c>.</param>
/// <param name="VoiceId">Voice that was actually used, for the activity log.</param>
public sealed record SynthesisResult(
    ReadOnlyMemory<byte> Audio,
    string ContentType,
    string VoiceId);

public sealed class VoiceProviderException : Exception
{
    public VoiceProviderException(string message) : base(message)
    {
    }

    public VoiceProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
