namespace HAMMOR.App.Presence;

/// <summary>
/// What the app is doing right now, as plain facts. Several can be true at
/// once (a reply can be speaking while a background task is blocked);
/// <see cref="LivingCoreStateResolver"/> picks the one state that owns the
/// core.
/// </summary>
/// <param name="IsSpeaking">HAMMOR's speech is playing (or just finished).</param>
/// <param name="IsListening">
/// The user is addressing HAMMOR: typing in the focused composer. A future
/// microphone source sets the same flag; the visual state does not change.
/// </param>
/// <param name="IsThinking">A request is in flight and its reply has not arrived.</param>
/// <param name="IsBlocked">At least one task is waiting for approval.</param>
/// <param name="HasError">A failure the user has not acted on yet.</param>
/// <param name="IsConfirming">An approval prompt for a risky action is open.</param>
/// <param name="IsSucceeding">Something just finished: a task completed, or one was approved.</param>
/// <param name="IsResting">The window is minimised, or nothing has happened for ten minutes.</param>
/// <param name="IsWaking">The Wake ignition is playing.</param>
public readonly record struct LivingCoreSignals(
    bool IsSpeaking,
    bool IsListening,
    bool IsThinking,
    bool IsBlocked,
    bool HasError,
    bool IsConfirming = false,
    bool IsSucceeding = false,
    bool IsResting = false,
    bool IsWaking = false);

/// <summary>
/// Resolves signals to exactly one <see cref="LivingCoreState"/>. This is the
/// single owner of the white core: nothing else chooses where it looks.
/// </summary>
/// <remarks>
/// <para>
/// Ranking follows the approved "who moves the core" stack (Interaction board):
/// Speaking, Listening, Thinking, Waiting for approval, then rest, with the
/// states the stack does not name placed where their boards put them:
/// </para>
/// <list type="number">
/// <item>Wake: the ignition plays out before anything else shows.</item>
/// <item>Speaking.</item>
/// <item>Listening: "if you speak while it is blocked or in error, it listens first".</item>
/// <item>Warning: an approval prompt pauses the work, so it outranks Thinking.</item>
/// <item>Thinking.</item>
/// <item>Blocked.</item>
/// <item>Error.</item>
/// <item>Success: a moment of joy never covers something that needs the user.</item>
/// <item>Sleep: only when nothing at all is going on.</item>
/// <item>Idle.</item>
/// </list>
/// <para>
/// Error and Blocked only end when their signal clears, never on a timer.
/// </para>
/// </remarks>
public static class LivingCoreStateResolver
{
    public static LivingCoreState Resolve(LivingCoreSignals signals)
    {
        if (signals.IsWaking)
        {
            return LivingCoreState.Wake;
        }

        if (signals.IsSpeaking)
        {
            return LivingCoreState.Speaking;
        }

        if (signals.IsListening)
        {
            return LivingCoreState.Listening;
        }

        if (signals.IsConfirming)
        {
            return LivingCoreState.Warning;
        }

        if (signals.IsThinking)
        {
            return LivingCoreState.Thinking;
        }

        if (signals.IsBlocked)
        {
            return LivingCoreState.Blocked;
        }

        if (signals.HasError)
        {
            return LivingCoreState.Error;
        }

        if (signals.IsSucceeding)
        {
            return LivingCoreState.Success;
        }

        return signals.IsResting ? LivingCoreState.Sleep : LivingCoreState.Idle;
    }

    /// <summary>
    /// Leaving Sleep for anything but Sleep goes through Wake first: "you speak
    /// to it from sleep", a task needs it, the user comes back.
    /// </summary>
    public static bool NeedsWake(LivingCoreState current, LivingCoreState next) =>
        current == LivingCoreState.Sleep && next is not (LivingCoreState.Sleep or LivingCoreState.Wake);
}
