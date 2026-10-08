using HAMMOR.App.Presence;
using Xunit;

namespace HAMMOR.App.Tests.Presence;

/// <summary>
/// The Living Core against the approved reference: the canvas prototype's
/// pointer, depth and events, and the four states it adds to P0 (Success,
/// Warning, Sleep, Wake), with the collision and reduced-motion rules.
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

        // Only the idle drift (under 1 unit) on top of the pointer's place.
        Assert.Equal(1.0, motion.PointerWeight, 1e-9);
        Assert.InRange(frame.CoreX, expectedX - 0.85, expectedX + 0.85);
    }

    [Fact]
    public void The_pointer_is_followed_450_ms_behind_and_never_overshot()
    {
        var motion = Idle(out var frame);
        var target = LivingCoreLooks.ZoneX - (LivingCoreMotion.PointerReach * LivingCoreLooks.ZoneRadiusX);
        var weights = new List<double>();
        var leftmost = double.MaxValue;

        motion.SetPointer(-3.0, 0.0, 8.0);
        MotionHarness.Step(motion, frame, 8.0, 8.6, after: (t, f) =>
        {
            weights.Add(motion.PointerWeight);
            leftmost = Math.Min(leftmost, f.CoreX);
        });

        AssertMonotonic(weights, rising: true);
        Assert.InRange(weights.Max(), 0.0, 1.0);
        Assert.Equal(1.0, weights[(int)Math.Round(LivingCoreMotion.PointerLag * MotionHarness.Fps)], 1e-9);
        Assert.True(leftmost >= target - 0.8 - 1e-9, $"overshot to {leftmost}");
    }

    [Fact]
    public void The_pointer_is_let_go_after_five_seconds_of_stillness()
    {
        var motion = Idle(out var frame);
        MotionHarness.Step(motion, frame, 8.0, 8.5, before: t => motion.SetPointer(-3.0, 0.0, t));

        // Last move at 8.5 s: it still follows just before 13.5 s...
        MotionHarness.Step(motion, frame, 8.5 + Frame, 13.45);
        Assert.Equal(1.0, motion.PointerWeight, 1e-9);

        // ...lets go over 450 ms, and is back to rest.
        MotionHarness.Step(motion, frame, 13.45 + Frame, 14.1);
        Assert.Equal(0.0, motion.PointerWeight, 1e-9);
        MotionHarness.Step(motion, frame, 14.1 + Frame, 15.0);
        Assert.InRange(MotionHarness.DistanceFromRest(frame), 0.0, 1.0);
    }

    [Fact]
    public void Leaving_the_window_lets_go_after_1_2_seconds()
    {
        var motion = Idle(out var frame);
        MotionHarness.Step(motion, frame, 8.0, 8.5, before: t => motion.SetPointer(-3.0, 0.0, t));
        motion.ReleasePointer(8.5 + LivingCoreMotion.PointerLeaveRelease);

        MotionHarness.Step(motion, frame, 8.5 + Frame, 9.6);
        Assert.Equal(1.0, motion.PointerWeight, 1e-9);
        MotionHarness.Step(motion, frame, 9.6 + Frame, 10.2);
        Assert.Equal(0.0, motion.PointerWeight, 1e-9);
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
        var look = LivingCoreLooks.For(state);
        var motion = new LivingCoreMotion(state, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();

        MotionHarness.Step(motion, frame, 0.0, 10.0, before: t => motion.SetPointer(-1.5, 1.0, t), after: (t, f) =>
        {
            Assert.Equal(0.0, motion.PointerWeight);
            if (state != LivingCoreState.Blocked || t > 4.0)
            {
                // Blocked's single request glance is over by then.
                Assert.Equal(look.Core.X, f.CoreX, 1e-6);
                Assert.Equal(look.Core.Y, f.CoreY, 1e-6);
            }
        });
    }

    [Fact]
    public void The_pointer_never_takes_the_core_out_of_its_zone()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();

        MotionHarness.Step(motion, frame, 0.0, 60.0,
            before: t => motion.SetPointer(2.0 * Math.Cos(t * 1.3), 2.0 * Math.Sin(t * 1.3), t),
            after: (_, f) => Assert.InRange(MotionHarness.ZoneRadius(f), 0.0, 1.0 + 1e-9));
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
    public void The_sleeping_sediment_stays_inside_the_membrane_however_the_school_has_turned()
    {
        var inside = LivingCoreLooks.Sleep.Inside;
        var shiftX = LivingCoreMotion.CellsDepth * (LivingCoreLooks.RestX - LivingCoreLooks.ZoneX);
        var shiftY = LivingCoreMotion.CellsDepth * (LivingCoreLooks.RestY - LivingCoreLooks.ZoneY);

        foreach (var cell in LivingCoreDesign.Cells)
        {
            var radius = Distance(cell.X, cell.Y);
            var start = Math.Atan2(cell.Y - LivingCoreDesign.Centre, cell.X - LivingCoreDesign.Centre);
            for (var k = 0; k < 360; k++)
            {
                var angle = start + (2.0 * Math.PI * k / 360.0);
                var rx = LivingCoreDesign.Centre + (radius * Math.Cos(angle));
                var ry = LivingCoreDesign.Centre + (radius * Math.Sin(angle));
                var x = LivingCoreLooks.ZoneX + ((rx - LivingCoreLooks.ZoneX) * inside.CellSpread * inside.CellNarrow) + shiftX;
                var y = LivingCoreLooks.ZoneY + ((ry - LivingCoreLooks.ZoneY) * inside.CellSpread * inside.CellSquash) + inside.CellDrop + shiftY;
                var reach = Distance(x, y) + cell.Radius;
                Assert.True(reach < LivingCoreDesign.MembraneRadius, $"sediment cell reaches {reach}");
            }
        }
    }

    // ---- Reactions ----

    [Fact]
    public void A_new_message_sends_one_ripple_and_the_cells_shiver_once()
    {
        var motion = Idle(out var frame);
        motion.React(LivingCoreReactionKind.NewMessage, 1.0, 8.0);
        motion.Advance(8.0, NoVoice, Leading, frame);
        Assert.Equal(0.75, frame.RippleOpacity, 1e-9);
        Assert.Equal(1.0, frame.RippleScale, 1e-9);

        var widest = 0.0;
        var shiver = 0.0;
        MotionHarness.Step(motion, frame, 8.0 + Frame, 8.85, after: (_, f) =>
        {
            widest = Math.Max(widest, f.RippleScale);
            shiver = Math.Max(shiver, Math.Abs(f.CellsShiftX - DepthX(f)));
        });

        Assert.True(widest > 1.3, $"ripple only reached {widest}");
        Assert.True(shiver > 0.3, $"cells shivered {shiver}");

        MotionHarness.Step(motion, frame, 8.85 + Frame, 8.9);
        Assert.Equal(0.0, frame.RippleOpacity);
        Assert.Equal(DepthX(frame), frame.CellsShiftX, 1e-12);
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

        MotionHarness.Step(motion, frame, 8.75 + Frame, 10.3);
        Assert.InRange(MotionHarness.DistanceFromRest(frame), 0.0, 1.0);
    }

    [Fact]
    public void Messages_within_two_seconds_count_as_one()
    {
        var motion = Idle(out var frame);
        motion.React(LivingCoreReactionKind.NewMessage, 1.0, 8.0);
        MotionHarness.Step(motion, frame, 8.0, 8.5);

        motion.React(LivingCoreReactionKind.NewMessage, 1.0, 8.5);
        motion.Advance(8.5, NoVoice, Leading, frame);
        var firstRipple = 0.75 * (1.0 - LivingCoreEasings.Strike.Evaluate(0.5 / LivingCoreMotion.RippleSeconds));
        Assert.Equal(firstRipple, frame.RippleOpacity, 1e-9);

        MotionHarness.Step(motion, frame, 8.5 + Frame, 10.1);
        motion.React(LivingCoreReactionKind.NewMessage, 1.0, 10.1);
        motion.Advance(10.1, NoVoice, Leading, frame);
        Assert.Equal(0.75, frame.RippleOpacity, 1e-9);
    }

    [Theory]
    [InlineData(LivingCoreState.Thinking)]
    [InlineData(LivingCoreState.Listening)]
    [InlineData(LivingCoreState.Speaking)]
    [InlineData(LivingCoreState.Blocked)]
    [InlineData(LivingCoreState.Error)]
    [InlineData(LivingCoreState.Warning)]
    [InlineData(LivingCoreState.Sleep)]
    public void A_message_while_busy_or_waiting_ripples_without_a_glance(LivingCoreState state)
    {
        var look = LivingCoreLooks.For(state);
        var motion = new LivingCoreMotion(state, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        MotionHarness.Run(motion, frame, 0.0, 6.0);
        motion.React(LivingCoreReactionKind.NewMessage, 1.0, 6.0);

        var ripple = 0.0;
        MotionHarness.Step(motion, frame, 6.0, 9.0, after: (_, f) =>
        {
            ripple = Math.Max(ripple, f.RippleOpacity);
            Assert.Equal(look.Core.X, f.CoreX, 1e-6);
            Assert.Equal(look.Core.Y, f.CoreY, 1e-6);
        });

        Assert.True(ripple > 0.7, $"ripple {ripple}");
    }

    [Fact]
    public void Success_glances_toward_a_new_message()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Success, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        MotionHarness.Run(motion, frame, 0.0, 4.0);
        motion.React(LivingCoreReactionKind.NewMessage, 1.0, 4.0);
        MotionHarness.Step(motion, frame, 4.0, 4.75);

        var (x, y) = OnZoneEdge(0.5, 0.9);
        Assert.Equal(x, frame.CoreX, 1e-6);
        Assert.Equal(y, frame.CoreY, 1e-6);
    }

    [Fact]
    public void Opening_a_panel_draws_a_glance_and_leans_the_aura_its_way()
    {
        var motion = Idle(out var frame);
        motion.React(LivingCoreReactionKind.PanelOpened, -1.0, 8.0);
        MotionHarness.Step(motion, frame, 8.0, 8.9);

        var x = LivingCoreLooks.ZoneX - (0.95 * LivingCoreLooks.ZoneRadiusX);
        Assert.InRange(frame.CoreX, x - 0.85, x + 0.85);
        Assert.Equal(-4.0, frame.AuraLeanX, 1e-9);

        MotionHarness.Step(motion, frame, 8.9 + Frame, 11.0);
        Assert.Equal(0.0, frame.AuraLeanX);
        Assert.InRange(MotionHarness.DistanceFromRest(frame), 0.0, 1.0);
    }

    [Fact]
    public void No_two_glances_within_two_seconds()
    {
        var motion = Idle(out var frame);
        motion.React(LivingCoreReactionKind.PanelOpened, -1.0, 8.0);
        MotionHarness.Step(motion, frame, 8.0, 9.0);

        // A message a second later ripples, but the core keeps to the panel.
        motion.React(LivingCoreReactionKind.NewMessage, 1.0, 9.0);
        MotionHarness.Step(motion, frame, 9.0, 9.3);
        Assert.True(frame.RippleOpacity > 0.0);
        Assert.True(frame.CoreX < LivingCoreLooks.ZoneX, $"core x {frame.CoreX}");
    }

    [Fact]
    public void Listening_fades_a_glance_out_instead_of_being_interrupted()
    {
        var motion = Idle(out var frame);
        motion.React(LivingCoreReactionKind.NewMessage, 1.0, 8.0);
        MotionHarness.Step(motion, frame, 8.0, 8.3);
        motion.SetState(LivingCoreState.Listening, 8.3);

        MotionHarness.Step(motion, frame, 8.3, 10.3);
        Assert.Equal(LivingCoreLooks.ZoneX, frame.CoreX, 0.05);
        Assert.Equal(LivingCoreLooks.ZoneY, frame.CoreY, 0.05);
    }

    [Fact]
    public void Typing_strengthens_the_inward_rings_and_they_settle_back()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Listening, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();

        var resting = MaxInward(motion, frame, 0.0, 3.0);

        // A keystroke every 0.1 s for two seconds.
        var typed = 0.0;
        MotionHarness.Step(motion, frame, 3.0, 5.0,
            before: t =>
            {
                if (Math.Abs((t * 10.0) - Math.Round(t * 10.0)) < 1e-6)
                {
                    motion.NoteInput(t);
                }
            },
            after: (_, f) => typed = Math.Max(typed, f.InwardOpacity.Max()));

        MotionHarness.Step(motion, frame, 5.0 + Frame, 7.0);
        var after = MaxInward(motion, frame, 7.0 + Frame, 9.0);

        Assert.InRange(resting, 0.5, 0.551);
        Assert.True(typed > 0.9, $"typing lifted the rings to {typed}");
        Assert.InRange(after, 0.5, 0.56);
    }

    // ---- Success, Warning, Sleep, Wake ----

    [Fact]
    public void Success_lifts_brightens_and_blooms_once()
    {
        var motion = Idle(out var frame, until: 6.0);
        motion.SetState(LivingCoreState.Success, 6.0);

        var warmPeak = (Time: 0.0, Value: 0.0);
        var ringPeak = 0.0;
        var ringWidest = 0.0;
        var lowestRise = 0.0;
        var ringGoneFrom = double.NaN;
        MotionHarness.Step(motion, frame, 6.0, 9.2, after: (t, f) =>
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

        // The warm bloom peaks a quarter of the way through 3.2 s, then settles.
        Assert.Equal(1.0, warmPeak.Value, 1e-6);
        Assert.InRange(warmPeak.Time, 6.75, 6.85);
        Assert.Equal(0.55, frame.WarmOpacity, 1e-6);

        // One clean ring expands to 150% and dissolves within 45%.
        Assert.Equal(1.0, ringPeak, 1e-3);
        Assert.Equal(1.5, ringWidest, 1e-9);
        Assert.InRange(ringGoneFrom, 6.0 + (0.45 * LivingCoreMotion.SuccessSeconds) - Frame, 6.0 + (0.45 * LivingCoreMotion.SuccessSeconds) + Frame);

        // The cells rise together and settle back.
        Assert.Equal(-3.5, lowestRise, 1e-6);
        Assert.Equal(0.0, frame.CellsY, 1e-9);

        // Lifted up and forward, 108%, in the success tone.
        Assert.Equal(LivingCoreLooks.Success.Core.X, frame.CoreX, 1e-9);
        Assert.Equal(LivingCoreLooks.Success.Core.Y, frame.CoreY, 1e-9);
        Assert.Equal(1.08, frame.CoreScale, 1e-9);
        Assert.Equal(1.0, frame.RimSuccess);

        // Back to Idle, the bloom fades rather than cuts.
        motion.SetState(LivingCoreState.Idle, 9.2);
        motion.Advance(9.2 + Frame, NoVoice, Leading, frame);
        Assert.True(frame.WarmOpacity > 0.4, $"bloom cut to {frame.WarmOpacity}");
        MotionHarness.Step(motion, frame, 9.2 + (2.0 * Frame), 11.5);
        Assert.Equal(0.0, frame.WarmOpacity);
    }

    [Fact]
    public void Success_sends_one_thread_round_once()
    {
        var motion = Idle(out var frame, until: 6.0);
        var twin = Idle(out var twinFrame, until: 6.0);
        motion.SetState(LivingCoreState.Success, 6.0);

        var midLap = 0.0;
        MotionHarness.Step(motion, frame, 6.0, 7.5, after: (t, f) =>
        {
            twin.Advance(t, NoVoice, Leading, twinFrame);
            var lead = Mod360(f.ThreadAngle[0] - twinFrame.ThreadAngle[0]);
            if (Math.Abs(t - 6.3) < 1e-6)
            {
                midLap = lead;
            }

            if (t >= 6.0 + 1.2)
            {
                Assert.True(Math.Min(lead, 360.0 - lead) < 1e-6, $"lap still showing at {t}: {lead}");
            }

            Assert.Equal(twinFrame.ThreadAngle[1], f.ThreadAngle[1], 1e-9);
        });

        Assert.InRange(midLap, 200.0, 320.0);
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
            Assert.Equal(1.0, f.RimWarning);
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

        MotionHarness.Step(motion, frame, 0.0, 9.6, after: (_, f) =>
        {
            smallest = Math.Min(smallest, f.BodyScale);
            largest = Math.Max(largest, f.BodyScale);
            Assert.Equal(LivingCoreLooks.RestX, f.CoreX, 1e-9);
            Assert.Equal(LivingCoreLooks.RestY, f.CoreY, 1e-9);
        });

        // The 9.6 s breath, between 86% and 87.5%.
        Assert.Equal(0.86, smallest, 1e-6);
        Assert.Equal(0.875, largest, 1e-6);
        Assert.Equal(9.0, frame.BodyY);
        Assert.Equal(0.6, frame.BodyOpacity, 1e-9);
        Assert.Equal(1.0, frame.EmberOpacity, 1e-9);
        Assert.Equal(0.0, frame.GlowOpacity);
        Assert.Equal(0.0, frame.ThreadsOpacity);
        Assert.Equal(0.5, frame.CellsSpread, 1e-9);
        Assert.Equal(0.2, frame.CellsScaleY, 1e-9);
        Assert.Equal(37.0, frame.CellsY, 1e-9);
        Assert.All(frame.StarClockOpacity, o => Assert.InRange(o, 0.0, 0.15 + 1e-9));
        Assert.InRange(frame.AuraOpacity, 0.0, 0.2 + 1e-9);
        Assert.True(motion.IsResting(9.6));
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

        // Then it hands over to Idle without a trace of the ignition or of Sleep.
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
                f.BodyScale, f.BodyOpacity, f.EmberOpacity, f.LensScaleY, f.GlowOpacity,
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
    public void State_changes_never_jump()
    {
        LivingCoreState[] sequence =
        [
            LivingCoreState.Listening, LivingCoreState.Thinking, LivingCoreState.Speaking, LivingCoreState.Success,
            LivingCoreState.Idle, LivingCoreState.Warning, LivingCoreState.Blocked, LivingCoreState.Error,
            LivingCoreState.Sleep, LivingCoreState.Wake, LivingCoreState.Idle, LivingCoreState.Thinking,
            LivingCoreState.Warning, LivingCoreState.Idle, LivingCoreState.Sleep, LivingCoreState.Wake,
            LivingCoreState.Blocked, LivingCoreState.Error,
        ];

        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: true);
        var frame = new LivingCoreFrame();
        var lastWake = double.NegativeInfinity;
        var lastOneShot = double.NegativeInfinity;
        double[]? previous = null;

        for (var i = 0; i < 300 + (sequence.Length * 80) + 200; i++)
        {
            var t = i / MotionHarness.Fps;
            if (i >= 300 && (i - 300) % 80 == 0 && (i - 300) / 80 < sequence.Length)
            {
                var state = sequence[(i - 300) / 80];
                motion.SetState(state, t);
                if (state == LivingCoreState.Wake)
                {
                    lastWake = t;
                    lastOneShot = t;
                }
                else if (state == LivingCoreState.Success)
                {
                    lastOneShot = t;
                }
            }

            motion.Advance(t, NoVoice, Leading, frame);
            double[] now =
            [
                frame.CoreX, frame.CoreY,
                frame.CoreScale, frame.GlowOpacity, frame.CellsSpread, frame.CellsScaleY,
                frame.CoreOpacity, frame.EmberOpacity, frame.AuraOpacity, frame.ThreadsOpacity, frame.WarmOpacity, frame.HaloOpacity[0],
                frame.BodyScale, frame.BodyY, frame.CellsY, frame.LensScaleY, frame.CrestY,
            ];

            if (previous is not null && i > 150)
            {
                var ignition = t - lastWake < LivingCoreMotion.WakeSeconds;
                var oneShotStart = t - lastOneShot < 0.1;
                var at = $"{t:0.000} s";

                // The white core glides on the cascade's settle curve; the
                // largest pose change is about 20 units.
                var moved = Math.Sqrt(Math.Pow(now[0] - previous[0], 2) + Math.Pow(now[1] - previous[1], 2));
                Assert.True(moved <= 3.5, $"core jumped {moved:0.00} units at {at}");

                // Wake and Success start their keyframes from a fixed point,
                // as the reference does: the ignition from a point of light,
                // the bloom from 40%.
                if (!ignition)
                {
                    AssertStep(now, previous, 2, 0.2, "core scale", at);
                    AssertStep(now, previous, 3, 0.2, "glow", at);
                    AssertStep(now, previous, 4, 0.15, "cells width", at);
                    AssertStep(now, previous, 5, 0.15, "cells height", at);
                }

                if (!oneShotStart)
                {
                    AssertStep(now, previous, 6, 0.2, "core light", at);
                    AssertStep(now, previous, 7, 0.2, "ember", at);
                    AssertStep(now, previous, 8, 0.1, "aura", at);
                    AssertStep(now, previous, 9, 0.1, "threads", at);
                    AssertStep(now, previous, 10, 0.1, "bloom", at);
                    AssertStep(now, previous, 11, 0.1, "halo", at);
                }

                AssertStep(now, previous, 12, 0.02, "body scale", at);
                AssertStep(now, previous, 13, 1.0, "body drop", at);
                AssertStep(now, previous, 14, 5.0, "sediment", at);
                AssertStep(now, previous, 15, 0.05, "lens", at);
                AssertStep(now, previous, 16, 0.6, "crest", at);
            }

            previous = now;
        }
    }

    [Fact]
    public void A_change_mid_cascade_starts_from_the_frame_on_screen()
    {
        var motion = Idle(out var frame, until: 10.0);
        motion.SetState(LivingCoreState.Thinking, 10.0);
        MotionHarness.Run(motion, frame, 10.0, 10.5);

        var before = MotionHarness.Snapshot(frame);
        motion.SetState(LivingCoreState.Warning, 10.5);
        motion.Advance(10.5, NoVoice, Leading, frame);
        var after = MotionHarness.Snapshot(frame);

        Assert.Equal(before.Length, after.Length);
        for (var i = 0; i < before.Length; i++)
        {
            Assert.Equal(before[i], after[i], 1e-9);
        }
    }

    [Fact]
    public void Leaving_blocked_mid_glance_fades_the_glance_instead_of_cutting_it()
    {
        var motion = Idle(out var frame, until: 2.0);
        motion.SetState(LivingCoreState.Blocked, 2.0);
        MotionHarness.Run(motion, frame, 2.0, 3.4);
        var previousX = frame.CoreX;
        var previousY = frame.CoreY;

        motion.SetState(LivingCoreState.Error, 3.4 + Frame);
        MotionHarness.Step(motion, frame, 3.4 + Frame, 4.5, after: (_, f) =>
        {
            var moved = Math.Sqrt(Math.Pow(f.CoreX - previousX, 2) + Math.Pow(f.CoreY - previousY, 2));
            Assert.True(moved <= 3.5, $"core jumped {moved:0.00}");
            previousX = f.CoreX;
            previousY = f.CoreY;
        });
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
            },
            after: (_, f) =>
            {
                Assert.Equal(look.Core.X, f.CoreX, 1e-9);
                Assert.Equal(look.Core.Y, f.CoreY, 1e-9);
                Assert.Equal(0.0, motion.PointerWeight);
                Assert.Equal(0.0, f.AuraLeanX);
                Assert.Equal(LivingCoreMotion.CellsDepth * (look.Core.X - LivingCoreLooks.ZoneX), f.CellsShiftX, 1e-9);
            });

        var stillLit = state is LivingCoreState.Speaking or LivingCoreState.Blocked;
        Assert.Equal(stillLit, motion.NeedsContinuousFrames(6.0));
    }

    [Fact]
    public void Reduced_motion_wakes_with_a_short_fade()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false, reducedMotion: true);
        var frame = new LivingCoreFrame();
        motion.Advance(1.0, NoVoice, Leading, frame);
        motion.SetState(LivingCoreState.Wake, 1.0);

        motion.Advance(1.0, NoVoice, Leading, frame);
        Assert.Equal(1.0, frame.CoreScale, 1e-9);
        Assert.Equal(0.0, frame.CoreOpacity);
        motion.Advance(1.2, NoVoice, Leading, frame);
        Assert.InRange(frame.CoreOpacity, 0.5, 0.999);
        Assert.True(motion.NeedsContinuousFrames(1.2));
        motion.Advance(1.0 + LivingCoreMotion.ReducedTransitionSeconds, NoVoice, Leading, frame);
        Assert.Equal(1.0, frame.CoreOpacity, 1e-9);
        Assert.False(motion.NeedsContinuousFrames(1.0 + LivingCoreMotion.ReducedTransitionSeconds + 0.01));
    }

    [Fact]
    public void Reduced_motion_success_is_a_change_of_light_only()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false, reducedMotion: true);
        var frame = new LivingCoreFrame();
        motion.Advance(1.0, NoVoice, Leading, frame);
        motion.SetState(LivingCoreState.Success, 1.0);

        var dimmest = double.MaxValue;
        var brightest = 0.0;
        MotionHarness.Step(motion, frame, 1.0, 1.0 + LivingCoreMotion.SuccessSeconds, after: (t, f) =>
        {
            dimmest = Math.Min(dimmest, f.WarmOpacity);
            brightest = Math.Max(brightest, f.WarmOpacity);
            Assert.Equal(1.0, f.WarmScale);
            Assert.Equal(1.2, f.SuccessRingScale);
            Assert.Equal(0.0, f.CellsY);
            if (t >= 1.0 + LivingCoreMotion.ReducedTransitionSeconds)
            {
                Assert.Equal(LivingCoreLooks.Success.Core.X, f.CoreX, 1e-9);
                Assert.Equal(LivingCoreLooks.Success.Core.Y, f.CoreY, 1e-9);
            }
        });

        Assert.InRange(dimmest, 0.4 - 1e-9, 0.45);
        Assert.Equal(1.0, brightest, 1e-6);
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

    /// <summary>An Idle core run to <paramref name="until"/> (8 s: after the first idle glance).</summary>
    private static LivingCoreMotion Idle(out LivingCoreFrame frame, double until = 8.0)
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        frame = new LivingCoreFrame();
        MotionHarness.Run(motion, frame, 0.0, until);
        return motion;
    }

    private static double MaxInward(LivingCoreMotion motion, LivingCoreFrame frame, double from, double to)
    {
        var max = 0.0;
        MotionHarness.Step(motion, frame, from, to, after: (_, f) => max = Math.Max(max, f.InwardOpacity.Max()));
        return max;
    }

    /// <summary>Where the depth alone puts the cells for the frame's gaze, recovered from the stars.</summary>
    private static double DepthX(LivingCoreFrame f) =>
        f.StarsShiftX / LivingCoreMotion.StarsDepth * LivingCoreMotion.CellsDepth;

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

    private static double Mod360(double angle) => ((angle % 360.0) + 360.0) % 360.0;

    private static double Distance(double x, double y)
    {
        var dx = x - LivingCoreDesign.Centre;
        var dy = y - LivingCoreDesign.Centre;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
