using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using HAMMOR.Core.Ai;
using HAMMOR.Core.Configuration;
using HAMMOR.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace HAMMOR.Infrastructure.Ai;

/// <summary>
/// Decorator over <see cref="ClaudeAiProvider"/> that adds structured tool
/// use. Availability and plain completion delegate straight to the inner
/// provider, and the Anthropic client — including DPAPI key retrieval — is
/// built by <see cref="ClaudeAiProvider.CreateClientAsync"/>, so there is
/// exactly one secret-handling path. All wire types stay in Infrastructure.
/// </summary>
public sealed class ClaudeToolCallingProvider(
    ClaudeAiProvider inner,
    IConfigurationStore configurationStore,
    ILogger<ClaudeToolCallingProvider> logger) : IToolCallingProvider
{
    private readonly ClaudeAiProvider _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IConfigurationStore _configurationStore = configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));
    private readonly ILogger<ClaudeToolCallingProvider> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public string ProviderId => _inner.ProviderId;
    public string DisplayName => _inner.DisplayName;

    public Task<ProviderAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
        => _inner.CheckAvailabilityAsync(cancellationToken);

    public Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default)
        => _inner.CompleteAsync(request, cancellationToken);

    public async Task<ModelResponseTurn> CompleteWithToolsAsync(
        AiToolAwareRequest request,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(tools);
        if (request.History.Count == 0 && request.ToolTranscript.Count == 0)
            throw new AiProviderException("Cannot send a request with no messages.");

        // Key retrieval + client construction stay in ClaudeAiProvider.
        var client = await _inner.CreateClientAsync(cancellationToken).ConfigureAwait(false);

        var settings = _configurationStore.Current.Ai;
        var model = request.Model ?? settings.Model;
        var maxTokens = request.MaxTokens ?? settings.MaxTokens;

        MessageCreateParamsSystem? systemBlocks = null;
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            systemBlocks = new List<TextBlockParam> { new() { Text = request.SystemPrompt } };

        var toolUnions = new List<ToolUnion>();
        foreach (var def in tools)
        {
            var inputSchema = ParseInputSchema(def.InputSchemaJson);
            toolUnions.Add(new Tool
            {
                Name = def.Name,
                Description = def.Description,
                InputSchema = inputSchema,
            });
        }

        var messages = BuildClaudeMessages(request);

        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = maxTokens,
            Messages = messages,
            System = systemBlocks,
            Tools = toolUnions.Count == 0 ? null : toolUnions,
            OutputConfig = new OutputConfig { Effort = ClaudeAiProvider.MapEffort(request.Effort ?? settings.Effort) },
        };

        try
        {
            var message = await client.Messages.Create(parameters).ConfigureAwait(false);

            // Extract text and tool_use blocks.
            string? text = null;
            var toolCalls = new List<ModelToolCall>();

            foreach (var block in message.Content)
            {
                if (block.TryPickText(out var textBlock))
                {
                    text = (text ?? string.Empty) + textBlock.Text;
                }
                else if (block.TryPickToolUse(out var toolUse))
                {
                    // Input is IReadOnlyDictionary<string, JsonElement>
                    string argsJson;
                    try { argsJson = JsonSerializer.Serialize(toolUse.Input); }
                    catch { argsJson = "{}"; }
                    toolCalls.Add(new ModelToolCall(toolUse.ID, toolUse.Name, argsJson));
                }
                // thinking/redacted/server tool blocks intentionally ignored — never surfaced.
            }

            var stopReason = message.StopReason?.ToString();
            // Normalise stop reason to provider-agnostic constants where known.
            // Anthropic uses "tool_use" and "end_turn"; we pass through verbatim and
            // the loop also recognises the typed constants.
            return new ModelResponseTurn(
                string.IsNullOrWhiteSpace(text) ? null : text,
                toolCalls,
                stopReason);
        }
        catch (OperationCanceledException) { throw; }
        catch (AnthropicUnauthorizedException ex)
        {
            throw new AiProviderException("The stored Anthropic API key was rejected.", ex);
        }
        catch (AnthropicRateLimitException ex)
        {
            throw new AiProviderException("Anthropic rate limit reached. Wait a moment and try again.", ex);
        }
        catch (AnthropicApiException ex)
        {
            _logger.LogWarning(ex, "Claude tool-calling request failed for model {Model}.", model);
            throw new AiProviderException(SecretRedactor.Redact($"Anthropic API error: {ex.Message}"), ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected failure calling Claude with tools.");
            throw new AiProviderException(SecretRedactor.Redact($"Unexpected failure calling Claude: {ex.Message}"), ex);
        }
    }

    /// <summary>
    /// Builds a subsequent message carrying tool results. Exposed for the
    /// loop's helper; also used in tests to verify wire shape.
    /// </summary>
    public static MessageParam BuildToolResultMessage(IReadOnlyList<ModelToolResult> results)
    {
        var blocks = new List<ContentBlockParam>(results.Count);
        foreach (var r in results)
        {
            var content = new ToolResultBlockParamContent(r.Content);
            blocks.Add(new ToolResultBlockParam
            {
                ToolUseID = r.ToolCallId,
                Content = content,
                IsError = r.IsError ? true : null,
            });
        }
        return new MessageParam { Role = Role.User, Content = blocks };
    }

    /// <summary>
    /// Builds an assistant message that contains text plus tool_use blocks,
    /// as it must appear when threading history back to the model.
    /// </summary>
    public static MessageParam BuildAssistantWithToolsMessage(string? text, IReadOnlyList<ModelToolCall> calls)
    {
        var blocks = new List<ContentBlockParam>();
        if (!string.IsNullOrWhiteSpace(text))
            blocks.Add(new TextBlockParam { Text = text });
        foreach (var call in calls)
        {
            IReadOnlyDictionary<string, JsonElement> inputDict;
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson);
                var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var p in doc.RootElement.EnumerateObject())
                    dict[p.Name] = p.Value.Clone();
                inputDict = dict;
            }
            catch
            {
                inputDict = new Dictionary<string, JsonElement>();
            }
            blocks.Add(new ToolUseBlockParam { ID = call.Id, Name = call.ToolName, Input = inputDict });
        }
        return new MessageParam { Role = Role.Assistant, Content = blocks };
    }

    private static InputSchema ParseInputSchema(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.Clone() : JsonDocument.Parse("\"object\"").RootElement;
            var props = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (root.TryGetProperty("properties", out var propsEl) && propsEl.ValueKind == JsonValueKind.Object)
                foreach (var p in propsEl.EnumerateObject()) props[p.Name] = p.Value.Clone();
            var required = new List<string>();
            if (root.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.Array)
                foreach (var r in reqEl.EnumerateArray())
                    if (r.ValueKind == JsonValueKind.String && r.GetString() is { } s) required.Add(s);
            return new InputSchema { Type = type, Properties = props, Required = required };
        }
        catch
        {
            return new InputSchema
            {
                Type = JsonDocument.Parse("\"object\"").RootElement,
                Properties = new Dictionary<string, JsonElement>(),
                Required = Array.Empty<string>(),
            };
        }
    }

    private static List<MessageParam> BuildClaudeMessages(AiToolAwareRequest request)
    {
        var messages = new List<MessageParam>();
        foreach (var m in request.History)
            messages.Add(ToMessageParam(new AiMessage(m.Role, m.Text)));
        foreach (var (role, content) in request.ToolTranscript)
        {
            if (content.ToolCalls is not null && content.ToolCalls.Count > 0)
            {
                var blocks = new List<ContentBlockParam>();
                if (!string.IsNullOrWhiteSpace(content.Text))
                    blocks.Add(new TextBlockParam { Text = content.Text });
                foreach (var c in content.ToolCalls)
                {
                    IReadOnlyDictionary<string, JsonElement> inputDict;
                    try
                    {
                        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(c.ArgumentsJson) ? "{}" : c.ArgumentsJson);
                        var d = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                        foreach (var p in doc.RootElement.EnumerateObject()) d[p.Name] = p.Value.Clone();
                        inputDict = d;
                    }
                    catch { inputDict = new Dictionary<string, JsonElement>(); }
                    blocks.Add(new ToolUseBlockParam { ID = c.Id, Name = c.ToolName, Input = inputDict });
                }
                messages.Add(new MessageParam { Role = Role.Assistant, Content = blocks });
            }
            else if (content.ToolResults is not null && content.ToolResults.Count > 0)
            {
                var blocks = new List<ContentBlockParam>();
                foreach (var r in content.ToolResults)
                {
                    blocks.Add(new ToolResultBlockParam
                    {
                        ToolUseID = r.ToolCallId,
                        Content = new ToolResultBlockParamContent(r.Content),
                        IsError = r.IsError ? true : null,
                    });
                }
                messages.Add(new MessageParam { Role = Role.User, Content = blocks });
            }
            else if (!string.IsNullOrWhiteSpace(content.Text))
            {
                messages.Add(new MessageParam { Role = role == AiRole.Assistant ? Role.Assistant : Role.User, Content = content.Text });
            }
        }
        return messages;
    }

    private static MessageParam ToMessageParam(AiMessage message) => new()
    {
        Role = message.Role == AiRole.Assistant ? Role.Assistant : Role.User,
        Content = message.Text,
    };

}
