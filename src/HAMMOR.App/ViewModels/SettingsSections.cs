using HAMMOR.App.Localization;
using Wpf.Ui.Controls;

namespace HAMMOR.App.ViewModels;

/// <summary>The categories of Settings v2, in navigation order.</summary>
public enum SettingsSection
{
    Appearance = 0,
    AiModels = 1,
    Voice = 2,
    Memory = 3,
    Security = 4,
    Tasks = 5,
    System = 6,
}

/// <summary>One row of the Settings navigation.</summary>
/// <param name="Id">The category.</param>
/// <param name="LabelKey">Localisation key of its name.</param>
/// <param name="DescriptionKey">Localisation key of the one-line summary under the page title.</param>
/// <param name="Icon">Its icon (never mirrored).</param>
public sealed record SettingsSectionItem(
    SettingsSection Id,
    string LabelKey,
    string DescriptionKey,
    SymbolRegular Icon)
{
    /// <summary>Read in the current language; the page is rebuilt when it changes.</summary>
    public string Label => LocalizationSource.Instance[LabelKey];

    public string Description => LocalizationSource.Instance[DescriptionKey];
}

/// <summary>
/// The Settings categories and what each owns.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Appearance</b>: theme.</item>
/// <item><b>AI &amp; models</b>: provider, the Claude Code account, the API-key
/// provider's model, effort, token limit and key.</item>
/// <item><b>Voice</b>: ElevenLabs voice and key, automatic speech, devices,
/// volume, test speech.</item>
/// <item><b>Memory</b>: memory folder, indexing on startup.</item>
/// <item><b>Security</b>: auto-approval ceiling, the locked destructive
/// confirmation, how secrets are stored.</item>
/// <item><b>Tasks &amp; scheduler</b>: the fixed rules unattended tasks run
/// under; nothing here is adjustable, by design.</item>
/// <item><b>System</b>: language, startup, connection check, where settings
/// are stored. Language lives here rather than in Appearance because it is
/// an install-level choice that also changes how HAMMOR replies.</item>
/// </list>
/// </remarks>
public static class SettingsSections
{
    /// <summary>The section Settings opens on first.</summary>
    public const SettingsSection Default = SettingsSection.Appearance;

    public static IReadOnlyList<SettingsSectionItem> All { get; } =
    [
        new(SettingsSection.Appearance, "Settings.Section.Appearance", "Settings.Section.Appearance.Description", SymbolRegular.PaintBrush24),
        new(SettingsSection.AiModels, "Settings.Section.Ai", "Settings.Section.Ai.Description", SymbolRegular.BrainCircuit24),
        new(SettingsSection.Voice, "Settings.Section.Voice", "Settings.Section.Voice.Description", SymbolRegular.Speaker224),
        new(SettingsSection.Memory, "Settings.Section.Memory", "Settings.Section.Memory.Description", SymbolRegular.Brain24),
        new(SettingsSection.Security, "Settings.Section.Security", "Settings.Section.Security.Description", SymbolRegular.ShieldLock24),
        new(SettingsSection.Tasks, "Settings.Section.Tasks", "Settings.Section.Tasks.Description", SymbolRegular.TaskListSquareLtr24),
        new(SettingsSection.System, "Settings.Section.System", "Settings.Section.System.Description", SymbolRegular.Settings24),
    ];

    /// <summary>The item for a section; unknown values fall back to the default.</summary>
    public static SettingsSectionItem Find(SettingsSection id) =>
        All.FirstOrDefault(item => item.Id == id) ?? All.First(item => item.Id == Default);
}
