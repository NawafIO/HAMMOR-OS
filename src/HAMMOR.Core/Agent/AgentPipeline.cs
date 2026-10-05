using HAMMOR.Core.Ai;
using HAMMOR.Core.Audit;
using HAMMOR.Core.Configuration;
using HAMMOR.Core.Diagnostics;
using HAMMOR.Core.Memory;
using HAMMOR.Core.Permissions;
using HAMMOR.Core.Projects;
using HAMMOR.Core.Tools;
using Microsoft.Extensions.Logging;

namespace HAMMOR.Core.Agent;

/// <summary>Runs a user turn through the HAMMOR execution loop.</summary>
public interface IAgentPipeline
{
    /// <summary>
    /// Executes SENSE → UNDERSTAND → ROUTE → PLAN → AUTHORIZE → EXECUTE →
    /// VERIFY → REMEMBER → RESPOND for one input.
    /// </summary>
    Task<AgentTurnResult> RunAsync(
        AgentTurnRequest request,
        IProgress<AgentProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a single tool through the authorisation gate. Exposed so the UI
    /// and future task runners share one enforcement path.
    /// </summary>
    Task<ToolResult> InvokeToolAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default pipeline. Phase 1 wires SENSE through RESPOND for conversation and
/// routes tool calls through the permission gate; model-driven tool selection
/// (the model choosing a tool mid-turn) is not yet connected — see
/// <see cref="RunAsync"/>.
/// </summary>
public sealed class AgentPipeline(
    IEnumerable<IAiProvider> aiProviders,
    IToolRegistry toolRegistry,
    IPermissionEvaluator permissionEvaluator,
    IConfirmationService confirmationService,
    IMemoryStore memoryStore,
    IProjectStore projectStore,
    IAuditLog auditLog,
    IConfigurationStore configurationStore,
    ILogger<AgentPipeline> logger,
    AgentLoop? agentLoop = null) : IAgentPipeline
{
    private readonly IReadOnlyList<IAiProvider> _aiProviders =
        aiProviders?.ToList() ?? throw new ArgumentNullException(nameof(aiProviders));

    private readonly IToolRegistry _toolRegistry =
        toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));

    private readonly IPermissionEvaluator _permissionEvaluator =
        permissionEvaluator ?? throw new ArgumentNullException(nameof(permissionEvaluator));

    private readonly IConfirmationService _confirmationService =
        confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));

    private readonly IMemoryStore _memoryStore =
        memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));

    private readonly IProjectStore _projectStore =
        projectStore ?? throw new ArgumentNullException(nameof(projectStore));

    private readonly IAuditLog _auditLog =
        auditLog ?? throw new ArgumentNullException(nameof(auditLog));

    private readonly IConfigurationStore _configurationStore =
        configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));

    private readonly ILogger<AgentPipeline> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Handles the iterative model → tool → model cycle. Optional so a
    /// conversation-only deployment (or a test) can run the pipeline without
    /// it; when absent the turn stays on the single-shot conversational path.
    /// </summary>
    private readonly AgentLoop? _agentLoop = agentLoop;

    public async Task<AgentTurnResult> RunAsync(
        AgentTurnRequest request,
        IProgress<AgentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Input))
        {
            return AgentTurnResult.Failed(AgentStage.Sense, "Input was empty.");
        }

        // SENSE — input has arrived and been normalised by the caller.
        progress?.Report(new AgentProgress(AgentStage.Sense, "Received input."));

        // UNDERSTAND — gather the context the reply depends on.
        progress?.Report(new AgentProgress(AgentStage.Understand, "Gathering context."));
        var projectContext = await LoadProjectContextAsync(request.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        // ROUTE — choose the provider that will reason about this turn.
        progress?.Report(new AgentProgress(AgentStage.Route, "Selecting AI provider."));
        var ai = _configurationStore.Current.Ai;
        var provider = _aiProviders.FirstOrDefault(
            p => string.Equals(p.ProviderId, ai.PrimaryProvider, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            return AgentTurnResult.Failed(
                AgentStage.Route,
                $"AI provider '{ai.PrimaryProvider}' is not registered.");
        }

        // ROUTE (continued) — an agentic turn is one where the routed provider
        // can return structured tool calls and there are tools to offer. The
        // pipeline stays the entry point and keeps macro-level coordination;
        // the multi-round EXECUTE stage is delegated to AgentLoop, which
        // enforces just-in-time per-tool authorisation through
        // IPermissionEvaluator and IConfirmationService for every call. The
        // delegation happens before the availability probe because the loop
        // runs its own — probing twice would cost a second API round trip.
        if (_agentLoop is not null
            && provider is IToolCallingProvider
            && _toolRegistry.All.Count > 0)
        {
            return await _agentLoop
                .RunAsync(request, options: null, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        var availability = await provider.CheckAvailabilityAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!availability.IsUsable)
        {
            return AgentTurnResult.Failed(
                AgentStage.Route,
                $"{provider.DisplayName} is not available: {availability.Detail}");
        }

        // PLAN — assemble the request. Phase 1 plans a single conversational
        // turn. Multi-step planning and model-driven tool selection arrive in
        // a later phase; until then tools are invoked explicitly through
        // InvokeToolAsync, which still enforces AUTHORIZE.
        progress?.Report(new AgentProgress(AgentStage.Plan, "Preparing request."));

        var messages = new List<AiMessage>(request.History) { AiMessage.User(request.Input) };

        var aiRequest = new AiRequest
        {
            Messages = messages,
            SystemPrompt = BuildSystemPrompt(ai.SystemPrompt, request.Language, projectContext),
            Model = ai.Model,
            MaxTokens = ai.MaxTokens,
            Effort = ai.Effort,
        };

        // AUTHORIZE — no tool call is planned for a pure conversational turn,
        // so there is nothing to gate here. Tool calls go through
        // InvokeToolAsync and cannot bypass the permission engine.
        progress?.Report(new AgentProgress(AgentStage.Authorize, "No privileged action required."));

        // EXECUTE — call the provider.
        progress?.Report(new AgentProgress(AgentStage.Execute, $"Querying {provider.DisplayName}."));

        AiResponse response;
        try
        {
            response = await provider.CompleteAsync(aiRequest, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AiProviderException ex)
        {
            _logger.LogWarning(ex, "AI provider {Provider} failed.", provider.ProviderId);

            await _auditLog.AppendAsync(
                new AuditEntry
                {
                    Category = AuditCategory.Provider,
                    Subject = provider.ProviderId,
                    Message = SecretRedactor.Redact(ex.Message),
                    Outcome = AuditOutcome.Failed,
                    ProjectId = request.ProjectId,
                },
                cancellationToken).ConfigureAwait(false);

            return AgentTurnResult.Failed(AgentStage.Execute, ex.Message);
        }

        // VERIFY — check the reply is usable before presenting it as an answer.
        progress?.Report(new AgentProgress(AgentStage.Verify, "Checking response."));

        if (response.IsRefusal)
        {
            return AgentTurnResult.Failed(
                AgentStage.Verify,
                "The model declined this request on safety grounds.");
        }

        if (string.IsNullOrWhiteSpace(response.Text))
        {
            return AgentTurnResult.Failed(
                AgentStage.Verify,
                "The model returned an empty response.");
        }

        // REMEMBER — persist the exchange so later turns can draw on it.
        progress?.Report(new AgentProgress(AgentStage.Remember, "Recording conversation."));
        await RememberAsync(request, response, cancellationToken).ConfigureAwait(false);

        // RESPOND.
        progress?.Report(new AgentProgress(AgentStage.Respond, "Done."));

        var reply = response.IsTruncated
            ? response.Text
              + Environment.NewLine
              + "[Response was cut off by the output token limit.]"
            : response.Text;

        return AgentTurnResult.Success(reply, response.Usage);
    }

    public async Task<ToolResult> InvokeToolAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var tool = _toolRegistry.Find(invocation.ToolName);
        if (tool is null)
        {
            return ToolResult.Failure($"No tool named '{invocation.ToolName}' is registered.");
        }

        // AUTHORIZE. Enforced here, in software — a model requesting a tool is
        // never sufficient authority to run it.
        var decision = await _permissionEvaluator.AuthoriseAsync(
            tool, invocation, _confirmationService, cancellationToken).ConfigureAwait(false);

        await _auditLog.AppendAsync(
            AuditEntry.ForDecision(tool, invocation, decision),
            cancellationToken).ConfigureAwait(false);

        if (!decision.IsAllowed)
        {
            return ToolResult.Failure(decision.Reason);
        }

        // EXECUTE.
        try
        {
            var result = await tool.ExecuteAsync(invocation, cancellationToken)
                .ConfigureAwait(false);

            await _auditLog.AppendAsync(
                new AuditEntry
                {
                    Category = AuditCategory.ToolExecution,
                    Subject = tool.Name,
                    Message = SecretRedactor.Redact(
                        result.Succeeded
                            ? $"Completed: {Truncate(result.Output, 500)}"
                            : $"Failed: {result.Error}"),
                    Outcome = result.Succeeded ? AuditOutcome.Succeeded : AuditOutcome.Failed,
                    Permission = tool.Permission,
                    ProjectId = invocation.ProjectId,
                    CorrelationId = invocation.InvocationId,
                },
                cancellationToken).ConfigureAwait(false);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Unexpected tool fault. Audited and logged with the stack trace
            // so it stays diagnosable, then surfaced as a failed result rather
            // than tearing down the UI.
            _logger.LogError(ex, "Tool {Tool} threw an unhandled exception.", tool.Name);

            await _auditLog.AppendAsync(
                new AuditEntry
                {
                    Category = AuditCategory.ToolExecution,
                    Subject = tool.Name,
                    Message = SecretRedactor.Redact($"Unhandled exception: {ex.Message}"),
                    Outcome = AuditOutcome.Failed,
                    Permission = tool.Permission,
                    ProjectId = invocation.ProjectId,
                    CorrelationId = invocation.InvocationId,
                },
                cancellationToken).ConfigureAwait(false);

            return ToolResult.Failure($"'{tool.Name}' failed unexpectedly: {ex.Message}");
        }
    }

    private async Task<string?> LoadProjectContextAsync(
        string? projectId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        var project = await _projectStore.GetAsync(projectId, cancellationToken)
            .ConfigureAwait(false);

        return project?.Context;
    }

    private async Task RememberAsync(
        AgentTurnRequest request,
        AiResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            await _memoryStore.SaveAsync(
                new MemoryEntry
                {
                    Title = Truncate(request.Input, 80),
                    Content = $"User: {request.Input}{Environment.NewLine}"
                              + $"HAMMOR: {response.Text}",
                    Kind = MemoryKind.Conversation,
                    ProjectId = request.ProjectId,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A memory write failure must not discard a reply the user is
            // waiting on, but it is still a real fault: log it loudly rather
            // than swallowing it.
            _logger.LogError(ex, "Failed to persist conversation memory.");
        }
    }

    private static string BuildSystemPrompt(
        string basePrompt,
        string? language,
        string? projectContext)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(basePrompt))
        {
            parts.Add(basePrompt);
        }

        if (!string.IsNullOrWhiteSpace(language))
        {
            parts.Add(language.StartsWith("ar", StringComparison.OrdinalIgnoreCase)
                ? "The user's interface language is Arabic. Reply in Arabic unless they write "
                  + "to you in another language. Mixed Arabic/English input is normal — mirror "
                  + "the language the user used."
                : "The user's interface language is English. Reply in English unless they write "
                  + "to you in another language.");
        }

        if (!string.IsNullOrWhiteSpace(projectContext))
        {
            parts.Add($"Project context:{Environment.NewLine}{projectContext}");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";
}
