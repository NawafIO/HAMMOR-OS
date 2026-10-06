using System.Windows;
using HAMMOR.App.ViewModels;
using HAMMOR.App.Views;
using HAMMOR.Core.Projects;
using HAMMOR.Core.Tasks;
using HAMMOR.Core.Tools;
using Microsoft.Extensions.Logging;

namespace HAMMOR.App.Services;

/// <summary>
/// Opens the task editor. Keeps window types out of view models, the same way
/// <see cref="DialogConfirmationService"/> does for approvals.
/// </summary>
public interface ITaskEditorDialog
{
    /// <summary>
    /// Shows the editor for a new task. Returns the created task, or null when
    /// the user cancelled or never approved the grant.
    /// </summary>
    Task<HammorTask?> ShowCreateAsync();

    /// <summary>
    /// Shows the editor for a replacement grant on a Blocked task. Returns the
    /// resumed task, or null when nothing changed.
    /// </summary>
    Task<HammorTask?> ShowResumeAsync(HammorTask blockedTask);
}

/// <summary>Default <see cref="ITaskEditorDialog"/>.</summary>
public sealed class TaskEditorDialogService(
    TaskAuthoringService authoring,
    IToolRegistry toolRegistry,
    IProjectStore projectStore,
    ILogger<TaskEditorDialogService> logger) : ITaskEditorDialog
{
    private readonly TaskAuthoringService _authoring =
        authoring ?? throw new ArgumentNullException(nameof(authoring));

    private readonly IToolRegistry _toolRegistry =
        toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));

    private readonly IProjectStore _projectStore =
        projectStore ?? throw new ArgumentNullException(nameof(projectStore));

    private readonly ILogger<TaskEditorDialogService> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<HammorTask?> ShowCreateAsync()
    {
        IReadOnlyList<HammorProject> projects;
        try
        {
            projects = await _projectStore.ListAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Projects are optional: the task can still be created unscoped.
            _logger.LogWarning(ex, "Could not load projects for the task editor.");
            projects = [];
        }

        return Show(TaskEditorViewModel.ForCreate(_authoring, ReadOnlyToolOptions(), projects));
    }

    public Task<HammorTask?> ShowResumeAsync(HammorTask blockedTask)
    {
        ArgumentNullException.ThrowIfNull(blockedTask);

        return Task.FromResult(
            Show(TaskEditorViewModel.ForResume(_authoring, ReadOnlyToolOptions(), blockedTask)));
    }

    /// <summary>
    /// Only Read tools are offered. This is a convenience, not the boundary:
    /// Core rejects any grant that names a tool needing more than Read.
    /// </summary>
    private List<ToolOption> ReadOnlyToolOptions() =>
        _toolRegistry.All
            .Where(t => t.Permission == ToolPermission.Read)
            .Select(t => new ToolOption(t.Name, t.Description, t is IPathScopedTool))
            .ToList();

    private static HammorTask? Show(TaskEditorViewModel viewModel)
    {
        var application = Application.Current;
        if (application is null)
        {
            // No UI thread (tests, headless host): nothing can be approved.
            return null;
        }

        var window = new TaskEditorWindow(viewModel)
        {
            Owner = application.MainWindow,
        };

        return window.ShowDialog() == true ? viewModel.Result?.Task : null;
    }
}
