namespace HAMMOR.Core.Tools;

/// <summary>
/// A capability HAMMOR can invoke. Each tool declares its own permission
/// level, validates its own input, and returns a structured result rather
/// than throwing for expected failures.
/// </summary>
/// <remarks>
/// Tools are intentionally narrow. There is no general "run any command"
/// tool: an arbitrary-execution primitive would make the permission model
/// meaningless, since a single Execute grant would cover every destructive
/// action as well.
/// </remarks>
public interface ITool
{
    /// <summary>Unique invocation name, e.g. <c>memory.search</c>.</summary>
    string Name { get; }

    /// <summary>
    /// What the tool does and when to use it. This text is what the model sees,
    /// so it should state trigger conditions, not just behaviour.
    /// </summary>
    string Description { get; }

    /// <summary>Authority this tool requires.</summary>
    ToolPermission Permission { get; }

    /// <summary>
    /// JSON Schema for <see cref="ToolInvocation.Arguments"/>, used both to
    /// describe the tool to the model and to document it in the UI.
    /// </summary>
    ToolInputSchema InputSchema { get; }

    /// <summary>
    /// Checks arguments before any authorisation prompt is shown, so the user
    /// is never asked to approve a call that cannot run.
    /// </summary>
    ToolValidationResult Validate(ToolInvocation invocation);

    /// <summary>
    /// Runs the tool. Expected failures come back as
    /// <see cref="ToolResult.Failure"/>; only genuinely unexpected faults
    /// should throw.
    /// </summary>
    Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken = default);
}

/// <summary>JSON Schema description of a tool's arguments.</summary>
/// <param name="Json">
/// Schema document as JSON. Stored as a string so Core carries no dependency
/// on a particular JSON library.
/// </param>
public sealed record ToolInputSchema(string Json)
{
    /// <summary>Schema for a tool that takes no arguments.</summary>
    public static ToolInputSchema None { get; } =
        new("""{"type":"object","properties":{},"additionalProperties":false}""");
}

/// <summary>A request to run one tool.</summary>
public sealed record ToolInvocation
{
    /// <summary>Correlation id, echoed into the audit trail.</summary>
    public string InvocationId { get; init; } = Guid.NewGuid().ToString("n");

    /// <summary>Name of the tool to run.</summary>
    public required string ToolName { get; init; }

    /// <summary>Arguments, keyed by schema property name.</summary>
    public IReadOnlyDictionary<string, string?> Arguments { get; init; } =
        new Dictionary<string, string?>();

    /// <summary>Project this call belongs to, when scoped to one.</summary>
    public string? ProjectId { get; init; }

    /// <summary>What prompted the call, recorded for the audit trail.</summary>
    public string? Reason { get; init; }

    /// <summary>Reads a string argument, or null when absent or blank.</summary>
    public string? GetString(string key) =>
        Arguments.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    /// <summary>Reads an int argument, falling back to <paramref name="fallback"/>.</summary>
    public int GetInt32(string key, int fallback) =>
        Arguments.TryGetValue(key, out var raw)
        && int.TryParse(raw, out var parsed)
            ? parsed
            : fallback;
}

/// <summary>Outcome of pre-execution argument validation.</summary>
public sealed record ToolValidationResult(bool IsValid, string? Error)
{
    public static ToolValidationResult Valid { get; } = new(true, null);

    public static ToolValidationResult Invalid(string error) => new(false, error);
}

/// <summary>Outcome of running a tool.</summary>
/// <param name="Succeeded">Whether the tool achieved its purpose.</param>
/// <param name="Output">Result text shown to the user and fed back to the model.</param>
/// <param name="Error">Failure reason when <paramref name="Succeeded"/> is false.</param>
public sealed record ToolResult(bool Succeeded, string Output, string? Error)
{
    public static ToolResult Success(string output) => new(true, output, null);

    public static ToolResult Failure(string error) => new(false, string.Empty, error);
}
