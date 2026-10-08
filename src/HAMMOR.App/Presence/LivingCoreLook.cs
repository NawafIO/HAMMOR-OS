namespace HAMMOR.App.Presence;

/// <summary>
/// One state's resting values, as the approved prototype's CSS writes them:
/// where the white core sits and how bright it is, and the base light and
/// shape of every layer. A state change moves each value to the next state's
/// with that element's own CSS transition, or at once where the prototype
/// has none; the looping and one-shot animations on top are
/// <see cref="LivingCoreMotion"/>'s.
/// </summary>
public readonly record struct LivingCoreLook
{
    // ---- The white core (prototype .pp, .ppk, .pcore, .pglow) ----

    /// <summary>White core position in design units.</summary>
    public double X { get; init; }

    /// <summary>White core position in design units.</summary>
    public double Y { get; init; }

    /// <summary>White core size: 1 is the logo's.</summary>
    public double Scale { get; init; }

    /// <summary>White core light.</summary>
    public double Opacity { get; init; }

    /// <summary>0 white, 1 the warm ember of Sleep.</summary>
    public double Ember { get; init; }

    /// <summary>The soft glow around the white core: on, or off in Sleep.</summary>
    public double Glow { get; init; }

    // ---- The body (.all) and its layers ----

    /// <summary>Light of everything from the halo inward (Sleep dims it).</summary>
    public double BodyOpacity { get; init; }

    /// <summary>
    /// The body's size: Sleep shrinks it to 86%. While Sleep's own breathing
    /// animation runs it shows instead; this resting value shows under
    /// reduced motion.
    /// </summary>
    public double BodyScale { get; init; }

    /// <summary>How far the body sinks, design units (Sleep: 9).</summary>
    public double BodyDrop { get; init; }

    public double ThreadsOpacity { get; init; }

    public double AuraOpacity { get; init; }

    public double StarsOpacity { get; init; }

    /// <summary>The cell school's shape: Listening draws it in, Error spreads it, Sleep lays it down.</summary>
    public double CellsScaleX { get; init; }

    public double CellsScaleY { get; init; }

    /// <summary>How far the cell school sinks, design units.</summary>
    public double CellsDrop { get; init; }

    public double CellsOpacity { get; init; }

    public double EnergyOpacity { get; init; }

    // ---- Lens current ----

    public double LensOpacity { get; init; }

    /// <summary>Warning narrows the lens to 80% of its height.</summary>
    public double LensScaleY { get; init; }

    /// <summary>Warning lifts the crest, design units (negative is up).</summary>
    public double CrestLift { get; init; }

    public double LateralAOpacity { get; init; }

    public double LateralBOpacity { get; init; }

    // ---- State marks: 0 or 1, faded in and out by their transition ----

    public double InwardRings { get; init; }

    public double OutwardRings { get; init; }

    public double GapRing { get; init; }

    public double Fragments { get; init; }

    public double Crack { get; init; }

    public double Links { get; init; }

    public double Orbits { get; init; }

    public double VoiceWave { get; init; }

    public double SpeakRing { get; init; }

    public double AttentionRing { get; init; }

    public double Warm { get; init; }

    public double SuccessRing { get; init; }

    public double WarningMark { get; init; }

    public double Park { get; init; }

    // ---- The rim and inner halo's tone (switches at once, as in the prototype) ----

    public double RimDanger { get; init; }

    public double RimSuccess { get; init; }

    public double RimWarning { get; init; }
}
