using HAMMOR.Core.Audit;
using HAMMOR.Core.Configuration;
using HAMMOR.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace HAMMOR.Core.Voice;

/// <summary>
/// Owns the speak half of the voice pipeline: resolve settings → synthesise →
/// play. Lives in Core so the UI never talks to a TTS provider directly.
/// </summary>
/// <remarks>
/// The input half (microphone → STT) is deliberately absent: no STT provider
/// is implemented in Phase 1, and a half-wired capture path would imply voice
/// input works.
/// </remarks>
public interface IVoiceOrchestrator
{
    /// <summary>
    /// Whether speaking is currently possible — a configured TTS provider and
    /// an available output device.
    /// </summary>
    Task<bool> CanSpeakAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Speaks <paramref name="text"/>. Returns false without throwing when
    /// TTS is not configured, so callers can treat voice as optional.
    /// </summary>
    Task<bool> SpeakAsync(
        string text,
        string? language = null,
        CancellationToken cancellationToken = default);

    /// <summary>Stops any in-flight playback.</summary>
    void StopSpeaking();
}

/// <inheritdoc cref="IVoiceOrchestrator"/>
public sealed class VoiceOrchestrator(
    IEnumerable<ITextToSpeechProvider> ttsProviders,
    IAudioPlayer audioPlayer,
    IConfigurationStore configurationStore,
    IAuditLog auditLog,
    ILogger<VoiceOrchestrator> logger) : IVoiceOrchestrator
{
    private readonly IReadOnlyList<ITextToSpeechProvider> _ttsProviders =
        ttsProviders?.ToList() ?? throw new ArgumentNullException(nameof(ttsProviders));

    private readonly IAudioPlayer _audioPlayer =
        audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));

    private readonly IConfigurationStore _configurationStore =
        configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));

    private readonly IAuditLog _auditLog =
        auditLog ?? throw new ArgumentNullException(nameof(auditLog));

    private readonly ILogger<VoiceOrchestrator> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<bool> CanSpeakAsync(CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider();
        if (provider is null)
        {
            return false;
        }

        var availability = await provider.CheckAvailabilityAsync(cancellationToken)
            .ConfigureAwait(false);

        return availability.IsUsable;
    }

    public async Task<bool> SpeakAsync(
        string text,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var provider = ResolveProvider();
        if (provider is null)
        {
            _logger.LogInformation(
                "Speech skipped: TTS provider '{ProviderId}' is not registered.",
                _configurationStore.Current.Voice.TtsProvider);
            return false;
        }

        var voice = _configurationStore.Current.Voice;

        try
        {
            var result = await provider.SynthesiseAsync(
                new SynthesisRequest { Text = text, Language = language },
                cancellationToken).ConfigureAwait(false);

            await _audioPlayer.PlayAsync(
                result.Audio,
                result.ContentType,
                voice.OutputDeviceId,
                voice.OutputVolume,
                cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled or StopSpeaking was invoked: not a failure.
            throw;
        }
        catch (VoiceProviderException ex)
        {
            // Expected failure mode (not configured, rejected key, HTTP error).
            // Logged and audited, never swallowed silently.
            _logger.LogWarning(ex, "Speech synthesis failed via {Provider}.", provider.ProviderId);

            await _auditLog.AppendAsync(
                new AuditEntry
                {
                    Category = AuditCategory.Provider,
                    Subject = provider.ProviderId,
                    Message = SecretRedactor.Redact($"Speech synthesis failed: {ex.Message}"),
                    Outcome = AuditOutcome.Failed,
                },
                cancellationToken).ConfigureAwait(false);

            return false;
        }
    }

    public void StopSpeaking() => _audioPlayer.Stop();

    /// <summary>
    /// Finds the configured provider. Returns null rather than falling back to
    /// an arbitrary provider: silently speaking through a different backend
    /// than the one configured would be surprising.
    /// </summary>
    private ITextToSpeechProvider? ResolveProvider()
    {
        var configuredId = _configurationStore.Current.Voice.TtsProvider;

        return _ttsProviders.FirstOrDefault(
            p => string.Equals(p.ProviderId, configuredId, StringComparison.OrdinalIgnoreCase));
    }
}
