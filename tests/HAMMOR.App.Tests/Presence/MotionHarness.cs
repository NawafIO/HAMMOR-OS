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
        f.CoreX, f.CoreY, f.CoreScale, f.CoreOpacity, f.EmberOpacity, f.GlowOpacity, f.GlowScale, f.AttentionOpacity, f.SpeakRingOpacity,
        f.AuraScale, f.AuraOpacity, f.AuraLeanX, f.WarmScale, f.WarmOpacity,
        f.EnergyOpacity, f.Energy1Angle, f.Energy2Angle, f.Energy1Flow, f.Energy2Flow,
        f.SuccessRingScale, f.SuccessRingOpacity, f.WarningMarkOpacity, f.BodyScale, f.BodyY, f.BodyOpacity,
        f.HaloScale[0], f.HaloScale[1], f.HaloScale[2], f.HaloOpacity[0], f.HaloOpacity[1], f.HaloOpacity[2],
        f.RimDanger, f.RimSuccess, f.RimWarning, f.RimDrawn, f.RippleScale, f.RippleOpacity,
        f.OutwardScale[0], f.OutwardOpacity[0], f.OutwardOpacity[1], f.OutwardOpacity[2], f.InwardOpacity[0], f.GapRingOpacity,
        f.ThreadAngle[0], f.ThreadAngle[1], f.ThreadAngle[2], f.ThreadAngle[3], f.ThreadsOpacity, f.ThreadsScale,
        f.ParkOpacity, f.FragmentOpacity, f.Fragment1Angle, f.CrackOpacity,
        f.StarsAngle, f.StarsScale, f.StarsShiftX, f.StarsShiftY,
        f.StarClockOpacity[0], f.StarClockOpacity[1], f.StarClockOpacity[2], f.StarClockOpacity[3],
        f.CellsAngle, f.CellsSpread, f.CellsScaleY, f.CellsY, f.CellsOpacity, f.CellsShiftX, f.CellsShiftY,
        f.BobX[0], f.BobY[0], f.BobY[2], f.LinkOpacity,
        f.SignalX[0], f.SignalY[0], f.SignalOpacity[0], f.SignalOpacity[5], f.OrbitOpacity, f.Orbit1Angle,
        f.LensOpacity, f.LensAngle, f.LensScaleX, f.LensScaleY, f.CrestAngle, f.CrestY,
        f.LateralAOpacity, f.LateralFlow, f.VoiceWaveOpacity, f.VoiceWaveScaleY,
    ];

    /// <summary>
    /// Frame by frame from <paramref name="from"/> to <paramref name="to"/>
    /// inclusive: <paramref name="before"/> ahead of each Advance (to report
    /// the pointer or a reaction), <paramref name="after"/> with the frame it
    /// wrote.
    /// </summary>
    public static void Step(
        LivingCoreMotion motion,
        LivingCoreFrame frame,
        double from,
        double to,
        Action<double>? before = null,
        Action<double, LivingCoreFrame>? after = null,
        double direction = -1.0)
    {
        var steps = (int)Math.Round((to - from) * Fps);
        for (var i = 0; i <= steps; i++)
        {
            var t = from + (i / Fps);
            before?.Invoke(t);
            motion.Advance(t, double.NaN, direction, frame);
            after?.Invoke(t, frame);
        }
    }

    /// <summary>
    /// Where the white core sits in its zone: 0 at the zone centre, 1 on the
    /// zone's edge. A <paramref name="margin"/> (design units) grows the zone
    /// on every side.
    /// </summary>
    public static double ZoneRadius(LivingCoreFrame f, double margin = 0.0)
    {
        var nx = (f.CoreX - LivingCoreLooks.ZoneX) / (LivingCoreLooks.ZoneRadiusX + margin);
        var ny = (f.CoreY - LivingCoreLooks.ZoneY) / (LivingCoreLooks.ZoneRadiusY + margin);
        return Math.Sqrt((nx * nx) + (ny * ny));
    }

    public static double DistanceFromRest(LivingCoreFrame f)
    {
        var dx = f.CoreX - LivingCoreLooks.RestX;
        var dy = f.CoreY - LivingCoreLooks.RestY;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
