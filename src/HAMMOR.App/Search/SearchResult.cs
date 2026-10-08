using HAMMOR.App.Localization;
using HAMMOR.App.Shell;
using HAMMOR.App.ViewModels;
using Wpf.Ui.Controls;

namespace HAMMOR.App.Search;

/// <summary>What a search result is, which also decides its group and order.</summary>
public enum SearchResultKind
{
    Page = 0,
    Setting = 1,
    Project = 2,
    Conversation = 3,
    Memory = 4,
    Task = 5,
    ChatMessage = 6,
}

/// <summary>One search result and where opening it leads.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Title">Its main line.</param>
/// <param name="Detail">A short second line, possibly empty.</param>
/// <param name="Icon">Its icon.</param>
public sealed record SearchResult(SearchResultKind Kind, string Title, string Detail, SymbolRegular Icon)
{
    /// <summary>The page that opens it.</summary>
    public ShellPage Page { get; init; }

    /// <summary>For a setting: the category to show.</summary>
    public SettingsSection? Section { get; init; }

    /// <summary>For a project: its id, so the Projects page can select it.</summary>
    public string? ItemId { get; init; }

    /// <summary>For memory: the text to search the Memory page for.</summary>
    public string? MemoryQuery { get; init; }

    /// <summary>When it happened or last changed, if known; newer ranks first among equals.</summary>
    public DateTimeOffset? When { get; init; }

    /// <summary>
    /// True when the store itself matched it (memory search runs in the
    /// database on the raw text), so it is kept even if the folded comparison
    /// here would not have matched.
    /// </summary>
    public bool MatchedByStore { get; init; }

    /// <summary>How well it matched; see <see cref="SearchText"/>.</summary>
    public int Score { get; init; }

    /// <summary>The group heading, in the current language.</summary>
    public string GroupLabel => LocalizationSource.Instance[GroupKey(Kind)];

    /// <summary>The localisation key of a kind's group heading.</summary>
    public static string GroupKey(SearchResultKind kind) => kind switch
    {
        SearchResultKind.Page => "Search.Group.Pages",
        SearchResultKind.Setting => "Search.Group.Settings",
        SearchResultKind.Project => "Search.Group.Projects",
        SearchResultKind.Conversation => "Search.Group.Conversations",
        SearchResultKind.Memory => "Search.Group.Memory",
        SearchResultKind.Task => "Search.Group.Tasks",
        _ => "Search.Group.ThisChat",
    };
}
