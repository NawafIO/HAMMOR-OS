using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HAMMOR.App.Localization;
using HAMMOR.Core.Projects;
using HAMMOR.Core.Tasks;

namespace HAMMOR.App.ViewModels;

/// <summary>A tool the user may grant to an unattended task.</summary>
public sealed partial class ToolOption : ObservableObject
{
    public ToolOption(string name, string description, bool isPathScoped)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? string.Empty;
        IsPathScoped = isPathScoped;
    }

    public string Name { get; }

    public string Description { get; }

    /// <summary>
    /// Whether the tool touches the filesystem. Only decides whether the folder
    /// picker is shown; Core enforces that such tools need roots.
    /// </summary>
    public bool IsPathScoped { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>A project the task may be scoped to; a null id means none.</summary>
public sealed record ProjectOption(string? Id, string Name);

/// <summary>
/// Collects a new unattended task, or a replacement grant for a Blocked task,
/// and submits it to <see cref="TaskAuthoringService"/> (ADR-004 §3, §5).
/// </summary>
/// <remarks>
/// Deliberately thin. It only turns form input into a draft: the date and time
/// fields must parse before a draft can exist. Every rule about what may be
/// granted (Read-only tools, roots, 30-day expiry, call cap, schedule before
/// expiry) is decided by Core, which validates the draft, asks the user to
/// approve the exact grant through the confirmation dialog, and audits it. Core
/// errors are shown as returned. The UI is not a security boundary.
/// </remarks>
public sealed partial class TaskEditorViewModel : ObservableObject
{
    private const string TimeFormat = "HH:mm";

    private static readonly string[] TimeInputFormats = [@"hh\:mm", @"h\:mm"];

    private readonly TaskAuthoringService _authoring;
    private readonly HammorTask? _resumeTarget;

    private TaskEditorViewModel(
        TaskAuthoringService authoring,
        IEnumerable<ToolOption> tools,
        HammorTask? resumeTarget)
    {
        _authoring = authoring ?? throw new ArgumentNullException(nameof(authoring));
        _resumeTarget = resumeTarget;

        Tools = new ObservableCollection<ToolOption>(tools ?? throw new ArgumentNullException(nameof(tools)));
        foreach (var tool in Tools)
        {
            tool.PropertyChanged += OnToolChanged;
        }

        Errors.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasErrors));
        Roots.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRoots));

        // The picker range is a convenience matching Core's 30-day limit; Core
        // still rejects anything beyond it.
        var now = DateTime.Now;
        MinDate = now.Date;
        MaxDate = now.Date.AddDays(TaskGrantValidator.MaxLifetime.TotalDays);

        ExpiryDate = now.Date.AddDays(1);
        ExpiryTime = now.ToString(TimeFormat, CultureInfo.InvariantCulture);
        ScheduleDate = now.Date;
        ScheduleTime = now.AddHours(1).ToString(TimeFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>Editor for a brand-new task.</summary>
    public static TaskEditorViewModel ForCreate(
        TaskAuthoringService authoring,
        IEnumerable<ToolOption> tools,
        IEnumerable<HammorProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);

        var editor = new TaskEditorViewModel(authoring, tools, resumeTarget: null);

        editor.Projects.Add(new ProjectOption(null, LocalizationSource.Instance["TaskEditor.NoProject"]));
        foreach (var project in projects)
        {
            editor.Projects.Add(new ProjectOption(project.Id, project.Name));
        }

        editor.SelectedProject = editor.Projects[0];
        return editor;
    }

    /// <summary>
    /// Editor for a replacement grant. The form is pre-filled from the blocked
    /// task's grant for convenience only: Core always creates a new grant with
    /// a new id that supersedes the old one, and never accepts the old grant.
    /// </summary>
    public static TaskEditorViewModel ForResume(
        TaskAuthoringService authoring,
        IEnumerable<ToolOption> tools,
        HammorTask blockedTask)
    {
        ArgumentNullException.ThrowIfNull(blockedTask);

        var editor = new TaskEditorViewModel(authoring, tools, blockedTask)
        {
            Title = blockedTask.Title,
            Prompt = blockedTask.Prompt ?? string.Empty,
        };

        if (blockedTask.Grant is { } previous)
        {
            foreach (var tool in editor.Tools)
            {
                tool.IsSelected = previous.AllowedTools.Contains(tool.Name, StringComparer.Ordinal);
            }

            editor.AddRoots(previous.AllowedRoots);
            editor.MaxToolCalls = previous.MaxToolCalls;
        }

        return editor;
    }

    /// <summary>Raised with true when a task was created or resumed, false when the user cancelled.</summary>
    public event EventHandler<bool>? CompletionRequested;

    public bool IsResume => _resumeTarget is not null;

    public bool IsCreate => _resumeTarget is null;

    public string WindowTitle =>
        LocalizationSource.Instance[IsResume ? "TaskEditor.ResumeTitle" : "TaskEditor.CreateTitle"];

    public string SubmitLabel =>
        LocalizationSource.Instance[IsResume ? "TaskEditor.SubmitResume" : "TaskEditor.SubmitCreate"];

    public string? BlockedReason => _resumeTarget?.BlockedReason;

    public bool HasBlockedReason => !string.IsNullOrWhiteSpace(BlockedReason);

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _prompt = string.Empty;

    /// <summary>Registered tools that need only Read permission.</summary>
    public ObservableCollection<ToolOption> Tools { get; }

    public bool HasTools => Tools.Count > 0;

    public ObservableCollection<string> Roots { get; } = [];

    public bool HasRoots => Roots.Count > 0;

    /// <summary>True when a selected tool touches the filesystem, so folders must be chosen.</summary>
    public bool NeedsRoots => Tools.Any(t => t.IsSelected && t.IsPathScoped);

    public DateTime MinDate { get; }

    public DateTime MaxDate { get; }

    [ObservableProperty]
    private DateTime? _expiryDate;

    [ObservableProperty]
    private string _expiryTime = string.Empty;

    [ObservableProperty]
    private int _maxToolCalls = 10;

    [ObservableProperty]
    private bool _isScheduled;

    [ObservableProperty]
    private DateTime? _scheduleDate;

    [ObservableProperty]
    private string _scheduleTime = string.Empty;

    public ObservableCollection<ProjectOption> Projects { get; } = [];

    /// <summary>Only offered when at least one project exists besides "none".</summary>
    public bool HasProjects => IsCreate && Projects.Count > 1;

    [ObservableProperty]
    private ProjectOption? _selectedProject;

    /// <summary>Errors returned by Core, or input that could not be read.</summary>
    public ObservableCollection<string> Errors { get; } = [];

    public bool HasErrors => Errors.Count > 0;

    /// <summary>Shown when the user did not approve the grant.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string? _notice;

    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);

    /// <summary>Outcome of the last submission, when there was one.</summary>
    public TaskAuthoringResult? Result { get; private set; }

    /// <summary>Adds folders chosen in the folder picker. Validation is Core's.</summary>
    public void AddRoots(IEnumerable<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        foreach (var folder in folders)
        {
            var trimmed = folder?.Trim();
            if (string.IsNullOrEmpty(trimmed)
                || Roots.Any(r => string.Equals(r, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            Roots.Add(trimmed);
        }
    }

    [RelayCommand]
    private void RemoveRoot(string? root)
    {
        if (root is not null)
        {
            Roots.Remove(root);
        }
    }

    [RelayCommand]
    private void Cancel() => CompletionRequested?.Invoke(this, false);

    [RelayCommand]
    private async Task SubmitAsync()
    {
        Errors.Clear();
        Notice = null;

        var expiry = ReadDateTime(ExpiryDate, ExpiryTime, "TaskEditor.ExpiryRequired");

        DateTimeOffset? schedule = null;
        if (IsCreate && IsScheduled)
        {
            schedule = ReadDateTime(ScheduleDate, ScheduleTime, "TaskEditor.ScheduleRequired");
        }

        if (Errors.Count > 0 || expiry is null)
        {
            return;
        }

        var grant = new GrantDraft
        {
            AllowedTools = Tools.Where(t => t.IsSelected).Select(t => t.Name).ToList(),

            // Folders are only sent while a filesystem tool is selected, so a
            // hidden list never ends up in a grant.
            AllowedRoots = NeedsRoots ? Roots.ToList() : new List<string>(),
            ExpiresUtc = expiry.Value,
            MaxToolCalls = MaxToolCalls,
        };

        TaskAuthoringResult result;
        try
        {
            result = _resumeTarget is null
                ? await _authoring.CreateAsync(new TaskDraft
                {
                    Title = Title,
                    Prompt = Prompt,
                    ScheduledForUtc = schedule,
                    ProjectId = SelectedProject?.Id,
                    Grant = grant,
                }).ConfigureAwait(true)
                : await _authoring.ResumeAsync(_resumeTarget.Id, grant).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Errors.Add($"{LocalizationSource.Instance["Common.Error"]}: {ex.Message}");
            return;
        }

        Result = result;

        if (result.Succeeded)
        {
            CompletionRequested?.Invoke(this, true);
            return;
        }

        if (result.Status == TaskAuthoringStatus.Refused)
        {
            Notice = LocalizationSource.Instance[IsResume ? "TaskEditor.RefusedResume" : "TaskEditor.Refused"];
            return;
        }

        foreach (var error in result.Errors)
        {
            Errors.Add(error);
        }

        if (Errors.Count == 0)
        {
            Errors.Add(LocalizationSource.Instance["Common.Error"]);
        }
    }

    /// <summary>
    /// Combines a picked date with an HH:mm time as local time. Only checks
    /// that the input can be read; whether the moment is allowed is Core's call.
    /// </summary>
    private DateTimeOffset? ReadDateTime(DateTime? date, string? time, string missingDateKey)
    {
        if (date is null)
        {
            Errors.Add(LocalizationSource.Instance[missingDateKey]);
            return null;
        }

        if (!TimeSpan.TryParseExact(
                (time ?? string.Empty).Trim(),
                TimeInputFormats,
                CultureInfo.InvariantCulture,
                out var timeOfDay))
        {
            Errors.Add(LocalizationSource.Instance["TaskEditor.InvalidTime"]);
            return null;
        }

        var local = DateTime.SpecifyKind(date.Value.Date + timeOfDay, DateTimeKind.Local);
        return new DateTimeOffset(local);
    }

    private void OnToolChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolOption.IsSelected))
        {
            OnPropertyChanged(nameof(NeedsRoots));
        }
    }
}
