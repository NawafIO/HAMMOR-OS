using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HAMMOR.App.Localization;
using HAMMOR.App.Presence;
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
    private readonly ILocalizationService _localization;
    private readonly LivingCorePresenter _presence;

    private Type _currentPageType = typeof(ChatPage);

    public MainWindow(
        ShellViewModel viewModel,
        INavigationViewPageProvider pageProvider,
        ILocalizationService localization,
        LivingCorePresenter presence)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(pageProvider);

        ViewModel = viewModel;
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _presence = presence ?? throw new ArgumentNullException(nameof(presence));

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
    }

    private void OnNavigated(NavigationView sender, NavigatedEventArgs args)
    {
        if (args.Page is FrameworkElement page)
        {
            _currentPageType = page.GetType();

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
        _ = Dispatcher.InvokeAsync(RebuildCurrentPage, DispatcherPriority.Loaded);

    private void RebuildCurrentPage()
    {
        var target = _currentPageType;

        // Navigating to the page that is already shown is a no-op, so clear
        // the host first to force a genuine rebuild.
        RootNavigation.Navigate(typeof(ChatPage));

        if (target != typeof(ChatPage))
        {
            RootNavigation.Navigate(target);
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _localization.LanguageChanged -= OnLanguageChanged;
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
