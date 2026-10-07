using System.Globalization;
using System.Resources;
using HAMMOR.App.Views;
using Xunit;

namespace HAMMOR.App.Tests.Search;

/// <summary>Every string Chat, Projects and Search read exists.</summary>
public sealed class Step3StringsTests
{
    [Theory]
    [InlineData("Chat.Copy")]
    [InlineData("Chat.InProject")]
    [InlineData("Chat.LeaveProject")]
    [InlineData("Chat.NewChat")]
    [InlineData("Chat.Stop")]
    [InlineData("Nav.Search")]
    [InlineData("Projects.All")]
    [InlineData("Projects.All.Description")]
    [InlineData("Projects.All.Detail")]
    [InlineData("Projects.ChatHere")]
    [InlineData("Projects.Context")]
    [InlineData("Projects.Conversations")]
    [InlineData("Projects.CopyPath")]
    [InlineData("Projects.CreateNotBuilt")]
    [InlineData("Projects.Created")]
    [InlineData("Projects.Kind.General")]
    [InlineData("Projects.Kind.Research")]
    [InlineData("Projects.Kind.Software")]
    [InlineData("Projects.LastOpened")]
    [InlineData("Projects.LoadFailed")]
    [InlineData("Projects.MemoryKind.Conversation")]
    [InlineData("Projects.MemoryKind.Fact")]
    [InlineData("Projects.MemoryKind.Note")]
    [InlineData("Projects.MemoryKind.Preference")]
    [InlineData("Projects.MemoryKind.ProjectContext")]
    [InlineData("Projects.NoConversations")]
    [InlineData("Projects.NoTasks")]
    [InlineData("Projects.Notes")]
    [InlineData("Projects.ShowArchived")]
    [InlineData("Projects.Tasks")]
    [InlineData("Search.Group.Conversations")]
    [InlineData("Search.Group.Memory")]
    [InlineData("Search.Group.Pages")]
    [InlineData("Search.Group.Projects")]
    [InlineData("Search.Group.Settings")]
    [InlineData("Search.Group.Tasks")]
    [InlineData("Search.Group.ThisChat")]
    [InlineData("Search.NoResults")]
    [InlineData("Search.NothingRecent")]
    [InlineData("Search.Placeholder")]
    [InlineData("Search.Results")]
    [InlineData("Search.Scope")]
    [InlineData("Search.SomeFailed")]
    [InlineData("Search.Title")]
    public void The_string_exists(string key)
    {
        var resources = new ResourceManager("HAMMOR.App.Localization.Strings", typeof(SearchPage).Assembly);

        Assert.False(string.IsNullOrWhiteSpace(resources.GetString(key, CultureInfo.InvariantCulture)), key);
    }
}
