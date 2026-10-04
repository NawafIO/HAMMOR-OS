using HAMMOR.Core.Configuration;
using Wpf.Ui.Appearance;

namespace HAMMOR.App.Services;

/// <summary>Applies the configured theme to the WPF-UI appearance manager.</summary>
public interface IThemeService
{
    void Apply(AppTheme theme);
}

/// <inheritdoc cref="IThemeService"/>
public sealed class WpfUiThemeService : IThemeService
{
    public void Apply(AppTheme theme)
    {
        switch (theme)
        {
            case AppTheme.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;

            case AppTheme.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;

            case AppTheme.System:
            default:
                // Follows the Windows personalisation setting, including a
                // later change while HAMMOR is running.
                ApplicationThemeManager.ApplySystemTheme();
                break;
        }
    }
}
