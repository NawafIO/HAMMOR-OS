using HAMMOR.App.Presence;
using Xunit;

namespace HAMMOR.App.Tests.Presence;

/// <summary>
/// The Living Core against the approved reference: the canvas prototype's
/// pointer, depth and events, Success, Warning, Sleep and Wake, how state
/// changes glide or (where the reference does) jump, and reduced motion.
/// </summary>
public sealed class LivingCoreLifeTests
{
    private const double NoVoice = double.NaN;
    private const double Leading = -1.0;
    private const double Frame = 1.0 / MotionHarness.Fps;

    public static TheoryData<LivingCoreState> AllStates => new()
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

    // ---- Pointer (Idle only) ----

    [Theory]
    [InlineData(-3.0, LivingCoreLooks.ZoneX - (LivingCoreMotion.PointerReach * LivingCoreLooks.ZoneRadiusX))]
    [InlineData(3.0, LivingCoreLooks.ZoneX + (LivingCoreMotion.PointerReach * LivingCoreLooks.ZoneRadiusX))]
    public void The_white_core_follows_the_pointer_across_its_zone(double pointerX, double expectedX)
    {
        var motion = Idle(out var frame);
        MotionHarness.Step(motion, frame, 8.0, 9.0, before: t => motion.SetPointer(pointerX, 0.0, t));

        // Only the idle drift (under a unit) on top of the pointer's place.
        Assert.InRange(frame.CoreX, expectedX - 0.85, expectedX + 0.85);
    }

    [Fact]
    public void The_pointer_is_followed_450_ms_behind_and_never_overshot()
    {
        var motion = Idle(out var frame);
        var twin = Idle(out var twinFrame);
        var target = LivingCoreLooks.ZoneX - (LivingCoreMotion.PointerReach * LivingCoreLooks.ZoneRadiusX);
        var shares = new List<double>();

        // The twin has no pointer: the difference is the pointer's alone.
        motion.SetPointer(-3.0, 0.0, 8.0);
        MotionHarness.Step(motion, frame, 8.0, 8.6, after: (t, f) =>
        {
            twin.Advance(t, NoVoice, Leading, twinFrame);
            shares.Add((f.CoreX - twinFrame.CoreX) / (target - LivingCoreLooks.RestX));
        });

        AssertMonotonic(shares, rising: true);
        Assert.Equal(0.0, shares[0], 1e-12);
        Assert.Equal(1.0, shares[(int)Math.Round(LivingCoreMotion.PointerLag * MotionHarness.Fps)], 1e-9);
        Assert.InRange(shares.Max(), 0.0, 1.0 + 1e-9);
    }

    [Fact]
    public void The_pointer_is_let_go_after_five_seconds_of_stillness()
    {
        var motion = Idle(out var frame);
        var twin = Idle(out var twinFrame);
        var target = LivingCoreLooks.ZoneX - (LivingCoreMotion.PointerReach * LivingCoreLooks.ZoneRadiusX);
        MotionHarness.Step(motion, frame, 8.0, 8.5, before: t => motion.SetPointer(-3.0, 0.0, t), after: (t, _) => twin.Advance(t, NoVoice, Leading, twinFrame));

        // Last move at 8.5 s: it still follows just before 13.5 s...
        MotionHarness.Step(motion, frame, 8.5 + Frame, 13.45, after: (t, _) => twin.Advance(t, NoVoice, Leading, twinFrame));
        Assert.Equal(target - LivingCoreLooks.RestX, frame.CoreX - twinFrame.CoreX, 1e-9);

        // ...then glides back to rest in 450 ms.
        MotionHarness.Step(motion, frame, 13.45 + Frame, 14.1, after: (t, _) => twin.Advance(t, NoVoice, Leading, twinFrame));
        Assert.Equal(0.0, frame.CoreX - twinFrame.CoreX, 1e-9);
        Assert.InRange(MotionHarness.DistanceFromRest(frame), 0.0, 1.0);
    }

    [Fact]
    public void Leaving_the_window_lets_go_after_1_2_seconds()
    {
        var motion = Idle(out var frame);
        var twin = Idle(out var twinFrame);
        var target = LivingCoreLooks.ZoneX - (LivingCoreMotion.PointerReach * LivingCoreLooks.ZoneRadiusX);
        MotionHarness.Step(motion, frame, 8.0, 8.5, before: t => motion.SetPointer(-3.0, 0.0, t), after: (t, _) => twin.Advance(t, NoVoice, Leading, twinFrame));
        motion.ReleasePointer(8.5 + LivingCoreMotion.PointerLeaveRelease);

        MotionHarness.Step(motion, frame, 8.5 + Frame, 9.6, after: (t, _) => twin.Advance(t, NoVoice, Leading, twinFrame));
        Assert.Equal(target - LivingCoreLooks.RestX, frame.CoreX - twinFrame.CoreX, 1e-9);
        MotionHarness.Step(motion, frame, 9.6 + Frame, 10.2, after: (t, _) => twin.Advance(t, NoVoice, Leading, twinFrame));
        Assert.Equal(0.0, frame.CoreX - twinFrame.CoreX, 1e-9);
    }

    [Theory]
    [InlineData(LivingCoreState.Listening)]
    [InlineData(LivingCoreState.Thinking)]
    [InlineData(LivingCoreState.Speaking)]
    [InlineData(LivingCoreState.Blocked)]
    [InlineData(LivingCoreState.Error)]
    [InlineData(LivingCoreState.Success)]
    [InlineData(LivingCoreState.Warning)]
    [InlineData(LivingCoreState.Sleep)]
    [InlineData(LivingCoreState.Wake)]
    public void Only_idle_follows_the_pointer(LivingCoreState state)
    {
        var motion = new LivingCoreMotion(state, 0.0, fromDormant: false);
        var twin = new LivingCoreMotion(state, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        var twinFrame = new LivingCoreFrame();

        MotionHarness.Step(motion, frame, 0.0, 10.0, before: t => motion.SetPointer(-1.5, 1.0, t), after: (t, f) =>
        {
            twin.Advance(t, NoVoice, Leading, twinFrame);
            Assert.Equal(MotionHarness.Snapshot(twinFrame), MotionHarness.Snapshot(f));
        });
    }

    [Fact]
    public void The_pointer_never_takes_the_core_past_its_zone_by_more_than_the_drift()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();

        MotionHarness.Step(motion, frame, 0.0, 60.0,
            before: t => motion.SetPointer(2.0 * Math.Cos(t * 1.3), 2.0 * Math.Sin(t * 1.3), t),
            after: (_, f) => Assert.InRange(MotionHarness.ZoneRadius(f, margin: 0.8), 0.0, 1.0 + 1e-9));
    }

    [Fact]
    public void A_new_pointer_target_is_chased_from_where_the_core_is()
    {
        var motion = Idle(out var frame);
        MotionHarness.Step(motion, frame, 8.0, 8.2, before: t => motion.SetPointer(-3.0, 0.0, t));
        var before = frame.CoreX;

        motion.SetPointer(3.0, 0.0, 8.2);
        motion.Advance(8.2, NoVoice, Leading, frame);
        Assert.Equal(before, frame.CoreX, 1e-9);

        MotionHarness.Step(motion, frame, 8.2 + Frame, 9.0);
        Assert.True(frame.CoreX > LivingCoreLooks.ZoneX + 15.0, $"core x {frame.CoreX}");
    }

    // ---- Depth ----

    [Fact]
    public void Depth_moves_the_cells_with_the_core_and_the_stars_against_it()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        motion.Advance(1.0, NoVoice, Leading, frame);

        // At rest, measured from the zone's centre as the prototype does.
        Assert.Equal(0.3 * (LivingCoreLooks.RestX - LivingCoreLooks.ZoneX), frame.CellsShiftX, 1e-9);
        Assert.Equal(0.3 * (LivingCoreLooks.RestY - LivingCoreLooks.ZoneY), frame.CellsShiftY, 1e-9);
        Assert.Equal(-0.1 * (LivingCoreLooks.RestX - LivingCoreLooks.ZoneX), frame.StarsShiftX, 1e-9);

        MotionHarness.Step(motion, frame, 1.0 + Frame, 8.0);
        MotionHarness.Step(motion, frame, 8.0 + Frame, 9.5, before: t => motion.SetPointer(-3.0, 0.0, t));
        var offset = -LivingCoreMotion.PointerReach * LivingCoreLooks.ZoneRadiusX;
        Assert.Equal(LivingCoreMotion.CellsDepth * offset, frame.CellsShiftX, 1e-9);
        Assert.Equal(LivingCoreMotion.StarsDepth * offset, frame.StarsShiftX, 1e-9);
    }

    [Fact]
    public void Depth_keeps_every_star_and_cell_inside_the_membrane()
    {
        // Wherever the white core looks on its zone's edge, and wherever the
        // cells are in their bob.
        for (var k = 0; k < 360; k++)
        {
            var angle = 2.0 * Math.PI * k / 360.0;
            var gx = LivingCoreLooks.ZoneRadiusX * Math.Cos(angle);
            var gy = LivingCoreLooks.ZoneRadiusY * Math.Sin(angle);

            foreach (var star in LivingCoreDesign.Stars)
            {
                var reach = Distance(star.X + (LivingCoreMotion.StarsDepth * gx), star.Y + (LivingCoreMotion.StarsDepth * gy)) + star.Radius;
                Assert.True(reach < LivingCoreDesign.MembraneRadius, $"star ({star.X}, {star.Y}) reaches {reach}");
            }

            foreach (var cell in LivingCoreDesign.Cells)
            {
                foreach (var up in new[] { 0.0, 1.0 })
                {
                    var x = cell.X + (LivingCoreDesign.BobX * up) + (LivingCoreMotion.CellsDepth * gx);
                    var y = cell.Y + (LivingCoreDesign.BobY * up) + (LivingCoreMotion.CellsDepth * gy);
                    var reach = Distance(x, y) + cell.Radius;
                    Assert.True(reach < LivingCoreDesign.MembraneRadius, $"cell ({cell.X}, {cell.Y}) reaches {reach}");
                }
            }
        }
    }

    [Fact]
    public void A_glance_moves_the_depth_with_it()
    {
        var motion = Idle(out var frame);
        motion.React(LivingCoreReactionKind.PanelOpened, -1.0, 8.0);
        MotionHarness.Step(motion, frame, 8.0, 9.0);

        var offset = -LivingCoreMotion.PanelGlance * LivingCoreLooks.ZoneRadiusX;
        Assert.Equal(LivingCoreMotion.CellsDepth * offset, frame.CellsShiftX, 1e-9);
        Assert.Equal(LivingCoreMotion.StarsDepth * offset, frame.StarsShiftX, 1e-9);
    }

    // ---- Reactions ----

    [Fact]
    public void A_new_message_sends_one_ripple_from_the_membrane()
    {
        var motion = Idle(out var frame);
        motion.React(LivingCoreReactionKind.NewMessage, 1.0, 8.0);
        motion.Advance(8.0, NoVoice, Leading, frame);
        Assert.Equal(0.75, frame.RippleOpacity, 1e-9);
        Assert.Equal(1.0, frame.RippleScale, 1e-9);

        var widest = 0.0;
        MotionHarness.Step(motion, frame, 8.0 + Frame, 8.85, after: (_, f) => widest = Math.Max(widest, f.RippleScale));
        Assert.True(widest > 1.3, $"ripple only reached {widest}");

        // Gone after 0.9 s.
        MotionHarness.Step(motion, frame, 8.85 + Frame, 9.0);
        Assert.Equal(0.0, frame.RippleOpacity);
        Assert.Equal(1.0, frame.RippleScale);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    public void A_new_message_draws_one_glance_toward_it_then_back(double side)
    {
        var motion = Idle(out var frame);
        motion.React(LivingCoreReactionKind.NewMessage, side, 8.0);
        MotionHarness.Step(motion, frame, 8.0, 8.75);

        // Toward the transcript and down; (0.5, 0.9) of the zone sits just
        // past its edge, so it is held on the edge, as in the prototype.
        var (x, y) = OnZoneEdge(side * 0.5, 0.9);
        Assert.InRange(frame.CoreX, x - 0.85, x + 0.85);
        Assert.InRange(frame.CoreY, y - 0.85, y + 0.85);

        // The glance lasts 1.2 s, then it glides back.
        MotionHarness.Step(motion, frame, 8.75 + Frame, 10.3);
        Assert.InRange(MotionHarness.DistanceFromRest(frame), 0.0, 1.0);
    }

    [Fact]
    public void Each_new_message_restarts_the_ripple_and_extends_the_glance()
    {
        var motion = Idle(out var frame);
        motion.React(LivingCoreReactionKind.NewMessage, 1.0, 8.0);
        MotionHarness.Step(motion, frame, 8.0, 8.5);

        motion.React(LivingCoreReactionKind.NewMessage, 1.0, 8.5);
        motion.Advance(8.5, NoVoice, Leading, frame);
        Assert.Equal(0.75, frame.RippleOpacity, 1e-9);

        // Still looking at 9.5 s, 1.5 s after the first message.
        MotionHarness.Step(motion, frame, 8.5 + Frame, 9.5);
        var (x, y) = OnZoneEdge(0.5, 0.9);
        Assert.InRange(frame.CoreX, x - 0.85, x + 0.85);
        Assert.InRange(frame.CoreY, y - 0.85, y + 0.85);
    }

    [Theory]
    [InlineData(LivingCoreState.Thinking)]
    [InlineData(LivingCoreState.Listening)]
    [InlineData(LivingCoreState.Speaking)]
    [InlineData(LivingCoreState.Sleep)]
    public void A_message_while_busy_or_asleep_ripples_without_a_glance(LivingCoreState state)
    {
        var motion = new LivingCoreMotion(state, 0.0, fromDormant: false);
        var twin = new LivingCoreMotion(state, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        var twinFrame = new LivingCoreFrame();
        MotionHarness.Run(motion, frame, 0.0, 6.0 - Frame);
        MotionHarness.Run(twin, twinFrame, 0.0, 6.0 - Frame);
        motion.React(LivingCoreReactionKind.NewMessage, 1.0, 6.0);

        var ripple = 0.0;
        MotionHarness.Step(motion, frame, 6.0, 9.0, after: (t, f) =>
        {
            twin.Advance(t, NoVoice, Leading, twinFrame);
            ripple = Math.Max(ripple, f.RippleOpacity);
            Assert.Equal(twinFrame.CoreX, f.CoreX, 1e-12);
            Assert.Equal(twinFrame.CoreY, f.CoreY, 1e-12);
        });

        Assert.True(ripple > 0.7, $"ripple {ripple}");
    }

    [Theory]
    [InlineData(LivingCoreState.Idle)]
    [InlineData(LivingCoreState.Blocked)]
    [InlineData(LivingCoreState.Error)]
    [InlineData(LivingCoreState.Warning)]
    [InlineData(LivingCoreState.Success)]
    public void A_message_draws_a_glance_in_idle_and_in_the_waiting_states(LivingCoreState state)
    {
        var look = LivingCoreLooks.For(state);
        var motion = new LivingCoreMotion(state, 0.0, fromDormant: false);
        var twin = new LivingCoreMotion(state, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        var twinFrame = new LivingCoreFrame();
        MotionHarness.Run(motion, frame, 0.0, 4.0);
        MotionHarness.Run(twin, twinFrame, 0.0, 4.0);
        motion.React(LivingCoreReactionKind.NewMessage, 1.0, 4.0);
        MotionHarness.Step(motion, frame, 4.0 + Frame, 4.75, after: (t, _) => twin.Advance(t, NoVoice, Leading, twinFrame));

        // Measured against a twin, so the drift and Blocked's own glance cancel out.
        var (x, y) = OnZoneEdge(0.5, 0.9);
        Assert.Equal(x - look.X, frame.CoreX - twinFrame.CoreX, 1e-9);
        Assert.Equal(y - look.Y, frame.CoreY - twinFrame.CoreY, 1e-9);
    }

    [Fact]
    public void Opening_a_panel_draws_a_glance_and_leans_the_aura_its_way()
    {
        var motion = Idle(out var frame);
        motion.React(LivingCoreReactionKind.PanelOpened, -1.0, 8.0);
        MotionHarness.Step(motion, frame, 8.0, 9.3);

        var x = LivingCoreLooks.ZoneX - (LivingCoreMotion.PanelGlance * LivingCoreLooks.ZoneRadiusX);
        Assert.InRange(frame.CoreX, x - 0.85, x + 0.85);
        Assert.Equal(-LivingCoreMotion.PanelLean * LivingCoreMotion.PanelGlance, frame.AuraLeanX, 1e-9);

        // 2.4 s, then the core and the aura come back.
        MotionHarness.Step(motion, frame, 9.3 + Frame, 11.7);
        Assert.Equal(0.0, frame.AuraLeanX);
        Assert.InRange(MotionHarness.DistanceFromRest(frame), 0.0, 1.0);
    }

    [Theory]
    [InlineData(LivingCoreState.Listening)]
    [InlineData(LivingCoreState.Thinking)]
    [InlineData(LivingCoreState.Speaking)]
    [InlineData(LivingCoreState.Sleep)]
    public void The_aura_leans_toward_a_panel_even_while_the_core_holds_its_pose(LivingCoreState state)
    {
        var look = LivingCoreLooks.For(state);
        var motion = new LivingCoreMotion(state, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        MotionHarness.Run(motion, frame, 0.0, 2.0);
        motion.React(LivingCoreReactionKind.PanelOpened, 1.0, 2.0);
        MotionHarness.Step(motion, frame, 2.0 + Frame, 3.4);

        Assert.Equal(LivingCoreMotion.PanelLean * LivingCoreMotion.PanelGlance, frame.AuraLeanX, 1e-9);
        Assert.Equal(look.X, frame.CoreX, 1e-9);
        Assert.Equal(look.Y, frame.CoreY, 1e-9);
    }

    [Fact]
    public void A_message_glance_goes_before_a_panel_glance()
    {
        var motion = Idle(out var frame);
        motion.React(LivingCoreReactionKind.PanelOpened, -1.0, 8.0);
        MotionHarness.Step(motion, frame, 8.0, 9.0);
        Assert.True(frame.CoreX < LivingCoreLooks.ZoneX, $"core x {frame.CoreX}");

        // The prototype's order: a message wins; the aura keeps leaning.
        motion.React(LivingCoreReactionKind.NewMessage, 1.0, 9.0);
        MotionHarness.Step(motion, frame, 9.0, 9.7);
        var (x, _) = OnZoneEdge(0.5, 0.9);
        Assert.InRange(frame.CoreX, x - 0.85, x + 0.85);
        Assert.Equal(-LivingCoreMotion.PanelLean * LivingCoreMotion.PanelGlance, frame.AuraLeanX, 1e-9);

        // The message's 1.2 s end before the panel's 2.4 s: back to the panel.
        MotionHarness.Step(motion, frame, 9.7 + Frame, 10.35);
        Assert.True(frame.CoreX < LivingCoreLooks.ZoneX, $"core x {frame.CoreX}");
    }

    [Fact]
    public void Listening_takes_over_from_a_glance_from_where_the_core_is()
    {
        var motion = Idle(out var frame);
        motion.React(LivingCoreReactionKind.NewMessage, 1.0, 8.0);
        MotionHarness.Step(motion, frame, 8.0, 8.3);
        var (x, y) = (frame.CoreX, frame.CoreY);

        motion.SetState(LivingCoreState.Listening, 8.3);
        motion.Advance(8.3, NoVoice, Leading, frame);
        Assert.Equal(x - DriftAt(8.3).X, frame.CoreX, 1e-9);
        Assert.Equal(y - DriftAt(8.3).Y, frame.CoreY, 1e-9);

        MotionHarness.Step(motion, frame, 8.3 + Frame, 10.3);
        Assert.Equal(LivingCoreLooks.ZoneX, frame.CoreX, 1e-9);
        Assert.Equal(LivingCoreLooks.ZoneY, frame.CoreY, 1e-9);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 1.0)]
    public void Listening_follows_a_real_voice_level_when_one_is_connected(double level, double expected)
    {
        var motion = new LivingCoreMotion(LivingCoreState.Listening, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        var strongest = 0.0;

        MotionHarness.Run(motion, frame, 0.0, 4.0, (t, f) =>
        {
            if (t >= 2.0)
            {
                strongest = Math.Max(strongest, f.InwardOpacity.Max());
            }
        }, voice: level);

        Assert.Equal(expected, strongest, 0.01);
    }

    // ---- Success, Warning, Sleep, Wake ----

    [Fact]
    public void Success_lifts_brightens_and_blooms_over_one_3_2_second_cycle()
    {
        var motion = Idle(out var frame, until: 6.0);
        motion.SetState(LivingCoreState.Success, 6.0);

        var warmPeak = (Time: 0.0, Value: 0.0);
        var ringPeak = 0.0;
        var ringWidest = 0.0;
        var lowestRise = 0.0;
        var ringGoneFrom = double.NaN;
        MotionHarness.Step(motion, frame, 6.0, 9.15, after: (t, f) =>
        {
            if (f.WarmOpacity > warmPeak.Value)
            {
                warmPeak = (t, f.WarmOpacity);
            }

            ringPeak = Math.Max(ringPeak, f.SuccessRingOpacity);
            ringWidest = Math.Max(ringWidest, f.SuccessRingScale);
            lowestRise = Math.Min(lowestRise, f.CellsY);
            if (f.SuccessRingOpacity == 0.0 && t > 6.1 && double.IsNaN(ringGoneFrom))
            {
                ringGoneFrom = t;
            }
        });

        // The warm bloom starts at 40%, peaks a quarter of the way through, settles to 55%.
        Assert.Equal(1.0, warmPeak.Value, 1e-6);
        Assert.InRange(warmPeak.Time, 6.75, 6.85);
        Assert.Equal(0.55, frame.WarmOpacity, 1e-3);

        // One clean ring expands to 150% and dissolves within 45%.
        Assert.Equal(1.0, ringPeak, 1e-3);
        Assert.Equal(1.5, ringWidest, 1e-9);
        Assert.InRange(ringGoneFrom, 6.0 + (0.45 * LivingCoreMotion.SuccessSeconds) - Frame, 6.0 + (0.45 * LivingCoreMotion.SuccessSeconds) + Frame);

        // The cells rise together and settle back.
        Assert.Equal(-3.5, lowestRise, 1e-6);
        Assert.Equal(0.0, frame.CellsY, 0.01);

        // Lifted up and forward, 108%, in the success tone.
        Assert.Equal(LivingCoreLooks.Success.X, frame.CoreX, 1e-9);
        Assert.Equal(LivingCoreLooks.Success.Y, frame.CoreY, 1e-9);
        Assert.Equal(1.08, frame.CoreScale, 1e-9);
        Assert.Equal(1.0, frame.RimSuccess);
    }

    [Fact]
    public void Success_begins_its_bloom_on_the_first_frame_and_ends_it_with_the_state()
    {
        var motion = Idle(out var frame, until: 6.0);
        motion.SetState(LivingCoreState.Success, 6.0);
        motion.Advance(6.0, NoVoice, Leading, frame);

        // The keyframes' first values, at once, as the reference shows them.
        Assert.Equal(0.4, frame.WarmOpacity, 1e-12);
        Assert.Equal(0.95, frame.WarmScale, 1e-12);
        Assert.Equal(1.0, frame.RimSuccess);

        MotionHarness.Step(motion, frame, 6.0 + Frame, 9.0);
        motion.SetState(LivingCoreState.Idle, 9.0);
        motion.Advance(9.0, NoVoice, Leading, frame);
        Assert.Equal(0.0, frame.WarmOpacity);
        Assert.Equal(0.0, frame.SuccessRingOpacity);
        Assert.Equal(0.0, frame.RimSuccess);
    }

    [Fact]
    public void Warning_holds_back_narrows_the_lens_and_marks_the_concern()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Warning, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        motion.Advance(0.0, NoVoice, Leading, frame);

        var mark = new List<double>();
        var halo = new List<double>();
        MotionHarness.Step(motion, frame, Frame, 6.4, after: (_, f) =>
        {
            mark.Add(f.WarningMarkOpacity);
            halo.Add(f.HaloScale[0]);
            Assert.Equal(LivingCoreLooks.ZoneX + (0.37 * LivingCoreLooks.ZoneRadiusX), f.CoreX, 1e-9);
            Assert.Equal(LivingCoreLooks.ZoneY - (0.125 * LivingCoreLooks.ZoneRadiusY), f.CoreY, 1e-9);
            Assert.Equal(0.92, f.CoreScale, 1e-9);
            Assert.Equal(0.8, f.LensScaleY, 1e-9);
            Assert.Equal(-4.0, f.CrestY, 1e-9);
            Assert.Equal(0.0, f.CrestAngle);
            Assert.Equal(1.0, f.RimWarning);
            Assert.All(f.BobY, y => Assert.Equal(0.0, y));
        });

        Assert.InRange(mark.Min(), 0.35 - 1e-9, 0.36);
        Assert.InRange(mark.Max(), 0.99, 1.0);
        Assert.InRange(halo.Min(), 0.97 - 1e-9, 0.971);
        Assert.InRange(halo.Max(), 0.979, 0.98 + 1e-9);

        // Threads at half speed: the first one's 14 s orbit takes 28 s.
        var probe = new LivingCoreMotion(LivingCoreState.Warning, 0.0, fromDormant: false);
        var probeFrame = new LivingCoreFrame();
        probe.Advance(0.0, NoVoice, Leading, probeFrame);
        var thread = probeFrame.ThreadAngle[0];
        MotionHarness.Run(probe, probeFrame, 0.0, 1.0);
        Assert.Equal(360.0 / 28.0, probeFrame.ThreadAngle[0] - thread, 1e-6);
    }

    [Fact]
    public void Sleep_sinks_shrinks_and_dims_to_an_ember()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Sleep, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        var smallest = double.MaxValue;
        var largest = 0.0;
        var bob = new List<double>();

        MotionHarness.Step(motion, frame, 0.0, 9.6, after: (_, f) =>
        {
            smallest = Math.Min(smallest, f.BodyScale);
            largest = Math.Max(largest, f.BodyScale);
            bob.Add(f.BobY[0]);
            Assert.Equal(LivingCoreLooks.RestX, f.CoreX, 1e-9);
            Assert.Equal(LivingCoreLooks.RestY, f.CoreY, 1e-9);
        });

        // The 9.6 s breath, between 86% and 87.5%, 9 units down.
        Assert.Equal(0.86, smallest, 1e-6);
        Assert.Equal(0.875, largest, 1e-6);
        Assert.Equal(9.0, frame.BodyY);
        Assert.Equal(0.6, frame.BodyOpacity, 1e-9);
        Assert.Equal(1.0, frame.EmberOpacity, 1e-9);
        Assert.Equal(0.0, frame.GlowOpacity);
        Assert.Equal(0.0, frame.ThreadsOpacity);
        Assert.Equal(0.3, frame.EnergyOpacity, 1e-9);

        // The school settles to the floor, as in the reference: 38 down, 40% tall.
        Assert.Equal(1.0, frame.CellsSpread, 1e-9);
        Assert.Equal(0.4, frame.CellsScaleY, 1e-9);
        Assert.Equal(38.0, frame.CellsY, 1e-9);
        Assert.All(frame.StarClockOpacity, o => Assert.InRange(o, 0.0, 0.15 + 1e-9));
        Assert.InRange(frame.AuraOpacity, 0.0, 0.2 + 1e-9);

        // Low energy, not dead: the cells still bob.
        Assert.True(bob.Max() - bob.Min() > 1.0, $"bob {bob.Min()}..{bob.Max()}");
        Assert.True(motion.IsResting(9.6));
    }

    [Fact]
    public void Sleep_drops_the_body_at_once_and_wake_lifts_it_at_once_as_the_reference_does()
    {
        var motion = Idle(out var frame, until: 2.0);
        motion.SetState(LivingCoreState.Sleep, 2.0);
        motion.Advance(2.0, NoVoice, Leading, frame);

        // The body's own animation starts at its first keyframe; its light fades.
        Assert.Equal(0.86, frame.BodyScale, 1e-12);
        Assert.Equal(9.0, frame.BodyY);
        Assert.Equal(1.0, frame.BodyOpacity, 1e-12);
        MotionHarness.Step(motion, frame, 2.0 + Frame, 2.4);
        Assert.InRange(frame.BodyOpacity, 0.61, 0.99);

        MotionHarness.Step(motion, frame, 2.4 + Frame, 5.0);
        motion.SetState(LivingCoreState.Wake, 5.0);
        motion.Advance(5.0, NoVoice, Leading, frame);
        Assert.Equal(1.0, frame.BodyScale);
        Assert.Equal(0.0, frame.BodyY);
    }

    [Fact]
    public void Wake_ignites_the_core_first_and_the_aura_last()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Sleep, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        MotionHarness.Run(motion, frame, 0.0, 4.0);
        motion.SetState(LivingCoreState.Wake, 4.0);
        Assert.False(motion.IsResting(4.0));

        // A point of light, then it swells past full (150% at 12%).
        motion.Advance(4.0, NoVoice, Leading, frame);
        Assert.Equal(0.0, frame.CoreScale);
        Assert.Equal(0.0, frame.AuraOpacity);
        motion.Advance(4.0 + (0.12 * LivingCoreMotion.WakeSeconds), NoVoice, Leading, frame);
        Assert.Equal(1.5, frame.CoreScale, 1e-9);

        // Each layer arrives in order: core, rim, lens, cells and stars,
        // halo and threads, aura last.
        var arrived = new Dictionary<string, double>();
        MotionHarness.Step(motion, frame, 4.0 + (0.12 * LivingCoreMotion.WakeSeconds), 6.6, after: (t, f) =>
        {
            Mark(arrived, "core", f.CoreOpacity, t);
            Mark(arrived, "rim", f.RimDrawn, t);
            Mark(arrived, "lens", f.LensOpacity, t);
            Mark(arrived, "cells", f.CellsOpacity, t);
            Mark(arrived, "threads", f.ThreadsOpacity, t);
            Mark(arrived, "aura", f.AuraOpacity, t);
        });

        string[] order = ["core", "rim", "lens", "cells", "threads", "aura"];
        for (var i = 1; i < order.Length; i++)
        {
            Assert.True(arrived[order[i - 1]] < arrived[order[i]], $"{order[i - 1]} at {arrived[order[i - 1]]} is not before {order[i]} at {arrived[order[i]]}");
        }

        // Then it holds the arrived core without a trace of the ignition or of Sleep.
        Assert.False(motion.IsWaking(6.6));
        Assert.Equal(1.0, frame.CoreScale, 1e-9);
        Assert.Equal(1.0, frame.RimDrawn);
        Assert.Equal(1.0, frame.BodyScale, 1e-9);
        Assert.Equal(0.0, frame.BodyY, 1e-9);
        Assert.Equal(0.0, frame.EmberOpacity, 1e-9);
    }

    // ---- Every state ----

    [Fact]
    public void Every_settled_state_is_clearly_distinct()
    {
        // Wake settles into Idle by design; its difference is the ignition.
        var states = Enum.GetValues<LivingCoreState>().Where(s => s != LivingCoreState.Wake).ToArray();
        var signatures = new Dictionary<LivingCoreState, double[]>();
        foreach (var state in states)
        {
            var motion = new LivingCoreMotion(state, 0.0, fromDormant: false, reducedMotion: true);
            var f = new LivingCoreFrame();
            motion.Advance(5.0, NoVoice, Leading, f);
            signatures[state] =
            [
                (f.CoreX - LivingCoreLooks.ZoneX) / LivingCoreLooks.ZoneRadiusX,
                (f.CoreY - LivingCoreLooks.ZoneY) / LivingCoreLooks.ZoneRadiusY,
                f.CoreScale, f.CoreOpacity, f.AttentionOpacity, f.SpeakRingOpacity,
                f.LinkOpacity, f.GapRingOpacity, f.FragmentOpacity, f.CrackOpacity,
                f.RimDanger, f.RimSuccess, f.RimWarning, f.WarningMarkOpacity,
                f.ThreadsOpacity, f.OutwardOpacity[0], f.InwardOpacity[0], f.HaloOpacity[0], f.AuraOpacity,
                f.BodyScale, f.BodyOpacity, f.EmberOpacity, f.LensScaleY, f.GlowOpacity, f.WarmOpacity,
            ];
        }

        for (var a = 0; a < states.Length; a++)
        {
            for (var b = a + 1; b < states.Length; b++)
            {
                var sum = 0.0;
                for (var i = 0; i < signatures[states[a]].Length; i++)
                {
                    var d = signatures[states[a]][i] - signatures[states[b]][i];
                    sum += d * d;
                }

                Assert.True(Math.Sqrt(sum) > 0.9, $"{states[a]} and {states[b]} look too alike: {Math.Sqrt(sum):0.000}");
            }
        }
    }

    [Fact]
    public void The_white_core_glides_to_every_new_pose()
    {
        // States without a wander of their own, so the frame is the pose.
        LivingCoreState[] sequence =
        [
            LivingCoreState.Thinking, LivingCoreState.Success, LivingCoreState.Listening, LivingCoreState.Error,
            LivingCoreState.Speaking, LivingCoreState.Warning, LivingCoreState.Sleep, LivingCoreState.Wake,
            LivingCoreState.Thinking, LivingCoreState.Error, LivingCoreState.Success,
        ];

        var motion = new LivingCoreMotion(LivingCoreState.Listening, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        double[]? previous = null;

        for (var i = 0; i < 120 + (sequence.Length * 80) + 120; i++)
        {
            var t = i / MotionHarness.Fps;
            if (i >= 120 && (i - 120) % 80 == 0 && (i - 120) / 80 < sequence.Length)
            {
                motion.SetState(sequence[(i - 120) / 80], t);
            }

            motion.Advance(t, NoVoice, Leading, frame);
            double[] now = [frame.CoreX, frame.CoreY];

            if (previous is not null)
            {
                // The largest pose change is about 20 units, on a 0.45 s settle curve.
                var moved = Math.Sqrt(Math.Pow(now[0] - previous[0], 2) + Math.Pow(now[1] - previous[1], 2));
                Assert.True(moved <= 3.5, $"core jumped {moved:0.00} units at {t:0.000} s");
            }

            previous = now;
        }
    }

    [Fact]
    public void A_change_during_a_change_starts_from_the_frame_on_screen()
    {
        var motion = Idle(out var frame, until: 10.0);
        motion.SetState(LivingCoreState.Thinking, 10.0);
        MotionHarness.Run(motion, frame, 10.0, 10.3);

        // Values that glide in both states pick up where they are.
        static double[] Gliding(LivingCoreFrame f) =>
        [
            f.CoreX, f.CoreY, f.CoreScale, f.CoreOpacity, f.LinkOpacity, f.OrbitOpacity,
            f.StarsShiftX, f.StarsShiftY, f.CellsShiftX, f.CellsShiftY, f.LensOpacity, f.ThreadsOpacity,
        ];

        var before = Gliding(frame);
        motion.SetState(LivingCoreState.Error, 10.3);
        motion.Advance(10.3, NoVoice, Leading, frame);
        var after = Gliding(frame);

        for (var i = 0; i < before.Length; i++)
        {
            Assert.Equal(before[i], after[i], 1e-9);
        }
    }

    [Fact]
    public void Leaving_blocked_ends_its_request_glance_at_once_as_the_reference_does()
    {
        // The reference's glance is an animation without a transition: when
        // Blocked ends mid-glance, it is gone on the next frame.
        var motion = Idle(out var frame, until: 2.0);
        motion.SetState(LivingCoreState.Blocked, 2.0);
        MotionHarness.Run(motion, frame, 2.0, 2.0 + (0.3 * 6.4), direction: 1.0);
        var glancing = frame.CoreX;

        motion.SetState(LivingCoreState.Error, 2.0 + (0.3 * 6.4) + Frame);
        motion.Advance(2.0 + (0.3 * 6.4) + Frame, NoVoice, 1.0, frame);
        Assert.Equal(glancing - 9.0, frame.CoreX, 1e-6);
    }

    [Fact]
    public void Overlays_with_their_own_rhythm_start_and_end_with_the_state()
    {
        var motion = Idle(out var frame, until: 2.0);
        motion.SetState(LivingCoreState.Warning, 2.0);
        motion.Advance(2.0, NoVoice, Leading, frame);

        // The amber mark's pulse starts at its first keyframe; the tone and
        // the crest switch at once; the lens narrows over 1.2 s.
        Assert.Equal(0.35, frame.WarningMarkOpacity, 1e-12);
        Assert.Equal(1.0, frame.RimWarning);
        Assert.Equal(-4.0, frame.CrestY);
        Assert.Equal(1.0, frame.LensScaleY, 1e-12);
        MotionHarness.Step(motion, frame, 2.0 + Frame, 2.3);
        Assert.InRange(frame.LensScaleY, 0.8 + 1e-3, 0.99);

        // Blocked's gap ring has no rhythm of its own: it fades in over 0.6 s.
        MotionHarness.Step(motion, frame, 2.3 + Frame, 4.0);
        motion.SetState(LivingCoreState.Blocked, 4.0);
        motion.Advance(4.0, NoVoice, Leading, frame);
        Assert.Equal(0.0, frame.WarningMarkOpacity);
        Assert.Equal(0.0, frame.GapRingOpacity);
        motion.Advance(4.3, NoVoice, Leading, frame);
        Assert.InRange(frame.GapRingOpacity, 0.2, 0.9);
        motion.Advance(4.61, NoVoice, Leading, frame);
        Assert.Equal(1.0, frame.GapRingOpacity);
    }

    // ---- Reduced motion ----

    [Theory]
    [MemberData(nameof(AllStates))]
    public void Reduced_motion_holds_every_state_still(LivingCoreState state)
    {
        var look = LivingCoreLooks.For(state);
        var motion = new LivingCoreMotion(state, 0.0, fromDormant: false, reducedMotion: true);
        var frame = new LivingCoreFrame();

        MotionHarness.Step(motion, frame, 0.0, 6.0,
            before: t =>
            {
                motion.SetPointer(-1.0, -1.0, t);
                motion.React(LivingCoreReactionKind.PanelOpened, -1.0, t);
                motion.React(LivingCoreReactionKind.NewMessage, 1.0, t);
            },
            after: (_, f) =>
            {
                Assert.Equal(look.X, f.CoreX, 1e-9);
                Assert.Equal(look.Y, f.CoreY, 1e-9);
                Assert.Equal(0.0, f.AuraLeanX);
                Assert.Equal(0.0, f.RippleOpacity);
                Assert.Equal(LivingCoreMotion.CellsDepth * (look.X - LivingCoreLooks.ZoneX), f.CellsShiftX, 1e-9);
            });

        Assert.False(motion.NeedsContinuousFrames(6.0));
    }

    [Fact]
    public void Reduced_motion_wakes_with_a_short_blend()
    {
        // Asleep without its breathing: sunk, shrunk and dimmed, still.
        var motion = new LivingCoreMotion(LivingCoreState.Sleep, 0.0, fromDormant: false, reducedMotion: true);
        var frame = new LivingCoreFrame();
        motion.Advance(1.0, NoVoice, Leading, frame);
        Assert.Equal(1.0, frame.EmberOpacity);
        Assert.Equal(38.0, frame.CellsY);
        Assert.Equal(0.86, frame.BodyScale);
        Assert.Equal(9.0, frame.BodyY);

        motion.SetState(LivingCoreState.Wake, 1.0);
        motion.Advance(1.2, NoVoice, Leading, frame);
        Assert.InRange(frame.EmberOpacity, 0.01, 0.5);
        Assert.InRange(frame.CellsY, 0.1, 19.0);
        Assert.InRange(frame.BodyScale, 0.87, 0.999);
        Assert.Equal(1.0, frame.CoreScale);
        Assert.True(motion.NeedsContinuousFrames(1.2));

        motion.Advance(1.0 + LivingCoreMotion.ReducedTransitionSeconds, NoVoice, Leading, frame);
        Assert.Equal(0.0, frame.EmberOpacity);
        Assert.Equal(0.0, frame.CellsY);
        Assert.Equal(1.0, frame.BodyOpacity);
        Assert.Equal(1.0, frame.BodyScale);
        Assert.Equal(0.0, frame.BodyY);
        Assert.False(motion.NeedsContinuousFrames(1.0 + LivingCoreMotion.ReducedTransitionSeconds + 0.01));
    }

    [Fact]
    public void Reduced_motion_success_is_a_change_of_light_only()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false, reducedMotion: true);
        var frame = new LivingCoreFrame();
        motion.Advance(1.0, NoVoice, Leading, frame);
        motion.SetState(LivingCoreState.Success, 1.0);

        MotionHarness.Step(motion, frame, 1.0, 1.0 + LivingCoreMotion.SuccessSeconds, after: (t, f) =>
        {
            Assert.Equal(1.0, f.WarmScale);
            Assert.Equal(1.0, f.SuccessRingScale);
            Assert.Equal(0.0, f.CellsY);
            if (t >= 1.0 + LivingCoreMotion.ReducedTransitionSeconds)
            {
                Assert.Equal(1.0, f.WarmOpacity);
                Assert.Equal(1.0, f.SuccessRingOpacity);
                Assert.Equal(LivingCoreLooks.Success.X, f.CoreX, 1e-9);
                Assert.Equal(LivingCoreLooks.Success.Y, f.CoreY, 1e-9);
            }
        });

        Assert.False(motion.NeedsContinuousFrames(1.0 + LivingCoreMotion.SuccessSeconds + 0.1));
    }

    [Fact]
    public void Sleep_rests_only_once_settled()
    {
        var motion = Idle(out var frame, until: 1.0);
        motion.SetState(LivingCoreState.Sleep, 1.0);
        Assert.False(motion.IsResting(1.5));
        Assert.True(motion.IsResting(3.0));

        motion.SetState(LivingCoreState.Wake, 3.0);
        Assert.False(motion.IsResting(3.5));
    }

    // ---- Helpers ----

    /// <summary>An Idle core run to <paramref name="until"/>.</summary>
    private static LivingCoreMotion Idle(out LivingCoreFrame frame, double until = 8.0)
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        frame = new LivingCoreFrame();
        MotionHarness.Run(motion, frame, 0.0, until);
        return motion;
    }

    /// <summary>The idle drift's offset at <paramref name="t"/> (an Idle core started at 0).</summary>
    private static (double X, double Y) DriftAt(double t)
    {
        var probe = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        probe.Advance(t, NoVoice, Leading, frame);
        return (frame.CoreX - LivingCoreLooks.RestX, frame.CoreY - LivingCoreLooks.RestY);
    }

    /// <summary>A zone target as the engine holds it: on the edge when it lies past it.</summary>
    private static (double X, double Y) OnZoneEdge(double fractionX, double fractionY)
    {
        var length = Math.Max(1.0, Math.Sqrt((fractionX * fractionX) + (fractionY * fractionY)));
        return (
            LivingCoreLooks.ZoneX + (LivingCoreLooks.ZoneRadiusX * fractionX / length),
            LivingCoreLooks.ZoneY + (LivingCoreLooks.ZoneRadiusY * fractionY / length));
    }

    private static void Mark(Dictionary<string, double> arrived, string layer, double value, double t)
    {
        if (value >= 0.5 && !arrived.ContainsKey(layer))
        {
            arrived[layer] = t;
        }
    }

    private static void AssertStep(double[] now, double[] previous, int index, double limit, string what, string at)
    {
        var step = Math.Abs(now[index] - previous[index]);
        Assert.True(step <= limit, $"{what} jumped {step:0.0000} at {at}");
    }

    private static void AssertMonotonic(IReadOnlyList<double> values, bool rising)
    {
        for (var i = 1; i < values.Count; i++)
        {
            var step = values[i] - values[i - 1];
            Assert.True(rising ? step >= -1e-12 : step <= 1e-12, $"not monotonic at {i}: {values[i - 1]} -> {values[i]}");
        }
    }

    private static double Distance(double x, double y)
    {
        var dx = x - LivingCoreDesign.Centre;
        var dy = y - LivingCoreDesign.Centre;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
