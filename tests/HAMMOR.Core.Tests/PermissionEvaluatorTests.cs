using HAMMOR.Core.Configuration;
using HAMMOR.Core.Permissions;
using HAMMOR.Core.Tools;
using HAMMOR.Core.Tests.TestDoubles;
using Xunit;

namespace HAMMOR.Core.Tests;

/// <summary>
/// Tests the software security boundary. These matter more than the rest:
/// if the permission engine is wrong, a model request becomes an action.
/// </summary>
public sealed class PermissionEvaluatorTests
{
    [Theory]
    [InlineData(ToolPermission.Read, ToolPermission.Read)]
    [InlineData(ToolPermission.Read, ToolPermission.Write)]
    [InlineData(ToolPermission.Write, ToolPermission.Write)]
    [InlineData(ToolPermission.Write, ToolPermission.Execute)]
    [InlineData(ToolPermission.Execute, ToolPermission.Execute)]
    public void Allows_tools_at_or_below_the_configured_ceiling(
        ToolPermission toolPermission,
        ToolPermission ceiling)
    {
        var evaluator = CreateEvaluator(ceiling);
        var tool = new FakeTool("fake.tool", toolPermission);

        var decision = evaluator.Evaluate(tool, Invocation(tool));

        Assert.Equal(PermissionOutcome.Allowed, decision.Outcome);
        Assert.True(decision.IsAllowed);
    }

    [Theory]
    [InlineData(ToolPermission.Write, ToolPermission.Read)]
    [InlineData(ToolPermission.Execute, ToolPermission.Read)]
    [InlineData(ToolPermission.Execute, ToolPermission.Write)]
    public void Requires_confirmation_for_tools_above_the_ceiling(
        ToolPermission toolPermission,
        ToolPermission ceiling)
    {
        var evaluator = CreateEvaluator(ceiling);
        var tool = new FakeTool("fake.tool", toolPermission);

        var decision = evaluator.Evaluate(tool, Invocation(tool));

        Assert.Equal(PermissionOutcome.ConfirmationRequired, decision.Outcome);
    }

    /// <summary>
    /// The hard floor: raising the ceiling to Destructive must not silence
    /// destructive confirmation. This is the case a prompt-only guard would
    /// get wrong.
    /// </summary>
    [Fact]
    public void Destructive_tools_require_confirmation_even_at_the_highest_ceiling()
    {
        var evaluator = CreateEvaluator(
            ToolPermission.Destructive,
            alwaysConfirmDestructive: true);

        var tool = new FakeTool("fake.destroy", ToolPermission.Destructive);

        var decision = evaluator.Evaluate(tool, Invocation(tool));

        Assert.Equal(PermissionOutcome.ConfirmationRequired, decision.Outcome);
    }

    [Fact]
    public void Invalid_arguments_are_denied_before_the_user_is_asked()
    {
        var evaluator = CreateEvaluator(ToolPermission.Execute);
        var tool = new FakeTool("fake.tool", ToolPermission.Read, isValid: false);
        var confirmation = new RecordingConfirmationService(approve: true);

        var decision = evaluator.Evaluate(tool, Invocation(tool));

        Assert.Equal(PermissionOutcome.Denied, decision.Outcome);
        Assert.Equal(0, confirmation.CallCount);
    }

    [Fact]
    public async Task Authorise_allows_when_the_user_approves()
    {
        var evaluator = CreateEvaluator(ToolPermission.Read);
        var tool = new FakeTool("fake.write", ToolPermission.Write);
        var confirmation = new RecordingConfirmationService(approve: true);

        var decision = await evaluator.AuthoriseAsync(tool, Invocation(tool), confirmation);

        Assert.True(decision.IsAllowed);
        Assert.Equal(1, confirmation.CallCount);
    }

    [Fact]
    public async Task Authorise_denies_when_the_user_declines()
    {
        var evaluator = CreateEvaluator(ToolPermission.Read);
        var tool = new FakeTool("fake.write", ToolPermission.Write);
        var confirmation = new RecordingConfirmationService(approve: false);

        var decision = await evaluator.AuthoriseAsync(tool, Invocation(tool), confirmation);

        Assert.Equal(PermissionOutcome.Denied, decision.Outcome);
        Assert.Equal(1, confirmation.CallCount);
    }

    [Fact]
    public async Task Authorise_does_not_prompt_for_an_already_allowed_tool()
    {
        var evaluator = CreateEvaluator(ToolPermission.Write);
        var tool = new FakeTool("fake.read", ToolPermission.Read);
        var confirmation = new RecordingConfirmationService(approve: false);

        var decision = await evaluator.AuthoriseAsync(tool, Invocation(tool), confirmation);

        // Already within the ceiling, so the user must not be interrupted —
        // and the service's "deny" answer must not override the allow.
        Assert.True(decision.IsAllowed);
        Assert.Equal(0, confirmation.CallCount);
    }

    [Fact]
    public void Decision_reason_is_populated_for_the_audit_trail()
    {
        var evaluator = CreateEvaluator(ToolPermission.Read);
        var tool = new FakeTool("fake.execute", ToolPermission.Execute);

        var decision = evaluator.Evaluate(tool, Invocation(tool));

        Assert.False(string.IsNullOrWhiteSpace(decision.Reason));
        Assert.Contains("fake.execute", decision.Reason, StringComparison.Ordinal);
    }

    private static PermissionEvaluator CreateEvaluator(
        ToolPermission ceiling,
        bool alwaysConfirmDestructive = true)
    {
        var configuration = new HammorConfiguration();
        configuration.Security.AutoApproveUpTo = ceiling;
        configuration.Security.AlwaysConfirmDestructive = alwaysConfirmDestructive;

        return new PermissionEvaluator(new FakeConfigurationStore(configuration));
    }

    private static ToolInvocation Invocation(ITool tool) =>
        new() { ToolName = tool.Name };
}
