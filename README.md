# HAMMOR

A native Windows 11 personal AI assistant. HAMMOR is the assistant system and
agent runtime; Claude is the reasoning brain; ElevenLabs is the initial voice
provider. Each of those is behind an abstraction so it can be replaced without
rebuilding the application.

**Status: Phase 5 (tag `phase-5`).** The shell, localisation,
configuration, persistence, permission model and provider integrations are
built and working, along with native filesystem tools behind a security
boundary, read-only Git and project inspection, model-driven tool calling
through the agent pipeline, and an unattended task runner/scheduler engine
(read-only, per-task grants — see
[ADR-003](docs/adr/ADR-003-task-runner-scheduler.md)).

Phase 6b wires that engine into the app: the scheduler starts after startup
housekeeping, and the Tasks page creates unattended tasks through an approval
dialog that shows the exact grant, displays Blocked tasks with their reason,
resumes them under a new grant, and cancels tasks
([ADR-004](docs/adr/ADR-004-task-runner-app-integration.md)). Phase 6b is
**pending Windows verification**. Several advertised capabilities are deliberately
*not* implemented yet and are labelled as such in the UI — see
[Current limitations](#current-limitations).

The Living Core P0 (the animated presence and the static V2 Lens mark) builds
on Windows and passes its tests (412/412). Three newer changes are **pending
Windows verification**:

- the Living Home, which turns the home screen into the approved canvas;
- the language-switch fix;
- the Claude Code account provider, which uses Claude without a required API
  key ([ADR-007](docs/adr/ADR-007-claude-code-account-provider.md)).

See [docs/LIVING-CORE.md](docs/LIVING-CORE.md).

---

## Requirements

| | |
|---|---|
| OS | Windows 11 (Windows 10 1809+ should also work) |
| SDK | .NET **8** SDK (the repo pins `8.0.130` via `global.json`) |
| Accounts | A Claude account with [Claude Code](https://code.claude.com/docs/en/setup) installed (recommended), or an Anthropic API key · ElevenLabs API key (optional, for speech) |

> The solution targets `net8.0` / `net8.0-windows` because the .NET 8 SDK is the
> only one installed on the development machine. Two package pins follow from
> that constraint — see [Engineering decisions](docs/ARCHITECTURE.md#engineering-decisions).

## Build

```powershell
dotnet restore
dotnet build HAMMOR.sln
```

## Run

```powershell
dotnet run --project src\HAMMOR.App
```

On first launch HAMMOR opens a setup wizard (language → Claude → voice →
storage → done). Every step after the language choice can be skipped and
configured later in **Settings**.

## Test

```powershell
dotnet test
```

255 tests (all passing at the Phase 5 release) covering the permission
engine, tool registry, secret redaction, configuration round-trip, SQLite
persistence, crash reconciliation, incremental indexing, the filesystem
security boundary, the safe Git and project-inspection tools, the
model-driven agent loop, and unattended task execution (grants, state
machine, blocking, retry, cancellation, audit, schema migration).

---

## Where your data lives

Everything is per-user under `%LOCALAPPDATA%\HAMMOR` — nothing is written into
the repository:

```
%LOCALAPPDATA%\HAMMOR\
├── hammor.config.json     settings (no secrets)
├── hammor.db              SQLite: tasks, memory, projects, audit
├── secrets\               DPAPI-encrypted API keys, one file per secret
├── memory\                human-readable markdown mirror (yyyy-MM\*.md)
└── logs\                  rolling daily logs, 14 retained
```

## Secrets

API keys are **never** placed in source, in `hammor.config.json`, or in logs.
They are encrypted with Windows DPAPI scoped to your user account
(`DataProtectionScope.CurrentUser`) and stored under `secrets\`. Anything
heading for a log or the audit table passes through `SecretRedactor` first.

`.gitignore` excludes the config file, the database, logs and any
`*.secret` / `.env` pattern. It was committed before any config file existed.

---

## Configuration

Edit in **Settings**, or directly in `hammor.config.json`.

| Section | Keys |
|---|---|
| `general` | `language` (`en` / `ar`), `theme`, startup behaviour |
| `ai` | `primaryProvider`, `model`, `maxTokens`, `effort` |
| `voice` | `ttsProvider`, `sttProvider`, `elevenLabs.*`, devices, volume |
| `memory` | `rootPath`, `indexOnStartup` |
| `security` | `autoApproveUpTo`, `alwaysConfirmDestructive` |

### ElevenLabs voice ID

The shipped voice is `G3YpdjT1OTh9cunaumJs`. It lives in configuration at
`voice.elevenLabs.voiceId` and is editable in **Settings → Voice**. There is no
voice-id literal anywhere in the TTS pipeline — `ElevenLabsTextToSpeechProvider`
always reads it from `IConfigurationStore`.

---

## Language

Arabic and English are both first-class. Switching language takes effect
immediately — no restart — from **Settings** or the quick toggle in the status
bar, and the choice persists across launches.

Arabic switches the whole shell to RTL (navigation moves to the right, text
right-aligns) and tells the model to reply in Arabic. Mixed Arabic/English
input is expected and handled: the model is asked to mirror whichever language
the user wrote in.

Adding a language means adding `Strings.<code>.resx` plus one entry in
`ResxLocalizationService.Languages` — no other code changes.

---

## Current limitations

These are real and visible in the UI rather than hidden:

| Area | State |
|---|---|
| **Claude via Claude Code** | **Text only, pending Windows verification.** HAMMOR runs your own `claude.exe` in print mode with every tool disabled and never sees your Claude credentials ([ADR-007](docs/adr/ADR-007-claude-code-account-provider.md)). HAMMOR's own tools (files, Git, projects) need the API-key provider. Only the native `claude.exe` is started, never the npm `claude.cmd`. |
| **Speech-to-text** | **Not implemented.** No recogniser is wired up; `UnavailableSpeechToTextProvider` reports itself unavailable and the chat page says so. Voice *output* works. |
| **TTS streaming** | Not implemented. The ElevenLabs provider buffers the full response before playback and reports `SupportsStreaming = false`. |
| **Semantic memory** | **Not implemented.** The abstraction exists; search is keyword-based and the Memory page states this. |
| **Task scheduling / retry** | **Implemented; app wiring pending Windows verification (Phase 6b).** `TaskRunner` and `TaskSchedulerService` run granted tasks one at a time with Read-only, expiring, immutable grants, retry/backoff, cancellation and audit (ADR-003), plus path-scoped grants and a grant-authoring service that requires explicit approval (ADR-004 Phase 6a). The app starts the scheduler after startup housekeeping. The Tasks page creates tasks, shows Blocked tasks with their reason, resumes them under a new grant and cancels them, updating live. Unattended tasks remain read-only, with no recurrence and no notifications beyond the Tasks page. |
| **Projects** | Store and model exist and scope memory/tasks. Read-only Git and project inspection exist as tools (status, diff, log, project inspect); Git tools only accept a repository root with its own `.git` directory and refuse layouts or config that would redirect git elsewhere ([ADR-005](docs/adr/ADR-005-interactive-git-repository-confinement.md)), and repository config cannot make them run a program; repositories whose own config defines a filter driver are refused ([ADR-006](docs/adr/ADR-006-git-repository-config-execution.md)); the project creation UI is not built. |
| **Tray / global hotkey / overlay** | **Not implemented.** The settings toggles are present but disabled. |
| **Mobile gateway** | Not built. Core is kept platform-neutral so it can be hosted later. |

## Documentation

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — layering, agent loop, providers,
  voice pipeline, tools and permissions, memory, tasks, engineering decisions.
- [docs/LIVING-CORE.md](docs/LIVING-CORE.md) — the Living Core, the Living
  Home and the V2 Lens mark: architecture, state mapping, budget, Windows
  checklist.
- [docs/LIVING-CORE-MOTION.md](docs/LIVING-CORE-MOTION.md) — Step 4: the
  Living Core's visible idle life, pointer attention, frame budget, Windows
  checklist.
- [docs/CHAT-PROJECTS-SEARCH.md](docs/CHAT-PROJECTS-SEARCH.md) — Step 3: chat,
  projects and global search, and what HAMMOR's data supports.
- [docs/SETTINGS.md](docs/SETTINGS.md) — Settings v2: categories, navigation,
  where every setting moved, Windows checklist.
- [docs/SHELL.md](docs/SHELL.md) — the shell and sidebar: patterns studied,
  metrics, behaviour, Windows checklist.
- [docs/adr/ADR-007-claude-code-account-provider.md](docs/adr/ADR-007-claude-code-account-provider.md)
  — Claude through your own Claude Code and Claude account.
