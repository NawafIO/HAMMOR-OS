using System.Globalization;
using System.Resources;
using HAMMOR.App.Views;
using Xunit;

namespace HAMMOR.App.Tests.Presence;

/// <summary>
/// The Living Home's rules: the hero's size strategy, the greeting by time of
/// day, and the strings the new screens read.
/// </summary>
public sealed class LivingHomeTests
{
    [Theory]
    [InlineData(912.0, 449.0, 449.0)]   // default window: height-bound
    [InlineData(1652.0, 760.0, 640.0)]  // large window: capped at the canvas scale
    [InlineData(400.0, 600.0, 248.0)]   // narrow window: width-bound (62%)
    [InlineData(300.0, 150.0, 180.0)]   // tiny window: never below the floor
    [InlineData(0.0, 500.0, 180.0)]
    [InlineData(double.NaN, 500.0, 180.0)]
    public void The_hero_follows_the_space_it_is_given(double width, double height, double expected)
    {
        Assert.Equal(expected, ChatPage.HeroSizeFor(width, height));
    }

    [Fact]
    public void The_hero_never_draws_beyond_the_canvas_scale()
    {
        for (var width = 100.0; width <= 4000.0; width += 37.0)
        {
            for (var height = 100.0; height <= 2400.0; height += 41.0)
            {
                var side = ChatPage.HeroSizeFor(width, height);

                Assert.InRange(side, ChatPage.MinHeroSize, ChatPage.MaxHeroSize);
                Assert.Equal(Math.Floor(side), side);
            }
        }
    }

    [Theory]
    [InlineData(0, "Home.Greeting.Evening")]
    [InlineData(4, "Home.Greeting.Evening")]
    [InlineData(5, "Home.Greeting.Morning")]
    [InlineData(11, "Home.Greeting.Morning")]
    [InlineData(12, "Home.Greeting.Afternoon")]
    [InlineData(17, "Home.Greeting.Afternoon")]
    [InlineData(18, "Home.Greeting.Evening")]
    [InlineData(23, "Home.Greeting.Evening")]
    public void The_greeting_follows_the_local_hour(int hour, string expected)
    {
        Assert.Equal(expected, ChatPage.GreetingKeyFor(hour));
    }

    [Theory]
    [InlineData("Home.Greeting.Morning")]
    [InlineData("Home.Greeting.Afternoon")]
    [InlineData("Home.Greeting.Evening")]
    [InlineData("Settings.Ai.Provider.ClaudeCode")]
    [InlineData("Settings.Ai.Provider.ApiKey")]
    [InlineData("Settings.Ai.ApiKeyOptional")]
    [InlineData("Settings.ClaudeCode.Description")]
    [InlineData("Settings.ClaudeCode.TextOnly")]
    [InlineData("Settings.ClaudeCode.State.NotInstalled")]
    [InlineData("Settings.ClaudeCode.State.UnsupportedInstall")]
    [InlineData("Settings.ClaudeCode.State.NotSignedIn")]
    [InlineData("Settings.ClaudeCode.State.SignedIn")]
    [InlineData("Settings.ClaudeCode.State.UsageLimited")]
    [InlineData("Settings.ClaudeCode.State.NeedsUpdate")]
    [InlineData("Settings.ClaudeCode.State.Error")]
    [InlineData("Settings.ClaudeCode.State.Checking")]
    [InlineData("Settings.ClaudeCode.Version")]
    [InlineData("Settings.ClaudeCode.Method")]
    [InlineData("Settings.ClaudeCode.Method.Subscription")]
    [InlineData("Settings.ClaudeCode.Method.Token")]
    [InlineData("Settings.ClaudeCode.Method.ApiKey")]
    [InlineData("Settings.ClaudeCode.Method.ThirdParty")]
    [InlineData("Settings.ClaudeCode.Method.Unknown")]
    [InlineData("Settings.ClaudeCode.InstallHint")]
    [InlineData("Settings.ClaudeCode.LauncherHint")]
    [InlineData("Settings.ClaudeCode.UpdateHint")]
    [InlineData("Settings.ClaudeCode.SignIn")]
    [InlineData("Settings.ClaudeCode.Reconnect")]
    [InlineData("Settings.ClaudeCode.SignOut")]
    [InlineData("Settings.ClaudeCode.Refresh")]
    [InlineData("Settings.ClaudeCode.Waiting")]
    [InlineData("Settings.ClaudeCode.SignOutNote")]
    [InlineData("FirstRun.ClaudeCodeHint")]
    public void Every_string_the_new_screens_read_exists(string key)
    {
        var resources = new ResourceManager("HAMMOR.App.Localization.Strings", typeof(ChatPage).Assembly);

        Assert.False(string.IsNullOrWhiteSpace(resources.GetString(key, CultureInfo.InvariantCulture)), key);
    }
}
