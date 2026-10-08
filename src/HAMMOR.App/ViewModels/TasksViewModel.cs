using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HAMMOR.App.Localization;
using HAMMOR.App.Services;
using HAMMOR.Core.Tasks;
using Microsoft.Extensions.Logging;

namespace HAMMOR.App.ViewModels;

/// <summary>
/// One row on the Tasks page. Display-only projections of a stored task; the
/// flags decide which buttons are shown, never what is allowed (Core rechecks
/// every state on cancel and resume).
/// </summary>
public sealed class TaskItemViewModel
{
    private const string DateFormat = "yyyy-MM-dd HH:mm";

    public TaskItemViewModel(HammorTask model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public HammorTask Model { get; }

    public string Id => Model.Id;

    public string Title => Model.Title;

    public string? Description => Model.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Model.Description);

    public TaskState State => Model.State;

    public bool IsBlocked => Model.State == TaskState.Blocked;

    public string? BlockedReason => Model.BlockedReason;

    public bool ShowBlockedReason => IsBlocked && !string.IsNullOrWhiteSpace(Model.BlockedReason);

    public string? Error => Model.Error;

    /// <summary>A blocked task explains itself through its reason instead.</summary>
    public bool HasError => !IsBlocked && !string.IsNullOrWhiteSpace(Model.Error);

    public string? Result => Model.Result;

    public bool HasResult => !string.IsNullOrWhiteSpace(Model.Result);

    public bool CanCancel => Model.State is TaskState.Pending or TaskState.Running or TaskState.Blocked;

    public bool CanResume => IsBlocked;

    public bool HasGrant => Model.Grant is not null;

    public string CreatedText => Format(Model.CreatedUtc);

    public bool HasSchedule => Model.ScheduledForUtc is not null;

    public string ScheduledText => Model.ScheduledForUtc is { } at ? Format(at) : string.Empty;

    public string ExpiresText => Model.Grant is { } grant ? Format(grant.ExpiresUtc) : string.Empty;

    public bool HasTools => Model.Grant is { AllowedTools.Count: > 0 };

    public string ToolsText => Model.Grant is { } grant ? string.Join(", ", grant.AllowedTools) : string.Empty;

    public bool HasRoots => Model.Grant is { AllowedRoots.Count: > 0 };

    public string RootsText => Model.Grant is { } grant ? string.Join(Environment.NewLine, grant.AllowedRoots) : string.Empty;

    /// <summary>Local time, Gregorian, digits left-to-right in both languages.</summary>
    private static string Format(DateTimeOffset value) =>
        value.ToLocalTime().ToString(DateFormat, CultureInfo.InvariantCulture);
}

/// <summary>
/// Backs the Tasks page: lists tasks, keeps them current from
/// <see cref="ITaskStore.TaskChanged"/>, and forwards create, resume and
/// cancel to <see cref="TaskAuthoringService"/> (ADR-004 §5).
/// </summary>
/// <remarks>
/// A thin layer: no grant or state rule is decided here. Creating and resuming
/// open the editor, whose submission Core validates, has the user approve and
/// audits; cancelling is Core's compare-and-set or a request to the runner.
/// </remarks>
public sealed partial class TasksViewModel : ObservableObject, IDisposable
{
    private readonly ITaskStore _taskStore;
    private readonly TaskAuthoringService _authoring;
    private readonly ITaskEditorDialog _editorDialog;
    private readonly ILogger<TasksViewModel> _logger;

    // Changes that arrive while a reload is reading the store are replayed
    // after it, so a newer state is never overwritten by an older snapshot.
    private readonly List<HammorTask> _changesDuringLoad = [];

    public TasksViewModel(
        ITaskStore taskStore,
        TaskAuthoringService authoring,
        ITaskEditorDialog editorDialog,
        ILogger<TasksViewModel> logger)
    {
        _taskStore = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
        _authoring = authoring ?? throw new ArgumentNullException(nameof(authoring));
        _editorDialog = editorDialog ?? throw new ArgumentNullException(nameof(editorDialog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Event-driven refresh: the store raises TaskChanged for every write,
        // from whichever thread made it (usually the scheduler's). No polling.
        _taskStore.TaskChanged += OnTaskChanged;
    }

    public ObservableCollection<TaskItemViewModel> Tasks { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    public bool IsEmpty => Tasks.Count == 0 && !IsLoading;

    /// <summary>Outcome of the last action, shown under the page title.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        _changesDuringLoad.Clear();

        try
        {
            var tasks = await _taskStore.ListAsync().ConfigureAwait(true);

            Tasks.Clear();
            foreach (var task in tasks)
            {
                Tasks.Add(new TaskItemViewModel(task));
            }

            foreach (var change in _changesDuringLoad)
            {
                Upsert(change);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load tasks.");
            StatusMessage = LocalizationSource.Instance["Common.Error"];
        }
        finally
        {
            _changesDuringLoad.Clear();
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private async Task NewTaskAsync()
    {
        StatusMessage = null;

        try
        {
            var created = await _editorDialog.ShowCreateAsync().ConfigureAwait(true);
            if (created is not null)
            {
                StatusMessage = LocalizationSource.Instance["Tasks.Message.Created"];
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Creating a task failed.");
            StatusMessage = LocalizationSource.Instance["Common.Error"];
        }
    }

    [RelayCommand]
    private async Task ResumeAsync(TaskItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        StatusMessage = null;

        try
        {
            var resumed = await _editorDialog.ShowResumeAsync(item.Model).ConfigureAwait(true);
            if (resumed is not null)
            {
                StatusMessage = LocalizationSource.Instance["Tasks.Message.Resumed"];
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resuming task {TaskId} failed.", item.Id);
            StatusMessage = LocalizationSource.Instance["Common.Error"];
        }
    }

    [RelayCommand]
    private async Task CancelTaskAsync(TaskItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        StatusMessage = null;

        try
        {
            var result = await _authoring.CancelAsync(item.Id).ConfigureAwait(true);

            StatusMessage = LocalizationSource.Instance[result.Status switch
            {
                TaskAuthoringStatus.Cancelled => "Tasks.Message.Cancelled",
                TaskAuthoringStatus.CancelRequested => "Tasks.Message.CancelRequested",
                _ => "Tasks.Message.CancelNotAllowed",
            }];

            // The store event refreshes the row; a task that could not be
            // cancelled may have changed state meanwhile, so show what is stored.
            if (result.Task is not null)
            {
                Upsert(result.Task);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cancelling task {TaskId} failed.", item.Id);
            StatusMessage = LocalizationSource.Instance["Common.Error"];
        }
    }

    public void Dispose() => _taskStore.TaskChanged -= OnTaskChanged;

    private void OnTaskChanged(object? sender, HammorTask task)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            ApplyChange(task);
        }
        else
        {
            // Never blocks the writer: the scheduler thread queues the update
            // and moves on, so shutdown can wait for the scheduler safely.
            _ = dispatcher.InvokeAsync(() => ApplyChange(task));
        }
    }

    private void ApplyChange(HammorTask task)
    {
        if (IsLoading)
        {
            _changesDuringLoad.Add(task);
            return;
        }

        Upsert(task);
    }

    private void Upsert(HammorTask task)
    {
        var item = new TaskItemViewModel(task);

        for (var i = 0; i < Tasks.Count; i++)
        {
            if (string.Equals(Tasks[i].Id, task.Id, StringComparison.Ordinal))
            {
                Tasks[i] = item;
                return;
            }
        }

        // New tasks are listed newest first, matching the store's order.
        Tasks.Insert(0, item);
        OnPropertyChanged(nameof(IsEmpty));
    }
}
