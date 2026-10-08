using HAMMOR.App.Search;
using Xunit;

namespace HAMMOR.App.Tests.Search;

/// <summary>How Search folds and compares text, Arabic included.</summary>
public sealed class SearchTextTests
{
    [Theory]
    [InlineData("مُهِمَّة", "مهمه")]       // tashkeel removed, ة as ه
    [InlineData("مهمة", "مهمه")]
    [InlineData("أحمد", "احمد")]            // hamza on alef
    [InlineData("إبراهيم", "ابراهيم")]       // hamza under alef
    [InlineData("آخر", "اخر")]              // madda
    [InlineData("مستشفى", "مستشفي")]        // alef maqsura as ya
    [InlineData("مـــرحبا", "مرحبا")]        // tatweel removed
    [InlineData("Hello   World", "hello world")]
    [InlineData("  Tasks\tand\nNotes ", "tasks and notes")]
    public void Text_is_folded_for_comparison(string text, string expected)
    {
        Assert.Equal(expected, SearchText.Normalize(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_folds_to_empty(string? text)
    {
        Assert.Equal(string.Empty, SearchText.Normalize(text));
    }

    [Fact]
    public void Arabic_typed_without_marks_finds_the_marked_title()
    {
        Assert.Equal(SearchText.TitleStart, SearchText.Score(SearchText.Normalize("مهمه"), "مُهِمَّة جديدة"));
    }

    [Theory]
    [InlineData("set", "Settings", null, SearchText.TitleStart)]
    [InlineData("key", "Anthropic API key", null, SearchText.TitleWord)]
    [InlineData("pic", "Anthropic API key", null, SearchText.TitleAnywhere)]
    [InlineData("dpapi", "Keys and secrets", "Stored with DPAPI", SearchText.DetailOnly)]
    [InlineData("voice", "Theme", "Light or dark", SearchText.NoMatch)]
    public void Matches_rank_title_start_then_word_then_anywhere_then_detail(
        string query, string title, string? detail, int expected)
    {
        Assert.Equal(expected, SearchText.Score(SearchText.Normalize(query), title, detail));
    }

    [Fact]
    public void An_empty_query_matches_nothing()
    {
        Assert.Equal(SearchText.NoMatch, SearchText.Score(string.Empty, "Anything"));
    }

    [Fact]
    public void Snippets_are_one_short_line()
    {
        var snippet = SearchText.Snippet("First line\r\nsecond   line\tand more", 20);

        Assert.DoesNotContain('\n', snippet);
        Assert.True(snippet.Length <= 20);
        Assert.EndsWith("…", snippet);
        Assert.Equal("Short", SearchText.Snippet("Short"));
        Assert.Equal(string.Empty, SearchText.Snippet(null));
    }
}
