namespace HAMMOR.App.Presence;

/// <summary>
/// The Living Core's motion engine: turns a state, the app's reactions and the
/// passing of time into a <see cref="LivingCoreFrame"/>. Pure arithmetic, no
/// WPF, deterministic: the same inputs at the same times always produce the
/// same frames.
/// </summary>
/// <remarks>
/// <para>
/// The behavioural source is the approved Living Core canvas: its prototype
/// board (the ten states, the events and the pointer), its state boards §04,
/// its interaction rules §05 and its awareness board §06.
/// </para>
/// <para>
/// <b>State changes</b> blend the previous look into the next one with the
/// approved 1.6 s cascade: white core 0 to 0.45 s, cells and stars 0.2 to
/// 0.8 s, halo and threads 0.4 to 1.2 s, aura and energy 0.8 to 1.6 s, all on
/// ease.settle. A change during a change starts from what is on screen, so
/// nothing ever jumps. Success and Wake add a one-shot timeline on top: they
/// reach their peak once, then fall back.
/// </para>
/// <para>
/// <b>Idle</b> runs nine clocks that never synchronise (Motion board): aura
/// 8 s, energy lines 90 and 140 s, halo rings 6.4 s a third apart, threads 14,
/// 22, 30 and 42 s in alternating directions, stars 240 s with four shimmer
/// clocks, cells 140 s with three bob clocks, lens sway 6.4 s and flow 3.2 s,
/// white core drift 9.6 s with one unhurried glance about every 14 s.
/// </para>
/// <para>
/// <b>The white core has one owner</b>: this class. Where it looks is, highest
/// first: the state's pose (Speaking, Listening, Thinking, Blocked), a glance
/// toward a message or a panel, the pointer (Idle only), rest. On top come the
/// idle drift and glance and the single Blocked glance toward the request.
/// Depth follows it: the cells move 30% with it, the stars 10% the other way.
/// Nothing else writes it.
/// </para>
/// <para>
/// <b>Reduced motion</b> keeps every state's position, shape and light, and
/// drops drift, glances, the pointer, orbit, shimmer and breathing. Pulses
/// become a slow change of light; state changes and Wake become a short blend.
/// </para>
/// </remarks>
public sealed class LivingCoreMotion
{
    /// <summary>Length of the full state-change cascade.</summary>
    public const double CascadeSeconds = 1.6;

    /// <summary>Length of a state change under reduced motion.</summary>
    public const double ReducedTransitionSeconds = 0.4;

    /// <summary>The Wake ignition: core, rim, lens, cells and stars, then halo, threads and aura.</summary>
    public const double WakeSeconds = 2.4;

    /// <summary>The Success timeline: the warm bloom, the ring and the rise, once.</summary>
    public const double SuccessSeconds = 3.2;

    /// <summary>A new message's ripple.</summary>
    public const double RippleSeconds = 0.9;

    /// <summary>No two glances within this time, and message bursts within it count as one.</summary>
    public const double GlanceSpacing = 2.0;

    /// <summary>The white core follows the pointer this far behind.</summary>
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

    // A frame never advances the clocks by more than this, so resuming after
    // a pause (minimised, hidden, debugger) continues instead of jumping.
    private const double MaxStep = 0.1;

    private const double GateTimeConstant = 0.15;
    private const double SuccessGateTimeConstant = 0.25;

    // "Smoothed over 120 ms so they never jitter" (state board, Listening).
    private const double EnvelopeTimeConstant = 0.12;

    // Typing is the user's input while Listening: each keystroke lifts the
    // level, which falls away over about half a second.
    private const double InputKick = 0.6;
    private const double InputRelease = 0.45;

    private const double GlanceOut = 0.45;
    private const double GlanceBack = 1.2;
    private const double FirstGlanceDelay = 4.2;
    private const double RequestGlanceDelay = 0.5;
    private const double RequestGlanceHold = 0.9;
    private const double RequestGlanceDx = 9.0;
    private const double RequestGlanceDy = -2.0;

    // Reactions, as fractions of the zone (prototype): a message is down and
    // to the transcript's side, a panel straight to its side.
    private const double MessageGlanceX = 0.5;
    private const double MessageGlanceY = 0.9;
    private const double MessageGlanceHold = 0.6;
    private const double PanelGlanceX = 0.95;
    private const double PanelGlanceHold = 1.0;

    // "The aura leans 4% toward the panel."
    private const double PanelAuraLean = 4.0;

    // "The cells shiver once."
    private const double ShiverSeconds = 0.6;
    private const double ShiverPeriod = 0.15;
    private const double ShiverAmplitude = 0.45;

    // Success: "a thread takes one quick lap".
    private const double SuccessLapSeconds = 1.2;

    private static readonly CubicBezierEasing Linear = new(0.0, 0.0, 1.0, 1.0);

    // Idle micro-drift, under 1.5% (hero board, prototype).
    private static readonly double[] DriftTimes = [0.0, 0.25, 0.5, 0.75, 1.0];
    private static readonly double[] DriftX = [0.0, 0.8, 0.2, -0.7, 0.0];
    private static readonly double[] DriftY = [0.0, -0.5, 0.7, 0.2, 0.0];

    // Idle glances: targets relative to rest, all inside the zone, with the
    // approved hold of 0.6 to 1.2 s and an interval of about 14 s.
    private static readonly double[] GlanceDx = [-14.0, 3.0, -9.0, -4.0];
    private static readonly double[] GlanceDy = [2.0, 5.0, -2.5, 4.0];
    private static readonly double[] GlanceHold = [0.9, 1.2, 0.7, 1.0];
    private static readonly double[] GlanceInterval = [14.0, 15.6, 12.8, 14.8];

    // Speaking: the core pulses with its own speech (1.25 to 1.47).
    private static readonly double[] PulseTimes = [0.0, 0.2, 0.4, 0.6, 0.8, 1.0];
    private static readonly double[] PulseScale = [1.0, 1.136, 1.032, 1.176, 1.048, 1.0];

    private static readonly double[] AuraPulseTimes = [0.0, 0.3, 0.6, 1.0];
    private static readonly double[] AuraPulseScale = [1.0, 1.06, 1.02, 1.0];
    private static readonly double[] AuraPulseLight = [0.9, 1.0, 0.95, 0.9];

    private static readonly double[] ThreadPulseTimes = [0.0, 0.3, 0.6, 0.8, 1.0];
    private static readonly double[] ThreadPulseLight = [0.7, 1.0, 0.82, 1.0, 0.7];

    private static readonly double[] WaveTimes = [0.0, 0.25, 0.5, 0.75, 1.0];
    private static readonly double[] WaveScale = [0.4, 1.2, 0.6, 1.4, 0.4];

    // Listening: the inner ring moves with the user (prototype "vox", 1.6 s).
    private static readonly double[] VoxTimes = [0.0, 0.18, 0.34, 0.52, 0.70, 0.86, 1.0];
    private static readonly double[] VoxScale = [1.0, 0.975, 1.008, 0.982, 1.012, 0.99, 1.0];

    // Error: the inner ring breathes off the 0.8 s grid.
    private static readonly double[] StutterTimes = [0.0, 0.17, 0.23, 0.49, 0.58, 0.83, 1.0];
    private static readonly double[] StutterScale = [1.0, 1.01, 1.004, 1.012, 1.0, 1.006, 1.0];
    private static readonly double[] StutterLight = [0.55, 0.3, 0.5, 0.25, 0.5, 0.32, 0.55];

    // Success, once over 3.2 s (prototype "warm" and "rise").
    private static readonly double[] WarmTimes = [0.0, 0.25, 1.0];
    private static readonly double[] WarmScale = [0.95, 1.12, 1.0];
    private static readonly double[] WarmLight = [0.4, 1.0, 0.55];
    private static readonly double[] RiseTimes = [0.0, 0.25, 0.6, 1.0];
    private static readonly double[] RiseY = [0.0, -3.5, -0.5, 0.0];

    // Still frames for rings under reduced motion (prototype's static poses).
    private static readonly double[] StaticOutwardScale = [1.06, 1.18, 1.3];
    private static readonly double[] StaticInwardScale = [1.22, 1.12, 1.03];

    private readonly double[] _threadAngle = new double[4];
    private readonly double[] _shimmerPhase = new double[4];

    private LivingCoreLook _from;
    private LivingCoreLook _to;
    private double _transitionStart;
    private bool _transitionDone;

    private double _lastTime;
    private bool _hasTime;

    private double _energy1Angle;
    private double _energy2Angle;
    private double _energy1Flow;
    private double _energy2Flow;
    private double _starsAngle;
    private double _cellsAngle;
    private double _lateralFlow;
    private double _fragment1Angle;
    private double _fragment2Angle;
    private double _orbit1Angle;
    private double _orbit2Angle;

    private int _glanceIndex;
    private double _nextGlanceAt;
    private double _glanceStart = double.NaN;
    private double _glanceGate;
    private double _requestGlanceStart = double.NaN;
    private double _requestGate;
    private double _envelope;
    private bool _hasVoiceLevel;

    // Reactions: the last glance of any kind, a glance toward a message or a
    // panel, and the message ripple.
    private double _lastGlanceAt = double.NegativeInfinity;
    private double _attentionStart = double.NaN;
    private double _attentionX;
    private double _attentionY;
    private double _attentionHold;
    private double _attentionLean;
    private double _attentionGate;
    private double _rippleStart = double.NegativeInfinity;

    // One-shot timelines.
    private double _wakeStart = double.NaN;
    private double _successStart = double.NaN;
    private double _successGate;

    // Typing while Listening.
    private double _inputLevel;
    private double _inputAt = double.NegativeInfinity;

    // The pointer: where it is in the zone (eased toward each new target)
    // and how much the white core follows it (eased in and out).
    private bool _hasPointer;
    private double _pointerMovedAt = double.NegativeInfinity;
    private double _pointerReleaseAt = double.PositiveInfinity;
    private bool _pointerEngaged;
    private double _pointerWeightFrom;
    private double _pointerWeightTo;
    private double _pointerWeightStart = double.NegativeInfinity;
    private double _pointerWeightLast;
    private double _pointerFromX;
    private double _pointerFromY;
    private double _pointerToX;
    private double _pointerToY;
    private double _pointerMoveStart = double.NegativeInfinity;

    // Where the white core looks this frame, before drift and glances: depth
    // follows it.
    private double _gazeX;
    private double _gazeY;
    private double _auraLean;

    /// <param name="initialState">State to show.</param>
    /// <param name="now">Current time, seconds, from a monotonic clock.</param>
    /// <param name="fromDormant">
    /// Arrive through the Wake ignition (core first, aura last), the way the
    /// app opens, instead of appearing fully formed.
    /// </param>
    /// <param name="reducedMotion">Start in reduced motion.</param>
    public LivingCoreMotion(LivingCoreState initialState, double now, bool fromDormant, bool reducedMotion = false)
    {
        State = initialState;
        ReducedMotion = reducedMotion;
        _to = LivingCoreLooks.For(initialState);
        _from = _to;
        _transitionStart = now;
        _transitionDone = true;
        _gazeX = _to.Core.X;
        _gazeY = _to.Core.Y;

        for (var i = 0; i < _threadAngle.Length; i++)
        {
            var thread = LivingCoreDesign.Threads[i];

            // CSS negative delays start a loop part-way through.
            _threadAngle[i] = Direction(thread) * 360.0 * -thread.Delay / thread.Period;
        }

        for (var k = 0; k < _shimmerPhase.Length; k++)
        {
            var clock = LivingCoreDesign.ShimmerClocks[k];
            _shimmerPhase[k] = Frac(-clock.Delay / clock.Period);
        }

        if (fromDormant)
        {
            _wakeStart = now;
        }

        EnterBehaviours(initialState, now);
    }

    /// <summary>The state currently shown (or being blended toward).</summary>
    public LivingCoreState State { get; private set; }

    /// <summary>Honour the system's reduced-motion setting.</summary>
    public bool ReducedMotion { get; set; }

    /// <summary>How much the white core followed the pointer in the last frame, 0 to 1.</summary>
    public double PointerWeight => _pointerWeightLast;

    private double TransitionLength => ReducedMotion ? ReducedTransitionSeconds : CascadeSeconds;

    private double WakeLength => ReducedMotion ? ReducedTransitionSeconds : WakeSeconds;

    /// <summary>
    /// Eased progress of each cascade group <paramref name="elapsed"/> seconds
    /// into a state change.
    /// </summary>
    public static CascadeProgress GetCascadeProgress(double elapsed, bool reducedMotion)
    {
        if (reducedMotion)
        {
            var t = LivingCoreEasings.Settle.Evaluate(Clamp01(elapsed / ReducedTransitionSeconds));
            return new CascadeProgress(t, t, t, t);
        }

        return new CascadeProgress(
            Stage(elapsed, 0.0, 0.45),
            Stage(elapsed, 0.2, 0.6),
            Stage(elapsed, 0.4, 0.8),
            Stage(elapsed, 0.8, 0.8));
    }

    /// <summary>True while a state change is still blending.</summary>
    public bool IsTransitioning(double now) => !_transitionDone && (now - _transitionStart) < TransitionLength;

    /// <summary>True while the Wake ignition is playing.</summary>
    public bool IsWaking(double now) => !double.IsNaN(_wakeStart) && (now - _wakeStart) < WakeLength;

    /// <summary>
    /// Settled in Sleep: only the slow 9.6 s breath moves, so the renderer can
    /// draw less often.
    /// </summary>
    public bool IsResting(double now) =>
        State == LivingCoreState.Sleep && !IsTransitioning(now) && !IsWaking(now) && !IsReacting(now);

    /// <summary>
    /// Whether the renderer has to keep producing frames. False only under
    /// reduced motion once a still state has settled, so a still core costs
    /// nothing. Speaking and Blocked keep their slow change of light;
    /// Listening changes only with real input; reactions and one-shot
    /// timelines finish their light.
    /// </summary>
    public bool NeedsContinuousFrames(double now) =>
        !ReducedMotion
        || IsTransitioning(now)
        || IsWaking(now)
        || IsReacting(now)
        || State is LivingCoreState.Speaking or LivingCoreState.Blocked
        || (State == LivingCoreState.Listening && (_hasVoiceLevel || InputLevelAt(now) > 0.002));

    /// <summary>Starts a cascade toward <paramref name="state"/>.</summary>
    public void SetState(LivingCoreState state, double now)
    {
        if (state == State)
        {
            return;
        }

        _from = CurrentLook(now);
        _to = LivingCoreLooks.For(state);
        _transitionStart = now;
        _transitionDone = false;
        State = state;
        EnterBehaviours(state, now);
    }

    /// <summary>
    /// Reports the pointer. The white core turns toward it in Idle only, never
    /// under reduced motion.
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

        // Direction, not distance: beyond the control it is capped by the zone.
        var distance = Math.Sqrt((x * x) + (y * y));
        if (distance > 1.0)
        {
            x /= distance;
            y /= distance;
        }

        var targetX = LivingCoreLooks.ZoneX + (x * PointerReach * LivingCoreLooks.ZoneRadiusX);
        var targetY = LivingCoreLooks.ZoneY + (y * PointerReach * LivingCoreLooks.ZoneRadiusY);

        if (!_pointerEngaged && PointerWeightAt(now) <= 1e-9)
        {
            // Not following yet: start from the new target, nothing moves.
            _pointerFromX = targetX;
            _pointerFromY = targetY;
        }
        else
        {
            // Retarget from where it is now, like a CSS transition.
            var (currentX, currentY) = PointerAt(now);
            _pointerFromX = currentX;
            _pointerFromY = currentY;
        }

        _pointerToX = targetX;
        _pointerToY = targetY;
        _pointerMoveStart = now;
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
    /// Something happened. A new message sends one ripple from the membrane and
    /// makes the cells shiver once; bursts within two seconds count as one.
    /// Where the state allows it (Idle, Success) a message or a panel also
    /// draws one glance toward it, never two glances within two seconds.
    /// </summary>
    /// <param name="kind">What happened.</param>
    /// <param name="physicalDirection">Its side on screen: negative left, positive right.</param>
    /// <param name="now">Current time.</param>
    public void React(LivingCoreReactionKind kind, double physicalDirection, double now)
    {
        var side = physicalDirection < 0.0 ? -1.0 : 1.0;

        if (kind == LivingCoreReactionKind.NewMessage)
        {
            if (now - _rippleStart < GlanceSpacing)
            {
                return;
            }

            _rippleStart = now;
        }

        if (ReducedMotion
            || !GlanceAllowed(State)
            || !double.IsNaN(_attentionStart)
            || !double.IsNaN(_glanceStart)
            || now - _lastGlanceAt < GlanceSpacing)
        {
            return;
        }

        if (kind == LivingCoreReactionKind.NewMessage)
        {
            _attentionX = LivingCoreLooks.ZoneX + (side * MessageGlanceX * LivingCoreLooks.ZoneRadiusX);
            _attentionY = LivingCoreLooks.ZoneY + (MessageGlanceY * LivingCoreLooks.ZoneRadiusY);
            _attentionHold = MessageGlanceHold;
            _attentionLean = 0.0;
        }
        else
        {
            _attentionX = LivingCoreLooks.ZoneX + (side * PanelGlanceX * LivingCoreLooks.ZoneRadiusX);
            _attentionY = LivingCoreLooks.ZoneY;
            _attentionHold = PanelGlanceHold;
            _attentionLean = side * PanelAuraLean;
        }

        _attentionStart = now;
        _lastGlanceAt = now;
    }

    /// <summary>
    /// The user typed. While Listening, the inward rings and the inner ring
    /// follow the rhythm of their input.
    /// </summary>
    public void NoteInput(double now)
    {
        _inputLevel = Math.Min(1.0, InputLevelAt(now) + InputKick);
        _inputAt = now;
    }

    /// <summary>The look on screen at <paramref name="now"/>.</summary>
    public LivingCoreLook CurrentLook(double now)
    {
        if (_transitionDone)
        {
            return _to;
        }

        var elapsed = now - _transitionStart;
        if (elapsed >= TransitionLength)
        {
            _transitionDone = true;
            _from = _to;
            return _to;
        }

        return LivingCoreLook.Blend(_from, _to, GetCascadeProgress(elapsed, ReducedMotion));
    }

    /// <summary>
    /// Advances to <paramref name="now"/> and writes the frame.
    /// </summary>
    /// <param name="now">Current time, seconds, same clock as the constructor.</param>
    /// <param name="voiceLevel">
    /// Level of the voice the state is about, 0 to 1, or NaN when no real
    /// envelope is connected. Without one, Speaking uses its own designed
    /// rhythm and Listening follows the user's typing.
    /// </param>
    /// <param name="requestDirection">
    /// Physical side of the approval request: negative for left, positive for
    /// right. Used by the single Blocked glance.
    /// </param>
    /// <param name="frame">Frame to overwrite.</param>
    public void Advance(double now, double voiceLevel, double requestDirection, LivingCoreFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var dt = _hasTime ? Math.Clamp(now - _lastTime, 0.0, MaxStep) : 0.0;
        _lastTime = now;
        _hasTime = true;

        var look = CurrentLook(now);
        var reduced = ReducedMotion;
        var hasVoice = !double.IsNaN(voiceLevel);
        _hasVoiceLevel = hasVoice;
        _envelope = hasVoice
            ? Smooth(_envelope, Math.Clamp(voiceLevel, 0.0, 1.0), dt, EnvelopeTimeConstant)
            : 0.0;

        // Success plays at full strength from its first frame; leaving it
        // fades the bloom instead of cutting it.
        var successTarget = State == LivingCoreState.Success ? 1.0 : 0.0;
        _successGate = reduced || successTarget >= _successGate
            ? successTarget
            : Smooth(_successGate, successTarget, dt, SuccessGateTimeConstant);

        if (!reduced)
        {
            AdvanceClocks(look, dt);
        }

        var wake = WakeAt(now, reduced);

        WriteCore(look, now, dt, reduced, hasVoice, requestDirection, wake, frame);
        WriteOutside(look, now, reduced, hasVoice, wake, frame);
        WriteInside(look, now, reduced, wake, frame);
    }

    private static bool GlanceAllowed(LivingCoreState state) =>
        state is LivingCoreState.Idle or LivingCoreState.Success;

    private bool IsReacting(double now) =>
        (now - _rippleStart) < RippleSeconds
        || (!double.IsNaN(_successStart) && (now - _successStart) < SuccessSeconds)
        || (_successGate > 0.002 && State != LivingCoreState.Success);

    private double InputLevelAt(double now) =>
        double.IsNegativeInfinity(_inputAt)
            ? 0.0
            : _inputLevel * Math.Exp(-Math.Max(0.0, now - _inputAt) / InputRelease);

    private double PointerWeightAt(double now) =>
        Mix(_pointerWeightFrom, _pointerWeightTo, LivingCoreEasings.Settle.Evaluate(Clamp01((now - _pointerWeightStart) / PointerLag)));

    private (double X, double Y) PointerAt(double now)
    {
        var t = LivingCoreEasings.Settle.Evaluate(Clamp01((now - _pointerMoveStart) / PointerLag));
        return (Mix(_pointerFromX, _pointerToX, t), Mix(_pointerFromY, _pointerToY, t));
    }

    private void EnterBehaviours(LivingCoreState state, double now)
    {
        if (state == LivingCoreState.Idle)
        {
            _nextGlanceAt = now + FirstGlanceDelay;
        }

        // Blocked glances once toward the request, then waits. Leaving Blocked
        // mid-glance lets the gate fade the glance instead of cutting it.
        if (state == LivingCoreState.Blocked && !ReducedMotion)
        {
            _requestGlanceStart = now + RequestGlanceDelay;
        }

        // Wake ignites once; a wake already playing carries on.
        if (state == LivingCoreState.Wake && !IsWaking(now))
        {
            _wakeStart = now;
        }

        // Success reaches its peak once; a second success inside it is the same moment.
        if (state == LivingCoreState.Success
            && (double.IsNaN(_successStart) || (now - _successStart) >= SuccessSeconds))
        {
            _successStart = now;
        }
    }

    private void AdvanceClocks(in LivingCoreLook look, double dt)
    {
        var energy = look.Aura.EnergySpeed * dt;
        _energy1Angle = (_energy1Angle + ((360.0 / 90.0) * energy)) % 360.0;
        _energy2Angle = (_energy2Angle - ((360.0 / 140.0) * energy)) % 360.0;

        // Dash flow: 40 units per 6 s and 9 s, kept within one pattern.
        _energy1Flow = (_energy1Flow - ((40.0 / 6.0) * energy)) % 20.0;
        _energy2Flow = (_energy2Flow - ((40.0 / 9.0) * energy)) % 32.0;

        AdvanceThread(0, look.Halo.ThreadSpeed1, dt);
        AdvanceThread(1, look.Halo.ThreadSpeed2, dt);
        AdvanceThread(2, look.Halo.ThreadSpeed3, dt);
        AdvanceThread(3, look.Halo.ThreadSpeed4, dt);

        _fragment1Angle = (_fragment1Angle + ((360.0 / 36.0) * dt)) % 360.0;
        _fragment2Angle = (_fragment2Angle - ((360.0 / 52.0) * dt)) % 360.0;

        _starsAngle = (_starsAngle + ((360.0 / 240.0) * look.Inside.StarDriftSpeed * dt)) % 360.0;
        _cellsAngle = (_cellsAngle + ((360.0 / 140.0) * look.Inside.CellDriftSpeed * dt)) % 360.0;
        _orbit1Angle = (_orbit1Angle + ((360.0 / 9.6) * dt)) % 360.0;
        _orbit2Angle = (_orbit2Angle - ((360.0 / 14.4) * dt)) % 360.0;

        // Lateral lines: 14.4 units per 3.2 s, pattern 3.6.
        _lateralFlow = (_lateralFlow - ((14.4 / 3.2) * look.Inside.LateralFlowSpeed * dt)) % 3.6;

        for (var k = 0; k < _shimmerPhase.Length; k++)
        {
            var period = LivingCoreDesign.ShimmerClocks[k].Period;
            _shimmerPhase[k] = Frac(_shimmerPhase[k] + (dt * look.Inside.StarShimmerSpeed / period));
        }
    }

    private void AdvanceThread(int index, double speed, double dt)
    {
        var thread = LivingCoreDesign.Threads[index];
        _threadAngle[index] = (_threadAngle[index] + (Direction(thread) * (360.0 / thread.Period) * speed * dt)) % 360.0;
    }

    private void WriteCore(
        in LivingCoreLook look,
        double now,
        double dt,
        bool reduced,
        bool hasVoice,
        double requestDirection,
        in WakeFactors wake,
        LivingCoreFrame frame)
    {
        var poseX = look.Core.X;
        var poseY = look.Core.Y;

        // ---- Pointer (Idle only): follows inside its zone, 450 ms behind,
        // and lets go after 5 s of stillness or when the pointer leaves ----
        var wantPointer = !reduced
            && State == LivingCoreState.Idle
            && _hasPointer
            && now < _pointerReleaseAt
            && (now - _pointerMovedAt) < PointerStillness;
        if (reduced)
        {
            _pointerEngaged = false;
            _pointerWeightFrom = 0.0;
            _pointerWeightTo = 0.0;
            _pointerWeightStart = double.NegativeInfinity;
        }
        else if (wantPointer != _pointerEngaged)
        {
            _pointerWeightFrom = PointerWeightAt(now);
            _pointerWeightTo = wantPointer ? 1.0 : 0.0;
            _pointerWeightStart = now;
            _pointerEngaged = wantPointer;
        }

        var pointerWeight = PointerWeightAt(now);
        _pointerWeightLast = pointerWeight;

        var gazeX = poseX;
        var gazeY = poseY;
        if (pointerWeight > 0.0)
        {
            var (pointerX, pointerY) = PointerAt(now);
            gazeX = Mix(poseX, pointerX, pointerWeight * look.Core.Drift);
            gazeY = Mix(poseY, pointerY, pointerWeight * look.Core.Drift);
        }

        // ---- A glance toward a message or a panel: out, hold, back. Gated so
        // a state that does not allow it (listening, speaking) fades it ----
        _attentionGate = Smooth(_attentionGate, GlanceAllowed(State) && !reduced ? 1.0 : 0.0, dt, GateTimeConstant);
        var lean = 0.0;
        if (!double.IsNaN(_attentionStart))
        {
            var amount = GlanceAmount(now - _attentionStart, _attentionHold);
            if (amount < 0.0 || reduced)
            {
                _attentionStart = double.NaN;
            }
            else
            {
                var weight = amount * _attentionGate;
                gazeX = Mix(gazeX, _attentionX, weight);
                gazeY = Mix(gazeY, _attentionY, weight);
                lean = _attentionLean * weight;
            }
        }

        _gazeX = gazeX;
        _gazeY = gazeY;
        _auraLean = lean;

        var x = gazeX;
        var y = gazeY;

        // ---- Idle micro-drift ----
        if (!reduced && look.Core.Drift > 0.0)
        {
            var phase = now / 9.6;
            x += Keyframes(phase, DriftTimes, DriftX, LivingCoreEasings.InOut) * look.Core.Drift;
            y += Keyframes(phase, DriftTimes, DriftY, LivingCoreEasings.InOut) * look.Core.Drift;
        }

        // ---- Idle glance: out 0.45 s, hold, back 1.2 s. Gated so leaving
        // Idle fades a glance in progress instead of cutting it. It waits
        // while the core follows the pointer or another glance is near ----
        var idle = State == LivingCoreState.Idle && !reduced;
        _glanceGate = Smooth(_glanceGate, idle ? 1.0 : 0.0, dt, GateTimeConstant);
        if (reduced)
        {
            _glanceStart = double.NaN;
        }
        else
        {
            if (idle
                && double.IsNaN(_glanceStart)
                && double.IsNaN(_attentionStart)
                && now >= _nextGlanceAt
                && pointerWeight < 0.3
                && (now - _lastGlanceAt) >= GlanceSpacing)
            {
                _glanceStart = now;
                _lastGlanceAt = now;
                _nextGlanceAt = now + GlanceInterval[_glanceIndex % GlanceInterval.Length];
            }

            if (!double.IsNaN(_glanceStart))
            {
                var slot = _glanceIndex % GlanceDx.Length;
                var amount = GlanceAmount(now - _glanceStart, GlanceHold[slot]);
                if (amount < 0.0)
                {
                    _glanceStart = double.NaN;
                    _glanceIndex++;
                }
                else
                {
                    var weight = amount * _glanceGate * (1.0 - pointerWeight);
                    x += GlanceDx[slot] * weight;
                    y += GlanceDy[slot] * weight;
                }
            }
        }

        // ---- Blocked: the single glance toward the request ----
        var blocked = State == LivingCoreState.Blocked && !reduced;
        _requestGate = Smooth(_requestGate, blocked ? 1.0 : 0.0, dt, GateTimeConstant);
        if (reduced)
        {
            _requestGlanceStart = double.NaN;
        }
        else if (!double.IsNaN(_requestGlanceStart))
        {
            var amount = GlanceAmount(now - _requestGlanceStart, RequestGlanceHold);
            if (amount < 0.0)
            {
                _requestGlanceStart = double.NaN;
            }
            else
            {
                var side = requestDirection < 0.0 ? -1.0 : 1.0;
                x += RequestGlanceDx * side * amount * _requestGate;
                y += RequestGlanceDy * amount * _requestGate;
            }
        }

        ClampToZone(ref x, ref y);
        frame.CoreX = x;
        frame.CoreY = y;

        // ---- Speaking: pulse with the speech envelope, or the designed rhythm ----
        var speakWeight = Clamp01(look.Core.SpeakRing);
        var pulse = 1.0;
        if (speakWeight > 0.0 && !reduced)
        {
            var raw = hasVoice
                ? 1.0 + (0.176 * _envelope)
                : Keyframes(now / 1.6, PulseTimes, PulseScale, LivingCoreEasings.Breath);
            pulse = Mix(1.0, raw, speakWeight);
        }

        frame.CoreScale = look.Core.Scale * pulse * wake.PupilScale;
        frame.CoreOpacity = Clamp01(look.Core.Opacity * wake.PupilLight);
        frame.EmberOpacity = Clamp01(look.Core.Ember) * frame.CoreOpacity;

        var glow = reduced ? 0.5 : Swing(now / 4.8, LivingCoreEasings.InOut);
        var glowLight = 0.9 + (0.1 * glow);
        if (speakWeight > 0.0 && reduced)
        {
            // Reduced motion: the speaking pulse becomes a slow change of light.
            glowLight = Mix(glowLight, 0.8 + (0.2 * Swing(now / 2.4, LivingCoreEasings.Breath)), speakWeight);
        }

        frame.GlowOpacity = Clamp01(look.Core.Opacity * Math.Min(1.0, look.Core.Glow) * glowLight * wake.PupilLight);
        frame.GlowScale = (1.0 + (0.1 * glow)) * Math.Max(1.0, look.Core.Glow) * look.Core.GlowSize;

        // The attention ring breathes; reduced motion drops breathing and
        // holds it at full.
        var attention = reduced ? 1.0 : 0.55 + (0.45 * Swing(now / 3.2, LivingCoreEasings.Breath));
        frame.AttentionOpacity = Clamp01(look.Core.AttentionRing * attention);
        frame.SpeakRingOpacity = Clamp01(look.Core.SpeakRing * look.Core.Opacity * wake.PupilLight);
    }

    private void WriteOutside(
        in LivingCoreLook look,
        double now,
        bool reduced,
        bool hasVoice,
        in WakeFactors wake,
        LivingCoreFrame frame)
    {
        // ---- Aura: breathes 4% over 8 s; pulses with speech; leans toward a panel ----
        var breath = reduced ? 0.5 : Swing(now / 8.0, LivingCoreEasings.Breath);
        var pulseScale = 1.0;
        var pulseLight = 1.0;
        if (look.Aura.AuraPulse > 0.0 && !reduced)
        {
            if (hasVoice)
            {
                pulseScale = 1.0 + (0.06 * _envelope);
                pulseLight = 0.9 + (0.1 * _envelope);
            }
            else
            {
                var phase = now / 1.6;
                pulseScale = Keyframes(phase, AuraPulseTimes, AuraPulseScale, LivingCoreEasings.Breath);
                pulseLight = Keyframes(phase, AuraPulseTimes, AuraPulseLight, LivingCoreEasings.Breath);
            }
        }

        frame.AuraScale = 1.0
            + (look.Aura.AuraBreath * (-0.04 + (0.08 * breath)))
            + (look.Aura.AuraPulse * (pulseScale - 1.0));
        frame.AuraOpacity = Clamp01(
            look.Aura.AuraOpacity
            * Mix(1.0, 0.78 + (0.22 * breath), look.Aura.AuraBreath)
            * Mix(1.0, pulseLight, look.Aura.AuraPulse)
            * wake.Aura);
        frame.AuraLeanX = reduced ? 0.0 : _auraLean;

        // ---- Success: the warm bloom and one clean ring, once ----
        var successTime = now - _successStart;
        if (double.IsNaN(_successStart) || _successGate <= 0.0005)
        {
            frame.WarmScale = 1.0;
            frame.WarmOpacity = 0.0;
            frame.SuccessRingScale = 1.0;
            frame.SuccessRingOpacity = 0.0;
        }
        else
        {
            var u = Clamp01(successTime / SuccessSeconds);
            frame.WarmScale = reduced ? 1.0 : Timeline(u, WarmTimes, WarmScale, LivingCoreEasings.Settle);
            frame.WarmOpacity = Clamp01(Timeline(u, WarmTimes, WarmLight, LivingCoreEasings.Settle) * _successGate * wake.Aura);

            // Expands 0.95 to 1.5 and dissolves within the first 45%.
            frame.SuccessRingScale = reduced
                ? 1.2
                : u < 0.45 ? Mix(0.95, 1.5, LivingCoreEasings.Strike.Evaluate(u / 0.45)) : 1.5;
            var ringLight = u < 0.1
                ? LivingCoreEasings.Strike.Evaluate(u / 0.1)
                : u < 0.45 ? 1.0 - LivingCoreEasings.Strike.Evaluate((u - 0.1) / 0.35) : 0.0;
            frame.SuccessRingOpacity = Clamp01(ringLight * _successGate);
        }

        // ---- Energy lines ----
        frame.EnergyOpacity = Clamp01(look.Aura.EnergyOpacity);
        frame.Energy1Angle = _energy1Angle;
        frame.Energy2Angle = _energy2Angle;
        frame.Energy1Flow = _energy1Flow;
        frame.Energy2Flow = _energy2Flow;

        // ---- Halo rings: ±1.4% over 6.4 s, a third apart ----
        var input = InputLevelAt(now);
        var activity = hasVoice ? _envelope : input;
        for (var i = 0; i < 3; i++)
        {
            var b = reduced ? 0.5 : Swing((now - LivingCoreDesign.HaloDelays[i]) / LivingCoreDesign.HaloPeriod, LivingCoreEasings.Breath);
            var scale = 1.0 + (look.Halo.HaloBreath * 0.014 * b);
            var light = Mix(1.0, 0.75 + (0.25 * b), look.Halo.HaloBreath);

            if (i == 0 && look.Halo.HaloIrregular > 0.0)
            {
                double stutterScale;
                double stutterLight;
                if (reduced)
                {
                    stutterScale = 1.0;
                    stutterLight = 0.4;
                }
                else
                {
                    var phase = now / 2.4;
                    stutterScale = Keyframes(phase, StutterTimes, StutterScale, Linear);
                    stutterLight = Keyframes(phase, StutterTimes, StutterLight, Linear);
                }

                scale = Mix(scale, stutterScale, look.Halo.HaloIrregular);
                light = Mix(light, stutterLight, look.Halo.HaloIrregular);
            }

            if (i == 0 && look.Halo.HaloTight > 0.0)
            {
                // Warning: the halo tightens and holds (97 to 98%).
                var hold = reduced ? 0.5 : Swing(now / 3.2, LivingCoreEasings.Breath);
                scale = Mix(scale, 0.97 + (0.01 * hold), look.Halo.HaloTight);
                light = Mix(light, 0.8 + (0.2 * hold), look.Halo.HaloTight);
            }

            if (i == 0 && look.Halo.Vox > 0.0 && !reduced)
            {
                // Listening: the inner ring moves with the user's input.
                var strength = look.Halo.Vox * (hasVoice ? _envelope : 0.5 + (0.5 * activity));
                scale *= 1.0 + ((Keyframes(now / 1.6, VoxTimes, VoxScale, LivingCoreEasings.Breath) - 1.0) * strength);
            }

            if (i == 0 && hasVoice && !reduced && look.Halo.InwardRings > 0.0)
            {
                // The inner ring draws in with the user's voice; reduced
                // motion keeps it in place.
                scale -= 0.02 * _envelope * look.Halo.InwardRings;
            }

            if (i == 1)
            {
                // Blocked: the gap ring takes the middle ring's place, so the
                // gap at the top reads as a gap.
                light *= 1.0 - look.Halo.GapRingOpacity;
            }

            frame.HaloScale[i] = scale * look.Halo.HaloScale * wake.HaloScale;
            frame.HaloOpacity[i] = Clamp01(look.Halo.HaloOpacity * light * wake.HaloLight);
        }

        frame.RimDanger = Clamp01(look.Halo.RimDanger);
        frame.RimSuccess = Clamp01(look.Halo.RimSuccess);
        frame.RimWarning = Clamp01(look.Halo.RimWarning);

        // ---- Speaking: rings pulse outward, 1.8 s, staggered 0.6 s ----
        for (var k = 0; k < 3; k++)
        {
            if (look.Halo.OutwardRings <= 0.0)
            {
                frame.OutwardScale[k] = 1.0;
                frame.OutwardOpacity[k] = 0.0;
                continue;
            }

            double scale;
            double light;
            if (reduced)
            {
                scale = StaticOutwardScale[k];
                light = 1.0;
            }
            else
            {
                var e = LivingCoreEasings.RingOut.Evaluate(Frac((now - (k * 0.6)) / 1.8));
                scale = 1.0 + (0.4 * e);
                light = 1.0 - e;
            }

            var gain = hasVoice ? 0.5 + (0.5 * _envelope) : 1.0;
            frame.OutwardScale[k] = scale;
            frame.OutwardOpacity[k] = Clamp01(look.Halo.OutwardRings * light * gain);
        }

        // ---- Listening: rings draw inward; stronger with the user's input,
        // and with a real voice they follow only the voice ----
        var inwardGain = look.Halo.InwardRings * (hasVoice ? _envelope : 0.55 + (0.45 * input));
        for (var k = 0; k < 3; k++)
        {
            if (inwardGain <= 0.001)
            {
                frame.InwardScale[k] = 1.0;
                frame.InwardOpacity[k] = 0.0;
                continue;
            }

            double scale;
            double light;
            if (reduced)
            {
                scale = StaticInwardScale[k];
                light = 1.0;
            }
            else
            {
                var p = Frac((now - (k * 0.6)) / 1.8);
                scale = 1.3 + ((1.02 - 1.3) * LivingCoreEasings.RingIn.Evaluate(p));
                light = p < 0.35
                    ? LivingCoreEasings.RingIn.Evaluate(p / 0.35)
                    : 1.0 - LivingCoreEasings.RingIn.Evaluate((p - 0.35) / 0.65);
            }

            frame.InwardScale[k] = scale;
            frame.InwardOpacity[k] = Clamp01(inwardGain * light);
        }

        frame.GapRingOpacity = Clamp01(look.Halo.GapRingOpacity);

        // ---- Warning: one amber segment pulses every 3.2 s ----
        frame.WarningMarkOpacity = Clamp01(look.Halo.WarningMark
            * (reduced ? 0.7 : 0.35 + (0.65 * Swing(now / 3.2, LivingCoreEasings.Breath))));

        // ---- Threads; Success sends the first one round once ----
        var lap = !reduced && !double.IsNaN(_successStart) && successTime >= 0.0 && successTime < SuccessLapSeconds
            ? 360.0 * LivingCoreEasings.Settle.Evaluate(successTime / SuccessLapSeconds)
            : 0.0;
        for (var i = 0; i < 4; i++)
        {
            frame.ThreadAngle[i] = i == 0 ? (_threadAngle[0] + lap) % 360.0 : _threadAngle[i];
        }

        var threadLight = 1.0;
        if (look.Halo.ThreadPulse > 0.0 && !reduced)
        {
            var pulse = hasVoice
                ? 0.7 + (0.3 * _envelope)
                : Keyframes(now / 1.6, ThreadPulseTimes, ThreadPulseLight, LivingCoreEasings.Breath);
            threadLight = Mix(1.0, pulse, look.Halo.ThreadPulse);
        }

        frame.ThreadsOpacity = Clamp01(look.Halo.ThreadOpacity * threadLight * wake.HaloLight);
        frame.ThreadsScale = look.Halo.HaloScale * wake.HaloScale;

        // Blocked: one slow pulse of light at the paused threads (kept under
        // reduced motion: a slow change of light, never a flash).
        frame.ParkOpacity = Clamp01(look.Halo.ParkOpacity * (0.45 + (0.55 * Swing(now / 3.2, LivingCoreEasings.Breath))));

        frame.FragmentOpacity = Clamp01(look.Halo.FragmentOpacity);
        frame.Fragment1Angle = _fragment1Angle;
        frame.Fragment2Angle = _fragment2Angle;

        // ---- The body: Sleep sinks and shrinks it, breathing over 9.6 s ----
        var sleepBreath = reduced ? 0.0 : Swing(now / 9.6, LivingCoreEasings.Sink);
        frame.BodyScale = look.Halo.BodyScale * (1.0 + (look.Halo.BodyBreath * ((0.875 / 0.86) - 1.0) * sleepBreath));
        frame.BodyY = look.Halo.BodyDrop;
        frame.BodyOpacity = Clamp01(look.Halo.BodyOpacity);
    }

    private void WriteInside(in LivingCoreLook look, double now, bool reduced, in WakeFactors wake, LivingCoreFrame frame)
    {
        frame.CrackOpacity = Clamp01(look.Inside.CrackOpacity);
        frame.RimDrawn = wake.Rim;

        // ---- A new message: one soft ripple leaves the membrane ----
        var rippleTime = now - _rippleStart;
        if (rippleTime >= 0.0 && rippleTime < RippleSeconds)
        {
            var e = LivingCoreEasings.Strike.Evaluate(rippleTime / RippleSeconds);
            frame.RippleScale = reduced ? 1.15 : 1.0 + (0.35 * e);
            frame.RippleOpacity = reduced
                ? 0.5 * Math.Sin(Math.PI * rippleTime / RippleSeconds)
                : 0.75 * (1.0 - e);
        }
        else
        {
            frame.RippleScale = 1.0;
            frame.RippleOpacity = 0.0;
        }

        // Depth: where the white core looks, measured from its zone's centre.
        var gazeOffsetX = _gazeX - LivingCoreLooks.ZoneX;
        var gazeOffsetY = _gazeY - LivingCoreLooks.ZoneY;

        // ---- Deep stars: drift as a field, shimmer on four clocks ----
        frame.StarsAngle = _starsAngle;
        frame.StarsScale = look.Inside.StarScale * wake.InsideScale;
        for (var k = 0; k < 4; k++)
        {
            var dip = reduced ? 0.0 : Swing(_shimmerPhase[k], LivingCoreEasings.InOut);
            var shimmer = 1.0 - (look.Inside.StarShimmerDepth * 0.65 * dip);
            frame.StarClockOpacity[k] = Clamp01(look.Inside.StarOpacity * shimmer * wake.InsideLight);
        }

        frame.StarsShiftX = StarsDepth * gazeOffsetX;
        frame.StarsShiftY = StarsDepth * gazeOffsetY;

        // ---- Floating cells: drift as a school, bob on three clocks; rise
        // together on Success; settle like sediment in Sleep ----
        var rise = 0.0;
        if (!reduced && !double.IsNaN(_successStart) && _successGate > 0.0005)
        {
            rise = Timeline(Clamp01((now - _successStart) / SuccessSeconds), RiseTimes, RiseY, LivingCoreEasings.Settle) * _successGate;
        }

        frame.CellsSpread = look.Inside.CellSpread * look.Inside.CellNarrow * wake.InsideScale;
        frame.CellsScaleY = look.Inside.CellSpread * look.Inside.CellSquash * wake.InsideScale;
        frame.CellsY = look.Inside.CellDrop + rise;
        frame.CellsOpacity = Clamp01(look.Inside.CellOpacity * wake.InsideLight);
        frame.CellsAngle = _cellsAngle;

        var shiver = 0.0;
        if (!reduced && rippleTime >= 0.0 && rippleTime < ShiverSeconds)
        {
            var fade = 1.0 - (rippleTime / ShiverSeconds);
            shiver = ShiverAmplitude * Math.Sin(2.0 * Math.PI * rippleTime / ShiverPeriod) * fade * fade;
        }

        frame.CellsShiftX = (CellsDepth * gazeOffsetX) + shiver;
        frame.CellsShiftY = CellsDepth * gazeOffsetY;

        for (var j = 0; j < 3; j++)
        {
            var clock = LivingCoreDesign.BobClocks[j];
            var up = reduced ? 0.0 : Swing((now - clock.Delay) / clock.Period, LivingCoreEasings.InOut) * look.Inside.CellBob;
            frame.BobX[j] = LivingCoreDesign.BobX * up;
            frame.BobY[j] = LivingCoreDesign.BobY * up;
        }

        // ---- Thinking: links, signals, orbit lanes ----
        frame.LinkOpacity = Clamp01(look.Inside.LinkOpacity);
        var links = LivingCoreDesign.Links;
        for (var s = 0; s < links.Length; s++)
        {
            var link = links[s];
            var length = link.Length;
            var travel = reduced
                ? length * 0.5
                : LivingCoreDesign.SignalTravel * Frac((now - link.Delay) / LivingCoreDesign.SignalPeriod);
            var visible = travel <= length ? 1.0 : 0.0;
            var f = Math.Min(travel, length) / length;
            frame.SignalX[s] = link.X1 + ((link.X2 - link.X1) * f);
            frame.SignalY[s] = link.Y1 + ((link.Y2 - link.Y1) * f);
            frame.SignalOpacity[s] = Clamp01(look.Inside.SignalOpacity * visible);
        }

        frame.OrbitOpacity = Clamp01(look.Inside.OrbitOpacity);
        frame.Orbit1Angle = _orbit1Angle;
        frame.Orbit2Angle = _orbit2Angle;

        // ---- Lens current; Warning narrows the lens and lifts the crest ----
        frame.LensOpacity = Clamp01(look.Inside.LensOpacity * wake.LensLight);
        frame.LensAngle = reduced ? 0.0 : look.Inside.LensPrecession * (-5.0 + (10.0 * Swing(now / 4.8, LivingCoreEasings.Breath)));
        frame.LensScaleX = wake.LensScaleX;
        frame.LensScaleY = look.Inside.LensNarrow;
        frame.CrestAngle = reduced ? 0.0 : look.Inside.LensSway * (-1.4 + (2.8 * Swing(now / 6.4, LivingCoreEasings.Breath)));
        frame.CrestY = look.Inside.CrestLift;
        frame.LateralAOpacity = Clamp01(look.Inside.LateralA);
        frame.LateralBOpacity = Clamp01(look.Inside.LateralB);
        frame.LateralFlow = _lateralFlow;
        frame.VoiceWaveOpacity = Clamp01(look.Inside.VoiceWave * 0.95);
        frame.VoiceWaveScaleY = reduced
            ? 1.0
            : _hasVoiceLevel
                ? 0.4 + _envelope
                : Keyframes(now / 1.6, WaveTimes, WaveScale, LivingCoreEasings.Breath);
    }

    /// <summary>
    /// The Wake ignition at <paramref name="now"/>, as multipliers for each
    /// layer (state board 10): a point of light swells past full and settles
    /// (0 to 25%), the rim draws itself around (16 to 50%), the lens sweeps in
    /// (30 to 60%), cells and stars bloom outward (48 to 80%), halo and
    /// threads arrive last (68 to 100%) as the aura rises (60 to 100%).
    /// Reduced motion fades everything in over 0.4 s instead.
    /// </summary>
    private WakeFactors WakeAt(double now, bool reduced)
    {
        if (double.IsNaN(_wakeStart))
        {
            return WakeFactors.None;
        }

        var t = Math.Max(0.0, now - _wakeStart);
        if (t >= WakeLength)
        {
            _wakeStart = double.NaN;
            return WakeFactors.None;
        }

        if (reduced)
        {
            var f = LivingCoreEasings.Settle.Evaluate(t / ReducedTransitionSeconds);
            return new WakeFactors(1.0, f, 1.0, 1.0, f, 1.0, f, 1.0, f, f);
        }

        var u = t / WakeSeconds;
        var pupilScale = u < 0.12
            ? 1.5 * LivingCoreEasings.Strike.Evaluate(u / 0.12)
            : u < 0.25 ? Mix(1.5, 1.0, LivingCoreEasings.Strike.Evaluate((u - 0.12) / 0.13)) : 1.0;
        var pupilLight = u < 0.12 ? LivingCoreEasings.Strike.Evaluate(u / 0.12) : 1.0;
        var rim = Phase(u, 0.16, 0.50, LivingCoreEasings.Settle);
        var lens = Phase(u, 0.30, 0.60, LivingCoreEasings.Settle);
        var inside = Phase(u, 0.48, 0.80, LivingCoreEasings.Settle);
        var halo = Phase(u, 0.68, 1.0, LivingCoreEasings.Settle);
        var aura = Phase(u, 0.60, 1.0, LivingCoreEasings.Breath);

        return new WakeFactors(
            pupilScale,
            pupilLight,
            rim,
            Mix(0.6, 1.0, lens),
            lens,
            Mix(0.3, 1.0, inside),
            inside,
            Mix(0.92, 1.0, halo),
            halo,
            aura);
    }

    /// <summary>
    /// Glance timeline: eased out, held, eased back. Returns the amount 0 to
    /// 1, or -1 once the glance is over.
    /// </summary>
    private static double GlanceAmount(double elapsed, double hold)
    {
        if (elapsed < 0.0)
        {
            return 0.0;
        }

        if (elapsed < GlanceOut)
        {
            return LivingCoreEasings.Strike.Evaluate(elapsed / GlanceOut);
        }

        elapsed -= GlanceOut;
        if (elapsed < hold)
        {
            return 1.0;
        }

        elapsed -= hold;
        if (elapsed < GlanceBack)
        {
            return 1.0 - LivingCoreEasings.Settle.Evaluate(elapsed / GlanceBack);
        }

        return -1.0;
    }

    /// <summary>
    /// Keeps the white core inside its zone (±16% by ±7%): it never touches
    /// the lens, whatever combination of pose and glance produced it.
    /// </summary>
    private static void ClampToZone(ref double x, ref double y)
    {
        var nx = (x - LivingCoreLooks.ZoneX) / LivingCoreLooks.ZoneRadiusX;
        var ny = (y - LivingCoreLooks.ZoneY) / LivingCoreLooks.ZoneRadiusY;
        var distance = Math.Sqrt((nx * nx) + (ny * ny));
        if (distance <= 1.0)
        {
            return;
        }

        x = LivingCoreLooks.ZoneX + (nx / distance * LivingCoreLooks.ZoneRadiusX);
        y = LivingCoreLooks.ZoneY + (ny / distance * LivingCoreLooks.ZoneRadiusY);
    }

    private static double Stage(double elapsed, double delay, double duration) =>
        LivingCoreEasings.Settle.Evaluate(Clamp01((elapsed - delay) / duration));

    /// <summary>Progress through one keyframe window, <paramref name="start"/> to <paramref name="end"/>.</summary>
    private static double Phase(double u, double start, double end, in CubicBezierEasing easing) =>
        easing.Evaluate(Clamp01((u - start) / (end - start)));

    private static double Direction(ThreadSpec thread) => thread.Clockwise ? 1.0 : -1.0;

    /// <summary>A two-beat loop: 0 at the start, 1 half way, eased each way.</summary>
    private static double Swing(double phase, in CubicBezierEasing easing)
    {
        var p = Frac(phase);
        return p < 0.5
            ? easing.Evaluate(p * 2.0)
            : 1.0 - easing.Evaluate((p - 0.5) * 2.0);
    }

    /// <summary>CSS-style keyframes on a loop: the easing applies to each segment.</summary>
    private static double Keyframes(double phase, double[] times, double[] values, in CubicBezierEasing easing) =>
        Timeline(Frac(phase), times, values, easing);

    /// <summary>CSS-style keyframes played once: <paramref name="u"/> is 0 to 1.</summary>
    private static double Timeline(double u, double[] times, double[] values, in CubicBezierEasing easing)
    {
        var p = Clamp01(u);
        for (var i = 1; i < times.Length; i++)
        {
            if (p <= times[i])
            {
                var span = times[i] - times[i - 1];
                var t = span <= 0.0 ? 1.0 : (p - times[i - 1]) / span;
                return Mix(values[i - 1], values[i], easing.Evaluate(t));
            }
        }

        return values[^1];
    }

    private static double Smooth(double current, double target, double dt, double timeConstant) =>
        dt <= 0.0 ? current : current + ((target - current) * (1.0 - Math.Exp(-dt / timeConstant)));

    private static double Mix(double a, double b, double t) => a + ((b - a) * t);

    private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);

    private static double Frac(double value) => value - Math.Floor(value);

    /// <summary>The Wake ignition's multipliers for one frame; all ones when not waking.</summary>
    private readonly record struct WakeFactors(
        double PupilScale,
        double PupilLight,
        double Rim,
        double LensScaleX,
        double LensLight,
        double InsideScale,
        double InsideLight,
        double HaloScale,
        double HaloLight,
        double Aura)
    {
        public static WakeFactors None { get; } = new(1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0);
    }
}
