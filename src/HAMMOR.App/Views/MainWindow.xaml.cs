using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HAMMOR.App.Localization;
using HAMMOR.App.Presence;
using HAMMOR.App.Shell;
using HAMMOR.App.Themes;
using HAMMOR.App.ViewModels;
using HAMMOR.Core.Localization;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace HAMMOR.App.Views;

/// <summary>Application shell.</summary>
public partial class MainWindow : FluentWindow
{
    /// <summary>Opens or closes the sidebar (Ctrl+B).</summary>
    public static readonly RoutedUICommand ToggleSidebarCommand =
        new("Toggle sidebar", nameof(ToggleSidebarCommand), typeof(MainWindow));

    /// <summary>Opens Search, or puts the caret back in its box (Ctrl+K).</summary>
    public static readonly RoutedUICommand OpenSearchCommand =
        new("Search", nameof(OpenSearchCommand), typeof(MainWindow));

    private static readonly Dictionary<ShellPage, Type> PageTypes = new()
    {
        [ShellPage.Chat] = typeof(ChatPage),
        [ShellPage.Search] = typeof(SearchPage),
        [ShellPage.Activity] = typeof(ActivityPage),
        [ShellPage.Tasks] = typeof(TasksPage),
        [ShellPage.Memory] = typeof(MemoryPage),
        [ShellPage.Projects] = typeof(ProjectsPage),
        [ShellPage.Settings] = typeof(SettingsPage),
    };

    private readonly ILocalizationService _localization;
    private readonly LivingCorePresenter _presence;
    private readonly ShellLayoutStore _layoutStore;
    private readonly BlockedTaskTracker _blockedTasks;

    private Type _currentPageType = typeof(ChatPage);
    private FrameworkElement? _currentPage;

    // Settings v2: the page Settings was opened from, whether Settings is
    // showing, and guards so the sidebar's resting state while in Settings is
    // never saved as the user's choice.
    private Type _pageBeforeSettings = typeof(ChatPage);
    private bool _inSettings;
    private bool _rebuilding;
    private bool _adjustingPane;

    public MainWindow(
        ShellViewModel viewModel,
        INavigationViewPageProvider pageProvider,
        ILocalizationService localization,
        LivingCorePresenter presence,
        ShellLayoutStore layoutStore,
        BlockedTaskTracker blockedTasks,
        ShellNavigator navigator)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(pageProvider);

        ViewModel = viewModel;
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _presence = presence ?? throw new ArgumentNullException(nameof(presence));
        _layoutStore = layoutStore ?? throw new ArgumentNullException(nameof(layoutStore));
        _blockedTasks = blockedTasks ?? throw new ArgumentNullException(nameof(blockedTasks));
        ArgumentNullException.ThrowIfNull(navigator);

        // The view model is the DataContext, not the window: binding the
        // window's own FlowDirection to a DataContext of `this` would be
        // self-referential.
        DataContext = viewModel;

        InitializeComponent();

        // Lets NavigationView resolve TargetPageType instances from DI so each
        // page receives its view model by constructor injection.
        RootNavigation.SetPageProviderService(pageProvider);

        RootNavigation.Navigated += OnNavigated;
        _localization.LanguageChanged += OnLanguageChanged;

        // The sidebar opens the way the user left it. The handlers come after
        // the restore so that putting it back is not saved again, and they
        // cover every way the pane changes: its toggle button and Ctrl+B.
        RootNavigation.SetCurrentValue(NavigationView.IsPaneOpenProperty, !_layoutStore.Load().SidebarCollapsed);
        RootNavigation.PaneOpened += OnPaneStateChanged;
        RootNavigation.PaneClosed += OnPaneStateChanged;
        CommandBindings.Add(new CommandBinding(ToggleSidebarCommand, OnToggleSidebarExecuted));
        InputBindings.Add(new KeyBinding(ToggleSidebarCommand, Key.B, ModifierKeys.Control));
        CommandBindings.Add(new CommandBinding(OpenSearchCommand, OnOpenSearchExecuted));
        InputBindings.Add(new KeyBinding(OpenSearchCommand, Key.K, ModifierKeys.Control));

        // Search and Projects move around the shell through the navigator.
        navigator.Attach(page => RootNavigation.Navigate(PageTypes[page]));

        // Blocked tasks put a dot on the Tasks row.
        _blockedTasks.CountChanged += OnBlockedTasksChanged;

        Loaded += OnLoaded;
        Closed += OnWindowClosed;
    }

    public ShellViewModel ViewModel { get; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        PaintClearColour();
        ApplicationThemeManager.Changed += OnThemeChanged;
    }

    /// <summary>
    /// WPF-UI resets the window's clear colour while it applies a theme; set
    /// the shell's colour again once it has finished.
    /// </summary>
    private void OnThemeChanged(ApplicationTheme currentApplicationTheme, Color systemAccent) =>
        _ = Dispatcher.InvokeAsync(PaintClearColour, DispatcherPriority.Background);

    /// <summary>
    /// The colour DirectX clears the window to before drawing: the shell
    /// surface, so a resize never flashes the system window colour at the
    /// edges.
    /// </summary>
    private void PaintClearColour()
    {
        if (PresentationSource.FromVisual(this) is HwndSource { CompositionTarget: { } target }
            && TryFindResource(ShellSurfaces.ShellBackgroundKey) is SolidColorBrush shell)
        {
            target.BackgroundColor = shell.Color;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Open on Chat so the app lands somewhere useful rather than on an
        // empty navigation host.
        RootNavigation.Navigate(typeof(ChatPage));

        // Reads the tasks that are already Blocked, then follows the store.
        _ = _blockedTasks.StartAsync();
    }

    private void OnPaneStateChanged(NavigationView sender, RoutedEventArgs args)
    {
        if (!_adjustingPane)
        {
            _layoutStore.Save(new ShellLayout { SidebarCollapsed = !RootNavigation.IsPaneOpen });
        }
    }

    /// <summary>Leaves Settings for the page it was opened from.</summary>
    public void LeaveSettings() => RootNavigation.Navigate(_pageBeforeSettings);

    /// <summary>Shows one of the shell's pages.</summary>
    public void NavigateTo(Type pageType) => RootNavigation.Navigate(pageType);

    /// <summary>
    /// While Settings is open its own navigation leads, so the main sidebar
    /// rests as its icon rail (still usable); leaving Settings puts it back
    /// the way the user last left it. Neither move is saved as a choice.
    /// </summary>
    private void SyncSidebarWithSettings(bool inSettings)
    {
        if (_rebuilding || inSettings == _inSettings)
        {
            return;
        }

        _inSettings = inSettings;
        var open = !inSettings && !_layoutStore.Load().SidebarCollapsed;

        _adjustingPane = true;
        try
        {
            RootNavigation.SetCurrentValue(NavigationView.IsPaneOpenProperty, open);
        }
        finally
        {
            _adjustingPane = false;
        }
    }

    private void OnOpenSearchExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (_currentPage is SearchPage search)
        {
            search.FocusQuery();
        }
        else
        {
            RootNavigation.Navigate(typeof(SearchPage));
        }
    }

    private void OnToggleSidebarExecuted(object sender, ExecutedRoutedEventArgs e) =>
        RootNavigation.SetCurrentValue(NavigationView.IsPaneOpenProperty, !RootNavigation.IsPaneOpen);

    private void OnBlockedTasksChanged(object? sender, EventArgs e) =>
        _ = Dispatcher.InvokeAsync(ApplyBlockedTaskIndicator);

    /// <summary>
    /// Shows a dot on the Tasks row while any task waits for approval: at the
    /// end of the row when the sidebar is open, on the icon in the rail. The
    /// row also says so to assistive technology.
    /// </summary>
    private void ApplyBlockedTaskIndicator()
    {
        var blocked = _blockedTasks.Count > 0;

        if (blocked && TasksNavigationItem.InfoBadge is null)
        {
            TasksNavigationItem.InfoBadge = new InfoBadge
            {
                Style = (Style)FindResource("HammorAttentionBadgeStyle"),
            };
        }
        else if (!blocked && TasksNavigationItem.InfoBadge is not null)
        {
            TasksNavigationItem.InfoBadge = null;
        }

        AutomationProperties.SetHelpText(
            TasksNavigationItem,
            blocked ? LocalizationSource.Instance["Nav.Tasks.Attention"] : string.Empty);
    }

    private void OnNavigated(NavigationView sender, NavigatedEventArgs args)
    {
        if (args.Page is FrameworkElement page)
        {
            _currentPageType = page.GetType();
            _currentPage = page;

            if (page is not SettingsPage && !_rebuilding)
            {
                _pageBeforeSettings = _currentPageType;
            }

            SyncSidebarWithSettings(page is SettingsPage);

            // A failed task shown on the Tasks page counts as seen, which
            // releases the Living Core's Error state.
            _presence.SetTaskListVisible(page is TasksPage);
        }
    }

    /// <summary>
    /// Rebuilds the active page when the language changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Text bound through <see cref="LocExtension"/> refreshes on its own,
    /// because those bindings observe <see cref="LocalizationSource"/>. Text
    /// produced by a value converter does not: those bindings are rooted at an
    /// enum or a bool that did not change, so WPF has no reason to re-run the
    /// converter and the old language stays on screen next to the new one.
    /// </para>
    /// <para>
    /// Re-navigating recreates the page's visual tree, so every binding and
    /// converter is evaluated again. Pages are transient in DI while their
    /// view models are singletons, so the page is rebuilt but its state is
    /// kept.
    /// </para>
    /// </remarks>
    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e) =>
        // On this window's thread, and after the work in progress: the switch
        // may have started inside a Settings combo box whose page this rebuild
        // replaces.
        _ = Dispatcher.InvokeAsync(
            () =>
            {
                RebuildCurrentPage();
                ApplyBlockedTaskIndicator();
            },
            DispatcherPriority.Loaded);

    private void RebuildCurrentPage()
    {
        var target = _currentPageType;

        // Navigating to the page that is already shown is a no-op, so clear
        // the host first to force a genuine rebuild. The detour through Chat
        // is not a visit: it must not move the sidebar or the Back target.
        _rebuilding = true;
        try
        {
            RootNavigation.Navigate(typeof(ChatPage));

            if (target != typeof(ChatPage))
            {
                RootNavigation.Navigate(target);
            }
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        _blockedTasks.CountChanged -= OnBlockedTasksChanged;
        RootNavigation.PaneOpened -= OnPaneStateChanged;
        RootNavigation.PaneClosed -= OnPaneStateChanged;
        ApplicationThemeManager.Changed -= OnThemeChanged;
        RootNavigation.Navigated -= OnNavigated;
    }
}

/// <summary>
/// Renders the busy flag as the localised "Busy" or "Ready" label.
/// </summary>
/// <remarks>
/// Declared alongside the shell because it is only meaningful to the status
/// bar; general-purpose converters live in <c>Converters</c>. The status bar
/// lives outside the navigation host, so <see cref="ShellViewModel"/> re-raises
/// its Status property on a language change to re-run this converter.
/// </remarks>
public sealed class BusyStateTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        LocalizationSource.Instance[value is true ? "Status.Busy" : "Status.Ready"];

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("One-way only.");
}
