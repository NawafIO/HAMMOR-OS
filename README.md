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

Phase 5 is an **engine-only milestone**: the runner, scheduler, grant model and
storage exist in Core/Infrastructure and are tested, but the app does not start
the scheduler and has no UI to create grants, so no task runs unattended in the
app yet. Several advertised capabilities are deliberately
*not* implemented yet and are labelled as such in the UI — see
[Current limitations](#current-limitations).

---

## Requirements

| | |
|---|---|
| OS | Windows 11 (Windows 10 1809+ should also work) |
| SDK | .NET **8** SDK (the repo pins `8.0.130` via `global.json`) |
| Accounts | Anthropic API key (required for chat) · ElevenLabs API key (optional, for speech) |

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
| **Speech-to-text** | **Not implemented.** No recogniser is wired up; `UnavailableSpeechToTextProvider` reports itself unavailable and the chat page says so. Voice *output* works. |
| **TTS streaming** | Not implemented. The ElevenLabs provider buffers the full response before playback and reports `SupportsStreaming = false`. |
| **Semantic memory** | **Not implemented.** The abstraction exists; search is keyword-based and the Memory page states this. |
| **Task scheduling / retry** | **Engine implemented, not wired into the app.** `TaskRunner` and `TaskSchedulerService` run granted tasks one at a time with Read-only, expiring, immutable grants, retry/backoff, cancellation and audit (ADR-003). The engine also has path-scoped grants (filesystem, Git and project tools limited to approved folders), a grant-authoring service that requires explicit approval and audits every grant, resume and cancel, and a retry limit of 10 (ADR-004 Phase 6a). Deferred to Phase 6b: starting the scheduler from the app, the grant-creation and approval UI, and Blocked-task display on the Tasks page. Until then nothing runs tasks automatically in the app. |
| **Projects** | Store and model exist and scope memory/tasks. Read-only Git and project inspection exist as tools (status, diff, log, project inspect); Git tools only accept a repository root with its own `.git` directory and refuse layouts or config that would redirect git elsewhere ([ADR-005](docs/adr/ADR-005-interactive-git-repository-confinement.md)), and repository config cannot make them run a program; repositories whose own config defines a filter driver are refused ([ADR-006](docs/adr/ADR-006-git-repository-config-execution.md)); the project creation UI is not built. |
| **Tray / global hotkey / overlay** | **Not implemented.** The settings toggles are present but disabled. |
| **Mobile gateway** | Not built. Core is kept platform-neutral so it can be hosted later. |

## Documentation

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — layering, agent loop, providers,
  voice pipeline, tools and permissions, memory, tasks, engineering decisions.
