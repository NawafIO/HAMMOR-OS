using System.Text;
using HAMMOR.Core.Ai;

namespace HAMMOR.Infrastructure.Ai.ClaudeCode;

/// <summary>A request as Claude Code receives it.</summary>
/// <param name="SystemPrompt">Goes on the command line as one argument.</param>
/// <param name="Prompt">Goes on standard input, so history never appears on a command line.</param>
public sealed record ClaudeCodePrompt(string SystemPrompt, string Prompt);

/// <summary>
/// Turns a provider-neutral request into one Claude Code print-mode request.
/// </summary>
/// <remarks>
/// Print mode takes a single prompt, and HAMMOR keeps no Claude Code session
/// on disk, so earlier turns travel inside the prompt as a labelled
/// transcript, newest last, trimmed from the oldest end to a size bound.
/// </remarks>
public static class ClaudeCodeTranscript
{
    /// <summary>Upper bound for the prompt on standard input (Claude Code caps stdin at 10 MB).</summary>
    public const int MaxPromptChars = 400_000;

    /// <summary>
    /// Upper bound for the system prompt on the command line, well inside the
    /// Windows limit of 32,767 characters for a whole command line. Longer
    /// instructions move into the prompt.
    /// </summary>
    public const int MaxSystemPromptChars = 12_000;

    internal const string DefaultSystemPrompt =
        "You are HAMMOR, a Windows desktop assistant. Answer in the user's language.";

    internal const string FormatNote =
        "Earlier turns of this conversation, if any, are inside <conversation> tags; reply only to the "
        + "message that follows them. You have no tools in this session.";

    internal const string MovedInstructionsNote =
        "Your instructions for this conversation are inside <instructions> tags at the start of the message.";

    public static ClaudeCodePrompt Compose(string? systemPrompt, IReadOnlyList<AiMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
        {
            throw new ArgumentException("A request needs at least one message.", nameof(messages));
        }

        var instructions = string.IsNullOrWhiteSpace(systemPrompt) ? DefaultSystemPrompt : systemPrompt.Trim();
        var transcript = BuildTranscript(messages);

        var fullSystemPrompt = instructions + "\n\n" + FormatNote;
        if (fullSystemPrompt.Length <= MaxSystemPromptChars)
        {
            return new ClaudeCodePrompt(fullSystemPrompt, transcript);
        }

        // Very long instructions (for example a large project context) would
        // crowd the command line; they ride in the prompt instead.
        var prompt = "<instructions>\n" + instructions + "\n</instructions>\n\n" + transcript;
        return new ClaudeCodePrompt(MovedInstructionsNote + "\n\n" + FormatNote, Truncate(prompt));
    }

    private static string BuildTranscript(IReadOnlyList<AiMessage> messages)
    {
        var latest = messages[^1];
        if (messages.Count == 1)
        {
            return Truncate(latest.Text);
        }

        // Keep the newest earlier turns that fit, then render them oldest first.
        var budget = MaxPromptChars - latest.Text.Length - 64;
        var kept = new List<AiMessage>();
        for (var i = messages.Count - 2; i >= 0 && budget > 0; i--)
        {
            var cost = messages[i].Text.Length + 32;
            if (cost > budget)
            {
                break;
            }

            kept.Add(messages[i]);
            budget -= cost;
        }

        if (kept.Count == 0)
        {
            return Truncate(latest.Text);
        }

        kept.Reverse();
        var builder = new StringBuilder();
        builder.Append("<conversation>\n");
        foreach (var message in kept)
        {
            var tag = message.Role == AiRole.User ? "user" : "assistant";
            builder.Append('<').Append(tag).Append(">\n")
                .Append(message.Text)
                .Append("\n</").Append(tag).Append(">\n");
        }

        builder.Append("</conversation>\n\n").Append(latest.Text);
        return Truncate(builder.ToString());
    }

    private static string Truncate(string text) =>
        text.Length <= MaxPromptChars ? text : text[^MaxPromptChars..];
}
