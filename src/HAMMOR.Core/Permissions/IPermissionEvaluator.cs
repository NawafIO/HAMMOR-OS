using HAMMOR.Core.Tools;

namespace HAMMOR.Core.Permissions;

/// <summary>
/// Decides whether a tool call may proceed. This is the software boundary the
/// model cannot talk its way past: the agent loop must consult it before every
/// invocation, and a model asking for a tool is never sufficient authority.
/// </summary>
public interface IPermissionEvaluator
{
    /// <summary>
    /// Classifies a call as allowed, needing confirmation, or denied, without
    /// performing any I/O.
    /// </summary>
    PermissionDecision Evaluate(ITool tool, ToolInvocation invocation);

    /// <summary>
    /// Resolves a call end-to-end: evaluates it and, when confirmation is
    /// required, asks the user through <paramref name="confirmation"/>.
    /// </summary>
    Task<PermissionDecision> AuthoriseAsync(
        ITool tool,
        ToolInvocation invocation,
        IConfirmationService confirmation,
        CancellationToken cancellationToken = default);
}

/// <summary>Verdict for one tool call.</summary>
/// <param name="Outcome">Allowed, confirmation required, or denied.</param>
/// <param name="Reason">Why, for the audit trail and the confirmation dialog.</param>
public sealed record PermissionDecision(PermissionOutcome Outcome, string Reason)
{
    public bool IsAllowed => Outcome == PermissionOutcome.Allowed;

    public static PermissionDecision Allow(string reason) =>
        new(PermissionOutcome.Allowed, reason);

    public static PermissionDecision RequireConfirmation(string reason) =>
        new(PermissionOutcome.ConfirmationRequired, reason);

    public static PermissionDecision Deny(string reason) =>
        new(PermissionOutcome.Denied, reason);
}

public enum PermissionOutcome
{
    Denied = 0,
    ConfirmationRequired = 1,
    Allowed = 2,
}

/// <summary>
/// Asks the user to approve a privileged call. Implemented by the UI; Core
/// only knows the contract so the permission engine stays testable and
/// presentation-free.
/// </summary>
public interface IConfirmationService
{
    /// <summary>
    /// Prompts for approval. Returns false on denial and on dismissal —
    /// anything other than explicit approval is treated as refusal.
    /// </summary>
    Task<bool> RequestApprovalAsync(
        ConfirmationRequest request,
        CancellationToken cancellationToken = default);
}

/// <param name="ToolName">Tool awaiting approval.</param>
/// <param name="Permission">Authority it is asking for.</param>
/// <param name="Summary">What will happen, in the user's language.</param>
/// <param name="Details">Arguments rendered for review.</param>
public sealed record ConfirmationRequest(
    string ToolName,
    ToolPermission Permission,
    string Summary,
    string Details);
