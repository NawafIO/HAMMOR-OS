using HAMMOR.App.Search;
using HAMMOR.App.Shell;
using Wpf.Ui.Controls;
using Xunit;

namespace HAMMOR.App.Tests.Search;

/// <summary>Ordering, grouping and caps of global Search.</summary>
public sealed class SearchComposerTests
{
    private static SearchResult Result(SearchResultKind kind, string title, string detail = "", int minutesAgo = 0, bool fromStore = false) =>
        new(kind, title, detail, SymbolRegular.Search24)
        {
            Page = ShellPage.Chat,
            When = DateTimeOffset.UnixEpoch.AddDays(1000).AddMinutes(-minutesAgo),
            MatchedByStore = fromStore,
        };

    [Fact]
    public void Groups_follow_a_fixed_order_whatever_the_input_order()
    {
        var results = SearchComposer.Compose("plan", new[]
        {
            Result(SearchResultKind.Task, "Plan the release"),
            Result(SearchResultKind.ChatMessage, "plan for today"),
            Result(SearchResultKind.Project, "Planner"),
            Result(SearchResultKind.Page, "Plans"),
            Result(SearchResultKind.Conversation, "plan my week"),
        });

        Assert.Equal(
            new[]
            {
                SearchResultKind.Page,
                SearchResultKind.Project,
                SearchResultKind.Conversation,
                SearchResultKind.Task,
                SearchResultKind.ChatMessage,
            },
            results.Select(result => result.Kind));
    }

    [Fact]
    public void Within_a_group_better_matches_come_first_then_newer()
    {
        var results = SearchComposer.Compose("notes", new[]
        {
            Result(SearchResultKind.Memory, "Old meeting notes", minutesAgo: 10),
            Result(SearchResultKind.Memory, "Recent meeting notes", minutesAgo: 1),
            Result(SearchResultKind.Memory, "Notes on Arabic", minutesAgo: 50),
        });

        Assert.Equal(
            new[] { "Notes on Arabic", "Recent meeting notes", "Old meeting notes" },
            results.Select(result => result.Title));
    }

    [Fact]
    public void Non_matches_are_dropped()
    {
        var results = SearchComposer.Compose("voice", new[]
        {
            Result(SearchResultKind.Setting, "Voice"),
            Result(SearchResultKind.Setting, "Security"),
        });

        Assert.Equal("Voice", Assert.Single(results).Title);
    }

    [Fact]
    public void A_store_match_is_kept_even_if_folding_differs()
    {
        var results = SearchComposer.Compose("zzz", new[]
        {
            Result(SearchResultKind.Memory, "Unrelated title", "unrelated text", fromStore: true),
        });

        Assert.Equal(SearchText.DetailOnly, Assert.Single(results).Score);
    }

    [Fact]
    public void Each_group_is_capped()
    {
        var many = Enumerable.Range(0, 20).Select(i => Result(SearchResultKind.Task, $"Task {i}"));

        var results = SearchComposer.Compose("task", many);

        Assert.Equal(SearchComposer.CapFor(SearchResultKind.Task), results.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_query_returns_nothing(string query)
    {
        Assert.Empty(SearchComposer.Compose(query, new[] { Result(SearchResultKind.Page, "Chat") }));
    }

    [Fact]
    public void Recents_are_newest_conversations_then_projects()
    {
        var conversations = Enumerable.Range(0, 9)
            .Select(i => Result(SearchResultKind.Conversation, $"Exchange {i}", minutesAgo: i))
            .Reverse();
        var projects = new[] { Result(SearchResultKind.Project, "HAMMOR"), Result(SearchResultKind.Project, "Thesis") };

        var recent = SearchComposer.Recent(conversations, projects);

        Assert.Equal(SearchComposer.CapFor(SearchResultKind.Conversation) + 2, recent.Count);
        Assert.Equal("Exchange 0", recent[0].Title);
        Assert.Equal(new[] { "HAMMOR", "Thesis" }, recent.TakeLast(2).Select(result => result.Title));
    }
}
