using HAMMOR.Core.Configuration;
using HAMMOR.Core.Tools;

namespace HAMMOR.Core.Permissions;

/// <summary>
/// Default permission engine. Reads the ceiling from
/// <see cref="SecuritySettings"/> and applies a hard floor that configuration
/// cannot remove: destructive calls always require confirmation.
/// </summary>
public sealed class PermissionEvaluator(IConfigurationStore configurationStore) : IPermissionEvaluator
{
    private readonly IConfigurationStore _configurationStore =
        configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));

    public PermissionDecision Evaluate(ITool tool, ToolInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(invocation);

        var security = _configurationStore.Current.Security;

        // Validity is a precondition for authorisation: never ask the user to
        // approve a call that would fail on its arguments anyway.
        var validation = tool.Validate(invocation);
        if (!validation.IsValid)
        {
            return PermissionDecision.Deny(
                validation.Error ?? $"Arguments for '{tool.Name}' are not valid.");
        }

        // Hard floor. Checked before the configured ceiling so that setting
        // AutoApproveUpTo = Destructive cannot silence destructive prompts.
        if (tool.Permission == ToolPermission.Destructive && security.AlwaysConfirmDestructive)
        {
            return PermissionDecision.RequireConfirmation(
                $"'{tool.Name}' is destructive and always requires explicit confirmation.");
        }

        if (tool.Permission <= security.AutoApproveUpTo)
        {
            return PermissionDecision.Allow(
                $"'{tool.Name}' needs {tool.Permission}, within the auto-approve ceiling "
                + $"of {security.AutoApproveUpTo}.");
        }

        return PermissionDecision.RequireConfirmation(
            $"'{tool.Name}' needs {tool.Permission}, above the auto-approve ceiling "
            + $"of {security.AutoApproveUpTo}.");
    }

    public async Task<PermissionDecision> AuthoriseAsync(
        ITool tool,
        ToolInvocation invocation,
        IConfirmationService confirmation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        var decision = Evaluate(tool, invocation);
        if (decision.Outcome != PermissionOutcome.ConfirmationRequired)
        {
            return decision;
        }

        var details = invocation.Arguments.Count == 0
            ? "(no arguments)"
            : string.Join(
                Environment.NewLine,
                invocation.Arguments.Select(a => $"{a.Key}: {a.Value}"));

        var approved = await confirmation.RequestApprovalAsync(
            new ConfirmationRequest(tool.Name, tool.Permission, decision.Reason, details),
            cancellationToken).ConfigureAwait(false);

        return approved
            ? PermissionDecision.Allow($"User approved '{tool.Name}'.")
            : PermissionDecision.Deny($"User declined '{tool.Name}'.");
    }
}
