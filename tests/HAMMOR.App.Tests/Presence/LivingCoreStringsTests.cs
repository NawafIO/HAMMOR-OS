using System.Globalization;
using System.Resources;
using HAMMOR.App.Presence;
using HAMMOR.App.Views;
using Xunit;

namespace HAMMOR.App.Tests.Presence;

/// <summary>
/// Every string the Living Core reads exists: its motion setting, and the
/// description of each state for screen readers.
/// </summary>
public sealed class LivingCoreStringsTests
{
    public static TheoryData<LivingCoreState> States
    {
        get
        {
            var states = new TheoryData<LivingCoreState>();
            foreach (var state in Enum.GetValues<LivingCoreState>())
            {
                states.Add(state);
            }

            return states;
        }
    }

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

    [Theory]
    [MemberData(nameof(States))]
    public void Every_state_has_a_description(LivingCoreState state)
    {
        var key = LivingCorePresenter.DescriptionKey(state);
        var resources = new ResourceManager("HAMMOR.App.Localization.Strings", typeof(SettingsPage).Assembly);

        Assert.False(string.IsNullOrWhiteSpace(resources.GetString(key, CultureInfo.InvariantCulture)), key);
    }

    [Fact]
    public void No_two_states_share_a_description()
    {
        var keys = Enum.GetValues<LivingCoreState>().Select(LivingCorePresenter.DescriptionKey).ToArray();

        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }
}
