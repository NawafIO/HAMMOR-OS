using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace HAMMOR.App.Themes;

/// <summary>
/// The shell's surfaces in the Living Core's visual language: the abyss for
/// the window and content, a lifted abyss for the navigation pane, hairline
/// strokes and the lens teal for the selected item, published as resources
/// for the dark theme.
/// </summary>
/// <remarks>
/// <para>
/// WPF-UI's NavigationView reads these keys with DynamicResource, so the
/// sidebar is refined without changing its template or navigation. Local
/// application resources take precedence over the theme dictionary; in the
/// light and high-contrast themes the overrides are removed again so WPF-UI's
/// own values apply.
/// </para>
/// <para>
/// Every brush is frozen, and theme changes are applied on the UI thread.
/// </para>
/// </remarks>
public static class ShellSurfaces
{
    /// <summary>Window background (and so the navigation pane).</summary>
    public const string ShellBackgroundKey = "HammorShellBackgroundBrush";

    /// <summary>Hairline separating chrome (pane, status bar) from content.</summary>
    public const string ChromeStrokeKey = "HammorChromeStrokeBrush";

    private const string ContentBackgroundKey = "NavigationViewContentBackground";
    private const string ContentBorderKey = "NavigationViewContentGridBorderBrush";
    private const string ItemSelectedKey = "NavigationViewItemBackgroundSelected";
    private const string ItemPointerOverKey = "NavigationViewItemBackgroundPointerOver";
    private const string IndicatorKey = "NavigationViewSelectionIndicatorForeground";
    private const string SeparatorKey = "LeftNavigationViewSeparatorBrush";

    private static readonly string[] NavigationOverrides =
    [
        ContentBackgroundKey,
        ContentBorderKey,
        ItemSelectedKey,
        ItemPointerOverKey,
        IndicatorKey,
        SeparatorKey,
    ];

    // Measured from the approved canvas: the abyss, and a pane a step above it.
    private static readonly Color Abyss = Color.FromRgb(0x03, 0x07, 0x0A);
    private static readonly Color Pane = Color.FromRgb(0x06, 0x0B, 0x0F);
    private static readonly Color LensTeal = Color.FromRgb(0x3E, 0xD0, 0xC8);

    private static bool s_listening;

    /// <summary>
    /// Publishes the surfaces for the current theme and follows theme changes.
    /// Call once at startup, after the theme has been applied.
    /// </summary>
    public static void Register(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        Apply(application, ApplicationThemeManager.GetAppTheme());

        if (!s_listening)
        {
            ApplicationThemeManager.Changed += OnThemeChanged;
            s_listening = true;
        }
    }

    private static void OnThemeChanged(ApplicationTheme currentApplicationTheme, Color systemAccent)
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        if (application.Dispatcher.CheckAccess())
        {
            Apply(application, currentApplicationTheme);
        }
        else
        {
            _ = application.Dispatcher.InvokeAsync(() => Apply(application, currentApplicationTheme));
        }
    }

    private static void Apply(Application application, ApplicationTheme theme)
    {
        var resources = application.Resources;

        if (theme == ApplicationTheme.Dark)
        {
            resources[ShellBackgroundKey] = Solid(Pane);
            resources[ChromeStrokeKey] = Solid(Color.FromArgb(0x10, 0xD6, 0xE8, 0xFF));

            resources[ContentBackgroundKey] = Solid(Abyss);
            resources[ContentBorderKey] = Solid(Color.FromArgb(0x10, 0xD6, 0xE8, 0xFF));
            resources[ItemSelectedKey] = Solid(Color.FromArgb(0x1A, LensTeal.R, LensTeal.G, LensTeal.B));
            resources[ItemPointerOverKey] = Solid(Color.FromArgb(0x0D, 0xD6, 0xE8, 0xFF));
            resources[IndicatorKey] = Solid(LensTeal);
            resources[SeparatorKey] = Solid(Color.FromArgb(0x10, 0xD6, 0xE8, 0xFF));
            return;
        }

        // Light and high contrast: WPF-UI's own surfaces.
        foreach (var key in NavigationOverrides)
        {
            resources.Remove(key);
        }

        resources[ShellBackgroundKey] = application.TryFindResource("ApplicationBackgroundBrush") as Brush
            ?? SystemColors.WindowBrush;
        resources[ChromeStrokeKey] = application.TryFindResource("CardStrokeColorDefaultBrush") as Brush
            ?? SystemColors.ActiveBorderBrush;
    }

    private static SolidColorBrush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
