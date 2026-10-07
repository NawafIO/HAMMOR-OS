using HAMMOR.App.Localization;
using HAMMOR.App.Shell;
using HAMMOR.App.ViewModels;
using HAMMOR.Core.Memory;
using HAMMOR.Core.Projects;
using HAMMOR.Core.Tasks;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Controls;

namespace HAMMOR.App.Search;

/// <summary>What one search returned.</summary>
/// <param name="Results">Ordered and grouped results.</param>
/// <param name="SomeSourcesFailed">True when a store could not be read; the other sources still answered.</param>
public sealed record SearchOutcome(IReadOnlyList<SearchResult> Results, bool SomeSourcesFailed);

/// <summary>
/// Global search over what HAMMOR can actually search today, read only:
/// <list type="bullet">
/// <item>its pages and the Settings categories;</item>
/// <item>projects (name, description, folder);</item>
/// <item>tasks (title, description; the newest 200);</item>
/// <item>memory, through the store's own keyword search, which also covers
/// every exchange the agent pipeline saved as conversation memory;</item>
/// <item>the messages of the conversation on screen.</item>
/// </list>
/// </summary>
/// <remarks>
/// Nothing is indexed or copied: each search asks the stores. A store that
/// fails is logged and skipped so the rest still answer, and the page says
/// some results may be missing. Memory search is keyword-based (the store's
/// LIKE match); the semantic index does not exist yet.
/// </remarks>
public sealed class SearchService
{
    private const int TaskScanLimit = 200;
    private const int MemoryLimit = 24;
    private const int RecentMemoryScan = 60;

    private readonly IProjectStore _projects;
    private readonly ITaskStore _tasks;
    private readonly IMemoryStore _memory;
    private readonly ChatViewModel _chat;
    private readonly ILogger<SearchService> _logger;

    public SearchService(
        IProjectStore projects,
        ITaskStore tasks,
        IMemoryStore memory,
        ChatViewModel chat,
        ILogger<SearchService> logger)
    {
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Searches every source for the query.</summary>
    public async Task<SearchOutcome> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var text = (query ?? string.Empty).Trim();
        if (SearchText.Normalize(text).Length == 0)
        {
            return await RecentAsync(cancellationToken).ConfigureAwait(true);
        }

        // Read on the calling (UI) thread: the transcript belongs to it.
        var candidates = new List<SearchResult>();
        candidates.AddRange(Pages());
        candidates.AddRange(SettingsSections.All.Select(FromSection));
        candidates.AddRange(_chat.Messages.Where(message => !message.IsError).Select(FromMessage));

        var failed = false;

        var projects = await ReadAsync(() => _projects.ListAsync(false, cancellationToken), "projects", cancellationToken)
            .ConfigureAwait(true);
        failed |= projects is null;
        candidates.AddRange((projects ?? Array.Empty<HammorProject>()).Select(FromProject));

        var tasks = await ReadAsync(() => _tasks.ListAsync(limit: TaskScanLimit, cancellationToken: cancellationToken), "tasks", cancellationToken)
            .ConfigureAwait(true);
        failed |= tasks is null;
        candidates.AddRange((tasks ?? Array.Empty<HammorTask>()).Select(FromTask));

        var memories = await ReadAsync(() => _memory.SearchAsync(text, MemoryLimit, cancellationToken), "memory", cancellationToken)
            .ConfigureAwait(true);
        failed |= memories is null;
        candidates.AddRange((memories ?? Array.Empty<MemoryEntry>()).Select(entry => FromMemory(entry) with { MatchedByStore = true }));

        return new SearchOutcome(SearchComposer.Compose(text, candidates), failed);
    }

    /// <summary>Before anything is typed: recent saved conversations and recent projects.</summary>
    public async Task<SearchOutcome> RecentAsync(CancellationToken cancellationToken = default)
    {
        var recentMemory = await ReadAsync(() => _memory.GetRecentAsync(limit: RecentMemoryScan, cancellationToken: cancellationToken), "memory", cancellationToken)
            .ConfigureAwait(true);
        var projects = await ReadAsync(() => _projects.ListAsync(false, cancellationToken), "projects", cancellationToken)
            .ConfigureAwait(true);

        var conversations = (recentMemory ?? Array.Empty<MemoryEntry>())
            .Where(ConversationExchange.IsExchange)
            .Select(FromMemory);

        return new SearchOutcome(
            SearchComposer.Recent(conversations, (projects ?? Array.Empty<HammorProject>()).Select(FromProject)),
            recentMemory is null || projects is null);
    }

    // ---- Candidates ---------------------------------------------------------

    /// <summary>The shell's pages, by their names in the current language.</summary>
    internal static IEnumerable<SearchResult> Pages() =>
    [
        Page(ShellPage.Chat, "Nav.Chat", SymbolRegular.ChatMultiple24),
        Page(ShellPage.Activity, "Nav.Activity", SymbolRegular.DocumentBulletList24),
        Page(ShellPage.Tasks, "Nav.Tasks", SymbolRegular.TaskListSquareLtr24),
        Page(ShellPage.Memory, "Nav.Memory", SymbolRegular.Brain24),
        Page(ShellPage.Projects, "Nav.Projects", SymbolRegular.FolderOpen24),
        Page(ShellPage.Settings, "Nav.Settings", SymbolRegular.Options24),
    ];

    internal static SearchResult FromSection(SettingsSectionItem section) =>
        new(SearchResultKind.Setting, section.Label, section.Description, section.Icon)
        {
            Page = ShellPage.Settings,
            Section = section.Id,
        };

    internal static SearchResult FromProject(HammorProject project) =>
        new(SearchResultKind.Project,
            project.Name,
            SearchText.Snippet(string.IsNullOrWhiteSpace(project.Description) ? project.RootPath : project.Description),
            IconFor(project.Kind))
        {
            Page = ShellPage.Projects,
            ItemId = project.Id,
            When = project.LastOpenedUtc ?? project.ModifiedUtc,
        };

    internal static SearchResult FromTask(HammorTask task) =>
        new(SearchResultKind.Task,
            task.Title,
            LocalizationSource.Instance[$"Tasks.State.{task.State}"]
                + (string.IsNullOrWhiteSpace(task.Description) ? string.Empty : " · " + SearchText.Snippet(task.Description, 110)),
            SymbolRegular.TaskListSquareLtr24)
        {
            Page = ShellPage.Tasks,
            When = task.CreatedUtc,
        };

    /// <summary>A memory entry; a saved exchange shows its question and answer.</summary>
    internal static SearchResult FromMemory(MemoryEntry entry)
    {
        if (ConversationExchange.IsExchange(entry))
        {
            var exchange = ConversationExchange.Parse(entry);
            return new SearchResult(
                SearchResultKind.Conversation,
                SearchText.Snippet(exchange.Question, 100),
                SearchText.Snippet(exchange.Answer),
                SymbolRegular.Chat24)
            {
                Page = ShellPage.Memory,
                MemoryQuery = entry.Title,
                When = entry.CreatedUtc,
            };
        }

        return new SearchResult(
            SearchResultKind.Memory,
            entry.Title,
            SearchText.Snippet(entry.Content),
            SymbolRegular.Notepad24)
        {
            Page = ShellPage.Memory,
            MemoryQuery = entry.Title,
            When = entry.ModifiedUtc,
        };
    }

    internal static SearchResult FromMessage(ChatMessageViewModel message) =>
        new(SearchResultKind.ChatMessage, SearchText.Snippet(message.Text, 100), message.AuthorLabel, SymbolRegular.Chat24)
        {
            Page = ShellPage.Chat,
            When = message.Timestamp,
        };

    internal static SymbolRegular IconFor(ProjectKind kind) => kind switch
    {
        ProjectKind.Software => SymbolRegular.Code24,
        ProjectKind.Research => SymbolRegular.BookOpen24,
        _ => SymbolRegular.Folder24,
    };

    private static SearchResult Page(ShellPage page, string labelKey, SymbolRegular icon) =>
        new(SearchResultKind.Page, LocalizationSource.Instance[labelKey], string.Empty, icon) { Page = page };

    /// <summary>Reads one source; null when it failed (logged), so the others still answer.</summary>
    private async Task<IReadOnlyList<T>?> ReadAsync<T>(
        Func<Task<IReadOnlyList<T>>> read,
        string source,
        CancellationToken cancellationToken)
    {
        try
        {
            return await read().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Search could not read {Source}.", source);
            return null;
        }
    }
}
