using HAMMOR.Core.Memory;

namespace HAMMOR.App.Search;

/// <summary>
/// One saved exchange: what the user asked and what HAMMOR answered.
/// </summary>
/// <remarks>
/// <para>
/// HAMMOR keeps no chat sessions. What it does keep is every successful
/// interactive turn, written by the agent pipeline to memory as a
/// <see cref="MemoryKind.Conversation"/> entry tagged with the turn's project:
/// <c>"User: {question}{newline}HAMMOR: {answer}"</c>. This reads that entry
/// back. Turns are not linked to each other, so the app presents exchanges,
/// never invented threads.
/// </para>
/// <para>
/// An entry that does not have that shape (written by an older build, or
/// edited by hand in the markdown mirror) still shows: its title as the
/// question and its whole content as the answer.
/// </para>
/// </remarks>
/// <param name="Question">The user's words.</param>
/// <param name="Answer">HAMMOR's reply.</param>
/// <param name="ProjectId">The project the turn belonged to, if any.</param>
/// <param name="When">When it was saved.</param>
public sealed record ConversationExchange(string Question, string Answer, string? ProjectId, DateTimeOffset When)
{
    private const string UserPrefix = "User: ";
    private const string AssistantMarker = "HAMMOR: ";

    /// <summary>Whether a memory entry is a saved exchange.</summary>
    public static bool IsExchange(MemoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Kind == MemoryKind.Conversation;
    }

    /// <summary>Reads an exchange out of a conversation memory entry.</summary>
    public static ConversationExchange Parse(MemoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var content = entry.Content;
        if (content.StartsWith(UserPrefix, StringComparison.Ordinal))
        {
            // The answer starts after the first line break followed by the
            // assistant marker; a question containing that exact sequence is
            // split early, which only shortens the preview.
            var split = IndexOfAnswer(content);
            if (split.QuestionEnd >= 0)
            {
                var question = content[UserPrefix.Length..split.QuestionEnd].TrimEnd('\r', '\n');
                var answer = content[split.AnswerStart..];
                return new ConversationExchange(question, answer, entry.ProjectId, entry.CreatedUtc);
            }
        }

        return new ConversationExchange(entry.Title, content, entry.ProjectId, entry.CreatedUtc);
    }

    private static (int QuestionEnd, int AnswerStart) IndexOfAnswer(string content)
    {
        var at = content.IndexOf("\n" + AssistantMarker, UserPrefix.Length, StringComparison.Ordinal);
        return at < 0 ? (-1, -1) : (at, at + 1 + AssistantMarker.Length);
    }
}
