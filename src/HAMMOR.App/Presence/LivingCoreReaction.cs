namespace HAMMOR.App.Presence;

/// <summary>Something that happened, which the Living Core reacts to without changing state.</summary>
public enum LivingCoreReactionKind
{
    /// <summary>
    /// A reply arrived: one soft ripple leaves the membrane, the cells shiver
    /// once, and where the state allows it, one glance toward the message.
    /// </summary>
    NewMessage = 0,

    /// <summary>A panel opened: one glance toward it, and the aura leans its way.</summary>
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
