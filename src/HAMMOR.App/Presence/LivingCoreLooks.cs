namespace HAMMOR.App.Presence;

/// <summary>
/// The resting values of the ten states, transcribed from the approved Living
/// Core prototype (the canvas's "Play with the core" board, recorded in the
/// reference video): its pose table and its state CSS.
/// </summary>
/// <remarks>
/// White core positions use the approved zone: centre (63, 60.5), ±19 units
/// sideways and ±8 up and down. Rest is (76, 57.5), exactly where the logo
/// puts the core. The prototype writes poses as fractions of the zone, so
/// Success at (0.68, −0.69) is <c>ZoneX + 0.68 × 19</c>, <c>ZoneY − 0.69 × 8</c>.
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

    /// <summary>Quietly alive. Every other state is written relative to this.</summary>
    public static readonly LivingCoreLook Idle = new()
    {
        X = RestX,
        Y = RestY,
        Scale = 1.0,
        Opacity = 1.0,
        Ember = 0.0,
        Glow = 1.0,
        BodyOpacity = 1.0,
        BodyScale = 1.0,
        BodyDrop = 0.0,
        ThreadsOpacity = 1.0,
        AuraOpacity = 1.0,
        StarsOpacity = 1.0,
        CellsScaleX = 1.0,
        CellsScaleY = 1.0,
        CellsDrop = 0.0,
        CellsOpacity = 1.0,
        EnergyOpacity = 1.0,
        LensOpacity = 1.0,
        LensScaleY = 1.0,
        CrestLift = 0.0,
        LateralAOpacity = 0.36,
        LateralBOpacity = 0.22,
    };

    /// <summary>Locked at the centre, facing you, at 112%; rings draw inward.</summary>
    public static readonly LivingCoreLook Listening = Idle with
    {
        X = ZoneX,
        Y = ZoneY,
        Scale = 1.12,
        CellsScaleX = 0.92,
        CellsScaleY = 0.92,
        LateralAOpacity = 0.95,
        LateralBOpacity = 0.95,
        InwardRings = 1.0,
        AttentionRing = 1.0,
    };

    /// <summary>Turned inward: up and back, 78%, at 55% light; links and orbits.</summary>
    public static readonly LivingCoreLook Thinking = Idle with
    {
        X = ZoneX - (0.35 * ZoneRadiusX),
        Y = ZoneY - (0.2 * ZoneRadiusY),
        Scale = 0.78,
        Opacity = 0.55,
        LensOpacity = 0.6,
        Links = 1.0,
        Orbits = 1.0,
    };

    /// <summary>Centred at 125%, pulsing with its own voice; rings pulse outward.</summary>
    public static readonly LivingCoreLook Speaking = Idle with
    {
        X = ZoneX,
        Y = ZoneY,
        Scale = 1.25,
        OutwardRings = 1.0,
        VoiceWave = 0.95,
        SpeakRing = 1.0,
    };

    /// <summary>Waits low; threads parked at the top, a gap in the outer ring. No alarm colour.</summary>
    public static readonly LivingCoreLook Blocked = Idle with
    {
        X = ZoneX + (0.16 * ZoneRadiusX),
        Y = ZoneY + (0.31 * ZoneRadiusY),
        Scale = 0.94,
        ThreadsOpacity = 0.0,
        AuraOpacity = 0.55,
        GapRing = 1.0,
        Park = 1.0,
    };

    /// <summary>Dimmed and staying with you: a crack, the danger rim, broken threads.</summary>
    public static readonly LivingCoreLook Error = Idle with
    {
        X = ZoneX + (0.68 * ZoneRadiusX),
        Y = ZoneY + (0.06 * ZoneRadiusY),
        Scale = 0.9,
        Opacity = 0.6,
        ThreadsOpacity = 0.0,
        AuraOpacity = 0.3,
        StarsOpacity = 0.3,
        CellsScaleX = 1.18,
        CellsScaleY = 1.18,
        CellsOpacity = 0.5,
        LensOpacity = 0.45,
        Fragments = 1.0,
        Crack = 1.0,
        RimDanger = 1.0,
    };

    /// <summary>Lifted and brighter at 108%: the warm bloom, one green ring, the success rim.</summary>
    public static readonly LivingCoreLook Success = Idle with
    {
        X = ZoneX + (0.68 * ZoneRadiusX),
        Y = ZoneY - (0.69 * ZoneRadiusY),
        Scale = 1.08,
        Warm = 1.0,
        SuccessRing = 1.0,
        RimSuccess = 1.0,
    };

    /// <summary>Held back and steady at 92%: the lens narrows, the crest lifts, one amber segment.</summary>
    public static readonly LivingCoreLook Warning = Idle with
    {
        X = ZoneX + (0.37 * ZoneRadiusX),
        Y = ZoneY - (0.125 * ZoneRadiusY),
        Scale = 0.92,
        AuraOpacity = 0.6,
        LensScaleY = 0.8,
        CrestLift = -4.0,
        WarningMark = 1.0,
        RimWarning = 1.0,
    };

    /// <summary>
    /// A dim ember, following nothing: the body sinks and shrinks, the cells
    /// settle to the bottom, threads stop, the aura and stars almost go out.
    /// </summary>
    public static readonly LivingCoreLook Sleep = Idle with
    {
        Ember = 1.0,
        Glow = 0.0,
        BodyOpacity = 0.6,
        BodyScale = 0.86,
        BodyDrop = 9.0,
        ThreadsOpacity = 0.0,
        AuraOpacity = 0.2,
        StarsOpacity = 0.15,
        CellsScaleY = 0.4,
        CellsDrop = 38.0,
        EnergyOpacity = 0.3,
    };

    /// <summary>What Wake settles into: Idle at rest. The ignition is the engine's 2.4 s animation set.</summary>
    public static readonly LivingCoreLook Wake = Idle;

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
}
