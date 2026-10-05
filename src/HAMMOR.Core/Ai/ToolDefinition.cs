using HAMMOR.Core.Tools;

namespace HAMMOR.Core.Ai;

/// <summary>
/// Provider-agnostic tool description handed to the model.
/// Derived from <see cref="ITool"/> so the model's surface always mirrors
/// the real registry — no extra disclosure.
/// </summary>
public sealed record ToolDefinition(string Name, string Description, string InputSchemaJson)
{
    public static ToolDefinition FromTool(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return new ToolDefinition(tool.Name, tool.Description, tool.InputSchema.Json);
    }
}

/// <summary>One assistant-side tool call as seen by Core (model is never trusted to name a real tool).</summary>
public sealed record ModelToolCall(string Id, string ToolName, string ArgumentsJson);

/// <summary>Result of one tool invocation, fed back to the model. Bounded and redacted.</summary>
public sealed record ModelToolResult(string ToolCallId, string ToolName, string Content, bool IsError);

/// <summary>One turn's content: text plus any tool calls.</summary>
public sealed record ModelResponseTurn(
    string? Text,
    IReadOnlyList<ModelToolCall> ToolCalls,
    string? StopReason)
{
    public bool HasToolCalls => ToolCalls.Count > 0;
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}

/// <summary>Stop reasons surfaced by providers in a uniform shape.</summary>
public static class ModelStopReasons
{
    public const string EndTurn = "end_turn";
    public const string ToolUse = "tool_use";
    public const string MaxTokens = "max_tokens";
    public const string Refusal = "refusal";
    public const string MaxRoundsExceeded = "max_rounds_exceeded";
}
