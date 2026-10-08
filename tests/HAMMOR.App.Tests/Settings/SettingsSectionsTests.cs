using System.Globalization;
using System.Resources;
using System.Windows;
using HAMMOR.App.Converters;
using HAMMOR.App.ViewModels;
using HAMMOR.App.Views;
using Xunit;

namespace HAMMOR.App.Tests.Settings;

/// <summary>
/// Settings v2: the seven categories, in order, each with its strings, and
/// the converter that shows only the selected one.
/// </summary>
public sealed class SettingsSectionsTests
{
    [Fact]
    public void The_categories_are_the_seven_in_order()
    {
        Assert.Equal(
            new[]
            {
                SettingsSection.Appearance,
                SettingsSection.AiModels,
                SettingsSection.Voice,
                SettingsSection.Memory,
                SettingsSection.Security,
                SettingsSection.Tasks,
                SettingsSection.System,
            },
            SettingsSections.All.Select(item => item.Id));
    }

    [Fact]
    public void Every_category_is_listed_once_with_its_own_name_and_icon()
    {
        var all = SettingsSections.All;

        Assert.Equal(Enum.GetValues<SettingsSection>().Length, all.Count);
        Assert.Equal(all.Count, all.Select(item => item.Id).Distinct().Count());
        Assert.Equal(all.Count, all.Select(item => item.LabelKey).Distinct().Count());
        Assert.Equal(all.Count, all.Select(item => item.DescriptionKey).Distinct().Count());
        Assert.Equal(all.Count, all.Select(item => item.Icon).Distinct().Count());
    }

    [Fact]
    public void Settings_opens_on_appearance()
    {
        Assert.Equal(SettingsSection.Appearance, SettingsSections.Default);
        Assert.Equal(SettingsSection.Appearance, SettingsSections.Find(SettingsSections.Default).Id);
    }

    [Theory]
    [InlineData(SettingsSection.Voice)]
    [InlineData(SettingsSection.System)]
    public void A_category_is_found_by_its_id(SettingsSection id)
    {
        Assert.Equal(id, SettingsSections.Find(id).Id);
    }

    [Fact]
    public void An_unknown_category_falls_back_to_the_default()
    {
        Assert.Equal(SettingsSections.Default, SettingsSections.Find((SettingsSection)99).Id);
    }

    [Theory]
    [InlineData(SettingsSection.Voice, "Voice", Visibility.Visible)]
    [InlineData(SettingsSection.Voice, "Memory", Visibility.Collapsed)]
    [InlineData(SettingsSection.AiModels, "AiModels", Visibility.Visible)]
    [InlineData(SettingsSection.AiModels, "aimodels", Visibility.Collapsed)]
    public void Only_the_selected_category_is_shown(SettingsSection selected, string page, Visibility expected)
    {
        var converter = new EnumMatchToVisibilityConverter();

        Assert.Equal(expected, converter.Convert(selected, typeof(Visibility), page, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Nothing_is_shown_without_a_category()
    {
        var converter = new EnumMatchToVisibilityConverter();

        Assert.Equal(Visibility.Collapsed, converter.Convert(null!, typeof(Visibility), "Voice", CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, converter.Convert("Voice", typeof(Visibility), "Voice", CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, converter.Convert(SettingsSection.Voice, typeof(Visibility), null!, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("Settings.Ai.ApiKey")]
    [InlineData("Settings.Ai.ApiKeyOptional")]
    [InlineData("Settings.Ai.Effort")]
    [InlineData("Settings.Ai.Group.ApiKey")]
    [InlineData("Settings.Ai.Group.ClaudeCode")]
    [InlineData("Settings.Ai.MaxTokens")]
    [InlineData("Settings.Ai.Model")]
    [InlineData("Settings.Ai.Provider")]
    [InlineData("Settings.ApiKeyNotStored")]
    [InlineData("Settings.ApiKeyPlaceholder")]
    [InlineData("Settings.ApiKeyStored")]
    [InlineData("Settings.Appearance.ThemeDescription")]
    [InlineData("Settings.Back")]
    [InlineData("Settings.Back.Name")]
    [InlineData("Settings.ClaudeCode.Description")]
    [InlineData("Settings.ClaudeCode.InstallHint")]
    [InlineData("Settings.ClaudeCode.LauncherHint")]
    [InlineData("Settings.ClaudeCode.Reconnect")]
    [InlineData("Settings.ClaudeCode.Refresh")]
    [InlineData("Settings.ClaudeCode.SignIn")]
    [InlineData("Settings.ClaudeCode.SignOut")]
    [InlineData("Settings.ClaudeCode.SignOutNote")]
    [InlineData("Settings.ClaudeCode.TextOnly")]
    [InlineData("Settings.ClaudeCode.UpdateHint")]
    [InlineData("Settings.ClaudeCode.Waiting")]
    [InlineData("Settings.ClearKey")]
    [InlineData("Settings.ConfigFile")]
    [InlineData("Settings.Language")]
    [InlineData("Settings.LanguageDescription")]
    [InlineData("Settings.LaunchOnStartup")]
    [InlineData("Settings.Memory.IndexOnStartup")]
    [InlineData("Settings.Memory.Location")]
    [InlineData("Settings.Navigation")]
    [InlineData("Settings.Save")]
    [InlineData("Settings.SaveHint")]
    [InlineData("Settings.SaveKey")]
    [InlineData("Settings.SecretsNote")]
    [InlineData("Settings.Section.Ai")]
    [InlineData("Settings.Section.Ai.Description")]
    [InlineData("Settings.Section.Appearance")]
    [InlineData("Settings.Section.Appearance.Description")]
    [InlineData("Settings.Section.Memory")]
    [InlineData("Settings.Section.Memory.Description")]
    [InlineData("Settings.Section.Security")]
    [InlineData("Settings.Section.Security.Description")]
    [InlineData("Settings.Section.System")]
    [InlineData("Settings.Section.System.Description")]
    [InlineData("Settings.Section.Tasks")]
    [InlineData("Settings.Section.Tasks.Description")]
    [InlineData("Settings.Section.Voice")]
    [InlineData("Settings.Section.Voice.Description")]
    [InlineData("Settings.Security.AlwaysConfirmDestructive")]
    [InlineData("Settings.Security.AutoApprove")]
    [InlineData("Settings.Security.AutoApproveDescription")]
    [InlineData("Settings.Security.DestructiveLocked")]
    [InlineData("Settings.Security.Secrets")]
    [InlineData("Settings.StartMinimised")]
    [InlineData("Settings.System.Connections.Description")]
    [InlineData("Settings.System.Connections.Header")]
    [InlineData("Settings.System.Group.Connections")]
    [InlineData("Settings.System.Group.Startup")]
    [InlineData("Settings.System.Group.Storage")]
    [InlineData("Settings.Tasks.Fixed")]
    [InlineData("Settings.Tasks.Group.Rules")]
    [InlineData("Settings.Tasks.Open")]
    [InlineData("Settings.Tasks.Open.Description")]
    [InlineData("Settings.Tasks.Open.Header")]
    [InlineData("Settings.Tasks.Rule.Blocked")]
    [InlineData("Settings.Tasks.Rule.Grant")]
    [InlineData("Settings.Tasks.Rule.NoRecurrence")]
    [InlineData("Settings.Tasks.Rule.OneAtATime")]
    [InlineData("Settings.Tasks.Rule.ReadOnly")]
    [InlineData("Settings.TestConnection")]
    [InlineData("Settings.Theme")]
    [InlineData("Settings.Title")]
    [InlineData("Settings.Voice.ApiKey")]
    [InlineData("Settings.Voice.Group.ElevenLabs")]
    [InlineData("Settings.Voice.Group.Playback")]
    [InlineData("Settings.Voice.InputDevice")]
    [InlineData("Settings.Voice.NoStreaming")]
    [InlineData("Settings.Voice.OutputDevice")]
    [InlineData("Settings.Voice.SpeakAutomatically")]
    [InlineData("Settings.Voice.Test.Description")]
    [InlineData("Settings.Voice.Test.Header")]
    [InlineData("Settings.Voice.TestSpeech")]
    [InlineData("Settings.Voice.TtsProvider")]
    [InlineData("Settings.Voice.VoiceId")]
    [InlineData("Settings.Voice.VoiceIdDescription")]
    [InlineData("Settings.Voice.Volume")]
    [InlineData("Tray.NotImplemented")]
    public void Every_string_settings_reads_exists(string key)
    {
        var resources = new ResourceManager("HAMMOR.App.Localization.Strings", typeof(SettingsPage).Assembly);

        Assert.False(string.IsNullOrWhiteSpace(resources.GetString(key, CultureInfo.InvariantCulture)), key);
    }
}
