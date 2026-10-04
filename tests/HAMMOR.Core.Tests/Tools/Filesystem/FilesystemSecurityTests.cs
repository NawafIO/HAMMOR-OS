using System.IO;
using HAMMOR.Core.Configuration;
using HAMMOR.Core.Storage;
using HAMMOR.Core.Tools;
using HAMMOR.Core.Tools.Filesystem;
using Xunit;

namespace HAMMOR.Core.Tests.Tools.Filesystem;

public sealed class FilesystemSecurityTests : IDisposable
{
    private readonly List<string> _tempRoots = new();

    public void Dispose()
    {
        foreach (var root in _tempRoots)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch { }
        }
    }

    private string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hammor-sec-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        _tempRoots.Add(root);
        return root;
    }

    private static IFilesystemPolicy PolicyFor(string allowedRoot, IPathResolution? resolution = null, List<string>? extraRoots = null) =>
        FilesystemPolicyFactory.CreateForRoot(allowedRoot, resolution, extraRoots);

    private static ToolInvocation Invoke(string toolName, Dictionary<string, string?> args) =>
        new() { ToolName = toolName, Arguments = args };

    // ---- Policy direct tests ------------------------------------------

    [Fact]
    public void Policy_denies_path_outside_allowed_root()
    {
        var allowed = CreateTempRoot();
        var policy = PolicyFor(allowed);

        var outside = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        // Pick a sibling temp path that is not under allowed.
        var sibling = Path.Combine(Path.GetTempPath(), $"hammor-outside-{Guid.NewGuid():N}");

        var result = policy.Validate(sibling);

        Assert.False(result.IsAllowed);
        Assert.Contains("outside the allowed roots", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Policy_denies_traversal_segment()
    {
        var allowed = CreateTempRoot();
        var policy = PolicyFor(allowed);

        var traversal = Path.Combine(allowed, "..", "escape.txt");

        var result = policy.Validate(traversal);

        Assert.False(result.IsAllowed);
        Assert.Contains("traversal", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Policy_denies_normalization_escape_via_parent()
    {
        var allowed = CreateTempRoot();
        var policy = PolicyFor(allowed);

        // Raw path contains ".." but after GetFullPath it escapes to parent's sibling.
        var escaped = allowed.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + $"hammor-escape-{Guid.NewGuid():N}";

        var result = policy.Validate(escaped);

        Assert.False(result.IsAllowed);
        Assert.Contains("traversal", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Policy_denies_alternate_separator_traversal()
    {
        var allowed = CreateTempRoot();
        var policy = PolicyFor(allowed);

        // Use forward slash traversal on Windows.
        var traversal = allowed.Replace('\\', '/') + "/../escape.txt";

        var result = policy.Validate(traversal);

        Assert.False(result.IsAllowed);
        Assert.Contains("traversal", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Policy_denies_windows_directory()
    {
        var allowed = CreateTempRoot();
        var policy = PolicyFor(allowed);

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDir))
        {
            return; // Skip on non-Windows CI.
        }

        var result = policy.Validate(windowsDir);

        Assert.False(result.IsAllowed);
        Assert.Contains("protected", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Policy_denies_program_files()
    {
        var allowed = CreateTempRoot();
        var policy = PolicyFor(allowed);

        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(pf))
        {
            return;
        }

        var result = policy.Validate(pf);

        Assert.False(result.IsAllowed);
        Assert.Contains("Program Files", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Policy_denies_hammor_secrets_directory()
    {
        var allowed = CreateTempRoot();
        var paths = new HammorPaths(allowed);
        var policy = PolicyFor(allowed);

        var result = policy.Validate(paths.SecretsDirectory);

        Assert.False(result.IsAllowed);
        Assert.Contains("secret", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Policy_denies_file_inside_secrets_directory()
    {
        var allowed = CreateTempRoot();
        var paths = new HammorPaths(allowed);
        Directory.CreateDirectory(paths.SecretsDirectory);
        var policy = PolicyFor(allowed);

        var secretFile = Path.Combine(paths.SecretsDirectory, "anthropic.api_key.dpapi");

        var result = policy.Validate(secretFile);

        Assert.False(result.IsAllowed);
        Assert.Contains("secret", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Policy_allows_paths_under_allowed_root()
    {
        var allowed = CreateTempRoot();
        var policy = PolicyFor(allowed);

        var child = Path.Combine(allowed, "sub", "file.txt");

        var result = policy.Validate(child);

        Assert.True(result.IsAllowed, result.Error);
        Assert.NotNull(result.NormalizedPath);
    }

    [Fact]
    public void Policy_allows_extra_configured_root()
    {
        var allowed = CreateTempRoot();
        var extra = CreateTempRoot();
        var policy = PolicyFor(allowed, extraRoots: new List<string> { extra });

        var child = Path.Combine(extra, "notes.txt");

        var result = policy.Validate(child);

        Assert.True(result.IsAllowed, result.Error);
    }

    // ---- Reparse / symlink tests via stub resolution ------------------

    private sealed class StubReparseResolution : IPathResolution
    {
        private readonly string _reparsePath;
        private readonly string _targetOutside;

        public StubReparseResolution(string reparsePath, string targetOutside)
        {
            _reparsePath = Path.GetFullPath(reparsePath);
            _targetOutside = Path.GetFullPath(targetOutside);
        }

        public bool IsReparsePoint(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                return string.Equals(full, _reparsePath, StringComparison.OrdinalIgnoreCase)
                       || full.StartsWith(_reparsePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public string? ResolveFinalPath(string path) => _targetOutside;

        public string GetFullPath(string path) => Path.GetFullPath(path);
    }

    [Fact]
    public void Policy_denies_reparse_point_that_escapes()
    {
        var allowed = CreateTempRoot();
        var reparse = Path.Combine(allowed, "link");
        Directory.CreateDirectory(reparse);
        var outside = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(outside))
        {
            outside = Path.Combine(Path.GetTempPath(), $"hammor-outside-{Guid.NewGuid():N}");
        }

        var stub = new StubReparseResolution(reparse, outside);
        var policy = PolicyFor(allowed, stub);

        var result = policy.Validate(reparse);

        Assert.False(result.IsAllowed);
        Assert.Contains("reparse", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outside", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Policy_denies_path_inside_escaping_reparse_chain()
    {
        var allowed = CreateTempRoot();
        var reparseDir = Path.Combine(allowed, "junction");
        Directory.CreateDirectory(reparseDir);
        var outside = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(outside))
        {
            outside = Path.Combine(Path.GetTempPath(), $"hammor-outside-{Guid.NewGuid():N}");
        }

        var stub = new StubReparseResolution(reparseDir, outside);
        var policy = PolicyFor(allowed, stub);

        var child = Path.Combine(reparseDir, "child.txt");

        var result = policy.Validate(child);

        Assert.False(result.IsAllowed);
        Assert.Contains("reparse", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    // ---- All four tools enforce the policy ----------------------------

    [Theory]
    [InlineData("filesystem.list_directory")]
    [InlineData("filesystem.read_file")]
    [InlineData("filesystem.write_file")]
    [InlineData("filesystem.delete_file")]
    public async Task All_tools_reject_path_outside_allowed_roots(string toolName)
    {
        var allowed = CreateTempRoot();
        var policy = PolicyFor(allowed);
        var outside = Path.Combine(Path.GetTempPath(), $"hammor-outside-{Guid.NewGuid():N}.txt");

        ITool tool = toolName switch
        {
            "filesystem.list_directory" => new ListDirectoryTool(policy),
            "filesystem.read_file" => new ReadFileTool(policy),
            "filesystem.write_file" => new WriteFileTool(policy),
            "filesystem.delete_file" => new DeleteFileTool(policy),
            _ => throw new InvalidOperationException(),
        };

        var args = toolName == "filesystem.write_file"
            ? new Dictionary<string, string?> { ["path"] = outside, ["content"] = "x" }
            : new Dictionary<string, string?> { ["path"] = outside };

        var result = await tool.ExecuteAsync(Invoke(toolName, args));

        Assert.False(result.Succeeded);
        Assert.Contains("outside the allowed roots", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("filesystem.list_directory")]
    [InlineData("filesystem.read_file")]
    [InlineData("filesystem.write_file")]
    [InlineData("filesystem.delete_file")]
    public async Task All_tools_reject_traversal(string toolName)
    {
        var allowed = CreateTempRoot();
        var policy = PolicyFor(allowed);
        var traversal = Path.Combine(allowed, "..", "escape.txt");

        ITool tool = toolName switch
        {
            "filesystem.list_directory" => new ListDirectoryTool(policy),
            "filesystem.read_file" => new ReadFileTool(policy),
            "filesystem.write_file" => new WriteFileTool(policy),
            "filesystem.delete_file" => new DeleteFileTool(policy),
            _ => throw new InvalidOperationException(),
        };

        var args = toolName == "filesystem.write_file"
            ? new Dictionary<string, string?> { ["path"] = traversal, ["content"] = "x" }
            : new Dictionary<string, string?> { ["path"] = traversal };

        var result = await tool.ExecuteAsync(Invoke(toolName, args));

        Assert.False(result.Succeeded);
        Assert.Contains("traversal", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("filesystem.list_directory")]
    [InlineData("filesystem.read_file")]
    [InlineData("filesystem.write_file")]
    [InlineData("filesystem.delete_file")]
    public async Task All_tools_reject_secrets_directory(string toolName)
    {
        var allowed = CreateTempRoot();
        var paths = new HammorPaths(allowed);
        Directory.CreateDirectory(paths.SecretsDirectory);
        var policy = PolicyFor(allowed);

        ITool tool = toolName switch
        {
            "filesystem.list_directory" => new ListDirectoryTool(policy),
            "filesystem.read_file" => new ReadFileTool(policy),
            "filesystem.write_file" => new WriteFileTool(policy),
            "filesystem.delete_file" => new DeleteFileTool(policy),
            _ => throw new InvalidOperationException(),
        };

        var args = toolName == "filesystem.write_file"
            ? new Dictionary<string, string?> { ["path"] = Path.Combine(paths.SecretsDirectory, "x.txt"), ["content"] = "x" }
            : new Dictionary<string, string?> { ["path"] = paths.SecretsDirectory };

        var result = await tool.ExecuteAsync(Invoke(toolName, args));

        Assert.False(result.Succeeded);
        Assert.Contains("secret", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("filesystem.list_directory")]
    [InlineData("filesystem.read_file")]
    [InlineData("filesystem.write_file")]
    [InlineData("filesystem.delete_file")]
    public async Task All_tools_reject_protected_windows_path(string toolName)
    {
        var allowed = CreateTempRoot();
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDir))
        {
            return;
        }

        var policy = PolicyFor(allowed);

        ITool tool = toolName switch
        {
            "filesystem.list_directory" => new ListDirectoryTool(policy),
            "filesystem.read_file" => new ReadFileTool(policy),
            "filesystem.write_file" => new WriteFileTool(policy),
            "filesystem.delete_file" => new DeleteFileTool(policy),
            _ => throw new InvalidOperationException(),
        };

        var args = toolName == "filesystem.write_file"
            ? new Dictionary<string, string?> { ["path"] = Path.Combine(windowsDir, "x.txt"), ["content"] = "x" }
            : new Dictionary<string, string?> { ["path"] = windowsDir };

        var result = await tool.ExecuteAsync(Invoke(toolName, args));

        Assert.False(result.Succeeded);
        Assert.Contains("protected", result.Error!, StringComparison.OrdinalIgnoreCase);
    }
}
