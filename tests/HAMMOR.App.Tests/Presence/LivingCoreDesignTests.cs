using HAMMOR.App.Presence;
using Xunit;

namespace HAMMOR.App.Tests.Presence;

/// <summary>
/// The Living Core's fixed design against the approved reference: state
/// poses and resting values, the prototype's keyframes, the build board's
/// budget, and the easing tokens.
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
        Assert.Equal(76.0, LivingCoreLooks.Idle.X);
        Assert.Equal(57.5, LivingCoreLooks.Idle.Y);
        Assert.Equal(1.0, LivingCoreLooks.Idle.Scale);
        Assert.Equal(1.0, LivingCoreLooks.Idle.Opacity);
    }

    [Fact]
    public void State_poses_match_the_reference()
    {
        // The prototype's pose table, as fractions of the zone.
        Assert.Equal(1.12, LivingCoreLooks.Listening.Scale);
        Assert.Equal(LivingCoreLooks.ZoneX, LivingCoreLooks.Listening.X);
        Assert.Equal(LivingCoreLooks.ZoneY, LivingCoreLooks.Listening.Y);
        Assert.Equal(1.0, LivingCoreLooks.Listening.AttentionRing);
        Assert.Equal(1.0, LivingCoreLooks.Listening.InwardRings);

        Assert.Equal(LivingCoreLooks.ZoneX - (0.35 * LivingCoreLooks.ZoneRadiusX), LivingCoreLooks.Thinking.X, 12);
        Assert.Equal(LivingCoreLooks.ZoneY - (0.2 * LivingCoreLooks.ZoneRadiusY), LivingCoreLooks.Thinking.Y, 12);
        Assert.Equal(0.78, LivingCoreLooks.Thinking.Scale);
        Assert.Equal(0.55, LivingCoreLooks.Thinking.Opacity);
        Assert.Equal(1.0, LivingCoreLooks.Thinking.Links);

        Assert.Equal(1.25, LivingCoreLooks.Speaking.Scale);
        Assert.Equal(LivingCoreLooks.ZoneX, LivingCoreLooks.Speaking.X);
        Assert.Equal(1.0, LivingCoreLooks.Speaking.OutwardRings);
        Assert.Equal(0.95, LivingCoreLooks.Speaking.VoiceWave);

        Assert.Equal(LivingCoreLooks.ZoneX + (0.16 * LivingCoreLooks.ZoneRadiusX), LivingCoreLooks.Blocked.X, 12);
        Assert.Equal(LivingCoreLooks.ZoneY + (0.31 * LivingCoreLooks.ZoneRadiusY), LivingCoreLooks.Blocked.Y, 12);
        Assert.Equal(0.94, LivingCoreLooks.Blocked.Scale);

        Assert.Equal(LivingCoreLooks.ZoneX + (0.68 * LivingCoreLooks.ZoneRadiusX), LivingCoreLooks.Error.X, 12);
        Assert.Equal(LivingCoreLooks.ZoneY + (0.06 * LivingCoreLooks.ZoneRadiusY), LivingCoreLooks.Error.Y, 12);
        Assert.Equal(0.9, LivingCoreLooks.Error.Scale);
        Assert.Equal(0.6, LivingCoreLooks.Error.Opacity);
    }

    [Fact]
    public void Success_lifts_up_and_forward()
    {
        // Prototype "success": translate(0.68, -0.69) of the zone, 108%.
        Assert.Equal(LivingCoreLooks.ZoneX + (0.68 * LivingCoreLooks.ZoneRadiusX), LivingCoreLooks.Success.X, 12);
        Assert.Equal(LivingCoreLooks.ZoneY - (0.69 * LivingCoreLooks.ZoneRadiusY), LivingCoreLooks.Success.Y, 12);
        Assert.Equal(1.08, LivingCoreLooks.Success.Scale);
        Assert.Equal(1.0, LivingCoreLooks.Success.Warm);
        Assert.Equal(1.0, LivingCoreLooks.Success.SuccessRing);
        Assert.Equal(1.0, LivingCoreLooks.Success.RimSuccess);
        Assert.Equal(0.0, LivingCoreLooks.Success.RimDanger);
    }

    [Fact]
    public void Warning_holds_back_steady_and_never_reads_as_an_error()
    {
        // Prototype "warning": translate(0.37, -0.125), 92%, lens 80% tall,
        // crest up 4, aura at 60%.
        var warning = LivingCoreLooks.Warning;

        Assert.Equal(LivingCoreLooks.ZoneX + (0.37 * LivingCoreLooks.ZoneRadiusX), warning.X, 12);
        Assert.Equal(LivingCoreLooks.ZoneY - (0.125 * LivingCoreLooks.ZoneRadiusY), warning.Y, 12);
        Assert.Equal(0.92, warning.Scale);
        Assert.Equal(0.8, warning.LensScaleY);
        Assert.Equal(-4.0, warning.CrestLift);
        Assert.Equal(1.0, warning.WarningMark);
        Assert.Equal(1.0, warning.RimWarning);
        Assert.Equal(0.6, warning.AuraOpacity);

        // "Never aggressive": the error's red rim and crack stay off.
        Assert.Equal(0.0, warning.RimDanger);
        Assert.Equal(0.0, warning.Crack);
    }

    [Fact]
    public void Sleep_is_an_ember_in_a_sunken_dimmed_body()
    {
        // Prototype "sleep": body down 9 at 86% and 60% light; threads off;
        // the cells laid down 38 below at 40% of their height.
        var sleep = LivingCoreLooks.Sleep;

        Assert.Equal(1.0, sleep.Ember);
        Assert.Equal(0.0, sleep.Glow);
        Assert.Equal(0.6, sleep.BodyOpacity);
        Assert.Equal(0.86, sleep.BodyScale);
        Assert.Equal(9.0, sleep.BodyDrop);
        Assert.Equal(0.0, sleep.ThreadsOpacity);
        Assert.Equal(0.2, sleep.AuraOpacity);
        Assert.Equal(0.15, sleep.StarsOpacity);
        Assert.Equal(0.4, sleep.CellsScaleY);
        Assert.Equal(38.0, sleep.CellsDrop);
        Assert.Equal(0.3, sleep.EnergyOpacity);
    }

    [Fact]
    public void Sleep_and_wake_rest_where_the_logo_puts_the_white_core()
    {
        Assert.Equal(LivingCoreLooks.RestX, LivingCoreLooks.Sleep.X);
        Assert.Equal(LivingCoreLooks.RestY, LivingCoreLooks.Sleep.Y);
        Assert.Equal(LivingCoreLooks.RestX, LivingCoreLooks.Wake.X);
        Assert.Equal(LivingCoreLooks.RestY, LivingCoreLooks.Wake.Y);
        Assert.Equal(LivingCoreLooks.Idle.Scale, LivingCoreLooks.Wake.Scale);
    }

    [Theory]
    [MemberData(nameof(States))]
    public void Every_state_has_its_own_look(LivingCoreState state)
    {
        // Wake settles into Idle; its difference is the ignition.
        var isIdle = state is LivingCoreState.Idle or LivingCoreState.Wake;
        Assert.Equal(isIdle, LivingCoreLooks.For(state).Equals(LivingCoreLooks.Idle));
    }

    [Fact]
    public void Blocked_waits_without_an_alarm_colour()
    {
        // State board: "No alarm colour: waiting, not failing."
        Assert.Equal(0.0, LivingCoreLooks.Blocked.RimDanger);
        Assert.Equal(0.0, LivingCoreLooks.Blocked.Crack);
        Assert.Equal(1.0, LivingCoreLooks.Blocked.GapRing);
        Assert.Equal(1.0, LivingCoreLooks.Blocked.Park);
        Assert.Equal(0.0, LivingCoreLooks.Blocked.ThreadsOpacity);
    }

    [Theory]
    [MemberData(nameof(States))]
    public void Every_pose_sits_inside_the_white_core_zone(LivingCoreState state)
    {
        var look = LivingCoreLooks.For(state);
        var nx = (look.X - LivingCoreLooks.ZoneX) / LivingCoreLooks.ZoneRadiusX;
        var ny = (look.Y - LivingCoreLooks.ZoneY) / LivingCoreLooks.ZoneRadiusY;

        Assert.InRange(Math.Sqrt((nx * nx) + (ny * ny)), 0.0, 1.0);
    }

    [Fact]
    public void The_reference_keyframes_are_transcribed_exactly()
    {
        // Spot checks against the prototype's @keyframes.
        Assert.Equal(1.47, PrototypeKeyframes.SpeakPulse.Track(KeyframeChannel.Scale)!.Evaluate(0.6, LivingCoreEasings.Linear), 12);
        Assert.Equal(1.5, PrototypeKeyframes.WakePupil.Track(KeyframeChannel.Scale)!.Evaluate(0.12, LivingCoreEasings.Linear), 12);
        Assert.Equal(9.0, PrototypeKeyframes.RequestGlance.Track(KeyframeChannel.X)!.Evaluate(0.3, LivingCoreEasings.Linear), 12);
        Assert.Equal(-3.5, PrototypeKeyframes.Rise.Track(KeyframeChannel.Y)!.Evaluate(0.25, LivingCoreEasings.Linear), 12);
        Assert.Equal(0.875, PrototypeKeyframes.SleepBreath.Track(KeyframeChannel.Scale)!.Evaluate(0.5, LivingCoreEasings.Linear), 12);
        Assert.True(PrototypeKeyframes.WakeHalo.Once);
        Assert.True(PrototypeKeyframes.WakeHalo.FillBoth);
        Assert.True(PrototypeKeyframes.Ripple.Once);
        Assert.False(PrototypeKeyframes.Ripple.FillBoth);
        Assert.False(PrototypeKeyframes.SuccessRing.Once);
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
            LivingCoreEasings.Linear,
            LivingCoreEasings.Ease,
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
