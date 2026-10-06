using HAMMOR.App.Presence;
using Xunit;

namespace HAMMOR.App.Tests.Presence;

/// <summary>
/// The Living Core's fixed design against the approved canvas: state poses,
/// the build board's budget, and the motion board's easing tokens.
/// </summary>
public sealed class LivingCoreDesignTests
{
    public static TheoryData<LivingCoreState> States => new()
    {
        LivingCoreState.Idle,
        LivingCoreState.Listening,
        LivingCoreState.Thinking,
        LivingCoreState.Speaking,
        LivingCoreState.Blocked,
        LivingCoreState.Error,
    };

    [Fact]
    public void Rest_is_where_the_logo_puts_the_white_core()
    {
        Assert.Equal(76.0, LivingCoreLooks.Idle.Core.X);
        Assert.Equal(57.5, LivingCoreLooks.Idle.Core.Y);
        Assert.Equal(1.0, LivingCoreLooks.Idle.Core.Scale);
    }

    [Fact]
    public void State_poses_match_the_approved_boards()
    {
        Assert.Equal(1.12, LivingCoreLooks.Listening.Core.Scale);
        Assert.Equal(LivingCoreLooks.ZoneX, LivingCoreLooks.Listening.Core.X);
        Assert.Equal(1.0, LivingCoreLooks.Listening.Core.AttentionRing);

        Assert.Equal(0.78, LivingCoreLooks.Thinking.Core.Scale);
        Assert.Equal(0.55, LivingCoreLooks.Thinking.Core.Opacity);

        Assert.Equal(1.25, LivingCoreLooks.Speaking.Core.Scale);
        Assert.Equal(LivingCoreLooks.ZoneX, LivingCoreLooks.Speaking.Core.X);

        Assert.Equal(0.6, LivingCoreLooks.Error.Core.Opacity);
    }

    [Fact]
    public void Blocked_waits_without_an_alarm_colour()
    {
        // State board: "No alarm colour: waiting, not failing."
        Assert.Equal(0.0, LivingCoreLooks.Blocked.Halo.RimDanger);
        Assert.Equal(0.0, LivingCoreLooks.Blocked.Inside.CrackOpacity);
        Assert.Equal(1.0, LivingCoreLooks.Blocked.Halo.GapRingOpacity);
    }

    [Theory]
    [MemberData(nameof(States))]
    public void Every_pose_sits_inside_the_white_core_zone(LivingCoreState state)
    {
        var look = LivingCoreLooks.For(state);
        var nx = (look.Core.X - LivingCoreLooks.ZoneX) / LivingCoreLooks.ZoneRadiusX;
        var ny = (look.Core.Y - LivingCoreLooks.ZoneY) / LivingCoreLooks.ZoneRadiusY;

        Assert.InRange(Math.Sqrt((nx * nx) + (ny * ny)), 0.0, 1.0);
    }

    [Fact]
    public void Layers_stay_within_the_approved_budget()
    {
        // Build board: at most 40 stars and 16 cells, 4 threads, 3 halo rings.
        Assert.InRange(LivingCoreDesign.Stars.Length, 1, 40);
        Assert.InRange(LivingCoreDesign.Cells.Length, 1, 16);
        Assert.Equal(4, LivingCoreDesign.Threads.Length);
        Assert.Equal(3, LivingCoreDesign.HaloDelays.Length);
        Assert.All(LivingCoreDesign.Stars, s => Assert.InRange(s.Clock, 0, LivingCoreDesign.ShimmerClocks.Length - 1));
        Assert.All(LivingCoreDesign.Cells, c => Assert.InRange(c.Clock, 0, LivingCoreDesign.BobClocks.Length - 1));
    }

    [Fact]
    public void Threads_alternate_direction()
    {
        var threads = LivingCoreDesign.Threads;
        for (var i = 1; i < threads.Length; i++)
        {
            Assert.NotEqual(threads[i - 1].Clockwise, threads[i].Clockwise);
        }
    }

    [Fact]
    public void Easing_tokens_start_at_rest_and_never_overshoot()
    {
        CubicBezierEasing[] tokens =
        [
            LivingCoreEasings.Breath,
            LivingCoreEasings.Strike,
            LivingCoreEasings.Settle,
            LivingCoreEasings.InOut,
        ];

        foreach (var easing in tokens)
        {
            Assert.Equal(0.0, easing.Evaluate(0.0));
            Assert.Equal(1.0, easing.Evaluate(1.0));

            var previous = 0.0;
            for (var i = 1; i <= 1000; i++)
            {
                var value = easing.Evaluate(i / 1000.0);

                // Motion board: "Water has weight": nothing overshoots its
                // target by more than 2%, and nothing moves backwards.
                Assert.InRange(value, previous - 1e-6, 1.02);
                previous = value;
            }
        }
    }

    [Fact]
    public void Breath_is_symmetric_so_loops_hand_over_cleanly()
    {
        Assert.Equal(0.5, LivingCoreEasings.Breath.Evaluate(0.5), 1e-3);
        Assert.Equal(
            1.0 - LivingCoreEasings.Breath.Evaluate(0.25),
            LivingCoreEasings.Breath.Evaluate(0.75),
            1e-3);
    }
}
