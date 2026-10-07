using HAMMOR.App.Shell;
using HAMMOR.Core.Tasks;
using Xunit;

namespace HAMMOR.App.Tests.Shell;

/// <summary>
/// The count behind the dot on the Tasks row: tasks waiting for approval,
/// read once and then kept current from the store's change events.
/// </summary>
public sealed class BlockedTaskTrackerTests
{
    private static HammorTask Make(string id, TaskState state) =>
        new() { Id = id, Title = "Task " + id, State = state };

    [Fact]
    public async Task Tasks_already_blocked_are_counted()
    {
        var store = new FakeTaskStore { Listed = [Make("a", TaskState.Blocked), Make("b", TaskState.Blocked)] };
        using var tracker = new BlockedTaskTracker(store);

        await tracker.StartAsync();

        Assert.Equal(2, tracker.Count);
        Assert.Equal(new[] { TaskState.Blocked }, store.RequestedStates);
    }

    [Fact]
    public async Task Changes_move_the_count_both_ways()
    {
        var store = new FakeTaskStore();
        using var tracker = new BlockedTaskTracker(store);
        await tracker.StartAsync();

        store.Raise(Make("a", TaskState.Blocked));
        store.Raise(Make("b", TaskState.Blocked));
        Assert.Equal(2, tracker.Count);

        // Resumed under a new grant: Pending again.
        store.Raise(Make("a", TaskState.Pending));
        Assert.Equal(1, tracker.Count);

        store.Raise(Make("b", TaskState.Cancelled));
        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public async Task The_count_is_announced_only_when_it_changes()
    {
        var store = new FakeTaskStore();
        using var tracker = new BlockedTaskTracker(store);
        var announced = 0;
        tracker.CountChanged += (_, _) => announced++;
        await tracker.StartAsync();

        store.Raise(Make("a", TaskState.Blocked));
        store.Raise(Make("a", TaskState.Blocked));      // same task again
        store.Raise(Make("z", TaskState.Running));      // never blocked
        store.Raise(Make("a", TaskState.Pending));

        Assert.Equal(2, announced);
    }

    [Fact]
    public async Task A_change_seen_while_listing_wins_over_the_listing()
    {
        var store = new FakeTaskStore
        {
            Listed = [Make("a", TaskState.Blocked), Make("b", TaskState.Blocked)],
            HoldListing = true,
        };
        using var tracker = new BlockedTaskTracker(store);

        var starting = tracker.StartAsync();
        store.Raise(Make("a", TaskState.Pending));      // resumed while the list was being read
        store.ReleaseListing();
        await starting;

        Assert.Equal(1, tracker.Count);
    }

    [Fact]
    public async Task A_store_that_cannot_be_listed_does_not_stop_the_tracker()
    {
        var store = new FakeTaskStore { ListFailure = new InvalidOperationException("database is locked") };
        using var tracker = new BlockedTaskTracker(store);

        await tracker.StartAsync();
        Assert.Equal(0, tracker.Count);

        store.Raise(Make("a", TaskState.Blocked));
        Assert.Equal(1, tracker.Count);
    }

    [Fact]
    public async Task Starting_twice_follows_the_store_once()
    {
        var store = new FakeTaskStore();
        using var tracker = new BlockedTaskTracker(store);

        await tracker.StartAsync();
        await tracker.StartAsync();

        Assert.Equal(1, store.Subscribers);
        Assert.Equal(1, store.ListCalls);
    }

    [Fact]
    public async Task A_disposed_tracker_stops_following()
    {
        var store = new FakeTaskStore();
        var tracker = new BlockedTaskTracker(store);
        await tracker.StartAsync();

        tracker.Dispose();
        store.Raise(Make("a", TaskState.Blocked));

        Assert.Equal(0, tracker.Count);
        Assert.Equal(0, store.Subscribers);
    }

    /// <summary>A task store that only lists, raises changes and counts subscribers.</summary>
    private sealed class FakeTaskStore : ITaskStore
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private EventHandler<HammorTask>? _changed;

        public IReadOnlyList<HammorTask> Listed { get; init; } = [];

        public bool HoldListing { get; init; }

        public Exception? ListFailure { get; init; }

        public IReadOnlyCollection<TaskState>? RequestedStates { get; private set; }

        public int ListCalls { get; private set; }

        public int Subscribers => _changed?.GetInvocationList().Length ?? 0;

        public event EventHandler<HammorTask>? TaskChanged
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public void Raise(HammorTask task) => _changed?.Invoke(this, task);

        public void ReleaseListing() => _release.TrySetResult();

        public async Task<IReadOnlyList<HammorTask>> ListAsync(
            IReadOnlyCollection<TaskState>? states = null,
            string? projectId = null,
            int limit = 200,
            CancellationToken cancellationToken = default)
        {
            ListCalls++;
            RequestedStates = states;

            if (HoldListing)
            {
                await _release.Task;
            }

            if (ListFailure is not null)
            {
                throw ListFailure;
            }

            return Listed;
        }

        public Task<HammorTask> CreateAsync(HammorTask task, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HammorTask?> GetAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HammorTask> UpdateAsync(HammorTask task, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> ReconcileInterruptedAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HammorTask> ResumeBlockedAsync(
            string taskId,
            TaskGrant newGrant,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HammorTask?> TryCancelAsync(string taskId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
