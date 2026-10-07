using HAMMOR.App.Shell;
using Xunit;

namespace HAMMOR.App.Tests.Shell;

/// <summary>
/// The remembered sidebar state: a convenience that must never get in the
/// way, so every bad file means "use the defaults" and a failed save is a
/// return value, not an exception.
/// </summary>
public sealed class ShellLayoutStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hammor-shell-tests-" + Guid.NewGuid().ToString("n"));

    private string LayoutFile => Path.Combine(_folder, "shell-layout.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void No_file_means_an_open_sidebar()
    {
        var layout = new ShellLayoutStore(LayoutFile).Load();

        Assert.False(layout.SidebarCollapsed);
        Assert.Equal(ShellLayout.CurrentVersion, layout.Version);
    }

    [Fact]
    public void A_saved_layout_comes_back()
    {
        var store = new ShellLayoutStore(LayoutFile);

        Assert.True(store.Save(new ShellLayout { SidebarCollapsed = true }));
        Assert.True(new ShellLayoutStore(LayoutFile).Load().SidebarCollapsed);

        Assert.True(store.Save(new ShellLayout { SidebarCollapsed = false }));
        Assert.False(new ShellLayoutStore(LayoutFile).Load().SidebarCollapsed);
    }

    [Fact]
    public void Saving_creates_the_folder_and_leaves_no_temporary_file()
    {
        var store = new ShellLayoutStore(LayoutFile);

        Assert.True(store.Save(new ShellLayout { SidebarCollapsed = true }));

        Assert.Equal(new[] { LayoutFile }, Directory.GetFiles(_folder));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{ \"sidebarCollapsed\": ")]
    [InlineData("[1, 2, 3]")]
    [InlineData("null")]
    public void An_unreadable_file_means_the_defaults(string content)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(LayoutFile, content);

        var layout = new ShellLayoutStore(LayoutFile).Load();

        Assert.False(layout.SidebarCollapsed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(99)]
    public void A_layout_from_an_unknown_version_is_not_guessed_at(int version)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(LayoutFile, "{ \"version\": " + version + ", \"sidebarCollapsed\": true }");

        Assert.False(new ShellLayoutStore(LayoutFile).Load().SidebarCollapsed);
    }

    [Fact]
    public void Properties_this_build_does_not_know_are_ignored()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(LayoutFile, "{ \"version\": 1, \"SIDEBARCOLLAPSED\": true, \"sidebarWidth\": 300 }");

        Assert.True(new ShellLayoutStore(LayoutFile).Load().SidebarCollapsed);
    }

    [Fact]
    public void A_failed_save_is_reported_not_thrown()
    {
        // The "folder" is really a file, so the folder cannot be created.
        Directory.CreateDirectory(_folder);
        var blocker = Path.Combine(_folder, "blocker");
        File.WriteAllText(blocker, "x");
        var store = new ShellLayoutStore(Path.Combine(blocker, "shell-layout.json"));

        Assert.False(store.Save(new ShellLayout { SidebarCollapsed = true }));
    }

    [Fact]
    public void The_file_needs_a_full_path()
    {
        Assert.Throws<ArgumentException>(() => new ShellLayoutStore("shell-layout.json"));
        Assert.Throws<ArgumentException>(() => new ShellLayoutStore(" "));
    }
}
