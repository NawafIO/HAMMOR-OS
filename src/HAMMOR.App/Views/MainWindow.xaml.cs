using System.Globalization;
using System.Windows;
using System.Windows.Data;
using HAMMOR.App.Localization;
using HAMMOR.App.ViewModels;
using HAMMOR.Core.Localization;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace HAMMOR.App.Views;

/// <summary>Application shell.</summary>
public partial class MainWindow : FluentWindow
{
    private readonly ILocalizationService _localization;

    private Type _currentPageType = typeof(ChatPage);

    public MainWindow(
        ShellViewModel viewModel,
        INavigationViewPageProvider pageProvider,
        ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(pageProvider);

        ViewModel = viewModel;
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));

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
    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
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
