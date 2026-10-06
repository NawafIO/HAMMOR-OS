namespace HAMMOR.App.Presence;

/// <summary>
/// The visual states of the HAMMOR Living Core (approved Living Core canvas,
/// boards §04).
/// </summary>
/// <remarks>
/// <para>
/// A state describes how the core looks, never what the app is doing. The app
/// reports its own signals to <see cref="LivingCorePresenter"/>, which resolves
/// exactly one state; the control decides how that state is drawn.
/// </para>
/// <para>
/// P0 implements six states. Success, Warning, Sleep and Wake are designed on
/// the canvas and are added later by appending a value here, a look in
/// <see cref="LivingCoreLooks"/> and a rank in
/// <see cref="LivingCoreStateResolver"/>; the motion engine, cascade and
/// renderer need no change.
/// </para>
/// </remarks>
public enum LivingCoreState
{
    /// <summary>Quietly alive: nothing is asked of HAMMOR.</summary>
    Idle = 0,

    /// <summary>Attending to the user. In P0 the trigger is the composer.</summary>
    Listening = 1,

    /// <summary>A request is being worked on; the core turns inward.</summary>
    Thinking = 2,

    /// <summary>HAMMOR's speech is playing.</summary>
    Speaking = 3,

    /// <summary>A task is waiting for the user's approval. Not a failure.</summary>
    Blocked = 4,

    /// <summary>Something failed and needs the user.</summary>
    Error = 5,
}
