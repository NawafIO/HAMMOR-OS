# ADR-006: Neutralising program execution from repository Git config

## Status
Accepted and implemented. Verified on Windows at commit `f57c4df`: `dotnet build -c Release` succeeded with 0 errors and 2 warnings, and `dotnet test -c Release` passed 325/325 (299 existing + 26 new), 0 failed, 0 skipped. Both warnings predate this change: CS0067 in `FilesystemPolicyFactory.cs` and xUnit1031 in `TaskStoreGrantTests.cs`. Also verified on Linux with real git 2.43.0 (Ubuntu `2.43.0-1ubuntu7.3`) and 2.55.0 (built from source): 325/325 on each.

## Date
2026-10-06

## Context
After ADR-005, `SafeGitRunner` runs three fixed commands, confined to the validated repository root:

| Tool | Command (ADR-005) |
|---|---|
| `git.status` | `git status --porcelain=v1 --ignore-submodules=all` |
| `git.diff` | `git diff --no-color --ignore-submodules=all [--staged]` |
| `git.log` | `git log --oneline -n <1..50> --no-decorate` |

ADR-005 **Residual risks** item 1 is still open: repository config can make git execute arbitrary programs. The owner verified this on Windows with git 2.55.0.windows.5 and the ADR-005 environment in place: `core.fsmonitor=<cmd>` runs on `git status`, and `diff.external=<cmd>` runs on `git diff`.

### Threat model
- **Untrusted:** the whole repository directory. That covers tracked files (including `.gitattributes`) and `.git` itself: `config`, `config.worktree`, `info/attributes`, `hooks/`, refs (including `refs/replace/`) and promisor settings.
- **Trusted:** the user's system and global git config, the HAMMOR process environment, and the installed git binary. If the user installed git-lfs globally, its filter is the user's choice.

### Constraints
- No new git operations: one git process per tool call, with a fixed argument list. That rules out even `git --version`.
- No generic command executor. `SafeGitRunner` keeps exactly `GetStatusAsync`, `GetDiffAsync` and `GetLogAsync` (asserted by `GitToolsSecurityTests.SafeGitRunner_does_not_expose_generic_executor`).
- `IFilesystemPolicy` semantics are unchanged.

## Vectors (empirically verified)
Every row below was reproduced with marker files on git **2.55.0** and git **2.43.0** on Linux. The ADR-005 environment was applied (`GIT_*` scrubbed, ceiling, `GIT_WORK_TREE`, `GIT_COMMON_DIR`, `safe.bareRepository=explicit`) and the ADR-005 command lines were used. "Live" means the payload ran.

| # | Vector (from untrusted config/attributes) | Live on | Mitigation | Verified |
|---|---|---|---|---|
| 1 | `core.fsmonitor=<cmd>` | status, diff | command-scope `core.fsmonitor=false` | 2.55, 2.43 |
| 2 | `diff.external=<cmd>` | diff, diff --staged | `--no-ext-diff` | 2.55, 2.43 |
| 3 | `diff.<drv>.command`, selected by a `diff=<drv>` attribute | diff, diff --staged | `--no-ext-diff` | 2.55, 2.43 |
| 4 | `diff.<drv>.textconv` | diff, diff --staged | `--no-textconv` | 2.55, 2.43 |
| 5 | `filter.<drv>.clean` / `.process`, selected by a `filter=<drv>` attribute | status, diff (stat-dirty files are re-hashed through the filter) | **refusal** (§4); no flag or override exists | 2.55, 2.43 |
| 6 | `log.showSignature=true` plus `gpg.program`, or `gpg.format=ssh` plus `gpg.ssh.program`, on a commit carrying a `gpgsig` header | log | `--no-show-signature` | 2.55, 2.43 |
| 7 | `hooks/post-index-change` (file hook), fired when status or diff rewrites `.git/index` after a stat refresh | status, diff | no index writes: `GIT_OPTIONAL_LOCKS=0` (status) and command-scope `diff.autoRefreshIndex=false` (diff) | 2.55, 2.43 |
| 8 | `hook.<name>.command` + `hook.<name>.event=post-index-change` (config-defined hooks, git ≥ 2.54) | status, diff | same as 7 | 2.55 |
| 9 | Lazy fetch from a promisor remote (`remote.<r>.uploadpack=<cmd>`, `core.sshCommand`, credential helpers, …) | diff (missing blob) | `GIT_NO_LAZY_FETCH=1` | 2.55, 2.43* |
| 10 | Submodules: status and diff run `git status` inside each submodule, which honours the **submodule's** config. A filter there runs. | status, diff | already closed by ADR-005 §4 `--ignore-submodules=all` (re-verified) | 2.55, 2.43 |
| 11 | `diff.submodule=diff` makes diff run `git diff` inside the submodule, which does not inherit `--no-ext-diff` | diff | already closed by ADR-005 §4 (re-verified) | 2.55, 2.43 |

\* `GIT_NO_LAZY_FETCH` is in upstream git from 2.45.0, and in the May 2024 security releases 2.39.4, 2.40.2, 2.41.1, 2.42.2, 2.43.4 and 2.44.1 (checked in the git source at those tags). Ubuntu's 2.43.0-1ubuntu7.3 carries that backport.

Checked and **not** live, so no change is needed:
- `core.pager`, `pager.status`, `pager.log`. Git starts a pager only when stdout is a terminal, and `SafeGitRunner` redirects stdout.
- `core.fsmonitor` on `git log`. Log never reads the index.

Git for Windows only, read in the `v2.55.0.windows.5` source, not run: the deprecated `core.useBuiltinFSMonitor=true` starts git's own fsmonitor daemon. It is consulted only when `core.fsmonitor` is unset or a path (`fsmonitor-settings.c`, `lookup_fsmonitor_settings`). The boolean `core.fsmonitor=false` from §3 returns before that check.

**Combined check.** Every non-filter vector above was planted in one repository, in both the superproject's and a submodule's config, alongside the pager settings and a promisor remote. Under the full ADR-005 + ADR-006 environment and flags, with `GIT_TRACE` enabled, `status`, `diff`, `diff --staged` and `log` started **zero** child processes on both versions, and `.git/index` was not rewritten. With a repository filter driver added, the filter was the only child process git started. That is why §4 is a refusal.

## Decision

### 1. Fixed arguments per command
| Tool | Fixed argument list (additions in bold) |
|---|---|
| `git.status` | `status --porcelain=v1 --ignore-submodules=all` (unchanged) |
| `git.diff` | `diff --no-color --ignore-submodules=all` **`--no-ext-diff --no-textconv`** `[--staged]` |
| `git.log` | `log --oneline -n <1..50> --no-decorate` **`--no-show-signature`** |

These are flags on the existing commands, not new operations. None takes a caller-supplied value.

### 2. Child environment additions (`SafeGitRunner.ConfineToRepository`)
| Variable | Value | Why |
|---|---|---|
| `GIT_NO_LAZY_FETCH` | `1` | Vector 9. A missing object becomes an error ("lazy fetching disabled"), never a call through the promisor remote. |
| `GIT_OPTIONAL_LOCKS` | `0` | Vectors 7 and 8 for status: status does not take the index lock or rewrite `.git/index`. |

### 3. Command-scope config additions (the `GIT_CONFIG_COUNT` list after ADR-005's `safe.bareRepository=explicit`)
| Key | Value | Why |
|---|---|---|
| `core.fsmonitor` | `false` | Vector 1. Command scope outranks the repository's config, and git passes `GIT_CONFIG_COUNT/KEY/VALUE` on to any child git (verified with a submodule). |
| `diff.autoRefreshIndex` | `false` | Vectors 7 and 8 for diff. `git diff` ignores `GIT_OPTIONAL_LOCKS`: `refresh_index_quietly()` in `builtin/diff.c` takes the index lock unconditionally. With this off, diff never rewrites `.git/index`, and stat-only changes still produce no output (verified). |

With §2 and §3 combined, the three tools never write `.git/index`. No `post-index-change` hook can fire, whether it is a file hook or a config-defined hook, on any git version. The tools also become truly read-only.

### 4. Refusal: repository filter drivers (`GitRepositoryGuard`)
ADR-005's guard already reads `.git/config` and `.git/config.worktree` as Latin-1. Gitfiles and `commondir` are refused, so these are the only repository-scope config files. The guard already refuses `\[\s*include`. It now also refuses `\[\s*filter`, case-insensitive: `'.git/<file>' defines a filter driver ([filter] section), which makes git run a program on file contents.`

Why the pattern is a sound superset: every spelling git **accepts** for a filter section contains `[filter`. That includes `[filter "x"]`, `[FiLtEr "x"]`, the legacy `[filter.x]`, a header after another header on the same line (`[core][filter "x"]clean=…`), a UTF-8 BOM, and CRLF line endings. Spellings without it, such as a header or a key split across lines, are **rejected** by git with `fatal: bad config line` before anything runs. The same goes for `[ filter "x"]`, which the pattern also matches. A false positive (for example `[filter` inside a value) refuses a harmless repository. That is the safe direction. `partialclonefilter = …` does not match.

Only filters need a refusal. Every other config-selected program has a flag or command-scope override (§1–§3), and filters have neither.

## Alternatives considered (and why rejected)
- **`--attr-source` / `GIT_ATTR_SOURCE` = empty tree, so no attribute selects a driver.** It stops worktree `.gitattributes`, but the harness showed three bypasses on both versions: `.git/info/attributes`, `core.attributesFile` from repository config, and `refs/replace/4b825dc6…` replacing the empty tree with a tree that has a malicious `.gitattributes`. The empty-tree id also depends on the hash algorithm. In a SHA-256 repository the SHA-1 id is resolved as a ref name, and the repository controls refs. Verified on 2.55: a branch named `4b825dc6…` carrying a malicious `.gitattributes` made the filter run. Finally, it drops `text`/`eol`/`filter=lfs` for every repository, so stat-dirty LFS or eol-normalised files would be hashed without their filters and show as modified.
- **`attr.tree=<unresolvable>`.** The documentation says an unresolvable value means an empty tree. In 2.55's `attr.c` (`compute_default_attr_source`) it means *no* attribute source, so git falls back to worktree `.gitattributes`. The harness confirmed this: the filter ran. This fails open.
- **`core.hooksPath=<empty location>`.** It does not stop config-defined hooks on 2.54+ (verified). There is also no obvious portable "empty location". An empty value makes git look for `/<hook>` at the filesystem root: verified on 2.55, where `-c core.hooksPath=` ran `/post-index-change`. Avoiding index writes removes the trigger instead.
- **`hook.post-index-change.enabled=false`.** Works on 2.55 (verified), but it does not exist in 2.54, which already has config-defined hooks (`hook.c` at `v2.54.0` has no event-level switch). It is redundant once nothing writes the index.
- **`protocol.allow=never`.** Blocks lazy fetch, but a repository-scope `protocol.file.allow=always` overrides it, because the per-protocol key wins regardless of scope (verified).
- **Scanning config for every dangerous key.** Every key other than filters already has a stronger override. Scanning for all of them would grow the guard's pattern list with nothing gained.
- **OS-level sandboxing of the git process (Windows job objects).** It would work regardless of git version, but it is Windows-specific and fragile with Git for Windows' launcher process, and it cannot be exercised by this repository's tests on other platforms. Keep it as a possible future layer.

## Consequences

### Positive
- No program named by repository content runs during `git.status`, `git.diff` or `git.log`. Program execution is limited to git itself, plus what the trusted global and system config chooses.
- The read-only tools never rewrite `.git/index`.
- `IFilesystemPolicy` semantics, tool schemas, the public API of `SafeGitRunner` and the set of git operations are unchanged.

### Negative (behaviour changes, all deliberate)
- **Diff:** no textconv and no external diff tool, so `git.diff` always shows git's own patch format.
- **Log:** no signature verification output.
- **Partial clones:** an operation that needs a missing object fails with "lazy fetching disabled" instead of fetching it.
- **Refused repositories:** repositories whose own `.git/config` or `.git/config.worktree` defines a filter driver, for example after `git-crypt unlock` or `git lfs install --local`. Git LFS installed the default way, through global config, is unaffected (test `Filter_attribute_without_a_repository_driver_is_allowed`).
- Refusal messages gain `filter` as part of the test contract.

## Residual risks
1. **Git older than the lazy-fetch fix.** On upstream git before 2.45.0 without the May 2024 backports (for example 2.40.0–2.40.1 or 2.44.0), `GIT_NO_LAZY_FETCH` is ignored and vector 9 stays open. Detecting that would take an extra `git --version` call, which the constraints exclude. `Lazy_fetch_from_promisor_remote_does_not_run` fails on such a git, which is intended. Every other mitigation works on git ≥ 2.16 (`core.fsmonitor` was added in 2.16.0).
2. **Trusted global/system config.** A filter, textconv or fsmonitor the user defined there still runs. This is by design.
3. **Concurrent writers.** The guard reads config before git starts. A `[filter]` section written between the check and process start is honoured, the same class as ADR-005 residual 3.
4. **Forks and future git.** Forks such as microsoft/git add execution paths (`core.virtualFilesystem`) that are not covered here. A future git release could add a new one to status, diff or log. Re-run the combined check (below) when moving to a new git version.
5. **CPU-only abuse.** `diff.<drv>.xfuncname` regexes and very large repositories can still cost CPU time. That is bounded by the caller's cancellation token, not by this ADR.

## Affected components
- `src/HAMMOR.Core/Tools/Git/SafeGitRunner.cs`: diff and log flags, `GIT_NO_LAZY_FETCH`, `GIT_OPTIONAL_LOCKS`, command-scope config list
- `src/HAMMOR.Core/Tools/Git/GitRepositoryGuard.cs`: `[filter` refusal
- `tests/HAMMOR.Core.Tests/Tools/Git/GitConfigExecutionTests.cs` (new, collection `Git process`)

## Verification
All new tests use real git in temp directories. Each vector test first runs the **plain** pre-ADR-006 command with ordinary git and asserts the payload ran. That way a payload that cannot run on the test machine fails the test instead of letting it pass. The test then asserts that the tool call created no marker file.

| Test | Covers |
|---|---|
| `Core_fsmonitor_hook_does_not_run` ×2 (status, diff) | vector 1 |
| `Diff_external_does_not_run` ×2 (unstaged, staged) | vector 2 |
| `Diff_driver_programs_selected_by_attributes_do_not_run` ×4 (command/textconv × unstaged/staged) | vectors 3, 4 |
| `Repository_filter_driver_is_refused` ×3, `Repository_filter_driver_variants_are_refused` ×4, `Filter_driver_in_config_worktree_is_refused` | vector 5 |
| `Log_signature_verification_program_does_not_run` ×2 (openpgp, ssh) | vector 6 |
| `Post_index_change_hook_does_not_run` ×2 | vector 7 |
| `Config_defined_post_index_change_hook_does_not_run` ×2 (returns early on git < 2.54, where the vector does not exist) | vector 8 |
| `Lazy_fetch_from_promisor_remote_does_not_run` (also: partial-clone config is not refused) | vector 9 |
| `Filter_attribute_without_a_repository_driver_is_allowed`, `Partial_clone_filter_setting_is_not_mistaken_for_a_filter_driver` | no over-refusal |
| `Tools_still_report_changes_and_never_rewrite_the_index` | status, diff, diff --staged and log still report real changes; a stat-only change is not reported; `.git/index` bytes unchanged |

**Results:** on Windows at `f57c4df`, `dotnet build -c Release` gave 0 errors and 2 pre-existing warnings (CS0067 in `FilesystemPolicyFactory.cs`, xUnit1031 in `TaskStoreGrantTests.cs`), and `dotnet test -c Release` passed 325/325, with 0 failed and 0 skipped. On Linux with git 2.43.0 and with git 2.55.0, 325/325 passed on each.

**Before the fix**, 20 of the original 24 failed for the expected reason. **Mutation check (git 2.55):** each mitigation was removed on its own and the suite re-run. Every removal turned at least one test red: `core.fsmonitor=false` → 2, `diff.autoRefreshIndex=false` → 3, `GIT_OPTIONAL_LOCKS=0` → 3, `GIT_NO_LAZY_FETCH=1` → 1, `--no-ext-diff` → 3, `--no-textconv` (unstaged / staged) → 1 / 1, `--no-show-signature` → 2, `[filter` refusal → 8.

The exploratory shell harness (per-vector marker checks plus the combined `GIT_TRACE` check) is not part of the repository. Its results are recorded in **Vectors** above.
