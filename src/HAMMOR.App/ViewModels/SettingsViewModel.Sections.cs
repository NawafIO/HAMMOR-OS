namespace HAMMOR.App.ViewModels;

/// <summary>
/// Settings v2: which category the secondary navigation shows.
/// </summary>
/// <remarks>
/// Every category edits the same <see cref="Draft"/>, so switching category
/// never loses an unsaved change and one Save writes them all. The choice
/// lives on this singleton, so it survives the page being rebuilt (for
/// example on a language switch) for the rest of the session.
/// </remarks>
public sealed partial class SettingsViewModel
{
    private SettingsSectionItem _selectedSectionItem = SettingsSections.Find(SettingsSections.Default);

    public IReadOnlyList<SettingsSectionItem> Sections => SettingsSections.All;

    /// <summary>The selected navigation row. A null from the list is ignored.</summary>
    public SettingsSectionItem? SelectedSectionItem
    {
        get => _selectedSectionItem;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedSectionItem))
            {
                return;
            }

            _selectedSectionItem = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSection));
        }
    }

    /// <summary>The selected category; the page shows only its content.</summary>
    public SettingsSection SelectedSection
    {
        get => _selectedSectionItem.Id;
        set => SelectedSectionItem = SettingsSections.Find(value);
    }
}
