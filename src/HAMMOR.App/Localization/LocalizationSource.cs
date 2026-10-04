using System.ComponentModel;
using System.Windows;
using HAMMOR.Core.Localization;

namespace HAMMOR.App.Localization;

/// <summary>
/// Singleton binding source that XAML attaches to for localised strings and
/// flow direction.
/// </summary>
/// <remarks>
/// A markup extension cannot resolve services from the DI container, so this
/// type is the bridge: <c>App</c> calls <see cref="Attach"/> once the host is
/// built, and every <see cref="LocExtension"/> binding targets this instance.
/// Before attachment (and in the XAML designer) lookups echo the key back, so
/// the designer renders instead of throwing.
/// </remarks>
public sealed class LocalizationSource : INotifyPropertyChanged
{
    public static LocalizationSource Instance { get; } = new();

    private ILocalizationService? _service;

    private LocalizationSource()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Localised string for <paramref name="key"/>.</summary>
    public string this[string key] => _service is null ? key : _service[key];

    /// <summary>
    /// Layout direction for the active language, bound by the shell so Arabic
    /// lays out right-to-left.
    /// </summary>
    public FlowDirection FlowDirection =>
        _service?.IsRightToLeft == true ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>True when the active language reads right-to-left.</summary>
    public bool IsRightToLeft => _service?.IsRightToLeft ?? false;

    /// <summary>
    /// Binds this source to the real service. Called once during startup.
    /// </summary>
    public void Attach(ILocalizationService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        if (_service is not null)
        {
            _service.LanguageChanged -= OnLanguageChanged;
        }

        _service = service;
        _service.LanguageChanged += OnLanguageChanged;

        RaiseAllChanged();
    }

    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e) => RaiseAllChanged();

    private void RaiseAllChanged()
    {
        // "Item[]" refreshes indexer bindings; the empty name refreshes
        // FlowDirection and IsRightToLeft at the same time.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
