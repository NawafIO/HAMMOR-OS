using System.Windows.Controls;
using HAMMOR.App.ViewModels;

namespace HAMMOR.App.Views;

/// <summary>Audit trail page.</summary>
public partial class ActivityPage : Page
{
    public ActivityPage(ActivityViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        DataContext = viewModel;
        InitializeComponent();

        // Pages are transient, so load on construction: navigating here always
        // shows current data without needing an explicit refresh.
        Loaded += async (_, _) => await viewModel.LoadAsync().ConfigureAwait(true);
    }
}

/// <summary>Task list page.</summary>
public partial class TasksPage : Page
{
    public TasksPage(TasksViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        DataContext = viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await viewModel.LoadAsync().ConfigureAwait(true);
    }
}

/// <summary>Memory browser and search page.</summary>
public partial class MemoryPage : Page
{
    public MemoryPage(MemoryViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        DataContext = viewModel;
        InitializeComponent();

        // A result opened from Search arrives as a pending search.
        Loaded += async (_, _) => await viewModel.OpenAsync().ConfigureAwait(true);
    }
}
