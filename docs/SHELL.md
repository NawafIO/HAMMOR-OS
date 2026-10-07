# HAMMOR shell and sidebar

**Status: Step 1 of the shell work, branch `claude/living-shell-sidebar`.
UNVERIFIED ON WINDOWS.** It was written in a Linux container without the .NET
SDK, so it has not been compiled, run or looked at on a real screen. Syntax
was checked with a C# parser, XAML and resx as XML, and every WPF-UI member
used against the WPF-UI 4.3.0 sources. The geometry was checked with an HTML
approximation of the same numbers, not a WPF render. See
[Windows verification](#windows-verification).

Step 1 covers the sidebar only. Search, the Settings structure, and projects
and chats are later steps ([What is not in Step 1](#what-is-not-in-step-1)).

## Where the patterns come from

The reference is the `NawafIO/berd-arabic` repository (a Tauri and React
desktop app). Its sidebar was read, not copied: no BERD code, asset, string or
brand is in HAMMOR, and the numbers below are layout measurements. BERD is
Apache-2.0; nothing from it is redistributed here.

| BERD pattern | Decision for HAMMOR |
|---|---|
| Dense, even rows: 28 px high, 8 px between icon and label, 6 px inset | **Taken, at 36 px.** 28 px is too tight for Windows pointer targets and 14 px text; 36 sits between BERD and WPF-UI's 40. |
| Hover and selection change only the fill; text and icon keep one foreground | **Taken.** A slim lens-teal indicator stays as HAMMOR's own selection mark. |
| Collapsed rail: icons only, tooltip on the content side, labels fade out | **Taken.** Tooltips show only while the pane is a rail; WPF-UI's own 160 ms open/compact animation is kept. |
| An attention dot on a row, inline when open and on the icon's corner when collapsed | **Taken, mapped to a real need:** tasks Blocked waiting for approval put a dot on Tasks. |
| Sidebar open/closed remembered; `Ctrl+B` toggles | **Taken.** |
| Settings kept at the bottom, apart from the main rows | Already so; unchanged. |
| Resizable sidebar (200 to 420 px, snaps shut below 100, double-click resets) | **Not in Step 1:** see [Why no resize](#why-no-resize-yet). |
| Settings as a second navigation surface that slides over the first | Done in Settings v2 ([SETTINGS.md](SETTINGS.md)). |
| Projects and Chats sections with hover actions and recents | Deferred: HAMMOR does not store conversations, and its projects screen has no creation UI yet. |
| A search view with grouped results and keyboard navigation | Deferred (search step; needs a backend first). |

## What Step 1 changes

| Area | Change | Files |
|---|---|---|
| Rows | WPF-UI's item template is replaced by a copy of its 4.3.0 compact template: 36 px rows, 8 px radius, 2 px between rows, one foreground, no nested-item parts. | `Themes/Sidebar.xaml` |
| Pressed fill | A theme override for the pressed row, like the hover and selected fills from the Living Home. | `Themes/ShellSurfaces.cs` |
| Header | The full mark and the compact lens sit on the icons' axis, 20 px from the pane edge, open or collapsed. Marks are still never mirrored. | `Views/MainWindow.xaml` |
| Remembered state | `ShellLayoutStore` saves whether the sidebar is collapsed to `%LOCALAPPDATA%\HAMMOR\shell-layout.json`. | `Shell/ShellLayout.cs` |
| `Ctrl+B` | Toggles the sidebar; every way the pane changes (its button too) is saved. | `Views/MainWindow.xaml.cs` |
| Blocked dot | `BlockedTaskTracker` counts Blocked tasks from the store's change events, as the Living Core's presenter does. | `Shell/BlockedTaskTracker.cs` |
| Strings | `Nav.Tasks.Attention`, in English and Arabic. | `Localization/*.resx` |

Nothing in Core, the permission engine, filesystem policy, task runner, Git
confinement, or the Living Core's motion changed. The tracker only reads.

### Metrics

| | Value |
|---|---|
| Row | 36 px high, 8 px radius, 1 px margin above and below |
| Icon | 16 px, centred in a 40 px column (the rail's width), so it never moves when the pane opens |
| Label | 14 px, starts 36 px from the row's start edge, 8 px from the end |
| Indicator | 3 × 14 px, on the start edge (mirrors in RTL) |
| Hover / pressed / selected fill | 5% / 8% blue-white, 10% lens teal (unchanged from the Living Home, plus pressed) |
| Attention dot | 6 px in the Blocked colour (`StatusBlockedBrush`), ringed 2 px in the pane colour |
| Pane | 220 px open (unchanged), 40 px rail; WPF-UI adds 4 px of margin each side |

### Behaviour

- **Collapse.** The pane toggle button and `Ctrl+B` both flip WPF-UI's
  `IsPaneOpen`. The choice is saved on every change and restored on launch.
  A saved collapse plays WPF-UI's 160 ms close once at start-up, because its
  template always begins open.
- **Rail.** Rows show only their icon, centred. Each row has a tooltip with
  its name, on the content side, and only while the pane is a rail.
- **Dot.** While any task is Blocked, Tasks shows a dot: at the end of the row
  when open, on the icon's top corner in the rail. The row also reports "Tasks
  waiting for your approval" to assistive technology. The dot follows the
  store live and is correct on launch.
- **A bad layout file never matters.** Missing, corrupt, empty, newer-version
  or unreadable files mean "open"; a failed save is logged. The file holds
  only that one flag.
- **RTL.** The pane, indicator, label alignment and dot mirror with the window's
  flow direction. The mark and wordmark are drawn left-to-right inside the
  header so they are never flipped.

### Why no resize yet

BERD's resizable sidebar needs the pane's width to change while the window is
running. In WPF-UI 4.3.0 the pane's width is driven by the `PaneOpen` visual
state's storyboard, which animates the pane from 40 px to `OpenPaneLength`
and then holds that value. After the first open, changing `OpenPaneLength`
(what a drag would do) is very likely not reflected, and the state cannot be
re-entered without closing and reopening the pane. The reliable fix is to
replace WPF-UI's whole `LeftNavigationViewTemplate`, which is much bigger than
this step and cannot be checked without a Windows build. The same limit
rules out BERD's slower 320 ms collapse. Both belong in a later step, after
Step 1 is confirmed on Windows.

## What is not in Step 1

- **Search.** Needs a backend and an agreed scope (memory? tasks? settings?).
  BERD's search view, its grouped results card, recents and keyboard model
  are the reference.
- **Settings structure.** Since done as Settings v2 ([SETTINGS.md](SETTINGS.md)). BERD slides a second navigation (the settings
  sections) over the main one, with a back row. HAMMOR's Settings page is one
  long scroll today.
- **Projects and chats.** HAMMOR keeps no conversation history, so there is
  nothing to list, and projects have no creation screen. Both need data work
  before they need sidebar work.

## Windows verification

Run from PowerShell in the repository root.

```powershell
git fetch origin claude/living-shell-sidebar
git checkout claude/living-shell-sidebar
git log -1 --oneline
dotnet build -c Release
dotnet test -c Release
.\src\HAMMOR.App\bin\Release\net8.0-windows\HAMMOR.exe
```

Expected: 0 errors; **551 tests** pass (Core 437, App 114: 91 before, 22 new
for the layout store and the blocked-task tracker, and 1 for the new string),
0 failed, 0 skipped.

Manual checks, in English and Arabic:

1. **Rows.** Five rows plus Settings, 36 px high, evenly spaced, no fill until
   hovered; Chat has the teal indicator and a faint teal fill that stays under
   the pointer. Text and icons do not change colour on hover or selection.
2. **Alignment.** Icons, the full mark and the compact lens share one vertical
   axis whether the pane is open or collapsed.
3. **Collapse.** The toggle button and `Ctrl+B` both collapse and expand. In
   the rail, each icon shows its name in a tooltip; when open, no tooltips.
4. **Remembered.** Collapse, close HAMMOR, reopen: it opens collapsed (after a
   short close animation). Expand, reopen: open. Delete
   `%LOCALAPPDATA%\HAMMOR\shell-layout.json`: it opens normally.
5. **Dot.** Create a task that asks for a file outside its folder (ADR-004
   checklist step 9). When it becomes Blocked, a dot appears on Tasks, inline
   when open and on the icon when collapsed. Resume or cancel it: the dot
   goes. Switch language: still correct.
6. **RTL.** In Arabic the pane is on the right, the indicator on the row's
   right edge, labels right-aligned, the dot on the left of the row, the
   tooltip on the content side; the mark is not mirrored.
7. **No regressions.** Navigation between pages, the language switch, the
   Living Core and the status bar behave as before.
8. **Layout file.** `Get-Content "$env:LOCALAPPDATA\HAMMOR\shell-layout.json"`
   shows only `version` and `sidebarCollapsed`.

If row heights, the dot's position or the tooltips look wrong, the cause is
most likely in `Themes/Sidebar.xaml`; send a screenshot.
