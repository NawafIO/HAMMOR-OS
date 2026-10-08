namespace HAMMOR.App.Presence;

/// <summary>
/// The visual states of the HAMMOR Living Core: the ten states of the approved
/// Living Core canvas (state boards §04) and its prototype.
/// </summary>
/// <remarks>
/// A state describes how the core looks, never what the app is doing. The app
/// reports its own signals to <see cref="LivingCorePresenter"/>, which resolves
/// exactly one state; the control decides how that state is drawn.
/// </remarks>
public enum LivingCoreState
{
    /// <summary>Quietly alive: nothing is asked of HAMMOR.</summary>
    Idle = 0,

    /// <summary>Attending to the user. The trigger is typing in the composer.</summary>
    Listening = 1,

    /// <summary>A request is being worked on; the core turns inward.</summary>
    Thinking = 2,

    /// <summary>HAMMOR's speech is playing.</summary>
    Speaking = 3,

    /// <summary>A task is waiting for the user's approval. Not a failure.</summary>
    Blocked = 4,

    /// <summary>Something failed and needs the user.</summary>
    Error = 5,

    /// <summary>
    /// Something finished: a task completed, or the user approved one. Reaches
    /// its peak once, then falls back to Idle.
    /// </summary>
    Success = 6,

    /// <summary>
    /// A risky action needs the user's confirmation: attentive, not alarmed.
    /// </summary>
    Warning = 7,

    /// <summary>
    /// A dim warm ember: the window is minimised, or nothing has happened for
    /// ten minutes.
    /// </summary>
    Sleep = 8,

    /// <summary>
    /// The 2.4 s ignition: the app opens, the window returns, or something
    /// wakes it from Sleep. Then it hands over to the state underneath.
    /// </summary>
    Wake = 9,
}
