namespace HAMMOR.App.Presence;

/// <summary>
/// The white core's pose. First group of the state-change cascade
/// (0 to 0.45 s): its new position announces the state before anything else
/// moves.
/// </summary>
/// <remarks>All positions are in the 120-unit design box; centre (60, 60).</remarks>
public struct CoreLook
{
    /// <summary>White core centre, X.</summary>
    public double X;

    /// <summary>White core centre, Y.</summary>
    public double Y;

    /// <summary>1 = the 7-unit core of the Idle state.</summary>
    public double Scale;

    /// <summary>Core and glow opacity.</summary>
    public double Opacity;

    /// <summary>Glow intensity relative to Idle.</summary>
    public double Glow;

    /// <summary>Thin attention ring (Listening), 0 to 1.</summary>
    public double AttentionRing;

    /// <summary>Speech ring around the core (Speaking), 0 to 1.</summary>
    public double SpeakRing;

    /// <summary>Idle micro-drift strength, 0 to 1.</summary>
    public double Drift;

    public static CoreLook Lerp(in CoreLook a, in CoreLook b, double t) => new()
    {
        X = LookMath.Mix(a.X, b.X, t),
        Y = LookMath.Mix(a.Y, b.Y, t),
        Scale = LookMath.Mix(a.Scale, b.Scale, t),
        Opacity = LookMath.Mix(a.Opacity, b.Opacity, t),
        Glow = LookMath.Mix(a.Glow, b.Glow, t),
        AttentionRing = LookMath.Mix(a.AttentionRing, b.AttentionRing, t),
        SpeakRing = LookMath.Mix(a.SpeakRing, b.SpeakRing, t),
        Drift = LookMath.Mix(a.Drift, b.Drift, t),
    };
}

/// <summary>
/// Membrane interior: deep stars, floating cells and the lens current. Second
/// cascade group (0.2 to 0.8 s).
/// </summary>
public struct InsideLook
{
    public double StarOpacity;

    /// <summary>Star field scale about the centre (bloom on entrance).</summary>
    public double StarScale;

    /// <summary>Shimmer clock rate; 2 doubles the tempo.</summary>
    public double StarShimmerSpeed;

    /// <summary>Shimmer depth; 1 dips each star to 35%, 0 holds it steady.</summary>
    public double StarShimmerDepth;

    /// <summary>Field drift rate; 1 = one turn in 240 s.</summary>
    public double StarDriftSpeed;

    public double CellOpacity;

    /// <summary>Cell school scale about the lens centre; below 1 leans in.</summary>
    public double CellSpread;

    /// <summary>School drift rate; 1 = one turn in 140 s.</summary>
    public double CellDriftSpeed;

    /// <summary>Per-cell bob strength; 0 stops random drifting.</summary>
    public double CellBob;

    /// <summary>Processing links between cells (Thinking).</summary>
    public double LinkOpacity;

    /// <summary>Signals travelling along the links (Thinking).</summary>
    public double SignalOpacity;

    /// <summary>Orbit lanes (Thinking).</summary>
    public double OrbitOpacity;

    public double LensOpacity;

    /// <summary>Crest sway strength; 1 = ±1.4°.</summary>
    public double LensSway;

    /// <summary>Deliberate lens turn; 1 = ±5° (Thinking).</summary>
    public double LensPrecession;

    /// <summary>Outer lateral line opacity.</summary>
    public double LateralA;

    /// <summary>Inner lateral line opacity.</summary>
    public double LateralB;

    /// <summary>Lateral flow rate; 1 = one pattern cycle in 3.2 s.</summary>
    public double LateralFlowSpeed;

    /// <summary>Hairline split in the membrane (Error).</summary>
    public double CrackOpacity;

    /// <summary>Voice along the lower membrane (Speaking).</summary>
    public double VoiceWave;

    public static InsideLook Lerp(in InsideLook a, in InsideLook b, double t) => new()
    {
        StarOpacity = LookMath.Mix(a.StarOpacity, b.StarOpacity, t),
        StarScale = LookMath.Mix(a.StarScale, b.StarScale, t),
        StarShimmerSpeed = LookMath.Mix(a.StarShimmerSpeed, b.StarShimmerSpeed, t),
        StarShimmerDepth = LookMath.Mix(a.StarShimmerDepth, b.StarShimmerDepth, t),
        StarDriftSpeed = LookMath.Mix(a.StarDriftSpeed, b.StarDriftSpeed, t),
        CellOpacity = LookMath.Mix(a.CellOpacity, b.CellOpacity, t),
        CellSpread = LookMath.Mix(a.CellSpread, b.CellSpread, t),
        CellDriftSpeed = LookMath.Mix(a.CellDriftSpeed, b.CellDriftSpeed, t),
        CellBob = LookMath.Mix(a.CellBob, b.CellBob, t),
        LinkOpacity = LookMath.Mix(a.LinkOpacity, b.LinkOpacity, t),
        SignalOpacity = LookMath.Mix(a.SignalOpacity, b.SignalOpacity, t),
        OrbitOpacity = LookMath.Mix(a.OrbitOpacity, b.OrbitOpacity, t),
        LensOpacity = LookMath.Mix(a.LensOpacity, b.LensOpacity, t),
        LensSway = LookMath.Mix(a.LensSway, b.LensSway, t),
        LensPrecession = LookMath.Mix(a.LensPrecession, b.LensPrecession, t),
        LateralA = LookMath.Mix(a.LateralA, b.LateralA, t),
        LateralB = LookMath.Mix(a.LateralB, b.LateralB, t),
        LateralFlowSpeed = LookMath.Mix(a.LateralFlowSpeed, b.LateralFlowSpeed, t),
        CrackOpacity = LookMath.Mix(a.CrackOpacity, b.CrackOpacity, t),
        VoiceWave = LookMath.Mix(a.VoiceWave, b.VoiceWave, t),
    };
}

/// <summary>
/// Halo rings and light threads. Third cascade group (0.4 to 1.2 s).
/// </summary>
public struct HaloLook
{
    public double HaloOpacity;

    /// <summary>Halo and thread scale about the centre (arrival on entrance).</summary>
    public double HaloScale;

    /// <summary>Ring breathing strength; 1 = ±1.4% over 6.4 s.</summary>
    public double HaloBreath;

    /// <summary>Uneven inner-ring rhythm (Error), 0 to 1.</summary>
    public double HaloIrregular;

    /// <summary>Rim and inner ring shift from accent to the danger tone.</summary>
    public double RimDanger;

    public double ThreadOpacity;

    /// <summary>Thread 1 (r 54, 14 s) orbit rate.</summary>
    public double ThreadSpeed1;

    /// <summary>Thread 2 (r 58.5, 22 s) orbit rate.</summary>
    public double ThreadSpeed2;

    /// <summary>Thread 3 (r 63.5, 30 s) orbit rate.</summary>
    public double ThreadSpeed3;

    /// <summary>Thread 4 (r 70, 42 s) orbit rate.</summary>
    public double ThreadSpeed4;

    /// <summary>Thread brightening on emphasis (Speaking), 0 to 1.</summary>
    public double ThreadPulse;

    /// <summary>Threads gathered at the top where work paused (Blocked).</summary>
    public double ParkOpacity;

    /// <summary>Territory ring with a controlled gap (Blocked).</summary>
    public double GapRingOpacity;

    /// <summary>Threads broken into slow fragments (Error).</summary>
    public double FragmentOpacity;

    /// <summary>Rings pulsing outward (Speaking), 0 to 1.</summary>
    public double OutwardRings;

    /// <summary>
    /// Gain for rings drawing inward with the user's voice (Listening). Only
    /// visible when a real voice envelope is connected; never faked.
    /// </summary>
    public double InwardRings;

    public static HaloLook Lerp(in HaloLook a, in HaloLook b, double t) => new()
    {
        HaloOpacity = LookMath.Mix(a.HaloOpacity, b.HaloOpacity, t),
        HaloScale = LookMath.Mix(a.HaloScale, b.HaloScale, t),
        HaloBreath = LookMath.Mix(a.HaloBreath, b.HaloBreath, t),
        HaloIrregular = LookMath.Mix(a.HaloIrregular, b.HaloIrregular, t),
        RimDanger = LookMath.Mix(a.RimDanger, b.RimDanger, t),
        ThreadOpacity = LookMath.Mix(a.ThreadOpacity, b.ThreadOpacity, t),
        ThreadSpeed1 = LookMath.Mix(a.ThreadSpeed1, b.ThreadSpeed1, t),
        ThreadSpeed2 = LookMath.Mix(a.ThreadSpeed2, b.ThreadSpeed2, t),
        ThreadSpeed3 = LookMath.Mix(a.ThreadSpeed3, b.ThreadSpeed3, t),
        ThreadSpeed4 = LookMath.Mix(a.ThreadSpeed4, b.ThreadSpeed4, t),
        ThreadPulse = LookMath.Mix(a.ThreadPulse, b.ThreadPulse, t),
        ParkOpacity = LookMath.Mix(a.ParkOpacity, b.ParkOpacity, t),
        GapRingOpacity = LookMath.Mix(a.GapRingOpacity, b.GapRingOpacity, t),
        FragmentOpacity = LookMath.Mix(a.FragmentOpacity, b.FragmentOpacity, t),
        OutwardRings = LookMath.Mix(a.OutwardRings, b.OutwardRings, t),
        InwardRings = LookMath.Mix(a.InwardRings, b.InwardRings, t),
    };
}

/// <summary>
/// Ambient aura and energy lines. Last cascade group (0.8 to 1.6 s).
/// </summary>
public struct AuraLook
{
    public double AuraOpacity;

    /// <summary>Breathing strength; 1 = ±4% and 78 to 100% light over 8 s.</summary>
    public double AuraBreath;

    /// <summary>Pulse with speech (Speaking), 0 to 1.</summary>
    public double AuraPulse;

    public double EnergyOpacity;

    /// <summary>Energy line turn and flow rate; 1 = 90 s and 140 s turns.</summary>
    public double EnergySpeed;

    public static AuraLook Lerp(in AuraLook a, in AuraLook b, double t) => new()
    {
        AuraOpacity = LookMath.Mix(a.AuraOpacity, b.AuraOpacity, t),
        AuraBreath = LookMath.Mix(a.AuraBreath, b.AuraBreath, t),
        AuraPulse = LookMath.Mix(a.AuraPulse, b.AuraPulse, t),
        EnergyOpacity = LookMath.Mix(a.EnergyOpacity, b.EnergyOpacity, t),
        EnergySpeed = LookMath.Mix(a.EnergySpeed, b.EnergySpeed, t),
    };
}

/// <summary>
/// One complete look: the numbers a state is made of. States differ only in
/// these values, which is what lets a new state be added without touching the
/// engine or the renderer.
/// </summary>
public struct LivingCoreLook
{
    public CoreLook Core;
    public InsideLook Inside;
    public HaloLook Halo;
    public AuraLook Aura;

    /// <summary>
    /// Blends two looks with a separate progress per cascade group, so the
    /// white core can finish moving before the aura starts.
    /// </summary>
    public static LivingCoreLook Blend(in LivingCoreLook from, in LivingCoreLook to, in CascadeProgress progress) => new()
    {
        Core = CoreLook.Lerp(from.Core, to.Core, progress.Core),
        Inside = InsideLook.Lerp(from.Inside, to.Inside, progress.Inside),
        Halo = HaloLook.Lerp(from.Halo, to.Halo, progress.Halo),
        Aura = AuraLook.Lerp(from.Aura, to.Aura, progress.Aura),
    };
}

/// <summary>Eased progress of each cascade group, each 0 to 1.</summary>
public readonly record struct CascadeProgress(double Core, double Inside, double Halo, double Aura)
{
    public static CascadeProgress Complete { get; } = new(1.0, 1.0, 1.0, 1.0);

    public bool IsComplete => Core >= 1.0 && Inside >= 1.0 && Halo >= 1.0 && Aura >= 1.0;
}

internal static class LookMath
{
    public static double Mix(double a, double b, double t) => a + ((b - a) * t);
}
