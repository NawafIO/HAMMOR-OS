using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using HAMMOR.Core.Ai;
using HAMMOR.Core.Configuration;
using HAMMOR.Core.Diagnostics;
using HAMMOR.Core.Security;
using HAMMOR.Core.Voice;
using Microsoft.Extensions.Logging;

namespace HAMMOR.Infrastructure.Voice;

/// <summary>
/// ElevenLabs text-to-speech over the public REST API.
/// </summary>
/// <remarks>
/// <para>
/// The voice id is always read from
/// <see cref="ElevenLabsSettings.VoiceId"/> — this class contains no voice id
/// literal, so changing the voice is a settings edit, not a code change.
/// </para>
/// <para>
/// <see cref="SupportsStreaming"/> is false: this implementation buffers the
/// full response before playback. ElevenLabs does offer a streaming endpoint,
/// but it is not wired up here and reporting otherwise would misstate what the
/// pipeline does.
/// </para>
/// </remarks>
public sealed class ElevenLabsTextToSpeechProvider(
    IHttpClientFactory httpClientFactory,
    ISecretStore secretStore,
    IConfigurationStore configurationStore,
    ILogger<ElevenLabsTextToSpeechProvider> logger) : ITextToSpeechProvider
{
    /// <summary>Id matched against <see cref="VoiceSettings.TtsProvider"/>.</summary>
    public const string Id = "elevenlabs";

    /// <summary>Named <see cref="HttpClient"/> registered for this provider.</summary>
    public const string HttpClientName = "elevenlabs";

    internal const string BaseAddress = "https://api.elevenlabs.io/";

    private readonly IHttpClientFactory _httpClientFactory =
        httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

    private readonly ISecretStore _secretStore =
        secretStore ?? throw new ArgumentNullException(nameof(secretStore));

    private readonly IConfigurationStore _configurationStore =
        configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));

    private readonly ILogger<ElevenLabsTextToSpeechProvider> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public string ProviderId => Id;

    public string DisplayName => "ElevenLabs";

    public bool SupportsStreaming => false;

    public async Task<ProviderAvailability> CheckAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        var apiKey = await _secretStore
            .GetAsync(SecretNames.ElevenLabsApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return ProviderAvailability.NotConfigured(
                "No ElevenLabs API key is stored. Add one in Settings.");
        }

        var voiceId = _configurationStore.Current.Voice.ElevenLabs.VoiceId;
        if (string.IsNullOrWhiteSpace(voiceId))
        {
            return ProviderAvailability.NotConfigured("No ElevenLabs voice id is configured.");
        }

        try
        {
            using var client = CreateClient(apiKey);
            using var response = await client
                .GetAsync("v1/user/subscription", cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return ProviderAvailability.Error("The stored ElevenLabs API key was rejected.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return ProviderAvailability.Error(
                    $"ElevenLabs returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            return ProviderAvailability.Ready($"Connected. Voice id {voiceId}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "ElevenLabs availability probe failed.");
            return ProviderAvailability.Error(
                SecretRedactor.Redact($"Could not reach ElevenLabs: {ex.Message}"));
        }
    }

    public async Task<SynthesisResult> SynthesiseAsync(
        SynthesisRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new VoiceProviderException("Cannot synthesise empty text.");
        }

        var apiKey = await _secretStore
            .GetAsync(SecretNames.ElevenLabsApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new VoiceProviderException(
                "ElevenLabs is not configured: no API key is stored.");
        }

        var settings = _configurationStore.Current.Voice.ElevenLabs;
        var voiceId = request.VoiceIdOverride ?? settings.VoiceId;

        if (string.IsNullOrWhiteSpace(voiceId))
        {
            throw new VoiceProviderException("No ElevenLabs voice id is configured.");
        }

        var payload = new SynthesisPayload(
            request.Text,
            settings.ModelId,
            new VoiceSettingsPayload(settings.Stability, settings.SimilarityBoost),
            // Passing the UI language as a hint helps the multilingual model
            // pronounce mixed Arabic/English text correctly.
            NormaliseLanguage(request.Language));

        try
        {
            using var client = CreateClient(apiKey);

            var url = $"v1/text-to-speech/{Uri.EscapeDataString(voiceId)}"
                      + $"?output_format={Uri.EscapeDataString(settings.OutputFormat)}";

            using var response = await client
                .PostAsJsonAsync(url, payload, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var detail = await ReadErrorAsync(response, cancellationToken)
                    .ConfigureAwait(false);

                throw new VoiceProviderException(
                    $"ElevenLabs returned {(int)response.StatusCode} {response.ReasonPhrase}. {detail}");
            }

            var audio = await response.Content
                .ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            if (audio.Length == 0)
            {
                throw new VoiceProviderException("ElevenLabs returned an empty audio response.");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";

            return new SynthesisResult(audio, contentType, voiceId);
        }
        catch (VoiceProviderException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new VoiceProviderException(
                SecretRedactor.Redact($"ElevenLabs synthesis failed: {ex.Message}"), ex);
        }
    }

    private HttpClient CreateClient(string apiKey)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);

        if (client.BaseAddress is null)
        {
            client.BaseAddress = new Uri(BaseAddress);
        }

        // Set per call rather than on the registered client so a rotated key
        // takes effect without recycling the factory's handler.
        client.DefaultRequestHeaders.Remove("xi-api-key");
        client.DefaultRequestHeaders.Add("xi-api-key", apiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));

        return client;
    }

    /// <summary>
    /// Reads an error body for diagnostics, capped and redacted. A failure to
    /// read the body must not mask the original HTTP failure.
    /// </summary>
    private static async Task<string> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(body))
            {
                return string.Empty;
            }

            var trimmed = body.Length > 500 ? body[..500] + "…" : body;
            return SecretRedactor.Redact(trimmed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return "(error body could not be read)";
        }
    }

    /// <summary>
    /// Reduces a BCP-47 tag to the two-letter code ElevenLabs expects, or null
    /// to let the model auto-detect.
    /// </summary>
    private static string? NormaliseLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var primary = language.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
        return primary.Length == 2 ? primary.ToLowerInvariant() : null;
    }

    private sealed record SynthesisPayload(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("model_id")] string ModelId,
        [property: JsonPropertyName("voice_settings")] VoiceSettingsPayload VoiceSettings,
        [property: JsonPropertyName("language_code")] string? LanguageCode);

    private sealed record VoiceSettingsPayload(
        [property: JsonPropertyName("stability")] double Stability,
        [property: JsonPropertyName("similarity_boost")] double SimilarityBoost);
}
