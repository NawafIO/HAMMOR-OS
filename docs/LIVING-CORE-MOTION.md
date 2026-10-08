# Step 4: Living Core motion, reworked to the reference

**Status: branch `claude/living-shell-sidebar`. UNVERIFIED ON WINDOWS.** It
was written in a Linux container without the .NET SDK, so it has not been
compiled, run or seen on a screen. What was checked:

- the C# was parsed, and every member the changed code and tests use was
  cross-referenced against the declarations in the repository and the .NET 8,
  WPF-UI 4.3 and CommunityToolkit reference assemblies;
- the XAML and resx files were checked as XML, and English and Arabic have
  the same keys;
- the 650 look values in `LivingCoreLooks.cs` were compared with a Python
  port of the engine, and every assertion of `LivingCoreLifeTests` and
  `LivingCoreMotionTests` (235 in all) was run against that port: all pass.

None of this replaces a compiler or a real screen.

## The reference

No video reached this session: the only attachment was the canvas PDF. The
behavioural source used instead is the approved Living Core canvas and its
**Prototype** board: its CSS keyframes and its event code (pointer, message,
panel, the ten state buttons). The state boards, interaction rules, motion
board and guardrails fill in what the prototype leaves out. If the video
differs from the prototype anywhere, name the state and what differs.

The first Step 4 added idle wander, parallax and breathing on top of the six
P0 states. It was too subtle and did not reproduce the reference's
state-driven motion. This rework replaces it: the wander is gone, and each of
the ten states has its own pose, intensity, white-core behaviour, ring and
thread behaviour and transition. The identity, geometry and colours are
unchanged; the success green (`#6AD895`), warning amber (`#E99B2A`) and ember
(`#E9C9A8`) are the prototype's own signal colours.

## The ten states

| State | White core | Inside | Halo, rings, threads | Body, aura |
|---|---|---|---|---|
| **Idle** | Rest; micro-drift; one glance about every 14 s; follows the pointer | Cells drift and bob; stars shimmer | Halo breathes over 6.4 s; threads 14, 22, 30, 42 s | Aura breathes over 8 s |
| **Listening** | Centred, 112%, attention ring | Stars brighter, shimmer twice as fast; lateral lines bright | Inward rings at 55%, lifted to full by typing; the inner ring moves with the input | — |
| **Thinking** | Up and to the left (−35%, −20% of its zone), 78%, 55% light | Links and travelling signals between cells; orbit lanes; lens precesses | Threads faster | Aura steady |
| **Speaking** | Centred, 125%, pulsing to 147% | Cells still; voice wave | Halo replaced by outward rings every 1.8 s; threads pulse | Aura pulses |
| **Success** | Lifted up and forward, to the right (+68%, −69%), 108%, brighter | Cells rise together (−3.5) and settle back; stars catch light | One green ring grows from 95% to 150% and dissolves; one thread laps once in 1.2 s; rim in the success tone | Warm bloom: 95→112→100% size, 40→100→55% light, once over 3.2 s |
| **Warning** | Held back at 92% (+37%, −12.5%), glow narrowed; no drift, no glances | Lens narrows to 80%, crest rises 4; cells slow, no bob | Inner halo tightens to 97–98% and holds; one amber segment pulses every 3.2 s; threads at half speed | Aura 60% |
| **Blocked** | Low and a little right (+16%, +31%), 94%; one glance toward the request, then waits | Stars and cells slow | Gap ring; threads parked, a slow light at the park | Aura 55% |
| **Error** | To the right (+68%, +6%), 90%, 60% light | Crack; cells dim and spread | Danger rim; the inner ring stutters; fragments; threads broken | Aura 30% |
| **Sleep** | A warm ember at rest, no glow, following nothing | Cells settle to a low band; stars almost out (15%); lens dims | Threads stop; halo breathes at half | The body sinks 9, shrinks to 86% at 60% light and breathes 86–87.5% over 9.6 s; aura 20% |
| **Wake** | A point of light swells to 150% and settles (0–25%) | Rim draws itself round (16–50%); lens sweeps in (30–60%); cells and stars bloom (48–80%) | Halo and threads arrive last (68–100%) | Aura rises (60–100%) |

Percentages of position are of the white core's zone. Wake lasts 2.4 s and
then hands over to the state underneath.

## Real HAMMOR triggers

| State or reaction | Source | Notes |
|---|---|---|
| Listening | The composer has focus and text | Each keystroke reaches the core as `InputSignal`; the rings follow the rhythm. Holds 1.2 s after typing stops. No microphone. |
| Speaking | `SpeechPlaybackMonitor` | Held 0.8 s after the last word. |
| Thinking | A chat turn in flight, until its reply arrives | |
| Success | A task completes, or a blocked task is approved (Blocked → Pending or Running) | Held for its 3.2 s timeline. |
| Warning | The approval prompt for a privileged tool call (`ConfirmationDialog`) is open | `ApprovalPromptWatcher` only hears the dialog's Loaded and Closed events. It never shows, answers or reads a prompt. |
| Blocked | Any task in `TaskState.Blocked` | |
| Error | A failed turn, or a task that failed this session | Holds until the user acts. |
| Sleep | The window is minimised, or ten minutes pass with no key, click, wheel or pointer move | Only when nothing else is going on. |
| Wake | Leaving Sleep for any reason; the window restored; the app opening | Leaving Sleep always goes through Wake. |
| New message | An assistant reply is added to the conversation | One ripple leaves the membrane (100→135%, 75%→0 in 0.9 s) and the cells shiver once. In Idle or Success, one glance toward the transcript and back. |
| Panel | The user opens the navigation pane on the chat page | In Idle or Success, one glance toward the pane, and the aura leans 4 units its way. |
| Pointer | The pointer moves over HAMMOR's window, in Idle | Followed inside the zone, 450 ms behind. Moves under 24 px are ignored. Let go after 5 s of stillness, or 1.2 s after the pointer leaves. The cells move 30% with the white core and the stars 10% against it. |

**Who moves the white core.** One owner, `LivingCoreMotion`. States rank
Wake, Speaking, Listening, Warning, Thinking, Blocked, Error, Success, Sleep,
Idle. Within a state the order is the pose, then a glance toward a message or
panel, then the pointer (Idle only), then rest.

The collision rules:

- no two glances within 2 s;
- message bursts within 2 s count as one;
- while listening, thinking, speaking, waiting, blocked, in error or asleep a
  message only ripples;
- a glance in progress fades out when the state stops allowing it.

## Transitions

- Every state change uses the approved 1.6 s cascade, on ease.settle:
  - the white core from 0 to 0.45 s;
  - cells and stars from 0.2 to 0.8 s;
  - halo and threads from 0.4 to 1.2 s;
  - aura and energy lines from 0.8 to 1.6 s.
- A change during a change starts from the frame on screen.
- Wake and Success add their one-shot keyframes, starting from a fixed point
  as the reference does: the ignition from a point of light, the bloom from
  40%.
- Leaving Success fades the bloom. Leaving Blocked in the middle of its
  glance fades the glance.
- The tests check every frame of an 18-state sequence for jumps. At 60 fps
  the white core moves at most 3.2 units per frame, and no light changes by
  more than 0.17 per frame outside those one-shot starts.

## Where it differs from the prototype

1. **Speaking hides the halo.** The approved P0 state board replaces it with
   the outward rings; the prototype keeps both.
2. **Idle glances about every 14 s.** The motion and state boards ask for
   this; the prototype has no idle glance.
3. **Blocked glances once.** The state board asks for one glance; the
   prototype repeats it.
4. **No glances in Warning or Error**, as their boards say.
5. **Listening's rings rest at 55%.** Typing lifts them, because there is no
   microphone and typing is the user's input.
6. **Sleep's sediment is narrower and flatter.** The cells are at 50% width
   and 20% height, dropped 37 units. The prototype uses full width and 40%
   height, dropped 38, which puts cells past the rim at some rotations. The
   tests check that every cell stays inside at every rotation.
7. **The cells move in 0.6 s.** They follow the motion board's cascade (0.2
   to 0.8 s); the prototype eases them over 1.2 s. The sediment therefore
   falls twice as fast as in the reference.
8. **The app opens with Wake.** This replaces the earlier cascade from a
   dormant look.
9. **The panel is the navigation pane.** It is HAMMOR's only panel beside the
   core.
10. **Warning is the privileged-tool prompt.** HAMMOR has no separate caution
    notice.

## Reduced motion

Windows Settings › Accessibility › Visual effects › **Animation effects**
off:

- Every state keeps its pose, shape and light.
- Drift, glances, the pointer, the aura lean, the shiver, the thread lap and
  breathing all stop.
- Wake becomes a 0.4 s fade.
- Success becomes a change of light only: the bloom 40→100→55%, the ring
  held at 120%.
- The ripple becomes one soft rise and fall of light.
- Frames stop once a state has settled, except in Speaking and Blocked, whose
  light changes slowly.

**Settings › Appearance › Living Core motion** shows which mode is active.

## Performance

`LivingCoreFramePolicy` decides when and how often the core draws:

| Situation | Frames |
|---|---|
| Minimised, hidden, unloaded | None |
| Reduced motion, settled state | None |
| Window active, hero core | Up to 60 fps, also on 120 and 144 Hz displays |
| Core 160 px or smaller (the 112 px header) | 30 fps |
| Window in the background | 30 fps, then 20 fps after 45 s |
| Settled in Sleep, window shown | 10 fps (Guardrails board: "10 asleep") |

- **Nothing is allocated per frame.**
- **The new layers are cheap.** They are ten drawing visuals (warm bloom,
  success ring, warning mark, two halo tones, two rim tones, the rim's draw
  line, ripple and ember) and two containers (aura group, body). They are
  drawn once and animated by transform and opacity. The exception is the
  rim's dash offset, which moves only during Wake.
- **Unchanged values are skipped**, as before.
- **The pointer is cheap.** A move only stores a number after the 24 px
  dead zone, and only while the core is animating.
- **The presenter adds no polling.** It has five one-shot timers, one per
  hold (speaking, listening, success, wake and the ten-minute sleep
  deadline). The sleep timer is re-armed only when it fires early.

## Files

| File | Change |
|---|---|
| `Presence/LivingCoreState.cs` | Ten states |
| `Presence/LivingCoreLook.cs`, `LivingCoreLooks.cs` | New look values (glow size, ember, cell squash, narrow and drop, lens and crest, halo tightness, voice ring, rim tones, warning mark, body); the four new looks |
| `Presence/LivingCoreMotion.cs` | Rewritten to the reference: poses, pointer, depth, reactions, collisions, Success and Wake timelines, Sleep |
| `Presence/LivingCoreFrame.cs`, `LivingCoreScene.cs` | The new frame values and visuals |
| `Presence/LivingCoreReaction.cs` (new) | New-message and panel reactions |
| `Presence/PointerDeadZone.cs` (new) | The 24 px dead zone |
| `Presence/ApprovalPromptWatcher.cs` (new) | Observes the approval prompt for Warning |
| `Presence/LivingCoreSignals.cs` | The new signals, ranks and `NeedsWake` |
| `Presence/LivingCorePresenter.cs` | Success, Warning, Sleep and Wake from real events; reactions; typing |
| `Presence/LivingCore.cs` | `Reaction` and `InputSignal` properties; the dead zone; letting go when the pointer leaves |
| `Presence/LivingCoreFramePolicy.cs` | 10 fps asleep |
| `Presence/CubicBezierEasing.cs` | The prototype's sleep curve (`Sink`) |
| `Presence/LivingCoreDesign.cs` | The first Step 4's wander clocks removed |
| `Views/ChatPage.xaml`, `Views/MainWindow.xaml.cs` | Bindings; activity, minimise and pane events reported to the presenter |
| `Localization/Strings*.resx` | Four state descriptions in each language |
| `tests/HAMMOR.App.Tests/Presence/` | See below |

No Core, Infrastructure, security, permission, chat, speech, task or
approval behaviour changed. There is no camera, microphone or speech-to-text.

Tests (App, 359 → 451):

| File | Before | After |
|---|---|---|
| `LivingCoreLifeTests` (rewritten) | 28 | 57 |
| `LivingCoreDesignTests` | 13 | 31 |
| `LivingCoreStateResolverTests` | 10 | 27 |
| `LivingCoreFramePolicyTests` | 20 | 27 |
| `LivingCoreStringsTests` | 4 | 15 |
| `PointerDeadZoneTests` (new) | — | 10 |
| `LivingCoreMotionTests` | 18 | 18 (four retargeted to the new behaviour) |

## Windows verification

```powershell
git fetch origin claude/living-shell-sidebar
git checkout claude/living-shell-sidebar
git pull origin claude/living-shell-sidebar
git log -1 --oneline
dotnet build -c Release
dotnet test -c Release
.\src\HAMMOR.App\bin\Release\net8.0-windows\HAMMOR.exe
```

Expected: 0 errors; **888 tests** pass (Core 437, App 451), 0 failed, 0
skipped.

To run only the Living Core tests:

```powershell
dotnet test tests\HAMMOR.App.Tests -c Release --filter "FullyQualifiedName~HAMMOR.App.Tests.Presence"
```

First open **Settings › Appearance**: "Living Core motion" must say **Full
motion**, or every check below shows a still core by design.

### What to look at

1. **App start.** Wake: a point of light swells and settles, then the rim
   draws itself round, then the lens, cells and stars, then the halo and
   threads, with the aura last. About 2.4 s.
2. **Idle and the pointer.**
   - Move the pointer around the window: the white core follows inside its
     zone, a little behind, never past it.
   - The cells move with it and the stars against it.
   - Small moves are ignored. Stop for 5 s, or leave the window: it returns
     to rest.
   - In Arabic it follows the physical side.
3. **Listening.** Click the composer and type: the core centres and grows,
   and the inward rings draw in, stronger while you type and easing 1.2 s
   after you stop.
4. **Thinking, then a new message.** Send: the core moves up and back,
   dims, and links and signals run between the cells. When the reply lands,
   one ripple leaves the membrane and the cells shiver; once back in Idle a
   later reply also draws one glance toward the conversation.
5. **Speaking.** Have a reply spoken: the core centres and pulses, rings
   pulse outward.
6. **Warning.** Trigger a privileged tool call that asks for approval. While
   the prompt is open: the core holds back, the lens narrows, the halo
   tightens and one amber segment pulses at the top right. Deny or allow:
   it returns.
7. **Blocked, then Success.** A task waiting for approval: the core sits
   low, glances once toward the request and waits; the gap ring shows.
   Approve it: Success lifts and brightens once, with a green ring, the
   warm bloom and one thread lap, then settles.
8. **Error.** A failed turn (for example with no network): dimmer, the crack
   and the danger rim; it holds until you send again.
9. **Panel.** On the chat page with the sidebar collapsed, open it: the core
   glances toward it and the aura leans its way.
10. **Sleep and Wake.** Minimise, then restore: Wake plays. Or leave HAMMOR
    untouched for ten minutes: the core sinks, shrinks and dims to an ember,
    and the cells settle low. Move the pointer: Wake, then Idle.
11. **Reduced motion.** Turn Animation effects off: every state above still
    reads, held still, and the Appearance row says reduced.
12. **Conversation header.** The 112 px core shows the same states.

### Visual checks needing a real screen

- **Sleep's sediment.** The cells flatten to 20% height. If they read as
  slivers instead of cells, the alternative is the prototype's 40% with a
  higher band.
- **Sleep's fall.** The sediment falls in 0.6 s, twice as fast as the
  prototype. If it reads as a drop rather than settling, the cells' cascade
  stage for Sleep should lengthen to 1.2 s.
- **Small sizes.** The warning segment, the success ring and the ember at
  112 px.
- **Amber and green.** Check both against the approved boards, on the
  default and violet themes.

### Performance

Use the CPU and GPU commands in [LIVING-CORE.md › Performance](LIVING-CORE.md#performance):

- focused Idle;
- Speaking;
- another window focused, for 30 s and after 45 s;
- Sleep after ten quiet minutes (10 fps);
- minimised (about the baseline).

Report the numbers. Nothing has been measured yet.
