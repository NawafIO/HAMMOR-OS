# ADR-005: Interactive Git Repository Confinement

## Status
Accepted and implemented. Verified on Windows with git 2.55.0.windows.5: `dotnet build -c Release` 0 errors, `dotnet test -c Release` 299/299 passed (255 existing + 44 new).

## Date
2026-10-06

## Context
`SafeGitRunner` (Phase 3) runs three fixed commands, `git status --porcelain=v1`, `git diff --no-color [--staged]` and `git log --oneline -n N --no-decorate`. It validates only `repositoryPath` through `IFilesystemPolicy`, then starts `git` with that directory as its working directory and HAMMOR's own environment. Git then decides for itself which repository, work tree and object store to read, based on the repository's layout and config. The filesystem policy never sees those locations.

Each redirection below was reproduced with real git before this change. The target was a repository outside the global allowed roots, and its file names, contents or commit subjects appeared in tool output.

| Vector | Effect before this ADR |
|---|---|
| `core.worktree` in `.git/config`: `[core]` on its own line, on the header line (`[CORE] WorkTree = x`), any case | `status` lists and `diff` shows files of another directory |
| `core.worktree` in `.git/config.worktree` (with `extensions.worktreeConfig`) | same |
| `.git/commondir` | refs, objects and config come from another git directory; `log` shows its commits |
| `.git` file (`gitdir: …`) | the whole repository is elsewhere |
| Parent-directory discovery: no `.git`, or an invalid `.git` directory that git skips | git uses a parent repository |
| `[include]` / `[includeIf]` (also `[core][include] path = x` on one line) | loads config from any file. An included `core.worktree` did not move the work tree in our tests (setup reads that key without includes), but every other included key applies. |

The following were found while probing and are handled the same way:

| Vector | Effect before this ADR |
|---|---|
| `.git/objects/info/alternates` | `log` reads objects from another repository |
| `.git` or an entry directly inside it is a junction or symlink | git reads through it |
| Repository directory itself shaped like a bare repository, with an invalid `.git` | git uses the directory as an implicit bare repository |
| Submodule gitlink whose checkout `.git` file points outside (+ `diff.submodule=log`) | `diff` prints the other repository's commit subjects; `status` runs a child git inside it |
| Inherited `GIT_*` variables in HAMMOR's environment (`GIT_ALTERNATE_OBJECT_DIRECTORIES`, `GIT_OBJECT_DIRECTORY`, `GIT_INDEX_FILE`, `GIT_CONFIG_PARAMETERS`, …) | redirect objects, index or config for every call |

ADR-004 §2.5 specifies a `.git`-must-be-a-directory rule for **unattended** runs only and says "Interactive Git behaviour is unchanged". That rule is not implemented yet: no `TaskPathScope` exists on any branch. This ADR supersedes that sentence: interactive calls are confined too, and Phase 6a should call the guard below instead of writing its own rule.

While testing, a pre-existing deadlock was found in `SafeGitRunner.ReadBoundedAsync`. It looped on `reader.EndOfStream`, which blocks synchronously, so the stderr reader did not start until stdout ended. Git blocks once the 4 KiB stderr pipe is full. `git diff` in a directory that is not a repository prints about 7 KiB of usage text with no stdout, so `git.diff` on any allowed non-repository directory hung forever.

## Decision

The confinement uses both mechanisms. Refusal covers what cannot be neutralised. Neutralisation covers what refusal could miss or what changes after the check.

### 1. Refuse: `GitRepositoryGuard` (Core, internal)
`SafeGitRunner` calls `GitRepositoryGuard.FindProblem(root)` after the policy check and the directory-exists check, before `Process.Start`. The root is the policy's `NormalizedPath`. The call is refused with `Repository '<root>' is not allowed: <reason>` when:

1. `<root>\.git` does not exist (`parent-directory discovery is not allowed`).
2. `.git` is a reparse point.
3. `.git` is a file (`gitdir indirection`). This refuses linked worktrees and submodule checkouts.
4. Any entry directly inside `.git` is a reparse point.
5. `.git/commondir` exists.
6. `.git/objects/info/alternates` exists.
7. `.git/config` or `.git/config.worktree` matches `\[\s*include` (any include or includeIf header, anywhere on a line) or `\bworktree\s*=` (`core.worktree` in either form), case-insensitive. The file is also refused if it is larger than 1 MiB or cannot be read.

The config checks scan raw bytes as Latin-1, as git parses them, and are deliberate supersets. A comment or value containing `[include` or `worktree =` is refused. That false positive is accepted; a missed redirection is not.

### 2. Neutralise: child environment (`SafeGitRunner.ConfineToRepository`)
1. Remove every inherited variable whose name starts with `GIT_`.
2. `GIT_CEILING_DIRECTORIES=<parent of root>`: discovery may look at the root but never climbs into its parent.
3. `GIT_WORK_TREE=<root>`: overrides `core.worktree` from every config source. Verified for `config`, `config.worktree` and the header-line form.
4. `GIT_COMMON_DIR=<root>\.git`: overrides `commondir`.
5. `GIT_CONFIG_COUNT=1`, `GIT_CONFIG_KEY_0=safe.bareRepository`, `GIT_CONFIG_VALUE_0=explicit`: the root itself is never used as an implicit bare repository. This is command-scope (protected) config, so repository config cannot override it.

These settings still hold if `core.worktree`, `commondir` or a broken `.git` appears between the guard and process start.

### 3. `GIT_DIR` is deliberately not set
With an explicit `GIT_DIR`, git skips its `safe.directory` ownership check. Measured with `GIT_TEST_ASSUME_DIFFERENT_OWNER=1`: plain git refused the repository, explicit `GIT_DIR` accepted it, and the bounded discovery above refused it. Bounded discovery keeps git's protection against repositories created by another user. The user's own `safe.directory` exceptions in global config keep working.

### 4. Submodules
`status` and `diff` gain `--ignore-submodules=all`. This flag only narrows behaviour. No git operation is added, `log` is unchanged, and callers still cannot pass flags.

### 5. Bounded reader
`ReadBoundedAsync` loops on `await reader.ReadAsync(...)` until it returns 0. It no longer uses `EndOfStream`, so stdout and stderr are drained concurrently.

### 6. Options evaluated
| Option | Outcome |
|---|---|
| `GIT_DIR` + `GIT_WORK_TREE` (+ `GIT_CEILING_DIRECTORIES`) | Rejected as the primary mechanism. It skips the ownership check (§3). The ceiling is a no-op while `GIT_DIR` is set. `GIT_DIR` pointing at a `.git` file still follows it. |
| `-c core.worktree=…` arguments | Not needed: `GIT_WORK_TREE` already wins over every config source. `-c` can only set keys, so it cannot disable includes, `commondir`, gitfiles, alternates or discovery. |
| Reuse `TaskPathScope` checks | Not possible: not implemented. Reversed: Phase 6a should reuse `GitRepositoryGuard`. |
| Refusal only | Fragile: it depends on parsing config exactly as git does, and it is open to changes after the check. |
| Neutralisation only | Insufficient: no environment variable or flag disables includes, alternates or gitfile following. |

## Consequences

### Positive
- Interactive `git.status`, `git.diff` and `git.log` read only the validated directory and its own `.git` directory, for every vector listed in Context.
- `IFilesystemPolicy` semantics, tool schemas, the public API of `SafeGitRunner` and the set of git operations are unchanged.
- One guard serves both interactive and (future) unattended calls.
- `git.diff` on a directory whose git invocation produces large stderr no longer hangs.

### Negative (behaviour changes)
- `repositoryPath` must be the repository root with a real `.git` directory. Subdirectories of a repository, bare repositories, linked worktrees (`git worktree add`), submodule checkouts and repositories using alternates (`clone --shared` / `--reference`) are refused.
- Config containing `[include` or `worktree =` anywhere is refused, including in comments and values.
- `status` and `diff` no longer report submodule changes.
- Inherited `GIT_*` variables are ignored for these tools. That includes `GIT_CONFIG_GLOBAL` and `GIT_TRACE*`; git falls back to its default global config location.
- Refusal messages become part of the test contract: `gitdir`, `parent-directory discovery`, `commondir`, `alternates`, `include`, `worktree`, `config.worktree`, `reparse`.

### Residual risks (accepted here, not mitigated)
1. **Repository config still controls everything except location.** With this change in place, `core.fsmonitor=<cmd>` still runs on `git status` and `diff.external=<cmd>` still runs on `git diff`; both were verified with marker files. Textconv and filter drivers, `log.showSignature` with `gpg.program`, and partial-clone lazy fetch are in the same class. This is command execution, not redirection. It needs its own decision (follow-up 1). Config can also name files outside the root (`core.excludesFile`, `core.attributesFile`, `diff.orderFile`); git reads them but does not print them.
2. Reparse points deeper inside `.git` (for example `.git/objects/ab`, `.git/refs/heads`) are not checked. This is the same class as alternates.
3. An include or alternates file added between the guard and process start is honoured. This requires local write access and precise timing, the same as ADR-004 §2.6.

## Affected components
- `src/HAMMOR.Core/Tools/Git/GitRepositoryGuard.cs` (new)
- `src/HAMMOR.Core/Tools/Git/SafeGitRunner.cs`: guard call, environment, submodule flag, reader fix
- `tests/HAMMOR.Core.Tests/Tools/Git/GitRepositoryConfinementTests.cs` (new)
- `tests/HAMMOR.Core.Tests/Tools/Git/GitToolsSecurityTests.cs`: shared `Git process` collection, deadlock test

## Verification
All tests use real git in temp directories. The redirection target is always a repository outside the allowed roots that contains `OUTSIDE_*` markers, and every test asserts that no marker reaches tool output or error text. Each new test was run before the fix and failed for the expected reason (a leak or an unexpected success).

| Test | Covers |
|---|---|
| `Dot_git_file_is_refused` ×3 tools | `.git` file |
| `Parent_repository_discovery_is_refused` ×3 | no `.git`, parent repository |
| `Invalid_dot_git_directory_does_not_fall_through_to_parent_repository` ×3 | ceiling |
| `Repository_directory_is_not_used_as_an_implicit_bare_repository` ×3 | `safe.bareRepository` |
| `Core_worktree_is_refused` ×3, `Core_worktree_header_line_and_case_variants_are_refused` ×3 | `core.worktree`, both forms |
| `Config_include_is_refused` ×3, `Config_include_variants_are_refused` ×4 | include, includeIf, `[core][include]` |
| `Commondir_is_refused` ×3 | `commondir` |
| `Config_worktree_setting_core_worktree_is_refused` ×3, `Config_worktree_include_is_refused` | `config.worktree` |
| `Object_alternates_are_refused` ×3 | alternates |
| `Dot_git_junction_is_refused` ×3, `Junction_directly_inside_dot_git_is_refused` | reparse points |
| `Submodule_gitfile_pointing_outside_is_not_followed` | `--ignore-submodules=all` |
| `Inherited_git_environment_variables_are_not_passed_to_git` | `GIT_*` scrub |
| `Ordinary_repository_still_works_for_all_tools`, `Harmless_config_worktree_file_is_allowed` | regressions |
| `Large_stderr_with_empty_stdout_does_not_deadlock` | reader fix (timed out at 30 s with the old loop) |

## Follow-ups
1. Decide how to neutralise config-driven command execution (residual 1). Candidates: `--no-ext-diff --no-textconv` on `diff`, `--no-show-signature` on `log`, command-scope overrides such as `core.fsmonitor=false`, `GIT_NO_LAZY_FETCH=1`, and attribute-source control for filter drivers.
2. Phase 6a: `TaskPathScope` calls `GitRepositoryGuard.FindProblem`. Its ADR-004 §2.5 rule is a subset of the guard. Add a note to ADR-004 when that branch merges.
3. Optional: follow gitdir and commondir indirection only when every hop passes `IFilesystemPolicy`, so worktrees and submodule checkouts inside the allowed roots can be inspected.
