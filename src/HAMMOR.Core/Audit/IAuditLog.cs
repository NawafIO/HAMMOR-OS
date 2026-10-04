using HAMMOR.Core.Permissions;
using HAMMOR.Core.Tools;

namespace HAMMOR.Core.Audit;

/// <summary>
/// Append-only record of meaningful actions: tool authorisations, tool
/// outcomes, configuration changes, provider failures.
/// </summary>
/// <remarks>
/// Implementations must redact credentials before persisting
/// (<see cref="Diagnostics.SecretRedactor"/>) and must never drop a write
/// silently — a failed audit write is itself a reportable failure.
/// </remarks>
public interface IAuditLog
{
    Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Most recent entries, newest first.</summary>
    Task<IReadOnlyList<AuditEntry>> GetRecentAsync(
        int limit = 200,
        CancellationToken cancellationToken = default);
}

/// <summary>One audited action.</summary>
public sealed record AuditEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("n");

    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    public required AuditCategory Category { get; init; }

    /// <summary>Subject of the action, e.g. a tool name or setting key.</summary>
    public required string Subject { get; init; }

    /// <summary>What happened, already redacted.</summary>
    public required string Message { get; init; }

    public AuditOutcome Outcome { get; init; } = AuditOutcome.Information;

    /// <summary>Permission level involved, when the entry concerns a tool.</summary>
    public ToolPermission? Permission { get; init; }

    public string? ProjectId { get; init; }

    /// <summary>Correlates with <see cref="ToolInvocation.InvocationId"/>.</summary>
    public string? CorrelationId { get; init; }

    public static AuditEntry ForDecision(
        ITool tool,
        ToolInvocation invocation,
        PermissionDecision decision) => new()
        {
            Category = AuditCategory.Authorisation,
            Subject = tool.Name,
            Message = decision.Reason,
            Permission = tool.Permission,
            ProjectId = invocation.ProjectId,
            CorrelationId = invocation.InvocationId,
            Outcome = decision.Outcome switch
            {
                PermissionOutcome.Allowed => AuditOutcome.Allowed,
                PermissionOutcome.Denied => AuditOutcome.Denied,
                _ => AuditOutcome.Information,
            },
        };
}

public enum AuditCategory
{
    Authorisation = 0,
    ToolExecution = 1,
    Configuration = 2,
    Provider = 3,
    TaskLifecycle = 4,
    Memory = 5,
}

public enum AuditOutcome
{
    Information = 0,
    Allowed = 1,
    Denied = 2,
    Succeeded = 3,
    Failed = 4,
}
