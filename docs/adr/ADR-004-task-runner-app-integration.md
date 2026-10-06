# ADR-004: Task Runner App Integration & Path-Scoped Grants (Phase 6)

## Status
Accepted. **Phase 6a (Core/Infrastructure) implemented and verified on Windows** at commit `4f73234`: `dotnet build -c Release` succeeded, `dotnet test -c Release` passed 371/371 (0 failed, 0 skipped). **Phase 6b (App) implemented at commit `4891eb9`, UNVERIFIED ON WINDOWS**: it has not been compiled or run, because the environment that wrote it has no .NET SDK and cannot run WPF. Phase 6b counts as verified only after a Windows Release build, a passing `dotnet test -c Release`, and the manual checklist under **Required tests**. See **Phase 6b implementation notes**.

§2.5 is superseded in part by [ADR-005](ADR-005-interactive-git-repository-confinement.md). See **Phase 6a implementation notes** for where the code differs from the text below.

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

#### 2.3 Grant creation rules (`TaskGrantValidator` + `TaskPathScope`; see implementation notes)
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
Because Git discovers repositories by walking parents and follows `.git` files, a scoped Git call additionally requires `<repositoryPath>\.git` to exist as a **directory** (not a file, not a reparse point) under a granted root. This stops discovery from escaping to a parent repository and refuses `gitdir:` indirection (worktrees, submodule checkouts) for unattended runs.

**Superseded in part by ADR-005 and ADR-006.** Interactive Git calls are now confined too, so the original sentence "Interactive Git behaviour is unchanged" no longer holds. The repository rules live in one place, `GitRepositoryGuard`, and unattended calls use it rather than a rule of their own. `TaskPathScope` calls `GitRepositoryGuard.FindProblem(resolvedRepositoryPath)`. The guard refuses everything in the paragraph above and also:
- object alternates;
- reparse points directly inside `.git`;
- `commondir`;
- `[include]`/`[includeIf]` sections and `worktree =` in `config` or `config.worktree`;
- filter drivers (ADR-006).

`TaskPathScope` then adds only the grant-specific checks: the `IPathResolution` reparse view of `.git`, and `.git` lying inside a granted root. `SafeGitRunner` additionally pins the work tree and common directory (ADR-005 §2).

The earlier "Residual, accepted: object alternates" is withdrawn: the guard now refuses alternates. ADR-005 lists the Git residuals that remain.

#### 2.6 Residual race, accepted
The gate validates paths and the tool validates them again when it runs. A local actor who can rewrite directories inside a granted root between those two checks could redirect a call to another location that is still inside the global allowed roots (the tool's own policy check still enforces those). This requires local write access and precise timing; it cannot reach outside the global policy. Documented, not mitigated in Phase 6.

#### 2.7 Residual: `project.inspect` link markers
`project.inspect` checks a fixed list of marker names directly under `projectPath`. Each marker passes `IFilesystemPolicy` on its own, but not the grant's roots. If a marker is a link pointing outside the granted root and inside the global allowed roots, the tool reports whether the target exists. It never reads content, and it cannot reach outside the global policy. This is documented, not mitigated in Phase 6.

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

## Phase 6a implementation notes
Where the code differs from, or adds to, the text above (commits `5af1e8d`, `4f73234`):

- **Root validation lives in `TaskPathScope.ValidateRoots`**, not in `TaskGrantValidator`, because it needs `IFilesystemPolicy` and `IPathResolution`. `TaskGrantValidator` enforces the registry-independent rules: at most 16 roots, no blanks, no duplicates, no wildcards. It also enforces that a grant listing a path-scoped tool names at least one root. `TaskRunner` re-runs `ValidateRoots` at the start of every run.
- **`UnattendedRunContext` and `TaskRunner` take a `TaskPathScope`**, not an `IFilesystemPolicy`. Without one, path-scoped calls and grants with roots block (fail closed). `UnattendedRunContext.Check(tool)` is kept as an overload that checks with no arguments, so a path-scoped tool blocks there.
- **`IGitRepositoryScopedTool`** is a marker extending `IPathScopedTool`. It is implemented by `git.status`, `git.diff` and `git.log`, and it selects the Git rule in §2.5 without matching tool names.
- **Resume takes a `GrantDraft`.** The signature is `ResumeAsync(string taskId, GrantDraft draft)`, not a full `TaskDraft`, because a resume replaces only the grant; title, prompt and schedule are unchanged. A resume always builds a new grant id and never accepts a grant object.
- **`ITaskStore.TryCancelAsync`** cancels only a Pending or Blocked task, in a single conditional update. A Running task is never overwritten; `TaskAuthoringService.CancelAsync` then asks the runner.
- **Retry limit:** `HammorTask.MaxAttemptsLimit = 10`. It is enforced in three places:
  - `TaskAuthoringService` rejects drafts outside 1–10;
  - the store rejects granted tasks outside 1–10;
  - the runner never retries past the limit, whatever a stored row says.
- **DI:** Infrastructure registers fallback `IPathResolution` (`ManagedPathResolution`) and `IFilesystemPolicy` with `TryAdd`, so the tools, `TaskPathScope` and authoring share one policy. Platform.Windows still overrides both. `TaskPathScope` and `TaskAuthoringService` are registered as singletons.
- **Approval text:** the localised summary uses the keys `Tasks.Grant.ApprovalSummary` and `Tasks.Grant.ResumeSummary`, falling back to English until 6b adds them to the resource files.

## Phase 6b implementation notes
How the App layer meets §4 and §5 (commit `4891eb9`), including the choices the text left open:

- **Scheduler start:** `App.StartScheduler` runs after `InitialiseAsync`, which has migrated the database and reconciled interrupted tasks, and before the first window is shown. If `InitialiseAsync` throws, the scheduler is never started. If the scheduler itself cannot be constructed, the error is logged and the app continues without it; no unattended task runs that session (fail closed).
- **Scheduler stop:** §4 says "await `StopAsync()`". `OnExit` instead calls it synchronously, with a 5-second limit, on the thread pool, before the host is stopped or disposed. The reason is that WPF does not await an `async void` `OnExit`, so work after its first `await` may never run.
  - Nothing in the scheduler's path waits for the UI thread (`TaskChanged` is forwarded with a non-blocking `Dispatcher.InvokeAsync`), so the wait cannot deadlock.
  - If the limit is ever hit, the task stays Running and the next launch's reconciliation marks it Failed, not re-run.
- **Editor:** `TaskEditorWindow` and `TaskEditorViewModel`, opened through `ITaskEditorDialog` so view models never reference window types.
  - Only registered tools with `ToolPermission.Read` are offered.
  - Folders are chosen with the .NET 8 `OpenFolderDialog` (several at once) and are sent only while a path-scoped tool is selected.
  - Expiry defaults to 24 hours from now. The time is entered as local `HH:mm`. The date picker covers today to today + 30 days, and Core enforces the exact 30-day lifetime.
  - The tool-call cap defaults to 10 (input 1–50).
  - The schedule is optional and offered for new tasks only.
  - The project is optional and shown only when projects exist.
  - Retry attempts are not exposed, so the draft default of 1 applies.
  - The view model only checks that dates and times can be read. Every grant rule, and its error text, comes from Core.
- **Approval:** `TaskAuthoringService` raises its `ConfirmationRequest` through the existing `DialogConfirmationService`.
  - The dialog is now owned by the active window, so it opens over the editor.
  - Its tool name and details are forced left-to-right.
  - The approval logic is unchanged: only Allow approves.
  - On refusal the editor stays open with a notice, and nothing is created or changed.
- **Resume:** the editor is pre-filled from the blocked grant: the tools that are still registered as Read, the roots and the call cap. The expiry is fresh. Core always creates a new grant id with `SupersedesGrantId`, and the old grant is never sent.
- **Cancel:** `TaskAuthoringService.CancelAsync` for Pending, Running and Blocked rows. The buttons appear by state; Core rechecks the state.
- **Live refresh:** `TasksViewModel` subscribes to `ITaskStore.TaskChanged` and updates rows on the dispatcher by task id. Changes that arrive during a reload are replayed after it, so an older snapshot never overwrites a newer state. There is no polling.
- **Visuals:** `StatusBlockedBrush` (#8957E5) in `Tokens.xaml`; `TaskStateToBrushConverter` maps Blocked to it.
- **Localisation:** 47 new keys in both `Strings.resx` and `Strings.ar.resx`. `Tasks.SchedulerNote` is rewritten. `Common.NotImplemented` stays because the Projects page still uses it. Paths, tool names, dates and grant details are kept left-to-right in RTL layouts.
- **Known limitations:**
  - Core error messages are not localised and appear in English in the Arabic UI. The approval details are invariant by design (§3).
  - The Arabic strings have not been reviewed by a native speaker.
  - Dates are shown in local time as `yyyy-MM-dd HH:mm`.

## Affected components
- `HAMMOR.Core` (6a, done): `TaskGrant.AllowedRoots`, `TaskGrantValidator` (root structure, path-tool rule), `IPathScopedTool` + `IGitRepositoryScopedTool` on filesystem/Git/project tools, new `TaskPathScope` (root validation, run-time path check, `GitRepositoryGuard` call), `UnattendedRunContext` (takes `TaskPathScope`), `TaskRunner` (re-validates roots, passes `TaskPathScope`, retry cap), `HammorTask.MaxAttemptsLimit`, `ITaskStore.TryCancelAsync`, new `TaskAuthoringService` + `TaskDraft` + `GrantDraft`, `ToolRegistry` (reject `task.grant`).
- `HAMMOR.Infrastructure` (6a, done): `task_grants.allowed_roots` column (in-place migration, separate immutability trigger `trg_task_grants_roots_immutable`), `SqliteTaskStore` mapping, `TryCancelAsync`, attempt-limit check, DI for fallback policy/resolution, `TaskPathScope` and `TaskAuthoringService`.
- `HAMMOR.App` (6b, implemented at `4891eb9`, unverified on Windows):
  - scheduler start/stop in `App.xaml.cs`;
  - new `TasksViewModel` + `TaskItemViewModel` (live refresh, commands), moved out of `ListPageViewModels.cs`;
  - new `TaskEditorViewModel`, `TaskEditorWindow` and `TaskEditorDialogService`;
  - `TasksPage.xaml`;
  - `TaskStateToBrushConverter` and `Tokens.xaml` (Blocked);
  - `ConfirmationDialog.xaml` (left-to-right technical text);
  - `DialogConfirmationService` (owned by the active window);
  - localisation (en/ar).
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
Not yet run (status: unverified on Windows). Run it on the 6b branch after `dotnet build -c Release` and `dotnet test -c Release` pass. A configured Claude API key is needed for steps that actually run a task. Logs are under `%LOCALAPPDATA%\HAMMOR\logs`.

1. **Start order.** Launch HAMMOR. In the day's log, `Task scheduler started.` appears after the reconciliation and memory-index lines. Kill HAMMOR while a task is Running, relaunch, and confirm that task shows Failed and is not re-run.
2. **Banner.** Tasks page: the info bar says "Unattended tasks", and no "Not Implemented" text appears.
3. **Text-only task.** Choose New task…, enter a title and instruction, select no tools, keep the default expiry, choose Review and create…, then Allow. The task appears immediately without a manual refresh, runs, and ends Completed with a visible result.
4. **Approval shows the exact grant.** In step 3 the confirmation lists `task:`, `grant:`, `permission: Read`, `tools:`, `roots:`, `expires:`, `max tool calls:`, `schedule:` and `project:`.
5. **Deny and dismissal.** Create again, but choose Deny. The editor shows "Not approved. Nothing was created." and no task appears. Repeat, closing the confirmation with Escape or ×; same result. Cancel the editor; nothing is created.
6. **Read-only tools only.** The tool list contains only Read tools: no `filesystem.write_file`, `filesystem.delete_file` or `memory.save`.
7. **Folders.** Selecting `filesystem.read_file` (or a `git.*` tool, or `project.inspect`) shows Allowed folders. Add folder… opens the Windows folder picker and allows several folders. Deselecting all filesystem tools hides the section. With such a tool but no folder, Core's error ("must name at least one root") is shown and no dialog appears.
8. **Expiry limits.** The date picker offers no date beyond 30 days. A time like `25:00` gives the HH:mm error. Today with a time in the past gives Core's expiry error.
9. **Inside and outside the root.** Create a task with `filesystem.read_file` and one folder, asking it to read a file inside the folder: it runs. Then ask for a file outside the folder: the task shows the Blocked badge (purple) with a reason naming the path.
10. **Resume.** On the Blocked task, choose Resume with new grant…. The form is pre-filled with tools, folders and call cap, and shows the blocked reason. Change what is needed, choose Review and resume…, and confirm the dialog shows `supersedes: <old grant id>`. Allow, and the task returns to Pending and runs. Repeat with Deny: "Not approved. The task stays blocked." and nothing changes.
11. **Cancel.** Cancel a Pending task, a Blocked task and a Running task. Each ends Cancelled; the Running one shows "Cancellation requested…" first. Completed, Failed and Cancelled rows have no Cancel button.
12. **Exit while running.** Exit while a task runs. The log shows `Task scheduler stopped.`, and on the next launch that task is Cancelled (or Failed if the 5 s limit was hit), never re-run.
13. **Arabic.** Switch to Arabic. All new texts are translated, with no `!Key!` placeholders. The layout mirrors, while paths, tool names, dates and the confirmation details read left-to-right. The Blocked badge is visible.

## Consequences
- Unattended tasks become usable from the app, still Read-only and approval-gated.
- Path-scoped grants are mandatory for filesystem, Git and project tools, so grants become strictly narrower than in Phase 5; any Phase 5 grant naming those tools blocks until resumed with roots.
- Unattended Git calls require the repository root (with a `.git` directory) inside a granted root; worktree/submodule checkouts cannot be inspected unattended.
- App behaviour is verified manually only; regressions in 6b will not be caught by `dotnet test`.
- The Phase 5 grant-audit gap is closed.
