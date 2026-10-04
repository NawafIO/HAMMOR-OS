# ADR-002: Filesystem Security Boundary

## Status
Accepted

## Date
2026-10-05

## Context
Phase 2 introduced four filesystem tools (`filesystem.list_directory`, `filesystem.read_file`, `filesystem.write_file`, `filesystem.delete_file`). Without a centralized policy the tools operated on the full process-accessible filesystem, with no guard against traversal, protected locations, credential storage, or reparse escapes. Existing tests covered functional behavior but not security invariants.

## Decision
Introduce a centralized `IFilesystemPolicy` consulted by all four tools, with a Windows-specific path resolution abstraction.

### Approved Roots
- `HammorPaths.DataRoot` is always allowed (per-user `%LocalAppData%\HAMMOR`, no hard-coded machine paths).
- `MemorySettings.RootPath` when configured is also allowed.
- `SecuritySettings.FilesystemAllowedRoots` — optional explicit allow-list from configuration (portable, user-managed).

All other paths are denied by default.

### Policy Rejections
| Class | Signal | Result |
|-------|--------|--------|
| Blank/invalid/overlong | validation | `ToolValidationResult.Invalid` or typed `ToolResult.Failure` |
| Traversal (`..` segment) | raw split before normalization | typed `Failure` ("traversal") |
| Normalization escape | `GetFullPath` outside roots | typed `Failure` ("outside the allowed roots") |
| Protected Windows locations | `%WINDIR%`, `Program Files`, `Program Files (x86)` | typed `Failure` ("protected"/"Program Files") |
| HAMMOR secrets | `HammorPaths.SecretsDirectory` + children | typed `Failure` ("secret") |
| Reparse/symlink escape | `IPathResolution.IsReparsePoint` + `ResolveFinalPath` outside roots or unresolvable | typed `Failure` ("reparse") |
| Reparse chain ancestor | walk ancestors under policy | typed `Failure` ("reparse") |

All rejections flow through the existing `ToolResult.Failure` system — no exceptions or silent behavior.

### Architecture
- `IFilesystemPolicy` + `IPathResolution` live in `HAMMOR.Core/Tools/Filesystem` — Core stays `net8.0` with no WPF.
- `FilesystemPolicy` — production policy (normalization, root checks, protected checks, reparse handling).
- `ManagedPathResolution` — portable fallback (no P/Invoke) used in Infrastructure/tests.
- `WindowsPathResolution` — `net8.0-windows` implementation using `File.GetAttributes` + `GetFinalPathNameByHandleW` (under `HAMMOR.Platform.Windows/Filesystem`).
- `FilesystemPolicyFactory` — test helper to build a policy scoped to a temporary root.
- `SecuritySettings.FilesystemAllowedRoots` — `List<string>` with deep `Clone()`.
- DI: `InfrastructureServiceCollectionExtensions` builds tools with `IFilesystemPolicy` (managed fallback when Windows layer absent); `WindowsPlatformServiceCollectionExtensions.AddHammorWindowsPlatform()` registers `WindowsPathResolution` + `FilesystemPolicy` to override.

### Tools
All four tools gain an optional `IFilesystemPolicy? policy` constructor parameter (null = no gate, for backward compatibility/plaine tests). When present, `Validate(rawPath)` is consulted before any OS call and failures are returned as `ToolResult.Failure`.

## Consequences

### Positive
- One enforcement point — no per-tool policy drift.
- Typed failures integrate with existing audit/logging (`AgentPipeline` redaction path unchanged).
- Portable root model — no machine-specific paths.
- Protected locations and credential storage are unavailable through filesystem tools.

### Negative
- Exact policy message strings become part of the contract (tests key on `"outside the allowed roots"`, `"traversal"`, `"secret"`, `"protected"`/`"Program Files"`, `"reparse"`).
- `GetFinalPathNameByHandleW` requires Windows; non-Windows hosts use the managed fallback which cannot follow reparse chains deeply.

### Alternatives Considered
- **Per-tool guard**: rejected — duplicates logic and invites drift.
- **Hard-coded allow-list in code**: rejected — unportable, not user-configurable.

## Verification
- `dotnet build -c Release` — 0 warnings/errors.
- `dotnet test -c Release` — 132 passed (96 + 28 new security + 4 additional policy-proves-it coverage + 4 existing tests now policy-aware).

## Follow-ups
- UI for `FilesystemAllowedRoots` editing was intentionally deferred — configuration is file-editable now; Settings page integration is next.
