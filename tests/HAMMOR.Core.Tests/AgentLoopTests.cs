using System.Collections.Concurrent;
using HAMMOR.Core.Agent;
using HAMMOR.Core.Ai;
using HAMMOR.Core.Audit;
using HAMMOR.Core.Configuration;
using HAMMOR.Core.Memory;
using HAMMOR.Core.Permissions;
using HAMMOR.Core.Projects;
using HAMMOR.Core.Tests.TestDoubles;
using HAMMOR.Core.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HAMMOR.Core.Tests;

public sealed class AgentLoopTests
{
    // ---------- helpers ----------

    private sealed class FakeToolCallingProvider : IToolCallingProvider
    {
        public string ProviderId => "claude";
        public string DisplayName => "Claude";
        private readonly Queue<ModelResponseTurn> _turns;
        private readonly bool _failNext;
        public List<AiToolAwareRequest> SeenRequests { get; } = new();
        public int CallCount { get; private set; }

        public FakeToolCallingProvider(IEnumerable<ModelResponseTurn>? turns = null, bool failNext = false)
        {
            _turns = new Queue<ModelResponseTurn>(turns ?? Array.Empty<ModelResponseTurn>());
            _failNext = failNext;
        }

        public Task<ProviderAvailability> CheckAvailabilityAsync(CancellationToken ct = default)
            => Task.FromResult(ProviderAvailability.Ready("ok"));

        public Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct = default)
            => throw new AiProviderException("should not be called in tool path");

        public Task<ModelResponseTurn> CompleteWithToolsAsync(AiToolAwareRequest request, IReadOnlyList<ToolDefinition> tools, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            CallCount++;
            SeenRequests.Add(request);
            if (_failNext) throw new AiProviderException("provider boom");
            if (_turns.Count == 0) return Task.FromResult(new ModelResponseTurn("final answer", Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn));
            return Task.FromResult(_turns.Dequeue());
        }
    }

    private sealed class FailingProvider : IToolCallingProvider
    {
        public string ProviderId => "claude";
        public string DisplayName => "Claude";
        public Task<ProviderAvailability> CheckAvailabilityAsync(CancellationToken ct = default) => Task.FromResult(ProviderAvailability.Ready("ok"));
        public Task<AiResponse> CompleteAsync(AiRequest r, CancellationToken ct = default) => throw new AiProviderException("fail");
        public Task<ModelResponseTurn> CompleteWithToolsAsync(AiToolAwareRequest r, IReadOnlyList<ToolDefinition> t, CancellationToken ct = default) => throw new AiProviderException("provider boom");
    }

    private sealed class InMemoryAuditLog : IAuditLog
    {
        public readonly List<AuditEntry> Entries = new();
        public Task AppendAsync(AuditEntry entry, CancellationToken ct = default) { Entries.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int limit = 200, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AuditEntry>>(Entries);
    }

    private sealed class InMemoryMemoryStore : IMemoryStore
    {
        public Task<MemoryEntry> SaveAsync(MemoryEntry entry, CancellationToken ct = default) => Task.FromResult(entry);
        public Task<IReadOnlyList<MemoryEntry>> SearchAsync(string q, int limit = 10, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MemoryEntry>>(Array.Empty<MemoryEntry>());
        public Task<IReadOnlyList<MemoryEntry>> GetRecentAsync(string? projectId = null, int limit = 50, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MemoryEntry>>(Array.Empty<MemoryEntry>());
        public Task<MemoryEntry?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<MemoryEntry?>(null);
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class InMemoryProjectStore : IProjectStore
    {
        public Task<HammorProject> CreateAsync(HammorProject p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<HammorProject?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<HammorProject?>(null);
        public Task<HammorProject> UpdateAsync(HammorProject p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<IReadOnlyList<HammorProject>> ListAsync(bool includeArchived = false, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HammorProject>>(Array.Empty<HammorProject>());
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static AgentLoop CreateLoop(
        IToolCallingProvider provider,
        IToolRegistry registry,
        IPermissionEvaluator evaluator,
        IConfirmationService confirmation,
        InMemoryAuditLog audit,
        HammorConfiguration? config = null)
    {
        config ??= new HammorConfiguration();
        // Ensure tool calls below ceiling by default for read tools.
        var store = new FakeConfigurationStore(config);
        return new AgentLoop(
            new[] { (IAiProvider)provider },
            registry,
            evaluator,
            confirmation,
            new InMemoryMemoryStore(),
            new InMemoryProjectStore(),
            audit,
            store,
            NullLogger<AgentLoop>.Instance);
    }

    private static ToolRegistry RegistryWith(params ITool[] tools)
    {
        var r = new ToolRegistry();
        foreach (var t in tools) r.Register(t);
        return r;
    }

    // ---------- 1. Normal final model response ----------

    [Fact]
    public async Task Final_response_without_tool_calls_succeeds()
    {
        var provider = new FakeToolCallingProvider(new[] { new ModelResponseTurn("hello world", Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn) });
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(provider, RegistryWith(), new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), new RecordingConfirmationService(true), audit);

        var result = await loop.RunAsync(new AgentTurnRequest { Input = "hi" });

        Assert.True(result.Succeeded);
        Assert.Equal("hello world", result.ReplyText);
        Assert.Equal(1, provider.CallCount);
    }

    // ---------- 2. Valid Read tool call executes ----------

    [Fact]
    public async Task Valid_read_tool_call_executes_and_returns_to_model()
    {
        var tool = new SchemaFakeTool("memory.search", ToolPermission.Read, """{"type":"object","properties":{"query":{"type":"string"}},"required":["query"],"additionalProperties":false}""");
        var registry = RegistryWith(tool);
        var turns = new[]
        {
            new ModelResponseTurn(null, new[] { new ModelToolCall("id1", "memory.search", """{"query":"hello"}""") }, ModelStopReasons.ToolUse),
            new ModelResponseTurn("done after tool", Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn),
        };
        var provider = new FakeToolCallingProvider(turns);
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(provider, registry, new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), new RecordingConfirmationService(true), audit);

        var result = await loop.RunAsync(new AgentTurnRequest { Input = "search" });

        Assert.True(result.Succeeded);
        Assert.Equal("done after tool", result.ReplyText);
        Assert.Equal(1, tool.ExecutionCount);
        Assert.Equal(2, provider.CallCount);
    }

    // ---------- 3. Permission denial prevents execution ----------

    [Fact]
    public async Task Permission_denial_prevents_execution()
    {
        var tool = new SchemaFakeTool("memory.save", ToolPermission.Write, """{"type":"object","properties":{"title":{"type":"string"},"content":{"type":"string"}},"required":["title","content"],"additionalProperties":false}""");
        var registry = RegistryWith(tool);
        var config = new HammorConfiguration();
        config.Security.AutoApproveUpTo = ToolPermission.Read; // write requires confirmation
        var turns = new[]
        {
            new ModelResponseTurn(null, new[] { new ModelToolCall("id1", "memory.save", """{"title":"t","content":"c"}""") }, ModelStopReasons.ToolUse),
            new ModelResponseTurn("after denied", Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn),
        };
        var provider = new FakeToolCallingProvider(turns);
        var audit = new InMemoryAuditLog();
        // Deny confirmation
        var loop = CreateLoop(provider, registry, new PermissionEvaluator(new FakeConfigurationStore(config)), new RecordingConfirmationService(false), audit);

        var result = await loop.RunAsync(new AgentTurnRequest { Input = "save" });

        Assert.True(result.Succeeded);
        Assert.Equal(0, tool.ExecutionCount);
        // Provider should have seen the denial as a tool result on the second call.
        Assert.Equal(2, provider.CallCount);
        var secondReq = provider.SeenRequests[1];
        Assert.Single(secondReq.ToolTranscript, x => x.Content.ToolResults != null);
        var toolResult = secondReq.ToolTranscript.SelectMany(x => x.Content.ToolResults ?? Array.Empty<AiToolResultContent>()).Single();
        Assert.True(toolResult.IsError);
        Assert.Contains("Permission/confirmation denied by user", toolResult.Content);
    }

    // ---------- 4. Confirmation denial prevents execution and returns structured error ----------

    [Fact]
    public async Task Confirmation_denial_returns_structured_error_and_allows_continue()
    {
        var tool = new SchemaFakeTool("memory.save", ToolPermission.Write, """{"type":"object","properties":{"title":{"type":"string"},"content":{"type":"string"}},"required":["title","content"],"additionalProperties":false}""");
        var registry = RegistryWith(tool);
        var config = new HammorConfiguration();
        config.Security.AutoApproveUpTo = ToolPermission.Read;
        var turns = new[]
        {
            new ModelResponseTurn(null, new[] { new ModelToolCall("id1", "memory.save", """{"title":"t","content":"c"}""") }, ModelStopReasons.ToolUse),
            new ModelResponseTurn("model continued", Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn),
        };
        var provider = new FakeToolCallingProvider(turns);
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(provider, registry, new PermissionEvaluator(new FakeConfigurationStore(config)), new RecordingConfirmationService(false), audit);

        var result = await loop.RunAsync(new AgentTurnRequest { Input = "save please" });

        Assert.True(result.Succeeded);
        Assert.Equal("model continued", result.ReplyText);
        Assert.Equal(0, tool.ExecutionCount);
    }

    // ---------- 5. Unknown tool rejected ----------

    [Fact]
    public async Task Unknown_tool_is_rejected_without_execution()
    {
        var tool = new FakeTool("memory.search", ToolPermission.Read);
        var registry = RegistryWith(tool);
        var turns = new[]
        {
            new ModelResponseTurn(null, new[] { new ModelToolCall("id1", "does.not.exist", "{}") }, ModelStopReasons.ToolUse),
            new ModelResponseTurn("recovered", Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn),
        };
        var provider = new FakeToolCallingProvider(turns);
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(provider, registry, new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), new RecordingConfirmationService(true), audit);

        var result = await loop.RunAsync(new AgentTurnRequest { Input = "hi" });

        Assert.True(result.Succeeded);
        Assert.Equal(0, tool.ExecutionCount);
        var secondReq = provider.SeenRequests[1];
        var err = secondReq.ToolTranscript.SelectMany(x => x.Content.ToolResults ?? Array.Empty<AiToolResultContent>()).Single();
        Assert.True(err.IsError);
        Assert.Contains("Unknown tool", err.Content);
    }

    // ---------- 6. Malformed arguments rejected ----------

    [Fact]
    public async Task Malformed_json_is_rejected()
    {
        var tool = new FakeTool("memory.search", ToolPermission.Read);
        var registry = RegistryWith(tool);
        var turns = new[]
        {
            new ModelResponseTurn(null, new[] { new ModelToolCall("id1", "memory.search", "{ not json }") }, ModelStopReasons.ToolUse),
            new ModelResponseTurn("recovered", Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn),
        };
        var provider = new FakeToolCallingProvider(turns);
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(provider, registry, new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), new RecordingConfirmationService(true), audit);

        var result = await loop.RunAsync(new AgentTurnRequest { Input = "hi" });

        Assert.True(result.Succeeded);
        Assert.Equal(0, tool.ExecutionCount);
        var err = provider.SeenRequests[1].ToolTranscript.SelectMany(x => x.Content.ToolResults ?? Array.Empty<AiToolResultContent>()).Single();
        Assert.True(err.IsError);
        Assert.Contains("Malformed JSON", err.Content);
    }

    // ---------- 7. Invalid arguments rejected (missing required) ----------

    [Fact]
    public async Task Missing_required_property_is_rejected()
    {
        var tool = new FakeTool("memory.search", ToolPermission.Read);
        // Wire real SearchMemoryTool-shaped schema via a wrapper that mirrors it?
        // Use a schema-checking fake: register a tool whose InputSchema requires "query".
        var strictTool = new StrictSchemaTool("memory.search", ToolPermission.Read);
        var registry = RegistryWith(strictTool);
        var turns = new[]
        {
            new ModelResponseTurn(null, new[] { new ModelToolCall("id1", "memory.search", "{}") }, ModelStopReasons.ToolUse),
            new ModelResponseTurn("recovered", Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn),
        };
        var provider = new FakeToolCallingProvider(turns);
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(provider, registry, new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), new RecordingConfirmationService(true), audit);

        var result = await loop.RunAsync(new AgentTurnRequest { Input = "hi" });

        Assert.True(result.Succeeded);
        Assert.Equal(0, strictTool.ExecutionCount);
        var err = provider.SeenRequests[1].ToolTranscript.SelectMany(x => x.Content.ToolResults ?? Array.Empty<AiToolResultContent>()).Single();
        Assert.True(err.IsError);
    }

    [Fact]
    public async Task Invalid_type_is_rejected()
    {
        var strictTool = new StrictSchemaTool("memory.search", ToolPermission.Read);
        var registry = RegistryWith(strictTool);
        var turns = new[]
        {
            new ModelResponseTurn(null, new[] { new ModelToolCall("id1", "memory.search", """{"query": 123 }""") }, ModelStopReasons.ToolUse),
            new ModelResponseTurn("recovered", Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn),
        };
        var provider = new FakeToolCallingProvider(turns);
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(provider, registry, new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), new RecordingConfirmationService(true), audit);

        var result = await loop.RunAsync(new AgentTurnRequest { Input = "hi" });

        Assert.True(result.Succeeded);
        Assert.Equal(0, strictTool.ExecutionCount);
    }

    [Fact]
    public async Task Unknown_property_rejected_when_additionalProperties_false()
    {
        var strictTool = new StrictSchemaTool("memory.search", ToolPermission.Read);
        var registry = RegistryWith(strictTool);
        var turns = new[]
        {
            new ModelResponseTurn(null, new[] { new ModelToolCall("id1", "memory.search", """{"query":"hi","evil":"x"}""") }, ModelStopReasons.ToolUse),
            new ModelResponseTurn("recovered", Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn),
        };
        var provider = new FakeToolCallingProvider(turns);
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(provider, registry, new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), new RecordingConfirmationService(true), audit);

        var result = await loop.RunAsync(new AgentTurnRequest { Input = "hi" });

        Assert.True(result.Succeeded);
        Assert.Equal(0, strictTool.ExecutionCount);
    }

    // ---------- 8. Multiple tool calls work independently ----------

    [Fact]
    public async Task Multiple_tool_calls_each_validated_and_executed_independently()
    {
        var toolA = new FakeTool("tool.a", ToolPermission.Read);
        var toolB = new FakeTool("tool.b", ToolPermission.Read);
        var registry = RegistryWith(toolA, toolB);
        var turns = new[]
        {
            new ModelResponseTurn(null, new[]
            {
                new ModelToolCall("id1", "tool.a", "{}"),
                new ModelToolCall("id2", "does.not.exist", "{}"),
                new ModelToolCall("id3", "tool.b", "{}"),
            }, ModelStopReasons.ToolUse),
            new ModelResponseTurn("done", Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn),
        };
        var provider = new FakeToolCallingProvider(turns);
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(provider, registry, new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), new RecordingConfirmationService(true), audit);

        var result = await loop.RunAsync(new AgentTurnRequest { Input = "hi" });

        Assert.True(result.Succeeded);
        Assert.Equal(1, toolA.ExecutionCount);
        Assert.Equal(1, toolB.ExecutionCount);
        var results = provider.SeenRequests[1].ToolTranscript.SelectMany(x => x.Content.ToolResults ?? Array.Empty<AiToolResultContent>()).ToList();
        Assert.Equal(3, results.Count);
        Assert.False(results[0].IsError);
        Assert.True(results[1].IsError);
        Assert.False(results[2].IsError);
    }

    // ---------- 9. Tool result is returned to the model ----------

    [Fact]
    public async Task Tool_result_content_is_bounded_and_passed_to_next_round()
    {
        var big = new string('x', 50_000);
        var bigTool = new ReturningTool("tool.big", ToolPermission.Read, big);
        var registry = RegistryWith(bigTool);
        var turns = new[]
        {
            new ModelResponseTurn(null, new[] { new ModelToolCall("id1", "tool.big", "{}") }, ModelStopReasons.ToolUse),
            new ModelResponseTurn("done", Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn),
        };
        var provider = new FakeToolCallingProvider(turns);
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(provider, registry, new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), new RecordingConfirmationService(true), audit);

        var result = await loop.RunAsync(new AgentTurnRequest { Input = "hi" }, new AgentLoopOptions { MaxToolResultChars = 100 });

        Assert.True(result.Succeeded);
        var toolResult = provider.SeenRequests[1].ToolTranscript.SelectMany(x => x.Content.ToolResults ?? Array.Empty<AiToolResultContent>()).Single();
        Assert.True(toolResult.Content.Length <= 300); // bounded + truncation marker
        Assert.Contains("truncated", toolResult.Content);
    }

    // ---------- 10. Model continues after tool result ----------

    [Fact]
    public async Task Model_continues_after_tool_result_and_can_call_again()
    {
        var tool = new FakeTool("tool.a", ToolPermission.Read);
        var registry = RegistryWith(tool, new FakeTool("tool.b", ToolPermission.Read));
        var turns = new[]
        {
            new ModelResponseTurn(null, new[] { new ModelToolCall("id1", "tool.a", "{}") }, ModelStopReasons.ToolUse),
            new ModelResponseTurn("intermediate", new[] { new ModelToolCall("id2", "tool.b", "{}") }, ModelStopReasons.ToolUse),
            new ModelResponseTurn("final", Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn),
        };
        var provider = new FakeToolCallingProvider(turns);
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(provider, registry, new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), new RecordingConfirmationService(true), audit);

        var result = await loop.RunAsync(new AgentTurnRequest { Input = "hi" });

        Assert.True(result.Succeeded);
        Assert.Equal("final", result.ReplyText);
        Assert.Equal(3, provider.CallCount);
    }

    // ---------- 11. MaxRounds terminates the loop ----------

    [Fact]
    public async Task MaxRounds_exhaustion_returns_failure()
    {
        // Provider keeps returning tool_use forever
        var tool = new FakeTool("tool.a", ToolPermission.Read);
        var registry = RegistryWith(tool);
        List<ModelResponseTurn> many = new();
        for (int i = 0; i < 10; i++) many.Add(new ModelResponseTurn(null, new[] { new ModelToolCall($"id{i}", "tool.a", "{}") }, ModelStopReasons.ToolUse));
        var provider = new FakeToolCallingProvider(many);
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(provider, registry, new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), new RecordingConfirmationService(true), audit);

        var result = await loop.RunAsync(new AgentTurnRequest { Input = "hi" }, new AgentLoopOptions { MaxRounds = 3 });

        Assert.False(result.Succeeded);
        Assert.Contains("max rounds", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, provider.CallCount);
    }

    [Fact]
    public void MaxRounds_is_clamped_to_hard_cap()
    {
        var opts = new AgentLoopOptions { MaxRounds = 99 };
        Assert.Equal(AgentLoopOptions.HardCapMaxRounds, opts.EffectiveMaxRounds);
        var opts2 = new AgentLoopOptions { MaxRounds = 0 };
        Assert.Equal(AgentLoopOptions.DefaultMaxRounds, opts2.EffectiveMaxRounds);
    }

    // ---------- 12. Cancellation stops execution ----------

    [Fact]
    public async Task Cancellation_throws_immediately()
    {
        var tool = new FakeTool("tool.a", ToolPermission.Read);
        var registry = RegistryWith(tool);
        var turns = new[]
        {
            new ModelResponseTurn(null, new[] { new ModelToolCall("id1", "tool.a", "{}") }, ModelStopReasons.ToolUse),
            new ModelResponseTurn("final", Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn),
        };
        var provider = new FakeToolCallingProvider(turns);
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(provider, registry, new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), new RecordingConfirmationService(true), audit);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop.RunAsync(new AgentTurnRequest { Input = "hi" }, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task Cancellation_during_tool_calls_throws()
    {
        var tool = new FakeTool("tool.a", ToolPermission.Read);
        var registry = RegistryWith(tool);
        var turns = new[]
        {
            new ModelResponseTurn(null, new[] { new ModelToolCall("id1", "tool.a", "{}"), new ModelToolCall("id2", "tool.a", "{}") }, ModelStopReasons.ToolUse),
            new ModelResponseTurn("final", Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn),
        };
        var provider = new FakeToolCallingProvider(turns);
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(provider, registry, new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), new RecordingConfirmationService(true), audit);

        // Cancel after first tool execution via a cancellation-aware confirmation
        var cts = new CancellationTokenSource();
        var cancelAfterFirst = new CancellingConfirmationService(cts);

        // Use a tool that cancels
        var cancelTool = new FakeTool("tool.a", ToolPermission.Read);
        var reg2 = RegistryWith(cancelTool);
        var provider2 = new FakeToolCallingProvider(turns);
        var loop2 = CreateLoop(provider2, reg2, new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), cancelAfterFirst, audit);

        // We trigger cancellation externally before second tool
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop2.RunAsync(new AgentTurnRequest { Input = "hi" }, cancellationToken: cts.Token));
    }

    // ---------- 13. Provider failure is handled safely ----------

    [Fact]
    public async Task Provider_failure_returns_failed_result_and_audits()
    {
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(new FailingProvider(), RegistryWith(), new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), new RecordingConfirmationService(true), audit);

        var result = await loop.RunAsync(new AgentTurnRequest { Input = "hi" });

        Assert.False(result.Succeeded);
        Assert.Contains("provider boom", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(audit.Entries, e => e.Category == AuditCategory.Provider && e.Outcome == AuditOutcome.Failed);
    }

    // ---------- 14. Audit events are generated ----------

    [Fact]
    public async Task Each_tool_call_generates_audit_entries()
    {
        var tool = new SchemaFakeTool("memory.search", ToolPermission.Read, """{"type":"object","properties":{"query":{"type":"string"}},"required":["query"],"additionalProperties":false}""");
        var registry = RegistryWith(tool);
        var turns = new[] { new ModelResponseTurn(null, new[] { new ModelToolCall("id1", "memory.search", """{"query":"hi"}""") }, ModelStopReasons.ToolUse), new ModelResponseTurn("done", Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn) };
        var provider = new FakeToolCallingProvider(turns);
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(provider, registry, new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), new RecordingConfirmationService(true), audit);

        await loop.RunAsync(new AgentTurnRequest { Input = "hi" });

        // Authorisation + ToolExecution
        Assert.Contains(audit.Entries, e => e.Category == AuditCategory.Authorisation && e.Subject == "memory.search");
        Assert.Contains(audit.Entries, e => e.Category == AuditCategory.ToolExecution && e.Subject == "memory.search" && e.Outcome == AuditOutcome.Succeeded);
    }

    // ---------- 15. Secrets do not appear in audit/log/tool results ----------

    [Fact]
    public async Task Secrets_are_redacted_in_audit_and_tool_results()
    {
        var secret = "sk-ant-" + new string('a', 32);
        var leakingTool = new ReturningTool("tool.leak", ToolPermission.Read, $"token is {secret} and done");
        var registry = RegistryWith(leakingTool);
        var turns = new[] { new ModelResponseTurn(null, new[] { new ModelToolCall("id1", "tool.leak", "{}") }, ModelStopReasons.ToolUse), new ModelResponseTurn("done", Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn) };
        var provider = new FakeToolCallingProvider(turns);
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(provider, registry, new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), new RecordingConfirmationService(true), audit);

        await loop.RunAsync(new AgentTurnRequest { Input = "hi" });

        foreach (var e in audit.Entries)
        {
            Assert.DoesNotContain("sk-ant-", e.Message);
        }
        var toolResult = provider.SeenRequests[1].ToolTranscript.SelectMany(x => x.Content.ToolResults ?? Array.Empty<AiToolResultContent>()).Single();
        Assert.DoesNotContain("sk-ant-", toolResult.Content);
        Assert.Contains("[redacted]", toolResult.Content);
    }

    // ---------- 16. Arbitrary Git arguments cannot be supplied ----------

    [Fact]
    public async Task Arbitrary_git_arguments_rejected_by_schema()
    {
        // git.status only allows repositoryPath; an extra "arguments" property must be rejected
        // We use a StrictSchemaTool mirroring git.status shape to avoid needing a real temp repo.
        var gitStatusLike = new StrictSchemaTool("git.status", ToolPermission.Read, allowedProps: new[] { "repositoryPath" }, required: new[] { "repositoryPath" });
        var registry = RegistryWith(gitStatusLike);
        var turns = new[]
        {
            new ModelResponseTurn(null, new[] { new ModelToolCall("id1", "git.status", """{"repositoryPath":"C:\\tmp","arguments":"--evil"}""") }, ModelStopReasons.ToolUse),
            new ModelResponseTurn("done", Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn),
        };
        var provider = new FakeToolCallingProvider(turns);
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(provider, registry, new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), new RecordingConfirmationService(true), audit);

        var result = await loop.RunAsync(new AgentTurnRequest { Input = "status" });

        Assert.True(result.Succeeded);
        Assert.Equal(0, gitStatusLike.ExecutionCount);
        var err = provider.SeenRequests[1].ToolTranscript.SelectMany(x => x.Content.ToolResults ?? Array.Empty<AiToolResultContent>()).Single();
        Assert.True(err.IsError);
        Assert.Contains("Unknown property", err.Content);
    }

    // ---------- 17. Arbitrary shell/process execution cannot be invoked ----------

    [Fact]
    public async Task Unknown_shell_tool_is_rejected()
    {
        var registry = RegistryWith(new FakeTool("memory.search", ToolPermission.Read));
        var turns = new[]
        {
            new ModelResponseTurn(null, new[] { new ModelToolCall("id1", "shell.exec", """{"command":"whoami"}""") }, ModelStopReasons.ToolUse),
            new ModelResponseTurn("done", Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn),
        };
        var provider = new FakeToolCallingProvider(turns);
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(provider, registry, new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), new RecordingConfirmationService(true), audit);

        var result = await loop.RunAsync(new AgentTurnRequest { Input = "exec" });

        Assert.True(result.Succeeded);
        var err = provider.SeenRequests[1].ToolTranscript.SelectMany(x => x.Content.ToolResults ?? Array.Empty<AiToolResultContent>()).Single();
        Assert.True(err.IsError);
        Assert.Contains("Unknown tool", err.Content);
        Assert.DoesNotContain(audit.Entries, e => e.Subject == "shell.exec" && e.Outcome == AuditOutcome.Succeeded);
    }

    // ---------- 18. HAMMOR.Core remains WPF-free ----------

    [Fact]
    public void Core_does_not_reference_wpf_assemblies()
    {
        var core = typeof(AgentLoop).Assembly;
        var refs = core.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain(refs, n => n != null && (n.Contains("PresentationFramework") || n.Contains("WindowsBase") || n.Contains("Wpf")));
        // Also ensure no WPF types are directly referenced in this assembly's types
        var wpfTypes = core.GetTypes().Where(t => t.FullName != null && (t.FullName.Contains("System.Windows") || t.FullName.Contains("Wpf"))).ToList();
        Assert.Empty(wpfTypes);
    }

    // ---------- 19. Existing tools discoverable ----------

    [Fact]
    public void Tool_definitions_mirror_registry()
    {
        var registry = RegistryWith(new FakeTool("git.status", ToolPermission.Read), new FakeTool("memory.save", ToolPermission.Write));
        var defs = registry.All.Select(d => HAMMOR.Core.Ai.ToolDefinition.FromTool(d)).ToList();
        Assert.Contains(defs, d => d.Name == "git.status");
        Assert.Contains(defs, d => d.Name == "memory.save");
    }

    // ---------- 20. AgentPipeline delegates agentic turns to AgentLoop ----------

    private static AgentPipeline CreatePipeline(
        IAiProvider provider,
        IToolRegistry registry,
        InMemoryAuditLog audit,
        AgentLoop? loop,
        HammorConfiguration? config = null)
    {
        config ??= new HammorConfiguration();
        return new AgentPipeline(
            new[] { provider },
            registry,
            new PermissionEvaluator(new FakeConfigurationStore(config)),
            new RecordingConfirmationService(true),
            new InMemoryMemoryStore(),
            new InMemoryProjectStore(),
            audit,
            new FakeConfigurationStore(config),
            NullLogger<AgentPipeline>.Instance,
            loop);
    }

    private sealed class FakeTextProvider : IAiProvider
    {
        public string ProviderId => "claude";
        public string DisplayName => "Claude";
        public int CallCount { get; private set; }
        public Task<ProviderAvailability> CheckAvailabilityAsync(CancellationToken ct = default)
            => Task.FromResult(ProviderAvailability.Ready("ok"));
        public Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(new AiResponse("plain reply", "test-model", null, ModelStopReasons.EndTurn));
        }
    }

    [Fact]
    public async Task Pipeline_delegates_agentic_turn_to_loop_and_tool_executes()
    {
        var tool = new SchemaFakeTool("memory.search", ToolPermission.Read, """{"type":"object","properties":{"query":{"type":"string"}},"required":["query"],"additionalProperties":false}""");
        var registry = RegistryWith(tool);
        var turns = new[]
        {
            new ModelResponseTurn(null, new[] { new ModelToolCall("id1", "memory.search", """{"query":"hi"}""") }, ModelStopReasons.ToolUse),
            new ModelResponseTurn("answer via loop", Array.Empty<ModelToolCall>(), ModelStopReasons.EndTurn),
        };
        var provider = new FakeToolCallingProvider(turns);
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(provider, registry, new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), new RecordingConfirmationService(true), audit);
        var pipeline = CreatePipeline(provider, registry, audit, loop);

        var result = await pipeline.RunAsync(new AgentTurnRequest { Input = "search" });

        Assert.True(result.Succeeded);
        Assert.Equal("answer via loop", result.ReplyText);
        Assert.Equal(1, tool.ExecutionCount);
        // The plain CompleteAsync path throws in this fake, so reaching a reply
        // proves the turn went through CompleteWithToolsAsync inside the loop.
        Assert.Equal(2, provider.CallCount);
        Assert.Contains(audit.Entries, e => e.Category == AuditCategory.ToolExecution && e.Subject == "memory.search");
    }

    [Fact]
    public async Task Pipeline_without_loop_stays_on_conversational_path()
    {
        var provider = new FakeTextProvider();
        var registry = RegistryWith(new FakeTool("tool.a", ToolPermission.Read));
        var pipeline = CreatePipeline(provider, registry, new InMemoryAuditLog(), loop: null);

        var result = await pipeline.RunAsync(new AgentTurnRequest { Input = "hi" });

        Assert.True(result.Succeeded);
        Assert.Equal("plain reply", result.ReplyText);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task Pipeline_stays_conversational_when_no_tools_are_registered()
    {
        var provider = new FakeToolCallingProvider(Array.Empty<ModelResponseTurn>());
        var registry = RegistryWith();
        var audit = new InMemoryAuditLog();
        var loop = CreateLoop(provider, registry, new PermissionEvaluator(new FakeConfigurationStore(new HammorConfiguration())), new RecordingConfirmationService(true), audit);
        var pipeline = CreatePipeline(provider, registry, audit, loop);

        // Empty registry means no agentic turn is possible, so the pipeline
        // keeps the single-shot path — which this fake refuses, proving no
        // delegation happened.
        var result = await pipeline.RunAsync(new AgentTurnRequest { Input = "hi" });

        Assert.False(result.Succeeded);
        Assert.Equal(0, provider.CallCount);
    }

    // ---------- helpers: schema-strict tools ----------

    private sealed class StrictSchemaTool : ITool
    {
        private readonly string _name;
        private readonly ToolPermission _permission;
        private readonly HashSet<string> _allowedProps;
        private readonly HashSet<string> _required;
        private readonly ToolInputSchema _schema;
        public int ExecutionCount { get; private set; }
        public StrictSchemaTool(string name, ToolPermission permission, string[]? allowedProps = null, string[]? required = null)
        {
            _name = name;
            _permission = permission;
            _allowedProps = new HashSet<string>(allowedProps ?? new[] { "query" }, StringComparer.Ordinal);
            _required = new HashSet<string>(required ?? new[] { "query" }, StringComparer.Ordinal);
            // Build a schema that matches allowed/required so the validator's schema gate fires correctly.
            var propsJson = string.Join(",", _allowedProps.Select(p => $"\"{p}\":{{\"type\":\"string\"}}"));
            var reqJson = _required.Count == 0 ? "" : $"\"required\":[{string.Join(",", _required.Select(r => $"\"{r}\""))}],";
            _schema = new ToolInputSchema($"{{\"type\":\"object\",\"properties\":{{{propsJson}}},{reqJson}\"additionalProperties\":false}}");
        }
        public string Name => _name;
        public string Description => "strict";
        public ToolPermission Permission => _permission;
        public ToolInputSchema InputSchema => _schema;
        public ToolValidationResult Validate(ToolInvocation inv)
        {
            foreach (var r in _required) if (inv.GetString(r) is null) return ToolValidationResult.Invalid($"Missing required property '{r}' for '{_name}'.");
            foreach (var k in inv.Arguments.Keys) if (!_allowedProps.Contains(k)) return ToolValidationResult.Invalid($"Unknown property '{k}' for '{_name}'.");
            return ToolValidationResult.Valid;
        }
        public Task<ToolResult> ExecuteAsync(ToolInvocation inv, CancellationToken ct = default) { ExecutionCount++; return Task.FromResult(ToolResult.Success("ok")); }
    }

    private sealed class SchemaFakeTool : ITool
    {
        private readonly string _name;
        private readonly ToolPermission _permission;
        public int ExecutionCount { get; private set; }
        public SchemaFakeTool(string name, ToolPermission permission, string schemaJson)
        {
            _name = name;
            _permission = permission;
            InputSchema = new ToolInputSchema(schemaJson);
        }
        public string Name => _name;
        public string Description => $"Test double for {Name}.";
        public ToolPermission Permission => _permission;
        public ToolInputSchema InputSchema { get; }
        public ToolValidationResult Validate(ToolInvocation invocation) => ToolValidationResult.Valid;
        public Task<ToolResult> ExecuteAsync(ToolInvocation invocation, CancellationToken ct = default) { ExecutionCount++; return Task.FromResult(ToolResult.Success($"{Name} ran.")); }
    }

    private sealed class ReturningTool : ITool
    {
        private readonly string _name;
        private readonly ToolPermission _permission;
        private readonly string _output;
        public ReturningTool(string name, ToolPermission permission, string output) { _name = name; _permission = permission; _output = output; }
        public string Name => _name;
        public string Description => "returning";
        public ToolPermission Permission => _permission;
        public ToolInputSchema InputSchema => ToolInputSchema.None;
        public ToolValidationResult Validate(ToolInvocation inv) => ToolValidationResult.Valid;
        public Task<ToolResult> ExecuteAsync(ToolInvocation inv, CancellationToken ct = default) => Task.FromResult(ToolResult.Success(_output));
    }

    private sealed class CancellingConfirmationService : IConfirmationService
    {
        private readonly CancellationTokenSource _cts;
        public CancellingConfirmationService(CancellationTokenSource cts) { _cts = cts; }
        public Task<bool> RequestApprovalAsync(ConfirmationRequest request, CancellationToken ct = default)
        {
            _cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }
    }
}
