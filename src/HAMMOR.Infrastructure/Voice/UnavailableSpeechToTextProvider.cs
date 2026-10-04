using HAMMOR.Core.Ai;
using HAMMOR.Core.Voice;

namespace HAMMOR.Infrastructure.Voice;

/// <summary>
/// Placeholder STT provider. NOT IMPLEMENTED — speech recognition does not
/// work in Phase 1.
/// </summary>
/// <remarks>
/// It exists so the <see cref="ISpeechToTextProvider"/> abstraction is
/// exercised and the UI has something concrete to report as unavailable. It
/// deliberately does not fall back to any recogniser: a half-working
/// transcription path would be worse than an explicit "Not Configured",
/// because the user could not tell whether a wrong transcript was a
/// recognition error or an unimplemented feature.
/// </remarks>
public sealed class UnavailableSpeechToTextProvider : ISpeechToTextProvider
{
    public const string Id = "none";

    public string ProviderId => Id;

    public string DisplayName => "Not configured";

    public Task<ProviderAvailability> CheckAvailabilityAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ProviderAvailability.NotConfigured(
            "Speech-to-text is not implemented yet. Voice input is unavailable; "
            + "typed input and spoken output both work."));

    public Task<TranscriptionResult> TranscribeAsync(
        TranscriptionRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Speech-to-text is not implemented. Check CheckAvailabilityAsync before "
            + "offering voice input.");
}
