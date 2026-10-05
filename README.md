# HAMMOR

A native Windows 11 personal AI assistant. HAMMOR is the assistant system and
agent runtime; Claude is the reasoning brain; ElevenLabs is the initial voice
provider. Each of those is behind an abstraction so it can be replaced without
rebuilding the application.

**Status: Phase 4 (released, tag `phase-4`).** The shell, localisation,
configuration, persistence, permission model and provider integrations are
built and working, along with native filesystem tools behind a security
boundary, read-only Git and project inspection, and model-driven tool calling
through the agent pipeline. Several advertised capabilities are deliberately
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

199 tests (all passing at the Phase 4 release) covering the permission
engine, tool registry, secret redaction, configuration round-trip, SQLite
persistence, crash reconciliation, incremental indexing, the filesystem
security boundary, the safe Git and project-inspection tools, and the
model-driven agent loop.

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
| **Task scheduling / retry** | **Not implemented.** Tasks persist and are reconciled after a crash, but nothing runs them automatically. |
| **Projects** | Store and model exist and scope memory/tasks. Read-only Git and project inspection exist as tools (status, diff, log, project inspect); the project creation UI is not built. |
| **Tray / global hotkey / overlay** | **Not implemented.** The settings toggles are present but disabled. |
| **Mobile gateway** | Not built. Core is kept platform-neutral so it can be hosted later. |

## Documentation

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — layering, agent loop, providers,
  voice pipeline, tools and permissions, memory, tasks, engineering decisions.
