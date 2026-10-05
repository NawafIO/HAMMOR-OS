# ADR-004: Task Runner App Integration & Path-Scoped Grants (Phase 6)

## Status
Accepted — specification only. No code accompanies this ADR. Implementation is a separate, explicitly authorised step.

## Date
2026-10-05

## Context
Phase 5 (ADR-003, tag `phase-5`) delivered an engine-only unattended task runner: per-task Read-only grants with mandatory finite expiry, immutable grants, Blocked → Pending only under a new grant, one task at a time, audit and cancellation. ADR-003 deferred four items:

1. Path-level grant scope for filesystem/Git tools (ADR-003 §3 `Scope`).
2. App wiring: `TaskSchedulerService.Start()` is never called.
3. Grant-creation UI with the confirmation dialog (ADR-003 §3).
4. Blocked-task UI on the Tasks page (ADR-003 §5, required test 18).

Inspection of the Phase 5 code also found a deviation from ADR-003 §7 and the §3 clarification: `SqliteTaskStore.CreateAsync` and `ResumeBlockedAsync` write **no audit entries**. Grant creation, supersession and the superseded-grant id on a new approval are therefore not audited; grants appear in the audit log only through the runner's "Run started" entries. Phase 6 closes this gap.

Relevant facts about the current code:
- The App uses a Generic Host: `_host.StartAsync()` → `InitialiseAsync` (load config, `SqliteDatabase.Migrate()`, `ITaskStore.ReconcileInterruptedAsync()`, memory index, status probe) → show first window. `OnExit` stops and disposes the host.
- `DialogConfirmationService` implements `IConfirmationService`; dismissal or any non-Allow result is refusal; with no UI thread it refuses.
- Tool path arguments: `filesystem.*` use `path`; `git.status`, `git.diff`, `git.log` use `repositoryPath`; `project.inspect` uses `projectPath`. `memory.search` has none.
- `IFilesystemPolicy.Validate(raw)` canonicalises through `IPathResolution.ResolveFinalPath` on the nearest existing ancestor and, for an allowed reparse point, returns the resolved target as `NormalizedPath`. Configured allowed roots that are themselves reparse points, or lie inside a reparse chain, are rejected (`FilesystemPolicy.TryAddRoot`).
- `SafeGitRunner` runs fixed `git` arguments with `WorkingDirectory` set to the policy-normalised `repositoryPath`. Git's own repository discovery walks parent directories and follows a `.git` *file* (`gitdir:`) indirection, so Git can read repository data outside the directory it was given.
- The test project is `net8.0` and cannot reference the WPF App.

Owner decisions for Phase 6:
1. Merge Phase 5 into `main` first; Phase 6 builds from `main`.
2. The scheduler starts automatically after migration and reconciliation; no enable/disable setting in v1.
3. App work (6b) is verified with a written manual Windows checklist; no `net8.0-windows` test project in Phase 6.
4. Path-scoped grants apply to filesystem, Git and `project.inspect` tools now.

## Decision

### 1. Principles (unchanged from ADR-003, restated because the UI is new)
1. The UI is not a security boundary. Every rule is enforced in Core/Infrastructure (authoring service, store, run-time gate). A faulty view model can produce a request that is rejected; it cannot produce a grant that runs.
2. No grant exists without explicit user approval through `IConfirmationService`. Dismissal is refusal.
3. Grants only narrow. `PermissionEvaluator`, `IFilesystemPolicy` and the Git/project tools run unchanged inside every call.
4. Unattended runs remain Read-only. Recurrence, parallel runs, notifications other than the Tasks page, and Write grants remain out of scope.
5. Testable logic lives in Core. The App only renders state and forwards user intent.

### 2. Path-scoped grants

#### 2.1 Grant field
`TaskGrant` gains `AllowedRoots` (list of absolute directory paths), stored as a new nullable column (JSON array) on `task_grants`, covered by the existing immutability trigger.

#### 2.2 Which tools are path-scoped
A tool is path-scoped if it implements a new Core contract:

```
public interface IPathScopedTool
{
    /// Argument names that carry filesystem paths the tool will touch.
    IReadOnlyList<string> PathArguments { get; }
}
```

Implemented by `filesystem.list_directory`, `filesystem.read_file` (`path`), `git.status`, `git.diff`, `git.log` (`repositoryPath`) and `project.inspect` (`projectPath`). `filesystem.write_file` and `filesystem.delete_file` also implement it for consistency, though they remain ungrantable (not Read). No argument names are hard-coded in the gate.

#### 2.3 Grant creation rules (added to `TaskGrantValidator`)
- If `AllowedTools` contains any path-scoped tool, `AllowedRoots` must be non-empty. There is no "any allowed path" grant for path-scoped tools.
- At most 16 roots; no blanks, no duplicates (case-insensitive after canonicalisation), no wildcards.
- Each root must pass `IFilesystemPolicy.Validate`, must be an existing directory, must not be a reparse point and must not lie inside a reparse chain (same rule as configured allowed roots). Stored in the policy-canonical form with a trailing separator.
- A grant with `AllowedRoots` but no path-scoped tool is valid (roots unused).

#### 2.4 Run-time check (in `UnattendedRunContext.Check`, after the existing grant checks, before `PermissionEvaluator`)
For a path-scoped tool, every declared path argument must be present and non-blank. Each value is run through `IFilesystemPolicy.Validate`; if the policy denies it, the call is **blocked**. Otherwise the policy's `NormalizedPath` must be under a granted root:

- segment-aware: equal to the root (ignoring the trailing separator) or starting with `root + separator`; `C:\Repo` never matches `C:\Repo-evil`;
- case-insensitive (`OrdinalIgnoreCase`), matching the filesystem policy;
- applied to the resolved path the policy returns, never to the raw argument.

Any miss blocks the run with a reason naming the argument and the normalised path. A grant without roots that lists a path-scoped tool (for example a Phase 5 grant) blocks at run time.

#### 2.5 Git-specific rule
Because Git discovers repositories by walking parents and follows `.git` files, a scoped Git call additionally requires `<repositoryPath>\.git` to exist as a **directory** (not a file, not a reparse point) under a granted root. This stops discovery from escaping to a parent repository and refuses `gitdir:` indirection (worktrees, submodule checkouts) for unattended runs. Interactive Git behaviour is unchanged.

Residual, accepted: object alternates (`.git/objects/info/alternates`) can make Git read object data elsewhere; Git output is still bounded/redacted and the operations remain read-only.

#### 2.6 Residual race, accepted
The gate validates paths and the tool validates them again when it runs. A local actor who can rewrite directories inside a granted root between those two checks could redirect a call to another location that is still inside the global allowed roots (the tool's own policy check still enforces those). This requires local write access and precise timing; it cannot reach outside the global policy. Documented, not mitigated in Phase 6.

### 3. Grant authoring and approval (Core: `TaskAuthoringService`)
The only component that creates grants. The store keeps its own validation as defence in depth.

- `CreateAsync(TaskDraft draft)`: draft = title, prompt, tools, roots, expiry, max tool calls, optional schedule, optional project.
  1. Build the grant with a new id and `GrantedUtc = now`; validate with `TaskGrantValidator` (registry + roots + policy). Invalid → typed validation result, no dialog.
  2. Request approval through `IConfirmationService` with a `ConfirmationRequest` whose tool name is the reserved, unregistered name `task.grant`, permission `Read`, a localised summary (via Core `ILocalizationService`) and `Details` listing the exact grant fields in invariant `key: value` form (tools, roots, expiry, call cap, schedule, project). `Details` is generated in Core and tested.
  3. Refused → audit `Denied` ("grant refused"), create nothing.
  4. Approved → audit "grant approved" (with grant id) **before** writing; if that audit write fails, create nothing. Then `ITaskStore.CreateAsync`; then audit "task created".
- `ResumeAsync(string taskId, TaskDraft draft)`: task must be Blocked. Builds a **new** grant (new id, `SupersedesGrantId` = current grant id) from the draft, which the UI pre-fills from the old grant. Same validate → approve → audit → write sequence, calling `ResumeBlockedAsync`. The approval audit names both the new and the superseded grant ids. The old grant object is never resubmitted.
- `CancelAsync(string taskId)`: Pending or Blocked → `Cancelled` through the store; Running → `ITaskRunner.RequestCancel` (the runner already records the cancellation). Every cancel request is audited.
- `task.grant` must never be registered as a tool; registration under that name is rejected.

ADR-003 §7's "grant expired" requirement is already met at run time by the runner's "blocked before running: … expired" audit; no background expiry sweep is added.

### 4. Scheduler startup and shutdown (App)
- Start `TaskSchedulerService` **after** `InitialiseAsync` completes successfully (after `Migrate()` and `ReconcileInterruptedAsync()`), before the first window is shown. If initialisation throws, the scheduler is not started.
- No setting to disable it in v1 (owner decision 2). It only executes tasks that hold a valid, user-approved grant.
- On exit, `await TaskSchedulerService.StopAsync()` **before** `_host.StopAsync()`/`Dispose()`, so a running task is cancelled and its Cancelled state and audit entry are written while services are alive.
- Implemented as an explicit call in `App`, not as a hosted service started by `_host.StartAsync()` (which runs before migration).

### 5. Tasks page (App)
- New-task form: title, prompt, Read-only tool picker (only registered Read tools are listed), root picker (folder dialog, only shown/required when a path-scoped tool is selected), expiry (required; picker limited to now…now + 30 days), max tool calls (1–50), optional schedule time, optional project. Submit calls `TaskAuthoringService.CreateAsync`; validation errors are shown inline.
- Each task row shows a state badge including **Blocked** (own brush), the blocked reason, error, result summary, schedule, and grant expiry.
- Actions: **Cancel** on Pending/Running/Blocked; **Resume with new grant** on Blocked, opening the form pre-filled from the old grant and calling `ResumeAsync`.
- Live refresh from `ITaskStore.TaskChanged`, marshalled to the UI dispatcher.
- The "Not Implemented / Tasks.SchedulerNote" banner is replaced with accurate text (tasks run unattended, Read-only, one at a time, only with an approved grant).
- Blocked tasks notify only through this page (ADR-003 decision 3 unchanged).
- All new strings in `Strings.resx` and `Strings.ar.resx`, including `Tasks.State.Blocked`; layout checked in Arabic RTL.

### 6. Out of scope
Write grants, Execute/Destructive grants, recurrence, parallel runs, tray/toast notifications, enable/disable setting, a `net8.0-windows` test project, any change to `PermissionEvaluator`, `IFilesystemPolicy` semantics, `ToolCallValidator`, or the set of Git operations.

## Affected components
- `HAMMOR.Core`: `TaskGrant.AllowedRoots`, `TaskGrantValidator` (roots, path-tool rule), `IPathScopedTool` + implementations on filesystem/Git/project tools, `UnattendedRunContext` (path and `.git` checks; takes `IFilesystemPolicy`), `TaskRunner` (passes policy into the context), new `TaskAuthoringService` + `TaskDraft`, `ToolRegistry` (reject `task.grant`).
- `HAMMOR.Infrastructure`: `task_grants.allowed_roots` column (in-place migration, immutability trigger updated), `SqliteTaskStore` mapping, DI for `TaskAuthoringService`.
- `HAMMOR.App`: scheduler start/stop in `App.xaml.cs`, `TasksViewModel` (live refresh, commands), Tasks page and new-task form, `TaskStateToBrushConverter` (Blocked), localisation (en/ar).
- Unchanged: `PermissionEvaluator`, `FilesystemPolicy`, `SafeGitRunner`, `ToolCallValidator`, interactive chat path.

## Required tests

### Automated (Core/Infrastructure, counted in the Windows run)
Path scope:
1. Path under a granted root → allowed; sibling-prefix root (`C:\Repo` vs `C:\Repo-evil`) → blocked.
2. Case differences resolve the same way; trailing separators do not matter.
3. `..` traversal, relative paths, `\\?\` and UNC forms → blocked or normalised consistently with the policy.
4. Junction/symlink inside a granted root pointing outside the root (but inside global roots) → blocked (uses resolved path).
5. Missing or blank path argument → blocked.
6. Path-scoped tool in a grant with no roots → rejected at creation; Phase 5-style grant without roots → blocked at run time.
7. Root that is a reparse point, inside a reparse chain, outside global roots, protected, non-existent, or a file → rejected at creation.
8. Git: `.git` directory present under root → allowed; `.git` as a file, missing (parent-repo discovery), or reparse point → blocked.
9. `memory.search` (not path-scoped) unaffected by roots.
10. Grant roots round-trip through SQLite and are covered by the immutability trigger; migration from the Phase 5 schema keeps existing grants (no roots).

Authoring:
11. Valid draft + approval → task and grant created; audit "approved" then "created" with grant id.
12. Refusal or dismissal → nothing created; audit "refused".
13. Invalid draft → no dialog shown, nothing created.
14. Approval-audit write failure → nothing created.
15. Resume: new grant id, supersedes the old one, approval audit names both ids; resume of non-Blocked task rejected; old grant never reused.
16. Cancel: Pending/Blocked → Cancelled and audited; Running → runner cancellation requested and audited.
17. `Details` text lists every grant field exactly; `task.grant` cannot be registered as a tool.

### Manual Windows verification checklist (6b)
1. Fresh start: migration and reconciliation complete before the scheduler starts (log order); a task left Running by a killed process shows Failed, not re-run.
2. Create a text-only task (no tools) with near-future schedule → runs once, Completed, result visible.
3. Create a task with `filesystem.read_file` + one root; approve → runs; reading inside root succeeds.
4. Same, model asked to read outside the root → task shows Blocked with reason.
5. Approval dialog: Deny and window close both create nothing.
6. Blocked → Resume with new grant: form pre-filled, new approval required, task runs; old grant not reused.
7. Cancel a Pending, a Blocked and a Running task; each shows Cancelled.
8. Expiry picker cannot exceed 30 days; tool picker lists only Read tools; root picker appears only for path-scoped tools.
9. List updates live without manual refresh.
10. Exit while a task runs → on next launch it is Cancelled (not Running, not re-run).
11. Switch to Arabic: all new strings translated, RTL layout correct, Blocked badge visible.

## Consequences
- Unattended tasks become usable from the app, still Read-only and approval-gated.
- Path-scoped grants are mandatory for filesystem, Git and project tools, so grants become strictly narrower than in Phase 5; any Phase 5 grant naming those tools blocks until resumed with roots.
- Unattended Git calls require the repository root (with a `.git` directory) inside a granted root; worktree/submodule checkouts cannot be inspected unattended.
- App behaviour is verified manually only; regressions in 6b will not be caught by `dotnet test`.
- The Phase 5 grant-audit gap is closed.
