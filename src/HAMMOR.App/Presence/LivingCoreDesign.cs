namespace HAMMOR.App.Presence;

/// <summary>
/// Fixed geometry of the Living Core, transcribed from the approved hero board
/// (120-unit box, centre (60, 60); the drawing spans -40 to 160 so the aura
/// fits). Shared by the motion engine and the renderer so both agree on every
/// point.
/// </summary>
/// <remarks>
/// Budget (approved build board): 26 stars of at most 40, 14 cells of at most
/// 16, 4 threads, 3 halo rings.
/// </remarks>
internal static class LivingCoreDesign
{
    public const double Centre = 60.0;

    /// <summary>The full drawing box: -40 to 160 on both axes.</summary>
    public const double ViewMin = -40.0;

    public const double ViewSize = 200.0;

    public const double MembraneRadius = 50.0;

    /// <summary>Deep stars: x, y, radius, base opacity, shimmer clock (0-3).</summary>
    public static readonly StarSpec[] Stars =
    [
        new(27.8, 69.9, 0.65, 0.53, 1), new(72.8, 89.5, 0.75, 0.70, 0), new(44.0, 65.6, 0.75, 0.70, 0),
        new(36.6, 44.0, 0.65, 0.63, 1), new(30.6, 31.3, 0.35, 0.33, 1), new(85.3, 36.1, 0.65, 0.46, 1),
        new(24.3, 55.7, 0.65, 0.30, 0), new(104.9, 59.3, 0.35, 0.65, 2), new(61.6, 27.8, 0.35, 0.34, 0),
        new(26.4, 84.3, 0.65, 0.33, 0), new(60.6, 59.1, 0.45, 0.76, 0), new(16.2, 68.4, 0.65, 0.51, 1),
        new(64.2, 37.0, 0.35, 0.46, 0), new(98.2, 51.2, 0.35, 0.37, 0), new(90.6, 62.1, 0.65, 0.39, 1),
        new(58.2, 43.8, 0.65, 0.36, 3), new(89.2, 26.5, 0.75, 0.45, 0), new(96.2, 86.4, 0.45, 0.52, 0),
        new(56.5, 71.7, 0.35, 0.40, 1), new(48.4, 48.9, 0.75, 0.54, 1), new(72.9, 45.7, 0.45, 0.46, 1),
        new(78.6, 52.7, 0.65, 0.74, 0), new(102.9, 70.6, 0.45, 0.67, 3), new(58.2, 100.8, 0.75, 0.55, 1),
        new(100.2, 40.9, 0.45, 0.41, 0), new(56.4, 89.8, 0.35, 0.32, 2),
    ];

    /// <summary>Star shimmer clocks: period and CSS delay, seconds.</summary>
    public static readonly ClockSpec[] ShimmerClocks =
    [
        new(3.2, 0.0), new(4.4, -1.3), new(5.6, -2.2), new(7.0, -3.1),
    ];

    /// <summary>Floating cells: x, y, radius, tone, base opacity, bob clock (0-2).</summary>
    public static readonly CellSpec[] Cells =
    [
        new(51.9, 76.2, 1.9, CellTone.Spot, 0.95, 0),
        new(52.7, 49.8, 1.7, CellTone.Accent, 0.80, 1),
        new(32.6, 53.7, 1.1, CellTone.Accent, 0.70, 2),
        new(90.95, 71.8, 1.2, CellTone.Pearl, 0.55, 0),
        new(22.4, 79.3, 1.3, CellTone.Spot, 0.60, 1),
        new(75.65, 75.4, 1.4, CellTone.Accent, 0.80, 2),
        new(35.6, 67.4, 1.2, CellTone.Spot, 0.90, 0),
        new(70.7, 22.6, 1.3, CellTone.Accent, 0.45, 1),
        new(61.3, 95.2, 1.1, CellTone.Accent, 0.60, 2),
        new(41.8, 38.25, 1.4, CellTone.Spot, 0.50, 0),
        new(97.0, 48.8, 1.2, CellTone.Spot, 0.60, 1),
        new(90.1, 60.8, 1.0, CellTone.Accent, 0.85, 2),
        new(28.0, 40.0, 1.0, CellTone.Accent, 0.50, 0),
        new(84.0, 90.0, 1.2, CellTone.Pearl, 0.45, 1),
    ];

    /// <summary>Cell bob clocks: period and CSS delay, seconds.</summary>
    public static readonly ClockSpec[] BobClocks =
    [
        new(5.2, 0.0), new(6.6, -2.0), new(8.4, -4.0),
    ];

    /// <summary>Bob displacement at the top of each bob, design units.</summary>
    public const double BobX = 0.6;

    public const double BobY = -1.3;

    // ---- Idle life (Step 4) ----
    // Slow, smooth wanders on clocks that share no period with each other or
    // with the clocks above, so the inside never repeats in step. Sines only:
    // nothing accelerates sharply, overshoots or bounces.

    /// <summary>
    /// Star depth parallax, one wander per shimmer group: the groups drift
    /// against each other, so the field reads as depth rather than one
    /// turning disc.
    /// </summary>
    public static readonly WanderSpec[] StarParallax =
    [
        new(1.6, 1.2, 12.7, 17.3, 0.00, 0.31),
        new(2.2, 1.6, 10.1, 14.9, 0.42, 0.07),
        new(2.6, 1.9, 15.7, 9.3, 0.18, 0.66),
        new(1.9, 2.4, 19.1, 13.1, 0.73, 0.55),
    ];

    /// <summary>Cell wander, one per bob group, on top of the bob.</summary>
    public static readonly WanderSpec[] CellWander =
    [
        new(1.8, 1.2, 9.7, 12.3, 0.00, 0.25),
        new(1.4, 1.6, 11.9, 8.3, 0.50, 0.10),
        new(2.0, 1.0, 13.7, 10.9, 0.30, 0.80),
    ];

    /// <summary>Cell glow: each bob group dims and recovers on its own clock.</summary>
    public static readonly ClockSpec[] CellGlowClocks =
    [
        new(5.8, 0.0), new(7.4, -2.6), new(9.2, -5.1),
    ];

    /// <summary>How far a cell group dims at the bottom of its glow, 0 to 1.</summary>
    public const double CellGlowDepth = 0.22;

    /// <summary>
    /// White core idle drift: two slow sines per axis. Each axis stays under
    /// the hero board's 1.5% of the 120-unit box (1.8 units).
    /// </summary>
    public static readonly WanderSpec[] CoreDrift =
    [
        new(1.3, 0.8, 11.3, 8.9, 0.00, 0.17),
        new(0.4, 0.3, 4.7, 3.9, 0.20, 0.60),
    ];

    /// <summary>The hero board's limit on idle drift, per axis, design units.</summary>
    public const double CoreDriftLimit = 1.8;

    /// <summary>Thinking links between cells, with each signal's CSS delay.</summary>
    public static readonly LinkSpec[] Links =
    [
        new(41.8, 38.25, 52.7, 49.8, 0.0),
        new(52.7, 49.8, 90.1, 60.8, -0.4),
        new(35.6, 67.4, 51.9, 76.2, -0.8),
        new(51.9, 76.2, 75.65, 75.4, -1.2),
        new(32.6, 53.7, 35.6, 67.4, -0.6),
        new(75.65, 75.4, 84.0, 90.0, -1.0),
    ];

    /// <summary>Signal tempo: one 60.1-unit pattern every 1.6 s.</summary>
    public const double SignalPeriod = 1.6;

    public const double SignalTravel = 60.1;

    /// <summary>
    /// Light threads. Segments are arc-length spans measured clockwise from 3
    /// o'clock, exactly as the board's dash patterns place them; the head
    /// always sits on the leading edge of the orbit.
    /// </summary>
    public static readonly ThreadSpec[] Threads =
    [
        new(54.0, 14.0, true, 0.0, Tail: new(0, 40, 0.14, 0.7), Mid: new(18, 22, 0.40, 0.8), Head: new(32, 8, 0.95, 1.0), HeadIsWhite: true, GlowOpacity: 0.80, GlowWidth: 2.6),
        new(58.5, 22.0, false, -7.0, Tail: new(0, 56, 0.12, 0.6), Mid: new(0, 30, 0.36, 0.7), Head: new(0, 10, 0.90, 0.9), HeadIsWhite: true, GlowOpacity: 0.75, GlowWidth: 2.4),
        new(63.5, 30.0, true, -11.0, Tail: new(0, 34, 0.12, 0.6), Mid: new(16, 18, 0.36, 0.7), Head: new(27, 7, 0.85, 0.8), HeadIsWhite: true, GlowOpacity: 0.75, GlowWidth: 2.2),
        new(70.0, 42.0, false, -25.0, Tail: new(0, 70, 0.08, 0.6), Mid: new(0, 36, 0.20, 0.7), Head: new(0, 12, 0.50, 0.8), HeadIsWhite: false, GlowOpacity: 0.0, GlowWidth: 0.0),
    ];

    /// <summary>Halo ring breathing: period and per-ring delay (a third apart).</summary>
    public const double HaloPeriod = 6.4;

    public static readonly double[] HaloDelays = [0.0, -2.1, -4.2];

    /// <summary>Outer energy line (r 72, amplitude 1.6, 7 waves), x/y pairs.</summary>
    public static readonly double[] EnergyLine1 =
    [
        132.0, 60.0, 132.6, 66.4, 132.4, 72.8, 131.0, 79.0, 128.6, 85.0, 125.4, 90.5, 121.7, 95.6, 117.8, 100.5,
        113.9, 105.3, 110.1, 110.1, 106.1, 114.9, 101.7, 119.5, 96.7, 123.6, 91.1, 126.7, 85.0, 128.8, 78.7, 129.9,
        72.4, 130.4, 66.2, 130.4, 60.0, 130.4, 53.8, 130.4, 47.6, 130.4, 41.3, 129.9, 35.0, 128.8, 28.9, 126.7,
        23.3, 123.6, 18.3, 119.5, 13.9, 114.9, 9.9, 110.1, 6.1, 105.3, 2.2, 100.5, -1.7, 95.6, -5.4, 90.5,
        -8.6, 85.0, -11.0, 79.0, -12.4, 72.8, -12.6, 66.4, -12.0, 60.0, -10.8, 53.8, -9.4, 47.8, -8.1, 41.8,
        -6.7, 35.7, -5.1, 29.6, -3.0, 23.6, -0.2, 17.9, 3.6, 12.7, 8.3, 8.3, 13.5, 4.6, 19.1, 1.6,
        24.7, -1.2, 30.2, -3.8, 35.8, -6.5, 41.5, -9.1, 47.4, -11.4, 53.6, -13.0, 60.0, -13.6, 66.4, -13.0,
        72.6, -11.4, 78.5, -9.1, 84.2, -6.5, 89.8, -3.8, 95.3, -1.2, 100.9, 1.6, 106.5, 4.6, 111.7, 8.3,
        116.4, 12.7, 120.2, 17.9, 123.0, 23.6, 125.1, 29.6, 126.7, 35.7, 128.1, 41.8, 129.4, 47.8, 130.8, 53.8,
    ];

    /// <summary>Second energy line (r 80, amplitude 2.2, 5 waves), x/y pairs.</summary>
    public static readonly double[] EnergyLine2 =
    [
        141.4, 60.0, 141.7, 67.1, 141.0, 74.3, 139.2, 81.2, 136.5, 87.8, 133.0, 94.0, 128.9, 99.8, 124.5, 105.2,
        119.8, 110.2, 115.0, 115.0, 110.1, 119.7, 105.0, 124.3, 99.6, 128.6, 93.9, 132.7, 87.7, 136.2, 81.2, 139.0,
        74.3, 140.9, 67.2, 141.8, 60.0, 141.7, 52.9, 140.6, 46.1, 138.8, 39.5, 136.4, 33.2, 133.6, 27.1, 130.6,
        21.1, 127.4, 15.1, 124.1, 9.3, 120.4, 3.6, 116.4, -1.9, 111.9, -6.8, 106.8, -11.1, 101.0, -14.5, 94.7,
        -17.0, 88.0, -18.5, 81.0, -19.2, 74.0, -19.1, 66.9, -18.6, 60.0, -17.7, 53.2, -16.6, 46.5, -15.3, 39.8,
        -13.8, 33.1, -12.0, 26.4, -9.6, 19.8, -6.6, 13.4, -2.7, 7.4, 1.9, 1.9, 7.2, -2.9, 13.2, -6.8,
        19.6, -9.9, 26.3, -12.3, 33.0, -14.1, 39.8, -15.5, 46.5, -16.7, 53.2, -17.6, 60.0, -18.3, 66.9, -18.8,
        73.9, -18.8, 80.9, -18.2, 87.9, -16.8, 94.7, -14.4, 101.1, -11.2, 106.9, -7.0, 112.1, -2.1, 116.7, 3.3,
        120.7, 9.1, 124.3, 15.0, 127.5, 21.0, 130.5, 27.1, 133.4, 33.3, 136.1, 39.6, 138.4, 46.2, 140.3, 53.0,
    ];
}

/// <summary>A deep star.</summary>
internal readonly record struct StarSpec(double X, double Y, double Radius, double Opacity, int Clock);

/// <summary>A floating cell.</summary>
internal readonly record struct CellSpec(double X, double Y, double Radius, CellTone Tone, double Opacity, int Clock);

internal enum CellTone
{
    Accent,
    Spot,
    Pearl,
}

/// <summary>A periodic clock: period and CSS animation delay, seconds.</summary>
internal readonly record struct ClockSpec(double Period, double Delay);

/// <summary>
/// A smooth two-axis wander: <c>x = AmplitudeX · sin 2π(t / PeriodX + PhaseX)</c>,
/// and the same for y. Design units and seconds.
/// </summary>
internal readonly record struct WanderSpec(
    double AmplitudeX,
    double AmplitudeY,
    double PeriodX,
    double PeriodY,
    double PhaseX,
    double PhaseY)
{
    public double X(double time) => AmplitudeX * Math.Sin(2.0 * Math.PI * ((time / PeriodX) + PhaseX));

    public double Y(double time) => AmplitudeY * Math.Sin(2.0 * Math.PI * ((time / PeriodY) + PhaseY));
}

/// <summary>A Thinking link between two cell centres.</summary>
internal readonly record struct LinkSpec(double X1, double Y1, double X2, double Y2, double Delay)
{
    public double Length => Math.Sqrt(((X2 - X1) * (X2 - X1)) + ((Y2 - Y1) * (Y2 - Y1)));
}

/// <summary>One stroke of a thread: start and length along the arc, opacity, width.</summary>
internal readonly record struct ArcSpan(double Start, double Length, double Opacity, double Width);

/// <summary>A light thread: an arc with a fading tail and a head on its leading edge.</summary>
internal sealed record ThreadSpec(
    double Radius,
    double Period,
    bool Clockwise,
    double Delay,
    ArcSpan Tail,
    ArcSpan Mid,
    ArcSpan Head,
    bool HeadIsWhite,
    double GlowOpacity,
    double GlowWidth);
