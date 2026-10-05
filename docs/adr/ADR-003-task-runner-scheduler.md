# ADR-003: Task Runner & Scheduler (unattended execution)

## Status
Accepted and implemented (Phase 5, engine-only). Verified on Windows: `dotnet build -c Release` passed and `dotnet test -c Release` passed 255/255 (199 existing + 56 new).

Phase 5 delivers the Core/Infrastructure engine only. See **Implementation status** for what is deferred.

## Context
`HammorTask` and `ITaskStore` persist tasks and reconcile `Running` rows to `Failed` after a crash, and `HammorTask` already carries `ScheduledForUtc`, `AttemptCount` and `MaxAttempts`. Nothing executes tasks. The UI says so (`Tasks` page, README limitations).

Today every protected tool call is authorised by `IPermissionEvaluator.AuthoriseAsync`, which resolves `ConfirmationRequired` by prompting a present user through `IConfirmationService`. An unattended run has no user. If the runner reused the interactive path it would either hang, or an implementer would be tempted to auto-approve. Both are unacceptable. This ADR defines how unattended execution gets authority without weakening the existing pipeline.

## Decision

### 1. Principles (non-negotiable)
1. The runner executes tool calls only through `AgentPipeline`/`AgentLoop`. No second execution path, no direct `ITool.ExecuteAsync` calls.
2. The full chain is unchanged: tool lookup → schema validation → permission authorisation → audit decision → execution → bounded/redacted result → audit outcome.
3. **No implicit approval.** Authority comes only from a grant the user created, while present, for that task.
4. **Fail closed.** Anything not covered by the grant is blocked or deferred, never run.
5. In v1 an unattended run may hold **Read authority only**. `Write`, `Execute` and `Destructive` are not grantable (see §3).
6. Cancellation is honoured at every await, including between rounds, during tool execution and while waiting to be scheduled.
7. Core stays WPF-independent. The runner lives in Core (logic) and Infrastructure (hosting).

### 2. Task model changes
`HammorTask` gains:
- `Grant` (nullable `TaskGrant`, see §3). Null means the task is **interactive-only** and the runner will not execute it.
- `TaskState.Blocked` — waiting for user action (grant missing/insufficient, or a call needed approval beyond the grant). Not terminal. `BlockedReason` (string) records why.
- `Prompt` — the instruction the agent receives (distinct from `Title`/`Description`, which are display text).

State machine:
`Pending → Running → Completed | Failed | Cancelled`, plus `Running → Blocked` and `Blocked → Pending` (only after explicit user approval that creates a **new, valid `TaskGrant`**; the blocked or expired grant is never reused, extended, edited or re-activated). `Blocked → Running` directly is forbidden. Crash reconciliation is unchanged: `Running` at startup becomes `Failed`, never resumed automatically.

### 3. Per-task scoped authorisation (`TaskGrant`)
Created at task creation, in the presence of the user, through the normal `IConfirmationService` dialog showing exactly what is being granted. Immutable once the task leaves `Pending`; a grant is never edited in place. A `Blocked` task returns to `Pending` only when the user, present, approves a **new** grant through the same confirmation dialog, and that new grant passes every creation rule (Read-only ceiling, mandatory future expiry, explicit tools). The previous grant is retired permanently: it is kept for audit, marked superseded, and can never authorise another run, including after a restart. Approval of the new grant is audited with the id of the grant it supersedes.

| Field | Meaning |
|---|---|
| `AllowedTools` | Explicit tool names. Empty means no tools (text-only task). No wildcards. |
| `MaxPermission` | Ceiling. **v1: must be `Read`.** `Write`, `Execute` and `Destructive` are rejected at grant creation. |
| `Scope` | Optional constraints re-checked at call time: project id, and for filesystem/git tools, paths that must also pass `IFilesystemPolicy`. The grant narrows the policy; it never widens it. **Phase 5 implements project-id scope only; path-level scope is deferred** (the full `IFilesystemPolicy` still applies to every call). |
| `GrantedUtc` / `ExpiresUtc` | **`ExpiresUtc` is mandatory.** A grant without an expiry, or with an expiry in the past or null/`MaxValue`, is rejected at creation. There is no default and no "forever" grant. A grant is invalid after expiry; a run starting past expiry is `Blocked`. |
| `MaxToolCalls` | Hard cap per run, in addition to `AgentLoopOptions` round limits. |

Why only `Read` in v1: unattended Write has no present user to catch a bad call, and the block/audit/cancel paths are not yet proven. `Destructive` additionally means "always requires explicit confirmation" (`ToolPermission`); a standing grant would make that false. Any widening requires a new ADR with evidence of need.

Because only `Read` tools can be granted, the v1 `AllowedTools` list may contain only registered tools whose `Permission` is `Read` (e.g. `filesystem.read_file`, `filesystem.list_directory`, `git.status`, `git.diff`, `git.log`, `project.inspect`, `memory.search`). Granting a tool above `Read` is rejected at creation, and the check is repeated at call time.

### 4. Unattended authorisation path
Introduce a non-interactive implementation of the existing contracts, selected by the runner, not by the model or the task:
- An `IConfirmationService` that **never prompts and always returns `false`** (`UnattendedConfirmationService`). Anything the evaluator would have asked a human about is refused.
- A grant check that runs **before** `AuthoriseAsync`: tool in `AllowedTools`, `tool.Permission <= MaxPermission`, grant unexpired, `MaxToolCalls` not exceeded, scope satisfied. Failure means the call is denied and the run moves to `Blocked`.
- The normal `PermissionEvaluator` still runs afterwards, so the configured policy (`autoApproveUpTo`, `alwaysConfirmDestructive`) can only make things stricter. A grant never overrides a `Denied` or `ConfirmationRequired` verdict; with the always-false confirmation service, `ConfirmationRequired` becomes a block.
- The grant check is implemented as a decorator or option on the pipeline's authorisation step, not as a branch the model can influence. Tool arguments and model text cannot alter the grant.

### 5. Blocked vs. Failed
- **Blocked:** a tool call was denied by grant/policy, or needed confirmation. The run stops at that call (it does not skip and continue), records `BlockedReason` (tool name, permission, which rule), and surfaces it **only on the Tasks page** (state badge plus `BlockedReason`). There is no tray, toast or other notification channel in v1, and no dependency on one. No retry is consumed.
- **Failed:** execution error, provider error, cap exceeded, crash reconciliation. Retried only per `MaxAttempts`, with bounded backoff, and never after a `Blocked`.

### 6. Scheduling
- Single hosted runner in the app process. No external scheduler, no OS task registration, no process spawning.
- Polling is event-driven where possible (`TaskChanged`) with one bounded timer for the next `ScheduledForUtc`; idle-cheap, consistent with ARCHITECTURE.md's "no polling" principle.
- Concurrency: **exactly one task at a time in v1.** Not configurable. The runner holds a single execution slot; further due tasks wait in `Pending`. Any future parallelism needs a new ADR.
- On startup, `ReconcileInterruptedAsync` runs first; the runner then considers only `Pending` tasks with a valid grant.
- Missed schedules (app was closed) run once on next start if the grant is still valid; they are not replayed per missed interval.
- Recurrence is out of scope for this phase (one-shot and "as soon as possible" only).

### 7. Audit
All via the existing `IAuditLog` with `AuditCategory.TaskLifecycle` for lifecycle and the existing `Authorisation`/`ToolExecution` categories for calls, correlated by `ToolInvocation.InvocationId` plus the task id. Required entries:
- grant created / changed / expired
- run started (task id, attempt, grant summary)
- every authorisation decision (already produced by `InvokeToolAsync`) marked as unattended
- every tool outcome (already produced)
- blocked (with reason), deferred, cancelled, failed, completed
- retry scheduled

Messages pass through `SecretRedactor`. A failed audit write fails the run (existing rule: audit writes are never dropped silently).

### 8. Cancellation
- Each run has its own `CancellationTokenSource` linked to app shutdown and to a per-task user cancel.
- Cancel before start → `Cancelled`, no execution. Cancel mid-run → token flows into the provider call and `ITool.ExecuteAsync`; state ends `Cancelled`; audit entry written even on cancel.
- App shutdown during a run leaves the row `Running`; reconciliation marks it `Failed` on next launch (existing behaviour).

### 9. Output bounds
Run results stored in `HammorTask.Result` and the audit log are truncated and redacted with the same limits the loop already applies to tool results. Task result is never raw tool output.

### 10. Explicitly out of scope
Shell/process execution, arbitrary Git commands, recurrence/cron, parallel runs, network-triggered tasks, STT, tray/toast notifications, remote/mobile triggering, any grant above `Read`, unattended Write.

## Affected components
- `HAMMOR.Core`: `HammorTask`, new `TaskGrant`, runner service, grant check in the pipeline authorisation step, `UnattendedConfirmationService`.
- `HAMMOR.Infrastructure`: `SqliteTaskStore` schema migration (new columns, new state), hosted service registration.
- `HAMMOR.App`: task creation UI with grant confirmation, Blocked display, Tasks page copy. Minimal for the first iteration.
- Unchanged: `PermissionEvaluator` semantics, filesystem/git policy, tool registry.

## Required tests
Security (treated as architecture, must not be weakened):
1. Task with no grant is never executed.
2. Tool not in `AllowedTools` → blocked, tool's `ExecuteAsync` never called.
3. Tool permission above `MaxPermission` → blocked.
4. Grants with `MaxPermission` above `Read`, or listing any tool whose permission is above `Read`, are rejected at creation (Write, Execute and Destructive each tested). Call-time re-check also blocks a tool whose permission was raised after grant creation.
5. Tool needing confirmation under unattended service → blocked, never auto-approved.
6. Grant without `ExpiresUtc`, with a past expiry, or with an effectively unbounded expiry is rejected at creation. Expired grant at run time → blocked. `MaxToolCalls` exceeded → failed.
7. Grant cannot widen filesystem policy: allowed tool, path outside roots → still denied.
8. Model output cannot alter grant or state (adversarial tool arguments and text).
9. Grant immutable after leaving `Pending`; no API edits a grant in place.
10. `Blocked → Running` is rejected. `Blocked → Pending` is rejected unless a new grant (new id) is approved and valid; supplying the old, expired or blocked grant, or the same grant with a later expiry, is rejected. A superseded grant never authorises a run, including after restart. The new approval is audited with the superseded grant id.

Lifecycle and reliability:
11. State transitions, including crash reconciliation and no auto-resume.
12. `MaxAttempts` retry with backoff; no retry after Blocked.
13. `ScheduledForUtc` honoured; missed schedule runs once. Two due tasks never execute concurrently (second stays `Pending` until the slot frees). A missed schedule whose grant expired meanwhile is `Blocked`, not run.
14. Cancellation before start, mid-provider-call, mid-tool; audit entry present in each.
15. Audit completeness and correlation per run; audit write failure fails the run.
16. Result and audit output bounded and redacted.
17. Store migration from the Phase 4 schema keeps existing rows valid (existing tasks have no grant and stay interactive-only).
18. Blocked tasks are visible on the Tasks page with their reason and trigger no other notification channel. **Deferred with the App work** (no Tasks-page change in Phase 5; no other notification channel exists).

## Implementation status (Phase 5)
Implemented in Core/Infrastructure and covered by tests:
- `TaskGrant` + `TaskGrantValidator` (Read-only ceiling, mandatory finite expiry capped at 30 days, explicit tools, call cap of 1–50).
- `TaskStateMachine`, `TaskState.Blocked`, enforced by `SqliteTaskStore`.
- Immutable grants: store refuses grant changes; SQLite triggers block grant edits/deletes and grant swaps outside a Blocked → Pending resume.
- `ResumeBlockedAsync`: new grant id required, must declare the superseded grant, old grant permanently retired.
- `UnattendedRunContext` + `UnattendedConfirmationService` (never approves); grant check before the unchanged `PermissionEvaluator` path in `AgentLoop`; `AgentPipeline.RunUnattendedAsync`.
- `TaskRunner` (single slot, lifecycle audit, retry/backoff, cancellation, bounded/redacted result; no conversation memory writes).
- `TaskSchedulerService` (event-driven, bounded idle) registered in DI.
- In-place schema migration from Phase 4.

Deferred (not in Phase 5):
- **Path-level grant scope** for filesystem/Git tools (§3 `Scope`). Project-id scope only.
- **App wiring**: `TaskSchedulerService.Start()` is not called by the app, so nothing runs unattended in the app.
- **Grant-creation UI** with the confirmation dialog (§3). No grant can be created from the app.
- **Blocked-task UI** on the Tasks page (§5, required test 18).

Behaviour note: a model tool call that is malformed or names an unknown tool is returned to the model as an error (unchanged Phase 4 behaviour) rather than blocking the run; only grant/permission misses block.

## Owner decisions (resolved)
1. **Read-only only for v1.** No unattended Write.
2. **Explicit expiry mandatory** on every grant. No "forever" grants.
3. **Blocked tasks notify only through the Tasks page** for now. No tray dependency.
4. **One task at a time** for v1.

## Consequences
- Unattended work is possible without a standing exception to confirmation.
- A schema migration and a new task state are required.
- Some tasks users expect to "just run" will block until the user approves a new grant. That is intended.
