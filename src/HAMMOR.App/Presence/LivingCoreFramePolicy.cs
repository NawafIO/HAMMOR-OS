namespace HAMMOR.App.Presence;

/// <summary>What a Living Core knows when it decides whether, and how often, to draw.</summary>
/// <param name="IsLoaded">The control is in a live visual tree.</param>
/// <param name="IsVisible">The control and all its ancestors are visible.</param>
/// <param name="IsMinimized">Its window is minimised.</param>
/// <param name="IsWindowActive">Its window has the focus (true when it has no window).</param>
/// <param name="InactiveSeconds">How long the window has been in the background; 0 while active.</param>
/// <param name="Side">The control's drawn size in device-independent pixels.</param>
/// <param name="MotionNeedsFrames">The motion engine still changes the picture.</param>
/// <param name="IsResting">The core is settled in Sleep: only its slow breath moves.</param>
public readonly record struct LivingCoreFrameContext(
    bool IsLoaded,
    bool IsVisible,
    bool IsMinimized,
    bool IsWindowActive,
    double InactiveSeconds,
    double Side,
    bool MotionNeedsFrames,
    bool IsResting = false);

/// <summary>
/// The Living Core's frame budget. Pure, so it is tested without WPF.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>No frames at all while unloaded, hidden or minimised, or when the
/// motion is settled (reduced motion with a still state).</item>
/// <item>Up to 60 fps while the window is active, even on faster displays.</item>
/// <item>30 fps for a small core (the 112 px conversation header): at that
/// size a frame moves nothing by more than half a pixel.</item>
/// <item>30 fps while the window is in the background, then 20 fps after
/// <see cref="BackgroundAfterSeconds"/>.</item>
/// <item>10 fps in Sleep once settled (Guardrails board: "10 asleep"): a
/// 9.6 s breath of a few percent needs no more.</item>
/// </list>
/// </remarks>
public static class LivingCoreFramePolicy
{
    /// <summary>Frame interval while active: 60 fps.</summary>
    public const double ActiveInterval = 1.0 / 60.0;

    /// <summary>Frame interval for a small core, or a window recently sent to the background: 30 fps.</summary>
    public const double ReducedInterval = 1.0 / 30.0;

    /// <summary>Frame interval for a window left in the background: 20 fps.</summary>
    public const double BackgroundInterval = 1.0 / 20.0;

    /// <summary>Frame interval for a core settled in Sleep: 10 fps.</summary>
    public const double RestingInterval = 1.0 / 10.0;

    /// <summary>How long a window stays in the background before it drops to 20 fps.</summary>
    public const double BackgroundAfterSeconds = 45.0;

    /// <summary>Cores this size or smaller draw at 30 fps.</summary>
    public const double SmallSide = 160.0;

    /// <summary>
    /// Slack for display timing: frames arrive a little early or late, and a
    /// frame that is almost due is drawn. 3 ms keeps a 60 Hz display at every
    /// frame and draws every second frame at 120 Hz (60 fps) and 144 Hz
    /// (72 fps).
    /// </summary>
    public const double Tolerance = 0.003;

    /// <summary>Whether the control should be hooked to the per-frame callback at all.</summary>
    public static bool ShouldRender(in LivingCoreFrameContext context) =>
        context.IsLoaded
        && context.IsVisible
        && !context.IsMinimized
        && context.MotionNeedsFrames;

    /// <summary>The shortest time between two drawn frames.</summary>
    public static double MinimumInterval(in LivingCoreFrameContext context)
    {
        double interval;
        if (!context.IsWindowActive)
        {
            interval = context.InactiveSeconds >= BackgroundAfterSeconds ? BackgroundInterval : ReducedInterval;
        }
        else
        {
            interval = context.Side > 0.0 && context.Side <= SmallSide ? ReducedInterval : ActiveInterval;
        }

        return context.IsResting ? Math.Max(interval, RestingInterval) : interval;
    }

    /// <summary>Whether a frame is due <paramref name="sinceLast"/> seconds after the last one.</summary>
    public static bool IsFrameDue(double sinceLast, double interval) =>
        double.IsNaN(sinceLast) || sinceLast >= interval - Tolerance;
}
