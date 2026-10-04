using HAMMOR.Core.Ai;

namespace HAMMOR.Core.Agent;

/// <summary>Stages of the HAMMOR execution loop, in order.</summary>
/// <remarks>
/// Reported on <see cref="AgentTurnResult.ReachedStage"/> and streamed to the
/// Activity view so a failure can be attributed to a specific stage rather
/// than appearing as a generic error.
/// </remarks>
public enum AgentStage
{
    Sense = 0,
    Understand = 1,
    Route = 2,
    Plan = 3,
    Authorize = 4,
    Execute = 5,
    Verify = 6,
    Remember = 7,
    Respond = 8,
}

/// <summary>One user input entering the loop.</summary>
public sealed record AgentTurnRequest
{
    /// <summary>What the user typed or said.</summary>
    public required string Input { get; init; }

    /// <summary>Prior turns for context, oldest first.</summary>
    public IReadOnlyList<AiMessage> History { get; init; } = [];

    /// <summary>Project scope, when the turn belongs to one.</summary>
    public string? ProjectId { get; init; }

    /// <summary>
    /// Language the user is working in, e.g. <c>ar</c>. Passed to the model so
    /// it replies in kind, and to TTS for pronunciation.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>Whether the input arrived by voice rather than typing.</summary>
    public bool FromVoice { get; init; }
}

/// <summary>Outcome of one pass through the loop.</summary>
/// <param name="Succeeded">Whether a reply was produced.</param>
/// <param name="ReplyText">Assistant reply, empty on failure.</param>
/// <param name="ReachedStage">Furthest stage reached.</param>
/// <param name="Error">Diagnosable failure reason, already redacted.</param>
/// <param name="Usage">Token accounting when the provider reported it.</param>
public sealed record AgentTurnResult(
    bool Succeeded,
    string ReplyText,
    AgentStage ReachedStage,
    string? Error,
    AiUsage? Usage)
{
    public static AgentTurnResult Success(string reply, AiUsage? usage) =>
        new(true, reply, AgentStage.Respond, null, usage);

    public static AgentTurnResult Failed(AgentStage stage, string error) =>
        new(false, string.Empty, stage, error, null);
}

/// <summary>
/// Progress notification emitted as a turn moves through the loop. Drives the
/// Activity view without the UI polling anything.
/// </summary>
/// <param name="Stage">Stage being entered.</param>
/// <param name="Detail">Short human-readable description.</param>
public sealed record AgentProgress(AgentStage Stage, string Detail);
