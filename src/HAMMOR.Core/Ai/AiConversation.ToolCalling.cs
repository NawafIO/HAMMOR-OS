namespace HAMMOR.Core.Ai;

/// <summary>
/// Structured content of one turn so tool_use/tool_result survive the
/// provider boundary. Stored alongside <see cref="AiMessage"/> history
/// without breaking the text-only <see cref="AiRequest"/> contract.
/// </summary>
public sealed record AiToolCallContent(string Id, string ToolName, string ArgumentsJson);

public sealed record AiToolResultContent(string ToolCallId, string ToolName, string Content, bool IsError);

public sealed record AiMessageContent(
    string? Text,
    IReadOnlyList<AiToolCallContent>? ToolCalls,
    IReadOnlyList<AiToolResultContent>? ToolResults)
{
    public static AiMessageContent FromText(string text) => new(text, null, null);
    public static AiMessageContent FromToolCalls(string? text, IReadOnlyList<AiToolCallContent> calls) => new(text, calls, null);
    public static AiMessageContent FromToolResults(IReadOnlyList<AiToolResultContent> results) => new(null, null, results);
}

/// <summary>
/// Extended request that carries structured tool transcript. When present,
/// providers that support tools use it; others ignore it and use
/// <see cref="AiRequest.Messages"/> as before.
/// </summary>
public sealed record AiToolAwareRequest
{
    public required IReadOnlyList<AiMessage> History { get; init; }
    public string? SystemPrompt { get; init; }
    public string? Model { get; init; }
    public int? MaxTokens { get; init; }
    public string? Effort { get; init; }
    /// <summary>Structured transcript after History: [assistant(tool_calls), user(tool_results)]* in wire order.</summary>
    public IReadOnlyList<(AiRole Role, AiMessageContent Content)> ToolTranscript { get; init; } = Array.Empty<(AiRole, AiMessageContent)>();
}
