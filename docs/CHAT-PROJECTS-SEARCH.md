# Step 3: Chat, Projects and Search

**Status: branch `claude/living-shell-sidebar`. UNVERIFIED ON WINDOWS.** It
was written in a Linux container without the .NET SDK, so it has not been
compiled, run or seen on a screen. What was checked:

- the C# was parsed;
- the XAML and resx were checked as XML;
- the WPF-UI members and icons used were checked against 4.3.0;
- the new type names do not clash with WPF-UI's.

## What HAMMOR's data actually supports

These facts decided the design. Nothing below invents data.

- **No chat sessions.** The conversation on screen lives in memory and is gone
  when cleared or when the app closes.
- **Every successful interactive turn is saved.** The agent pipeline writes it
  to memory as a `Conversation` entry, `"User: …" newline "HAMMOR: …"`, tagged
  with the turn's project. HAMMOR can therefore list and search past
  **exchanges** (one question and answer each), but cannot rebuild threads,
  because exchanges are not linked to each other. The UI says this.
- **Projects** exist in a store (name, description, folder, kind, standing
  context, dates, archived). There is still no UI to create or edit them.
  Memory and tasks can belong to a project.
- **Chat turns can be scoped to a project.** Core already accepts a project
  for a turn: the pipeline adds the project's standing context to the prompt
  and saves the exchange under the project. The app never used this before.
  Now it does, only when you choose a project.
- **Memory search** is the store's keyword (LIKE) match. Semantic search does
  not exist.
- **Tasks** can be listed (the newest 200 are searched) and filtered by
  project.

No Core, Infrastructure or security code changed.

## Chat

- **Project scope.** "Chat in this project" on the Projects page scopes the
  next turns to that project. A chip in the header and above the composer
  shows it; its cross returns to no project. Without a project, behaviour is
  exactly as before.
- **Stop.** While a turn runs, the send button becomes Stop. It cancels the
  turn through the cancellation token the pipeline already honoured; the
  question stays and no reply is added. A stopped turn is not shown as an
  error, so the Living Core does not go to Error.
- **Message actions.** Each message shows its time and a Copy button, when
  hovered or focused.
- **New chat.** The header's clear button is now "New chat", with the same
  behaviour. It keeps the project scope.
- **Unchanged:** the Living Core, the composer's look and Listening, the
  greeting, voice, and AI and provider behaviour.

## Projects

The page is a list beside the selected item's details.

- **The list.**
  - **All conversations** is first: every saved exchange, newest first, each
    labelled with its project.
  - Projects follow, most recently opened first. Archived ones are hidden
    unless "Show archived projects" is ticked, and then they are dimmed.
- **A project.**
  - The header shows its name, kind, dates and description.
  - **Chat in this project.**
  - **Copy folder path.** The folder is copied, never opened: starting a
    stored path through the shell could run whatever it points at.
  - Its standing context.
  - Its saved conversations, its other memory (notes, facts, preferences)
    and its tasks.
- **Honest limits.** The page says that creating and editing projects is not
  built yet.

## Search

- **Where.** A **Search** page, and the first sidebar row after Chat.
  **Ctrl+K** opens it from anywhere, or puts the caret back in the box.
- **What it searches:**
  - pages;
  - Settings categories;
  - projects;
  - tasks;
  - memory and saved conversations, through the store's keyword search;
  - the conversation on screen.

  The page says this, and says when a source could not be read; the other
  sources still answer.
- **Matching.** Matching is case-insensitive and Arabic-aware: tashkeel and
  tatweel are ignored, and أ إ آ ٱ count as ا, ى as ي and ة as ه. So "مهمه"
  finds "مُهِمَّة".
- **Ranking.** Results are grouped, in this order:
  1. Pages
  2. Settings
  3. Projects
  4. Saved conversations
  5. Memory
  6. Tasks
  7. This chat

  Within a group, matches at the start of the title come first, then matches
  at the start of a word, anywhere in the title, then in the detail line.
  Newer comes first among equals. Each group shows at most four (pages,
  settings) or six results.
- **Before typing.** Recent saved conversations and projects.
- **Keyboard.** The arrows move through results, Enter opens one, Escape
  clears the box. A click also opens.
- **Opening a result.**
  - A Setting opens Settings on its category.
  - A project opens Projects with it selected.
  - Memory or a conversation opens Memory searching for it.
  - A task opens Tasks.
  - A message opens Chat.

## Patterns from BERD (`NawafIO/berd-arabic`, read only)

**Taken:**

- Search as its own view with one heading box, results grouped by type,
  recents before typing, and keyboard-first navigation.
- Message actions on hover (copy, metadata).
- A project selector shown as a chip on the composer.
- Project rows with a second line.

**Not taken:**

- Chat sessions, pinning, unread dots, renaming and drag-and-drop. They need
  session data HAMMOR does not have.
- Transcript search inside Chat; the global Search covers this chat.

No BERD code, branding, colours or product logic was copied.

## Files

| File | Change |
|---|---|
| `Search/SearchText.cs` | Folding (Arabic-aware) and match ranking |
| `Search/SearchResult.cs`, `Search/SearchComposer.cs` | Result model; grouping, ordering, caps, recents |
| `Search/SearchService.cs` | Reads the stores, read only; failures per source |
| `Search/ConversationExchange.cs` | Reads saved exchanges back |
| `Shell/ShellPage.cs`, `Shell/ShellNavigator.cs` | Navigation for Search and Projects (Settings category, project selection, memory search, chat project) |
| `ViewModels/SearchViewModel.cs`, `Views/SearchPage.xaml(.cs)` | The Search page |
| `ViewModels/ProjectsViewModel.cs`, `Views/ProjectsPage.xaml(.cs)` | The Projects page (moved out of the shared list files) |
| `ViewModels/ChatViewModel.cs`, `Views/ChatPage.xaml(.cs)` | Project scope, Stop, time and Copy, New chat |
| `ViewModels/ListPageViewModels.cs`, `Views/ListPages.xaml.cs` | Memory opens a pending search; projects code moved out |
| `Views/MainWindow.xaml(.cs)` | Search row, Ctrl+K, navigator |
| `Themes/Tokens.xaml` | Message action button and project chip styles |
| `App.xaml.cs` | Registrations |
| `Localization/Strings*.resx` | 44 new strings in each language; nothing existing changed |

## Windows verification

```powershell
git fetch origin claude/living-shell-sidebar
git checkout claude/living-shell-sidebar
git log -1 --oneline
dotnet build -c Release
dotnet test -c Release
.\src\HAMMOR.App\bin\Release\net8.0-windows\HAMMOR.exe
```

Expected: 0 errors; **744 tests** pass (Core 437, App 307: 215 before plus 92
for Step 3), 0 failed, 0 skipped.

Check in English, then Arabic:

1. **Chat.**
   - Send a message: hover each bubble to see its time and Copy, and paste to
     confirm.
   - While a reply is running, the send button is Stop. Press it: the
     question stays, no reply comes, and the Living Core does not show Error.
   - New chat clears the screen.
2. **Projects.** You need at least one project in the store. HAMMOR cannot
   create one yet; a task's project picker reads the same store.
   - All conversations lists past exchanges, newest first, with project
     labels.
   - Select a project: its details, conversations, notes and tasks.
   - Copy folder path copies; nothing opens.
   - Tick "Show archived".
3. **Chat in a project.**
   - From a project, choose Chat in this project. The chip shows in the
     header and above the composer.
   - Ask something: the reply uses the project's context, and the exchange
     then appears under that project.
   - The chip's cross removes the scope.
4. **Search.**
   - Ctrl+K from any page.
   - Type "set", a project name, a task title, and a word from a past
     answer. Check the groups and their order.
   - Arrows, Enter and Escape. Open each kind and check where it lands; a
     setting result opens that Settings category.
5. **Arabic search.** Type "مهمه" to find a title written "مُهِمَّة", and
   "احمد" to find "أحمد".
6. **RTL.**
   - The project list, the search results and their indicators mirror.
   - The folder path and Claude Code messages stay left to right.
   - The chips sit on the start side.
7. **Regressions.**
   - The sidebar: Ctrl+B and the remembered state.
   - Settings v2: Back and its sidebar folding.
   - The Living Home, and the language switch on every page.
