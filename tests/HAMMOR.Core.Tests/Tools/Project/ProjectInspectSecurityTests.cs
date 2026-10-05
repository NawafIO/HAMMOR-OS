using System.IO;
using HAMMOR.Core.Tools;
using HAMMOR.Core.Tools.Filesystem;
using HAMMOR.Core.Tools.Project;
using Xunit;

namespace HAMMOR.Core.Tests.Tools.Project;

public sealed class ProjectInspectSecurityTests : IDisposable
{
    private readonly List<string> _tempRoots = new();
    private readonly List<string> _fileReadsObserved = new();

    public void Dispose()
    {
        foreach (var root in _tempRoots)
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    private string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hammor-proj-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        _tempRoots.Add(root);
        return root;
    }

    private static IFilesystemPolicy PolicyFor(string allowedRoot, IPathResolution? resolution = null) =>
        FilesystemPolicyFactory.CreateForRoot(allowedRoot, resolution);

    private static ToolInvocation Inv(string projectPath) =>
        new() { ToolName = "project.inspect", Arguments = new Dictionary<string, string?> { ["projectPath"] = projectPath } };

    [Fact]
    public async Task Inspect_does_not_read_file_contents()
    {
        var root = CreateTempRoot();
        var secretFile = Path.Combine(root, "README.md");
        var secret = Guid.NewGuid().ToString("N");
        File.WriteAllText(secretFile, secret);

        var tool = new ProjectInspectTool(PolicyFor(root));
        var result = await tool.ExecuteAsync(Inv(root));
        Assert.True(result.Succeeded, result.Error);
        Assert.DoesNotContain(secret, result.Output);
        // It should mention the marker existence but never the content.
        Assert.Contains("README.md", result.Output);
    }

    [Fact]
    public async Task Inspect_does_not_recursively_traverse_workspace()
    {
        var root = CreateTempRoot();
        var nested = Path.Combine(root, "src", "deep", "nested");
        Directory.CreateDirectory(nested);
        var marker = Path.Combine(nested, "evil.txt");
        File.WriteAllText(marker, "hidden");

        // Create src so known marker 'src' is present, but deep nesting should not be enumerated.
        var tool = new ProjectInspectTool(PolicyFor(root));
        var result = await tool.ExecuteAsync(Inv(root));
        Assert.True(result.Succeeded, result.Error);
        Assert.DoesNotContain("evil.txt", result.Output);
        Assert.DoesNotContain("deep", result.Output);
        // Top-level should contain 'src' but not deeply nested files.
        Assert.Contains("src", result.Output);
    }

    [Fact]
    public async Task Inspect_rejects_outside_allowed_roots()
    {
        var allowed = CreateTempRoot();
        var outside = Path.Combine(Path.GetTempPath(), $"hammor-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        _tempRoots.Add(outside);

        var tool = new ProjectInspectTool(PolicyFor(allowed));
        var result = await tool.ExecuteAsync(Inv(outside));
        Assert.False(result.Succeeded);
        Assert.Contains("outside the allowed roots", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inspect_rejects_traversal()
    {
        var allowed = CreateTempRoot();
        var tool = new ProjectInspectTool(PolicyFor(allowed));
        var result = await tool.ExecuteAsync(Inv(Path.Combine(allowed, "..", "escape")));
        Assert.False(result.Succeeded);
        Assert.Contains("traversal", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inspect_rejects_protected_path()
    {
        var allowed = CreateTempRoot();
        var tool = new ProjectInspectTool(PolicyFor(allowed));
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDir)) return;
        var result = await tool.ExecuteAsync(Inv(windowsDir));
        Assert.False(result.Succeeded);
        Assert.Contains("protected", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inspect_rejects_secrets_directory()
    {
        var allowed = CreateTempRoot();
        Directory.CreateDirectory(Path.Combine(allowed, "secrets"));
        var tool = new ProjectInspectTool(PolicyFor(allowed));
        var result = await tool.ExecuteAsync(Inv(Path.Combine(allowed, "secrets")));
        Assert.False(result.Succeeded);
        Assert.Contains("secret", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inspect_rejects_reparse_escape()
    {
        var allowed = CreateTempRoot();
        var reparse = Path.Combine(allowed, "link");
        Directory.CreateDirectory(reparse);
        var outside = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(outside)) outside = Path.Combine(Path.GetTempPath(), $"hammor-outside-{Guid.NewGuid():N}");
        var stub = new StubReparseResolution(reparse, outside);
        var tool = new ProjectInspectTool(PolicyFor(allowed, stub));
        var result = await tool.ExecuteAsync(Inv(reparse));
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Inspect_every_marker_validated_through_policy()
    {
        // If a known marker resolves outside allowed roots via reparse, it must be reported as not inspected, not as present.
        var allowed = CreateTempRoot();
        var markerTargetOutside = Path.Combine(Path.GetTempPath(), $"hammor-outside-{Guid.NewGuid():N}", "README.md");
        // We stub so that <root>/README.md is considered a reparse escaping to outside.
        var readmePath = Path.Combine(allowed, "README.md");
        File.WriteAllText(readmePath, "hello");
        var stub = new StubSpecificFileReparseResolution(readmePath, markerTargetOutside);
        var tool = new ProjectInspectTool(PolicyFor(allowed, stub));
        var result = await tool.ExecuteAsync(Inv(allowed));
        Assert.True(result.Succeeded, result.Error);
        // The README.md line should indicate it was not inspected due to policy, not leak content.
        Assert.Contains("README.md", result.Output);
        Assert.DoesNotContain("hello", result.Output);
    }

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
            try { return string.Equals(Path.GetFullPath(path), _reparsePath, StringComparison.OrdinalIgnoreCase); } catch { return false; }
        }
        public string? ResolveFinalPath(string path)
        {
            try
            {
                if (string.Equals(Path.GetFullPath(path), _reparsePath, StringComparison.OrdinalIgnoreCase))
                    return _targetOutside;
                return Path.GetFullPath(path);
            }
            catch { return null; }
        }
        public string GetFullPath(string path) => Path.GetFullPath(path);
    }

    private sealed class StubSpecificFileReparseResolution : IPathResolution
    {
        private readonly string _filePath;
        private readonly string _targetOutside;
        public StubSpecificFileReparseResolution(string filePath, string targetOutside)
        {
            _filePath = Path.GetFullPath(filePath);
            _targetOutside = Path.GetFullPath(targetOutside);
        }
        public bool IsReparsePoint(string path)
        {
            try { return string.Equals(Path.GetFullPath(path), _filePath, StringComparison.OrdinalIgnoreCase); } catch { return false; }
        }
        public string? ResolveFinalPath(string path)
        {
            try
            {
                if (string.Equals(Path.GetFullPath(path), _filePath, StringComparison.OrdinalIgnoreCase))
                    return _targetOutside;
                return Path.GetFullPath(path);
            }
            catch { return null; }
        }
        public string GetFullPath(string path) => Path.GetFullPath(path);
    }
}
