namespace HAMMOR.Core.Ai;

/// <summary>Who produced a message in a conversation.</summary>
public enum AiRole
{
    User = 0,
    Assistant = 1,
}

/// <summary>One turn of conversation history.</summary>
/// <param name="Role">Author of the message.</param>
/// <param name="Text">Message body.</param>
public sealed record AiMessage(AiRole Role, string Text)
{
    public static AiMessage User(string text) => new(AiRole.User, text);

    public static AiMessage Assistant(string text) => new(AiRole.Assistant, text);
}

/// <summary>
/// A provider-neutral request. Deliberately carries no provider-specific
/// shapes so the same object can be handed to any <see cref="IAiProvider"/>.
/// </summary>
public sealed record AiRequest
{
    /// <summary>Full conversation history, oldest first. Must not be empty.</summary>
    public required IReadOnlyList<AiMessage> Messages { get; init; }

    /// <summary>System prompt for the turn, or null to use the provider default.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>Model id. Null means "use the configured model".</summary>
    public string? Model { get; init; }

    /// <summary>Hard output ceiling. Null means "use the configured value".</summary>
    public int? MaxTokens { get; init; }

    /// <summary>Reasoning effort hint. Null means "use the configured value".</summary>
    public string? Effort { get; init; }
}

/// <summary>A completed provider reply.</summary>
/// <param name="Text">Assistant-visible text, concatenated across text blocks.</param>
/// <param name="Model">Model that actually served the request.</param>
/// <param name="Usage">Token accounting, when the provider reports it.</param>
/// <param name="StopReason">
/// Provider stop reason, surfaced verbatim so callers can detect truncation
/// (<c>max_tokens</c>) or a safety decline (<c>refusal</c>).
/// </param>
public sealed record AiResponse(
    string Text,
    string Model,
    AiUsage? Usage,
    string? StopReason)
{
    /// <summary>
    /// True when the provider declined the request on safety grounds. Callers
    /// must check this before treating <see cref="Text"/> as an answer.
    /// </summary>
    public bool IsRefusal =>
        string.Equals(StopReason, "refusal", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when output was cut off by the token ceiling.</summary>
    public bool IsTruncated =>
        string.Equals(StopReason, "max_tokens", StringComparison.OrdinalIgnoreCase);
}

/// <param name="InputTokens">Tokens charged as input.</param>
/// <param name="OutputTokens">Tokens generated.</param>
public sealed record AiUsage(long InputTokens, long OutputTokens);
