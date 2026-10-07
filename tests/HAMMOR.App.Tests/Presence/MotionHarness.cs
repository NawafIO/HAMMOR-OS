using HAMMOR.App.Presence;

namespace HAMMOR.App.Tests.Presence;

/// <summary>Drives a <see cref="LivingCoreMotion"/> through simulated time.</summary>
internal static class MotionHarness
{
    public const double Fps = 60.0;

    /// <summary>
    /// Advances frame by frame from <paramref name="from"/> to
    /// <paramref name="to"/> inclusive, calling <paramref name="onFrame"/>
    /// after each frame.
    /// </summary>
    public static void Run(
        LivingCoreMotion motion,
        LivingCoreFrame frame,
        double from,
        double to,
        Action<double, LivingCoreFrame>? onFrame = null,
        double voice = double.NaN,
        double direction = -1.0)
    {
        var steps = (int)Math.Round((to - from) * Fps);
        for (var i = 0; i <= steps; i++)
        {
            var t = from + (i / Fps);
            motion.Advance(t, voice, direction, frame);
            onFrame?.Invoke(t, frame);
        }
    }

    /// <summary>The frame values that matter for comparing two runs.</summary>
    public static double[] Snapshot(LivingCoreFrame f) =>
    [
        f.CoreX, f.CoreY, f.CoreScale, f.CoreOpacity, f.GlowOpacity, f.GlowScale, f.AttentionOpacity, f.SpeakRingOpacity,
        f.AuraScale, f.AuraOpacity, f.EnergyOpacity, f.Energy1Angle, f.Energy2Angle, f.Energy1Flow, f.Energy2Flow,
        f.HaloScale[0], f.HaloScale[1], f.HaloScale[2], f.HaloOpacity[0], f.HaloOpacity[1], f.HaloOpacity[2], f.RimDanger,
        f.OutwardScale[0], f.OutwardOpacity[0], f.OutwardOpacity[1], f.OutwardOpacity[2], f.InwardOpacity[0], f.GapRingOpacity,
        f.ThreadAngle[0], f.ThreadAngle[1], f.ThreadAngle[2], f.ThreadAngle[3], f.ThreadsOpacity, f.ThreadsScale,
        f.ParkOpacity, f.FragmentOpacity, f.Fragment1Angle, f.CrackOpacity,
        f.StarsAngle, f.StarsScale, f.StarClockOpacity[0], f.StarClockOpacity[1], f.StarClockOpacity[2], f.StarClockOpacity[3],
        f.StarGroupX[0], f.StarGroupY[1], f.StarGroupX[2], f.StarGroupY[3],
        f.CellsAngle, f.CellsSpread, f.CellsOpacity, f.BobX[0], f.BobY[0], f.BobY[2], f.CellGlow[0], f.CellGlow[2], f.LinkOpacity,
        f.SignalX[0], f.SignalY[0], f.SignalOpacity[0], f.SignalOpacity[5], f.OrbitOpacity, f.Orbit1Angle,
        f.LensOpacity, f.LensAngle, f.CrestAngle, f.LateralAOpacity, f.LateralFlow, f.VoiceWaveOpacity, f.VoiceWaveScaleY,
    ];

    /// <summary>
    /// Where the white core sits in its zone: 0 at the zone centre, 1 on the
    /// zone's edge.
    /// </summary>
    public static double ZoneRadius(LivingCoreFrame f)
    {
        var nx = (f.CoreX - LivingCoreLooks.ZoneX) / LivingCoreLooks.ZoneRadiusX;
        var ny = (f.CoreY - LivingCoreLooks.ZoneY) / LivingCoreLooks.ZoneRadiusY;
        return Math.Sqrt((nx * nx) + (ny * ny));
    }

    public static double DistanceFromRest(LivingCoreFrame f)
    {
        var dx = f.CoreX - LivingCoreLooks.RestX;
        var dy = f.CoreY - LivingCoreLooks.RestY;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
