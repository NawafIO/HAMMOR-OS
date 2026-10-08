# HAMMOR

**A native Windows personal AI assistant, built for Arabic and English.**

HAMMOR is the assistant system and agent runtime. Claude does the reasoning,
through your own Claude account in Claude Code or through an Anthropic API
key. ElevenLabs speaks the replies. Each of these sits behind an abstraction,
so it can be replaced without changing the rest of the application.

HAMMOR keeps its data on your machine. Conversations, including any tool
results, go to the Claude provider you choose, and the text of spoken replies
goes to ElevenLabs. Every tool the model can use passes the same permission
engine and is written to an audit trail.

> **Status:** every feature described here is merged into `main` (pull
> request #4). The platform is verified on Windows through Phase 6a and the
> Living Core P0. Newer work is **pending Windows verification**, and the
> latest Living Core motion is **pending Windows visual verification**. See
> [Development status](#development-status).

## Contents

- [At a glance](#at-a-glance)
- [Development status](#development-status)
- [Features](#features)
- [Arabic and English](#arabic-and-english)
- [AI providers](#ai-providers)
- [The Living Core](#the-living-core)
- [Security model](#security-model)
- [Architecture](#architecture)
- [Getting started](#getting-started)
- [Configuration](#configuration)
- [Where your data lives](#where-your-data-lives)
- [Current limitations](#current-limitations)
- [Documentation](#documentation)

---

## At a glance

| | |
|---|---|
| **Platform** | Native WPF on .NET 8, for Windows 11 |
| **Reasoning** | Claude, through Claude Code and your Claude account (no API key), or through the Anthropic API |
| **Voice** | ElevenLabs text-to-speech; speech input is not built |
| **Languages** | Arabic and English, switched live, with a full right-to-left layout |
| **Tools** | Ten built-in tools: memory, filesystem, read-only Git and project inspection, all permission-gated |
| **Unattended work** | Read-only background tasks, run only under grants you approve |
| **Storage** | Local only: SQLite, a readable markdown memory mirror, and DPAPI-encrypted secrets |
| **Presence** | The Living Core, an animated identity that shows what HAMMOR is doing |

---

## Development status

Each milestone's Windows status, as recorded in its ADR or document. Commits
written without a Windows build are marked **UNVERIFIED** in their message.

| Milestone | What it delivers | Windows verification |
|---|---|---|
| **Phases 1–5** (tags `phase-1` to `phase-5`) | Solution layers, WPF shell, localisation, first-run setup, configuration, SQLite persistence, DPAPI secrets and the permission engine. Filesystem tools behind a security boundary ([ADR-002](docs/adr/ADR-002-filesystem-security-boundary.md)). Read-only Git and project inspection. Model-driven tool calling. The unattended task engine ([ADR-003](docs/adr/ADR-003-task-runner-scheduler.md)). | Verified: 255/255 tests at Phase 5 |
| **Git confinement** ([ADR-005](docs/adr/ADR-005-interactive-git-repository-confinement.md), [ADR-006](docs/adr/ADR-006-git-repository-config-execution.md)) | Git tools confined to the repository root; repository config cannot run programs | Verified: 299/299, then 325/325 |
| **Phase 6a** ([ADR-004](docs/adr/ADR-004-task-runner-app-integration.md)) | Path-scoped grants, grant authoring with approval, cancellation | Verified: 371/371 |
| **Phase 6b** ([ADR-004](docs/adr/ADR-004-task-runner-app-integration.md)) | Tasks in the app: the scheduler, the task editor, Blocked tasks, resume and cancel | Compiled on Windows in the Living Core P0 run below; manual checklist pending |
| **Living Core P0** | The static V2 Lens mark and the procedural Living Core | Verified for build and tests: 412/412. Manual checklist and performance measurements pending |
| **Living Home, language-switch fix, Claude Code provider** ([ADR-007](docs/adr/ADR-007-claude-code-account-provider.md)) | The home screen from the approved canvas; Claude without an API key | Pending |
| **Shell, Settings v2, Chat, Projects and Search** | The sidebar; categorised Settings; project-scoped chat; the Projects page; global search | Pending |
| **Living Core motion** (Step 4) | Ten states and reactions, matched frame by frame to the reference video | **Pending Windows visual verification** |

The test suite has **903 test cases**: Core 437 and App 466. The last
Windows run recorded in this repository is the Living Core P0's 412/412. The
current suite has not been recorded as run on Windows yet.

---

## Features

### Chat

- **The Living Home.** With no messages, the conversation page shows the
  large Living Core, a greeting for the time of day, and the composer. Once
  messages exist, a compact core sits in the header.
- **Your language.** HAMMOR asks the model to answer in your interface
  language and to mirror mixed Arabic and English.
- **Project scope.** "Chat in this project" adds the project's standing
  context to each turn and saves the exchange under that project. A chip
  shows the scope.
- **Controls.** Stop cancels a running turn. Each message shows its time and
  a Copy button. New chat clears the screen.
- **Tools.** With the API-key provider, the model can call HAMMOR's tools.
  Every call passes the permission engine, and a call above your auto-approve
  level opens a confirmation dialog.
- **Voice.** Replies can be spoken through ElevenLabs, automatically if you
  choose.
- **History.** Every successful turn is saved to local memory as one
  exchange. HAMMOR does not keep chat sessions or threads.

### Projects

- **The store.** Each project has a name, description, folder, kind, standing
  context and dates, and can be archived. Projects scope memory, tasks and
  chat turns.
- **The list.** It starts with **All conversations**: every saved exchange,
  newest first, labelled with its project. Projects follow, most recently
  opened first. Archived projects show only on request.
- **A project.** Its details, standing context, saved conversations, other
  memory and tasks, and **Chat in this project**.
- **Folders.** **Copy folder path** copies the folder's path. HAMMOR never
  opens a stored path.
- **Not yet built.** Creating and editing projects. The page says so.

### Search

- **Opening it.** Press **Ctrl+K** from anywhere, or choose the Search row
  in the sidebar.
- **Sources.** One box searches:
  - pages and Settings categories;
  - projects and tasks;
  - memory and saved conversations;
  - the conversation on screen.

  If a source cannot be read, the page says so and the others still answer.
- **Matching.** Case-insensitive and Arabic-aware. Tashkeel and tatweel are
  ignored, and the alef forms, ى/ي and ة/ه are treated as the same letter.
  So "مهمه" finds "مُهِمَّة".
- **Results.** Grouped by kind, with recent items before you type. Arrow
  keys, Enter and Escape work. Each result opens in its own page.

### Tasks

Background tasks run inside HAMMOR, one at a time, read-only, and only under
a grant you approved.

- **The editor.** It takes:
  - an instruction;
  - read-only tools;
  - folders, for file, Git and project tools;
  - an expiry within 30 days;
  - a cap of 1 to 50 tool calls;
  - an optional schedule and project.

  **Review and create** shows the exact grant in a confirmation dialog, and
  only **Allow** creates the task.
- **Blocked.** A task that needs more than its grant stops as **Blocked**,
  with the reason. Resume it under a new grant, or cancel it.
- **Cancel.** Pending, Running and Blocked tasks can be cancelled.
- **Visibility.** The list updates live, and a dot on the Tasks row shows
  that a task is waiting for you. There are no notifications outside the
  app, and no recurring tasks.

### Memory

- **Storage.** SQLite is authoritative. Every entry is also written as a
  markdown file you can read outside HAMMOR. **Open folder** opens that
  folder in Explorer.
- **Kinds.** Notes, facts, preferences, project context and conversations.
- **The Memory page.** Browse and search by keyword. Semantic search is not
  implemented, and the page says so.
- **Tools.** The model can search memory (`memory.search`, Read) and save to
  it (`memory.save`, Write, which asks first by default).
- **Indexing.** On startup, the markdown folder is indexed incrementally:
  only changed files are re-read.

### Activity

The audit trail: authorisations, tool runs and provider errors, newest first
(the 200 most recent entries).

### Voice

- **Speaking.** ElevenLabs text-to-speech with a configurable voice, played
  on the output device you choose. Each reply is synthesised in full before
  playback; streaming is not implemented.
- **Listening.** Speech input is not implemented. HAMMOR contains no
  microphone, camera or speech-recognition code.

### Settings

- **Seven categories:** Appearance, AI & models, Voice, Memory, Security,
  Tasks & scheduler, and System.
- **Saving.** All categories edit one draft with one Save. The language and
  API keys apply at once.
- **Checks.** **Check connections** re-tests the AI, speech and voice
  providers.
- **First run.** A setup wizard covers language, Claude, voice and storage.
  Every step after the language can be skipped.

---

## Arabic and English

- **Live switching.** Both languages are first-class, and switching takes
  effect without a restart. Switch from **Settings → System** or the status
  bar; the choice persists.
- **Right-to-left.** Arabic mirrors the whole interface: navigation, text
  alignment and indicators.
- **Kept left-to-right.** The brand marks and the Living Core are never
  mirrored. Machine values stay left-to-right in both languages: paths, tool
  names, model and voice IDs, and grant details.
- **Strings.** All 324 interface strings exist in English and Arabic.
  - A missing key shows as `!Key!`, so a gap is visible rather than blank.
  - Adding a language means adding `Strings.<code>.resx` and one entry in
    `ResxLocalizationService.Languages`.
- **Replies.** The model is asked to answer in the interface language and to
  mirror mixed Arabic and English input.
- **Search.** Matching is Arabic-aware (see [Search](#search)).
- **Known gaps:**
  - error messages from Core are in English in both languages;
  - the Arabic strings have not been reviewed by a native speaker.

---

## AI providers

Providers are registered as a collection, and `ai.primaryProvider` chooses
one by its ID. HAMMOR never falls back to a provider you did not choose.

| | **Claude Code** (`claude-code`), recommended | **Anthropic API** (`claude`), optional |
|---|---|---|
| Sign-in | Your own Claude account, through Claude Code's own sign-in window | An Anthropic API key, stored with DPAPI |
| Usage | Counts against your Claude plan | Billed to your Anthropic API account |
| HAMMOR's tools | No: text only | Yes: tool calls through the permission engine |
| Credentials | HAMMOR never reads, stores, displays or logs them | The key is read from the secret store for each call; never in config or logs |
| Built on | The installed, unmodified `claude.exe` in documented print mode | The official Anthropic C# SDK |

**Claude Code** ([ADR-007](docs/adr/ADR-007-claude-code-account-provider.md)):

- **Command lines.** HAMMOR runs only fixed ones, without a shell. Each turn
  is `claude -p --output-format json` with every Claude Code tool and MCP
  server disabled, safe mode on and no saved session.
- **Supported installs.** The native installer and WinGet. The npm launcher
  (`claude.cmd`) is reported and never started.
- **Settings.** Settings shows Claude Code's state (not installed, not
  signed in, signed in, usage limited, needs update), with Sign in, Reconnect
  and Sign out.
- **First run.** HAMMOR picks Claude Code when no API key is entered.
- **Distribution.** Anthropic's terms allow this for an end user signing in
  to their own unmodified Claude Code. Distributing HAMMOR to other people
  with this provider needs Anthropic's Commercial Terms; confirm with
  Anthropic first.

**Anthropic API.**

- **Model settings.** The model, effort and token limit come from
  configuration, never from code. The defaults are `claude-opus-5`, `high`
  and 16,000.
- **Ready check.** The provider is reported ready only after a real API call
  succeeds, not merely because a key exists.
- **Before showing a reply.** Refusals and truncation are checked first.

---

## The Living Core

HAMMOR's presence: a native, procedural WPF control built from the V2 Lens
identity. It is drawn from geometry and animated in code, with no video, GIF,
Lottie or SkiaSharp. The app tells it the state; the control decides how
that looks.

| State | Real trigger |
|---|---|
| **Idle** | Nothing else is happening; the white core can follow the pointer |
| **Listening** | You are typing in the focused composer (no microphone) |
| **Thinking** | A chat turn is in flight |
| **Speaking** | A reply is being spoken aloud |
| **Warning** | An approval prompt for a privileged tool call is open |
| **Blocked** | A task is waiting for your approval |
| **Error** | A turn or task failed; it holds until you act |
| **Success** | A task completed, or you approved a blocked one |
| **Sleep** | The window is minimised, or ten minutes passed without input |
| **Wake** | HAMMOR opens, or comes back from Sleep |

- **Reactions.** It responds to a new reply, to the navigation pane opening,
  and, in Idle, to the pointer.
- **Motion.** It follows the approved prototype's animations, matched frame
  by frame to the reference video.

  The latest motion is **pending Windows visual verification**: it has been
  checked numerically, not yet on a real screen
  ([docs/LIVING-CORE-MOTION.md](docs/LIVING-CORE-MOTION.md)).
- **Reduced motion.** It follows the Windows **Animation effects** setting:
  every state stays readable, held still.
- **Cost.** It draws nothing while minimised or hidden. Otherwise:
  - up to 60 fps while active;
  - 30 fps for a small core or a background window;
  - 20 fps after 45 s in the background;
  - 10 fps asleep.

  Nothing is allocated per frame.
- **The brand.** The V2 Lens mark supplies the app and taskbar icon, the
  title-bar icon, the sidebar mark and wordmark (English and Arabic), and
  the splash screen.

Details: [docs/LIVING-CORE.md](docs/LIVING-CORE.md).

---

## Security model

The guiding rule: the model never acts outside a gate the user controls, and
everything it does is audited.

### Permission engine

- **Permission levels.** Every tool declares one: Read, Write, Execute or
  Destructive.
- **Order of checks:**
  1. Arguments are validated first: nobody is asked to approve a call that
     cannot run.
  2. Destructive tools **always** ask. This floor is checked before your
     settings, and nothing can turn it off.
  3. Anything above your auto-approve level (default **Read**) asks.
- **Refusal.** Only an explicit **Allow** approves. Deny, Escape, closing
  the dialog, or no window to ask in all count as refusal.
- **No command runner.** There is no tool that runs arbitrary commands, and
  a test asserts that none is registered.
- **Bounded turns.** By default a turn runs at most 5 model–tool rounds (hard
  cap 10), and each tool result is cut to 12,000 characters.

The ten built-in tools:

| Tool | Permission |
|---|---|
| `memory.search`, `filesystem.list_directory`, `filesystem.read_file`, `git.status`, `git.diff`, `git.log`, `project.inspect` | Read |
| `memory.save`, `filesystem.write_file` | Write |
| `filesystem.delete_file` | Destructive |

### Filesystem boundary ([ADR-002](docs/adr/ADR-002-filesystem-security-boundary.md))

- **Allowed roots.** File tools work only inside HAMMOR's data folder, your
  memory folder, and folders you list in `security.filesystemAllowedRoots`.
- **Refused:**
  - traversal;
  - paths outside those roots;
  - the Windows and Program Files folders;
  - HAMMOR's own secrets folder;
  - symbolic-link and junction escapes.

### Git ([ADR-005](docs/adr/ADR-005-interactive-git-repository-confinement.md), [ADR-006](docs/adr/ADR-006-git-repository-config-execution.md))

- **Commands.** Three fixed, read-only commands: `status`, `diff` and `log`.
- **Confinement.** Only a repository root with its own `.git` directory is
  accepted. Anything that would redirect Git elsewhere is refused:
  - `gitdir` files;
  - shared and alternate object stores;
  - config includes;
  - work-tree settings.

  The child environment also pins Git to that root.
- **No programs from config.** Repository config cannot make Git run a
  program. External diff, text conversion, signature checks and the
  filesystem monitor are switched off, and repositories that define filter
  drivers are refused.

### Unattended tasks ([ADR-003](docs/adr/ADR-003-task-runner-scheduler.md), [ADR-004](docs/adr/ADR-004-task-runner-app-integration.md))

- **Where authority comes from.** Only from a grant you approve, through the
  confirmation dialog. It is never implicit.
- **Grant limits.** Grants are:
  - read-only;
  - limited to named tools;
  - limited to named folders for file, Git and project tools;
  - capped in tool calls;
  - mandatorily expiring, within 30 days.
- **Immutable.** A grant never changes. A Blocked task resumes only under a
  new grant you approve, and the old one is retired permanently.
- **Normal rules still apply.** The permission engine and the filesystem
  policy still run inside every call. Anything that would need confirmation
  is refused, and the task becomes Blocked.
- **Execution.** One task at a time, with cancellation and bounded retries.
  A task interrupted by a crash is marked Failed and never resumed
  automatically.
- **Residual risks.** These are documented in ADR-004 §2.6–2.7 and ADR-005.

### Secrets and processes

- **Where keys live.** API keys are encrypted with Windows DPAPI for your
  user account, with app-specific entropy. They are never placed in source,
  `hammor.config.json` or logs.
- **Redaction.** Every audit entry passes `SecretRedactor` before it is
  stored. So does error text from providers, tools, tasks and voice before it
  is shown or logged.
- **The repository.** `.gitignore` excludes the configuration file, the
  database, logs and secret patterns. It was committed before any
  configuration existed.
- **The Claude credential.** It never passes through HAMMOR.
- **Processes.** HAMMOR starts only three:
  - `git`, with one of the three fixed read-only commands;
  - your own `claude.exe`, with fixed arguments;
  - Explorer, on HAMMOR's own memory folder.

  None goes through a command shell (cmd or PowerShell).
- **SQL.** Every query is parameterised.

---

## Architecture

Four source projects and two test projects. Dependencies point inward to
Core, and Core cannot see WPF: the compiler enforces it.

```
            ┌──────────────────────────────────────────────┐
            │  HAMMOR.App                 net8.0-windows   │  WPF + WPF-UI: views, view models,
            │                                              │  localisation, the Living Core
            └──────────┬──────────────────────┬────────────┘
                       │                      │
     ┌─────────────────▼───────┐   ┌──────────▼────────────────┐
     │ HAMMOR.Infrastructure   │   │ HAMMOR.Platform.Windows   │
     │ net8.0                  │   │ net8.0-windows            │
     │ Claude API, Claude Code,│   │ DPAPI secrets, NAudio     │
     │ ElevenLabs, SQLite,     │   │ playback, Windows path    │
     │ JSON config, markdown   │   │ resolution                │
     └─────────────────┬───────┘   └──────────┬────────────────┘
                       │                      │
                ┌──────▼──────────────────────▼──────┐
                │ HAMMOR.Core               net8.0   │  no WPF, no Windows APIs:
                │ agent loop, permissions, tools,    │  contracts and domain logic
                │ tasks and grants, memory, projects │
                └────────────────────────────────────┘

   tests/HAMMOR.Core.Tests  net8.0           Core and Infrastructure
   tests/HAMMOR.App.Tests   net8.0-windows   app logic and the Living Core
```

### The agent loop

```
SENSE → UNDERSTAND → ROUTE → PLAN → AUTHORIZE → EXECUTE → VERIFY → REMEMBER → RESPOND
```

- **Reporting.** `AgentPipeline` reports the stage it reached, so a failure
  names its stage, for example `[Route] Claude is not available: …`.
- **Tool calls.** They go through `InvokeToolAsync`, which cannot bypass
  the permission engine.
- **Unattended runs.** They use the same loop, with a confirmation service
  that never approves.

### Design choices

- **Provider-neutral Core.** It holds the contracts, so a provider, or a
  future mobile client reaching Core through a gateway, needs no UI code.
- **Event-driven.** Status checks run on demand. The task scheduler wakes
  on task changes and for the next scheduled task, and otherwise re-checks
  at most every 5 minutes.
- **Atomic configuration writes.** A malformed file falls back to defaults
  and is preserved for inspection, never overwritten.
- **The memory mirror is a decorator** over the SQLite store, so each can be
  tested and replaced on its own.

More in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md); its tasks section and
recommended next steps predate Phases 5 and 6.

---

## Getting started

### Requirements

| | |
|---|---|
| **OS** | Windows 11 (Windows 10 1809 or later should also work) |
| **SDK** | .NET 8 SDK; `global.json` pins `8.0.130`, rolling forward to the latest feature band |
| **Claude** | [Claude Code](https://code.claude.com/docs/en/setup) installed and signed in with your Claude account (recommended), or an Anthropic API key |
| **Optional** | An ElevenLabs API key for spoken replies; Git for Windows for the Git tools |

### Build, test and run

```powershell
dotnet restore
dotnet build HAMMOR.sln -c Release
dotnet test HAMMOR.sln -c Release
dotnet run --project src\HAMMOR.App -c Release
```

- **Platforms.** The Core tests target `net8.0`; the App tests need Windows.
- **Windows checklists.** Each feature document has one.

### First run

A setup wizard asks for the language, Claude, voice and storage. Every step
after the language can be skipped and set later in **Settings**.

---

## Configuration

Edit settings in **Settings**, or directly in `hammor.config.json`. Keys are
camelCase and enums are written as names.

| Section | Keys |
|---|---|
| `general` | `language` (`en`, `ar`), `theme` (`System`, `Light`, `Dark`), `launchOnStartup`, `startMinimisedToTray` |
| `ai` | `primaryProvider` (`claude-code`, `claude`), `model`, `maxTokens`, `effort`, `systemPrompt` |
| `voice` | `ttsProvider`, `sttProvider`, `elevenLabs.*` (voice ID, model, output format, stability, similarity), devices, `speakResponsesAutomatically`, `outputVolume` |
| `memory` | `rootPath`, `indexOnStartup` |
| `security` | `autoApproveUpTo` (default `Read`), `alwaysConfirmDestructive` (always on), `filesystemAllowedRoots`, `auditRetentionDays` |

- **Not applied yet:**
  - `launchOnStartup` and `auditRetentionDays` are saved but not acted on;
  - `startMinimisedToTray` is locked in Settings until the tray exists;
  - `memory.rootPath` does not move memory (see
    [Current limitations](#current-limitations)).
- **The voice.** The shipped ElevenLabs voice is `G3YpdjT1OTh9cunaumJs`. It
  lives at `voice.elevenLabs.voiceId`, editable in **Settings → Voice**, and
  no voice ID is written into the speech code.

---

## Where your data lives

Everything is per user, under `%LOCALAPPDATA%\HAMMOR`. Nothing is written
into the repository.

```
%LOCALAPPDATA%\HAMMOR\
├── hammor.config.json     settings (no secrets)
├── hammor.db              SQLite: memory, projects, tasks and grants, audit
├── secrets\               DPAPI-encrypted API keys, one file per secret
├── memory\                readable markdown mirror (yyyy-MM\*.md)
├── logs\                  daily logs, 14 kept
├── claude-code\           empty working folder for Claude Code runs
└── shell-layout.json      whether the sidebar is collapsed
```

---

## Current limitations

These are real. The interface states most of them; the launch-on-startup
and memory-location gaps are not shown there yet.

| Area | State |
|---|---|
| **Claude Code provider** | Text only. HAMMOR's own tools need the API-key provider. |
| **Speech input** | Not implemented: no speech-to-text, microphone or camera. Voice output works. |
| **TTS streaming** | Not implemented: replies are synthesised in full before playback. |
| **Semantic memory** | Not implemented: memory search is by keyword. |
| **Chat history** | Exchanges are saved one by one; there are no chat sessions or threads. |
| **Projects** | No UI to create or edit projects yet. |
| **Unattended tasks** | Read-only by design, with no recurrence and no notifications outside the app. |
| **Tray, global hotkey, quick overlay** | Not implemented. Settings says so, and "Start minimised to the system tray" is locked. |
| **Launch on startup** | The toggle is saved but not applied yet: HAMMOR does not register itself to start with Windows. |
| **Memory location** | The Memory › Storage location setting does not move memory yet. Notes always live under `%LOCALAPPDATA%\HAMMOR\memory`; the setting only adds a folder the file tools may use. |
| **Living Core voice** | No real speech level is connected; Speaking uses the designed rhythm. |
| **Mobile gateway** | Not built; Core is kept platform-neutral so it can be hosted later. |

---

## Documentation

| Document | Contents |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Layering, agent loop, providers, voice pipeline, tools and permissions, memory, engineering decisions |
| [docs/LIVING-CORE.md](docs/LIVING-CORE.md) | The Living Core, the Living Home and the V2 Lens mark: architecture, state mapping, budget, Windows checklist |
| [docs/LIVING-CORE-MOTION.md](docs/LIVING-CORE-MOTION.md) | The Living Core's motion matched to the reference video, its triggers in HAMMOR, the frame budget, Windows checklist |
| [docs/CHAT-PROJECTS-SEARCH.md](docs/CHAT-PROJECTS-SEARCH.md) | Chat, Projects and global Search, and what HAMMOR's data supports |
| [docs/SETTINGS.md](docs/SETTINGS.md) | Settings v2: categories, navigation, where every setting lives |
| [docs/SHELL.md](docs/SHELL.md) | The shell and sidebar: metrics, behaviour, Windows checklist |
| [docs/adr/](docs/adr/) | Architecture decisions: [001](docs/adr/ADR-001-phase-1-foundation.md) foundation, [002](docs/adr/ADR-002-filesystem-security-boundary.md) filesystem boundary, [003](docs/adr/ADR-003-task-runner-scheduler.md) task runner, [004](docs/adr/ADR-004-task-runner-app-integration.md) tasks in the app and path-scoped grants, [005](docs/adr/ADR-005-interactive-git-repository-confinement.md) Git confinement, [006](docs/adr/ADR-006-git-repository-config-execution.md) Git config execution, [007](docs/adr/ADR-007-claude-code-account-provider.md) the Claude Code provider |
