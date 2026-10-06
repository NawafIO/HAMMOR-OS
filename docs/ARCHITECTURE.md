# HAMMOR architecture

## Layering

Five projects. The dependency direction only ever points inward toward Core.

```
          ┌──────────────────────────────────────────┐
          │  HAMMOR.App            net8.0-windows    │  WPF + WPF-UI
          │  views, view models, localisation, DI    │  presentation only
          └───────────┬─────────────────┬────────────┘
                      │                 │
      ┌───────────────▼──────┐  ┌───────▼─────────────────────┐
      │ HAMMOR.Infrastructure│  │ HAMMOR.Platform.Windows     │
      │ net8.0               │  │ net8.0-windows              │
      │ Claude, ElevenLabs,  │  │ DPAPI secrets, WASAPI audio │
      │ SQLite, JSON config, │  │                             │
      │ markdown memory      │  │                             │
      └───────────────┬──────┘  └───────┬─────────────────────┘
                      │                 │
                ┌─────▼─────────────────▼─────┐
                │ HAMMOR.Core      net8.0     │  no WPF, no Windows APIs
                │ abstractions + domain +     │
                │ agent loop + permissions    │
                └─────────────────────────────┘

                  tests/HAMMOR.Core.Tests  net8.0
                  tests/HAMMOR.App.Tests   net8.0-windows  (Living Core model)
```

**Core owns** AI provider contracts, intent/routing/planning, the permission
engine, the tool registry, memory, tasks, projects, audit, configuration, voice
orchestration and error types. It targets plain `net8.0` and references no UI
assembly, so a future mobile client can reach the same Core through a gateway
without dragging WPF along. That constraint is structural, not a convention:
`HAMMOR.Core.csproj` simply cannot see WPF.

**Infrastructure** supplies concrete adapters. **Platform.Windows** holds the
Windows-only pieces that still are not UI (DPAPI, WASAPI). **App** is
presentation: it contains no AI, tool, memory or task logic.

`HammorPaths` lives in Core because both Infrastructure and Platform.Windows
need it; putting it in Infrastructure would have forced a sideways reference.

---

## Agent loop

`AgentPipeline.RunAsync` implements the full loop and reports which stage it
reached, so a failure is attributable rather than generic.

```
SENSE → UNDERSTAND → ROUTE → PLAN → AUTHORIZE → EXECUTE → VERIFY → REMEMBER → RESPOND
```

| Stage | What happens |
|---|---|
| SENSE | Input arrives (typed; voice input is not implemented) |
| UNDERSTAND | Project context is loaded |
| ROUTE | Provider selected by configured id, then probed for availability |
| PLAN | Request assembled: history, system prompt, language, model, effort |
| AUTHORIZE | Nothing privileged in a pure chat turn. Tool calls go through `InvokeToolAsync`, which **cannot** bypass the permission engine |
| EXECUTE | Provider call |
| VERIFY | Refusal and empty-response checks before the text is presented as an answer |
| REMEMBER | Exchange persisted to memory |
| RESPOND | Reply returned, with truncation noted if the token ceiling was hit |

A failure returns `AgentTurnResult.Failed(stage, error)`; the chat UI renders it
as an error bubble prefixed with the stage, e.g.
`[Route] Claude is not available: No Anthropic API key is stored.`

---

## AI providers

```csharp
public interface IAiProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    Task<ProviderAvailability> CheckAvailabilityAsync(CancellationToken ct = default);
    Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct = default);
}
```

Providers are registered as a **collection**; the pipeline picks one by matching
`ProviderId` against `ai.primaryProvider`. Adding a provider is a registration,
not a change to the loop. No fallback to an arbitrary provider happens — routing
to a backend the user did not choose would be surprising.

`ClaudeAiProvider` uses the official **Anthropic C# SDK** (`Anthropic` 12.53.0).
Notes:

- The API key is read from `ISecretStore` **per call**, not cached in a field,
  so rotating it in Settings takes effect immediately.
- `CheckAvailabilityAsync` performs a real `models.list` call. Checking only
  that *a key exists* would report "Ready" for a revoked credential.
- `AiResponse` surfaces `StopReason` verbatim, so `IsRefusal` and `IsTruncated`
  are checked before the text is treated as an answer.
- Model and effort come from configuration, never from code.

---

## Voice pipeline

Target design:

```
user speaks → STT → HAMMOR → Claude → HAMMOR → ElevenLabs TTS → speaker
```

Built in Phase 1:

```
                      HAMMOR → Claude → HAMMOR → ElevenLabs TTS → speaker
                                                 ▲ real HTTP        ▲ WASAPI
  (STT not implemented) ─────────────────────────┘
```

`IVoiceOrchestrator` owns the speak half: resolve settings → synthesise → play.
It returns `false` rather than throwing when TTS is unconfigured, so voice is
genuinely optional. The capture half is deliberately absent — a half-wired
microphone path would imply voice input works.

`ElevenLabsTextToSpeechProvider` performs real synthesis against
`POST /v1/text-to-speech/{voice_id}`, passing a language hint so a multilingual
voice pronounces mixed Arabic/English correctly. It reports
`SupportsStreaming = false` because it buffers the whole response; claiming
otherwise would misstate the pipeline.

`UnavailableSpeechToTextProvider` implements `ISpeechToTextProvider` and always
reports `NotConfigured`. `TranscribeAsync` throws `NotSupportedException` — the
UI is expected to check availability and disable voice input, not to catch it.

---

## Tools and permissions

```csharp
public enum ToolPermission { Read = 0, Write = 1, Execute = 2, Destructive = 3 }
```

Ordered least→most dangerous so the engine can compare against a configured
ceiling with `<=`.

Every tool declares a name, description (written to state *when* to call it),
JSON input schema, permission level, validation and an execution handler, and
every authorisation plus outcome is written to the audit trail.

### The enforcement rules

`PermissionEvaluator` applies, in order:

1. **Validation first.** Invalid arguments are denied *before* the user is
   prompted — nobody should approve a call that cannot run.
2. **A hard destructive floor.** Destructive tools always require confirmation.
   This is checked *before* the configured ceiling, so setting
   `autoApproveUpTo: Destructive` cannot silence destructive prompts. The
   Settings UI does not even offer Destructive as an auto-approve option.
3. **The configured ceiling.** At or below → allowed; above → confirmation.

Anything other than an explicit Allow — Deny, Escape, closing the dialog, or no
UI thread at all — resolves to **false**. A dismissed prompt is never consent.

> There is deliberately **no** `run_any_command` tool. An arbitrary-execution
> primitive would collapse the permission model, since a single Execute grant
> would also cover every destructive action. A test asserts no such tool is
> registered.

Phase 1 ships two real tools: `memory.save` (Write) and `memory.search` (Read).

---

## Memory

- **SQLite** is authoritative (`memory` table, indexed on created/project).
- **Markdown** is a readable, portable mirror at `memory\yyyy-MM\<slug>-<id>.md`
  with YAML front-matter. It is explicitly **not** a security boundary and
  nothing in HAMMOR executes content read from it.
- `MarkdownMirroringMemoryStore` composes the two, so SQLite persistence stays
  independently testable and the mirror can be swapped or disabled.
- **Semantic search** is an abstraction only (`ISemanticMemoryIndex`,
  `IsAvailable = false`). It returns an empty list rather than silently
  degrading to keyword matching.

### Incremental indexing

`MarkdownMemoryIndexer` never rebuilds the whole index. Two gates:

1. Unchanged **size + mtime** → skip the file entirely; no read, no hash.
2. mtime moved but **content hash** identical → record the new stat, do no work.

Only a genuine content change counts as an update. Removed files drop out. This
keeps startup cost flat as memory grows.

---

## Tasks

States: `Pending · Running · Completed · Failed · Cancelled`.

Tasks are durable, carry attempt counts, an optional `ScheduledForUtc`, and a
terminal-state flag. **Crash recovery is real**: `ReconcileInterruptedAsync`
runs at startup and marks anything still `Running` as `Failed` — a Running row
at launch means the previous process died, and leaving it Running would make
the UI claim work is in flight when nothing is executing.

Background execution, scheduling and retry are **not implemented**. The Tasks
page says so rather than implying a scheduler exists.

---

## Localisation

The hard requirement is switching language **without restarting**. That rules
out `x:Static` against generated resx classes, which resolve once at load.

- `ILocalizationService` exposes a **bindable indexer** and raises change
  notification on switch.
- `LocalizationSource` is a singleton binding bridge (a markup extension cannot
  resolve DI services). Before attachment it echoes the key back, so the XAML
  designer renders instead of throwing.
- `{loc:Loc Some.Key}` returns a live `Binding`, not a resolved string. When the
  source raises `Item[]` + `PropertyChanged(string.Empty)`, every bound string
  re-reads in place.
- Flow direction is bound too, so Arabic mirrors the whole shell to RTL.

**One subtlety worth knowing.** Text produced by a *value converter* does not
refresh on its own: those bindings are rooted at an enum or bool that did not
change, so WPF has no reason to re-run the converter — leaving stale text from
the old language beside freshly-translated text. Two fixes are in place:

- the shell **re-navigates the active page** on language change, rebuilding its
  visual tree so every binding and converter re-evaluates (pages are transient
  in DI, view models are singletons, so state survives);
- the status bar lives outside the navigation host, so `ShellViewModel`
  re-raises `Status` to re-run its converter.

Missing keys render as `!Key!` so an untranslated string is visible during
development rather than silently blank.

---

## Security summary

| Requirement | How |
|---|---|
| Secrets at rest | Windows DPAPI, `CurrentUser` scope, app-specific entropy |
| Secrets in config | Never — the config model has no credential field; a test walks the written JSON to keep it that way |
| Secrets in logs | `SecretRedactor` on every audit write and provider error path |
| Secrets in git | `.gitignore` committed before any config file existed |
| Least privilege | Auto-approve defaults to `Read` |
| Destructive actions | Always confirmed; the floor is not configurable |
| Input validation | Per-tool `Validate`, run before authorisation |
| Path traversal | `DpapiSecretStore` whitelists secret-name characters |
| SQL injection | Every query parameterised, including the `IN (...)` state filter |
| Audit trail | Authorisations, tool outcomes, provider failures |
| Error visibility | No empty catch blocks; expected failures become typed results, unexpected ones are logged with the stack trace and surfaced |

---

## Engineering decisions

**Five projects rather than two.** The brief asked for Core vs Windows UI.
Splitting Infrastructure and Platform.Windows out as well keeps Core free of
*any* concrete dependency and isolates the Windows-only, non-UI services. The
cost is two extra projects; the benefit is that "Core must not depend on WPF" is
enforced by the compiler.

**`net8.0` / `net8.0-windows`.** Only the .NET 8 SDK is installed (9 and 10
runtimes exist, but no SDK). Two pins follow:

- `System.Text.Json` **10.0.6** — required by `Anthropic` 12.53.0; pinning lower
  produced `NU1605`/`NU1109` downgrade errors. It ships a `net8.0` target.
- `NAudio` **2.4.0** — 3.x targets `net9.0`+ only. 2.4.0 is the newest release
  with a `net6.0-windows7.0` target, which `net8.0-windows` can consume.

A `dotnet restore` gate was run before any implementation; it caught both.

**Official Anthropic SDK over hand-rolled HTTP.** Model IDs, parameter shapes
and error types come from the SDK rather than from memory.

**Memory mirroring as a decorator**, not merged into the SQLite store, so
persistence and the markdown mirror are independently testable and swappable.

**Event-driven status, no polling.** `ISystemStatusService` re-probes on demand
and raises an event; nothing runs on a timer, so HAMMOR is idle-cheap. The one
sleep loop (`NAudioPlayer`) only runs while audio is actually playing.

**Config written atomically** (temp file then move) so an interrupted write
cannot leave half-serialised settings. Malformed JSON falls back to defaults and
**preserves the bad file** for inspection instead of overwriting the user's
settings.

---

## Recommended next phase

1. **Model-driven tool calling** — pass the registry's schemas to Claude and run
   the returned `tool_use` blocks through `InvokeToolAsync`. The authorisation
   path already exists; this is the piece that makes HAMMOR agentic.
2. **STT** — implement `ISpeechToTextProvider` (Windows Speech or Whisper) and
   wire the capture half of the orchestrator, completing the voice loop.
3. **Task runner + scheduler** — a hosted service draining `Pending`, honouring
   `ScheduledForUtc`, `MaxAttempts` and cancellation.
4. **Tray, global hotkey, quick overlay** — the interfaces and settings exist.
5. **Projects UI + git inspection** — `ProjectGitInfo` is modelled already.
