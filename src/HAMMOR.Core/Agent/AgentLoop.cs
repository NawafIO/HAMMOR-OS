using HAMMOR.Core.Ai;
using HAMMOR.Core.Audit;
using HAMMOR.Core.Configuration;
using HAMMOR.Core.Diagnostics;
using HAMMOR.Core.Memory;
using HAMMOR.Core.Permissions;
using HAMMOR.Core.Projects;
using HAMMOR.Core.Tasks;
using HAMMOR.Core.Tools;
using Microsoft.Extensions.Logging;

namespace HAMMOR.Core.Agent;

/// <summary>
/// Provider-agnostic model→tool→model loop. The model is never trusted:
/// every tool call resolves through <see cref="IToolRegistry"/>, is
/// validated via <see cref="ToolCallValidator"/>, passes through the existing
/// permission/confirmation path, is audited, and has bounded output.
/// </summary>
public sealed class AgentLoop(
    IEnumerable<IAiProvider> aiProviders,
    IToolRegistry toolRegistry,
    IPermissionEvaluator permissionEvaluator,
    IConfirmationService confirmationService,
    IMemoryStore memoryStore,
    IProjectStore projectStore,
    IAuditLog auditLog,
    IConfigurationStore configurationStore,
    ILogger<AgentLoop> logger)
{
    private readonly IReadOnlyList<IAiProvider> _providers = aiProviders?.ToList() ?? throw new ArgumentNullException(nameof(aiProviders));
    private readonly IToolRegistry _registry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
    private readonly IPermissionEvaluator _evaluator = permissionEvaluator ?? throw new ArgumentNullException(nameof(permissionEvaluator));
    private readonly IConfirmationService _confirmation = confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));
    private readonly IMemoryStore _memoryStore = memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));
    private readonly IProjectStore _projectStore = projectStore ?? throw new ArgumentNullException(nameof(projectStore));
    private readonly IAuditLog _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
    private readonly IConfigurationStore _configurationStore = configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));
    private readonly ILogger<AgentLoop> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "api_key", "apikey", "authorization", "bearer", "password", "secret", "token", "x-api-key", "xi-api-key"
    };

    /// <summary>
    /// Runs the tool-calling loop until the model returns a final answer or
    /// <see cref="AgentLoopOptions.EffectiveMaxRounds"/> is exhausted.
    /// </summary>
    public async Task<AgentTurnResult> RunAsync(
        AgentTurnRequest request,
        AgentLoopOptions? options = null,
        IProgress<AgentProgress>? progress = null,
        CancellationToken cancellationToken = default,
        UnattendedRunContext? unattended = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Input))
            return AgentTurnResult.Failed(AgentStage.Sense, "Input was empty.");

        options ??= new AgentLoopOptions();
        var maxRounds = options.EffectiveMaxRounds;
        var maxToolChars = options.MaxToolResultChars <= 0 ? 12_000 : options.MaxToolResultChars;

        progress?.Report(new AgentProgress(AgentStage.Sense, "Received input."));
        progress?.Report(new AgentProgress(AgentStage.Understand, "Gathering context."));
        var projectContext = await LoadProjectContextAsync(request.ProjectId, cancellationToken).ConfigureAwait(false);

        progress?.Report(new AgentProgress(AgentStage.Route, "Selecting AI provider."));
        var ai = _configurationStore.Current.Ai;
        var provider = _providers.FirstOrDefault(p => string.Equals(p.ProviderId, ai.PrimaryProvider, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
            return AgentTurnResult.Failed(AgentStage.Route, $"AI provider '{ai.PrimaryProvider}' is not registered.");

        var availability = await provider.CheckAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        if (!availability.IsUsable)
            return AgentTurnResult.Failed(AgentStage.Route, $"{provider.DisplayName} is not available: {availability.Detail}");

        // Fallback when provider does not support tool calling — single-shot text path, still audited correctly.
        var toolCapable = provider as IToolCallingProvider;

        // Build tool definitions once; they are stable for this turn.
        // In an unattended run the model is only shown the tools its grant names.
        var toolDefs = _registry.All
            .Where(t => unattended is null || unattended.IsToolGranted(t.Name))
            .Select(ToolDefinition.FromTool)
            .ToList();

        // Audit text from an unattended run is tagged with its task id.
        string Tag(string message) => unattended is null ? message : unattended.Tag(message);

        // Conversation history for the model: starts from caller-supplied history plus the new user turn.
        var history = new List<AiMessage>(request.History) { AiMessage.User(request.Input) };
        // Provider-facing history with tool turns. We maintain a typed transcript separately
        // because AiMessage is text-only; the actual provider call uses a mapped representation
        // inside the provider. For Core we keep a history of ModelResponseTurn-derived messages
        // via an auxiliary list so we can thread tool results back.
        var toolTranscript = new List<(ModelResponseTurn Assistant, IReadOnlyList<ModelToolResult> Results)>();

        string baseSystemPrompt = BuildSystemPrompt(ai.SystemPrompt, request.Language, projectContext);

        for (int round = 0; round < maxRounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new AgentProgress(AgentStage.Plan, round == 0 ? "Asking model." : $"Asking model (round {round + 1}/{maxRounds})."));

            // Build the provider request: system prompt + history, plus prior tool transcript encoded as text.
            // The actual tool-result threading is handled inside provider adapters; Core only needs to keep
            // history coherent. We pass history as AiMessages plus toolTranscript so providers that support tools
            // can reconstruct the proper wire shapes.
            ModelResponseTurn turn;
            try
            {
                if (toolCapable is not null)
                {
                    var toolRequest = new AiToolAwareRequest
                    {
                        History = history,
                        SystemPrompt = baseSystemPrompt,
                        Model = ai.Model,
                        MaxTokens = ai.MaxTokens,
                        Effort = ai.Effort,
                        ToolTranscript = BuildToolTranscript(toolTranscript),
                    };
                    turn = await toolCapable.CompleteWithToolsAsync(toolRequest, toolDefs, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var aiRequest = new AiRequest
                    {
                        Messages = history.Count == 0 ? new[] { AiMessage.User(request.Input) } : BuildMessages(history, toolTranscript),
                        SystemPrompt = baseSystemPrompt,
                        Model = ai.Model,
                        MaxTokens = ai.MaxTokens,
                        Effort = ai.Effort,
                    };
                    var resp = await provider.CompleteAsync(aiRequest, cancellationToken).ConfigureAwait(false);
                    if (resp.IsRefusal)
                        return AgentTurnResult.Failed(AgentStage.Verify, "The model declined this request on safety grounds.");
                    if (string.IsNullOrWhiteSpace(resp.Text))
                        return AgentTurnResult.Failed(AgentStage.Verify, "The model returned an empty response.");
                    var reply = resp.IsTruncated ? resp.Text + Environment.NewLine + "[Response was cut off by the output token limit.]" : resp.Text;
                    // Unattended runs are read-only: they do not write conversation memory.
                    if (unattended is null)
                        await RememberAsync(request, reply, cancellationToken).ConfigureAwait(false);
                    return AgentTurnResult.Success(reply, resp.Usage);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (AiProviderException ex)
            {
                _logger.LogWarning(ex, "AI provider {Provider} failed.", provider.ProviderId);
                await _auditLog.AppendAsync(new AuditEntry
                {
                    Category = AuditCategory.Provider,
                    Subject = provider.ProviderId,
                    Message = SecretRedactor.Redact(ex.Message),
                    Outcome = AuditOutcome.Failed,
                    ProjectId = request.ProjectId,
                }, cancellationToken).ConfigureAwait(false);
                return AgentTurnResult.Failed(AgentStage.Execute, ex.Message);
            }

            // Refusal/empty handling harmonised with tool path.
            bool isRefusal = string.Equals(turn.StopReason, ModelStopReasons.Refusal, StringComparison.OrdinalIgnoreCase);
            if (isRefusal)
                return AgentTurnResult.Failed(AgentStage.Verify, "The model declined this request on safety grounds.");

            // Final answer: no tool calls => we're done.
            if (!turn.HasToolCalls)
            {
                if (string.IsNullOrWhiteSpace(turn.Text))
                    return AgentTurnResult.Failed(AgentStage.Verify, "The model returned an empty response.");

                var reply = string.Equals(turn.StopReason, ModelStopReasons.MaxTokens, StringComparison.OrdinalIgnoreCase)
                    ? turn.Text! + Environment.NewLine + "[Response was cut off by the output token limit.]"
                    : turn.Text!;

                progress?.Report(new AgentProgress(AgentStage.Remember, "Recording conversation."));
                if (unattended is null)
                    await RememberAsync(request, reply, cancellationToken).ConfigureAwait(false);
                progress?.Report(new AgentProgress(AgentStage.Respond, "Done."));
                return AgentTurnResult.Success(reply, null);
            }

            // We have tool calls: validate, authorise, execute each independently.
            progress?.Report(new AgentProgress(AgentStage.Authorize, $"Validating {turn.ToolCalls.Count} tool call(s)."));
            var results = new List<ModelToolResult>(turn.ToolCalls.Count);

            foreach (var call in turn.ToolCalls)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (isValid, validationError, invocation) = ToolCallValidator.ValidateAndBuild(call, _registry);
                if (!isValid || invocation is null)
                {
                    // Audit the rejected call (never executed).
                    var safeToolName = string.IsNullOrWhiteSpace(call.ToolName) ? "(unknown)" : call.ToolName;
                    await _auditLog.AppendAsync(new AuditEntry
                    {
                        Category = AuditCategory.Authorisation,
                        Subject = safeToolName,
                        Message = SecretRedactor.Redact(SanitizeForAudit(validationError ?? "Invalid tool call.")),
                        Outcome = AuditOutcome.Denied,
                        ProjectId = request.ProjectId,
                        CorrelationId = call.Id,
                    }, cancellationToken).ConfigureAwait(false);

                    results.Add(new ModelToolResult(call.Id, safeToolName, BoundAndRedact(validationError ?? "Invalid tool call.", maxToolChars), IsError: true));
                    continue;
                }

                var tool = _registry.Find(invocation.ToolName)!;

                // Unattended runs: the task grant is checked BEFORE the normal
                // permission path and can only narrow it. Any miss stops the run.
                if (unattended is not null)
                {
                    var gate = unattended.Check(tool, invocation);
                    if (gate.Outcome != ToolGateOutcome.Allow)
                    {
                        await _auditLog.AppendAsync(new AuditEntry
                        {
                            Category = AuditCategory.Authorisation,
                            Subject = tool.Name,
                            Message = SecretRedactor.Redact(Tag(gate.Reason)),
                            Outcome = AuditOutcome.Denied,
                            Permission = tool.Permission,
                            ProjectId = request.ProjectId,
                            CorrelationId = invocation.InvocationId,
                        }, cancellationToken).ConfigureAwait(false);

                        return gate.Outcome == ToolGateOutcome.Block
                            ? AgentTurnResult.Blocked(AgentStage.Authorize, gate.Reason)
                            : AgentTurnResult.Failed(AgentStage.Authorize, gate.Reason);
                    }
                }

                // Permission evaluation + confirmation (existing path). Unattended
                // runs use a confirmation service that never approves.
                IConfirmationService callConfirmation = unattended is null ? _confirmation : unattended.Confirmation;
                PermissionDecision decision;
                try
                {
                    decision = await _evaluator.AuthoriseAsync(tool, invocation, callConfirmation, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Permission evaluation failed for {Tool}.", tool.Name);
                    decision = PermissionDecision.Deny($"Permission evaluation failed for '{tool.Name}'.");
                }

                var decisionEntry = AuditEntry.ForDecision(tool, invocation, decision);
                if (unattended is not null)
                    decisionEntry = decisionEntry with { Message = SecretRedactor.Redact(Tag(decisionEntry.Message)) };
                await _auditLog.AppendAsync(decisionEntry, cancellationToken).ConfigureAwait(false);

                if (!decision.IsAllowed)
                {
                    // Unattended: nothing that needed a person (or was denied by
                    // policy) is skipped or approved; the run stops and blocks.
                    if (unattended is not null)
                    {
                        var blockedReason = SecretRedactor.Redact(SanitizeForAudit(decision.Reason));
                        await _auditLog.AppendAsync(new AuditEntry
                        {
                            Category = AuditCategory.ToolExecution,
                            Subject = tool.Name,
                            Message = SecretRedactor.Redact(Tag("Not executed: " + blockedReason)),
                            Outcome = AuditOutcome.Denied,
                            Permission = tool.Permission,
                            ProjectId = invocation.ProjectId,
                            CorrelationId = invocation.InvocationId,
                        }, cancellationToken).ConfigureAwait(false);

                        return AgentTurnResult.Blocked(AgentStage.Authorize, blockedReason);
                    }

                    // Structured denial: prevent execution, return typed error so the model can continue.
                    var deniedMsg = decision.Outcome == PermissionOutcome.Denied && decision.Reason.Contains("declined", StringComparison.OrdinalIgnoreCase)
                        ? "Permission/confirmation denied by user"
                        : SecretRedactor.Redact(SanitizeForAudit(decision.Reason));
                    // Ensure user-denied phrasing is exactly as required when confirmation was declined.
                    if (decision.Reason.Contains("declined", StringComparison.OrdinalIgnoreCase))
                        deniedMsg = "Permission/confirmation denied by user";

                    await _auditLog.AppendAsync(new AuditEntry
                    {
                        Category = AuditCategory.ToolExecution,
                        Subject = tool.Name,
                        Message = SecretRedactor.Redact(deniedMsg),
                        Outcome = AuditOutcome.Denied,
                        Permission = tool.Permission,
                        ProjectId = invocation.ProjectId,
                        CorrelationId = invocation.InvocationId,
                    }, cancellationToken).ConfigureAwait(false);

                    results.Add(new ModelToolResult(call.Id, tool.Name, BoundAndRedact(deniedMsg, maxToolChars), IsError: true));
                    continue;
                }

                // Execute through the existing tool — no bypass of the gate above.
                ToolResult execResult;
                try
                {
                    execResult = await tool.ExecuteAsync(invocation, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Tool {Tool} threw an unhandled exception.", tool.Name);
                    execResult = ToolResult.Failure($"'{tool.Name}' failed unexpectedly: {ex.Message}");
                }

                var outcome = execResult.Succeeded ? AuditOutcome.Succeeded : AuditOutcome.Failed;
                var rawMessage = execResult.Succeeded ? execResult.Output : (execResult.Error ?? "Tool failed.");
                var safeMessage = BoundAndRedact(SanitizeForAudit(rawMessage), maxToolChars);

                await _auditLog.AppendAsync(new AuditEntry
                {
                    Category = AuditCategory.ToolExecution,
                    Subject = tool.Name,
                    Message = SecretRedactor.Redact(Tag((execResult.Succeeded ? "Completed: " : "Failed: ") + Truncate(safeMessage, 800))),
                    Outcome = outcome,
                    Permission = tool.Permission,
                    ProjectId = invocation.ProjectId,
                    CorrelationId = invocation.InvocationId,
                }, cancellationToken).ConfigureAwait(false);

                results.Add(new ModelToolResult(call.Id, tool.Name, safeMessage, IsError: !execResult.Succeeded));
            }

            toolTranscript.Add((turn, results));

            // Edge: if all calls were rejected and the model keeps producing bad calls without text,
            // still advance history so the next prompt sees the errors and can correct itself.
            // Also avoid infinite loops of identical bad calls by letting the next round try again.
        }

        // Exhausted rounds.
        return AgentTurnResult.Failed(AgentStage.Execute, $"Tool loop reached max rounds ({maxRounds}) without a final answer.");
    }

    private async Task<string?> LoadProjectContextAsync(string? projectId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectId)) return null;
        var project = await _projectStore.GetAsync(projectId, ct).ConfigureAwait(false);
        return project?.Context;
    }

    private async Task RememberAsync(AgentTurnRequest request, string responseText, CancellationToken ct)
    {
        try
        {
            await _memoryStore.SaveAsync(new MemoryEntry
            {
                Title = Truncate(request.Input, 80),
                Content = $"User: {request.Input}{Environment.NewLine}HAMMOR: {responseText}",
                Kind = MemoryKind.Conversation,
                ProjectId = request.ProjectId,
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to persist conversation memory.");
        }
    }

    private static List<AiMessage> BuildMessages(
        List<AiMessage> baseHistory,
        List<(ModelResponseTurn Assistant, IReadOnlyList<ModelToolResult> Results)> toolTranscript)
    {
        // Used only by the non-tool fallback path; encodes prior tool turns as
        // text so the plain CompleteAsync shape can still see them.
        var messages = new List<AiMessage>(baseHistory);
        foreach (var (assistant, results) in toolTranscript)
        {
            var assistantText = string.IsNullOrWhiteSpace(assistant.Text) ? "(tool calls)" : assistant.Text!;
            messages.Add(AiMessage.Assistant(assistantText));
            foreach (var r in results)
            {
                var prefix = r.IsError ? "[Tool error]" : "[Tool result]";
                messages.Add(AiMessage.User($"{prefix} {r.ToolName} ({r.ToolCallId}): {r.Content}"));
            }
        }
        return messages;
    }

    private static IReadOnlyList<(AiRole Role, AiMessageContent Content)> BuildToolTranscript(
        List<(ModelResponseTurn Assistant, IReadOnlyList<ModelToolResult> Results)> transcript)
    {
        var list = new List<(AiRole, AiMessageContent)>(transcript.Count * 2);
        foreach (var (assistant, results) in transcript)
        {
            var calls = assistant.ToolCalls.Select(c => new AiToolCallContent(c.Id, c.ToolName, c.ArgumentsJson)).ToList();
            list.Add((AiRole.Assistant, new AiMessageContent(assistant.Text, calls, null)));
            var toolResults = results.Select(r => new AiToolResultContent(r.ToolCallId, r.ToolName, r.Content, r.IsError)).ToList();
            list.Add((AiRole.User, new AiMessageContent(null, null, toolResults)));
        }
        return list;
    }

    private static string BuildSystemPrompt(string basePrompt, string? language, string? projectContext)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(basePrompt)) parts.Add(basePrompt);
        if (!string.IsNullOrWhiteSpace(language))
            parts.Add(language.StartsWith("ar", StringComparison.OrdinalIgnoreCase)
                ? "The user's interface language is Arabic. Reply in Arabic unless they write to you in another language. Mixed Arabic/English input is normal — mirror the language the user used."
                : "The user's interface language is English. Reply in English unless they write to you in another language.");
        if (!string.IsNullOrWhiteSpace(projectContext)) parts.Add($"Project context:{Environment.NewLine}{projectContext}");
        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

    private static string BoundAndRedact(string value, int maxChars)
    {
        var redacted = SecretRedactor.Redact(value ?? string.Empty);
        if (redacted.Length <= maxChars) return redacted;
        return redacted[..maxChars] + $"… [truncated — exceeded {maxChars} chars]";
    }

    private static string SanitizeForAudit(string value)
    {
        // Strip values whose keys look sensitive if the message accidentally contains key:value pairs.
        // Lightweight and conservative — SecretRedactor is the primary defence.
        if (string.IsNullOrEmpty(value)) return string.Empty;
        foreach (var key in SensitiveKeys)
        {
            // Mask "key: value" occurrences in audit text.
            var needle = key + ":";
            int idx;
            var result = value;
            while ((idx = result.IndexOf(needle, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int end = result.IndexOfAny(new[] { '\n', '\r', ' ', ',', ';', '"' }, idx + needle.Length);
                if (end < 0) end = result.Length;
                result = result[..(idx + needle.Length)] + " [redacted]" + result[end..];
                // avoid infinite loop on same key
                if (result.Length > 50_000) break;
            }
            value = result;
        }
        return value;
    }
}
