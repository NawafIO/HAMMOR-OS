using HAMMOR.App.Search;
using HAMMOR.App.Shell;
using HAMMOR.App.ViewModels;
using HAMMOR.Core.Memory;
using HAMMOR.Core.Projects;
using HAMMOR.Core.Tasks;
using Wpf.Ui.Controls;
using Xunit;

namespace HAMMOR.App.Tests.Search;

/// <summary>What each kind of HAMMOR data becomes as a search result, and where it opens.</summary>
public sealed class SearchCandidateTests
{
    [Fact]
    public void A_saved_exchange_opens_memory_on_its_title()
    {
        var entry = new MemoryEntry
        {
            Title = "What is HAMMOR?",
            Content = "User: What is HAMMOR?\nHAMMOR: Your assistant.",
            Kind = MemoryKind.Conversation,
        };

        var result = SearchService.FromMemory(entry);

        Assert.Equal(SearchResultKind.Conversation, result.Kind);
        Assert.Equal("What is HAMMOR?", result.Title);
        Assert.Equal("Your assistant.", result.Detail);
        Assert.Equal(ShellPage.Memory, result.Page);
        Assert.Equal(entry.Title, result.MemoryQuery);
    }

    [Fact]
    public void Other_memory_opens_memory_too()
    {
        var result = SearchService.FromMemory(new MemoryEntry { Title = "Preference", Content = "Replies in Arabic", Kind = MemoryKind.Preference });

        Assert.Equal(SearchResultKind.Memory, result.Kind);
        Assert.Equal(ShellPage.Memory, result.Page);
        Assert.Equal("Preference", result.MemoryQuery);
    }

    [Fact]
    public void A_project_opens_the_projects_page_on_itself()
    {
        var project = new HammorProject { Name = "Thesis", Description = "Chapter drafts", Kind = ProjectKind.Research };

        var result = SearchService.FromProject(project);

        Assert.Equal(SearchResultKind.Project, result.Kind);
        Assert.Equal(ShellPage.Projects, result.Page);
        Assert.Equal(project.Id, result.ItemId);
        Assert.Equal("Chapter drafts", result.Detail);
        Assert.Equal(SymbolRegular.BookOpen24, result.Icon);
    }

    [Fact]
    public void A_project_without_a_description_shows_its_folder()
    {
        var result = SearchService.FromProject(new HammorProject { Name = "HAMMOR", RootPath = @"C:\src\hammor", Kind = ProjectKind.Software });

        Assert.Equal(@"C:\src\hammor", result.Detail);
        Assert.Equal(SymbolRegular.Code24, result.Icon);
    }

    [Fact]
    public void A_task_opens_the_tasks_page()
    {
        var result = SearchService.FromTask(new HammorTask { Title = "Summarise notes", State = TaskState.Blocked });

        Assert.Equal(SearchResultKind.Task, result.Kind);
        Assert.Equal(ShellPage.Tasks, result.Page);
        Assert.Equal("Summarise notes", result.Title);
    }

    [Fact]
    public void Every_settings_category_is_searchable()
    {
        var results = SettingsSections.All.Select(SearchService.FromSection).ToList();

        Assert.Equal(SettingsSections.All.Count, results.Count);
        Assert.All(results, result =>
        {
            Assert.Equal(SearchResultKind.Setting, result.Kind);
            Assert.Equal(ShellPage.Settings, result.Page);
            Assert.NotNull(result.Section);
        });
    }

    [Fact]
    public void Every_main_page_is_searchable_except_search_itself()
    {
        var pages = SearchService.Pages().Select(result => result.Page).ToList();

        Assert.Equal(
            Enum.GetValues<ShellPage>().Where(page => page != ShellPage.Search).OrderBy(page => page),
            pages.OrderBy(page => page));
    }

    [Theory]
    [InlineData(SearchResultKind.Page, "Search.Group.Pages")]
    [InlineData(SearchResultKind.Conversation, "Search.Group.Conversations")]
    [InlineData(SearchResultKind.ChatMessage, "Search.Group.ThisChat")]
    public void Each_kind_has_its_own_group_heading(SearchResultKind kind, string key)
    {
        Assert.Equal(key, SearchResult.GroupKey(kind));
    }

    [Fact]
    public void Group_headings_are_distinct()
    {
        var keys = Enum.GetValues<SearchResultKind>().Select(SearchResult.GroupKey).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void The_project_list_starts_with_all_conversations()
    {
        var list = ProjectActivity.BuildList(new[]
        {
            new HammorProject { Name = "HAMMOR" },
            new HammorProject { Name = "Thesis", IsArchived = true },
        });

        Assert.Equal(3, list.Count);
        Assert.True(list[0].IsAllConversations);
        Assert.Equal(new[] { "HAMMOR", "Thesis" }, list.Skip(1).Select(item => item.Name));
        Assert.True(list[2].IsArchived);
    }
}
