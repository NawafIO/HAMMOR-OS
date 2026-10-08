namespace HAMMOR.App.Presence;

/// <summary>
/// The approved look of every state, transcribed from the Living Core canvas
/// (state boards §04, interaction §05, awareness §06) and its prototype.
/// </summary>
/// <remarks>
/// <para>
/// White core positions use the approved zone: centre (63, 60.5), ±19 units
/// sideways and ±8 up and down. Rest is (76, 57.5), exactly where the logo
/// puts the core. The prototype writes poses as fractions of the zone, so
/// Success at (0.68, −0.69) is <c>ZoneX + 0.68 × 19</c>, <c>ZoneY − 0.69 × 8</c>.
/// </para>
/// <para>
/// Values are multipliers of the Idle choreography unless the field says
/// otherwise, so Idle is all ones and zeros. The one-shot parts of Success
/// and Wake (the warm bloom, the ring, the ignition) are timelines in
/// <see cref="LivingCoreMotion"/>; their looks hold what they settle to.
/// </para>
/// </remarks>
public static class LivingCoreLooks
{
    /// <summary>White core rest position (logo position).</summary>
    public const double RestX = 76.0;

    /// <summary>White core rest position (logo position).</summary>
    public const double RestY = 57.5;

    /// <summary>Centre of the white core's zone.</summary>
    public const double ZoneX = 63.0;

    /// <summary>Centre of the white core's zone.</summary>
    public const double ZoneY = 60.5;

    /// <summary>Half-width of the zone: ±16% of the core.</summary>
    public const double ZoneRadiusX = 19.0;

    /// <summary>Half-height of the zone: ±7% of the core.</summary>
    public const double ZoneRadiusY = 8.0;

    /// <summary>Quietly alive. Every other look is defined relative to this.</summary>
    public static readonly LivingCoreLook Idle = new()
    {
        Core = new CoreLook
        {
            X = RestX,
            Y = RestY,
            Scale = 1.0,
            Opacity = 1.0,
            Glow = 1.0,
            GlowSize = 1.0,
            AttentionRing = 0.0,
            SpeakRing = 0.0,
            Drift = 1.0,
            Ember = 0.0,
        },
        Inside = new InsideLook
        {
            StarOpacity = 1.0,
            StarScale = 1.0,
            StarShimmerSpeed = 1.0,
            StarShimmerDepth = 1.0,
            StarDriftSpeed = 1.0,
            CellOpacity = 1.0,
            CellSpread = 1.0,
            CellSquash = 1.0,
            CellNarrow = 1.0,
            CellDrop = 0.0,
            CellDriftSpeed = 1.0,
            CellBob = 1.0,
            LinkOpacity = 0.0,
            SignalOpacity = 0.0,
            OrbitOpacity = 0.0,
            LensOpacity = 1.0,
            LensSway = 1.0,
            LensPrecession = 0.0,
            LensNarrow = 1.0,
            CrestLift = 0.0,
            LateralA = 0.36,
            LateralB = 0.22,
            LateralFlowSpeed = 1.0,
            CrackOpacity = 0.0,
            VoiceWave = 0.0,
        },
        Halo = new HaloLook
        {
            HaloOpacity = 1.0,
            HaloScale = 1.0,
            HaloBreath = 1.0,
            HaloIrregular = 0.0,
            HaloTight = 0.0,
            Vox = 0.0,
            RimDanger = 0.0,
            RimSuccess = 0.0,
            RimWarning = 0.0,
            WarningMark = 0.0,
            ThreadOpacity = 1.0,
            ThreadSpeed1 = 1.0,
            ThreadSpeed2 = 1.0,
            ThreadSpeed3 = 1.0,
            ThreadSpeed4 = 1.0,
            ThreadPulse = 0.0,
            ParkOpacity = 0.0,
            GapRingOpacity = 0.0,
            FragmentOpacity = 0.0,
            OutwardRings = 0.0,
            InwardRings = 0.0,
            BodyScale = 1.0,
            BodyDrop = 0.0,
            BodyOpacity = 1.0,
            BodyBreath = 0.0,
        },
        Aura = new AuraLook
        {
            AuraOpacity = 1.0,
            AuraBreath = 1.0,
            AuraPulse = 0.0,
            EnergyOpacity = 1.0,
            EnergySpeed = 1.0,
        },
    };

    /// <summary>
    /// Centred and held at 112% with a thin attention ring; no drift, no
    /// glances. Halo rings draw inward and the inner ring moves with the
    /// user's input. Stars brighten and shimmer twice as fast, cells lean
    /// toward the core, lateral lines light up and flow faster, threads
    /// quicken.
    /// </summary>
    public static readonly LivingCoreLook Listening = Derive(Idle, look =>
    {
        look.Core.X = ZoneX;
        look.Core.Y = ZoneY;
        look.Core.Scale = 1.12;
        look.Core.AttentionRing = 1.0;
        look.Core.Drift = 0.0;
        look.Inside.StarOpacity = 1.15;
        look.Inside.StarShimmerSpeed = 2.0;
        look.Inside.CellSpread = 0.92;
        look.Inside.LateralA = 0.95;
        look.Inside.LateralB = 0.95;
        look.Inside.LateralFlowSpeed = 3.2 / 1.1;
        look.Halo.ThreadSpeed1 = 14.0 / 8.0;
        look.Halo.ThreadSpeed2 = 22.0 / 12.0;
        look.Halo.InwardRings = 1.0;
        look.Halo.Vox = 1.0;
        return look;
    });

    /// <summary>
    /// Turned inward to 78% and dimmed to 55%: thinking is private. Cells stop
    /// their random drift and link up, signals travel between them, orbit
    /// lanes appear, the lens turns deliberately. The aura holds steady.
    /// </summary>
    public static readonly LivingCoreLook Thinking = Derive(Idle, look =>
    {
        look.Core.X = ZoneX - (0.35 * ZoneRadiusX);
        look.Core.Y = ZoneY - (0.2 * ZoneRadiusY);
        look.Core.Scale = 0.78;
        look.Core.Opacity = 0.55;
        look.Core.Drift = 0.0;
        look.Inside.StarDriftSpeed = 240.0 / 60.0;
        look.Inside.CellBob = 0.0;
        look.Inside.LinkOpacity = 1.0;
        look.Inside.SignalOpacity = 1.0;
        look.Inside.OrbitOpacity = 1.0;
        look.Inside.LensOpacity = 0.6;
        look.Inside.LensSway = 0.0;
        look.Inside.LensPrecession = 1.0;
        look.Halo.ThreadSpeed1 = 14.0 / 8.0;
        look.Halo.ThreadSpeed2 = 22.0 / 12.0;
        look.Halo.ThreadSpeed3 = 30.0 / 16.0;
        look.Aura.AuraBreath = 0.0;
        return look;
    });

    /// <summary>
    /// Centred at 125% and pulsing with its own speech: the brightest point on
    /// the screen. Cells hold their places; the voice runs along the lower
    /// membrane; the halo rings leave their rest and pulse outward instead;
    /// threads brighten; the aura glows a step stronger.
    /// </summary>
    public static readonly LivingCoreLook Speaking = Derive(Idle, look =>
    {
        look.Core.X = ZoneX;
        look.Core.Y = ZoneY;
        look.Core.Scale = 1.25;
        look.Core.Glow = 1.15;
        look.Core.SpeakRing = 1.0;
        look.Core.Drift = 0.0;
        look.Inside.CellDriftSpeed = 0.0;
        look.Inside.CellBob = 0.0;
        look.Inside.VoiceWave = 1.0;
        look.Halo.HaloOpacity = 0.0;
        look.Halo.ThreadPulse = 1.0;
        look.Halo.OutwardRings = 1.0;
        look.Aura.AuraOpacity = 1.1;
        look.Aura.AuraBreath = 0.0;
        look.Aura.AuraPulse = 1.0;
        return look;
    });

    /// <summary>
    /// Waiting, not failing: one glance toward the request (driven by the
    /// engine), then low and centred. Inside slows to near stillness; threads
    /// gather at the top where work paused and the ring opens a gap there,
    /// while the rest of the halo thins to a hairline. Pearl, never an alarm
    /// colour.
    /// </summary>
    public static readonly LivingCoreLook Blocked = Derive(Idle, look =>
    {
        look.Core.X = ZoneX + (0.16 * ZoneRadiusX);
        look.Core.Y = ZoneY + (0.31 * ZoneRadiusY);
        look.Core.Scale = 0.94;
        look.Core.Glow = 0.9;
        look.Core.Drift = 0.0;
        look.Inside.StarShimmerSpeed = 0.5;
        look.Inside.StarShimmerDepth = 0.5;
        look.Inside.StarDriftSpeed = 0.25;
        look.Inside.CellDriftSpeed = 140.0 / 600.0;
        look.Inside.CellBob = 0.0;
        look.Inside.LensSway = 0.4;
        look.Inside.LateralFlowSpeed = 0.4;
        look.Halo.HaloOpacity = 0.55;
        look.Halo.HaloBreath = 0.5;
        look.Halo.ThreadOpacity = 0.0;
        look.Halo.ParkOpacity = 1.0;
        look.Halo.GapRingOpacity = 1.0;
        look.Aura.AuraOpacity = 0.55;
        look.Aura.AuraBreath = 0.5;
        look.Aura.EnergyOpacity = 0.8;
        look.Aura.EnergySpeed = 0.5;
        return look;
    });

    /// <summary>
    /// Dimmed to 60% and dropped slightly; it stays with the user. A hairline
    /// split opens in the membrane, cells drift out to the rim and dim, almost
    /// every star goes out, the halo breathes unevenly and threads break into
    /// slow fragments. No shake, no flash, no red flood.
    /// </summary>
    public static readonly LivingCoreLook Error = Derive(Idle, look =>
    {
        look.Core.X = ZoneX + (0.68 * ZoneRadiusX);
        look.Core.Y = ZoneY + (0.06 * ZoneRadiusY);
        look.Core.Scale = 0.9;
        look.Core.Opacity = 0.6;
        look.Core.Drift = 0.0;
        look.Inside.StarOpacity = 0.3;
        look.Inside.StarShimmerSpeed = 0.7;
        look.Inside.StarShimmerDepth = 0.5;
        look.Inside.StarDriftSpeed = 0.5;
        look.Inside.CellOpacity = 0.5;
        look.Inside.CellSpread = 1.18;
        look.Inside.CellDriftSpeed = 0.5;
        look.Inside.CellBob = 0.5;
        look.Inside.LensOpacity = 0.45;
        look.Inside.LensSway = 0.5;
        look.Inside.LateralA = 0.2;
        look.Inside.LateralB = 0.12;
        look.Inside.LateralFlowSpeed = 0.5;
        look.Inside.CrackOpacity = 1.0;
        look.Halo.HaloIrregular = 1.0;
        look.Halo.RimDanger = 1.0;
        look.Halo.ThreadOpacity = 0.0;
        look.Halo.FragmentOpacity = 1.0;
        look.Aura.AuraOpacity = 0.3;
        look.Aura.AuraBreath = 0.5;
        look.Aura.EnergyOpacity = 0.6;
        look.Aura.EnergySpeed = 0.6;
        return look;
    });

    /// <summary>
    /// The closest it comes to a smile: lifted up and forward to 108% and
    /// brighter, with the rim in the success tone. A few stars catch the
    /// light. The warm bloom, the ring that expands and dissolves, the cells
    /// rising together and one quick thread lap are the engine's one-shot
    /// timeline; then it settles back to rest.
    /// </summary>
    public static readonly LivingCoreLook Success = Derive(Idle, look =>
    {
        look.Core.X = ZoneX + (0.68 * ZoneRadiusX);
        look.Core.Y = ZoneY - (0.69 * ZoneRadiusY);
        look.Core.Scale = 1.08;
        look.Core.Glow = 1.2;
        look.Core.Drift = 0.0;
        look.Inside.StarOpacity = 1.15;
        look.Halo.RimSuccess = 1.0;
        return look;
    });

    /// <summary>
    /// Attentive, not alarmed: held slightly back at 92% and steady, its glow
    /// narrowed; no drift, no glances. The lens narrows and the crest rises;
    /// cells slow almost to a stop. The halo tightens and holds, one amber
    /// segment marks the concern and pulses every 3.2 s, threads slow to half
    /// speed, the aura dims.
    /// </summary>
    public static readonly LivingCoreLook Warning = Derive(Idle, look =>
    {
        look.Core.X = ZoneX + (0.37 * ZoneRadiusX);
        look.Core.Y = ZoneY - (0.125 * ZoneRadiusY);
        look.Core.Scale = 0.92;
        look.Core.GlowSize = 0.8;
        look.Core.Drift = 0.0;
        look.Inside.CellDriftSpeed = 140.0 / 280.0;
        look.Inside.CellBob = 0.0;
        look.Inside.LensNarrow = 0.8;
        look.Inside.CrestLift = -4.0;
        look.Inside.LensSway = 0.0;
        look.Halo.HaloTight = 1.0;
        look.Halo.RimWarning = 1.0;
        look.Halo.WarningMark = 1.0;
        look.Halo.ThreadSpeed1 = 14.0 / 28.0;
        look.Halo.ThreadSpeed2 = 22.0 / 44.0;
        look.Halo.ThreadSpeed3 = 30.0 / 60.0;
        look.Halo.ThreadSpeed4 = 0.5;
        look.Aura.AuraOpacity = 0.6;
        return look;
    });

    /// <summary>
    /// A dim warm ember instead of white, following nothing. The whole body
    /// sinks 9 units and shrinks to 86% at 60% light, barely breathing over
    /// 9.6 s. Cells settle to the bottom like sediment, the stars are almost
    /// out, the lens dims to an outline. Threads stop, the aura is almost off.
    /// </summary>
    /// <remarks>
    /// The sediment uses the prototype's move (the school drops and flattens)
    /// laid out as state board 09 draws it: a low band inside the bottom of
    /// the membrane. The prototype's own 38-unit drop at 40% height would put
    /// the lowest cells past the rim.
    /// </remarks>
    public static readonly LivingCoreLook Sleep = Derive(Idle, look =>
    {
        look.Core.Glow = 0.0;
        look.Core.Drift = 0.0;
        look.Core.Ember = 1.0;
        look.Inside.StarOpacity = 0.15;
        look.Inside.StarShimmerSpeed = 0.5;
        look.Inside.StarShimmerDepth = 0.5;
        look.Inside.StarDriftSpeed = 0.25;
        look.Inside.CellSquash = 0.2;
        look.Inside.CellNarrow = 0.5;
        look.Inside.CellDrop = 37.0;
        look.Inside.CellDriftSpeed = 0.0;
        look.Inside.CellBob = 0.0;
        look.Inside.LensOpacity = 0.55;
        look.Inside.LensSway = 0.3;
        look.Inside.LateralA = 0.1;
        look.Inside.LateralB = 0.06;
        look.Inside.LateralFlowSpeed = 0.3;
        look.Halo.HaloBreath = 0.5;
        look.Halo.ThreadOpacity = 0.0;
        look.Halo.BodyScale = 0.86;
        look.Halo.BodyDrop = 9.0;
        look.Halo.BodyOpacity = 0.6;
        look.Halo.BodyBreath = 1.0;
        look.Aura.AuraOpacity = 0.2;
        look.Aura.AuraBreath = 0.4;
        look.Aura.EnergyOpacity = 0.3;
        look.Aura.EnergySpeed = 0.3;
        return look;
    });

    /// <summary>
    /// What Wake settles into: Idle at rest, without drift while it ignites.
    /// The ignition itself (core first, rim, lens, cells and stars, then halo,
    /// threads and aura) is the engine's 2.4 s timeline.
    /// </summary>
    public static readonly LivingCoreLook Wake = Derive(Idle, look =>
    {
        look.Core.Drift = 0.0;
        return look;
    });

    public static LivingCoreLook For(LivingCoreState state) => state switch
    {
        LivingCoreState.Listening => Listening,
        LivingCoreState.Thinking => Thinking,
        LivingCoreState.Speaking => Speaking,
        LivingCoreState.Blocked => Blocked,
        LivingCoreState.Error => Error,
        LivingCoreState.Success => Success,
        LivingCoreState.Warning => Warning,
        LivingCoreState.Sleep => Sleep,
        LivingCoreState.Wake => Wake,
        _ => Idle,
    };

    private static LivingCoreLook Derive(LivingCoreLook baseLook, Func<LivingCoreLook, LivingCoreLook> change) =>
        change(baseLook);
}
