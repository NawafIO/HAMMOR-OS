using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace HAMMOR.App.Themes;

/// <summary>
/// The static HAMMOR mark (V2 Lens, approved Logo board) as vector images,
/// published as application resources for the title bar and the sidebar.
/// </summary>
/// <remarks>
/// <para>
/// Geometry is the Logo board's 120-unit box: seal ring r 50, lens from two
/// r 44 circles 44 apart, open rear where the wake leaves, white core on the
/// forward focus at (76, 57.5). Weights follow the board's per-size tuning:
/// the 28 px sidebar mark uses ring 6 and belly 4.5, the collapsed sidebar
/// uses the compact lens (ring dropped), and the title bar uses the 20 px
/// tile icon whose tile carries its own night in every theme.
/// </para>
/// <para>
/// "Never themed, never mirrored": the mark is pearl with a white core on
/// dark surfaces and the reversed abyss mark on light ones, swapped when the
/// app theme changes. Consumers set FlowDirection to LeftToRight so Arabic
/// layouts never mirror it.
/// </para>
/// <para>
/// The wordmarks are outlines of Alexandria Medium (SIL Open Font License
/// 1.1) shaped with HarfBuzz: 13 px with 0.24 em tracking for HAMMOR, 14 px
/// for the Arabic, so they render identically without the font installed.
/// </para>
/// </remarks>
public static class BrandMarks
{
    /// <summary>Title bar icon: the 20 px tile with the solid lens.</summary>
    public const string TitleBarIconKey = "HammorTitleBarIcon";

    /// <summary>Expanded sidebar: the full mark with its seal ring.</summary>
    public const string SidebarMarkKey = "HammorSidebarMark";

    /// <summary>Collapsed sidebar: the compact lens.</summary>
    public const string SidebarLensKey = "HammorSidebarLens";

    /// <summary>Latin wordmark geometry (caps box, origin top-left).</summary>
    public const string WordmarkLatinKey = "HammorWordmarkLatin";

    /// <summary>Arabic wordmark geometry (ink box, origin top-left).</summary>
    public const string WordmarkArabicKey = "HammorWordmarkArabic";

    /// <summary>Brush for wordmarks: pearl, or abyss when reversed.</summary>
    public const string InkBrushKey = "HammorMarkInkBrush";

    private const string Frame = "M 0,0 H 120 V 120 H 0 Z";
    private const string HeavyCrest = "M 21.9,60 A 44,44 0 0 1 98.1,60 A 55.89,55.89 0 0 0 21.9,60 Z";
    private const string SolidLens = "M 21.9,60 A 44,44 0 0 1 98.1,60 A 44,44 0 0 1 21.9,60 Z";
    private const string Belly = "M 98.1,60 A 44,44 0 0 1 26,65.93";

    // Outlines: nonzero fill (F1) because shaped glyphs may overlap.
    private const string WordmarkLatinPath = "F1 M1.12 -9.1H2.86V-5.17H7.4V-9.1H9.15V0H7.4V-3.73H2.86V0H1.12ZM17.38 -9.1H19.16L23.1 0H21.24L18.21 -7.34L15.2 0H13.38ZM15.47 -3.41H20.89V-1.99H15.47ZM27.34 -9.1H29.33L32.34 -3.13L35.33 -9.1H37.32V0H35.72V-6.67L32.89 -0.94H31.77L28.93 -6.67V0H27.34ZM42.68 -9.1H44.67L47.68 -3.13L50.67 -9.1H52.66V0H51.06V-6.67L48.23 -0.94H47.11L44.27 -6.67V0H42.68ZM62.15 -9.2Q63.18 -9.2 64.06 -8.85Q64.95 -8.5 65.6 -7.86Q66.26 -7.23 66.63 -6.39Q67 -5.55 67 -4.56Q67 -3.59 66.63 -2.73Q66.26 -1.87 65.6 -1.23Q64.95 -0.6 64.06 -0.24Q63.18 0.12 62.15 0.12Q61.13 0.12 60.25 -0.24Q59.37 -0.6 58.71 -1.23Q58.04 -1.87 57.67 -2.72Q57.3 -3.57 57.3 -4.56Q57.3 -5.55 57.67 -6.4Q58.04 -7.24 58.71 -7.87Q59.37 -8.5 60.25 -8.85Q61.13 -9.2 62.15 -9.2ZM62.18 -7.73Q61.54 -7.73 60.98 -7.49Q60.42 -7.25 59.99 -6.82Q59.57 -6.38 59.33 -5.81Q59.08 -5.24 59.08 -4.56Q59.08 -3.89 59.33 -3.31Q59.58 -2.73 60.01 -2.29Q60.44 -1.85 61 -1.6Q61.55 -1.35 62.18 -1.35Q62.8 -1.35 63.36 -1.6Q63.91 -1.85 64.32 -2.29Q64.74 -2.73 64.98 -3.31Q65.22 -3.89 65.22 -4.56Q65.22 -5.24 64.98 -5.81Q64.74 -6.38 64.32 -6.82Q63.91 -7.25 63.36 -7.49Q62.8 -7.73 62.18 -7.73ZM75.45 -9.1Q77.22 -9.1 78.18 -8.28Q79.14 -7.46 79.14 -5.98Q79.14 -4.43 78.18 -3.58Q77.22 -2.72 75.45 -2.72H73.4V0H71.66V-9.1ZM75.45 -4.16Q76.43 -4.16 76.96 -4.6Q77.49 -5.04 77.49 -5.94Q77.49 -6.8 76.96 -7.22Q76.43 -7.64 75.45 -7.64H73.4V-4.16ZM75.39 -3.38H77.09L79.33 0H77.35Z";
    private const string WordmarkArabicPath = "F1 M0 2.03Q0.69 2.03 1.09 1.88Q1.5 1.74 1.67 1.4Q1.85 1.06 1.85 0.56V-6.86H3.44V0.56Q3.44 1.55 3.06 2.27Q2.67 2.98 1.98 3.37Q1.29 3.75 0.29 3.78ZM10.92 0V-1.83H12.98V0ZM12.98 0V-1.83Q13.12 -1.83 13.19 -1.58Q13.26 -1.33 13.26 -0.92Q13.26 -0.5 13.19 -0.25Q13.12 0 12.98 0ZM5.4 3.78V1.96H8.08Q8.96 1.96 9.34 1.62Q9.72 1.27 9.72 0.56V-6.54H11.31V0.56Q11.31 1.58 10.92 2.3Q10.53 3.02 9.8 3.4Q9.07 3.78 8.08 3.78ZM8.53 0Q7.41 0 6.58 -0.48Q5.75 -0.97 5.29 -1.79Q4.83 -2.6 4.83 -3.57Q4.83 -4.37 5.12 -5.04Q5.4 -5.71 5.92 -6.22Q6.44 -6.72 7.11 -7Q7.78 -7.28 8.55 -7.28Q9.32 -7.28 10.04 -7.05Q10.75 -6.82 11.31 -6.54L10.63 -4.9Q9.46 -5.5 8.55 -5.5Q7.95 -5.5 7.47 -5.24Q6.99 -4.98 6.71 -4.55Q6.43 -4.12 6.43 -3.57Q6.43 -3.07 6.69 -2.66Q6.94 -2.25 7.41 -2.02Q7.88 -1.78 8.53 -1.78H10.6V0ZM12.98 0V-1.83Q13.64 -1.83 14.07 -2.08Q14.5 -2.32 14.85 -2.76Q15.19 -3.21 15.53 -3.78Q15.86 -4.33 16.23 -4.87Q16.6 -5.42 17.06 -5.86Q17.51 -6.3 18.06 -6.57Q18.61 -6.85 19.31 -6.85Q20.06 -6.85 20.61 -6.53Q21.15 -6.22 21.5 -5.68Q21.84 -5.14 22 -4.47Q22.16 -3.79 22.16 -3.08Q22.16 -2.34 21.97 -1.44Q21.77 -0.53 21.34 0.24L19.91 -0.59Q20.23 -1.15 20.4 -1.79Q20.57 -2.42 20.57 -3.08Q20.57 -4.07 20.24 -4.55Q19.91 -5.03 19.31 -5.03Q18.91 -5.03 18.55 -4.82Q18.19 -4.62 17.79 -4.14Q17.4 -3.65 16.88 -2.81Q16.3 -1.86 15.74 -1.23Q15.19 -0.6 14.53 -0.3Q13.87 0 12.98 0ZM21.25 -1.58 21.34 0.24Q20.05 0.31 19.1 0.22Q18.14 0.13 17.44 -0.12Q16.73 -0.36 16.18 -0.74Q15.62 -1.12 15.15 -1.61L16.27 -2.83Q16.77 -2.31 17.43 -2.01Q18.09 -1.71 19.02 -1.61Q19.95 -1.51 21.25 -1.58ZM12.98 0Q12.82 0 12.76 -0.25Q12.7 -0.5 12.7 -0.94Q12.7 -1.34 12.76 -1.59Q12.82 -1.83 12.98 -1.83ZM25.65 0V-1.83H27.31V0ZM24.08 0V-11.65H25.68V0ZM27.31 0V-1.83Q27.45 -1.83 27.52 -1.58Q27.59 -1.33 27.59 -0.92Q27.59 -0.5 27.52 -0.25Q27.45 0 27.31 0ZM33.67 0.03Q32.68 0.03 31.68 -0.28Q30.67 -0.59 29.86 -1.17Q29.04 -1.75 28.55 -2.56Q28.06 -3.37 28.06 -4.4Q28.06 -5.26 28.42 -5.99Q28.78 -6.72 29.43 -7.16Q30.07 -7.6 30.9 -7.6Q31.51 -7.6 32.05 -7.35Q32.58 -7.1 32.97 -6.66Q33.36 -6.22 33.59 -5.65Q33.81 -5.08 33.81 -4.45Q33.81 -3.58 33.37 -2.79Q32.93 -1.99 32.08 -1.36Q31.23 -0.73 30.03 -0.36Q28.83 0 27.31 0V-1.83Q28.39 -1.83 29.3 -2.06Q30.2 -2.28 30.87 -2.65Q31.54 -3.01 31.91 -3.49Q32.28 -3.96 32.28 -4.47Q32.28 -4.86 32.09 -5.17Q31.91 -5.47 31.6 -5.66Q31.29 -5.85 30.9 -5.85Q30.53 -5.85 30.23 -5.67Q29.93 -5.49 29.76 -5.18Q29.58 -4.87 29.58 -4.49Q29.58 -3.91 29.97 -3.39Q30.35 -2.87 30.99 -2.48Q31.63 -2.09 32.38 -1.87Q33.14 -1.65 33.88 -1.65Q34.43 -1.65 34.85 -1.8Q35.28 -1.95 35.52 -2.26Q35.77 -2.58 35.77 -3.11Q35.77 -3.86 35.15 -4.4Q34.54 -4.93 33.23 -5.32Q31.92 -5.71 29.83 -6.08Q29.76 -6.05 29.68 -6.05Q29.6 -6.05 29.51 -6.08Q29.43 -6.1 29.33 -6.12Q28.92 -6.19 28.47 -6.25Q28.01 -6.31 27.54 -6.38L27.75 -8.15Q29.88 -7.85 31.65 -7.5Q33.42 -7.15 34.7 -6.64Q35.98 -6.12 36.67 -5.29Q37.37 -4.45 37.37 -3.18Q37.37 -2.31 37.07 -1.7Q36.78 -1.09 36.25 -0.71Q35.73 -0.34 35.07 -0.15Q34.41 0.03 33.67 0.03ZM27.31 0Q27.16 0 27.1 -0.25Q27.03 -0.5 27.03 -0.94Q27.03 -1.34 27.1 -1.59Q27.16 -1.83 27.31 -1.83Z";

    private static readonly Color Pearl = Color.FromRgb(0xEA, 0xF0, 0xF5);
    private static readonly Color White = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private static readonly Color Abyss = Color.FromRgb(0x04, 0x07, 0x0A);
    private static readonly Color ReversedInk = Color.FromRgb(0x0B, 0x11, 0x18);

    private static bool s_listening;

    /// <summary>
    /// Publishes the mark resources and keeps them matched to the theme.
    /// Call once at startup, after the theme has been applied.
    /// </summary>
    public static void Register(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.Resources[TitleBarIconKey] = CreateTitleBarIcon();
        application.Resources[WordmarkLatinKey] = CreateWordmark(WordmarkLatinPath, -1.12, 9.2);
        application.Resources[WordmarkArabicKey] = CreateWordmark(WordmarkArabicPath, 0.0, 11.65);
        ApplyInk(application.Resources, ApplicationThemeManager.GetAppTheme());

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
            ApplyInk(application.Resources, currentApplicationTheme);
        }
        else
        {
            _ = application.Dispatcher.InvokeAsync(() => ApplyInk(application.Resources, currentApplicationTheme));
        }
    }

    private static void ApplyInk(ResourceDictionary resources, ApplicationTheme theme)
    {
        Color ink;
        Color core;
        switch (theme)
        {
            case ApplicationTheme.Light:
                // Reversed: abyss on pearl, for light surfaces.
                ink = ReversedInk;
                core = ReversedInk;
                break;

            case ApplicationTheme.HighContrast:
                ink = SystemColors.WindowTextColor;
                core = ink;
                break;

            default:
                ink = Pearl;
                core = White;
                break;
        }

        var inkBrush = Frozen(new SolidColorBrush(ink));
        var coreBrush = Frozen(new SolidColorBrush(core));

        resources[InkBrushKey] = inkBrush;
        resources[SidebarMarkKey] = CreateSidebarMark(inkBrush, coreBrush);
        resources[SidebarLensKey] = CreateSidebarLens(inkBrush, coreBrush);
    }

    /// <summary>Full mark at sidebar size: ring 6, heavier crest, belly 4.5, core 8.</summary>
    private static DrawingImage CreateSidebarMark(Brush ink, Brush core)
    {
        var group = new DrawingGroup();
        group.Children.Add(FrameDrawing());
        group.Children.Add(new GeometryDrawing(null, Stroke(ink, 6.0), new EllipseGeometry(new Point(60.0, 60.0), 50.0, 50.0)));
        group.Children.Add(new GeometryDrawing(ink, null, Parse(HeavyCrest)));
        group.Children.Add(new GeometryDrawing(null, Stroke(ink, 4.5), Parse(Belly)));
        group.Children.Add(new GeometryDrawing(core, null, new EllipseGeometry(new Point(76.0, 57.5), 8.0, 8.0)));
        return Frozen(new DrawingImage(group));
    }

    /// <summary>Compact lens: ring dropped, lens scaled 1.24 about the centre.</summary>
    private static DrawingImage CreateSidebarLens(Brush ink, Brush core)
    {
        var lens = new DrawingGroup { Transform = ScaleAboutCentre(1.24) };
        lens.Children.Add(new GeometryDrawing(ink, null, Parse(HeavyCrest)));
        lens.Children.Add(new GeometryDrawing(null, Stroke(ink, 5.0), Parse(Belly)));
        lens.Children.Add(new GeometryDrawing(core, null, new EllipseGeometry(new Point(76.0, 57.5), 8.0, 8.0)));

        var group = new DrawingGroup();
        group.Children.Add(FrameDrawing());
        group.Children.Add(lens);
        return Frozen(new DrawingImage(group));
    }

    /// <summary>The 20 px app-icon tile: solid lens with a dark core.</summary>
    private static DrawingImage CreateTitleBarIcon()
    {
        var tile = new RectangleGeometry(new Rect(0.0, 0.0, 120.0, 120.0), 27.0, 27.0);
        var tileFill = new LinearGradientBrush(
            Color.FromRgb(0x17, 0x21, 0x2D),
            Abyss,
            new Point(0.0, 0.0),
            new Point(0.0, 1.0));
        var tileGlow = new RadialGradientBrush
        {
            Center = new Point(0.6, 0.48),
            GradientOrigin = new Point(0.6, 0.48),
            RadiusX = 0.5,
            RadiusY = 0.5,
        };
        tileGlow.GradientStops.Add(new GradientStop(Color.FromArgb(0xE6, 0x14, 0x70, 0x78), 0.0));
        tileGlow.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x14, 0x70, 0x78), 1.0));

        var lens = new DrawingGroup { Transform = ScaleAboutCentre(1.25) };
        lens.Children.Add(new GeometryDrawing(Frozen(new SolidColorBrush(Pearl)), null, Parse(SolidLens)));
        lens.Children.Add(new GeometryDrawing(Frozen(new SolidColorBrush(Abyss)), null, new EllipseGeometry(new Point(76.0, 57.5), 9.5, 9.5)));

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Frozen(tileFill), null, tile));
        group.Children.Add(new GeometryDrawing(Frozen(tileGlow), null, tile));
        group.Children.Add(lens);
        return Frozen(new DrawingImage(group));
    }

    private static Geometry CreateWordmark(string data, double offsetX, double offsetY)
    {
        var geometry = Geometry.Parse(data).Clone();
        geometry.Transform = new TranslateTransform(offsetX, offsetY);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>A transparent 120-unit square so every mark keeps the board's box.</summary>
    private static GeometryDrawing FrameDrawing() =>
        new(Brushes.Transparent, null, Parse(Frame));

    private static Transform ScaleAboutCentre(double scale) =>
        Frozen(new MatrixTransform(scale, 0.0, 0.0, scale, 60.0 - (60.0 * scale), 60.0 - (60.0 * scale)));

    private static Pen Stroke(Brush brush, double thickness) => Frozen(new Pen(brush, thickness)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
    });

    private static Geometry Parse(string data) => Frozen(Geometry.Parse(data).Clone());

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
