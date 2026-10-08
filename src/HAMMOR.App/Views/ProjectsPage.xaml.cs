using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using HAMMOR.App.Shell;
using HAMMOR.App.ViewModels;

namespace HAMMOR.App.Views;

/// <summary>Projects: the list and the selected project's details.</summary>
/// <remarks>
/// The project folder is copied, never opened: starting a stored path
/// through the shell would run whatever it points at.
/// </remarks>
public partial class ProjectsPage : Page
{
    private readonly ProjectsViewModel _viewModel;
    private readonly ShellNavigator _navigator;

    public ProjectsPage(ProjectsViewModel viewModel, ShellNavigator navigator)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));

        DataContext = viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await viewModel.LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Continues the conversation on screen, scoped to this project.</summary>
    private void OnChatHereClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedProject is { } project)
        {
            _navigator.ChatInProject(project);
        }
    }

    private void OnOpenTasksClick(object sender, RoutedEventArgs e) => _navigator.Go(ShellPage.Tasks);

    private void OnCopyPathClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.SelectedProject?.RootPath))
        {
            return;
        }

        try
        {
            Clipboard.SetText(_viewModel.SelectedProject.RootPath);
        }
        catch (ExternalException)
        {
            // Another program holds the clipboard; copying again will work.
        }
    }
}
