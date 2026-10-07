using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using HAMMOR.App.Search;
using HAMMOR.App.Shell;
using HAMMOR.App.ViewModels;

namespace HAMMOR.App.Views;

/// <summary>Global Search.</summary>
public partial class SearchPage : Page
{
    private readonly SearchViewModel _viewModel;
    private readonly ShellNavigator _navigator;

    public SearchPage(SearchViewModel viewModel, ShellNavigator navigator)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));

        DataContext = viewModel;
        InitializeComponent();

        // Group by what each result is. The view model is a singleton, so its
        // collection's default view outlives this page: add the grouping once.
        var view = CollectionViewSource.GetDefaultView(viewModel.Results);
        if (view.GroupDescriptions.Count == 0)
        {
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SearchResult.GroupLabel)));
        }

        Loaded += async (_, _) =>
        {
            FocusQuery();
            await viewModel.OpenAsync().ConfigureAwait(true);
        };
    }

    /// <summary>Puts the caret in the box, with its text selected for retyping.</summary>
    public void FocusQuery() =>
        _ = Dispatcher.InvokeAsync(
            () =>
            {
                QueryBox.Focus();
                QueryBox.SelectAll();
            },
            DispatcherPriority.Input);

    /// <summary>Arrows move through results, Enter opens one, Escape clears the box.</summary>
    private void OnQueryKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
            case Key.Up:
                _viewModel.MoveSelection(e.Key == Key.Down ? 1 : -1);
                if (_viewModel.SelectedResult is { } selected)
                {
                    ResultsList.ScrollIntoView(selected);
                }

                e.Handled = true;
                break;
            case Key.Enter:
                Open(_viewModel.SelectedResult ?? _viewModel.Results.FirstOrDefault());
                e.Handled = true;
                break;
            case Key.Escape when _viewModel.HasQuery:
                _viewModel.Query = string.Empty;
                e.Handled = true;
                break;
        }
    }

    private void OnResultsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Open(_viewModel.SelectedResult);
            e.Handled = true;
        }
    }

    private void OnResultClicked(object sender, MouseButtonEventArgs e) =>
        Open((sender as FrameworkElement)?.DataContext as SearchResult);

    private void Open(SearchResult? result)
    {
        if (result is not null)
        {
            _navigator.Open(result);
        }
    }
}
