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

        var traversal = allowed.Replace('\\', '/') + "/../escape.txt";

        var result = policy.Validate(traversal);

        Assert.False(result.IsAllowed);
        Assert.Contains("traversal", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Policy_denies_whitespace_padded_traversal()
    {
        var allowed = CreateTempRoot();
        var policy = PolicyFor(allowed);

        var traversal = Path.Combine(allowed, " .. ", "escape.txt");

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
            return;
        }

        var result = policy.Validate(windowsDir);

        Assert.False(result.IsAllowed);
        Assert.Contains("protected", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Policy_denies_windows_directory_with_trailing_dot()
    {
        var allowed = CreateTempRoot();
        var policy = PolicyFor(allowed);

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDir))
        {
            return;
        }

        var withDot = windowsDir + ".";

        var result = policy.Validate(withDot);

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
                return string.Equals(full, _reparsePath, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public string? ResolveFinalPath(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (string.Equals(full, _reparsePath, StringComparison.OrdinalIgnoreCase))
                {
                    return _targetOutside;
                }

                return Path.GetFullPath(path);
            }
            catch { return null; }
        }

        public string GetFullPath(string path) => Path.GetFullPath(path);
    }

    private sealed class StubDirectoryReparseResolution : IPathResolution
    {
        private readonly string _reparsePath;
        private readonly string _targetOutside;

        public StubDirectoryReparseResolution(string reparsePath, string targetOutside)
        {
            _reparsePath = Path.GetFullPath(reparsePath);
            _targetOutside = Path.GetFullPath(targetOutside);
        }

        public bool IsReparsePoint(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                return string.Equals(full, _reparsePath, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public string? ResolveFinalPath(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (string.Equals(full, _reparsePath, StringComparison.OrdinalIgnoreCase))
                {
                    return _targetOutside;
                }

                return Path.GetFullPath(path);
            }
            catch { return null; }
        }

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
        Assert.True(
            result.Error!.Contains("reparse", StringComparison.OrdinalIgnoreCase)
            || result.Error!.Contains("outside", StringComparison.OrdinalIgnoreCase)
            || result.Error!.Contains("protected", StringComparison.OrdinalIgnoreCase),
            $"Expected reparse/outside/protected but got: {result.Error}");
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

        var stub = new StubDirectoryReparseResolution(reparseDir, outside);
        var policy = PolicyFor(allowed, stub);

        var child = Path.Combine(reparseDir, "child.txt");

        var result = policy.Validate(child);

        Assert.False(result.IsAllowed);
        Assert.True(
            result.Error!.Contains("reparse", StringComparison.OrdinalIgnoreCase)
            || result.Error!.Contains("outside", StringComparison.OrdinalIgnoreCase)
            || result.Error!.Contains("protected", StringComparison.OrdinalIgnoreCase),
            $"Expected reparse/outside/protected but got: {result.Error}");
    }

    [Fact]
    public void Policy_denies_reparse_point_whose_target_is_protected()
    {
        var allowed = CreateTempRoot();
        var reparse = Path.Combine(allowed, "link2");
        Directory.CreateDirectory(reparse);
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDir))
        {
            return;
        }

        var stub = new StubReparseResolution(reparse, windowsDir);
        var policy = PolicyFor(allowed, stub);

        var result = policy.Validate(reparse);

        Assert.False(result.IsAllowed);
        Assert.Contains("protected", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Policy_rejects_extra_root_that_is_a_reparse_point()
    {
        var allowed = CreateTempRoot();
        var outside = Path.Combine(Path.GetTempPath(), $"hammor-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);

        var extraRoot = Path.Combine(allowed, "extra-link");
        Directory.CreateDirectory(extraRoot);

        var stub = new StubReparseResolution(extraRoot, outside);
        var policy = PolicyFor(allowed, stub, extraRoots: new List<string> { extraRoot });

        // The reparse extra root must not be trusted — accessing through it should not be allowed.
        var throughReparse = policy.Validate(Path.Combine(extraRoot, "file.txt"));
        Assert.False(throughReparse.IsAllowed);
    }

    [Fact]
    public void Policy_outside_path_remains_denied_even_when_reparse_root_was_rejected()
    {
        var allowed = CreateTempRoot();
        var outside = Path.Combine(Path.GetTempPath(), $"hammor-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);

        var extraRoot = Path.Combine(allowed, "extra-link");
        Directory.CreateDirectory(extraRoot);

        var stub = new StubReparseResolution(extraRoot, outside);
        var policy = PolicyFor(allowed, stub, extraRoots: new List<string> { extraRoot });

        var outsideFile = Path.Combine(outside, "evil.txt");
        var outsideResult = policy.Validate(outsideFile);
        Assert.False(outsideResult.IsAllowed);
        Assert.Contains("outside the allowed roots", outsideResult.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Policy_allows_non_reparse_extra_root()
    {
        var allowed = CreateTempRoot();
        var extra = CreateTempRoot();
        // No reparse stub — plain extra root should be accepted.
        var policy = PolicyFor(allowed, extraRoots: new List<string> { extra });

        var result = policy.Validate(Path.Combine(extra, "notes.txt"));
        Assert.True(result.IsAllowed, result.Error);
    }

    // ---- Canonicalization / Windows casing tests ----------------------

    [Fact]
    public void Policy_denies_windows_path_with_different_casing()
    {
        var allowed = CreateTempRoot();
        var policy = PolicyFor(allowed);

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDir))
        {
            return;
        }

        var result = policy.Validate(windowsDir.ToUpperInvariant());
        Assert.False(result.IsAllowed);
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

    [Theory]
    [InlineData("filesystem.list_directory")]
    [InlineData("filesystem.read_file")]
    [InlineData("filesystem.write_file")]
    [InlineData("filesystem.delete_file")]
    public async Task All_tools_reject_reparse_escape(string toolName)
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

        ITool tool = toolName switch
        {
            "filesystem.list_directory" => new ListDirectoryTool(policy),
            "filesystem.read_file" => new ReadFileTool(policy),
            "filesystem.write_file" => new WriteFileTool(policy),
            "filesystem.delete_file" => new DeleteFileTool(policy),
            _ => throw new InvalidOperationException(),
        };

        var args = toolName == "filesystem.write_file"
            ? new Dictionary<string, string?> { ["path"] = reparse, ["content"] = "x" }
            : new Dictionary<string, string?> { ["path"] = reparse };

        var result = await tool.ExecuteAsync(Invoke(toolName, args));

        Assert.False(result.Succeeded);
    }

    // ---- WriteFile parent-directory / TOCTOU regressions --------------

    [Fact]
    public async Task WriteFile_through_reparse_parent_is_denied()
    {
        var allowed = CreateTempRoot();
        var reparseDir = Path.Combine(allowed, "junction");
        Directory.CreateDirectory(reparseDir);
        var outside = Path.Combine(Path.GetTempPath(), $"hammor-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);

        var stub = new StubDirectoryReparseResolution(reparseDir, outside);
        var policy = PolicyFor(allowed, stub);
        var tool = new WriteFileTool(policy);

        var target = Path.Combine(reparseDir, "evil.txt");
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = target, ["content"] = "pwn" }));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task WriteFile_parent_recheck_blocks_reparse_swapped_between_validate_and_write()
    {
        var allowed = CreateTempRoot();
        var subdir = Path.Combine(allowed, "a");
        Directory.CreateDirectory(subdir);

        var outside = Path.Combine(Path.GetTempPath(), $"hammor-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);

        var flipping = new FlippingReparseResolution(Path.Combine(allowed, "a"), outside);
        var policy = PolicyFor(allowed, flipping);
        var tool = new WriteFileTool(policy);

        var target = Path.Combine(allowed, "a", "b", "file.txt");
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = target, ["content"] = "hello" }));

        Assert.False(result.Succeeded);
    }

    private sealed class FlippingReparseResolution : IPathResolution
    {
        private readonly string _reparseDir;
        private readonly string _target;
        private int _calls;

        public FlippingReparseResolution(string reparseDir, string target)
        {
            _reparseDir = Path.GetFullPath(reparseDir);
            _target = Path.GetFullPath(target);
        }

        public bool IsReparsePoint(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (!string.Equals(full, _reparseDir, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return Interlocked.Increment(ref _calls) > 1;
            }
            catch { return false; }
        }

        public string? ResolveFinalPath(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (string.Equals(full, _reparseDir, StringComparison.OrdinalIgnoreCase))
                {
                    return _target;
                }

                return Path.GetFullPath(path);
            }
            catch { return null; }
        }

        public string GetFullPath(string path) => Path.GetFullPath(path);
    }

    [Fact]
    public async Task DeleteFile_through_reparse_parent_is_denied()
    {
        var allowed = CreateTempRoot();
        var reparseDir = Path.Combine(allowed, "junction-del");
        Directory.CreateDirectory(reparseDir);
        var outside = Path.Combine(Path.GetTempPath(), $"hammor-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);

        var stub = new StubDirectoryReparseResolution(reparseDir, outside);
        var policy = PolicyFor(allowed, stub);

        var fileUnderReparse = Path.Combine(reparseDir, "victim.txt");
        File.WriteAllText(fileUnderReparse, "x");

        var tool = new DeleteFileTool(policy);
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = fileUnderReparse }));

        Assert.False(result.Succeeded);
    }
}
