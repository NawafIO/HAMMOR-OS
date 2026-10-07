using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HAMMOR.App.Localization;
using HAMMOR.App.Search;
using HAMMOR.Core.Memory;
using HAMMOR.Core.Projects;
using HAMMOR.Core.Tasks;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Controls;

namespace HAMMOR.App.ViewModels;

/// <summary>A row of the project list: a project, or "All conversations".</summary>
/// <param name="Project">The project, or null for the all-conversations row.</param>
/// <param name="Name">Its name.</param>
/// <param name="Detail">A short second line.</param>
/// <param name="Icon">Its icon.</param>
public sealed record ProjectListItem(HammorProject? Project, string Name, string Detail, SymbolRegular Icon)
{
    public bool IsAllConversations => Project is null;

    public bool IsArchived => Project?.IsArchived == true;

    /// <summary>The all-conversations row, in the current language.</summary>
    public static ProjectListItem AllConversations() =>
        new(null,
            LocalizationSource.Instance["Projects.All"],
            LocalizationSource.Instance["Projects.All.Detail"],
            SymbolRegular.History24);

    public static ProjectListItem For(HammorProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return new ProjectListItem(
            project,
            project.Name,
            SearchText.Snippet(string.IsNullOrWhiteSpace(project.Description)
                ? LocalizationSource.Instance[$"Projects.Kind.{project.Kind}"]
                : project.Description, 70),
            SearchService.IconFor(project.Kind));
    }
}

/// <summary>A saved exchange as the Projects page shows it.</summary>
public sealed record ExchangeItem(ConversationExchange Exchange, string? ProjectName)
{
    public string Question => SearchText.Snippet(Exchange.Question, 160);

    public string Answer => SearchText.Snippet(Exchange.Answer, 280);

    public string WhenLabel => Exchange.When.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public bool HasProject => !string.IsNullOrEmpty(ProjectName);
}

/// <summary>A non-conversation memory entry of a project.</summary>
public sealed record NoteItem(MemoryEntry Entry)
{
    public string Title => Entry.Title;

    public string Snippet => SearchText.Snippet(Entry.Content, 200);

    public string KindLabel => LocalizationSource.Instance[$"Projects.MemoryKind.{Entry.Kind}"];
}

/// <summary>A task of a project.</summary>
public sealed record ProjectTaskItem(HammorTask Task)
{
    public string Title => Task.Title;

    public TaskState State => Task.State;

    public string StateLabel => LocalizationSource.Instance[$"Tasks.State.{Task.State}"];
}

/// <summary>
/// Sorting a project's memory into saved exchanges and other notes. Pure.
/// </summary>
public static class ProjectActivity
{
    /// <summary>
    /// Saved exchanges and other notes, each newest first. The pipeline
    /// writes one conversation entry per successful turn; everything else in
    /// a project's memory is a note.
    /// </summary>
    public static (IReadOnlyList<ConversationExchange> Exchanges, IReadOnlyList<MemoryEntry> Notes) Split(
        IEnumerable<MemoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var ordered = entries.OrderByDescending(entry => entry.CreatedUtc).ToList();
        return (
            ordered.Where(ConversationExchange.IsExchange).Select(ConversationExchange.Parse).ToList(),
            ordered.Where(entry => !ConversationExchange.IsExchange(entry)).ToList());
    }

    /// <summary>The list: "All conversations" first, then projects as the store orders them (most recently opened).</summary>
    public static IReadOnlyList<ProjectListItem> BuildList(IEnumerable<HammorProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);

        return [ProjectListItem.AllConversations(), .. projects.Select(ProjectListItem.For)];
    }
}

/// <summary>
/// Projects: a list of projects with "All conversations" on top, and the
/// selected one's details, saved exchanges, notes and tasks.
/// </summary>
/// <remarks>
/// Read only, from data HAMMOR already keeps: the project store, the memory
/// store (where every interactive turn is saved as a conversation entry with
/// its project) and the task store. Creating and editing projects is not
/// built yet, and the page says so.
/// </remarks>
public sealed partial class ProjectsViewModel : ObservableObject
{
    private const int MemoryLimit = 100;
    private const int TaskLimit = 50;

    private readonly IProjectStore _projectStore;
    private readonly IMemoryStore _memoryStore;
    private readonly ITaskStore _taskStore;
    private readonly ILogger<ProjectsViewModel> _logger;

    private CancellationTokenSource? _detailLoad;
    private Dictionary<string, string> _projectNames = new(StringComparer.Ordinal);

    public ProjectsViewModel(
        IProjectStore projectStore,
        IMemoryStore memoryStore,
        ITaskStore taskStore,
        ILogger<ProjectsViewModel> logger)
    {
        _projectStore = projectStore ?? throw new ArgumentNullException(nameof(projectStore));
        _memoryStore = memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));
        _taskStore = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ObservableCollection<ProjectListItem> Items { get; } = [];

    public ObservableCollection<ExchangeItem> Exchanges { get; } = [];

    public ObservableCollection<NoteItem> Notes { get; } = [];

    public ObservableCollection<ProjectTaskItem> Tasks { get; } = [];

    /// <summary>A project to select on the next load, set by global Search. Used once.</summary>
    public string? PendingSelectionId { get; set; }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isDetailLoading;

    [ObservableProperty]
    private bool _loadFailed;

    [ObservableProperty]
    private bool _showArchived;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedProject))]
    [NotifyPropertyChangedFor(nameof(IsProjectSelected))]
    [NotifyPropertyChangedFor(nameof(IsAllConversationsSelected))]
    [NotifyPropertyChangedFor(nameof(KindLabel))]
    [NotifyPropertyChangedFor(nameof(DatesLabel))]
    [NotifyPropertyChangedFor(nameof(HasContext))]
    [NotifyPropertyChangedFor(nameof(HasRootPath))]
    private ProjectListItem? _selectedItem;

    /// <summary>True when at least one real project exists.</summary>
    public bool HasProjects => Items.Any(item => !item.IsAllConversations);

    public HammorProject? SelectedProject => SelectedItem?.Project;

    public bool IsProjectSelected => SelectedProject is not null;

    public bool IsAllConversationsSelected => SelectedItem?.IsAllConversations == true;

    public string KindLabel => SelectedProject is { } project
        ? LocalizationSource.Instance[$"Projects.Kind.{project.Kind}"]
        : string.Empty;

    public string DatesLabel
    {
        get
        {
            if (SelectedProject is not { } project)
            {
                return string.Empty;
            }

            var created = string.Format(
                CultureInfo.CurrentCulture,
                LocalizationSource.Instance["Projects.Created"],
                project.CreatedUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture));

            return project.LastOpenedUtc is { } opened
                ? created + " · " + string.Format(
                    CultureInfo.CurrentCulture,
                    LocalizationSource.Instance["Projects.LastOpened"],
                    opened.ToLocalTime().ToString("d", CultureInfo.CurrentCulture))
                : created;
        }
    }

    public bool HasContext => !string.IsNullOrWhiteSpace(SelectedProject?.Context);

    public bool HasRootPath => !string.IsNullOrWhiteSpace(SelectedProject?.RootPath);

    public bool HasExchanges => Exchanges.Count > 0;

    public bool HasNotes => Notes.Count > 0;

    public bool HasTasks => Tasks.Count > 0;

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        LoadFailed = false;
        var keep = PendingSelectionId ?? SelectedItem?.Project?.Id;
        PendingSelectionId = null;

        try
        {
            var projects = await _projectStore.ListAsync(ShowArchived).ConfigureAwait(true);
            _projectNames = projects.ToDictionary(project => project.Id, project => project.Name, StringComparer.Ordinal);

            Items.Clear();
            foreach (var item in ProjectActivity.BuildList(projects))
            {
                Items.Add(item);
            }

            SelectedItem = Items.FirstOrDefault(item => item.Project?.Id == keep && keep is not null)
                           ?? Items.First();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load projects.");
            LoadFailed = true;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasProjects));
        }
    }

    partial void OnShowArchivedChanged(bool value) => _ = LoadAsync();

    partial void OnSelectedItemChanged(ProjectListItem? value) => _ = LoadDetailAsync(value);

    private async Task LoadDetailAsync(ProjectListItem? item)
    {
        _detailLoad?.Cancel();
        _detailLoad?.Dispose();
        var cancellation = new CancellationTokenSource();
        _detailLoad = cancellation;

        Exchanges.Clear();
        Notes.Clear();
        Tasks.Clear();
        RaiseCounts();

        if (item is null)
        {
            return;
        }

        IsDetailLoading = true;
        try
        {
            var projectId = item.Project?.Id;
            var memory = await _memoryStore
                .GetRecentAsync(projectId, MemoryLimit, cancellation.Token)
                .ConfigureAwait(true);
            var tasks = projectId is null
                ? Array.Empty<HammorTask>()
                : await _taskStore
                    .ListAsync(projectId: projectId, limit: TaskLimit, cancellationToken: cancellation.Token)
                    .ConfigureAwait(true);

            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            var (exchanges, notes) = ProjectActivity.Split(memory);
            foreach (var exchange in exchanges)
            {
                var name = exchange.ProjectId is { } id && _projectNames.TryGetValue(id, out var found) ? found : null;
                Exchanges.Add(new ExchangeItem(exchange, projectId is null ? name : null));
            }

            // "All conversations" shows exchanges only; notes belong to a project.
            if (projectId is not null)
            {
                foreach (var note in notes)
                {
                    Notes.Add(new NoteItem(note));
                }
            }

            foreach (var task in tasks)
            {
                Tasks.Add(new ProjectTaskItem(task));
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Another selection replaced this one.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load the details of a project.");
            LoadFailed = true;
        }
        finally
        {
            if (ReferenceEquals(_detailLoad, cancellation))
            {
                IsDetailLoading = false;
            }

            RaiseCounts();
        }
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(HasExchanges));
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(HasTasks));
    }
}
