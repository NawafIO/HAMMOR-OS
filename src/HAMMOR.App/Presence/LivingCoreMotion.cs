namespace HAMMOR.App.Presence;

/// <summary>
/// The Living Core's motion engine: turns a state, the app's reactions, the
/// pointer and the passing of time into a <see cref="LivingCoreFrame"/>. Pure
/// arithmetic, no WPF, deterministic: the same inputs at the same times always
/// produce the same frames.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reference.</b> The approved prototype, and the video recorded from
/// it, are CSS: every layer is an element with a resting value per state, a
/// CSS transition, and animations. This engine reproduces those rules exactly,
/// element by element, as the browser applies them:
/// </para>
/// <list type="bullet">
/// <item>Each state applies its set of animations (<see cref="PrototypeKeyframes"/>).
/// One that stays applied across a state change keeps its clock; a new
/// duration moves its phase at once (so the threads jump when Thinking speeds
/// them up, as in the video).</item>
/// <item>A resting value that changes glides with its element's transition
/// (the white core 0.45 s, its size and light 0.6 s, the layers' shape 1.2 s
/// and light 0.8 s, the state marks 0.6 s), from the value on screen.</item>
/// <item>A value an animation drives jumps when that animation starts or
/// stops: the browser never transitions from an animated value. So the body
/// drops into Sleep and back at once, and the white core snaps to 125% when
/// Speaking starts.</item>
/// <item>Values without a transition in the prototype (the rim's tone, the
/// crest's lift, the energy lines' light) switch at once.</item>
/// </list>
/// <para>
/// <b>The white core</b> has one owner: the prototype's pose order. Sleep and
/// Wake rest; Speaking, Listening and Thinking hold their poses; otherwise a
/// new message or an opening panel draws a glance, then Blocked, Warning,
/// Error and Success hold theirs, then in Idle the pointer, then rest.
/// </para>
/// <para>
/// <b>Reduced motion</b> turns every animation off, as the prototype's
/// reduced-motion switch does, and shortens every transition to 0.4 s: each
/// state is a still frame.
/// </para>
/// </remarks>
public sealed class LivingCoreMotion
{
    /// <summary>The Wake ignition (prototype keyframes <c>wk*</c>).</summary>
    public const double WakeSeconds = 2.4;

    /// <summary>How long Wake shows before the state underneath (prototype <c>wakeThen</c>).</summary>
    public const double WakeHoldSeconds = 2.6;

    /// <summary>One Success bloom: the warm light, the ring and the rise.</summary>
    public const double SuccessSeconds = 3.2;

    /// <summary>A new message's ripple.</summary>
    public const double RippleSeconds = 0.9;

    /// <summary>A new message draws the white core for this long.</summary>
    public const double MessageGlanceSeconds = 1.2;

    /// <summary>An opening panel draws the white core for this long.</summary>
    public const double PanelGlanceSeconds = 2.4;

    /// <summary>Every transition under reduced motion.</summary>
    public const double ReducedTransitionSeconds = 0.4;

    /// <summary>The white core reaches a new target (pointer, glance or pose) this long after it changes.</summary>
    public const double PointerLag = 0.45;

    /// <summary>It lets the pointer go after this much stillness.</summary>
    public const double PointerStillness = 5.0;

    /// <summary>And this long after the pointer leaves the window.</summary>
    public const double PointerLeaveRelease = 1.2;

    /// <summary>The share of its zone the pointer may turn it through.</summary>
    public const double PointerReach = 0.92;

    /// <summary>Depth: the cells move this share of the white core's offset.</summary>
    public const double CellsDepth = 0.3;

    /// <summary>Depth: the stars move this share, the other way.</summary>
    public const double StarsDepth = -0.1;

    /// <summary>A panel glance, as a share of the zone.</summary>
    public const double PanelGlance = 0.95;

    /// <summary>The aura leans this far toward a panel per unit of glance.</summary>
    public const double PanelLean = 4.0;

    private const double CoreMoveSeconds = PointerLag;
    private const double CoreSizeSeconds = 0.6;
    private const double DepthSeconds = 0.6;
    private const double LeanSeconds = 1.2;
    private const double ShapeSeconds = 1.2;
    private const double LightSeconds = 0.8;
    private const double MarkSeconds = 0.6;
    private const double MessageGlanceX = 0.5;
    private const double MessageGlanceY = 0.9;
    private const double MaxStep = 0.1;
    private const double EnvelopeTimeConstant = 0.12;

    // Listening and Speaking rings: their delays, and their resting sizes
    // (what shows before a ring's delay ends, and under reduced motion).
    private static readonly double[] RingDelays = [0.0, 0.6, 1.2];
    private static readonly double[] InwardRest = [1.22, 1.12, 1.03];
    private static readonly double[] OutwardRest = [1.06, 1.18, 1.3];

    private static readonly AnimationSpec[][] Specs = BuildSpecs();

    private readonly CssAnimation[] _animations = new CssAnimation[(int)Slot.Count];

    // The animations the last frame showed: a value jumps when the animation
    // on it changes between two frames, as the browser decides per style change.
    private readonly CssKeyframes?[] _before = new CssKeyframes?[(int)Slot.Count];
    private readonly CssTransition[] _values = new CssTransition[(int)Value.Count];

    private LivingCoreState _shown;
    private double _wakeUntil = double.NegativeInfinity;
    private double _wakeStart = double.NegativeInfinity;
    private (double X, double Y, double Scale, double Opacity) _pose;
    private CssAnimation _ripple;

    private double _messageUntil = double.NegativeInfinity;
    private double _messageSide = 1.0;
    private double _panelUntil = double.NegativeInfinity;
    private double _panel;

    private bool _hasPointer;
    private double _pointerX;
    private double _pointerY;
    private double _pointerMovedAt = double.NegativeInfinity;
    private double _pointerReleaseAt = double.PositiveInfinity;

    private double _lastTime;
    private bool _hasTime;
    private double _envelope;
    private bool _hasVoiceLevel;

    /// <param name="initialState">State to show.</param>
    /// <param name="now">Current time, seconds, from a monotonic clock.</param>
    /// <param name="fromDormant">
    /// Arrive through the Wake ignition, the way the app opens: 2.6 s of Wake,
    /// then <paramref name="initialState"/> (or whatever state was set meanwhile).
    /// </param>
    /// <param name="reducedMotion">Start in reduced motion (no ignition).</param>
    public LivingCoreMotion(LivingCoreState initialState, double now, bool fromDormant, bool reducedMotion = false)
    {
        State = initialState;
        ReducedMotion = reducedMotion;
        if (fromDormant && !reducedMotion)
        {
            _wakeUntil = now + WakeHoldSeconds;
        }

        // The loops run from the moment the core appears, like a page's.
        _shown = ShownState(now);
        var specs = Specs[(int)_shown];
        for (var i = 0; i < _animations.Length; i++)
        {
            var spec = specs[i];
            if (spec.Keyframes is not null)
            {
                _animations[i] = new CssAnimation { Keyframes = spec.Keyframes, Start = now, Duration = spec.Duration, Delay = spec.Delay };
            }

            _before[i] = _animations[i].Keyframes;
        }

        if (_shown == LivingCoreState.Wake)
        {
            _wakeStart = now;
        }

        var look = LivingCoreLooks.For(_shown);
        for (var v = Value.Ember; v < Value.PoseX; v++)
        {
            _values[(int)v] = new CssTransition(Base(look, v, _shown));
        }

        _pose = PoseFor(_shown, now);
        _values[(int)Value.PoseX] = new CssTransition(_pose.X);
        _values[(int)Value.PoseY] = new CssTransition(_pose.Y);
        _values[(int)Value.CoreScale] = new CssTransition(_pose.Scale);
        _values[(int)Value.CoreOpacity] = new CssTransition(_pose.Opacity);
        _values[(int)Value.StarsShiftX] = new CssTransition(StarsDepth * (_pose.X - LivingCoreLooks.ZoneX));
        _values[(int)Value.StarsShiftY] = new CssTransition(StarsDepth * (_pose.Y - LivingCoreLooks.ZoneY));
        _values[(int)Value.CellsShiftX] = new CssTransition(CellsDepth * (_pose.X - LivingCoreLooks.ZoneX));
        _values[(int)Value.CellsShiftY] = new CssTransition(CellsDepth * (_pose.Y - LivingCoreLooks.ZoneY));
        _values[(int)Value.Lean] = new CssTransition(0.0);
    }

    /// <summary>Every layer the prototype animates, one animation each.</summary>
    private enum Slot
    {
        AuraBreath,
        Energy1Turn,
        Energy1Flow,
        Energy2Turn,
        Energy2Flow,
        Halo0,
        Halo1,
        Halo2,
        Thread0,
        Thread1,
        Thread2,
        Thread3,
        Stars,
        Twinkle0,
        Twinkle1,
        Twinkle2,
        Twinkle3,
        Cells,
        Bob0,
        Bob1,
        Bob2,
        Lateral,
        Crest,
        Wander,
        Pupil,
        Aura,
        Threads,
        HaloGroup,
        Lens,
        CellGroup,
        Body,
        Rim,
        Warm,
        SuccessRing,
        WarningMark,
        Park,
        Attention,
        Inward0,
        Inward1,
        Inward2,
        Outward0,
        Outward1,
        Outward2,
        VoiceWave,
        Fragment1,
        Fragment2,
        Orbit1,
        Orbit2,
        Signal0,
        Signal1,
        Signal2,
        Signal3,
        Signal4,
        Signal5,
        Count,
    }

    /// <summary>Every resting value with a transition (or an instant switch).</summary>
    private enum Value
    {
        // State values, from LivingCoreLooks.
        Ember,
        Glow,
        BodyOpacity,
        BodyScale,
        BodyDrop,
        LensScaleY,
        LensOpacity,
        CrestLift,
        ThreadsOpacity,
        AuraOpacity,
        StarsOpacity,
        CellsScaleX,
        CellsScaleY,
        CellsDrop,
        CellsOpacity,
        EnergyOpacity,
        LateralA,
        LateralB,
        InwardRings,
        OutwardRings,
        GapRing,
        Fragments,
        Crack,
        Links,
        Orbits,
        VoiceWave,
        SpeakRing,
        AttentionRing,
        Warm,
        SuccessRing,
        WarningMark,
        Park,
        Inward0Scale,
        Inward1Scale,
        Inward2Scale,
        Outward0Scale,
        Outward1Scale,
        Outward2Scale,
        RimDanger,
        RimSuccess,
        RimWarning,

        // The white core's pose, its depth and the aura's lean.
        PoseX,
        PoseY,
        CoreScale,
        CoreOpacity,
        StarsShiftX,
        StarsShiftY,
        CellsShiftX,
        CellsShiftY,
        Lean,
        Count,
    }

    /// <summary>The state the app asked for. Startup's Wake may still be showing.</summary>
    public LivingCoreState State { get; private set; }

    /// <summary>Honour the system's reduced-motion setting: no animations, short transitions.</summary>
    public bool ReducedMotion { get; set; }

    /// <summary>The state on screen at <paramref name="now"/>: Wake while the startup ignition holds.</summary>
    public LivingCoreState ShownState(double now) => now < _wakeUntil ? LivingCoreState.Wake : State;

    /// <summary>True while any value is still gliding to a new state.</summary>
    public bool IsTransitioning(double now)
    {
        for (var i = 0; i < _values.Length; i++)
        {
            if (_values[i].IsRunning(now))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True while the Wake ignition is playing.</summary>
    public bool IsWaking(double now) =>
        !ReducedMotion && ShownState(now) == LivingCoreState.Wake && now - _wakeStart < WakeSeconds;

    /// <summary>Settled in Sleep: only slow loops move, so the renderer can draw less often.</summary>
    public bool IsResting(double now) =>
        ShownState(now) == LivingCoreState.Sleep && !IsTransitioning(now) && !_ripple.IsPlaying(now);

    /// <summary>
    /// Whether the renderer has to keep producing frames: always with motion
    /// (every state has loops), and under reduced motion only while a
    /// transition plays.
    /// </summary>
    public bool NeedsContinuousFrames(double now) => !ReducedMotion || IsTransitioning(now);

    /// <summary>Shows <paramref name="state"/>, as the prototype's class change does.</summary>
    public void SetState(LivingCoreState state, double now)
    {
        State = state;
        Sync(now);
    }

    /// <summary>
    /// Reports the pointer. The white core follows it in Idle only, never under
    /// reduced motion.
    /// </summary>
    /// <param name="x">
    /// Pointer offset from the core's centre, physical left to right, in
    /// half-sides of the control: 1 is the control's edge. Farther away only
    /// the direction counts.
    /// </param>
    /// <param name="y">The same, top to bottom.</param>
    /// <param name="now">Current time, same clock as <see cref="Advance"/>.</param>
    public void SetPointer(double x, double y, double now)
    {
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y))
        {
            return;
        }

        var distance = Math.Sqrt((x * x) + (y * y));
        if (distance > 1.0)
        {
            x /= distance;
            y /= distance;
        }

        _pointerX = x * PointerReach;
        _pointerY = y * PointerReach;
        _hasPointer = true;
        _pointerMovedAt = now;
        _pointerReleaseAt = double.PositiveInfinity;
    }

    /// <summary>
    /// The pointer left HAMMOR's window: the white core lets go at
    /// <paramref name="at"/> unless the pointer comes back first.
    /// </summary>
    public void ReleasePointer(double at) => _pointerReleaseAt = Math.Min(_pointerReleaseAt, at);

    /// <summary>
    /// Something happened. A new message sends a ripple from the membrane and
    /// draws the white core toward it for 1.2 s; a panel draws it for 2.4 s
    /// and leans the aura its way. Speaking, Listening, Thinking, Sleep and
    /// Wake keep their pose (the aura still leans). Under reduced motion
    /// nothing moves.
    /// </summary>
    /// <param name="kind">What happened.</param>
    /// <param name="physicalDirection">Its side on screen: negative left, positive right.</param>
    /// <param name="now">Current time.</param>
    public void React(LivingCoreReactionKind kind, double physicalDirection, double now)
    {
        if (ReducedMotion)
        {
            return;
        }

        var side = physicalDirection < 0.0 ? -1.0 : 1.0;
        if (kind == LivingCoreReactionKind.NewMessage)
        {
            _messageUntil = now + MessageGlanceSeconds;
            _messageSide = side;
            _ripple = new CssAnimation { Keyframes = PrototypeKeyframes.Ripple, Start = now, Duration = RippleSeconds };
        }
        else
        {
            _panel = PanelGlance * side;
            _panelUntil = now + PanelGlanceSeconds;
        }
    }

    /// <summary>Advances to <paramref name="now"/> and writes the frame.</summary>
    /// <param name="now">Current time, seconds, same clock as the constructor.</param>
    /// <param name="voiceLevel">
    /// Level of the voice the state is about, 0 to 1, or NaN when no real
    /// envelope is connected. Without one, Speaking uses the prototype's own
    /// rhythm.
    /// </param>
    /// <param name="requestDirection">
    /// Physical side of the approval request: negative for left, positive for
    /// right. Blocked's glance goes that way.
    /// </param>
    /// <param name="frame">Frame to overwrite.</param>
    public void Advance(double now, double voiceLevel, double requestDirection, LivingCoreFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var dt = _hasTime ? Math.Clamp(now - _lastTime, 0.0, MaxStep) : 0.0;
        _lastTime = now;
        _hasTime = true;

        Sync(now);

        _hasVoiceLevel = !double.IsNaN(voiceLevel);
        _envelope = _hasVoiceLevel
            ? _envelope + ((Math.Clamp(voiceLevel, 0.0, 1.0) - _envelope) * (dt > 0.0 ? 1.0 - Math.Exp(-dt / EnvelopeTimeConstant) : 0.0))
            : 0.0;

        UpdatePose(now);
        WriteFrame(now, requestDirection, frame);

        for (var i = 0; i < _before.Length; i++)
        {
            _before[i] = _animations[i].Keyframes;
        }
    }

    private static AnimationSpec[][] BuildSpecs()
    {
        var all = new AnimationSpec[10][];
        for (var s = 0; s < all.Length; s++)
        {
            var state = (LivingCoreState)s;
            var specs = new AnimationSpec[(int)Slot.Count];
            for (var i = 0; i < specs.Length; i++)
            {
                specs[i] = SpecFor(state, (Slot)i);
            }

            all[s] = specs;
        }

        return all;
    }

    /// <summary>The prototype's animation on <paramref name="slot"/> in <paramref name="state"/>.</summary>
    private static AnimationSpec SpecFor(LivingCoreState state, Slot slot)
    {
        var wake = state == LivingCoreState.Wake;
        var listening = state == LivingCoreState.Listening;
        var thinking = state == LivingCoreState.Thinking;
        var speaking = state == LivingCoreState.Speaking;
        var warning = state == LivingCoreState.Warning;
        var blocked = state == LivingCoreState.Blocked;
        var stillCells = thinking || warning || blocked;

        return slot switch
        {
            Slot.AuraBreath => new(PrototypeKeyframes.AuraBreath, 8.0),
            Slot.Energy1Turn => new(PrototypeKeyframes.Turn, 90.0),
            Slot.Energy1Flow => new(PrototypeKeyframes.EnergyFlow, 6.0),
            Slot.Energy2Turn => new(PrototypeKeyframes.TurnBack, 140.0),
            Slot.Energy2Flow => new(PrototypeKeyframes.EnergyFlow, 9.0),
            Slot.Halo0 => state switch
            {
                LivingCoreState.Listening => new(PrototypeKeyframes.Voice, 1.6),
                LivingCoreState.Warning => new(PrototypeKeyframes.Tighten, 3.2),
                LivingCoreState.Error => new(PrototypeKeyframes.Stutter, 2.4),
                _ => new(PrototypeKeyframes.HaloBreath, LivingCoreDesign.HaloPeriod, LivingCoreDesign.HaloDelays[0]),
            },
            Slot.Halo1 => new(PrototypeKeyframes.HaloBreath, LivingCoreDesign.HaloPeriod, LivingCoreDesign.HaloDelays[1]),
            Slot.Halo2 => new(PrototypeKeyframes.HaloBreath, LivingCoreDesign.HaloPeriod, LivingCoreDesign.HaloDelays[2]),
            Slot.Thread0 or Slot.Thread1 or Slot.Thread2 or Slot.Thread3 => ThreadSpec(state, slot - Slot.Thread0),
            Slot.Stars => wake
                ? new(PrototypeKeyframes.WakeBloom, WakeSeconds)
                : new(PrototypeKeyframes.Turn, thinking ? 60.0 : 240.0),
            Slot.Twinkle0 or Slot.Twinkle1 or Slot.Twinkle2 or Slot.Twinkle3 => new(
                PrototypeKeyframes.Twinkle,
                listening ? 1.6 : LivingCoreDesign.ShimmerClocks[slot - Slot.Twinkle0].Period,
                LivingCoreDesign.ShimmerClocks[slot - Slot.Twinkle0].Delay),
            Slot.Cells => new(PrototypeKeyframes.Turn, warning ? 280.0 : blocked ? 600.0 : 140.0),
            Slot.Bob0 or Slot.Bob1 or Slot.Bob2 => stillCells
                ? default
                : new(PrototypeKeyframes.Bob, LivingCoreDesign.BobClocks[slot - Slot.Bob0].Period, LivingCoreDesign.BobClocks[slot - Slot.Bob0].Delay),
            Slot.Lateral => new(PrototypeKeyframes.LateralFlow, listening ? 1.1 : 3.2),
            Slot.Crest => warning ? default : new(PrototypeKeyframes.CrestSway, 6.4),
            Slot.Wander => state switch
            {
                LivingCoreState.Idle => new(PrototypeKeyframes.Drift, 9.6),
                LivingCoreState.Blocked => new(PrototypeKeyframes.RequestGlance, 6.4),
                _ => default,
            },
            Slot.Pupil => speaking ? new(PrototypeKeyframes.SpeakPulse, 1.6) : wake ? new(PrototypeKeyframes.WakePupil, WakeSeconds) : default,
            Slot.Aura => speaking ? new(PrototypeKeyframes.AuraPulse, 1.6) : wake ? new(PrototypeKeyframes.WakeAura, WakeSeconds) : default,
            Slot.Threads => speaking ? new(PrototypeKeyframes.ThreadPulse, 1.6) : wake ? new(PrototypeKeyframes.WakeHalo, WakeSeconds) : default,
            Slot.HaloGroup => wake ? new(PrototypeKeyframes.WakeHalo, WakeSeconds) : default,
            Slot.Lens => thinking ? new(PrototypeKeyframes.Precession, 4.8) : wake ? new(PrototypeKeyframes.WakeLens, WakeSeconds) : default,
            Slot.CellGroup => state == LivingCoreState.Success
                ? new(PrototypeKeyframes.Rise, SuccessSeconds)
                : wake ? new(PrototypeKeyframes.WakeBloom, WakeSeconds) : default,
            Slot.Body => state == LivingCoreState.Sleep ? new(PrototypeKeyframes.SleepBreath, 9.6) : default,
            Slot.Rim => wake ? new(PrototypeKeyframes.WakeRim, WakeSeconds) : default,
            Slot.Warm => state == LivingCoreState.Success ? new(PrototypeKeyframes.WarmBloom, SuccessSeconds) : default,
            Slot.SuccessRing => state == LivingCoreState.Success ? new(PrototypeKeyframes.SuccessRing, SuccessSeconds) : default,
            Slot.WarningMark => warning ? new(PrototypeKeyframes.WarningMark, 3.2) : default,
            Slot.Park => blocked ? new(PrototypeKeyframes.Park, 3.2) : default,
            Slot.Attention => listening ? new(PrototypeKeyframes.Attention, 3.2) : default,
            Slot.Inward0 or Slot.Inward1 or Slot.Inward2 => listening
                ? new(PrototypeKeyframes.InwardRing, 1.8, RingDelays[slot - Slot.Inward0])
                : default,
            Slot.Outward0 or Slot.Outward1 or Slot.Outward2 => speaking
                ? new(PrototypeKeyframes.OutwardRing, 1.8, RingDelays[slot - Slot.Outward0])
                : default,
            Slot.VoiceWave => speaking ? new(PrototypeKeyframes.VoiceWave, 1.6) : default,
            Slot.Fragment1 => state == LivingCoreState.Error ? new(PrototypeKeyframes.Turn, 36.0) : default,
            Slot.Fragment2 => state == LivingCoreState.Error ? new(PrototypeKeyframes.TurnBack, 52.0) : default,
            Slot.Orbit1 => thinking ? new(PrototypeKeyframes.Turn, 9.6) : default,
            Slot.Orbit2 => thinking ? new(PrototypeKeyframes.TurnBack, 14.4) : default,
            Slot.Signal0 or Slot.Signal1 or Slot.Signal2 or Slot.Signal3 or Slot.Signal4 or Slot.Signal5 => thinking
                ? new(PrototypeKeyframes.Signal, LivingCoreDesign.SignalPeriod, LivingCoreDesign.Links[slot - Slot.Signal0].Delay)
                : default,
            _ => default,
        };
    }

    /// <summary>
    /// A thread's orbit. Listening and Thinking speed the inner two up (8 and
    /// 12 s), Thinking the third (16 s) and HAMMOR's fourth with it; Warning
    /// halves them all.
    /// </summary>
    private static AnimationSpec ThreadSpec(LivingCoreState state, int index)
    {
        var thread = LivingCoreDesign.Threads[index];
        var keyframes = thread.Clockwise ? PrototypeKeyframes.Turn : PrototypeKeyframes.TurnBack;
        var period = thread.Period;
        if (state is LivingCoreState.Listening or LivingCoreState.Thinking && index < 2)
        {
            period = index == 0 ? 8.0 : 12.0;
        }
        else if (state == LivingCoreState.Thinking)
        {
            period = period * 16.0 / 30.0;
        }
        else if (state == LivingCoreState.Warning)
        {
            period *= 2.0;
        }

        return new(keyframes, period, thread.Delay);
    }

    /// <summary>A state's resting value for <paramref name="value"/>.</summary>
    private static double Base(in LivingCoreLook look, Value value, LivingCoreState state) => value switch
    {
        Value.Ember => look.Ember,
        Value.Glow => look.Glow,
        Value.BodyOpacity => look.BodyOpacity,
        Value.BodyScale => look.BodyScale,
        Value.BodyDrop => look.BodyDrop,
        Value.LensScaleY => look.LensScaleY,
        Value.LensOpacity => look.LensOpacity,
        Value.CrestLift => look.CrestLift,
        Value.ThreadsOpacity => look.ThreadsOpacity,
        Value.AuraOpacity => look.AuraOpacity,
        Value.StarsOpacity => look.StarsOpacity,
        Value.CellsScaleX => look.CellsScaleX,
        Value.CellsScaleY => look.CellsScaleY,
        Value.CellsDrop => look.CellsDrop,
        Value.CellsOpacity => look.CellsOpacity,
        Value.EnergyOpacity => look.EnergyOpacity,
        Value.LateralA => look.LateralAOpacity,
        Value.LateralB => look.LateralBOpacity,
        Value.InwardRings => look.InwardRings,
        Value.OutwardRings => look.OutwardRings,
        Value.GapRing => look.GapRing,
        Value.Fragments => look.Fragments,
        Value.Crack => look.Crack,
        Value.Links => look.Links,
        Value.Orbits => look.Orbits,
        Value.VoiceWave => look.VoiceWave,
        Value.SpeakRing => look.SpeakRing,
        Value.AttentionRing => look.AttentionRing,
        Value.Warm => look.Warm,
        Value.SuccessRing => look.SuccessRing,
        Value.WarningMark => look.WarningMark,
        Value.Park => look.Park,
        Value.Inward0Scale or Value.Inward1Scale or Value.Inward2Scale =>
            state == LivingCoreState.Listening ? InwardRest[value - Value.Inward0Scale] : 1.0,
        Value.Outward0Scale or Value.Outward1Scale or Value.Outward2Scale =>
            state == LivingCoreState.Speaking ? OutwardRest[value - Value.Outward0Scale] : 1.0,
        Value.RimDanger => look.RimDanger,
        Value.RimSuccess => look.RimSuccess,
        Value.RimWarning => look.RimWarning,
        _ => 0.0,
    };

    /// <summary>The prototype's transition on <paramref name="value"/>; zero duration switches at once.</summary>
    private static (double Duration, CubicBezierEasing Easing) TransitionOf(Value value) => value switch
    {
        Value.Ember or Value.BodyOpacity or Value.LensOpacity or Value.ThreadsOpacity or Value.AuraOpacity
            or Value.StarsOpacity or Value.CellsOpacity => (LightSeconds, LivingCoreEasings.Ease),
        Value.BodyScale or Value.BodyDrop or Value.LensScaleY or Value.CellsScaleX or Value.CellsScaleY
            or Value.CellsDrop => (ShapeSeconds, LivingCoreEasings.Settle),
        Value.InwardRings or Value.OutwardRings or Value.GapRing or Value.Fragments or Value.Crack or Value.Links
            or Value.Orbits or Value.VoiceWave or Value.SpeakRing or Value.AttentionRing or Value.Warm
            or Value.SuccessRing or Value.WarningMark or Value.Park => (MarkSeconds, LivingCoreEasings.Ease),
        _ => (0.0, LivingCoreEasings.Linear),
    };

    /// <summary>
    /// The animated layer whose animation can drive <paramref name="value"/>,
    /// and whether through its transform (otherwise its opacity).
    /// </summary>
    private static (Slot Slot, bool Transform) OwnerOf(Value value) => value switch
    {
        Value.CoreScale => (Slot.Pupil, true),
        Value.BodyScale or Value.BodyDrop => (Slot.Body, true),
        Value.LensScaleY => (Slot.Lens, true),
        Value.LensOpacity => (Slot.Lens, false),
        Value.ThreadsOpacity => (Slot.Threads, false),
        Value.AuraOpacity => (Slot.Aura, false),
        Value.StarsOpacity => (Slot.Stars, false),
        Value.CellsScaleX or Value.CellsScaleY or Value.CellsDrop => (Slot.CellGroup, true),
        Value.CellsOpacity => (Slot.CellGroup, false),
        Value.AttentionRing => (Slot.Attention, false),
        Value.Warm => (Slot.Warm, false),
        Value.SuccessRing => (Slot.SuccessRing, false),
        Value.WarningMark => (Slot.WarningMark, false),
        Value.Park => (Slot.Park, false),
        Value.Inward0Scale or Value.Inward1Scale or Value.Inward2Scale => (Slot.Inward0 + (value - Value.Inward0Scale), true),
        Value.Outward0Scale or Value.Outward1Scale or Value.Outward2Scale => (Slot.Outward0 + (value - Value.Outward0Scale), true),
        _ => (Slot.Count, false),
    };

    private static bool Drives(CssKeyframes? keyframes, bool transform) =>
        keyframes is not null && (transform ? keyframes.AnimatesTransform : keyframes.AnimatesOpacity);

    /// <summary>Applies the state on screen if it changed, as a class change does.</summary>
    private void Sync(double now)
    {
        var shown = ShownState(now);
        if (shown == _shown)
        {
            return;
        }

        // Start, keep or stop each animation.
        var specs = Specs[(int)shown];
        for (var i = 0; i < _animations.Length; i++)
        {
            var spec = specs[i];
            if (spec.Keyframes is null)
            {
                _animations[i] = default;
            }
            else if (!ReferenceEquals(_animations[i].Keyframes, spec.Keyframes))
            {
                _animations[i] = new CssAnimation { Keyframes = spec.Keyframes, Start = now, Duration = spec.Duration, Delay = spec.Delay };
            }
            else
            {
                _animations[i].Duration = spec.Duration;
                _animations[i].Delay = spec.Delay;
            }
        }

        if (shown == LivingCoreState.Wake)
        {
            _wakeStart = now;
        }

        // Move every resting value: at once where an animation drives it now
        // or did before, otherwise with its transition.
        var look = LivingCoreLooks.For(shown);
        for (var v = Value.Ember; v < Value.PoseX; v++)
        {
            var target = Base(look, v, shown);
            if (Jumps(v))
            {
                _values[(int)v].Snap(target);
            }
            else
            {
                var (duration, easing) = Transition(TransitionOf(v));
                _values[(int)v].Go(target, now, duration, easing);
            }
        }

        _shown = shown;
    }

    /// <summary>
    /// Whether <paramref name="value"/> jumps instead of transitioning: an
    /// animation on its property started, runs or stopped since the last
    /// frame (the browser never transitions from an animated value).
    /// </summary>
    private bool Jumps(Value value)
    {
        if (ReducedMotion)
        {
            return false;
        }

        var (slot, transform) = OwnerOf(value);
        return slot != Slot.Count
            && (Drives(_before[(int)slot], transform) || Drives(_animations[(int)slot].Keyframes, transform));
    }

    /// <summary>A transition as given, or the short one of reduced motion; instant stays instant.</summary>
    private (double Duration, CubicBezierEasing Easing) Transition((double Duration, CubicBezierEasing Easing) normal) =>
        normal.Duration <= 0.0
            ? normal
            : ReducedMotion ? (ReducedTransitionSeconds, LivingCoreEasings.Settle) : normal;

    /// <summary>
    /// The prototype's pose order: where the white core looks, its size and
    /// light, for the state on screen and what is happening now.
    /// </summary>
    private (double X, double Y, double Scale, double Opacity) PoseFor(LivingCoreState shown, double now)
    {
        var look = LivingCoreLooks.For(shown);
        double nx;
        double ny;
        if (IsPoseHeld(shown))
        {
            return (look.X, look.Y, look.Scale, look.Opacity);
        }

        if (now < _messageUntil && !ReducedMotion)
        {
            nx = MessageGlanceX * _messageSide;
            ny = MessageGlanceY;
        }
        else if (now < _panelUntil && !ReducedMotion)
        {
            nx = _panel;
            ny = 0.0;
        }
        else if (shown != LivingCoreState.Idle)
        {
            return (look.X, look.Y, look.Scale, look.Opacity);
        }
        else if (FollowsPointer(now))
        {
            nx = _pointerX;
            ny = _pointerY;
        }
        else
        {
            return (look.X, look.Y, look.Scale, look.Opacity);
        }

        var distance = Math.Sqrt((nx * nx) + (ny * ny));
        if (distance > 1.0)
        {
            nx /= distance;
            ny /= distance;
        }

        // A glance or the pointer looks at full size and light, as in the prototype.
        return (
            LivingCoreLooks.ZoneX + (nx * LivingCoreLooks.ZoneRadiusX),
            LivingCoreLooks.ZoneY + (ny * LivingCoreLooks.ZoneRadiusY),
            1.0,
            1.0);
    }

    private bool FollowsPointer(double now) =>
        _hasPointer && !ReducedMotion && now < _pointerReleaseAt && now - _pointerMovedAt < PointerStillness;

    /// <summary>
    /// Retargets the white core, its depth and the aura's lean when the pose
    /// changes: every new target starts from the value on screen.
    /// </summary>
    private void UpdatePose(double now)
    {
        var pupilChanged = !ReferenceEquals(_before[(int)Slot.Pupil], _animations[(int)Slot.Pupil].Keyframes);
        var pose = PoseFor(_shown, now);
        if (pose != _pose)
        {
            Move(Value.PoseX, pose.X, now, CoreMoveSeconds, LivingCoreEasings.Settle);
            Move(Value.PoseY, pose.Y, now, CoreMoveSeconds, LivingCoreEasings.Settle);
            if (Jumps(Value.CoreScale))
            {
                _values[(int)Value.CoreScale].Snap(pose.Scale);
            }
            else
            {
                Move(Value.CoreScale, pose.Scale, now, CoreSizeSeconds, LivingCoreEasings.Settle);
            }

            Move(Value.CoreOpacity, pose.Opacity, now, CoreSizeSeconds, LivingCoreEasings.Ease);
            Move(Value.StarsShiftX, StarsDepth * (pose.X - LivingCoreLooks.ZoneX), now, DepthSeconds, LivingCoreEasings.Settle);
            Move(Value.StarsShiftY, StarsDepth * (pose.Y - LivingCoreLooks.ZoneY), now, DepthSeconds, LivingCoreEasings.Settle);
            Move(Value.CellsShiftX, CellsDepth * (pose.X - LivingCoreLooks.ZoneX), now, DepthSeconds, LivingCoreEasings.Settle);
            Move(Value.CellsShiftY, CellsDepth * (pose.Y - LivingCoreLooks.ZoneY), now, DepthSeconds, LivingCoreEasings.Settle);
            _pose = pose;
        }
        else if (pupilChanged && Jumps(Value.CoreScale))
        {
            _values[(int)Value.CoreScale].Snap(pose.Scale);
        }

        // The aura leans toward an opening panel in every state, as in the
        // prototype, even while the white core holds its pose.
        var lean = now < _panelUntil && !ReducedMotion ? PanelLean * _panel : 0.0;
        Move(Value.Lean, lean, now, LeanSeconds, LivingCoreEasings.Settle);
    }

    private static bool IsPoseHeld(LivingCoreState state) =>
        state is LivingCoreState.Sleep or LivingCoreState.Wake or LivingCoreState.Speaking
            or LivingCoreState.Listening or LivingCoreState.Thinking;

    private void Move(Value value, double target, double now, double duration, in CubicBezierEasing easing)
    {
        var (d, e) = Transition((duration, easing));
        _values[(int)value].Go(target, now, d, e);
    }

    private double V(Value value, double now) => _values[(int)value].At(now);

    private double A(Slot slot, KeyframeChannel channel, double now, double otherwise) =>
        ReducedMotion ? otherwise : _animations[(int)slot].Get(channel, now, otherwise);

    private bool TryA(Slot slot, KeyframeChannel channel, double now, out double value)
    {
        value = 0.0;
        return !ReducedMotion && _animations[(int)slot].TryGet(channel, now, out value);
    }

    private void WriteFrame(double now, double requestDirection, LivingCoreFrame frame)
    {
        var shown = _shown;

        // ---- White core: pose, size, light; the idle drift or the request glance on top ----
        var wanderX = A(Slot.Wander, KeyframeChannel.X, now, 0.0);
        if (ReferenceEquals(_animations[(int)Slot.Wander].Keyframes, PrototypeKeyframes.RequestGlance) && requestDirection < 0.0)
        {
            wanderX = -wanderX;
        }

        frame.CoreX = V(Value.PoseX, now) + wanderX;
        frame.CoreY = V(Value.PoseY, now) + A(Slot.Wander, KeyframeChannel.Y, now, 0.0);

        var coreScale = A(Slot.Pupil, KeyframeChannel.Scale, now, V(Value.CoreScale, now));
        if (_hasVoiceLevel && shown == LivingCoreState.Speaking && !ReducedMotion)
        {
            // A real speech envelope replaces the designed rhythm (125% to 147%).
            coreScale = LivingCoreLooks.Speaking.Scale * (1.0 + (0.176 * _envelope));
        }

        var poseLight = V(Value.CoreOpacity, now);
        frame.CoreScale = coreScale;
        frame.CoreOpacity = Clamp01(poseLight * A(Slot.Pupil, KeyframeChannel.Opacity, now, 1.0));
        frame.EmberOpacity = Clamp01(V(Value.Ember, now)) * frame.CoreOpacity;
        frame.GlowOpacity = Clamp01(V(Value.Glow, now)) * frame.CoreOpacity;
        frame.GlowScale = 1.0;
        frame.AttentionOpacity = Clamp01(A(Slot.Attention, KeyframeChannel.Opacity, now, V(Value.AttentionRing, now)) * poseLight);
        frame.SpeakRingOpacity = Clamp01(V(Value.SpeakRing, now)) * frame.CoreOpacity;

        // ---- Aura: breathing, the state's light, speech or ignition, the lean ----
        frame.AuraScale = A(Slot.AuraBreath, KeyframeChannel.Scale, now, 1.0) * A(Slot.Aura, KeyframeChannel.Scale, now, 1.0);
        frame.AuraOpacity = Clamp01(
            A(Slot.AuraBreath, KeyframeChannel.Opacity, now, 1.0)
            * A(Slot.Aura, KeyframeChannel.Opacity, now, V(Value.AuraOpacity, now)));
        frame.AuraLeanX = V(Value.Lean, now);
        frame.WarmScale = A(Slot.Warm, KeyframeChannel.Scale, now, 1.0);
        frame.WarmOpacity = Clamp01(A(Slot.Warm, KeyframeChannel.Opacity, now, V(Value.Warm, now)));

        // ---- Energy lines ----
        frame.EnergyOpacity = Clamp01(V(Value.EnergyOpacity, now));
        frame.Energy1Angle = A(Slot.Energy1Turn, KeyframeChannel.Angle, now, 0.0);
        frame.Energy2Angle = A(Slot.Energy2Turn, KeyframeChannel.Angle, now, 0.0);
        frame.Energy1Flow = A(Slot.Energy1Flow, KeyframeChannel.Dash, now, 0.0);
        frame.Energy2Flow = A(Slot.Energy2Flow, KeyframeChannel.Dash, now, 0.0);

        // ---- Rings outside the body ----
        // A real voice level, when one is connected, sets the inward rings' strength.
        var inwardGain = _hasVoiceLevel && shown == LivingCoreState.Listening && !ReducedMotion ? _envelope : 1.0;
        var inward = V(Value.InwardRings, now);
        var outward = V(Value.OutwardRings, now);
        for (var k = 0; k < 3; k++)
        {
            frame.InwardScale[k] = A(Slot.Inward0 + k, KeyframeChannel.Scale, now, V(Value.Inward0Scale + k, now));
            frame.InwardOpacity[k] = Clamp01(inward * A(Slot.Inward0 + k, KeyframeChannel.Opacity, now, 1.0) * inwardGain);
            frame.OutwardScale[k] = A(Slot.Outward0 + k, KeyframeChannel.Scale, now, V(Value.Outward0Scale + k, now));
            frame.OutwardOpacity[k] = Clamp01(outward * A(Slot.Outward0 + k, KeyframeChannel.Opacity, now, 1.0));
        }

        frame.SuccessRingScale = A(Slot.SuccessRing, KeyframeChannel.Scale, now, 1.0);
        frame.SuccessRingOpacity = Clamp01(A(Slot.SuccessRing, KeyframeChannel.Opacity, now, V(Value.SuccessRing, now)));
        frame.GapRingOpacity = Clamp01(V(Value.GapRing, now));
        frame.WarningMarkOpacity = Clamp01(A(Slot.WarningMark, KeyframeChannel.Opacity, now, V(Value.WarningMark, now)));

        // ---- The body: Sleep's breathing replaces its sunken resting size ----
        frame.BodyScale = A(Slot.Body, KeyframeChannel.Scale, now, V(Value.BodyScale, now));
        frame.BodyY = A(Slot.Body, KeyframeChannel.Y, now, V(Value.BodyDrop, now));
        frame.BodyOpacity = Clamp01(V(Value.BodyOpacity, now));

        // ---- Halo rings, inside the halo group the ignition grows ----
        var haloScale = A(Slot.HaloGroup, KeyframeChannel.Scale, now, 1.0);
        var haloLight = A(Slot.HaloGroup, KeyframeChannel.Opacity, now, 1.0);
        for (var i = 0; i < 3; i++)
        {
            frame.HaloScale[i] = haloScale * A(Slot.Halo0 + i, KeyframeChannel.Scale, now, 1.0);
            frame.HaloOpacity[i] = Clamp01(haloLight * A(Slot.Halo0 + i, KeyframeChannel.Opacity, now, 1.0));
        }

        frame.RimDanger = Clamp01(V(Value.RimDanger, now));
        frame.RimSuccess = Clamp01(V(Value.RimSuccess, now));
        frame.RimWarning = Clamp01(V(Value.RimWarning, now));
        frame.RimDrawn = A(Slot.Rim, KeyframeChannel.Dash, now, 1.0);

        // ---- A new message's ripple ----
        if (!ReducedMotion && _ripple.TryGet(KeyframeChannel.Scale, now, out var rippleScale))
        {
            frame.RippleScale = rippleScale;
            frame.RippleOpacity = _ripple.Get(KeyframeChannel.Opacity, now, 0.0);
        }
        else
        {
            frame.RippleScale = 1.0;
            frame.RippleOpacity = 0.0;
        }

        // ---- Threads and their state forms ----
        for (var i = 0; i < 4; i++)
        {
            frame.ThreadAngle[i] = A(Slot.Thread0 + i, KeyframeChannel.Angle, now, 0.0);
        }

        frame.ThreadsOpacity = Clamp01(A(Slot.Threads, KeyframeChannel.Opacity, now, V(Value.ThreadsOpacity, now)));
        frame.ThreadsScale = A(Slot.Threads, KeyframeChannel.Scale, now, 1.0);
        frame.ParkOpacity = Clamp01(A(Slot.Park, KeyframeChannel.Opacity, now, V(Value.Park, now)));
        frame.FragmentOpacity = Clamp01(V(Value.Fragments, now));
        frame.Fragment1Angle = A(Slot.Fragment1, KeyframeChannel.Angle, now, 0.0);
        frame.Fragment2Angle = A(Slot.Fragment2, KeyframeChannel.Angle, now, 0.0);
        frame.CrackOpacity = Clamp01(V(Value.Crack, now));

        // ---- Deep stars: turning field, shimmer clocks, depth ----
        var starsLight = A(Slot.Stars, KeyframeChannel.Opacity, now, V(Value.StarsOpacity, now));
        frame.StarsAngle = A(Slot.Stars, KeyframeChannel.Angle, now, 0.0);
        frame.StarsScale = A(Slot.Stars, KeyframeChannel.Scale, now, 1.0);
        for (var k = 0; k < 4; k++)
        {
            frame.StarClockOpacity[k] = Clamp01(starsLight * A(Slot.Twinkle0 + k, KeyframeChannel.Opacity, now, 1.0));
        }

        frame.StarsShiftX = V(Value.StarsShiftX, now);
        frame.StarsShiftY = V(Value.StarsShiftY, now);

        // ---- Floating cells: the school's shape (its animation replaces it while it runs) ----
        var cellsAnimated = TryA(Slot.CellGroup, KeyframeChannel.Scale, now, out var cellsScale);
        var cellsRising = TryA(Slot.CellGroup, KeyframeChannel.Y, now, out var cellsRise);
        if (cellsAnimated || cellsRising)
        {
            frame.CellsSpread = cellsAnimated ? cellsScale : 1.0;
            frame.CellsScaleY = cellsAnimated ? cellsScale : 1.0;
            frame.CellsY = cellsRising ? cellsRise : 0.0;
        }
        else
        {
            frame.CellsSpread = V(Value.CellsScaleX, now);
            frame.CellsScaleY = V(Value.CellsScaleY, now);
            frame.CellsY = V(Value.CellsDrop, now);
        }

        frame.CellsOpacity = Clamp01(A(Slot.CellGroup, KeyframeChannel.Opacity, now, V(Value.CellsOpacity, now)));
        frame.CellsAngle = A(Slot.Cells, KeyframeChannel.Angle, now, 0.0);
        frame.CellsShiftX = V(Value.CellsShiftX, now);
        frame.CellsShiftY = V(Value.CellsShiftY, now);
        for (var j = 0; j < 3; j++)
        {
            frame.BobX[j] = A(Slot.Bob0 + j, KeyframeChannel.X, now, 0.0);
            frame.BobY[j] = A(Slot.Bob0 + j, KeyframeChannel.Y, now, 0.0);
        }

        // ---- Thinking: links with travelling signals, orbit lanes ----
        frame.LinkOpacity = Clamp01(V(Value.Links, now));
        var links = LivingCoreDesign.Links;
        for (var s = 0; s < links.Length; s++)
        {
            var link = links[s];
            var travel = -A(Slot.Signal0 + s, KeyframeChannel.Dash, now, 0.0);
            var f = Math.Min(travel, link.Length) / link.Length;
            frame.SignalX[s] = link.X1 + ((link.X2 - link.X1) * f);
            frame.SignalY[s] = link.Y1 + ((link.Y2 - link.Y1) * f);
            frame.SignalOpacity[s] = travel <= link.Length ? frame.LinkOpacity : 0.0;
        }

        frame.OrbitOpacity = Clamp01(V(Value.Orbits, now));
        frame.Orbit1Angle = A(Slot.Orbit1, KeyframeChannel.Angle, now, 0.0);
        frame.Orbit2Angle = A(Slot.Orbit2, KeyframeChannel.Angle, now, 0.0);

        // ---- Lens: an animation on its transform replaces Warning's narrowing ----
        var lensTurning = TryA(Slot.Lens, KeyframeChannel.Angle, now, out var lensAngle);
        var lensSweeping = TryA(Slot.Lens, KeyframeChannel.ScaleX, now, out var lensScaleX);
        frame.LensOpacity = Clamp01(A(Slot.Lens, KeyframeChannel.Opacity, now, V(Value.LensOpacity, now)));
        frame.LensAngle = lensTurning ? lensAngle : 0.0;
        frame.LensScaleX = lensSweeping ? lensScaleX : 1.0;
        frame.LensScaleY = lensTurning || lensSweeping ? 1.0 : V(Value.LensScaleY, now);
        frame.CrestAngle = A(Slot.Crest, KeyframeChannel.Angle, now, 0.0);
        frame.CrestY = V(Value.CrestLift, now);
        frame.LateralAOpacity = Clamp01(V(Value.LateralA, now));
        frame.LateralBOpacity = Clamp01(V(Value.LateralB, now));
        frame.LateralFlow = A(Slot.Lateral, KeyframeChannel.Dash, now, 0.0);
        frame.VoiceWaveOpacity = Clamp01(V(Value.VoiceWave, now));
        frame.VoiceWaveScaleY = _hasVoiceLevel && shown == LivingCoreState.Speaking && !ReducedMotion
            ? 0.4 + _envelope
            : A(Slot.VoiceWave, KeyframeChannel.ScaleY, now, 1.0);
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);

    /// <summary>One layer's animation in one state.</summary>
    private readonly record struct AnimationSpec(CssKeyframes? Keyframes, double Duration, double Delay = 0.0);
}
