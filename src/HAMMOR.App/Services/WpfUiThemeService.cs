using System.Windows.Media;
using HAMMOR.Core.Configuration;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace HAMMOR.App.Services;

/// <summary>Applies the configured theme to the WPF-UI appearance manager.</summary>
public interface IThemeService
{
    void Apply(AppTheme theme);
}

/// <inheritdoc cref="IThemeService"/>
/// <remarks>
/// <para>
/// No Mica: HAMMOR paints its own surfaces (the abyss in the dark theme, see
/// <c>ShellSurfaces</c>), and a backdrop would replace them with the desktop's
/// tint. WPF-UI re-applies Mica on every theme change unless told otherwise.
/// </para>
/// <para>
/// The accent is HAMMOR's lens teal rather than the Windows accent, so the
/// selected item, primary buttons and focus share the Living Core's colour.
/// High contrast keeps the system's colours.
/// </para>
/// </remarks>
public sealed class WpfUiThemeService : IThemeService
{
    // The lens teal, with a lighter and a deeper step for WPF-UI's accent
    // ramp on dark surfaces.
    private static readonly Color Teal = Color.FromRgb(0x3E, 0xD0, 0xC8);
    private static readonly Color TealLight = Color.FromRgb(0x7F, 0xE3, 0xDE);
    private static readonly Color TealDeep = Color.FromRgb(0x2A, 0x9E, 0x98);

    // Inked teal for light surfaces, dark enough for text contrast.
    private static readonly Color TealInk = Color.FromRgb(0x0E, 0x7F, 0x79);
    private static readonly Color TealInkDeep = Color.FromRgb(0x0B, 0x6B, 0x66);
    private static readonly Color TealInkDeeper = Color.FromRgb(0x08, 0x57, 0x52);

    public void Apply(AppTheme theme)
    {
        var applicationTheme = theme switch
        {
            AppTheme.Light => ApplicationTheme.Light,
            AppTheme.Dark => ApplicationTheme.Dark,

            // Follows the Windows personalisation setting at the time it is
            // applied.
            _ => SystemApplicationTheme(),
        };

        ApplicationThemeManager.Apply(applicationTheme, WindowBackdropType.None, updateAccent: false);
        ApplyAccent(applicationTheme);
    }

    private static ApplicationTheme SystemApplicationTheme()
    {
        SystemThemeManager.UpdateSystemThemeCache();

        return ApplicationThemeManager.GetSystemTheme() switch
        {
            SystemTheme.Dark or SystemTheme.CapturedMotion or SystemTheme.Glow => ApplicationTheme.Dark,
            SystemTheme.HC1 or SystemTheme.HC2 or SystemTheme.HCBlack or SystemTheme.HCWhite => ApplicationTheme.HighContrast,
            _ => ApplicationTheme.Light,
        };
    }

    private static void ApplyAccent(ApplicationTheme theme)
    {
        switch (theme)
        {
            case ApplicationTheme.Dark:
                // On dark surfaces WPF-UI fills with the secondary step.
                ApplicationAccentColorManager.Apply(Teal, TealLight, Teal, TealDeep);
                break;

            case ApplicationTheme.Light:
                // On light surfaces WPF-UI fills with the primary step.
                ApplicationAccentColorManager.Apply(TealInk, TealInk, TealInkDeep, TealInkDeeper);
                break;

            default:
                ApplicationAccentColorManager.ApplySystemAccent();
                break;
        }
    }
}
