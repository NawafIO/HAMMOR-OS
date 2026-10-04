using System.IO;
using System.Text;
using HAMMOR.Core.Configuration;
using HAMMOR.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using HAMMOR.Core.Permissions;
using HAMMOR.Core.Tests.TestDoubles;
using HAMMOR.Core.Tools;
using HAMMOR.Core.Tools.Filesystem;
using Xunit;

namespace HAMMOR.Core.Tests.Tools.Filesystem;

public sealed class FilesystemToolsTests : IDisposable
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
            catch
            {
                // Best-effort cleanup; test failure is not caused by leftover temp dirs.
            }
        }
    }

    // ---- helpers ------------------------------------------------------

    private string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hammor-fs-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        _tempRoots.Add(root);
        return root;
    }

    private static ToolInvocation Invoke(string toolName, Dictionary<string, string?> args) =>
        new() { ToolName = toolName, Arguments = args };

    private static PermissionEvaluator Evaluator(ToolPermission ceiling, bool alwaysConfirmDestructive = true)
    {
        var configuration = new HammorConfiguration();
        configuration.Security.AutoApproveUpTo = ceiling;
        configuration.Security.AlwaysConfirmDestructive = alwaysConfirmDestructive;
        return new PermissionEvaluator(new FakeConfigurationStore(configuration));
    }

    // ---- ListDirectoryTool --------------------------------------------

    [Fact]
    public async Task ListDirectory_valid_directory_lists_entries()
    {
        var root = CreateTempRoot();
        File.WriteAllText(Path.Combine(root, "a.txt"), "hello");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "sub", "b.txt"), "world");

        var tool = new ListDirectoryTool();
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = root }));

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("a.txt", result.Output, StringComparison.Ordinal);
        Assert.Contains("sub", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListDirectory_includes_file_metadata()
    {
        var root = CreateTempRoot();
        var filePath = Path.Combine(root, "notes.txt");
        File.WriteAllText(filePath, "content");

        var tool = new ListDirectoryTool();
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = root }));

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("[File]", result.Output, StringComparison.Ordinal);
        Assert.Contains("bytes", result.Output, StringComparison.Ordinal);
        Assert.Contains("notes.txt", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListDirectory_non_existent_returns_failure()
    {
        var tool = new ListDirectoryTool();
        var missing = Path.Combine(Path.GetTempPath(), $"hammor-missing-{Guid.NewGuid():N}");
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = missing }));

        Assert.False(result.Succeeded);
        Assert.Contains("does not exist", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListDirectory_path_is_file_returns_failure()
    {
        var root = CreateTempRoot();
        var filePath = Path.Combine(root, "file.txt");
        File.WriteAllText(filePath, "x");

        var tool = new ListDirectoryTool();
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = filePath }));

        Assert.False(result.Succeeded);
        Assert.Contains("not a directory", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListDirectory_recursive_enumerates_nested_files()
    {
        var root = CreateTempRoot();
        var nested = Path.Combine(root, "a", "b");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "deep.txt"), "deep");

        var tool = new ListDirectoryTool();
        var nonRecursive = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = root }));
        var recursive = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = root, ["recursive"] = "true" }));

        Assert.True(nonRecursive.Succeeded, nonRecursive.Error);
        Assert.DoesNotContain("deep.txt", nonRecursive.Output, StringComparison.Ordinal);
        Assert.True(recursive.Succeeded, recursive.Error);
        Assert.Contains("deep.txt", recursive.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListDirectory_empty_directory_reports_empty()
    {
        var root = CreateTempRoot();
        var tool = new ListDirectoryTool();
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = root }));

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("empty", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListDirectory_validate_rejects_blank_path()
    {
        var tool = new ListDirectoryTool();
        var result = tool.Validate(Invoke(tool.Name, new() { ["path"] = "   " }));
        Assert.False(result.IsValid);
    }

    // ---- ReadFileTool -------------------------------------------------

    [Fact]
    public async Task ReadFile_valid_text_file_returns_content()
    {
        var root = CreateTempRoot();
        var filePath = Path.Combine(root, "hello.txt");
        File.WriteAllText(filePath, "line one\nline two\nline three", Encoding.UTF8);

        var tool = new ReadFileTool();
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = filePath }));

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("line one", result.Output, StringComparison.Ordinal);
        Assert.Contains("line three", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadFile_missing_file_returns_failure()
    {
        var tool = new ReadFileTool();
        var missing = Path.Combine(Path.GetTempPath(), $"hammor-missing-{Guid.NewGuid():N}.txt");
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = missing }));

        Assert.False(result.Succeeded);
        Assert.Contains("does not exist", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadFile_directory_instead_of_file_returns_failure()
    {
        var root = CreateTempRoot();
        var tool = new ReadFileTool();
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = root }));

        Assert.False(result.Succeeded);
        Assert.Contains("directory", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadFile_utf8_content_is_preserved()
    {
        var root = CreateTempRoot();
        var filePath = Path.Combine(root, "arabic.txt");
        const string content = "مرحبا هامور — Hello HAMMOR";
        File.WriteAllText(filePath, content, new UTF8Encoding(false));

        var tool = new ReadFileTool();
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = filePath }));

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("مرحبا", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadFile_offset_and_limit_page_correctly()
    {
        var root = CreateTempRoot();
        var filePath = Path.Combine(root, "paged.txt");
        File.WriteAllLines(filePath, Enumerable.Range(0, 10).Select(i => $"line {i}"), Encoding.UTF8);

        var tool = new ReadFileTool();
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = filePath, ["offset"] = "3", ["limit"] = "2" }));

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("line 3", result.Output, StringComparison.Ordinal);
        Assert.Contains("line 4", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("line 2", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("line 5", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadFile_offset_beyond_end_returns_failure()
    {
        var root = CreateTempRoot();
        var filePath = Path.Combine(root, "short.txt");
        File.WriteAllText(filePath, "only one line");

        var tool = new ReadFileTool();
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = filePath, ["offset"] = "999" }));

        Assert.False(result.Succeeded);
        Assert.Contains("beyond end", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadFile_too_large_returns_failure()
    {
        var root = CreateTempRoot();
        var filePath = Path.Combine(root, "huge.bin");

        // Create a file just over the 2 MiB limit without holding it in memory as a string.
        const int overLimit = 2 * 1024 * 1024 + 1024;
        var bytes = new byte[overLimit];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)'a';
        }

        File.WriteAllBytes(filePath, bytes);

        var tool = new ReadFileTool();
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = filePath }));

        Assert.False(result.Succeeded);
        Assert.Contains("too large", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadFile_binary_content_returns_failure()
    {
        var root = CreateTempRoot();
        var filePath = Path.Combine(root, "binary.dat");
        File.WriteAllBytes(filePath, [0x00, 0x01, 0x02, 0x48, 0x65, 0x6C, 0x6C, 0x6F]);

        var tool = new ReadFileTool();
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = filePath }));

        Assert.False(result.Succeeded);
        Assert.Contains("binary", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadFile_unsupported_encoding_is_invalid()
    {
        var tool = new ReadFileTool();
        var result = tool.Validate(Invoke(tool.Name, new() { ["path"] = "C:\\tmp\\x.txt", ["encoding"] = "not-an-encoding" }));
        Assert.False(result.IsValid);
        Assert.Contains("not supported", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    // ---- WriteFileTool ------------------------------------------------

    [Fact]
    public async Task WriteFile_creates_file()
    {
        var root = CreateTempRoot();
        var filePath = Path.Combine(root, "new.txt");

        var tool = new WriteFileTool();
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = filePath, ["content"] = "hello world" }));

        Assert.True(result.Succeeded, result.Error);
        Assert.True(File.Exists(filePath));
        Assert.Equal("hello world", File.ReadAllText(filePath, Encoding.UTF8));
    }

    [Fact]
    public async Task WriteFile_creates_missing_parent_directories()
    {
        var root = CreateTempRoot();
        var filePath = Path.Combine(root, "a", "b", "c.txt");

        var tool = new WriteFileTool();
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = filePath, ["content"] = "nested" }));

        Assert.True(result.Succeeded, result.Error);
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task WriteFile_overwrite_false_refuses_existing_file()
    {
        var root = CreateTempRoot();
        var filePath = Path.Combine(root, "exists.txt");
        File.WriteAllText(filePath, "original");

        var tool = new WriteFileTool();
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = filePath, ["content"] = "replacement", ["overwrite"] = "false" }));

        Assert.False(result.Succeeded);
        Assert.Contains("already exists", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("original", File.ReadAllText(filePath));
    }

    [Fact]
    public async Task WriteFile_overwrite_true_replaces_existing_file()
    {
        var root = CreateTempRoot();
        var filePath = Path.Combine(root, "exists2.txt");
        File.WriteAllText(filePath, "original");

        var tool = new WriteFileTool();
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = filePath, ["content"] = "replacement", ["overwrite"] = "true" }));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("replacement", File.ReadAllText(filePath, Encoding.UTF8));
    }

    [Fact]
    public async Task WriteFile_default_is_no_overwrite()
    {
        var root = CreateTempRoot();
        var filePath = Path.Combine(root, "default.txt");
        File.WriteAllText(filePath, "original");

        var tool = new WriteFileTool();
        // No overwrite argument — default false must still refuse.
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = filePath, ["content"] = "new" }));

        Assert.False(result.Succeeded);
        Assert.Contains("already exists", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteFile_directory_target_returns_failure()
    {
        var root = CreateTempRoot();
        var tool = new WriteFileTool();
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = root, ["content"] = "x" }));

        Assert.False(result.Succeeded);
        Assert.Contains("directory", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriteFile_validate_rejects_missing_content()
    {
        var tool = new WriteFileTool();
        var result = tool.Validate(Invoke(tool.Name, new() { ["path"] = "C:\\tmp\\x.txt" }));
        Assert.False(result.IsValid);
        Assert.Contains("content", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    // ---- DeleteFileTool -----------------------------------------------

    [Fact]
    public async Task DeleteFile_deletes_target_file()
    {
        var root = CreateTempRoot();
        var filePath = Path.Combine(root, "to-delete.txt");
        File.WriteAllText(filePath, "bye");

        var tool = new DeleteFileTool();
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = filePath }));

        Assert.True(result.Succeeded, result.Error);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task DeleteFile_missing_file_returns_failure()
    {
        var tool = new DeleteFileTool();
        var missing = Path.Combine(Path.GetTempPath(), $"hammor-missing-{Guid.NewGuid():N}.txt");
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = missing }));

        Assert.False(result.Succeeded);
        Assert.Contains("does not exist", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteFile_cannot_delete_directory()
    {
        var root = CreateTempRoot();
        var tool = new DeleteFileTool();
        var result = await tool.ExecuteAsync(Invoke(tool.Name, new() { ["path"] = root }));

        Assert.False(result.Succeeded);
        Assert.Contains("directory", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public void DeleteFile_permission_is_destructive()
    {
        var tool = new DeleteFileTool();
        Assert.Equal(ToolPermission.Destructive, tool.Permission);
    }

    [Fact]
    public void DeleteFile_policy_requires_confirmation_even_at_highest_ceiling()
    {
        var tool = new DeleteFileTool();
        var evaluator = Evaluator(ToolPermission.Destructive, alwaysConfirmDestructive: true);
        var decision = evaluator.Evaluate(tool, Invoke(tool.Name, new() { ["path"] = "C:\\tmp\\x.txt" }));

        Assert.Equal(PermissionOutcome.ConfirmationRequired, decision.Outcome);
    }

    [Fact]
    public async Task DeleteFile_policy_denies_without_user_approval()
    {
        var tool = new DeleteFileTool();
        var evaluator = Evaluator(ToolPermission.Destructive, alwaysConfirmDestructive: true);
        var denying = new RecordingConfirmationService(approve: false);

        var decision = await evaluator.AuthoriseAsync(tool, Invoke(tool.Name, new() { ["path"] = "C:\\tmp\\x.txt" }), denying);

        Assert.Equal(PermissionOutcome.Denied, decision.Outcome);
        Assert.Equal(1, denying.CallCount);
    }

    [Fact]
    public async Task DeleteFile_policy_allows_with_user_approval()
    {
        var tool = new DeleteFileTool();
        var evaluator = Evaluator(ToolPermission.Destructive, alwaysConfirmDestructive: true);
        var approving = new RecordingConfirmationService(approve: true);

        var decision = await evaluator.AuthoriseAsync(tool, Invoke(tool.Name, new() { ["path"] = "C:\\tmp\\x.txt" }), approving);

        Assert.True(decision.IsAllowed);
        Assert.Equal(1, approving.CallCount);
    }

    // ---- Registry + permission mapping --------------------------------

    [Fact]
    public void Registry_maps_all_four_filesystem_tools_to_expected_permissions()
    {
        var registry = new ToolRegistry();
        registry.Register(new ListDirectoryTool());
        registry.Register(new ReadFileTool());
        registry.Register(new WriteFileTool());
        registry.Register(new DeleteFileTool());

        Assert.Equal(ToolPermission.Read, registry.Find("filesystem.list_directory")!.Permission);
        Assert.Equal(ToolPermission.Read, registry.Find("filesystem.read_file")!.Permission);
        Assert.Equal(ToolPermission.Write, registry.Find("filesystem.write_file")!.Permission);
        Assert.Equal(ToolPermission.Destructive, registry.Find("filesystem.delete_file")!.Permission);
    }

    [Fact]
    public void Default_infrastructure_registry_contains_all_four_filesystem_tools()
    {
        // Build the real registry via DI — this is the same registry the app
        // and AgentPipeline use, so it covers the integration path.
        var services = new ServiceCollection();
        services.AddHammorInfrastructure(dataRoot: Path.Combine(Path.GetTempPath(), $"hammor-di-{Guid.NewGuid():N}"));
        // Platform secret store is not needed for this check; fake confirmation is not registered.
        // Add a no-op confirmation service so the pipeline can construct.
        services.AddSingleton<HAMMOR.Core.Permissions.IConfirmationService>(new RecordingConfirmationService(true));
        // Replace the File-based config with in-memory so test does not touch %LocalAppData%.
        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IToolRegistry>();

        Assert.NotNull(registry.Find("filesystem.list_directory"));
        Assert.NotNull(registry.Find("filesystem.read_file"));
        Assert.NotNull(registry.Find("filesystem.write_file"));
        Assert.NotNull(registry.Find("filesystem.delete_file"));
    }

    [Fact]
    public void Filesystem_tools_do_not_include_arbitrary_command_tool()
    {
        var registry = new ToolRegistry();
        registry.Register(new ListDirectoryTool());
        registry.Register(new ReadFileTool());
        registry.Register(new WriteFileTool());
        registry.Register(new DeleteFileTool());

        var forbidden = new[] { "run_any_command", "run_command", "shell", "exec", "bash", "powershell" };
        Assert.DoesNotContain(registry.All, t => forbidden.Contains(t.Name, StringComparer.OrdinalIgnoreCase));
    }
}
