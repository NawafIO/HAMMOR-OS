using System.Windows;
using HAMMOR.App.Localization;
using HAMMOR.App.ViewModels;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace HAMMOR.App.Views;

/// <summary>
/// Modal editor for a new unattended task or a replacement grant.
/// </summary>
/// <remarks>
/// The folder picker lives in code-behind because it is a view concern; the
/// chosen paths are handed to the view model as plain strings and validated
/// by Core. The window closes only when the view model reports completion, so
/// a refused approval or a validation error keeps the form open for changes.
/// </remarks>
public partial class TaskEditorWindow : FluentWindow
{
    public TaskEditorWindow(TaskEditorViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        DataContext = viewModel;
        InitializeComponent();

        ViewModel.CompletionRequested += OnCompletionRequested;
    }

    public TaskEditorViewModel ViewModel { get; }

    private void OnCompletionRequested(object? sender, bool completed)
    {
        DialogResult = completed;
        Close();
    }

    /// <summary>Windows folder picker; several folders may be added at once.</summary>
    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Multiselect = true,
            Title = LocalizationSource.Instance["TaskEditor.AddFolder"],
        };

        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.AddRoots(dialog.FolderNames);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.CompletionRequested -= OnCompletionRequested;
        base.OnClosed(e);
    }
}
