using HAMMOR.App.Presence;
using Xunit;

namespace HAMMOR.App.Tests.Presence;

/// <summary>
/// The Living Core motion engine against the approved motion system: cascade
/// order, idle choreography, the six P0 states and reduced motion.
/// </summary>
public sealed class LivingCoreMotionTests
{
    private const double NoVoice = double.NaN;
    private const double Leading = -1.0;

    // ---- State-change cascade ----

    [Fact]
    public void Cascade_moves_the_white_core_first_and_the_aura_last()
    {
        var early = LivingCoreMotion.GetCascadeProgress(0.3, reducedMotion: false);
        Assert.True(early.Core > 0.95, $"core {early.Core}");
        Assert.InRange(early.Inside, 0.5, 0.7);
        Assert.Equal(0.0, early.Halo);
        Assert.Equal(0.0, early.Aura);

        var middle = LivingCoreMotion.GetCascadeProgress(0.8, reducedMotion: false);
        Assert.Equal(1.0, middle.Core);
        Assert.Equal(1.0, middle.Inside);
        Assert.Equal(0.0, middle.Aura);

        Assert.True(LivingCoreMotion.GetCascadeProgress(LivingCoreMotion.CascadeSeconds, reducedMotion: false).IsComplete);
    }

    [Fact]
    public void The_white_core_reaches_its_new_pose_before_the_aura_changes()
    {
        var subject = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var twin = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        var twinFrame = new LivingCoreFrame();

        MotionHarness.Run(subject, frame, 0.0, 10.0);
        subject.SetState(LivingCoreState.Thinking, 10.0);
        subject.Advance(10.45, NoVoice, Leading, frame);
        twin.Advance(10.45, NoVoice, Leading, twinFrame);

        Assert.Equal(0.78, frame.CoreScale, 1e-6);
        Assert.Equal(twinFrame.AuraOpacity, frame.AuraOpacity, 1e-9);
    }

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

    // ---- The white core ----

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
    public void Idle_takes_one_unhurried_glance_about_every_14_seconds_and_returns_to_rest()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        var starts = new List<double>();
        var ends = new List<double>();
        var away = false;
        var restSamples = new List<double>();

        MotionHarness.Run(motion, frame, 0.0, 60.0, (t, f) =>
        {
            var distance = MotionHarness.DistanceFromRest(f);
            if (distance > 2.5 && !away)
            {
                starts.Add(t);
                away = true;
            }
            else if (distance <= 2.5 && away)
            {
                ends.Add(t);
                away = false;
            }

            // Moments between glances: only the micro-drift moves the core.
            if (Math.Abs(t - 3.0) < 1e-6 || Math.Abs(t - 12.0) < 1e-6 || Math.Abs(t - 30.0) < 1e-6 || Math.Abs(t - 58.0) < 1e-6)
            {
                restSamples.Add(distance);
            }
        });

        Assert.Equal(4, starts.Count);
        Assert.Equal(4, ends.Count);
        for (var i = 1; i < starts.Count; i++)
        {
            Assert.InRange(starts[i] - starts[i - 1], 12.0, 17.0);
        }

        for (var i = 0; i < starts.Count; i++)
        {
            // Never a stare: each glance is over within three seconds.
            Assert.InRange(ends[i] - starts[i], 0.5, 3.0);
        }

        Assert.Equal(4, restSamples.Count);
        Assert.All(restSamples, d => Assert.InRange(d, 0.0, 1.0));
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

    // ---- States ----

    [Fact]
    public void Thinking_turns_inward_dims_and_keeps_the_aura_steady()
    {
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        MotionHarness.Run(motion, frame, 0.0, 10.0);
        motion.SetState(LivingCoreState.Thinking, 10.0);

        var maxSignal = 0.0;
        MotionHarness.Run(motion, frame, 10.0, 14.0, (t, f) =>
        {
            if (t < 12.0)
            {
                return;
            }

            Assert.Equal(56.35, f.CoreX, 1e-6);
            Assert.Equal(58.9, f.CoreY, 1e-6);
            Assert.Equal(0.78, f.CoreScale, 1e-9);
            Assert.Equal(0.55, f.CoreOpacity, 1e-9);
            Assert.Equal(1.0, f.LinkOpacity, 1e-9);
            Assert.Equal(1.0, f.AuraOpacity, 1e-12);
            Assert.All(f.BobY, y => Assert.Equal(0.0, y, 1e-12));
            maxSignal = Math.Max(maxSignal, f.SignalOpacity.Max());
        });

        Assert.Equal(1.0, maxSignal, 1e-9);
    }

    [Fact]
    public void Speaking_centres_the_core_and_pulses_up_from_125_percent()
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

            // The halo rings themselves pulse outward; none stay behind.
            Assert.All(f.HaloOpacity, o => Assert.Equal(0.0, o));
            minScale = Math.Min(minScale, f.CoreScale);
            maxScale = Math.Max(maxScale, f.CoreScale);
            maxRing = Math.Max(maxRing, f.OutwardOpacity.Max());
        });

        Assert.InRange(minScale, 1.25 - 1e-9, 1.26);
        Assert.InRange(maxScale, 1.45, (1.25 * 1.176) + 1e-9);
        Assert.True(maxRing > 0.9, $"outward rings {maxRing}");
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
    public void Listening_holds_the_core_and_draws_the_halo_inward()
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
            maxInward = Math.Max(maxInward, f.InwardOpacity.Max());
        });

        // As the prototype: the rings draw in while it listens. Without input
        // they hold at their resting strength; typing lifts them.
        Assert.InRange(maxInward, 0.5, 0.55 + 1e-9);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(1.0)]
    public void Blocked_glances_once_toward_the_request_then_waits_low_and_centred(double direction)
    {
        var poseX = LivingCoreLooks.Blocked.Core.X;
        var poseY = LivingCoreLooks.Blocked.Core.Y;
        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false);
        var frame = new LivingCoreFrame();
        MotionHarness.Run(motion, frame, 0.0, 2.0, direction: direction);
        motion.SetState(LivingCoreState.Blocked, 2.0);

        // Mid-glance: 9 units toward the request side, slightly up.
        MotionHarness.Run(motion, frame, 2.0, 3.4, direction: direction);
        Assert.Equal(poseX + (9.0 * Math.Sign(direction)), frame.CoreX, 0.05);
        Assert.Equal(poseY - 2.0, frame.CoreY, 0.05);

        // Then it waits: no second glance, threads parked, a gap, no alarm.
        MotionHarness.Run(motion, frame, 3.4, 30.0, (t, f) =>
        {
            if (t < 8.0)
            {
                return;
            }

            Assert.Equal(poseX, f.CoreX, 1e-6);
            Assert.Equal(poseY, f.CoreY, 1e-6);
            Assert.Equal(0.0, f.ThreadsOpacity);
            Assert.InRange(f.ParkOpacity, 0.45 - 1e-9, 1.0);
            Assert.Equal(1.0, f.GapRingOpacity);
            Assert.Equal(0.0, f.HaloOpacity[1]);
            Assert.Equal(0.0, f.RimDanger);
        }, direction: direction);
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
        Assert.True(frame.ParkOpacity > 0.0, $"park {frame.ParkOpacity}");
        Assert.Equal(1.0, frame.GapRingOpacity);
        Assert.Equal(0.0, frame.ThreadsOpacity);
        Assert.True(blocked.NeedsContinuousFrames(1.0));

        var speaking = new LivingCoreMotion(LivingCoreState.Speaking, 0.0, fromDormant: false, reducedMotion: true);
        speaking.Advance(1.0, NoVoice, Leading, frame);
        var glowAtOne = frame.GlowOpacity;
        Assert.Equal(1.25, frame.CoreScale, 1e-9);
        Assert.Equal(new[] { 1.0, 1.0, 1.0 }, frame.OutwardOpacity);
        Assert.Equal(new[] { 1.06, 1.18, 1.3 }, frame.OutwardScale);
        Assert.Equal(new[] { 0.0, 0.0, 0.0 }, frame.HaloOpacity);
        speaking.Advance(2.0, NoVoice, Leading, frame);
        Assert.Equal(1.25, frame.CoreScale, 1e-9);

        // The pulse becomes a slow change of light, not movement.
        Assert.NotEqual(glowAtOne, frame.GlowOpacity, 1e-3);

        // Listening drops the attention ring's breathing and, without a real
        // voice level or typing, settles to a still frame: the rings held in
        // place at their resting light.
        var listening = new LivingCoreMotion(LivingCoreState.Listening, 0.0, fromDormant: false, reducedMotion: true);
        listening.Advance(1.0, NoVoice, Leading, frame);
        Assert.Equal(1.12, frame.CoreScale, 1e-9);
        Assert.Equal(1.0, frame.AttentionOpacity);
        listening.Advance(2.6, NoVoice, Leading, frame);
        Assert.Equal(1.0, frame.AttentionOpacity);
        Assert.All(frame.InwardOpacity, o => Assert.Equal(0.55, o, 1e-9));
        Assert.Equal(new[] { 1.22, 1.12, 1.03 }, frame.InwardScale);
        Assert.False(listening.NeedsContinuousFrames(2.6));

        // A real voice level shows the voice rings as light, held in place.
        listening.Advance(3.0, 0.8, Leading, frame);
        Assert.True(frame.InwardOpacity[0] > 0.0, $"inward {frame.InwardOpacity[0]}");
        Assert.Equal(new[] { 1.22, 1.12, 1.03 }, frame.InwardScale);
        Assert.True(listening.NeedsContinuousFrames(3.0));
    }

    [Fact]
    public void Reduced_motion_changes_state_with_a_short_blend()
    {
        var half = LivingCoreMotion.GetCascadeProgress(0.2, reducedMotion: true);
        Assert.Equal(half.Core, half.Aura);
        Assert.True(LivingCoreMotion.GetCascadeProgress(LivingCoreMotion.ReducedTransitionSeconds, reducedMotion: true).IsComplete);

        var motion = new LivingCoreMotion(LivingCoreState.Idle, 0.0, fromDormant: false, reducedMotion: true);
        motion.SetState(LivingCoreState.Error, 1.0);
        Assert.True(motion.IsTransitioning(1.3));
        Assert.False(motion.IsTransitioning(1.41));
    }
}
