namespace HAMMOR.App.Presence;

/// <summary>
/// Everything the renderer needs for one frame. Allocated once per control
/// and overwritten in place every frame, so animating allocates nothing.
/// </summary>
/// <remarks>
/// Angles are degrees, clockwise. Positions and flows are design units.
/// Opacities are final values in 0 to 1.
/// </remarks>
public sealed class LivingCoreFrame
{
    // ---- Layer 0: aura ----
    public double AuraScale;
    public double AuraOpacity;

    // ---- Layer 1: energy lines ----
    public double EnergyOpacity;
    public double Energy1Angle;
    public double Energy2Angle;
    public double Energy1Flow;
    public double Energy2Flow;

    // ---- Layer 2: halo rings (index 0 = inner ring at r 56) ----
    public readonly double[] HaloScale = new double[3];
    public readonly double[] HaloOpacity = new double[3];

    /// <summary>Crossfade of the inner ring and membrane rim to the danger tone.</summary>
    public double RimDanger;

    /// <summary>Speaking rings leaving the halo.</summary>
    public readonly double[] OutwardScale = new double[3];
    public readonly double[] OutwardOpacity = new double[3];

    /// <summary>Listening rings drawing in with a real voice envelope.</summary>
    public readonly double[] InwardScale = new double[3];
    public readonly double[] InwardOpacity = new double[3];

    /// <summary>Blocked: the territory ring with its gap.</summary>
    public double GapRingOpacity;

    // ---- Layer 3: light threads ----
    public readonly double[] ThreadAngle = new double[4];
    public double ThreadsOpacity;
    public double ThreadsScale;

    /// <summary>Blocked: threads gathered at the top.</summary>
    public double ParkOpacity;

    /// <summary>Error: threads broken into slow fragments.</summary>
    public double FragmentOpacity;
    public double Fragment1Angle;
    public double Fragment2Angle;

    // ---- Layer 4: membrane ----
    public double CrackOpacity;

    // ---- Layer 5: deep stars ----
    public double StarsAngle;
    public double StarsScale;
    public readonly double[] StarClockOpacity = new double[4];

    // ---- Layer 6: floating cells ----
    public double CellsSpread;
    public double CellsOpacity;
    public double CellsAngle;
    public readonly double[] BobX = new double[3];
    public readonly double[] BobY = new double[3];
    public double LinkOpacity;
    public readonly double[] SignalX = new double[6];
    public readonly double[] SignalY = new double[6];
    public readonly double[] SignalOpacity = new double[6];
    public double OrbitOpacity;
    public double Orbit1Angle;
    public double Orbit2Angle;

    // ---- Layer 7: lens current ----
    public double LensOpacity;
    public double LensAngle;
    public double CrestAngle;
    public double LateralAOpacity;
    public double LateralBOpacity;
    public double LateralFlow;
    public double VoiceWaveOpacity;
    public double VoiceWaveScaleY;

    // ---- Layer 8: white core ----
    public double CoreX;
    public double CoreY;
    public double CoreScale;
    public double CoreOpacity;
    public double GlowOpacity;
    public double GlowScale;
    public double AttentionOpacity;
    public double SpeakRingOpacity;
}
