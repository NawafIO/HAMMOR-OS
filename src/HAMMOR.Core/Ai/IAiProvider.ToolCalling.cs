namespace HAMMOR.Core.Ai;

/// <summary>
/// Tool-calling extension of <see cref="IAiProvider"/>. Providers that support
/// structured tool use implement this; the agent loop probes for it with a
/// type test and falls back to the text-only path when absent, preserving
/// backward compatibility with any existing <see cref="IAiProvider"/>.
/// </summary>
public interface IToolCallingProvider : IAiProvider
{
    Task<ModelResponseTurn> CompleteWithToolsAsync(
        AiToolAwareRequest request,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken = default);
}
