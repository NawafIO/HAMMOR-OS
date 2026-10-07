namespace HAMMOR.App.Presence;

/// <summary>
/// How Settings › Appearance names the Living Core's motion: full, or held
/// still because Windows asks for reduced motion.
/// </summary>
internal static class LivingCoreMotionStatus
{
    public const string FullKey = "Settings.Appearance.Motion.Full";

    public const string ReducedKey = "Settings.Appearance.Motion.Reduced";

    /// <summary>The string key for the current motion preference.</summary>
    public static string KeyFor(bool reducedMotion) => reducedMotion ? ReducedKey : FullKey;
}
