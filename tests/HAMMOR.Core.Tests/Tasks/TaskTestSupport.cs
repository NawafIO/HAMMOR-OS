using HAMMOR.Core.Tasks;
using HAMMOR.Core.Tools;

namespace HAMMOR.Core.Tests.Tasks;

/// <summary>Controllable clock so expiry and scheduling are deterministic.</summary>
internal sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

internal static class TestTasks
{
    public static readonly DateTimeOffset Start = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    public static TaskGrant Grant(
        string taskId,
        TestClock clock,
        string[]? tools = null,
        TimeSpan? ttl = null,
        int maxCalls = 3,
        ToolPermission permission = ToolPermission.Read,
        string? supersedes = null) => new()
        {
            TaskId = taskId,
            AllowedTools = tools ?? Array.Empty<string>(),
            MaxPermission = permission,
            GrantedUtc = clock.GetUtcNow(),
            ExpiresUtc = clock.GetUtcNow() + (ttl ?? TimeSpan.FromHours(1)),
            MaxToolCalls = maxCalls,
            SupersedesGrantId = supersedes,
        };

    public static HammorTask Task(
        TestClock clock,
        string[]? tools = null,
        TimeSpan? ttl = null,
        int maxCalls = 3,
        int maxAttempts = 1,
        bool withGrant = true)
    {
        var id = Guid.NewGuid().ToString("n");
        return new HammorTask
        {
            Id = id,
            Title = "task",
            Prompt = "do the thing",
            MaxAttempts = maxAttempts,
            CreatedUtc = clock.GetUtcNow(),
            Grant = withGrant ? Grant(id, clock, tools, ttl, maxCalls) : null,
        };
    }
}
