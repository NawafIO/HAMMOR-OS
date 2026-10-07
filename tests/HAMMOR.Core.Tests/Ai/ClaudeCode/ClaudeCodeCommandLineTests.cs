using HAMMOR.Infrastructure.Ai.ClaudeCode;
using Xunit;

namespace HAMMOR.Core.Tests.Ai.ClaudeCode;

/// <summary>
/// The command lines HAMMOR runs, and where it finds the executable: only
/// documented Claude Code behaviour, no shell, no way to add a flag.
/// </summary>
public sealed class ClaudeCodeCommandLineTests
{
    private static readonly string[] ForbiddenArguments =
    [
        "--dangerously-skip-permissions",
        "--allow-dangerously-skip-permissions",
        "--bare",
        "--permission-mode",
        "bypassPermissions",
        "--add-dir",
        "--mcp-config",
        "--settings",
        "--resume",
        "--continue",
    ];

    [Fact]
    public void A_request_runs_print_mode_with_json_output_and_no_tools()
    {
        var arguments = ClaudeCodeArguments.Request("Be HAMMOR.");

        Assert.Equal(
            new[]
            {
                "-p",
                "--output-format",
                "json",
                "--tools",
                "",
                "--no-session-persistence",
                "--safe-mode",
                "--strict-mcp-config",
                "--system-prompt=Be HAMMOR.",
            },
            arguments);
    }

    [Fact]
    public void Account_commands_are_the_documented_ones()
    {
        Assert.Equal(new[] { "--version" }, ClaudeCodeArguments.Version);
        Assert.Equal(new[] { "auth", "status", "--json" }, ClaudeCodeArguments.AuthStatus);
        Assert.Equal(new[] { "auth", "login", "--claudeai" }, ClaudeCodeArguments.AuthLogin);
        Assert.Equal(new[] { "auth", "logout" }, ClaudeCodeArguments.AuthLogout);
    }

    [Fact]
    public void No_command_bypasses_permissions_disables_sign_in_or_widens_access()
    {
        IReadOnlyList<string>[] all =
        [
            ClaudeCodeArguments.Version,
            ClaudeCodeArguments.AuthStatus,
            ClaudeCodeArguments.AuthLogin,
            ClaudeCodeArguments.AuthLogout,
            ClaudeCodeArguments.Request("Be HAMMOR."),
        ];

        foreach (var arguments in all)
        {
            foreach (var forbidden in ForbiddenArguments)
            {
                Assert.DoesNotContain(forbidden, arguments);
            }
        }
    }

    [Fact]
    public void A_system_prompt_that_looks_like_flags_stays_one_argument()
    {
        const string hostile = "--dangerously-skip-permissions --tools default --add-dir C:\\";

        var arguments = ClaudeCodeArguments.Request(hostile);

        Assert.Equal(9, arguments.Count);
        Assert.Equal("--system-prompt=" + hostile, arguments[^1]);
        Assert.DoesNotContain("--dangerously-skip-permissions", arguments);
        Assert.DoesNotContain("--add-dir", arguments);
    }

    [Fact]
    public void The_native_installer_location_comes_first()
    {
        var home = Path.Combine(Path.GetTempPath(), "hammor-user");
        var native = Path.Combine(home, ".local", "bin", "claude.exe");
        var onPath = Path.Combine(Path.GetTempPath(), "hammor-path", "claude.exe");
        var host = new FakeClaudeCodeHost(isWindows: true);
        host.Variables["USERPROFILE"] = home;
        host.Variables["PATH"] = Path.GetDirectoryName(onPath);
        host.Files.Add(native);
        host.Files.Add(onPath);

        var location = ClaudeCodeLocator.Locate(host);

        Assert.Equal(native, location.ExecutablePath);
    }

    [Fact]
    public void The_executable_is_found_on_the_path()
    {
        var first = Path.Combine(Path.GetTempPath(), "hammor-empty");
        var second = Path.Combine(Path.GetTempPath(), "hammor-links");
        var executable = Path.Combine(second, "claude.exe");
        var host = new FakeClaudeCodeHost(isWindows: true);
        host.Variables["PATH"] = first + ";\"" + second + "\"";
        host.Files.Add(executable);

        var location = ClaudeCodeLocator.Locate(host);

        Assert.Equal(executable, location.ExecutablePath);
        Assert.Null(location.ScriptLauncherPath);
    }

    [Fact]
    public void Relative_path_entries_can_never_supply_the_executable()
    {
        var host = new FakeClaudeCodeHost(isWindows: true);
        host.Variables["PATH"] = ".;bin;tools";
        host.Files.Add(Path.Combine(".", "claude.exe"));
        host.Files.Add(Path.Combine("bin", "claude.exe"));

        var location = ClaudeCodeLocator.Locate(host);

        Assert.False(location.Found);
    }

    [Fact]
    public void An_npm_launcher_is_reported_but_never_used()
    {
        var npm = Path.Combine(Path.GetTempPath(), "hammor-npm");
        var launcher = Path.Combine(npm, "claude.cmd");
        var host = new FakeClaudeCodeHost(isWindows: true);
        host.Variables["PATH"] = npm;
        host.Files.Add(launcher);

        var location = ClaudeCodeLocator.Locate(host);

        Assert.False(location.Found);
        Assert.Equal(launcher, location.ScriptLauncherPath);
    }

    [Fact]
    public void Nothing_installed_finds_nothing()
    {
        var host = new FakeClaudeCodeHost(isWindows: true);
        host.Variables["PATH"] = Path.Combine(Path.GetTempPath(), "hammor-empty");

        Assert.Equal(ClaudeCodeLocation.None, ClaudeCodeLocator.Locate(host));
    }

    [Fact]
    public void Other_systems_look_for_claude_without_an_extension()
    {
        var bin = Path.Combine(Path.GetTempPath(), "hammor-bin");
        var executable = Path.Combine(bin, "claude");
        var host = new FakeClaudeCodeHost(isWindows: false);
        host.Variables["PATH"] = bin;
        host.Files.Add(executable);

        Assert.Equal(executable, ClaudeCodeLocator.Locate(host).ExecutablePath);
    }

    [Fact]
    public void The_runner_needs_a_full_working_directory()
    {
        Assert.Throws<ArgumentException>(() => new ClaudeCodeCli(
            new FakeClaudeCodeHost(isWindows: true),
            "relative\\folder",
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ClaudeCodeCli>.Instance));
    }

    [Fact]
    public async Task Without_an_executable_nothing_is_started()
    {
        var host = new FakeClaudeCodeHost(isWindows: true);
        var cli = new ClaudeCodeCli(
            host,
            Path.Combine(Path.GetTempPath(), "hammor-claude-code"),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ClaudeCodeCli>.Instance);

        await Assert.ThrowsAsync<ClaudeCodeNotFoundException>(
            () => cli.RunAsync(ClaudeCodeArguments.Version, null, TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAsync<ClaudeCodeNotFoundException>(
            () => cli.RunInteractiveAsync(ClaudeCodeArguments.AuthLogin, TimeSpan.FromSeconds(5)));
    }
}
