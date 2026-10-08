namespace HAMMOR.App.Presence;

/// <summary>Something that happened, which the Living Core reacts to without changing state.</summary>
public enum LivingCoreReactionKind
{
    /// <summary>
    /// A reply arrived: one soft ripple leaves the membrane and, where the
    /// state allows it, the white core glances toward the message for 1.2 s.
    /// </summary>
    NewMessage = 0,

    /// <summary>
    /// A panel opened: the aura leans its way for 2.4 s and, where the state
    /// allows it, the white core glances toward it.
    /// </summary>
    PanelOpened = 1,
}

/// <summary>
/// One reaction for the Living Core. Each instance is a new event, so binding
/// a fresh one fires again even when the kind repeats.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="Direction">
/// Where it happened, in reading order: negative is the leading side (where
/// the navigation pane is), positive the trailing side.
/// </param>
/// <param name="Sequence">Increases with every reaction.</param>
public sealed record LivingCoreReaction(LivingCoreReactionKind Kind, double Direction, long Sequence);
