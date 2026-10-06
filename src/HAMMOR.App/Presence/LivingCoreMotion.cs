namespace HAMMOR.App.Presence;

/// <summary>
/// The Living Core's motion engine: turns a state and the passing of time into
/// a <see cref="LivingCoreFrame"/>. Pure arithmetic, no WPF, deterministic:
/// the same states at the same times always produce the same frames.
/// </summary>
/// <remarks>
/// <para>
/// <b>State changes</b> blend the previous look into the next one with the
/// approved 1.6 s cascade: white core 0 to 0.45 s, cells and stars 0.2 to
/// 0.8 s, halo and threads 0.4 to 1.2 s, aura and energy 0.8 to 1.6 s, all on
/// ease.settle. A change during a change starts from what is on screen, so
/// nothing ever jumps.
/// </para>
/// <para>
/// <b>Idle</b> runs nine clocks that never synchronise (Motion board): aura
/// 8 s, energy lines 90 and 140 s, halo rings 6.4 s a third apart, threads 14,
/// 22, 30 and 42 s in alternating directions, stars 240 s with four shimmer
/// clocks, cells 140 s with three bob clocks, lens sway 6.4 s and flow 3.2 s,
/// white core drift 9.6 s with one unhurried glance about every 14 s.
/// </para>
/// <para>
/// <b>The white core has one owner</b>: this class. Its position is the
/// state's pose plus, only where the state allows it, the idle drift, the
/// idle glance or the Blocked request glance. Nothing else writes it.
/// </para>
/// <para>
/// <b>Reduced motion</b> keeps every state's position, shape and light, and
/// drops drift, orbit, shimmer and breathing. Pulses become a slow change of
/// light; state changes become a short blend.
/// </para>
/// </remarks>
public sealed class LivingCoreMotion
{
    /// <summary>Length of the full state-change cascade.</summary>
    public const double CascadeSeconds = 1.6;

    /// <summary>Length of a state change under reduced motion.</summary>
    public const double ReducedTransitionSeconds = 0.4;

    // A frame never advances the clocks by more than this, so resuming after
    // a pause (minimised, hidden, debugger) continues instead of jumping.
    private const double MaxStep = 0.1;

    private const double GateTimeConstant = 0.15;

    // "Smoothed over 120 ms so they never jitter" (state board, Listening).
    private const double EnvelopeTimeConstant = 0.12;

    private const double GlanceOut = 0.45;
    private const double GlanceBack = 1.2;
    private const double FirstGlanceDelay = 4.2;
    private const double RequestGlanceDelay = 0.5;
    private const double RequestGlanceHold = 0.9;
    private const double RequestGlanceDx = 9.0;
    private const double RequestGlanceDy = -2.0;

    private static readonly CubicBezierEasing Linear = new(0.0, 0.0, 1.0, 1.0);

    // Idle micro-drift, under 1.5% (hero board).
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

    // Error: the inner ring breathes off the 0.8 s grid.
    private static readonly double[] StutterTimes = [0.0, 0.17, 0.23, 0.49, 0.58, 0.83, 1.0];
    private static readonly double[] StutterScale = [1.0, 1.01, 1.004, 1.012, 1.0, 1.006, 1.0];
    private static readonly double[] StutterLight = [0.55, 0.3, 0.5, 0.25, 0.5, 0.32, 0.55];

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

    /// <param name="initialState">State to show.</param>
    /// <param name="now">Current time, seconds, from a monotonic clock.</param>
    /// <param name="fromDormant">
    /// Arrive through the cascade from <see cref="LivingCoreLooks.Dormant"/>
    /// (core first, aura last) instead of appearing fully formed.
    /// </param>
    /// <param name="reducedMotion">Start in reduced motion.</param>
    public LivingCoreMotion(LivingCoreState initialState, double now, bool fromDormant, bool reducedMotion = false)
    {
        State = initialState;
        ReducedMotion = reducedMotion;
        _to = LivingCoreLooks.For(initialState);
        _from = fromDormant ? LivingCoreLooks.Dormant : _to;
        _transitionStart = now;
        _transitionDone = !fromDormant;

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

        EnterBehaviours(initialState, now);
    }

    /// <summary>The state currently shown (or being blended toward).</summary>
    public LivingCoreState State { get; private set; }

    /// <summary>Honour the system's reduced-motion setting.</summary>
    public bool ReducedMotion { get; set; }

    private double TransitionLength => ReducedMotion ? ReducedTransitionSeconds : CascadeSeconds;

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

    /// <summary>
    /// Whether the renderer has to keep producing frames. False only under
    /// reduced motion once a still state has settled, so a still core costs
    /// nothing. Speaking and Blocked keep their slow change of light;
    /// Listening changes only with a real voice level.
    /// </summary>
    public bool NeedsContinuousFrames(double now) =>
        !ReducedMotion
        || IsTransitioning(now)
        || State is LivingCoreState.Speaking or LivingCoreState.Blocked
        || (State == LivingCoreState.Listening && _hasVoiceLevel);

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
    /// rhythm and Listening shows no voice rings.
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

        if (!reduced)
        {
            AdvanceClocks(look, dt);
        }

        WriteOutside(look, now, reduced, hasVoice, frame);
        WriteInside(look, now, reduced, hasVoice, frame);
        WriteCore(look, now, dt, reduced, hasVoice, requestDirection, frame);
    }

    private void EnterBehaviours(LivingCoreState state, double now)
    {
        if (state == LivingCoreState.Idle)
        {
            _nextGlanceAt = now + FirstGlanceDelay;
        }

        // Blocked glances once toward the request, then waits.
        _requestGlanceStart = state == LivingCoreState.Blocked && !ReducedMotion
            ? now + RequestGlanceDelay
            : double.NaN;
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

    private void WriteOutside(in LivingCoreLook look, double now, bool reduced, bool hasVoice, LivingCoreFrame frame)
    {
        // ---- Aura: breathes 4% over 8 s; pulses with speech ----
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
            * Mix(1.0, pulseLight, look.Aura.AuraPulse));

        // ---- Energy lines ----
        frame.EnergyOpacity = Clamp01(look.Aura.EnergyOpacity);
        frame.Energy1Angle = _energy1Angle;
        frame.Energy2Angle = _energy2Angle;
        frame.Energy1Flow = _energy1Flow;
        frame.Energy2Flow = _energy2Flow;

        // ---- Halo rings: ±1.4% over 6.4 s, a third apart ----
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

            frame.HaloScale[i] = scale * look.Halo.HaloScale;
            frame.HaloOpacity[i] = Clamp01(look.Halo.HaloOpacity * light);
        }

        frame.RimDanger = Clamp01(look.Halo.RimDanger);

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

        // ---- Listening: rings draw inward, only with a real voice ----
        var inwardGain = look.Halo.InwardRings * _envelope;
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

        // ---- Threads ----
        for (var i = 0; i < 4; i++)
        {
            frame.ThreadAngle[i] = _threadAngle[i];
        }

        var threadLight = 1.0;
        if (look.Halo.ThreadPulse > 0.0 && !reduced)
        {
            var pulse = hasVoice
                ? 0.7 + (0.3 * _envelope)
                : Keyframes(now / 1.6, ThreadPulseTimes, ThreadPulseLight, LivingCoreEasings.Breath);
            threadLight = Mix(1.0, pulse, look.Halo.ThreadPulse);
        }

        frame.ThreadsOpacity = Clamp01(look.Halo.ThreadOpacity * threadLight);
        frame.ThreadsScale = look.Halo.HaloScale;

        // Blocked: one slow pulse of light at the paused threads (kept under
        // reduced motion: a slow change of light, never a flash).
        frame.ParkOpacity = Clamp01(look.Halo.ParkOpacity * (0.45 + (0.55 * Swing(now / 3.2, LivingCoreEasings.Breath))));

        frame.FragmentOpacity = Clamp01(look.Halo.FragmentOpacity);
        frame.Fragment1Angle = _fragment1Angle;
        frame.Fragment2Angle = _fragment2Angle;
    }

    private void WriteInside(in LivingCoreLook look, double now, bool reduced, bool hasVoice, LivingCoreFrame frame)
    {
        frame.CrackOpacity = Clamp01(look.Inside.CrackOpacity);

        // ---- Deep stars: drift as a field, shimmer on four clocks ----
        frame.StarsAngle = _starsAngle;
        frame.StarsScale = look.Inside.StarScale;
        for (var k = 0; k < 4; k++)
        {
            var dip = reduced ? 0.0 : Swing(_shimmerPhase[k], LivingCoreEasings.InOut);
            var shimmer = 1.0 - (look.Inside.StarShimmerDepth * 0.65 * dip);
            frame.StarClockOpacity[k] = Clamp01(look.Inside.StarOpacity * shimmer);
        }

        // ---- Floating cells: drift as a school, bob on three clocks ----
        frame.CellsSpread = look.Inside.CellSpread;
        frame.CellsOpacity = Clamp01(look.Inside.CellOpacity);
        frame.CellsAngle = _cellsAngle;
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

        // ---- Lens current ----
        frame.LensOpacity = Clamp01(look.Inside.LensOpacity);
        frame.LensAngle = reduced ? 0.0 : look.Inside.LensPrecession * (-5.0 + (10.0 * Swing(now / 4.8, LivingCoreEasings.Breath)));
        frame.CrestAngle = reduced ? 0.0 : look.Inside.LensSway * (-1.4 + (2.8 * Swing(now / 6.4, LivingCoreEasings.Breath)));
        frame.LateralAOpacity = Clamp01(look.Inside.LateralA);
        frame.LateralBOpacity = Clamp01(look.Inside.LateralB);
        frame.LateralFlow = _lateralFlow;
        frame.VoiceWaveOpacity = Clamp01(look.Inside.VoiceWave * 0.95);
        frame.VoiceWaveScaleY = reduced
            ? 1.0
            : hasVoice
                ? 0.4 + _envelope
                : Keyframes(now / 1.6, WaveTimes, WaveScale, LivingCoreEasings.Breath);
    }

    private void WriteCore(
        in LivingCoreLook look,
        double now,
        double dt,
        bool reduced,
        bool hasVoice,
        double requestDirection,
        LivingCoreFrame frame)
    {
        var x = look.Core.X;
        var y = look.Core.Y;

        // Idle micro-drift.
        if (!reduced && look.Core.Drift > 0.0)
        {
            var phase = now / 9.6;
            x += Keyframes(phase, DriftTimes, DriftX, LivingCoreEasings.InOut) * look.Core.Drift;
            y += Keyframes(phase, DriftTimes, DriftY, LivingCoreEasings.InOut) * look.Core.Drift;
        }

        // Idle glance: out 0.45 s, hold, back 1.2 s. Gated so leaving Idle
        // fades a glance in progress instead of cutting it.
        var idle = State == LivingCoreState.Idle && !reduced;
        _glanceGate = Smooth(_glanceGate, idle ? 1.0 : 0.0, dt, GateTimeConstant);
        if (reduced)
        {
            _glanceStart = double.NaN;
        }
        else
        {
            if (idle && double.IsNaN(_glanceStart) && now >= _nextGlanceAt)
            {
                _glanceStart = now;
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
                    x += GlanceDx[slot] * amount * _glanceGate;
                    y += GlanceDy[slot] * amount * _glanceGate;
                }
            }
        }

        // Blocked: the single glance toward the request.
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

        // Speaking: pulse with the speech envelope, or the designed rhythm.
        var speakWeight = Clamp01(look.Core.SpeakRing);
        var pulse = 1.0;
        if (speakWeight > 0.0 && !reduced)
        {
            var raw = hasVoice
                ? 1.0 + (0.176 * _envelope)
                : Keyframes(now / 1.6, PulseTimes, PulseScale, LivingCoreEasings.Breath);
            pulse = Mix(1.0, raw, speakWeight);
        }

        frame.CoreScale = look.Core.Scale * pulse;
        frame.CoreOpacity = Clamp01(look.Core.Opacity);

        var glow = reduced ? 0.5 : Swing(now / 4.8, LivingCoreEasings.InOut);
        var glowLight = 0.9 + (0.1 * glow);
        if (speakWeight > 0.0 && reduced)
        {
            // Reduced motion: the speaking pulse becomes a slow change of light.
            glowLight = Mix(glowLight, 0.8 + (0.2 * Swing(now / 2.4, LivingCoreEasings.Breath)), speakWeight);
        }

        frame.GlowOpacity = Clamp01(look.Core.Opacity * Math.Min(1.0, look.Core.Glow) * glowLight);
        frame.GlowScale = (1.0 + (0.1 * glow)) * Math.Max(1.0, look.Core.Glow);
        // The attention ring breathes; reduced motion drops breathing and
        // holds it at full.
        var attention = reduced ? 1.0 : 0.55 + (0.45 * Swing(now / 3.2, LivingCoreEasings.Breath));
        frame.AttentionOpacity = Clamp01(look.Core.AttentionRing * attention);
        frame.SpeakRingOpacity = Clamp01(look.Core.SpeakRing * look.Core.Opacity);
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

    private static double Direction(ThreadSpec thread) => thread.Clockwise ? 1.0 : -1.0;

    /// <summary>A two-beat loop: 0 at the start, 1 half way, eased each way.</summary>
    private static double Swing(double phase, in CubicBezierEasing easing)
    {
        var p = Frac(phase);
        return p < 0.5
            ? easing.Evaluate(p * 2.0)
            : 1.0 - easing.Evaluate((p - 0.5) * 2.0);
    }

    /// <summary>CSS-style keyframes: the easing applies to each segment.</summary>
    private static double Keyframes(double phase, double[] times, double[] values, in CubicBezierEasing easing)
    {
        var p = Frac(phase);
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
}
