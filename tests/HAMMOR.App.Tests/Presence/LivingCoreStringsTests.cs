using System.Globalization;
using System.Resources;
using HAMMOR.App.Presence;
using HAMMOR.App.Views;
using Xunit;

namespace HAMMOR.App.Tests.Presence;

/// <summary>Every string the Living Core's motion setting reads exists.</summary>
public sealed class LivingCoreStringsTests
{
    [Theory]
    [InlineData("Settings.Appearance.Motion")]
    [InlineData("Settings.Appearance.MotionDescription")]
    [InlineData(LivingCoreMotionStatus.FullKey)]
    [InlineData(LivingCoreMotionStatus.ReducedKey)]
    public void The_string_exists(string key)
    {
        var resources = new ResourceManager("HAMMOR.App.Localization.Strings", typeof(SettingsPage).Assembly);

        Assert.False(string.IsNullOrWhiteSpace(resources.GetString(key, CultureInfo.InvariantCulture)), key);
    }
}
