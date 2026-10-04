using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HAMMOR.App.Localization;
using HAMMOR.Core.Localization;
using HAMMOR.Core.Status;

namespace HAMMOR.App.ViewModels;

/// <summary>
/// Backs the shell: status indicators, the quick language control and flow
/// direction.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly ISystemStatusService _statusService;
    private readonly ILocalizationService _localization;

    public ShellViewModel(
        ISystemStatusService statusService,
        ILocalizationService localization)
    {
        _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));

        Status = statusService.Current;

        // Event-driven: the shell reflects status changes as they happen
        // instead of polling on a timer.
        _statusService.StatusChanged += OnStatusChanged;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    [ObservableProperty]
    private SystemStatus _status;

    /// <summary>Layout direction for the active language.</summary>
    public FlowDirection FlowDirection =>
        _localization.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>Languages offered by the quick-access control.</summary>
    public IReadOnlyList<LanguageOption> AvailableLanguages => _localization.AvailableLanguages;

    public string CurrentLanguage => _localization.CurrentLanguage;

    /// <summary>
    /// Label for the quick language control: shows the language the user would
    /// switch *to*, written in that language.
    /// </summary>
    public string LanguageToggleLabel =>
        _localization.AvailableLanguages
            .FirstOrDefault(l => l.Code != _localization.CurrentLanguage)?.NativeName
        ?? _localization.CurrentLanguage;

    /// <summary>
    /// Switches to the other installed language. Applies immediately — no
    /// restart.
    /// </summary>
    [RelayCommand]
    private async Task ToggleLanguageAsync()
    {
        var next = _localization.AvailableLanguages
            .FirstOrDefault(l => l.Code != _localization.CurrentLanguage);

        if (next is not null)
        {
            await _localization.SetLanguageAsync(next.Code).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task RefreshStatusAsync() =>
        await _statusService.RefreshAsync().ConfigureAwait(true);

    private void OnStatusChanged(object? sender, SystemStatus status) => Status = status;

    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        OnPropertyChanged(nameof(FlowDirection));
        OnPropertyChanged(nameof(CurrentLanguage));
        OnPropertyChanged(nameof(LanguageToggleLabel));

        // The status bar is outside the navigation host, so it is not rebuilt
        // by the shell's re-navigation. Its busy label goes through a value
        // converter whose binding is rooted at Status; re-raising Status is
        // what makes that converter run again in the new language.
        OnPropertyChanged(nameof(Status));
    }
}
