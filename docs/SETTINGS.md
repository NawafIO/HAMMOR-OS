# HAMMOR Settings v2

**Status: branch `claude/living-shell-sidebar`. UNVERIFIED ON WINDOWS.** It
was written in a Linux container without the .NET SDK, so it has not been
compiled, run or seen on a screen. The C# was parsed, and the XAML and resx
files were checked as XML. Every icon and WPF-UI member used was checked
against WPF-UI 4.3.0, and every binding, command, named field and handler of
the old page was checked to still be on the new one. See
[Windows verification](#windows-verification).

## What changed

The single long Settings page is now a **secondary navigation pane** with
seven HAMMOR categories, and the selected category's page beside it:

- **Back** at the top of the pane returns to the main HAMMOR navigation, on
  the page Settings was opened from.
- **The main sidebar steps back.** While Settings is open it rests as its icon
  rail (still usable), so the settings pane reads as the navigation. Leaving
  Settings puts it back the way you last left it. This resting state is never
  saved as your choice; `Ctrl+B` inside Settings still is.
- **One draft, one Save.** Every category edits the same working copy, so
  switching category never loses an unsaved change. A Save bar sits under
  every page. Language and API keys still apply at once.
- **Rows.** Each setting is a row: name and explanation at the start, control
  at the end, or under the text for wide controls (key fields, the Claude
  Code panel). The rows sit in grouped cards.

No setting was added, removed or changed in behaviour, and nothing in Core,
Infrastructure or the security model changed. The locked controls (always
confirm destructive actions, start minimised) are still locked.

## Where every setting moved

| Category | Settings |
|---|---|
| **Appearance** | Theme |
| **AI & models** | Provider; the Claude Code panel (state, version and method, hints, Sign in, Reconnect, Sign out, Refresh); for the API-key provider: model, effort, max tokens, the Anthropic key (save, clear) |
| **Voice** | Speech provider (read only), ElevenLabs voice ID, the ElevenLabs key (save, clear), speak replies automatically, output and input devices, volume, test speech |
| **Memory** | Memory folder, index on startup |
| **Security** | Auto-approve ceiling, always confirm destructive actions (locked on), how keys are stored |
| **Tasks & scheduler** | No adjustable settings, by design. It lists the fixed rules from ADR-003/004 (read-only, a grant per task that expires within 30 days, Blocked waits for you, one at a time with limited retries, no recurrence yet) and opens the Tasks page |
| **System** | Language, launch on startup, start minimised (locked), check connections, where settings are stored |

**Language is under System**, not Appearance: like BERD, it is treated as an
install-level choice, and in HAMMOR it also decides how the assistant replies.

## Patterns taken from BERD

The `NawafIO/berd-arabic` Settings were read for structure only. No code,
strings or assets were copied.

- A secondary settings navigation with a Back row to the main navigation.
- A small set of plain categories that opens on Appearance.
- Language with the install-level settings.
- A check (here "check connections") as a row inside System, not a page.
- Storage details at the bottom of System.
- Rows of name, explanation and control.

**Not taken:**

- Instant save per control: HAMMOR keeps its draft and Save, which existing
  behaviour and tests rely on.
- Settings search: deferred to the search step.
- BERD's slide animation between the two navigations.

## RTL and LTR

- **Mirroring.** The whole page follows the language's flow direction. In
  Arabic the settings pane is on the right, row text right-aligned, controls
  at the left end, and the Back arrow points right.
- **Kept left-to-right.** Machine values stay left-to-right in both
  languages: model ID, voice ID, speech provider, memory folder, config path
  and Claude Code's own messages.
- **Strings.** All new strings exist in English and Arabic.

## Files

| File | Change |
|---|---|
| `Views/SettingsPage.xaml` | Rewritten: navigation pane, seven category pages, Save bar |
| `Views/SettingsPage.xaml.cs` | Back, Open Tasks, scroll to top on category change |
| `Views/MainWindow.xaml.cs` | Back target, main sidebar rests while in Settings (not saved), page rebuild does not count as a visit |
| `ViewModels/SettingsSections.cs` | The categories, their order, icons and strings |
| `ViewModels/SettingsViewModel.Sections.cs` | The selected category |
| `Controls/SettingsRow.cs`, `Themes/Settings.xaml` | The row control, navigation rows (same look as the sidebar), cards, Save bar |
| `Converters/ValueConverters.cs` | `EnumMatchToVisibilityConverter` |
| `App.xaml` | Merges `Settings.xaml`, registers the converter |
| `Localization/Strings*.resx` | 41 new strings in each language; nothing existing changed |

## Windows verification

```powershell
git fetch origin claude/living-shell-sidebar
git checkout claude/living-shell-sidebar
git log -1 --oneline
dotnet build -c Release
dotnet test -c Release
.\src\HAMMOR.App\bin\Release\net8.0-windows\HAMMOR.exe
```

Expected: 0 errors; **652 tests** pass (Core 437, App 215: 114 before plus
101 for Settings v2), 0 failed, 0 skipped.

Check in English, then Arabic:

1. **Open.** Settings opens on Appearance; the main sidebar folds to its rail.
   The settings pane shows Back, the title and seven categories with icons;
   the selected one has the teal indicator and fill, as in the main sidebar.
2. **Navigate.** Click each category and use the arrow keys. Each page starts
   at its top, with its title and one-line description.
3. **Back.** Back returns to the page Settings was opened from, and the main
   sidebar returns to how you left it (open or rail). Try it with the sidebar
   collapsed before opening Settings.
4. **Everything is still there.** Walk the table above. Change a theme, a
   voice ID and the memory folder, switching categories in between, then
   Save once: all three are saved.
5. **AI & models.**
   - Choosing Claude Code shows its panel, and Sign in / Refresh work as
     before.
   - Choosing the API key shows model, effort, tokens and the key field.
   - A saved key never shows its text.
6. **Voice.** Save and clear the ElevenLabs key; Test speech speaks.
7. **Locked.** "Always confirm destructive actions" and "Start minimised" are
   on/off and disabled exactly as before.
8. **Language.** Switch language in System: the page rebuilds in the new
   language, stays on System, and the main sidebar does not move.
9. **Arabic layout.**
   - The pane is on the right and the Back arrow points right.
   - Row text is on the right and controls on the left.
   - IDs and paths still read left-to-right.
10. **Narrow window.** At the minimum size (840 px) nothing is cut off; rows
    wrap their text.

Send a screenshot of any page that looks wrong; the look lives in
`Themes/Settings.xaml`.
