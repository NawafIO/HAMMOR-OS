using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using HAMMOR.Core.Ai;
using HAMMOR.Core.Configuration;
using HAMMOR.Core.Diagnostics;
using HAMMOR.Core.Security;
using Microsoft.Extensions.Logging;

namespace HAMMOR.Infrastructure.Ai;

/// <summary>
/// Claude implementation of <see cref="IAiProvider"/>, built on the official
/// Anthropic SDK. HAMMOR's primary reasoning backend.
/// </summary>
/// <remarks>
/// The API key is read from <see cref="ISecretStore"/> on each call rather than
/// cached in a field, so rotating the key in Settings takes effect immediately
/// and the credential is not held in memory longer than a request needs it.
/// </remarks>
public sealed class ClaudeAiProvider(
    ISecretStore secretStore,
    IConfigurationStore configurationStore,
    ILogger<ClaudeAiProvider> logger) : IAiProvider
{
    /// <summary>Id matched against <see cref="AiSettings.PrimaryProvider"/>.</summary>
    public const string Id = "claude";

    private readonly ISecretStore _secretStore =
        secretStore ?? throw new ArgumentNullException(nameof(secretStore));

    private readonly IConfigurationStore _configurationStore =
        configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));

    private readonly ILogger<ClaudeAiProvider> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public string ProviderId => Id;

    public string DisplayName => "Claude";

    public async Task<ProviderAvailability> CheckAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        var apiKey = await _secretStore
            .GetAsync(SecretNames.AnthropicApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return ProviderAvailability.NotConfigured(
                "No Anthropic API key is stored. Add one in Settings.");
        }

        // A models list is the cheapest call that actually proves the key is
        // accepted — checking only that a key exists would report "Ready" for
        // a revoked credential.
        try
        {
            var client = CreateClient(apiKey);
            var models = await client.Models.List().ConfigureAwait(false);

            var count = models.Items?.Count ?? 0;
            return ProviderAvailability.Ready(
                $"Connected. {count} model(s) available to this key.");
        }
        catch (AnthropicUnauthorizedException)
        {
            return ProviderAvailability.Error("The stored API key was rejected (401).");
        }
        catch (AnthropicApiException ex)
        {
            _logger.LogWarning(ex, "Claude availability probe failed.");
            return ProviderAvailability.Error(
                SecretRedactor.Redact($"Anthropic API error: {ex.Message}"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Claude availability probe failed to reach the API.");
            return ProviderAvailability.Error(
                SecretRedactor.Redact($"Could not reach the Anthropic API: {ex.Message}"));
        }
    }

    public async Task<AiResponse> CompleteAsync(
        AiRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Messages.Count == 0)
        {
            throw new AiProviderException("Cannot send a request with no messages.");
        }

        var apiKey = await _secretStore
            .GetAsync(SecretNames.AnthropicApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new AiProviderException(
                "Claude is not configured: no Anthropic API key is stored.");
        }

        var settings = _configurationStore.Current.Ai;
        var model = request.Model ?? settings.Model;
        var maxTokens = request.MaxTokens ?? settings.MaxTokens;

        // System is init-only on MessageCreateParams, so the value is resolved
        // before the initializer rather than assigned afterwards. The implicit
        // List -> MessageCreateParamsSystem conversion rejects null, so the
        // nullable target type is declared explicitly and only assigned when
        // there is a prompt to send.
        MessageCreateParamsSystem? systemBlocks = null;
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            systemBlocks = new List<TextBlockParam> { new() { Text = request.SystemPrompt } };
        }

        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = maxTokens,
            Messages = request.Messages.Select(ToMessageParam).ToList(),
            System = systemBlocks,
            OutputConfig = new OutputConfig
            {
                Effort = MapEffort(request.Effort ?? settings.Effort),
            },
        };

        try
        {
            var client = CreateClient(apiKey);
            var message = await client.Messages.Create(parameters).ConfigureAwait(false);

            // Content is a union; narrow to text blocks and concatenate. On
            // Claude Opus 5 thinking blocks carry no text, so skipping
            // non-text blocks loses nothing the user should see.
            var text = string.Concat(
                message.Content
                    .Select(block => block.Value)
                    .OfType<TextBlock>()
                    .Select(block => block.Text));

            var usage = message.Usage is null
                ? null
                : new AiUsage(message.Usage.InputTokens, message.Usage.OutputTokens);

            return new AiResponse(
                text,
                message.Model.ToString() ?? model,
                usage,
                message.StopReason?.ToString());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AnthropicUnauthorizedException ex)
        {
            throw new AiProviderException("The stored Anthropic API key was rejected.", ex);
        }
        catch (AnthropicRateLimitException ex)
        {
            throw new AiProviderException(
                "Anthropic rate limit reached. Wait a moment and try again.", ex);
        }
        catch (AnthropicApiException ex)
        {
            _logger.LogWarning(ex, "Claude request failed for model {Model}.", model);
            throw new AiProviderException(
                SecretRedactor.Redact($"Anthropic API error: {ex.Message}"), ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected failure calling Claude.");
            throw new AiProviderException(
                SecretRedactor.Redact($"Unexpected failure calling Claude: {ex.Message}"), ex);
        }
    }

    private static AnthropicClient CreateClient(string apiKey) => new() { ApiKey = apiKey };

    /// <summary>
    /// Reads the stored key through DPAPI and builds a client from it. The one
    /// place in Infrastructure that turns a stored secret into a live client,
    /// so extensions such as <see cref="ClaudeToolCallingProvider"/> reuse this
    /// path instead of re-implementing secret retrieval.
    /// </summary>
    internal async Task<AnthropicClient> CreateClientAsync(CancellationToken cancellationToken)
    {
        var apiKey = await _secretStore
            .GetAsync(SecretNames.AnthropicApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new AiProviderException(
                "Claude is not configured: no Anthropic API key is stored.");
        }

        return CreateClient(apiKey);
    }

    private static MessageParam ToMessageParam(AiMessage message) => new()
    {
        Role = message.Role == AiRole.Assistant ? Role.Assistant : Role.User,
        Content = message.Text,
    };

    /// <summary>
    /// Maps the configured effort string onto the SDK enum. Unrecognised
    /// values fall back to High rather than throwing, so a hand-edited config
    /// file cannot prevent the app from answering.
    /// </summary>
    internal static Effort MapEffort(string? configured) =>
        configured?.Trim().ToLowerInvariant() switch
        {
            "low" => Effort.Low,
            "medium" => Effort.Medium,
            "high" => Effort.High,
            "xhigh" => Effort.Xhigh,
            "max" => Effort.Max,
            _ => Effort.High,
        };
}
