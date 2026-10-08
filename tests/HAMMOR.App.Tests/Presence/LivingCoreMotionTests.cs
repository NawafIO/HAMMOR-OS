using HAMMOR.App.Presence;
using Xunit;

namespace HAMMOR.App.Tests.Presence;

/// <summary>
/// The Living Core motion engine against the approved reference (the canvas
/// prototype, as recorded in the reference video): startup, idle life, the
/// states' poses and loops, and reduced motion.
/// </summary>
public sealed class LivingCoreMotionTests
{
    private const double NoVoice = double.NaN;
    private const double Leading = -1.0;
    private const double Frame = 1.0 / MotionHarness.Fps;

    // ---- Startup: the Wake ignition ----

    [Fact]
    public void A_new_core_arrives_white_core_first_aura_last()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: true);
        var frame = new LivingCoreFrame();

        motion.Advance(0.0, NoVoice, Leading, frame);
        motion.Advance(0.3, NoVoice, Leading, frame);
        Assert.True(frame.CoreOpacity > 0.95, $"core {frame.CoreOpacity}");
        Assert.Equal(0.0, frame.AuraOpacity);
        Assert.Equal(0.0, frame.ThreadsOpacity);

        // The app opens with the reference's Wake ignition: 2.4 s, the aura
        // and the threads arriving last, at its end.
        motion.Advance(2.0, NoVoice, Leading, frame);
        Assert.True(frame.AuraOpacity < 0.7, $"aura at 2.0 s {frame.AuraOpacity}");
        motion.Advance(LivingCoreMotion.WakeSeconds, NoVoice, Leading, frame);
        Assert.True(frame.AuraOpacity > 0.7, $"aura {frame.AuraOpacity}");
        Assert.Equal(1.0, frame.ThreadsOpacity);
    }

    [Fact]
    public void Startup_starts_from_a_point_of_light_and_swells_past_full()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: true);
        var frame = new LivingCoreFrame();

        motion.Advance(0.0, NoVoice, Leading, frame);
        Assert.Equal(0.0, frame.CoreScale);
        Assert.Equal(0.0, frame.CoreOpacity);
        Assert.Equal(0.0, frame.RimDrawn);
        Assert.Equal(0.0, frame.AuraOpacity);

        // 150% at 12% of the ignition, then it settles to full size.
        motion.Advance(0.12 * LivingCoreMotion.WakeSeconds, NoVoice, Leading, frame);
        Assert.Equal(1.5, frame.CoreScale, 1e-9);
        motion.Advance(LivingCoreMotion.WakeSeconds, NoVoice, Leading, frame);
        Assert.Equal(1.0, frame.CoreScale, 1e-9);
        Assert.Equal(1.0, frame.RimDrawn);
    }

    [Fact]
    public void Startup_shows_wake_for_2_6_seconds_then_the_state_set_meanwhile()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: true);
        var frame = new LivingCoreFrame();

        Assert.Equal(LivingCoreState.Wake, motion.ShownState(0.0));
        Assert.True(motion.IsWaking(0.1));

        motion.SetState(LivingCoreState.Thinking, 1.0);
        MotionHarness.Run(motion, frame, 0.0, LivingCoreMotion.WakeHoldSeconds - Frame);
        Assert.Equal(LivingCoreState.Wake, motion.ShownState(LivingCoreMotion.WakeHoldSeconds - Frame));
        Assert.False(motion.IsWaking(LivingCoreMotion.WakeHoldSeconds - Frame));
        Assert.Equal(1.0, frame.CoreScale, 1e-9);

        motion.Advance(LivingCoreMotion.WakeHoldSeconds, NoVoice, Leading, frame);
        Assert.Equal(LivingCoreState.Thinking, motion.ShownState(LivingCoreMotion.WakeHoldSeconds));
        Assert.Equal(0.0, frame.LinkOpacity);
        MotionHarness.Run(motion, frame, LivingCoreMotion.WakeHoldSeconds + Frame, 4.0);
        Assert.Equal(0.78, frame.CoreScale, 1e-9);
        Assert.Equal(1.0, frame.LinkOpacity, 1e-9);
    }

    [Fact]
    public void Reduced_motion_starts_without_the_ignition()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: true, reducedMotion: true);
        var frame = new LivingCoreFrame();

        motion.Advance(0.0, NoVoice, Leading, frame);

        Assert.Equal(LivingCoreState.Idle, motion.ShownState(0.0));
        Assert.False(motion.IsWaking(0.0));
        Assert.Equal(1.0, frame.CoreScale);
        Assert.Equal(1.0, frame.CoreOpacity);
        Assert.Equal(1.0, frame.AuraOpacity);
    }

    [Fact]
    public void Transitions_are_deterministic()
    {
        static List<double[]> Script()
        {
            var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: true);
            var frame = new LivingCoreFrame();
            var frames = new List<double[]>();
            for (var i = 0; i < 1800; i++)
            {
                var t = i / 60.0;
                switch (i)
                {
                    case 300:
                        motion.SetState(LivingCoreState.Listening, t);
                        break;
                    case 420:
                        motion.SetState(LivingCoreState.Thinking, t);
                        break;
                    case 700:
                        motion.SetState(LivingCoreState.Speaking, t);
                        break;
                    case 1000:
                        motion.SetState(LivingCoreState.Blocked, t);
                        break;
                    case 1300:
                        motion.SetState(LivingCoreState.Error, t);
                        break;
                }

                motion.Advance(t, NoVoice, Leading, frame);
                frames.Add(MotionHarness.Snapshot(frame));
            }

            return frames;
        }

        var first = Script();
        var second = Script();

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i], second[i]);
        }
    }

    // ---- Idle ----

    [Fact]
    public void Idle_drifts_gently_around_rest_on_a_9_6_second_loop()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        var dx = new List<double>();
        var dy = new List<double>();

        MotionHarness.Run(motion, frame, 0.0, 9.6, (_, f) =>
        {
            dx.Add(f.CoreX - LivingCoreLooks.RestX);
            dy.Add(f.CoreY - LivingCoreLooks.RestY);
        });

        // Prototype "drift": under a unit each way, back where it started.
        Assert.Equal(0.8, dx.Max(), 1e-9);
        Assert.Equal(-0.7, dx.Min(), 1e-9);
        Assert.Equal(0.7, dy.Max(), 1e-9);
        Assert.Equal(-0.5, dy.Min(), 1e-9);
        Assert.Equal(dx[0], dx[^1], 1e-9);
        Assert.Equal(dy[0], dy[^1], 1e-9);
    }

    [Fact]
    public void The_white_core_never_leaves_its_zone()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: true);
        var frame = new LivingCoreFrame();
        var worst = 0.0;

        void Track(double t, LivingCoreFrame f) => worst = Math.Max(worst, MotionHarness.ZoneRadius(f));

        MotionHarness.Run(motion, frame, 0.0, 60.0, Track);

        var start = 60.0;
        LivingCoreState[] sequence =
        [
            LivingCoreState.Listening,
            LivingCoreState.Thinking,
            LivingCoreState.Speaking,
            LivingCoreState.Blocked,
            LivingCoreState.Error,
            LivingCoreState.Success,
            LivingCoreState.Warning,
            LivingCoreState.Sleep,
            LivingCoreState.Wake,
            LivingCoreState.Idle,
        ];
        foreach (var state in sequence)
        {
            motion.SetState(state, start);
            MotionHarness.Run(motion, frame, start, start + 8.0, Track);
            start += 8.0;
        }

        Assert.InRange(worst, 0.0, 1.0 + 1e-9);
    }

    [Fact]
    public void Threads_orbit_in_alternating_directions_at_their_own_tempo()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();

        motion.Advance(0.0, NoVoice, Leading, frame);
        var before = (double[])frame.ThreadAngle.Clone();
        MotionHarness.Run(motion, frame, 0.0, 1.0);

        Assert.Equal(360.0 / 14.0, frame.ThreadAngle[0] - before[0], 1e-6);
        Assert.Equal(-360.0 / 22.0, frame.ThreadAngle[1] - before[1], 1e-6);
        Assert.Equal(360.0 / 30.0, frame.ThreadAngle[2] - before[2], 1e-6);
        Assert.Equal(-360.0 / 42.0, frame.ThreadAngle[3] - before[3], 1e-6);
    }

    [Fact]
    public void Idle_keeps_every_layer_moving()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();

        static double[] Layers(LivingCoreFrame f) =>
        [
            f.AuraScale, f.Energy1Angle, f.Energy1Flow, f.HaloScale[0], f.ThreadAngle[0], f.StarsAngle,
            f.StarClockOpacity[0], f.CellsAngle, f.BobY[0], f.CrestAngle, f.LateralFlow, f.CoreX,
        ];

        motion.Advance(1.0, NoVoice, Leading, frame);
        var first = Layers(frame);
        motion.Advance(2.0, NoVoice, Leading, frame);
        var second = Layers(frame);

        for (var i = 0; i < first.Length; i++)
        {
            Assert.True(Math.Abs(second[i] - first[i]) > 1e-3, $"layer {i} still: {first[i]} -> {second[i]}");
        }

        Assert.True(motion.NeedsContinuousFrames(2.0));
    }

    // ---- States ----

    [Fact]
    public void Thinking_turns_inward_dims_and_links_the_cells()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        MotionHarness.Run(motion, frame, 0.0, 10.0);
        motion.SetState(LivingCoreState.Thinking, 10.0);

        var maxSignal = 0.0;
        var lensTurn = new List<double>();
        MotionHarness.Run(motion, frame, 10.0, 15.0, (t, f) =>
        {
            if (t < 11.0)
            {
                return;
            }

            // Up and back, 78%, at 55% light; the pointer and drift are gone.
            Assert.Equal(56.35, f.CoreX, 1e-6);
            Assert.Equal(58.9, f.CoreY, 1e-6);
            Assert.Equal(0.78, f.CoreScale, 1e-9);
            Assert.Equal(0.55, f.CoreOpacity, 1e-9);
            Assert.Equal(1.0, f.LinkOpacity, 1e-9);
            Assert.Equal(1.0, f.OrbitOpacity, 1e-9);
            Assert.Equal(0.6, f.LensOpacity, 1e-9);
            Assert.All(f.BobY, y => Assert.Equal(0.0, y));
            maxSignal = Math.Max(maxSignal, f.SignalOpacity.Max());
            lensTurn.Add(f.LensAngle);
        });

        Assert.Equal(1.0, maxSignal, 1e-9);

        // The lens precesses ±5° over 4.8 s.
        Assert.InRange(lensTurn.Max(), 4.99, 5.0 + 1e-9);
        Assert.InRange(lensTurn.Min(), -5.0 - 1e-9, -4.99);
    }

    [Fact]
    public void Thinking_quickens_the_threads_and_the_stars()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Thinking, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();

        motion.Advance(0.0, NoVoice, Leading, frame);
        var threads = (double[])frame.ThreadAngle.Clone();
        var stars = frame.StarsAngle;
        MotionHarness.Run(motion, frame, 0.0, 1.0);

        Assert.Equal(360.0 / 8.0, frame.ThreadAngle[0] - threads[0], 1e-6);
        Assert.Equal(-360.0 / 12.0, frame.ThreadAngle[1] - threads[1], 1e-6);
        Assert.Equal(360.0 / 16.0, frame.ThreadAngle[2] - threads[2], 1e-6);
        Assert.Equal(-360.0 / 22.4, frame.ThreadAngle[3] - threads[3], 1e-6);
        Assert.Equal(360.0 / 60.0, frame.StarsAngle - stars, 1e-6);
    }

    [Fact]
    public void A_loop_that_changes_tempo_keeps_its_clock_as_css_does()
    {
        // The reference changes a running animation's duration: its start
        // stays, so its phase moves at once (the threads jump when Listening
        // speeds them up, as in the video).
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        MotionHarness.Run(motion, frame, 0.0, 10.0);
        Assert.Equal(360.0 * ((10.0 / 14.0) % 1.0), frame.ThreadAngle[0], 1e-6);

        motion.SetState(LivingCoreState.Listening, 10.0);
        motion.Advance(10.0, NoVoice, Leading, frame);
        Assert.Equal(360.0 * ((10.0 / 8.0) % 1.0), frame.ThreadAngle[0], 1e-6);
    }

    [Fact]
    public void Speaking_centres_the_core_and_pulses_from_125_to_147_percent()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        MotionHarness.Run(motion, frame, 0.0, 5.0);
        motion.SetState(LivingCoreState.Speaking, 5.0);

        var minScale = double.MaxValue;
        var maxScale = 0.0;
        var maxRing = 0.0;
        MotionHarness.Run(motion, frame, 5.0, 9.0, (t, f) =>
        {
            if (t < 7.0)
            {
                return;
            }

            Assert.Equal(LivingCoreLooks.ZoneX, f.CoreX, 1e-9);
            Assert.Equal(LivingCoreLooks.ZoneY, f.CoreY, 1e-9);
            Assert.Equal(0.95, f.VoiceWaveOpacity, 1e-9);
            Assert.Equal(1.0, f.SpeakRingOpacity, 1e-9);

            // The halo keeps breathing behind the voice rings.
            Assert.All(f.HaloOpacity, o => Assert.InRange(o, 0.75 - 1e-9, 1.0));
            minScale = Math.Min(minScale, f.CoreScale);
            maxScale = Math.Max(maxScale, f.CoreScale);
            maxRing = Math.Max(maxRing, f.OutwardOpacity.Max());
        });

        Assert.InRange(minScale, 1.25 - 1e-9, 1.26);
        Assert.InRange(maxScale, 1.46, 1.47 + 1e-9);
        Assert.True(maxRing > 0.9, $"outward rings {maxRing}");
    }

    [Fact]
    public void Speaking_starts_and_stops_its_pulse_at_once_as_the_reference_does()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        MotionHarness.Run(motion, frame, 0.0, 5.0);

        // The pulse's first keyframe shows on the first frame; the white
        // core then glides to the centre.
        motion.SetState(LivingCoreState.Speaking, 5.0);
        motion.Advance(5.0, NoVoice, Leading, frame);
        Assert.Equal(1.25, frame.CoreScale, 1e-12);
        Assert.Equal(LivingCoreLooks.RestX, frame.CoreX, 1e-9);

        // When it ends, the size goes straight to the next state's.
        MotionHarness.Run(motion, frame, 5.0 + Frame, 6.0);
        motion.SetState(LivingCoreState.Idle, 6.0);
        motion.Advance(6.0, NoVoice, Leading, frame);
        Assert.Equal(1.0, frame.CoreScale, 1e-12);
    }

    [Theory]
    [InlineData(0.0, 1.25)]
    [InlineData(1.0, 1.47)]
    public void Speaking_follows_a_real_speech_envelope_when_one_is_connected(double level, double expectedScale)
    {
        var motion = new LivingCoreMotion(LivingCoreState.Speaking, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();

        MotionHarness.Run(motion, frame, 0.0, 3.0, voice: level);

        Assert.Equal(expectedScale, frame.CoreScale, 0.005);
    }

    [Fact]
    public void Listening_holds_the_core_and_draws_rings_inward()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        MotionHarness.Run(motion, frame, 0.0, 3.0);
        motion.SetState(LivingCoreState.Listening, 3.0);

        var maxInward = 0.0;
        MotionHarness.Run(motion, frame, 3.0, 8.0, (t, f) =>
        {
            if (t < 5.0)
            {
                return;
            }

            Assert.Equal(LivingCoreLooks.ZoneX, f.CoreX, 1e-9);
            Assert.Equal(LivingCoreLooks.ZoneY, f.CoreY, 1e-9);
            Assert.Equal(1.12, f.CoreScale, 1e-9);
            Assert.InRange(f.AttentionOpacity, 0.55 - 1e-9, 1.0);
            Assert.Equal(0.95, f.LateralAOpacity);
            Assert.Equal(0.92, f.CellsSpread, 1e-9);
            maxInward = Math.Max(maxInward, f.InwardOpacity.Max());
        });

        Assert.True(maxInward > 0.99, $"inward rings {maxInward}");
    }

    [Fact]
    public void Listening_quickens_the_shimmer_the_current_and_the_inner_threads()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Listening, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        var shimmer = new List<double>();

        motion.Advance(0.0, NoVoice, Leading, frame);
        var thread = frame.ThreadAngle[0];
        MotionHarness.Run(motion, frame, 0.0, 1.0);
        Assert.Equal(360.0 / 8.0, frame.ThreadAngle[0] - thread, 1e-6);

        // Every star clock on the same 1.6 s shimmer; the current on 1.1 s.
        MotionHarness.Run(motion, frame, 1.0, 1.0 + 1.6, (_, f) => shimmer.Add(f.StarClockOpacity[0]));
        Assert.InRange(shimmer.Min(), 0.35 - 1e-9, 0.36);
        motion.Advance(2.7, NoVoice, Leading, frame);
        var flow = frame.LateralFlow;
        motion.Advance(2.7 + 1.1, NoVoice, Leading, frame);
        Assert.Equal(flow, frame.LateralFlow, 1e-6);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(1.0)]
    public void Blocked_glances_toward_the_request_every_6_4_seconds_then_waits_low(double direction)
    {
        var poseX = LivingCoreLooks.Blocked.X;
        var poseY = LivingCoreLooks.Blocked.Y;
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        MotionHarness.Run(motion, frame, 0.0, 2.0, direction: direction);
        motion.SetState(LivingCoreState.Blocked, 2.0);

        // Held glance: 9 units toward the request side, 2 up.
        MotionHarness.Run(motion, frame, 2.0, 2.0 + (0.3 * 6.4), direction: direction);
        Assert.Equal(poseX + (9.0 * Math.Sign(direction)), frame.CoreX, 1e-9);
        Assert.Equal(poseY - 2.0, frame.CoreY, 1e-9);

        // Then it waits, low and still, threads parked, a gap, no alarm.
        MotionHarness.Run(motion, frame, 2.0 + (0.3 * 6.4) + Frame, 2.0 + (0.8 * 6.4), direction: direction);
        Assert.Equal(poseX, frame.CoreX, 1e-9);
        Assert.Equal(poseY, frame.CoreY, 1e-9);
        Assert.Equal(0.0, frame.ThreadsOpacity);
        Assert.InRange(frame.ParkOpacity, 0.45 - 1e-9, 1.0);
        Assert.Equal(1.0, frame.GapRingOpacity);
        Assert.Equal(0.0, frame.RimDanger);

        // And glances again on the next cycle.
        MotionHarness.Run(motion, frame, 2.0 + (0.8 * 6.4) + Frame, 2.0 + 6.4 + (0.3 * 6.4), direction: direction);
        Assert.Equal(poseX + (9.0 * Math.Sign(direction)), frame.CoreX, 1e-9);
    }

    [Fact]
    public void Error_dims_splits_the_membrane_and_breaks_the_threads()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        MotionHarness.Run(motion, frame, 0.0, 2.0);
        motion.SetState(LivingCoreState.Error, 2.0);

        MotionHarness.Run(motion, frame, 2.0, 20.0, (t, f) =>
        {
            if (t < 4.0)
            {
                return;
            }

            Assert.Equal(75.92, f.CoreX, 1e-6);
            Assert.Equal(60.98, f.CoreY, 1e-6);
            Assert.Equal(0.9, f.CoreScale, 1e-9);
            Assert.Equal(0.6, f.CoreOpacity, 1e-9);
            Assert.Equal(1.0, f.CrackOpacity);
            Assert.Equal(1.0, f.FragmentOpacity);
            Assert.Equal(0.0, f.ThreadsOpacity);
            Assert.Equal(1.0, f.RimDanger);
            Assert.Equal(1.18, f.CellsSpread, 1e-9);
            Assert.Equal(0.5, f.CellsOpacity, 1e-9);
            Assert.Equal(0.45, f.LensOpacity, 1e-9);
            Assert.InRange(f.HaloOpacity[0], 0.25 - 1e-9, 0.55 + 1e-9);
            Assert.InRange(f.AuraOpacity, 0.0, 0.3 + 1e-9);
            Assert.All(f.StarClockOpacity, o => Assert.InRange(o, 0.0, 0.3 + 1e-9));
        });

        // Error holds until the user acts; the engine never times it out.
        Assert.Equal(LivingCoreState.Error, motion.State);
    }

    // ---- Reduced motion ----

    [Fact]
    public void Reduced_motion_idle_is_a_still_frame_that_costs_nothing()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false, reducedMotion: true);
        var frame = new LivingCoreFrame();

        motion.Advance(1.0, NoVoice, Leading, frame);
        var first = MotionHarness.Snapshot(frame);
        motion.Advance(6.0, NoVoice, Leading, frame);

        Assert.Equal(first, MotionHarness.Snapshot(frame));
        Assert.Equal(LivingCoreLooks.RestX, frame.CoreX);
        Assert.Equal(LivingCoreLooks.RestY, frame.CoreY);
        Assert.False(motion.NeedsContinuousFrames(6.0));
    }

    [Fact]
    public void Reduced_motion_keeps_each_state_readable()
    {
        var frame = new LivingCoreFrame();

        var thinking = new LivingCoreMotion(LivingCoreState.Thinking, 0.0, fromDormant: false, reducedMotion: true);
        thinking.Advance(1.0, NoVoice, Leading, frame);
        Assert.Equal(0.78, frame.CoreScale, 1e-9);
        Assert.Equal(56.35, frame.CoreX, 1e-6);
        Assert.Equal(1.0, frame.LinkOpacity);
        Assert.All(frame.SignalOpacity, o => Assert.Equal(1.0, o));
        Assert.False(thinking.NeedsContinuousFrames(1.0));

        var blocked = new LivingCoreMotion(LivingCoreState.Blocked, 0.0, fromDormant: false, reducedMotion: true);
        blocked.Advance(1.0, NoVoice, Leading, frame);
        Assert.Equal(1.0, frame.ParkOpacity);
        Assert.Equal(1.0, frame.GapRingOpacity);
        Assert.Equal(0.0, frame.ThreadsOpacity);
        Assert.False(blocked.NeedsContinuousFrames(1.0));

        var speaking = new LivingCoreMotion(LivingCoreState.Speaking, 0.0, fromDormant: false, reducedMotion: true);
        speaking.Advance(1.0, NoVoice, Leading, frame);
        Assert.Equal(1.25, frame.CoreScale, 1e-9);
        Assert.Equal(new[] { 1.0, 1.0, 1.0 }, frame.OutwardOpacity);
        Assert.Equal(new[] { 1.06, 1.18, 1.3 }, frame.OutwardScale);
        Assert.Equal(0.95, frame.VoiceWaveOpacity, 1e-9);
        Assert.False(speaking.NeedsContinuousFrames(1.0));

        // Listening holds its rings in place at full strength, voice or not.
        var listening = new LivingCoreMotion(LivingCoreState.Listening, 0.0, fromDormant: false, reducedMotion: true);
        listening.Advance(1.0, NoVoice, Leading, frame);
        Assert.Equal(1.12, frame.CoreScale, 1e-9);
        Assert.Equal(1.0, frame.AttentionOpacity);
        Assert.Equal(new[] { 1.0, 1.0, 1.0 }, frame.InwardOpacity);
        Assert.Equal(new[] { 1.22, 1.12, 1.03 }, frame.InwardScale);
        listening.Advance(3.0, 0.8, Leading, frame);
        Assert.Equal(new[] { 1.0, 1.0, 1.0 }, frame.InwardOpacity);
        Assert.False(listening.NeedsContinuousFrames(3.0));
    }

    [Fact]
    public void Reduced_motion_changes_state_with_a_short_blend()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false, reducedMotion: true);
        var frame = new LivingCoreFrame();
        motion.Advance(1.0, NoVoice, Leading, frame);
        motion.SetState(LivingCoreState.Error, 1.0);
        motion.Advance(1.0, NoVoice, Leading, frame);

        // The white core and the aura blend together, on one 0.4 s curve.
        motion.Advance(1.2, NoVoice, Leading, frame);
        var core = (1.0 - frame.CoreOpacity) / (1.0 - 0.6);
        var aura = (1.0 - frame.AuraOpacity) / (1.0 - 0.3);
        Assert.InRange(core, 0.5, 0.99);
        Assert.Equal(core, aura, 1e-9);

        Assert.True(motion.IsTransitioning(1.3));
        Assert.False(motion.IsTransitioning(1.0 + LivingCoreMotion.ReducedTransitionSeconds + 0.01));
    }
}
