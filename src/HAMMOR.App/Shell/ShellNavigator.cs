using HAMMOR.App.Search;
using HAMMOR.App.ViewModels;
using HAMMOR.Core.Projects;

namespace HAMMOR.App.Shell;

/// <summary>
/// Moves around the shell on behalf of Search and Projects: prepares the
/// destination's view model (the Settings category, the project to select,
/// the memory search, the chat's project), then shows the page.
/// </summary>
/// <remarks>
/// MainWindow attaches the actual navigation, so this class knows pages only
/// as <see cref="ShellPage"/> values. Every destination's view model is a
/// singleton that outlives its transient page, which is what lets the page
/// pick the prepared state up when it loads.
/// </remarks>
public sealed class ShellNavigator
{
    private readonly SettingsViewModel _settings;
    private readonly ProjectsViewModel _projects;
    private readonly MemoryViewModel _memory;
    private readonly ChatViewModel _chat;

    private Action<ShellPage>? _navigate;

    public ShellNavigator(
        SettingsViewModel settings,
        ProjectsViewModel projects,
        MemoryViewModel memory,
        ChatViewModel chat)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
    }

    /// <summary>Called once by the shell window.</summary>
    public void Attach(Action<ShellPage> navigate) =>
        _navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));

    public void Go(ShellPage page) => _navigate?.Invoke(page);

    /// <summary>Opens a search result where it lives.</summary>
    public void Open(SearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        switch (result.Kind)
        {
            case SearchResultKind.Setting when result.Section is { } section:
                _settings.SelectedSection = section;
                break;
            case SearchResultKind.Project:
                _projects.PendingSelectionId = result.ItemId;
                break;
            case SearchResultKind.Conversation or SearchResultKind.Memory:
                _memory.PendingQuery = result.MemoryQuery;
                break;
        }

        Go(result.Page);
    }

    /// <summary>Continues the conversation on screen, scoped to a project.</summary>
    public void ChatInProject(HammorProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        _chat.ActiveProject = project;
        Go(ShellPage.Chat);
    }
}
