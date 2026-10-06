namespace HAMMOR.App.Presence;

/// <summary>
/// What the app is doing right now, as plain facts. Several can be true at
/// once (a reply can be speaking while a background task is blocked);
/// <see cref="LivingCoreStateResolver"/> picks the one state that owns the
/// core.
/// </summary>
/// <param name="IsSpeaking">HAMMOR's speech is playing (or just finished).</param>
/// <param name="IsListening">
/// The user is addressing HAMMOR. P0: typing in the focused composer. Later a
/// microphone source sets the same flag; the visual state does not change.
/// </param>
/// <param name="IsThinking">A request is in flight.</param>
/// <param name="IsBlocked">At least one task is waiting for approval.</param>
/// <param name="HasError">A failure the user has not acted on yet.</param>
public readonly record struct LivingCoreSignals(
    bool IsSpeaking,
    bool IsListening,
    bool IsThinking,
    bool IsBlocked,
    bool HasError);

/// <summary>
/// Resolves signals to exactly one <see cref="LivingCoreState"/>. This is the
/// single owner of the white core: nothing else chooses where it looks.
/// </summary>
/// <remarks>
/// Ranking follows the approved "who moves the core" stack (Interaction board):
/// Speaking, Listening, Thinking, Waiting for approval, then rest. Error sits
/// below Blocked and above rest. The collision rules hold by construction:
/// "if you speak while it is blocked or in error, it listens first", and Error
/// and Blocked only end when their signal clears, never on a timer.
/// </remarks>
public static class LivingCoreStateResolver
{
    public static LivingCoreState Resolve(LivingCoreSignals signals)
    {
        if (signals.IsSpeaking)
        {
            return LivingCoreState.Speaking;
        }

        if (signals.IsListening)
        {
            return LivingCoreState.Listening;
        }

        if (signals.IsThinking)
        {
            return LivingCoreState.Thinking;
        }

        if (signals.IsBlocked)
        {
            return LivingCoreState.Blocked;
        }

        return signals.HasError ? LivingCoreState.Error : LivingCoreState.Idle;
    }
}
