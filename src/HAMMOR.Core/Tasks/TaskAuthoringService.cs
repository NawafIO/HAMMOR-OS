using System.Globalization;
using System.Text;
using HAMMOR.Core.Audit;
using HAMMOR.Core.Diagnostics;
using HAMMOR.Core.Localization;
using HAMMOR.Core.Permissions;
using HAMMOR.Core.Tools;
using Microsoft.Extensions.Logging;

namespace HAMMOR.Core.Tasks;

/// <summary>Grant fields the user chooses; ids and timestamps are never accepted from the caller.</summary>
public sealed record GrantDraft
{
    public IReadOnlyList<string> AllowedTools { get; init; } = [];

    public IReadOnlyList<string> AllowedRoots { get; init; } = [];

    /// <summary>Mandatory; there is no default expiry.</summary>
    public required DateTimeOffset ExpiresUtc { get; init; }

    public int MaxToolCalls { get; init; } = 10;
}

/// <summary>A new unattended task as entered by the user.</summary>
public sealed record TaskDraft
{
    public required string Title { get; init; }

    public string? Description { get; init; }

    public required string Prompt { get; init; }

    public DateTimeOffset? ScheduledForUtc { get; init; }

    public string? ProjectId { get; init; }

    public int MaxAttempts { get; init; } = 1;

    public required GrantDraft Grant { get; init; }
}

public enum TaskAuthoringStatus
{
    Created = 0,
    Resumed = 1,
    Cancelled = 2,

    /// <summary>The task was running; the runner was asked to cancel it.</summary>
    CancelRequested = 3,

    /// <summary>The draft broke a rule; no dialog was shown, nothing changed.</summary>
    Invalid = 4,

    /// <summary>The user refused or dismissed the approval; nothing changed.</summary>
    Refused = 5,

    NotFound = 6,

    /// <summary>The task's state does not allow the operation; nothing changed.</summary>
    NotAllowed = 7,

    /// <summary>A required audit write failed; nothing changed.</summary>
    Failed = 8,
}

public sealed record TaskAuthoringResult(
    TaskAuthoringStatus Status,
    HammorTask? Task,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded =>
        Status is TaskAuthoringStatus.Created
            or TaskAuthoringStatus.Resumed
            or TaskAuthoringStatus.Cancelled
            or TaskAuthoringStatus.CancelRequested;

    internal static TaskAuthoringResult Of(TaskAuthoringStatus status, HammorTask? task = null) =>
        new(status, task, Array.Empty<string>());

    internal static TaskAuthoringResult Error(TaskAuthoringStatus status, IEnumerable<string> errors, HammorTask? task = null) =>
        new(status, task, errors.ToList());

    internal static TaskAuthoringResult Error(TaskAuthoringStatus status, string error, HammorTask? task = null) =>
        new(status, task, new[] { error });
}

/// <summary>
/// The only component that creates task grants (ADR-004 §3). Sequence for
/// every grant: validate → explicit approval through
/// <see cref="IConfirmationService"/> → audit the approval → write. Refusal,
/// dismissal, an invalid draft or a failed approval audit changes nothing.
/// The store and the run-time gate re-check everything independently.
/// </summary>
public sealed class TaskAuthoringService(
    ITaskStore taskStore,
    ITaskRunner taskRunner,
    IToolRegistry toolRegistry,
    TaskPathScope pathScope,
    IConfirmationService confirmationService,
    IAuditLog auditLog,
    ILogger<TaskAuthoringService> logger,
    ILocalizationService? localization = null,
    TimeProvider? timeProvider = null)
{
    public const int MaxAttemptsLimit = HammorTask.MaxAttemptsLimit;

    private readonly ITaskStore _store = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
    private readonly ITaskRunner _runner = taskRunner ?? throw new ArgumentNullException(nameof(taskRunner));
    private readonly IToolRegistry _registry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
    private readonly TaskPathScope _pathScope = pathScope ?? throw new ArgumentNullException(nameof(pathScope));
    private readonly IConfirmationService _confirmation = confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));
    private readonly IAuditLog _audit = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
    private readonly ILogger<TaskAuthoringService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ILocalizationService? _localization = localization;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>Creates a new unattended task after the user approves its grant.</summary>
    public async Task<TaskAuthoringResult> CreateAsync(TaskDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(draft.Grant);

        var now = _time.GetUtcNow();
        var taskId = Guid.NewGuid().ToString("n");
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(draft.Title))
        {
            errors.Add("A task needs a title.");
        }

        if (string.IsNullOrWhiteSpace(draft.Prompt))
        {
            errors.Add("A task needs a prompt.");
        }

        if (draft.MaxAttempts < 1 || draft.MaxAttempts > MaxAttemptsLimit)
        {
            errors.Add($"MaxAttempts must be between 1 and {MaxAttemptsLimit}.");
        }

        if (draft.ScheduledForUtc is { } scheduled && scheduled >= draft.Grant.ExpiresUtc)
        {
            errors.Add("The schedule must be before the grant expires.");
        }

        var grant = BuildGrant(taskId, draft.Grant, draft.ProjectId, supersedes: null, now, errors);
        if (errors.Count > 0)
        {
            return TaskAuthoringResult.Error(TaskAuthoringStatus.Invalid, errors);
        }

        var task = new HammorTask
        {
            Id = taskId,
            Title = draft.Title.Trim(),
            Description = draft.Description,
            Prompt = draft.Prompt,
            ProjectId = draft.ProjectId,
            ScheduledForUtc = draft.ScheduledForUtc,
            MaxAttempts = draft.MaxAttempts,
            CreatedUtc = now,
            Grant = grant,
        };

        var details = DescribeGrant(grant, task.Title, draft.ScheduledForUtc, blockedReason: null);
        var approved = await _confirmation.RequestApprovalAsync(
            new ConfirmationRequest(
                ToolRegistry.ReservedGrantApprovalName,
                ToolPermission.Read,
                Text("Tasks.Grant.ApprovalSummary", "Allow this task to run unattended with the read-only access below?"),
                details),
            cancellationToken).ConfigureAwait(false);

        if (!approved)
        {
            await AuditAsync(taskId, AuditOutcome.Denied, $"Grant {grant.Id} refused by the user; task not created.", cancellationToken)
                .ConfigureAwait(false);
            return TaskAuthoringResult.Of(TaskAuthoringStatus.Refused);
        }

        // The approval is audited before anything is written: if the audit
        // fails, no grant exists without a record of who approved it.
        if (!await TryAuditAsync(taskId, AuditOutcome.Allowed, $"Grant {grant.Id} approved by the user for new task.{Environment.NewLine}{details}", cancellationToken)
                .ConfigureAwait(false))
        {
            return TaskAuthoringResult.Error(TaskAuthoringStatus.Failed, "The approval could not be recorded in the audit log; nothing was created.");
        }

        HammorTask created;
        try
        {
            created = await _store.CreateAsync(task, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            await AuditAsync(taskId, AuditOutcome.Failed, $"Task creation rejected by the store: {ex.Message}", cancellationToken)
                .ConfigureAwait(false);
            return TaskAuthoringResult.Error(TaskAuthoringStatus.Invalid, ex.Message);
        }

        await AuditAsync(taskId, AuditOutcome.Succeeded, $"Task created with grant {grant.Id}.", cancellationToken)
            .ConfigureAwait(false);

        return TaskAuthoringResult.Of(TaskAuthoringStatus.Created, created);
    }

    /// <summary>
    /// Returns a Blocked task to Pending under a NEW grant built from
    /// <paramref name="draft"/>. The old grant is never resubmitted; the
    /// store supersedes it permanently.
    /// </summary>
    public async Task<TaskAuthoringResult> ResumeAsync(string taskId, GrantDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentNullException.ThrowIfNull(draft);

        var existing = await _store.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return TaskAuthoringResult.Error(TaskAuthoringStatus.NotFound, $"No task with id '{taskId}' exists.");
        }

        if (existing.State != TaskState.Blocked)
        {
            return TaskAuthoringResult.Error(
                TaskAuthoringStatus.NotAllowed,
                $"Only a Blocked task can be resumed; this task is {existing.State}.",
                existing);
        }

        var now = _time.GetUtcNow();
        var errors = new List<string>();
        var oldGrantId = existing.Grant?.Id;

        if (existing.ScheduledForUtc is { } scheduled && scheduled >= draft.ExpiresUtc)
        {
            errors.Add("The task's schedule must be before the new grant expires.");
        }

        var grant = BuildGrant(taskId, draft, existing.ProjectId, oldGrantId, now, errors);
        if (errors.Count > 0)
        {
            return TaskAuthoringResult.Error(TaskAuthoringStatus.Invalid, errors, existing);
        }

        var details = DescribeGrant(grant, existing.Title, existing.ScheduledForUtc, existing.BlockedReason);
        var approved = await _confirmation.RequestApprovalAsync(
            new ConfirmationRequest(
                ToolRegistry.ReservedGrantApprovalName,
                ToolPermission.Read,
                Text("Tasks.Grant.ResumeSummary", "Resume this blocked task with a new read-only grant? The previous grant will be retired."),
                details),
            cancellationToken).ConfigureAwait(false);

        if (!approved)
        {
            await AuditAsync(taskId, AuditOutcome.Denied, $"Replacement grant {grant.Id} refused by the user; task stays Blocked.", cancellationToken)
                .ConfigureAwait(false);
            return TaskAuthoringResult.Of(TaskAuthoringStatus.Refused, existing);
        }

        if (!await TryAuditAsync(
                taskId,
                AuditOutcome.Allowed,
                $"Grant {grant.Id} approved by the user, superseding grant {oldGrantId ?? "(none)"}.{Environment.NewLine}{details}",
                cancellationToken).ConfigureAwait(false))
        {
            return TaskAuthoringResult.Error(TaskAuthoringStatus.Failed, "The approval could not be recorded in the audit log; nothing was changed.", existing);
        }

        HammorTask resumed;
        try
        {
            resumed = await _store.ResumeBlockedAsync(taskId, grant, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            await AuditAsync(taskId, AuditOutcome.Failed, $"Resume rejected by the store: {ex.Message}", cancellationToken)
                .ConfigureAwait(false);
            return TaskAuthoringResult.Error(
                ex is ArgumentException ? TaskAuthoringStatus.Invalid : TaskAuthoringStatus.NotAllowed,
                ex.Message,
                existing);
        }

        await AuditAsync(
            taskId,
            AuditOutcome.Succeeded,
            $"Grant {oldGrantId ?? "(none)"} superseded by grant {grant.Id}; task returned to Pending.",
            cancellationToken).ConfigureAwait(false);

        return TaskAuthoringResult.Of(TaskAuthoringStatus.Resumed, resumed);
    }

    /// <summary>
    /// Cancels a Pending or Blocked task through the store, or asks the
    /// runner to cancel a Running one. Every request is audited.
    /// </summary>
    public async Task<TaskAuthoringResult> CancelAsync(string taskId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

        var existing = await _store.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return TaskAuthoringResult.Error(TaskAuthoringStatus.NotFound, $"No task with id '{taskId}' exists.");
        }

        await AuditAsync(taskId, AuditOutcome.Information, $"Cancel requested by the user (task was {existing.State}).", cancellationToken)
            .ConfigureAwait(false);

        var cancelled = await _store.TryCancelAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (cancelled is not null)
        {
            await AuditAsync(taskId, AuditOutcome.Succeeded, "Task cancelled by the user before it ran.", cancellationToken)
                .ConfigureAwait(false);
            return TaskAuthoringResult.Of(TaskAuthoringStatus.Cancelled, cancelled);
        }

        // Not Pending/Blocked any more: running (the runner records the
        // cancellation itself), terminal, or gone.
        if (_runner.RequestCancel(taskId))
        {
            await AuditAsync(taskId, AuditOutcome.Information, "Cancellation of the running task was requested.", cancellationToken)
                .ConfigureAwait(false);
            return TaskAuthoringResult.Of(TaskAuthoringStatus.CancelRequested, existing);
        }

        var current = await _store.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        var reason = $"The task cannot be cancelled in state {current?.State.ToString() ?? "(deleted)"}.";
        await AuditAsync(taskId, AuditOutcome.Denied, reason, cancellationToken).ConfigureAwait(false);
        return TaskAuthoringResult.Error(TaskAuthoringStatus.NotAllowed, reason, current);
    }

    /// <summary>
    /// Exact, invariant description of a grant, shown in the approval dialog
    /// and written to the audit log.
    /// </summary>
    public static string DescribeGrant(TaskGrant grant, string title, DateTimeOffset? scheduledForUtc, string? blockedReason)
    {
        ArgumentNullException.ThrowIfNull(grant);

        var builder = new StringBuilder();
        builder.Append("task: ").AppendLine(title);
        builder.Append("grant: ").AppendLine(grant.Id);
        if (grant.SupersedesGrantId is not null)
        {
            builder.Append("supersedes: ").AppendLine(grant.SupersedesGrantId);
        }

        builder.Append("permission: ").AppendLine(grant.MaxPermission.ToString());
        builder.Append("tools: ").AppendLine(grant.AllowedTools.Count == 0 ? "(none)" : string.Join(", ", grant.AllowedTools));
        builder.Append("roots: ").AppendLine(grant.AllowedRoots.Count == 0 ? "(none)" : string.Join(", ", grant.AllowedRoots));
        builder.Append("expires: ").AppendLine(grant.ExpiresUtc.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture));
        builder.Append("max tool calls: ").AppendLine(grant.MaxToolCalls.ToString(CultureInfo.InvariantCulture));
        builder.Append("schedule: ").AppendLine(scheduledForUtc is { } at
            ? at.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture)
            : "as soon as possible");
        builder.Append("project: ").AppendLine(grant.ProjectId ?? "(none)");
        if (!string.IsNullOrWhiteSpace(blockedReason))
        {
            builder.Append("blocked because: ").AppendLine(blockedReason);
        }

        return builder.ToString().TrimEnd();
    }

    private TaskGrant BuildGrant(
        string taskId,
        GrantDraft draft,
        string? projectId,
        string? supersedes,
        DateTimeOffset now,
        List<string> errors)
    {
        var roots = _pathScope.ValidateRoots(draft.AllowedRoots ?? Array.Empty<string>());
        errors.AddRange(roots.Errors);

        var grant = new TaskGrant
        {
            TaskId = taskId,
            AllowedTools = (draft.AllowedTools ?? Array.Empty<string>()).ToList(),
            AllowedRoots = roots.CanonicalRoots,
            MaxPermission = ToolPermission.Read,
            GrantedUtc = now,
            ExpiresUtc = draft.ExpiresUtc,
            MaxToolCalls = draft.MaxToolCalls,
            ProjectId = projectId,
            SupersedesGrantId = supersedes,
        };

        // Root errors are already reported; avoid a second "needs a root" line for the same cause.
        foreach (var error in TaskGrantValidator.Validate(grant, _registry, now))
        {
            if (!errors.Contains(error))
            {
                errors.Add(error);
            }
        }

        return grant;
    }

    private string Text(string key, string fallback)
    {
        var value = _localization?[key];
        return string.IsNullOrWhiteSpace(value) || (value.StartsWith('!') && value.EndsWith('!'))
            ? fallback
            : value;
    }

    private Task AuditAsync(string taskId, AuditOutcome outcome, string message, CancellationToken cancellationToken) =>
        _audit.AppendAsync(
            new AuditEntry
            {
                Category = AuditCategory.TaskLifecycle,
                Subject = $"task:{taskId}",
                Message = SecretRedactor.Redact(message),
                Outcome = outcome,
                CorrelationId = taskId,
            },
            cancellationToken);

    private async Task<bool> TryAuditAsync(string taskId, AuditOutcome outcome, string message, CancellationToken cancellationToken)
    {
        try
        {
            await AuditAsync(taskId, outcome, message, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Audit write for task {TaskId} failed; the operation was not performed.", taskId);
            return false;
        }
    }
}
