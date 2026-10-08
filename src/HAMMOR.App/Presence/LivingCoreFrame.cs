namespace HAMMOR.App.Presence;

/// <summary>
/// Everything the renderer needs for one frame. Allocated once per control
/// and overwritten in place every frame, so animating allocates nothing.
/// </summary>
/// <remarks>
/// Angles are degrees, clockwise. Positions, offsets and flows are design
/// units. Opacities are final values in 0 to 1.
/// </remarks>
public sealed class LivingCoreFrame
{
    // ---- Layer 0: aura ----
    public double AuraScale;
    public double AuraOpacity;

    /// <summary>The aura leans toward an opening panel, design units.</summary>
    public double AuraLeanX;

    /// <summary>Success: the aura blooms warm.</summary>
    public double WarmScale;
    public double WarmOpacity;

    // ---- Layer 1: energy lines ----
    public double EnergyOpacity;
    public double Energy1Angle;
    public double Energy2Angle;
    public double Energy1Flow;
    public double Energy2Flow;

    // ---- Rings outside the body ----

    /// <summary>Speaking rings leaving the halo.</summary>
    public readonly double[] OutwardScale = new double[3];
    public readonly double[] OutwardOpacity = new double[3];

    /// <summary>Listening rings drawing in toward the core.</summary>
    public readonly double[] InwardScale = new double[3];
    public readonly double[] InwardOpacity = new double[3];

    /// <summary>Success: one clean ring expands and dissolves.</summary>
    public double SuccessRingScale;
    public double SuccessRingOpacity;

    /// <summary>Blocked: the territory ring with its gap.</summary>
    public double GapRingOpacity;

    /// <summary>Warning: the amber segment that marks the concern.</summary>
    public double WarningMarkOpacity;

    // ---- The body: everything from the halo inward ----

    /// <summary>Sleep: the whole body sinks and shrinks.</summary>
    public double BodyScale;
    public double BodyY;
    public double BodyOpacity;

    // ---- Layer 2: halo rings (index 0 = inner ring at r 56) ----
    public readonly double[] HaloScale = new double[3];
    public readonly double[] HaloOpacity = new double[3];

    /// <summary>Crossfade of the inner ring and membrane rim to the danger tone.</summary>
    public double RimDanger;

    /// <summary>Crossfade of the inner ring and membrane rim to the success tone.</summary>
    public double RimSuccess;

    /// <summary>Crossfade of the inner ring and membrane rim to the warning tone.</summary>
    public double RimWarning;

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

    /// <summary>How much of the rim line is drawn, 0 to 1: Wake draws it around.</summary>
    public double RimDrawn;

    /// <summary>A new message: one soft ripple leaves the membrane.</summary>
    public double RippleScale;
    public double RippleOpacity;

    // ---- Layer 5: deep stars ----
    public double StarsAngle;
    public double StarsScale;
    public readonly double[] StarClockOpacity = new double[4];

    /// <summary>Depth: the stars slip 10% the other way when the core moves.</summary>
    public double StarsShiftX;
    public double StarsShiftY;

    // ---- Layer 6: floating cells ----
    public double CellsSpread;
    public double CellsScaleY;
    public double CellsY;
    public double CellsOpacity;
    public double CellsAngle;

    /// <summary>Depth: the cells move 30% with the core; a new message makes them shiver.</summary>
    public double CellsShiftX;
    public double CellsShiftY;

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
    public double LensScaleX;
    public double LensScaleY;
    public double CrestAngle;
    public double CrestY;
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

    /// <summary>Sleep: the warm ember drawn over the white.</summary>
    public double EmberOpacity;
    public double GlowOpacity;
    public double GlowScale;
    public double AttentionOpacity;
    public double SpeakRingOpacity;
}
