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
        LivingCoreState.Success,
        LivingCoreState.Warning,
        LivingCoreState.Sleep,
        LivingCoreState.Wake,
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
    public void Success_lifts_up_and_forward()
    {
        // Prototype "success": translate(0.68, -0.69) of the zone, 108%.
        Assert.Equal(LivingCoreLooks.ZoneX + (0.68 * LivingCoreLooks.ZoneRadiusX), LivingCoreLooks.Success.Core.X, 12);
        Assert.Equal(LivingCoreLooks.ZoneY - (0.69 * LivingCoreLooks.ZoneRadiusY), LivingCoreLooks.Success.Core.Y, 12);
        Assert.Equal(1.08, LivingCoreLooks.Success.Core.Scale);
        Assert.True(LivingCoreLooks.Success.Core.Glow > LivingCoreLooks.Idle.Core.Glow);
        Assert.Equal(1.0, LivingCoreLooks.Success.Halo.RimSuccess);
        Assert.Equal(0.0, LivingCoreLooks.Success.Halo.RimDanger);
    }

    [Fact]
    public void Warning_holds_back_steady_and_never_reads_as_an_error()
    {
        // Prototype "warning": translate(0.37, -0.125), 92%, lens 80% tall,
        // crest up 4, threads at half speed, aura at 60%.
        var warning = LivingCoreLooks.Warning;

        Assert.Equal(LivingCoreLooks.ZoneX + (0.37 * LivingCoreLooks.ZoneRadiusX), warning.Core.X, 12);
        Assert.Equal(LivingCoreLooks.ZoneY - (0.125 * LivingCoreLooks.ZoneRadiusY), warning.Core.Y, 12);
        Assert.Equal(0.92, warning.Core.Scale);
        Assert.Equal(0.0, warning.Core.Drift);
        Assert.Equal(0.8, warning.Inside.LensNarrow);
        Assert.Equal(-4.0, warning.Inside.CrestLift);
        Assert.Equal(0.0, warning.Inside.LensSway);
        Assert.Equal(1.0, warning.Halo.WarningMark);
        Assert.Equal(0.5, warning.Halo.ThreadSpeed1, 12);
        Assert.Equal(0.5, warning.Halo.ThreadSpeed2, 12);
        Assert.Equal(0.5, warning.Halo.ThreadSpeed3, 12);
        Assert.Equal(0.5, warning.Halo.ThreadSpeed4, 12);
        Assert.Equal(0.6, warning.Aura.AuraOpacity);

        // "Never aggressive": the error's red rim and crack stay off.
        Assert.Equal(0.0, warning.Halo.RimDanger);
        Assert.Equal(0.0, warning.Inside.CrackOpacity);
    }

    [Fact]
    public void Sleep_is_an_ember_in_a_sunken_dimmed_body()
    {
        // Prototype "sleep": body down 9, 86%, 60% light; threads stop.
        var sleep = LivingCoreLooks.Sleep;

        Assert.Equal(1.0, sleep.Core.Ember);
        Assert.Equal(0.0, sleep.Core.Glow);
        Assert.Equal(0.0, sleep.Core.Drift);
        Assert.Equal(9.0, sleep.Halo.BodyDrop);
        Assert.Equal(0.86, sleep.Halo.BodyScale);
        Assert.Equal(0.6, sleep.Halo.BodyOpacity);
        Assert.Equal(0.0, sleep.Halo.ThreadOpacity);
        Assert.Equal(0.2, sleep.Aura.AuraOpacity);
        Assert.Equal(0.15, sleep.Inside.StarOpacity);
    }

    [Fact]
    public void Sleep_and_wake_rest_where_the_logo_puts_the_white_core()
    {
        Assert.Equal(LivingCoreLooks.RestX, LivingCoreLooks.Sleep.Core.X);
        Assert.Equal(LivingCoreLooks.RestY, LivingCoreLooks.Sleep.Core.Y);
        Assert.Equal(LivingCoreLooks.RestX, LivingCoreLooks.Wake.Core.X);
        Assert.Equal(LivingCoreLooks.RestY, LivingCoreLooks.Wake.Core.Y);
        Assert.Equal(LivingCoreLooks.Idle.Core.Scale, LivingCoreLooks.Wake.Core.Scale);
    }

    [Theory]
    [MemberData(nameof(States))]
    public void Every_state_has_its_own_look(LivingCoreState state)
    {
        Assert.Equal(state == LivingCoreState.Idle, LivingCoreLooks.For(state).Equals(LivingCoreLooks.Idle));
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
            LivingCoreEasings.RingOut,
            LivingCoreEasings.RingIn,
            LivingCoreEasings.Sink,
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
