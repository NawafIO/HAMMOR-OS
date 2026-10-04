using HAMMOR.Core.Tests.TestDoubles;
using HAMMOR.Core.Tools;
using Xunit;

namespace HAMMOR.Core.Tests;

public sealed class ToolRegistryTests
{
    [Fact]
    public void Registered_tools_can_be_found_by_name()
    {
        var registry = new ToolRegistry();
        var tool = new FakeTool("memory.save", ToolPermission.Write);

        registry.Register(tool);

        Assert.Same(tool, registry.Find("memory.save"));
    }

    [Fact]
    public void Lookup_is_case_insensitive()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeTool("memory.search", ToolPermission.Read));

        // A model may emit a differently-cased name; resolving it is
        // preferable to a spurious "no such tool".
        Assert.NotNull(registry.Find("Memory.Search"));
        Assert.NotNull(registry.Find("MEMORY.SEARCH"));
    }

    [Fact]
    public void Unknown_names_resolve_to_null_rather_than_throwing()
    {
        var registry = new ToolRegistry();

        Assert.Null(registry.Find("does.not.exist"));
        Assert.Null(registry.Find(string.Empty));
        Assert.Null(registry.Find("   "));
    }

    /// <summary>
    /// Silent replacement would let one component shadow another's capability,
    /// so a duplicate registration is an error.
    /// </summary>
    [Fact]
    public void Duplicate_registration_throws()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeTool("memory.save", ToolPermission.Write));

        var exception = Assert.Throws<InvalidOperationException>(
            () => registry.Register(new FakeTool("memory.save", ToolPermission.Read)));

        Assert.Contains("memory.save", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_detection_ignores_case()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeTool("memory.save", ToolPermission.Write));

        Assert.Throws<InvalidOperationException>(
            () => registry.Register(new FakeTool("Memory.Save", ToolPermission.Write)));
    }

    [Fact]
    public void Blank_tool_names_are_rejected()
    {
        var registry = new ToolRegistry();

        Assert.Throws<ArgumentException>(
            () => registry.Register(new FakeTool("  ", ToolPermission.Read)));
    }

    [Fact]
    public void All_returns_every_tool_ordered_by_name()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeTool("zeta.tool", ToolPermission.Read));
        registry.Register(new FakeTool("alpha.tool", ToolPermission.Read));
        registry.Register(new FakeTool("mid.tool", ToolPermission.Read));

        var names = registry.All.Select(t => t.Name).ToList();

        Assert.Equal(["alpha.tool", "mid.tool", "zeta.tool"], names);
    }

    /// <summary>
    /// Phase 1 ships no arbitrary-command tool. An Execute-level primitive
    /// that ran any command would make the permission levels meaningless,
    /// since one grant would cover destructive actions too.
    /// </summary>
    [Fact]
    public void Default_registry_contains_no_unrestricted_command_tool()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeTool("memory.save", ToolPermission.Write));
        registry.Register(new FakeTool("memory.search", ToolPermission.Read));

        var forbiddenNames = new[]
        {
            "run_any_command", "run_command", "shell", "exec", "bash", "powershell",
        };

        Assert.DoesNotContain(
            registry.All,
            tool => forbiddenNames.Contains(tool.Name, StringComparer.OrdinalIgnoreCase));
    }
}
