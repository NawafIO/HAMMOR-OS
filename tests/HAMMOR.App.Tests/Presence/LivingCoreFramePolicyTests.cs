using HAMMOR.App.Presence;
using Xunit;

namespace HAMMOR.App.Tests.Presence;

/// <summary>
/// The Living Core's frame budget: when it draws at all, and how often.
/// </summary>
public sealed class LivingCoreFramePolicyTests
{
    private static readonly LivingCoreFrameContext Shown = new(
        IsLoaded: true,
        IsVisible: true,
        IsMinimized: false,
        IsWindowActive: true,
        InactiveSeconds: 0.0,
        Side: 450.0,
        MotionNeedsFrames: true);

    [Fact]
    public void A_shown_animating_core_draws()
    {
        Assert.True(LivingCoreFramePolicy.ShouldRender(Shown));
        Assert.True(LivingCoreFramePolicy.ShouldRender(Shown with { IsWindowActive = false, InactiveSeconds = 600.0 }));
    }

    [Fact]
    public void Minimised_hidden_unloaded_or_settled_cores_do_no_work()
    {
        Assert.False(LivingCoreFramePolicy.ShouldRender(Shown with { IsMinimized = true }));
        Assert.False(LivingCoreFramePolicy.ShouldRender(Shown with { IsVisible = false }));
        Assert.False(LivingCoreFramePolicy.ShouldRender(Shown with { IsLoaded = false }));
        Assert.False(LivingCoreFramePolicy.ShouldRender(Shown with { MotionNeedsFrames = false }));
    }

    [Fact]
    public void Settled_reduced_motion_needs_no_frames()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false, reducedMotion: true);

        Assert.False(LivingCoreFramePolicy.ShouldRender(Shown with { MotionNeedsFrames = motion.NeedsContinuousFrames(1.0) }));
    }

    [Theory]
    [InlineData(true, 0.0, 450.0, 1.0 / 60.0)]
    [InlineData(true, 0.0, 640.0, 1.0 / 60.0)]
    [InlineData(true, 0.0, 0.0, 1.0 / 60.0)]
    [InlineData(true, 0.0, 112.0, 1.0 / 30.0)]
    [InlineData(true, 0.0, 160.0, 1.0 / 30.0)]
    [InlineData(false, 0.0, 450.0, 1.0 / 30.0)]
    [InlineData(false, 44.9, 450.0, 1.0 / 30.0)]
    [InlineData(false, 45.0, 450.0, 1.0 / 20.0)]
    [InlineData(false, 3600.0, 112.0, 1.0 / 20.0)]
    public void Frame_interval_follows_activity_and_size(bool active, double inactiveSeconds, double side, double expected)
    {
        var context = Shown with { IsWindowActive = active, InactiveSeconds = inactiveSeconds, Side = side };

        Assert.Equal(expected, LivingCoreFramePolicy.MinimumInterval(context), 12);
    }

    [Theory]
    [InlineData(true, 0.0, 450.0)]
    [InlineData(true, 0.0, 112.0)]
    [InlineData(false, 0.0, 450.0)]
    [InlineData(false, 3600.0, 450.0)]
    public void A_core_settled_in_sleep_draws_at_10_fps(bool active, double inactiveSeconds, double side)
    {
        // Guardrails board: "10 asleep", wherever the window is.
        var context = Shown with { IsWindowActive = active, InactiveSeconds = inactiveSeconds, Side = side, IsResting = true };

        Assert.Equal(LivingCoreFramePolicy.RestingInterval, LivingCoreFramePolicy.MinimumInterval(context), 12);
    }

    [Fact]
    public void A_resting_core_slows_down_but_keeps_breathing()
    {
        // Sleep is "low energy", not dead: it draws less often, never not at all.
        Assert.True(LivingCoreFramePolicy.ShouldRender(Shown with { IsResting = true }));
        Assert.False(LivingCoreFramePolicy.ShouldRender(Shown with { IsResting = true, IsMinimized = true }));
    }

    [Theory]
    [InlineData(60.0, LivingCoreFramePolicy.ActiveInterval, 60)]
    [InlineData(120.0, LivingCoreFramePolicy.ActiveInterval, 60)]
    [InlineData(144.0, LivingCoreFramePolicy.ActiveInterval, 72)]
    [InlineData(60.0, LivingCoreFramePolicy.ReducedInterval, 30)]
    [InlineData(60.0, LivingCoreFramePolicy.BackgroundInterval, 20)]
    [InlineData(120.0, LivingCoreFramePolicy.ReducedInterval, 30)]
    [InlineData(60.0, LivingCoreFramePolicy.RestingInterval, 10)]
    [InlineData(120.0, LivingCoreFramePolicy.RestingInterval, 10)]
    public void Frames_drawn_in_one_second_of_display_refreshes(double hertz, double interval, int expected)
    {
        var last = double.NaN;
        var drawn = 0;
        var refreshes = (int)hertz;
        for (var i = 1; i <= refreshes; i++)
        {
            var now = i / hertz;
            if (LivingCoreFramePolicy.IsFrameDue(now - last, interval))
            {
                drawn++;
                last = now;
            }
        }

        Assert.Equal(expected, drawn);
    }

    [Fact]
    public void A_slightly_early_display_frame_still_counts_as_due()
    {
        Assert.True(LivingCoreFramePolicy.IsFrameDue(0.0150, LivingCoreFramePolicy.ActiveInterval));
        Assert.False(LivingCoreFramePolicy.IsFrameDue(0.0100, LivingCoreFramePolicy.ActiveInterval));
        Assert.True(LivingCoreFramePolicy.IsFrameDue(double.NaN, LivingCoreFramePolicy.BackgroundInterval));
    }

    [Fact]
    public void The_motion_setting_has_a_label_for_each_preference()
    {
        Assert.Equal(LivingCoreMotionStatus.ReducedKey, LivingCoreMotionStatus.KeyFor(reducedMotion: true));
        Assert.Equal(LivingCoreMotionStatus.FullKey, LivingCoreMotionStatus.KeyFor(reducedMotion: false));
    }
}
