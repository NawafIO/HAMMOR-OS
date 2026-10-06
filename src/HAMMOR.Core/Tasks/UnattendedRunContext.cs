using HAMMOR.Core.Permissions;
using HAMMOR.Core.Tools;

namespace HAMMOR.Core.Tasks;

/// <summary>
/// Confirmation service for unattended runs: never prompts, always refuses.
/// Anything the permission evaluator would have asked a person about is
/// therefore refused, never approved implicitly.
/// </summary>
public sealed class UnattendedConfirmationService : IConfirmationService
{
    public int RequestCount { get; private set; }

    public ConfirmationRequest? LastRequest { get; private set; }

    public Task<bool> RequestApprovalAsync(
        ConfirmationRequest request,
        CancellationToken cancellationToken = default)
    {
        RequestCount++;
        LastRequest = request;
        return Task.FromResult(false);
    }
}

public enum ToolGateOutcome
{
    Allow = 0,

    /// <summary>The run stops and the task becomes Blocked.</summary>
    Block = 1,

    /// <summary>The run stops and fails (e.g. call cap exceeded).</summary>
    Fail = 2,
}

public sealed record ToolGateDecision(ToolGateOutcome Outcome, string Reason)
{
    public static ToolGateDecision Allow { get; } = new(ToolGateOutcome.Allow, "Within task grant.");

    public static ToolGateDecision Block(string reason) => new(ToolGateOutcome.Block, reason);

    public static ToolGateDecision Fail(string reason) => new(ToolGateOutcome.Fail, reason);
}

/// <summary>
/// Per-run authority check for one unattended run. The grant is a snapshot
/// taken by the runner; nothing the model says can change it. It narrows what
/// the normal permission path allows and never widens it.
/// </summary>
public sealed class UnattendedRunContext
{
    private readonly HashSet<string> _allowed;
    private readonly TimeProvider _time;
    private readonly TaskPathScope? _pathScope;

    /// <param name="pathScope">
    /// Required for path-scoped tools; when absent every path-scoped call
    /// blocks (fail closed).
    /// </param>
    public UnattendedRunContext(
        string taskId,
        TaskGrant grant,
        TimeProvider? timeProvider = null,
        TaskPathScope? pathScope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentNullException.ThrowIfNull(grant);

        TaskId = taskId;
        Grant = grant;
        _time = timeProvider ?? TimeProvider.System;
        _pathScope = pathScope;
        _allowed = new HashSet<string>(grant.AllowedTools ?? Array.Empty<string>(), StringComparer.Ordinal);
    }

    public string TaskId { get; }

    public TaskGrant Grant { get; }

    public UnattendedConfirmationService Confirmation { get; } = new();

    public int ToolCallsAuthorised { get; private set; }

    /// <summary>Whether a tool may even be offered to the model for this run.</summary>
    public bool IsToolGranted(string toolName) => _allowed.Contains(toolName);

    /// <summary>Prefix that marks audit text as belonging to an unattended run.</summary>
    public string Tag(string message) => $"[unattended task {TaskId}] {message}";

    /// <summary>
    /// Tool-level check without arguments. A path-scoped tool always blocks
    /// here because its path arguments cannot be verified.
    /// </summary>
    public ToolGateDecision Check(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return Check(tool, new ToolInvocation { ToolName = tool.Name });
    }

    /// <summary>
    /// Checked after the call has been validated and the tool resolved, and
    /// before the normal permission evaluation.
    /// </summary>
    public ToolGateDecision Check(ITool tool, ToolInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(invocation);

        if (Grant.SupersededUtc is not null)
        {
            return ToolGateDecision.Block("The task's grant has been superseded.");
        }

        if (_time.GetUtcNow() >= Grant.ExpiresUtc)
        {
            return ToolGateDecision.Block("The task's grant has expired.");
        }

        if (Grant.MaxPermission != ToolPermission.Read || tool.Permission != ToolPermission.Read)
        {
            return ToolGateDecision.Block(
                $"'{tool.Name}' needs {tool.Permission}; unattended runs are limited to Read.");
        }

        if (!_allowed.Contains(tool.Name))
        {
            return ToolGateDecision.Block($"'{tool.Name}' is not in the task's grant.");
        }

        // ADR-004 §2.4: path arguments must resolve inside the granted roots.
        if (tool is IPathScopedTool)
        {
            if (_pathScope is null)
            {
                return ToolGateDecision.Block(
                    $"'{tool.Name}' touches the filesystem, but no path scope is configured for this run.");
            }

            var pathDecision = _pathScope.CheckCall(tool, invocation, Grant.AllowedRoots);
            if (pathDecision.Outcome != ToolGateOutcome.Allow)
            {
                return pathDecision;
            }
        }

        if (ToolCallsAuthorised >= Grant.MaxToolCalls)
        {
            return ToolGateDecision.Fail(
                $"The task exceeded its limit of {Grant.MaxToolCalls} tool call(s).");
        }

        ToolCallsAuthorised++;
        return ToolGateDecision.Allow;
    }
}
