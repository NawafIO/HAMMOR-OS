using HAMMOR.App.Search;
using HAMMOR.App.ViewModels;
using HAMMOR.Core.Memory;
using Xunit;

namespace HAMMOR.App.Tests.Search;

/// <summary>
/// Reading back the exchanges the agent pipeline saves as conversation
/// memory ("User: …" newline "HAMMOR: …").
/// </summary>
public sealed class ConversationExchangeTests
{
    private static MemoryEntry Entry(string content, MemoryKind kind = MemoryKind.Conversation, string? project = null, int minutes = 0) =>
        new()
        {
            Title = "Saved title",
            Content = content,
            Kind = kind,
            ProjectId = project,
            CreatedUtc = DateTimeOffset.UnixEpoch.AddMinutes(minutes),
        };

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void An_exchange_splits_into_question_and_answer(string newline)
    {
        var exchange = ConversationExchange.Parse(Entry($"User: What is HAMMOR?{newline}HAMMOR: Your assistant.", project: "p1"));

        Assert.Equal("What is HAMMOR?", exchange.Question);
        Assert.Equal("Your assistant.", exchange.Answer);
        Assert.Equal("p1", exchange.ProjectId);
    }

    [Fact]
    public void Multi_line_questions_and_answers_survive()
    {
        var exchange = ConversationExchange.Parse(Entry("User: line one\nline two\nHAMMOR: answer one\nanswer two"));

        Assert.Equal("line one\nline two", exchange.Question);
        Assert.Equal("answer one\nanswer two", exchange.Answer);
    }

    [Fact]
    public void Arabic_exchanges_parse_the_same_way()
    {
        var exchange = ConversationExchange.Parse(Entry("User: ما هو هامور؟\nHAMMOR: مساعدك الشخصي."));

        Assert.Equal("ما هو هامور؟", exchange.Question);
        Assert.Equal("مساعدك الشخصي.", exchange.Answer);
    }

    [Theory]
    [InlineData("A note written by hand")]
    [InlineData("User: no answer marker")]
    public void Other_shapes_fall_back_to_title_and_content(string content)
    {
        var exchange = ConversationExchange.Parse(Entry(content));

        Assert.Equal("Saved title", exchange.Question);
        Assert.Equal(content, exchange.Answer);
    }

    [Fact]
    public void Only_conversation_entries_are_exchanges()
    {
        Assert.True(ConversationExchange.IsExchange(Entry("x")));
        Assert.False(ConversationExchange.IsExchange(Entry("x", MemoryKind.Note)));
        Assert.False(ConversationExchange.IsExchange(Entry("x", MemoryKind.ProjectContext)));
    }

    [Fact]
    public void A_projects_memory_splits_into_exchanges_and_notes_newest_first()
    {
        var (exchanges, notes) = ProjectActivity.Split(new[]
        {
            Entry("User: first\nHAMMOR: one", minutes: 1),
            Entry("A note", MemoryKind.Note, minutes: 2),
            Entry("User: second\nHAMMOR: two", minutes: 3),
            Entry("A fact", MemoryKind.Fact, minutes: 4),
        });

        Assert.Equal(new[] { "second", "first" }, exchanges.Select(exchange => exchange.Question));
        Assert.Equal(new[] { "A fact", "A note" }, notes.Select(note => note.Content));
    }
}
