namespace HAMMOR.Core.Agent;

public sealed record AgentLoopOptions
{
    public const int DefaultMaxRounds = 5;
    public const int HardCapMaxRounds = 10;

    /// <summary>Rounds of model→tools→model. Clamped to [1, HardCapMaxRounds], default 5.</summary>
    public int MaxRounds { get; init; } = DefaultMaxRounds;

    /// <summary>Characters fed back per tool result. Prevents one tool from consuming the context.</summary>
    public int MaxToolResultChars { get; init; } = 12_000;

    public int EffectiveMaxRounds => Math.Clamp(MaxRounds <= 0 ? DefaultMaxRounds : MaxRounds, 1, HardCapMaxRounds);
}
