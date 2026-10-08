namespace HAMMOR.App.Presence;

/// <summary>What a keyframe track moves on its element.</summary>
internal enum KeyframeChannel
{
    Scale = 0,
    Angle = 1,
    X = 2,
    Y = 3,
    ScaleX = 4,
    ScaleY = 5,
    Opacity = 6,
    Dash = 7,
}

/// <summary>
/// One channel's keyframes: offsets 0 to 1 and the values at them. The
/// animation's easing applies to each segment between two keyframes, as in
/// CSS.
/// </summary>
internal sealed class KeyframeTrack
{
    private readonly double[] _offsets;
    private readonly double[] _values;

    public KeyframeTrack(KeyframeChannel channel, double[] offsets, double[] values)
    {
        Channel = channel;
        _offsets = offsets;
        _values = values;
    }

    public KeyframeChannel Channel { get; }

    public double First => _values[0];

    public double Last => _values[^1];

    public double Evaluate(double progress, in CubicBezierEasing easing)
    {
        for (var i = 1; i < _offsets.Length; i++)
        {
            if (progress <= _offsets[i])
            {
                var span = _offsets[i] - _offsets[i - 1];
                var t = span <= 0.0 ? 1.0 : (progress - _offsets[i - 1]) / span;
                return _values[i - 1] + ((_values[i] - _values[i - 1]) * easing.Evaluate(t));
            }
        }

        return _values[^1];
    }
}

/// <summary>
/// A CSS <c>@keyframes</c> rule with its timing function, iteration count
/// and fill mode: the prototype's animations, verbatim.
/// </summary>
internal sealed class CssKeyframes
{
    private readonly KeyframeTrack?[] _tracks = new KeyframeTrack?[8];

    public CssKeyframes(CubicBezierEasing easing, bool once, bool fillBoth, params KeyframeTrack[] tracks)
    {
        Easing = easing;
        Once = once;
        FillBoth = fillBoth;
        foreach (var track in tracks)
        {
            _tracks[(int)track.Channel] = track;
        }
    }

    public CubicBezierEasing Easing { get; }

    /// <summary>One iteration only (otherwise it loops).</summary>
    public bool Once { get; }

    /// <summary><c>fill-mode: both</c>: holds its first values during a delay and its last after the end.</summary>
    public bool FillBoth { get; }

    public KeyframeTrack? Track(KeyframeChannel channel) => _tracks[(int)channel];

    /// <summary>Whether this rule animates the element's transform (any geometric channel).</summary>
    public bool AnimatesTransform =>
        _tracks[(int)KeyframeChannel.Scale] is not null
        || _tracks[(int)KeyframeChannel.Angle] is not null
        || _tracks[(int)KeyframeChannel.X] is not null
        || _tracks[(int)KeyframeChannel.Y] is not null
        || _tracks[(int)KeyframeChannel.ScaleX] is not null
        || _tracks[(int)KeyframeChannel.ScaleY] is not null;

    public bool AnimatesOpacity => _tracks[(int)KeyframeChannel.Opacity] is not null;
}

/// <summary>
/// A CSS animation applied to one element: its rule, when it started, its
/// duration and delay. A state change that keeps the same rule keeps the
/// start and may change the duration, which moves the phase at once, as CSS
/// does.
/// </summary>
internal struct CssAnimation
{
    public CssKeyframes? Keyframes;
    public double Start;
    public double Duration;
    public double Delay;

    public readonly bool IsActive => Keyframes is not null;

    /// <summary>
    /// The animated value of <paramref name="channel"/> at <paramref name="now"/>,
    /// or false when the animation has no effect then: no such channel,
    /// waiting out a positive delay, or finished without fill.
    /// </summary>
    public readonly bool TryGet(KeyframeChannel channel, double now, out double value)
    {
        value = 0.0;
        var keyframes = Keyframes;
        var track = keyframes?.Track(channel);
        if (keyframes is null || track is null)
        {
            return false;
        }

        var elapsed = now - Start - Delay;
        if (elapsed < 0.0)
        {
            if (!keyframes.FillBoth)
            {
                return false;
            }

            value = track.First;
            return true;
        }

        var progress = elapsed / Duration;
        if (keyframes.Once)
        {
            if (progress >= 1.0)
            {
                if (!keyframes.FillBoth)
                {
                    return false;
                }

                value = track.Last;
                return true;
            }
        }
        else
        {
            progress -= Math.Floor(progress);
        }

        value = track.Evaluate(progress, keyframes.Easing);
        return true;
    }

    /// <summary>The value, or <paramref name="otherwise"/> when the animation has no effect.</summary>
    public readonly double Get(KeyframeChannel channel, double now, double otherwise) =>
        TryGet(channel, now, out var value) ? value : otherwise;

    /// <summary>Whether a one-shot animation is still playing at <paramref name="now"/>.</summary>
    public readonly bool IsPlaying(double now) =>
        Keyframes is not null && (!Keyframes.Once || now - Start - Delay < Duration);
}

/// <summary>
/// A CSS transition on one number. A new target starts from the value on
/// screen (retargeting), so a change during a change never jumps.
/// </summary>
internal struct CssTransition
{
    private double _from;
    private double _to;
    private double _start;
    private double _duration;
    private CubicBezierEasing _easing;

    public CssTransition(double value)
    {
        _from = value;
        _to = value;
        _start = double.NegativeInfinity;
        _duration = 0.0;
        _easing = LivingCoreEasings.Linear;
    }

    public readonly double Target => _to;

    public readonly double At(double now)
    {
        if (_duration <= 0.0 || now >= _start + _duration)
        {
            return _to;
        }

        var t = (now - _start) / _duration;
        return t <= 0.0 ? _from : _from + ((_to - _from) * _easing.Evaluate(t));
    }

    public readonly bool IsRunning(double now) => _duration > 0.0 && now < _start + _duration;

    /// <summary>Transitions to <paramref name="target"/> unless it is already the target.</summary>
    public void Go(double target, double now, double duration, in CubicBezierEasing easing)
    {
        // Exact comparison on purpose: targets come from fixed tables, and
        // CSS only starts a transition when the computed value changes.
        if (target == _to)
        {
            return;
        }

        if (duration <= 0.0)
        {
            Snap(target);
            return;
        }

        _from = At(now);
        _to = target;
        _start = now;
        _duration = duration;
        _easing = easing;
    }

    /// <summary>Jumps to <paramref name="target"/>: no transition.</summary>
    public void Snap(double target)
    {
        _from = target;
        _to = target;
        _duration = 0.0;
    }
}

/// <summary>
/// The prototype's <c>@keyframes</c>, with the timing each one is used with.
/// Names follow what they do; the prototype's own name is in each comment.
/// </summary>
internal static class PrototypeKeyframes
{
    // au: the aura breathes 4% over 8 s.
    public static readonly CssKeyframes AuraBreath = Loop(
        LivingCoreEasings.Breath,
        Track(KeyframeChannel.Scale, [0.0, 0.5, 1.0], [0.96, 1.04, 0.96]),
        Track(KeyframeChannel.Opacity, [0.0, 0.5, 1.0], [0.78, 1.0, 0.78]));

    // rot / rotr: a full turn, clockwise or back.
    public static readonly CssKeyframes Turn = Loop(LivingCoreEasings.Linear, Track(KeyframeChannel.Angle, [0.0, 1.0], [0.0, 360.0]));

    public static readonly CssKeyframes TurnBack = Loop(LivingCoreEasings.Linear, Track(KeyframeChannel.Angle, [0.0, 1.0], [0.0, -360.0]));

    // flow: the energy lines' dashes travel 40 units.
    public static readonly CssKeyframes EnergyFlow = Loop(LivingCoreEasings.Linear, Track(KeyframeChannel.Dash, [0.0, 1.0], [0.0, -40.0]));

    // hb: a halo ring breathes 1.4% over 6.4 s.
    public static readonly CssKeyframes HaloBreath = Loop(
        LivingCoreEasings.Breath,
        Track(KeyframeChannel.Scale, [0.0, 0.5, 1.0], [1.0, 1.014, 1.0]),
        Track(KeyframeChannel.Opacity, [0.0, 0.5, 1.0], [0.75, 1.0, 0.75]));

    // tw: a star clock's shimmer.
    public static readonly CssKeyframes Twinkle = Loop(LivingCoreEasings.InOut, Track(KeyframeChannel.Opacity, [0.0, 0.5, 1.0], [1.0, 0.35, 1.0]));

    // bob: a cell group's slow bob.
    public static readonly CssKeyframes Bob = Loop(
        LivingCoreEasings.InOut,
        Track(KeyframeChannel.X, [0.0, 0.5, 1.0], [0.0, 0.6, 0.0]),
        Track(KeyframeChannel.Y, [0.0, 0.5, 1.0], [0.0, -1.3, 0.0]));

    // flowl: the lateral lines' dashes travel 14.4 units.
    public static readonly CssKeyframes LateralFlow = Loop(LivingCoreEasings.Linear, Track(KeyframeChannel.Dash, [0.0, 1.0], [0.0, -14.4]));

    // sway: the crest sways ±1.4°.
    public static readonly CssKeyframes CrestSway = Loop(LivingCoreEasings.Breath, Track(KeyframeChannel.Angle, [0.0, 0.5, 1.0], [-1.4, 1.4, -1.4]));

    // drift: the white core's idle drift.
    public static readonly CssKeyframes Drift = Loop(
        LivingCoreEasings.InOut,
        Track(KeyframeChannel.X, [0.0, 0.25, 0.5, 0.75, 1.0], [0.0, 0.8, 0.2, -0.7, 0.0]),
        Track(KeyframeChannel.Y, [0.0, 0.25, 0.5, 0.75, 1.0], [0.0, -0.5, 0.7, 0.2, 0.0]));

    // rin: a Listening ring draws in.
    public static readonly CssKeyframes InwardRing = Loop(
        LivingCoreEasings.RingIn,
        Track(KeyframeChannel.Scale, [0.0, 1.0], [1.3, 1.02]),
        Track(KeyframeChannel.Opacity, [0.0, 0.35, 1.0], [0.0, 1.0, 0.0]));

    // vox: the inner halo ring moves with the voice.
    public static readonly CssKeyframes Voice = Loop(
        LivingCoreEasings.Breath,
        Track(KeyframeChannel.Scale, [0.0, 0.18, 0.34, 0.52, 0.70, 0.86, 1.0], [1.0, 0.975, 1.008, 0.982, 1.012, 0.99, 1.0]));

    // att: the attention ring breathes.
    public static readonly CssKeyframes Attention = Loop(LivingCoreEasings.Breath, Track(KeyframeChannel.Opacity, [0.0, 0.5, 1.0], [0.55, 1.0, 0.55]));

    // sig: a signal travels along a Thinking link.
    public static readonly CssKeyframes Signal = Loop(LivingCoreEasings.Linear, Track(KeyframeChannel.Dash, [0.0, 1.0], [0.0, -60.1]));

    // prec: the lens precesses ±5° while Thinking.
    public static readonly CssKeyframes Precession = Loop(LivingCoreEasings.Breath, Track(KeyframeChannel.Angle, [0.0, 0.5, 1.0], [-5.0, 5.0, -5.0]));

    // rout: a Speaking ring leaves the halo.
    public static readonly CssKeyframes OutwardRing = Loop(
        LivingCoreEasings.RingOut,
        Track(KeyframeChannel.Scale, [0.0, 1.0], [1.0, 1.4]),
        Track(KeyframeChannel.Opacity, [0.0, 1.0], [1.0, 0.0]));

    // wv: the voice wave.
    public static readonly CssKeyframes VoiceWave = Loop(
        LivingCoreEasings.Breath,
        Track(KeyframeChannel.ScaleY, [0.0, 0.25, 0.5, 0.75, 1.0], [0.4, 1.2, 0.6, 1.4, 0.4]));

    // pls: the white core pulses with its own voice, 125% to 147%.
    public static readonly CssKeyframes SpeakPulse = Loop(
        LivingCoreEasings.Breath,
        Track(KeyframeChannel.Scale, [0.0, 0.2, 0.4, 0.6, 0.8, 1.0], [1.25, 1.42, 1.29, 1.47, 1.31, 1.25]));

    // aus: the aura pulses with speech.
    public static readonly CssKeyframes AuraPulse = Loop(
        LivingCoreEasings.Breath,
        Track(KeyframeChannel.Scale, [0.0, 0.3, 0.6, 1.0], [1.0, 1.06, 1.02, 1.0]),
        Track(KeyframeChannel.Opacity, [0.0, 0.3, 0.6, 1.0], [0.9, 1.0, 0.95, 0.9]));

    // thr: the threads pulse with speech.
    public static readonly CssKeyframes ThreadPulse = Loop(
        LivingCoreEasings.Breath,
        Track(KeyframeChannel.Opacity, [0.0, 0.3, 0.6, 0.8, 1.0], [0.7, 1.0, 0.82, 1.0, 0.7]));

    // warm: Success's warm bloom.
    public static readonly CssKeyframes WarmBloom = Loop(
        LivingCoreEasings.Settle,
        Track(KeyframeChannel.Scale, [0.0, 0.25, 1.0], [0.95, 1.12, 1.0]),
        Track(KeyframeChannel.Opacity, [0.0, 0.25, 1.0], [0.4, 1.0, 0.55]));

    // ring1: Success's green ring expands and dissolves.
    public static readonly CssKeyframes SuccessRing = Loop(
        LivingCoreEasings.Strike,
        Track(KeyframeChannel.Scale, [0.0, 0.45, 1.0], [0.95, 1.5, 1.5]),
        Track(KeyframeChannel.Opacity, [0.0, 0.1, 0.45, 1.0], [0.0, 1.0, 0.0, 0.0]));

    // rise: Success lifts the cells together.
    public static readonly CssKeyframes Rise = Loop(
        LivingCoreEasings.Settle,
        Track(KeyframeChannel.Y, [0.0, 0.25, 0.6, 1.0], [0.0, -3.5, -0.5, 0.0]));

    // wseg: Warning's amber segment pulses.
    public static readonly CssKeyframes WarningMark = Loop(LivingCoreEasings.Breath, Track(KeyframeChannel.Opacity, [0.0, 0.5, 1.0], [0.35, 1.0, 0.35]));

    // tight: Warning's inner halo tightens and holds.
    public static readonly CssKeyframes Tighten = Loop(
        LivingCoreEasings.Breath,
        Track(KeyframeChannel.Scale, [0.0, 0.5, 1.0], [0.97, 0.98, 0.97]),
        Track(KeyframeChannel.Opacity, [0.0, 0.5, 1.0], [0.8, 1.0, 0.8]));

    // park: Blocked's parked threads pulse slowly.
    public static readonly CssKeyframes Park = Loop(LivingCoreEasings.Breath, Track(KeyframeChannel.Opacity, [0.0, 0.5, 1.0], [0.45, 1.0, 0.45]));

    // wait: Blocked glances at the request every 6.4 s, then waits.
    public static readonly CssKeyframes RequestGlance = Loop(
        LivingCoreEasings.Settle,
        Track(KeyframeChannel.X, [0.0, 0.12, 0.22, 0.40, 0.58, 1.0], [0.0, 0.0, 9.0, 9.0, 0.0, 0.0]),
        Track(KeyframeChannel.Y, [0.0, 0.12, 0.22, 0.40, 0.58, 1.0], [0.0, 0.0, -2.0, -2.0, 0.0, 0.0]));

    // stut: Error's inner halo breathes off the grid.
    public static readonly CssKeyframes Stutter = Loop(
        LivingCoreEasings.Linear,
        Track(KeyframeChannel.Scale, [0.0, 0.17, 0.23, 0.49, 0.58, 0.83, 1.0], [1.0, 1.01, 1.004, 1.012, 1.0, 1.006, 1.0]),
        Track(KeyframeChannel.Opacity, [0.0, 0.17, 0.23, 0.49, 0.58, 0.83, 1.0], [0.55, 0.3, 0.5, 0.25, 0.5, 0.32, 0.55]));

    // sleep: the sunken body barely breathes.
    public static readonly CssKeyframes SleepBreath = Loop(
        LivingCoreEasings.Sink,
        Track(KeyframeChannel.Scale, [0.0, 0.5, 1.0], [0.86, 0.875, 0.86]),
        Track(KeyframeChannel.Y, [0.0, 1.0], [9.0, 9.0]));

    // wkp: Wake, a point of light swells past full and settles.
    public static readonly CssKeyframes WakePupil = Ignition(
        LivingCoreEasings.Strike,
        Track(KeyframeChannel.Scale, [0.0, 0.12, 0.25, 1.0], [0.0, 1.5, 1.0, 1.0]),
        Track(KeyframeChannel.Opacity, [0.0, 0.12, 1.0], [0.0, 1.0, 1.0]));

    // wkr: Wake, the rim draws itself round (0 hidden, 1 drawn).
    public static readonly CssKeyframes WakeRim = Ignition(
        LivingCoreEasings.Settle,
        Track(KeyframeChannel.Dash, [0.0, 0.16, 0.5, 1.0], [0.0, 0.0, 1.0, 1.0]));

    // wkc: Wake, the lens sweeps in.
    public static readonly CssKeyframes WakeLens = Ignition(
        LivingCoreEasings.Settle,
        Track(KeyframeChannel.ScaleX, [0.0, 0.3, 0.6, 1.0], [0.6, 0.6, 1.0, 1.0]),
        Track(KeyframeChannel.Opacity, [0.0, 0.3, 0.6, 1.0], [0.0, 0.0, 1.0, 1.0]));

    // wkb: Wake, the cells and stars bloom outward.
    public static readonly CssKeyframes WakeBloom = Ignition(
        LivingCoreEasings.Settle,
        Track(KeyframeChannel.Scale, [0.0, 0.48, 0.8, 1.0], [0.3, 0.3, 1.0, 1.0]),
        Track(KeyframeChannel.Opacity, [0.0, 0.48, 0.8, 1.0], [0.0, 0.0, 1.0, 1.0]));

    // wkh: Wake, the halo and threads arrive last.
    public static readonly CssKeyframes WakeHalo = Ignition(
        LivingCoreEasings.Settle,
        Track(KeyframeChannel.Scale, [0.0, 0.68, 1.0], [0.92, 0.92, 1.0]),
        Track(KeyframeChannel.Opacity, [0.0, 0.68, 1.0], [0.0, 0.0, 1.0]));

    // wka: Wake, the aura rises.
    public static readonly CssKeyframes WakeAura = Ignition(
        LivingCoreEasings.Breath,
        Track(KeyframeChannel.Opacity, [0.0, 0.6, 1.0], [0.0, 0.0, 1.0]));

    // ripA / ripB: a new message's ripple (fill none: gone when done).
    public static readonly CssKeyframes Ripple = new(
        LivingCoreEasings.Strike,
        once: true,
        fillBoth: false,
        Track(KeyframeChannel.Scale, [0.0, 1.0], [1.0, 1.35]),
        Track(KeyframeChannel.Opacity, [0.0, 1.0], [0.75, 0.0]));

    private static CssKeyframes Loop(CubicBezierEasing easing, params KeyframeTrack[] tracks) =>
        new(easing, once: false, fillBoth: false, tracks);

    private static CssKeyframes Ignition(CubicBezierEasing easing, params KeyframeTrack[] tracks) =>
        new(easing, once: true, fillBoth: true, tracks);

    private static KeyframeTrack Track(KeyframeChannel channel, double[] offsets, double[] values) =>
        new(channel, offsets, values);
}
