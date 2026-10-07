using HAMMOR.App.Presence;
using Xunit;

namespace HAMMOR.App.Tests.Presence;

/// <summary>
/// Step 4: the Living Core is visibly alive in Idle, attends to the pointer,
/// keeps every state distinct, changes state without a jump, and holds still
/// under reduced motion.
/// </summary>
public sealed class LivingCoreLifeTests
{
    private const double NoVoice = double.NaN;
    private const double Leading = -1.0;

    public static TheoryData<LivingCoreState> States => new()
    {
        LivingCoreState.Idle,
        LivingCoreState.Listening,
        LivingCoreState.Thinking,
        LivingCoreState.Speaking,
        LivingCoreState.Blocked,
        LivingCoreState.Error,
    };

    // ---- Idle life ----

    [Fact]
    public void Idle_drift_is_visible_but_stays_within_the_hero_boards_limit()
    {
        var maxX = 0.0;
        var maxY = 0.0;
        for (var i = 0; i <= 60_000; i++)
        {
            var t = i / 100.0;
            maxX = Math.Max(maxX, Math.Abs(LivingCoreMotion.IdleDriftX(t)));
            maxY = Math.Max(maxY, Math.Abs(LivingCoreMotion.IdleDriftY(t)));
        }

        Assert.InRange(maxX, 1.0, LivingCoreDesign.CoreDriftLimit);
        Assert.InRange(maxY, 0.6, LivingCoreDesign.CoreDriftLimit);
    }

    [Fact]
    public void The_idle_white_core_is_never_still_for_two_seconds()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        var points = new List<(double X, double Y)>();
        MotionHarness.Run(motion, frame, 0.0, 60.0, (_, f) => points.Add((f.CoreX, f.CoreY)));

        var window = (int)(2.0 * MotionHarness.Fps);
        for (var start = 0; start + window < points.Count; start += 30)
        {
            var moved = 0.0;
            for (var j = start; j <= start + window; j++)
            {
                var dx = points[j].X - points[start].X;
                var dy = points[j].Y - points[start].Y;
                moved = Math.Max(moved, Math.Sqrt((dx * dx) + (dy * dy)));
            }

            Assert.True(moved >= 0.15, $"still from {start / MotionHarness.Fps:0.0} s: moved {moved:0.000}");
        }
    }

    [Fact]
    public void Star_groups_drift_against_each_other_in_idle()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        var minX = new double[4];
        var maxX = new double[4];
        var minY = new double[4];
        var maxY = new double[4];
        Array.Fill(minX, double.MaxValue);
        Array.Fill(minY, double.MaxValue);
        Array.Fill(maxX, double.MinValue);
        Array.Fill(maxY, double.MinValue);

        MotionHarness.Run(motion, frame, 0.0, 40.0, (_, f) =>
        {
            for (var k = 0; k < 4; k++)
            {
                minX[k] = Math.Min(minX[k], f.StarGroupX[k]);
                maxX[k] = Math.Max(maxX[k], f.StarGroupX[k]);
                minY[k] = Math.Min(minY[k], f.StarGroupY[k]);
                maxY[k] = Math.Max(maxY[k], f.StarGroupY[k]);
            }

            // Never all in step: at least two groups are apart.
            var spread = f.StarGroupX.Max() - f.StarGroupX.Min();
            Assert.True(spread > 0.2, $"star groups in step: {spread}");
        });

        for (var k = 0; k < 4; k++)
        {
            Assert.True(maxX[k] - minX[k] > 2.0, $"group {k} x range {maxX[k] - minX[k]}");
            Assert.True(maxY[k] - minY[k] > 1.5, $"group {k} y range {maxY[k] - minY[k]}");
        }
    }

    [Fact]
    public void Wandering_stars_and_cells_stay_inside_the_membrane()
    {
        // The field turns about the centre, which keeps distances; the
        // parallax adds at most its amplitude.
        foreach (var star in LivingCoreDesign.Stars)
        {
            var wander = LivingCoreDesign.StarParallax[star.Clock];
            var reach = Distance(star.X, star.Y) + Math.Sqrt((wander.AmplitudeX * wander.AmplitudeX) + (wander.AmplitudeY * wander.AmplitudeY)) + star.Radius;
            Assert.True(reach < LivingCoreDesign.MembraneRadius, $"star at ({star.X}, {star.Y}) reaches {reach}");
        }

        // Cells: bob plus wander, then the school's spread about the zone
        // centre (Idle 1, Listening leans in). Error is designed to drift out
        // to the rim, so it is not held to this.
        foreach (var spread in new[] { LivingCoreLooks.Idle.Inside.CellSpread, LivingCoreLooks.Listening.Inside.CellSpread })
        {
            foreach (var cell in LivingCoreDesign.Cells)
            {
                var wander = LivingCoreDesign.CellWander[cell.Clock];
                var dx = Math.Abs(LivingCoreDesign.BobX) + wander.AmplitudeX;
                var dy = Math.Abs(LivingCoreDesign.BobY) + wander.AmplitudeY;
                var unspread = Distance(cell.X, cell.Y) + Math.Sqrt((dx * dx) + (dy * dy));
                var zoneOffset = Distance(LivingCoreLooks.ZoneX, LivingCoreLooks.ZoneY) * Math.Abs(1.0 - spread);
                var reach = zoneOffset + (spread * (unspread + cell.Radius));
                Assert.True(reach < LivingCoreDesign.MembraneRadius, $"cell at ({cell.X}, {cell.Y}) reaches {reach}");
            }
        }
    }

    [Fact]
    public void Cells_wander_and_glow_in_idle()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        var minX = double.MaxValue;
        var maxX = double.MinValue;
        var minGlow = 1.0;

        MotionHarness.Run(motion, frame, 0.0, 30.0, (_, f) =>
        {
            minX = Math.Min(minX, f.BobX[0]);
            maxX = Math.Max(maxX, f.BobX[0]);
            minGlow = Math.Min(minGlow, f.CellGlow.Min());
            Assert.All(f.CellGlow, g => Assert.InRange(g, 1.0 - LivingCoreDesign.CellGlowDepth - 1e-9, 1.0));
        });

        Assert.True(maxX - minX > 2.5, $"cell x range {maxX - minX}");
        Assert.True(minGlow < 0.85, $"cells never dimmed: {minGlow}");
    }

    [Theory]
    [InlineData(LivingCoreState.Thinking)]
    [InlineData(LivingCoreState.Speaking)]
    [InlineData(LivingCoreState.Blocked)]
    public void States_that_hold_the_cells_hold_their_wander_and_glow(LivingCoreState state)
    {
        var motion = new LivingCoreMotion(state, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();

        MotionHarness.Run(motion, frame, 0.0, 12.0, (_, f) =>
        {
            Assert.All(f.BobX, x => Assert.Equal(0.0, x, 1e-12));
            Assert.All(f.BobY, y => Assert.Equal(0.0, y, 1e-12));
            Assert.All(f.CellGlow, g => Assert.Equal(1.0, g, 1e-12));
        });
    }

    [Fact]
    public void Halo_rings_and_threads_breathe_light_in_idle()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        var haloMin = double.MaxValue;
        var haloMax = 0.0;
        var scaleMax = 0.0;
        var threadMin = double.MaxValue;

        MotionHarness.Run(motion, frame, 0.0, 14.0, (_, f) =>
        {
            haloMin = Math.Min(haloMin, f.HaloOpacity[2]);
            haloMax = Math.Max(haloMax, f.HaloOpacity[2]);
            scaleMax = Math.Max(scaleMax, f.HaloScale[2]);
            threadMin = Math.Min(threadMin, f.ThreadsOpacity);
            Assert.InRange(f.ThreadsOpacity, 0.84 - 1e-9, 1.0);
        });

        Assert.InRange(haloMin, 0.6 - 1e-9, 0.65);
        Assert.Equal(1.0, haloMax, 0.01);
        Assert.InRange(scaleMax, 1.02, 1.022 + 1e-9);
        Assert.InRange(threadMin, 0.84 - 1e-9, 0.86);
    }

    // ---- Pointer attention ----

    [Fact]
    public void Idle_turns_toward_the_pointer_then_settles_back()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();

        // After the first glance (4.2 to about 6.8 s), before the next (18.2 s).
        MotionHarness.Run(motion, frame, 0.0, 8.0);
        var rising = new List<double>();
        Step(motion, frame, 8.0, 9.5,
            before: t => motion.SetPointer(-1.0, 0.0, t),
            after: (_, _) => rising.Add(motion.PointerAttention));

        Assert.True(LivingCoreLooks.RestX - frame.CoreX > 8.0, $"core x {frame.CoreX}");
        Assert.True(motion.PointerAttention > 0.95, $"attention {motion.PointerAttention}");
        AssertMonotonic(rising, rising: true);

        // It holds a moment after the pointer stops, then fades without
        // overshooting back past rest.
        Step(motion, frame, 9.5 + (1.0 / 60.0), 11.8);
        Assert.True(motion.PointerAttention > 0.95, $"attention during hold {motion.PointerAttention}");

        var falling = new List<double>();
        Step(motion, frame, 12.0, 17.5, after: (_, _) => falling.Add(motion.PointerAttention));
        AssertMonotonic(falling, rising: false);
        Assert.True(motion.PointerAttention < 0.01, $"attention after settling {motion.PointerAttention}");
        Assert.InRange(MotionHarness.DistanceFromRest(frame), 0.0, 2.1);
    }

    [Fact]
    public void The_core_turns_toward_the_pointers_physical_side()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        MotionHarness.Run(motion, frame, 0.0, 8.0);

        Step(motion, frame, 8.0, 10.0, before: t => motion.SetPointer(1.0, 0.0, t));
        Assert.True(frame.CoreX > LivingCoreLooks.RestX + 3.0, $"right: {frame.CoreX}");

        Step(motion, frame, 10.0 + (1.0 / 60.0), 12.0, before: t => motion.SetPointer(0.0, -1.0, t));
        Assert.True(frame.CoreY < LivingCoreLooks.RestY - 2.5, $"up: {frame.CoreY}");
    }

    [Fact]
    public void A_pointer_on_the_core_itself_does_not_move_it()
    {
        var subject = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var twin = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        var twinFrame = new LivingCoreFrame();

        for (var i = 0; i <= 12 * 60; i++)
        {
            var t = i / MotionHarness.Fps;
            if (t >= 8.0)
            {
                subject.SetPointer(0.05, 0.03, t);
            }

            subject.Advance(t, NoVoice, Leading, frame);
            twin.Advance(t, NoVoice, Leading, twinFrame);
            Assert.Equal(twinFrame.CoreX, frame.CoreX, 1e-9);
            Assert.Equal(twinFrame.CoreY, frame.CoreY, 1e-9);
        }
    }

    [Fact]
    public void The_pointer_never_takes_the_core_out_of_its_zone()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();

        Step(motion, frame, 0.0, 60.0,
            before: t => motion.SetPointer(2.0 * Math.Cos(t * 1.3), 2.0 * Math.Sin(t * 1.3), t),
            after: (_, f) => Assert.InRange(MotionHarness.ZoneRadius(f), 0.0, 1.0 + 1e-9));
    }

    [Theory]
    [InlineData(LivingCoreState.Listening)]
    [InlineData(LivingCoreState.Thinking)]
    [InlineData(LivingCoreState.Speaking)]
    [InlineData(LivingCoreState.Blocked)]
    [InlineData(LivingCoreState.Error)]
    public void Only_idle_attends_to_the_pointer(LivingCoreState state)
    {
        var look = LivingCoreLooks.For(state);
        var motion = new LivingCoreMotion(state, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();

        Step(motion, frame, 0.0, 10.0, before: t => motion.SetPointer(-1.5, 1.0, t), after: (t, f) =>
        {
            Assert.Equal(0.0, motion.PointerAttention);
            if (state != LivingCoreState.Blocked || t > 4.0)
            {
                // Blocked's single request glance is over by then.
                Assert.Equal(look.Core.X, f.CoreX, 1e-6);
                Assert.Equal(look.Core.Y, f.CoreY, 1e-6);
            }
        });
    }

    [Fact]
    public void Leaving_the_window_lets_the_core_settle()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        MotionHarness.Run(motion, frame, 0.0, 8.0);
        Step(motion, frame, 8.0, 9.0, before: t => motion.SetPointer(-1.0, 0.5, t));
        Assert.True(motion.PointerAttention > 0.9);

        motion.ClearPointer();
        Step(motion, frame, 9.0 + (1.0 / 60.0), 14.0);

        Assert.True(motion.PointerAttention < 0.01, $"attention {motion.PointerAttention}");
    }

    // ---- States and transitions ----

    [Fact]
    public void Every_state_is_clearly_distinct_when_settled()
    {
        var signatures = new Dictionary<LivingCoreState, double[]>();
        foreach (var state in Enum.GetValues<LivingCoreState>())
        {
            var motion = new LivingCoreMotion(state, 0.0, fromDormant: false, reducedMotion: true);
            var f = new LivingCoreFrame();
            motion.Advance(1.0, NoVoice, Leading, f);
            signatures[state] =
            [
                (f.CoreX - LivingCoreLooks.ZoneX) / LivingCoreLooks.ZoneRadiusX,
                (f.CoreY - LivingCoreLooks.ZoneY) / LivingCoreLooks.ZoneRadiusY,
                f.CoreScale, f.CoreOpacity, f.AttentionOpacity, f.SpeakRingOpacity,
                f.LinkOpacity, f.GapRingOpacity, f.FragmentOpacity, f.CrackOpacity, f.RimDanger,
                f.ThreadsOpacity, f.OutwardOpacity[0], f.HaloOpacity[0], f.AuraOpacity,
            ];
        }

        var states = signatures.Keys.ToArray();
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

                Assert.True(Math.Sqrt(sum) > 1.0, $"{states[a]} and {states[b]} look too alike: {Math.Sqrt(sum):0.000}");
            }
        }
    }

    [Fact]
    public void State_changes_never_jump()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: true);
        var frame = new LivingCoreFrame();
        var changes = new Dictionary<int, LivingCoreState>
        {
            [300] = LivingCoreState.Listening,
            [350] = LivingCoreState.Thinking,
            [420] = LivingCoreState.Speaking,
            [470] = LivingCoreState.Blocked,
            [700] = LivingCoreState.Error,
            [760] = LivingCoreState.Idle,
            [1000] = LivingCoreState.Speaking,
            [1030] = LivingCoreState.Idle,
            [1300] = LivingCoreState.Thinking,
        };

        double[]? previous = null;
        for (var i = 0; i < 1800; i++)
        {
            var t = i / MotionHarness.Fps;
            if (changes.TryGetValue(i, out var state))
            {
                motion.SetState(state, t);
            }

            motion.Advance(t, NoVoice, Leading, frame);
            double[] now = [frame.CoreX, frame.CoreY, frame.CoreScale, frame.CoreOpacity, frame.AuraOpacity, frame.HaloOpacity[0], frame.ThreadsOpacity];
            if (previous is not null && i > 120)
            {
                // The largest pose change is about 20 units; a frame moves at
                // most a sixth of that, on the cascade's settle curve.
                var moved = Math.Sqrt(Math.Pow(now[0] - previous[0], 2) + Math.Pow(now[1] - previous[1], 2));
                Assert.True(moved <= 3.5, $"core jumped {moved:0.00} units at {t:0.000} s");
                for (var k = 2; k < now.Length; k++)
                {
                    Assert.True(Math.Abs(now[k] - previous[k]) <= 0.1, $"value {k} jumped {Math.Abs(now[k] - previous[k]):0.000} at {t:0.000} s");
                }
            }

            previous = now;
        }
    }

    [Fact]
    public void A_change_mid_cascade_starts_from_the_frame_on_screen()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        MotionHarness.Run(motion, frame, 0.0, 10.0);
        motion.SetState(LivingCoreState.Thinking, 10.0);
        MotionHarness.Run(motion, frame, 10.0, 10.5);

        var before = MotionHarness.Snapshot(frame);
        motion.SetState(LivingCoreState.Speaking, 10.5);
        motion.Advance(10.5, NoVoice, Leading, frame);
        var after = MotionHarness.Snapshot(frame);

        Assert.Equal(before.Length, after.Length);
        for (var i = 0; i < before.Length; i++)
        {
            Assert.Equal(before[i], after[i], 1e-9);
        }
    }

    // ---- Reduced motion ----

    [Theory]
    [MemberData(nameof(States))]
    public void Reduced_motion_holds_every_wander(LivingCoreState state)
    {
        var motion = new LivingCoreMotion(state, 0.0, fromDormant: false, reducedMotion: true);
        var frame = new LivingCoreFrame();

        Step(motion, frame, 0.0, 6.0, before: t => motion.SetPointer(-1.0, -1.0, t), after: (_, f) =>
        {
            Assert.All(f.StarGroupX, x => Assert.Equal(0.0, x));
            Assert.All(f.StarGroupY, y => Assert.Equal(0.0, y));
            Assert.All(f.BobX, x => Assert.Equal(0.0, x));
            Assert.All(f.BobY, y => Assert.Equal(0.0, y));
            Assert.All(f.CellGlow, g => Assert.Equal(1.0, g));
            Assert.Equal(0.0, motion.PointerAttention);
        });

        var look = LivingCoreLooks.For(state);
        Assert.Equal(look.Core.X, frame.CoreX, 1e-9);
        Assert.Equal(look.Core.Y, frame.CoreY, 1e-9);
    }

    // ---- Helpers ----

    /// <summary>
    /// Frame by frame from <paramref name="from"/> to <paramref name="to"/>
    /// inclusive: <paramref name="before"/> ahead of each Advance (to report
    /// the pointer), <paramref name="after"/> with the frame it wrote.
    /// </summary>
    private static void Step(
        LivingCoreMotion motion,
        LivingCoreFrame frame,
        double from,
        double to,
        Action<double>? before = null,
        Action<double, LivingCoreFrame>? after = null)
    {
        var steps = (int)Math.Round((to - from) * MotionHarness.Fps);
        for (var i = 0; i <= steps; i++)
        {
            var t = from + (i / MotionHarness.Fps);
            before?.Invoke(t);
            motion.Advance(t, NoVoice, Leading, frame);
            after?.Invoke(t, frame);
        }
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
