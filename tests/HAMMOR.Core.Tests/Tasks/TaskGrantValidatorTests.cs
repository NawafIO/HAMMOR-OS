using HAMMOR.Core.Tasks;
using HAMMOR.Core.Tests.TestDoubles;
using HAMMOR.Core.Tools;
using Xunit;

namespace HAMMOR.Core.Tests.Tasks;

public sealed class TaskGrantValidatorTests
{
    private readonly TestClock _clock = new(TestTasks.Start);

    private static ToolRegistry Registry() =>
        Build(new FakeTool("memory.search", ToolPermission.Read),
              new FakeTool("memory.save", ToolPermission.Write),
              new FakeTool("proc.run", ToolPermission.Execute),
              new FakeTool("filesystem.delete_file", ToolPermission.Destructive));

    private static ToolRegistry Build(params ITool[] tools)
    {
        var registry = new ToolRegistry();
        foreach (var tool in tools)
        {
            registry.Register(tool);
        }

        return registry;
    }

    [Fact]
    public void Valid_read_grant_passes()
    {
        var grant = TestTasks.Grant("t", _clock, ["memory.search"]);

        Assert.Empty(TaskGrantValidator.Validate(grant, Registry(), _clock.GetUtcNow()));
    }

    [Theory]
    [InlineData(ToolPermission.Write)]
    [InlineData(ToolPermission.Execute)]
    [InlineData(ToolPermission.Destructive)]
    public void Ceiling_above_read_is_rejected(ToolPermission permission)
    {
        var grant = TestTasks.Grant("t", _clock, ["memory.search"], permission: permission);

        Assert.NotEmpty(TaskGrantValidator.ValidateStructure(grant, _clock.GetUtcNow()));
    }

    [Theory]
    [InlineData("memory.save")]
    [InlineData("proc.run")]
    [InlineData("filesystem.delete_file")]
    public void Listing_a_tool_above_read_is_rejected(string tool)
    {
        var grant = TestTasks.Grant("t", _clock, [tool]);

        Assert.NotEmpty(TaskGrantValidator.Validate(grant, Registry(), _clock.GetUtcNow()));
    }

    [Fact]
    public void Unknown_tool_and_inexact_name_are_rejected()
    {
        Assert.NotEmpty(TaskGrantValidator.Validate(
            TestTasks.Grant("t", _clock, ["nope.tool"]), Registry(), _clock.GetUtcNow()));

        // The registry resolves case-insensitively; a grant must still use the exact name.
        Assert.NotEmpty(TaskGrantValidator.Validate(
            TestTasks.Grant("t", _clock, ["Memory.Search"]), Registry(), _clock.GetUtcNow()));
    }

    [Fact]
    public void Wildcards_blank_and_duplicate_tool_names_are_rejected()
    {
        foreach (var tools in new[] { new[] { "*" }, new[] { "memory.*" }, new[] { " " }, new[] { "memory.search", "MEMORY.SEARCH" } })
        {
            Assert.NotEmpty(TaskGrantValidator.ValidateStructure(
                TestTasks.Grant("t", _clock, tools), _clock.GetUtcNow()));
        }
    }

    [Fact]
    public void Expiry_in_the_past_or_equal_to_now_is_rejected()
    {
        var past = TestTasks.Grant("t", _clock, ttl: TimeSpan.FromHours(1));
        _clock.Advance(TimeSpan.FromHours(2));

        Assert.NotEmpty(TaskGrantValidator.ValidateStructure(past, _clock.GetUtcNow()));
    }

    [Fact]
    public void Missing_or_unbounded_expiry_is_rejected()
    {
        var unset = TestTasks.Grant("t", _clock) with { ExpiresUtc = default };
        var forever = TestTasks.Grant("t", _clock) with { ExpiresUtc = DateTimeOffset.MaxValue };
        var tooLong = TestTasks.Grant("t", _clock, ttl: TaskGrantValidator.MaxLifetime + TimeSpan.FromDays(1));

        Assert.NotEmpty(TaskGrantValidator.ValidateStructure(unset, _clock.GetUtcNow()));
        Assert.NotEmpty(TaskGrantValidator.ValidateStructure(forever, _clock.GetUtcNow()));
        Assert.NotEmpty(TaskGrantValidator.ValidateStructure(tooLong, _clock.GetUtcNow()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(TaskGrantValidator.MaxToolCallsLimit + 1)]
    public void Tool_call_cap_must_be_in_range(int cap)
    {
        var grant = TestTasks.Grant("t", _clock, maxCalls: cap);

        Assert.NotEmpty(TaskGrantValidator.ValidateStructure(grant, _clock.GetUtcNow()));
    }

    [Fact]
    public void Text_only_grant_with_no_tools_is_valid()
    {
        var grant = TestTasks.Grant("t", _clock, Array.Empty<string>());

        Assert.Empty(TaskGrantValidator.Validate(grant, Registry(), _clock.GetUtcNow()));
    }

    [Theory]
    [InlineData(TaskState.Pending, TaskState.Running, true)]
    [InlineData(TaskState.Running, TaskState.Blocked, true)]
    [InlineData(TaskState.Running, TaskState.Pending, true)]
    [InlineData(TaskState.Blocked, TaskState.Cancelled, true)]
    [InlineData(TaskState.Blocked, TaskState.Pending, false)]
    [InlineData(TaskState.Blocked, TaskState.Running, false)]
    [InlineData(TaskState.Completed, TaskState.Running, false)]
    [InlineData(TaskState.Failed, TaskState.Pending, false)]
    [InlineData(TaskState.Cancelled, TaskState.Pending, false)]
    public void State_machine_transitions(TaskState from, TaskState to, bool allowed)
    {
        Assert.Equal(allowed, TaskStateMachine.IsAllowed(from, to));
    }
}
