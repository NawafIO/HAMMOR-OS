# Step 4: Living Core motion, matched to the reference video

**Status: branch `claude/living-shell-sidebar`. UNVERIFIED ON WINDOWS.** It
was written in a Linux container without the .NET SDK, so it has not been
compiled, run or seen on a screen. What was checked is listed under
[How the video was used](#how-the-video-was-used). None of it replaces a
compiler or a real screen.

## Why it changed again

The previous rework was built from the canvas boards. The boards soften the
prototype: one 1.6 s cascade for every change, idle glances, halos hidden
while speaking, loops slowed in Error and Blocked, a thread lap on Success.
The video shows none of that. It is a recording of the canvas's **Play with
the core** prototype, which is SVG driven by CSS, and it moves the way that
CSS moves.

So the engine is now an exact emulation of the prototype's CSS, element by
element, as Chrome applies it. Where the boards and the video disagree, the
video wins. The identity, the geometry, the colours and the state wiring are
unchanged.

## How the video was used

1. **The recording.** 1080 × 738 px, 37.3 s, 2,244 frames at about 60 fps.
   The core's zone was calibrated from the frame at 2.546 px per design unit.
   The twelve state changes were read from the stage label:

   | Time | State | Time | State |
   |---|---|---|---|
   | 2.49 s | Thinking | 21.44 s | Warning |
   | 7.10 s | Success | 23.64 s | Error |
   | 9.07 s | Blocked | 25.45 s | Wake |
   | 13.23 s | Sleep | 28.05 s | Idle (Wake hands over) |
   | ≈15.3 s | Listening | 29.47 s | Speaking |
   | 18.54 s | Speaking | 31.54 s | Listening |

2. **The white core was tracked in every frame.**
3. **Ground truth in Chromium.** The prototype's own source was run in
   headless Chromium, driven with the video's timeline. The computed style
   of every animated element was sampled every frame. Frames rendered from
   it were checked side by side against the video's frames.
4. **The rules Chrome applies.** The harness confirmed these, and the video
   shows them:
   - A property with a running animation shows the animation's value.
   - When an animation starts, it shows its first keyframe at once.
   - When an animation stops, the property jumps to its resting value. The
     browser never transitions from an animated value.
   - Otherwise a change of resting value runs the element's own CSS
     transition from the value on screen. A property with no transition
     switches at once.
   - An animation that stays applied across a state change keeps its start
     time. A new duration therefore moves its phase at once.
   - A positive delay shows the resting value. `fill: both` holds the first
     and last keyframes.
5. **A model of those rules against the harness.** A Python model was
   compared with the harness, property by property and frame by frame. Every
   property agrees within frame jitter (±3 frames). The largest residuals sit
   at the one change time that had to be estimated (Sleep → Listening).
6. **The model against the video.** Over 1,968 frames, the model's
   white-core path differs from the tracked one by a median of 0.035 design
   units, or 0.09 px.
7. **The C# against the model.** `LivingCoreMotion` was ported line by line
   to Python and diffed against the model. All 103 frame fields agree to
   1e-9 in every one of these runs:
   - the video's timeline;
   - every reaction in every state;
   - the pointer;
   - a voice level;
   - reduced motion;
   - 40 randomised runs.

   The only difference is the voice wave under a real speech envelope, a
   HAMMOR addition (no envelope is connected today).
8. **The tests.** Every assertion in the three rewritten test files was run
   against that port: 90 cases, all pass. The C# was parsed and its member
   names cross-referenced against the repository and the .NET, WPF and
   toolkit reference assemblies.

## What changed

**Removed**, because the video does not do it:

- the 1.6 s staggered cascade;
- the Idle glance every 14 s;
- the thread lap on Success, and the cells' shiver on a new message;
- the glow's breathing;
- typing modulation of Listening (`InputSignal`);
- the 2 s spacing between glances, and merging message bursts;
- the board's restraint, which had:
  - hidden the halo in Speaking and Blocked's middle ring;
  - held the aura still in Thinking and stopped the crest's sway;
  - slowed Error, Blocked and Sleep.

**Now as in the reference:**

- every layer's own transition, and the reference's jumps (see
  [Transitions and jumps](#transitions-and-jumps));
- its keyframes, verbatim (`LivingCoreKeyframes.cs`);
- its loop tempos;
- the request glance in Blocked, every 6.4 s;
- each new message restarting the ripple and the glance;
- the aura leaning toward a panel in every state;
- Sleep's sediment, from the prototype's values.

Startup holds Wake for 2.6 s. A state set during that time shows when it ends.

## The ten states

Positions are fractions of the white core's zone (centre 63, 60.5; ±19 ×
±8 units). Loops run for as long as the state lasts.

| State | White core | Inside | Halo, rings, threads | Body, aura |
|---|---|---|---|---|
| **Idle** | Rest (76, 57.5), with a 9.6 s drift of under a unit; follows the pointer | Cells bob on their own clocks and the school turns over 140 s; stars turn over 240 s and shimmer | Halo breathes over 6.4 s; threads at 14, 22, 30 and 42 s; crest sways ±1.4° | Aura breathes 96–104% over 8 s |
| **Listening** | Centred, 112%, attention ring breathing | School drawn in to 92%; every star shimmers on one 1.6 s clock; lateral lines bright, flowing every 1.1 s | Three rings draw inward every 1.8 s, 0.6 s apart; the inner halo ring moves with the voice rhythm; inner threads at 8 and 12 s | Aura breathes |
| **Thinking** | Up and back (−0.35, −0.2), 78%, 55% light | Cell links with travelling signals; two orbit lanes; lens precesses ±5° at 60% light; bobbing stops; stars turn in 60 s | Threads at 8, 12, 16 and 22.4 s | Aura breathes |
| **Speaking** | Centred, pulsing from 125% to 147% over 1.6 s | Voice wave along the lower membrane | Three rings leave the halo every 1.8 s; the halo keeps breathing; threads pulse | Aura pulses with the voice |
| **Success** | Lifted, up and to the right (0.68, −0.69), 108% | Cells rise 3.5 together and settle | One green ring: 95% → 150%, gone by 45%; rim in the success tone | Warm bloom: 40% → 100% → 55% light, 95% → 112% → 100% size, over 3.2 s |
| **Warning** | Held back (0.37, −0.125), 92% | Lens narrowed to 80%; crest up 4 and still; bobbing stops; school turns over 280 s | Inner halo ring tightens to 97–98%; one amber segment pulses every 3.2 s; threads at half speed; rim in the warning tone | Aura 60% |
| **Blocked** | Low (0.16, 0.31), 94%; every 6.4 s glances 9 units toward the request and 2 up, holds, comes back | Bobbing stops; school turns over 600 s | Threads off; parked light at the top pulses over 3.2 s; gap ring | Aura 55% |
| **Error** | Right (0.68, 0.06), 90%, 60% light | Crack; cells spread to 118% at 50% light; stars 30%; lens 45% | Inner halo ring stutters on a 2.4 s pattern; fragments turn; threads off; rim in the danger tone | Aura 30% |
| **Sleep** | An ember at rest, no glow | School laid down: 38 lower, 40% tall; stars 15%; energy lines 30% | Threads off; halo and loops continue | Body down 9, 86% size, 60% light, breathing 86–87.5% over 9.6 s; aura 20% |
| **Wake** | A point of light swells to 150% at 12% and settles by 25% | Rim draws itself round (16–50%); lens sweeps in (30–60%); cells and stars bloom (48–80%) | Halo and threads arrive last (68–100%) | Aura rises (60–100%) |

The Wake ignition lasts 2.4 s, and Wake shows for 2.6 s before the state
underneath.

## Transitions and jumps

**Glides**, each on its own transition from the prototype's CSS:

| What | Time | Curve |
|---|---|---|
| White core position (pose, glance, pointer) | 0.45 s | settle |
| White core size | 0.6 s | settle |
| White core light | 0.6 s | ease |
| Depth: cells move 30% with the core, stars 10% against | 0.6 s | settle |
| Aura lean toward a panel | 1.2 s | settle |
| Shapes: lens narrowing, cell school, body | 1.2 s | settle |
| Light of the body, lens, threads, aura, stars, cells; the ember | 0.8 s | ease |
| State marks: rings, gap ring, links, orbits, fragments, crack, voice wave, speaking ring | 0.6 s | ease |

A change during a change starts from the frame on screen.

**Jumps the reference makes, reproduced on purpose.** The video shows them,
and smoothing them would make HAMMOR move unlike it:

1. The body drops into Sleep at once and lifts out of it at once: its
   breathing animation carries the sink and the shrink. Its light fades.
2. The white core's size goes straight to 125% when Speaking starts, and
   straight to the next state's size when it ends.
3. Overlays with their own rhythm appear at their first keyframe and vanish
   at once when the state ends: Success's bloom and ring, Warning's amber
   segment, Blocked's parked light and Listening's attention ring.
4. These switch at once, because the prototype gives them no transition:
   - the rim's tone (danger, success, warning);
   - the crest's lift;
   - the energy lines' light;
   - the glow;
   - the lateral lines' light.
5. Loops whose tempo changes with the state jump in phase at the change:
   - the threads;
   - the star field and its shimmer;
   - the cell school;
   - the lateral current.

   CSS keeps the animation's start time and changes only its duration.
6. The idle drift and Blocked's request glance end at once when their state
   ends. That is up to 0.8 units for the drift, and up to 9 for the glance if
   Blocked ends mid-glance.
7. Wake starts from nothing. When it ends, whatever its keyframes held goes
   straight to the next state: Wake → Thinking puts the core at 78% at once.
8. Some animations replace a shape while they run:
   - Thinking's precession and Wake's sweep replace Warning's lens
     narrowing;
   - Success's rise and Wake's bloom replace the cells' Listening, Error or
     Sleep shape (Error → Success resets the 118% spread at once).

If any of these looks wrong in HAMMOR, name it: each is one rule in
`LivingCoreMotion`, and smoothing one is a small, separate change.

## Real HAMMOR triggers

| State or reaction | Source | Notes |
|---|---|---|
| Listening | The composer has focus and text | Holds 1.2 s after typing stops. No microphone. |
| Speaking | `SpeechPlaybackMonitor` | Held 0.8 s after the last word. |
| Thinking | A chat turn in flight, until its reply arrives | |
| Success | A task completes, or a blocked task is approved | Held 3.2 s: one cycle of the bloom. |
| Warning | The approval prompt for a privileged tool call is open | `ApprovalPromptWatcher` only hears the dialog's Loaded and Closed events. It never shows, answers or reads a prompt. |
| Blocked | Any task in `TaskState.Blocked` | The request glance goes toward the leading side, where Tasks is. |
| Error | A failed turn, or a task that failed this session | Holds until the user acts. |
| Sleep | The window is minimised, or ten minutes pass with no input | |
| Wake | Leaving Sleep; the window restored; the app opening | Leaving Sleep always goes through Wake. |
| New message | An assistant reply is added to the conversation | See below. |
| Panel | The navigation pane opens on the chat page | See below. |
| Pointer | The pointer moves over HAMMOR's window, in Idle | See below. |

- **New message.**
  - One ripple leaves the membrane: 100% → 135% while its light falls from
    75% to 0, over 0.9 s.
  - For 1.2 s the white core glances toward the transcript and down. The
    target is (0.5, 0.9) of the zone, which is held on the zone's edge.
  - A second message restarts both.
- **Panel.**
  - For 2.4 s the white core glances toward the pane (0.95 of its zone
    sideways).
  - The aura leans 3.8 units its way in every state.
- **Pointer.**
  - The white core follows it inside its zone, 450 ms behind, ignoring moves
    under 24 px.
  - It lets go after 5 s of stillness, or 1.2 s after the pointer leaves the
    window.

**Who moves the white core**, in the prototype's order:

1. Sleep and Wake rest.
2. Speaking, Listening and Thinking hold their pose.
3. Otherwise a message glance goes first, then a panel glance.
4. Then Blocked, Warning, Error and Success hold their pose.
5. Then, in Idle only, the pointer.
6. Otherwise, rest.

## Where it still differs from the reference

1. **HAMMOR's own geometry.** It has 26 stars, 14 cells, 4 threads and 3
   halo rings, as approved on the build board; the prototype has 18, 13, 3
   and 2. The fourth thread (42 s, counter-clockwise), the third halo ring,
   the fourth star clock and the third bob clock follow the same rules as
   their neighbours.
2. **The app opens with Wake.** The prototype opens in Idle.
3. **Success lasts one 3.2 s cycle.** The prototype loops it until the next
   click.
4. **Triggers are HAMMOR's events**, as in the table above. In right-to-left
   layouts the glances follow the physical side.
5. **Reduced motion is stricter.** Transitions shorten to 0.4 s, and
   reactions and the pointer are ignored. The prototype's switch only stops
   animations.
6. **Frame rate.** HAMMOR draws:
   - at the display rate, up to 60 fps, while its window is active;
   - at 30 fps for a 160 px core or smaller, or a window in the background;
   - at 20 fps after 45 s in the background;
   - at 10 fps settled in Sleep.

   The prototype always draws at the display rate.
7. **A real voice envelope**, when one is connected (none is today), sets the
   Speaking pulse, the voice wave and the strength of Listening's rings.
8. **No camera mode.** There is no camera, microphone or speech-to-text.

## Reduced motion

Windows Settings › Accessibility › Visual effects › **Animation effects**
off:

- Every animation stops, as with the prototype's reduced-motion switch.
- Each state shows its resting values:
  - Listening's rings held in place at full light;
  - Success's ring at full size;
  - Blocked's parked light at full;
  - Sleep's body sunk and shrunk.
- State changes blend over 0.4 s, then frames stop.
- There is no ignition, ripple, glance, lean or pointer follow.

**Settings › Appearance › Living Core motion** shows which mode is active.

## Performance

`LivingCoreFramePolicy` decides when and how often the core draws:

| Situation | Frames |
|---|---|
| Minimised, hidden or unloaded | None |
| Reduced motion, settled state | None |
| Window active, hero core | Up to 60 fps, also on 120 and 144 Hz displays |
| Core 160 px or smaller (the 112 px header) | 30 fps |
| Window in the background | 30 fps, then 20 fps after 45 s |
| Settled in Sleep, window shown | 10 fps (Guardrails board: "10 asleep") |

- **Nothing is allocated per frame.** Each animation is a precomputed table
  of keyframes.
- **One frame costs the same as before.** The engine evaluates about 55
  animation slots and 50 transitions per frame. That is plain arithmetic
  over fixed arrays.
- **The scene is unchanged.** It is the same retained visuals, moved by
  transforms, opacity and dash offsets. Unchanged values are skipped.
- **The presenter adds no polling.** It uses only one-shot timers, one per
  hold.

## Files

| File | Change |
|---|---|
| `Presence/LivingCoreKeyframes.cs` (new) | The prototype's `@keyframes`; CSS animation and transition semantics |
| `Presence/LivingCoreMotion.cs` | Rewritten as the CSS emulation |
| `Presence/LivingCoreLook.cs`, `LivingCoreLooks.cs` | One flat set of resting values per state, from the prototype's CSS |
| `Presence/CubicBezierEasing.cs` | CSS `linear` and `ease`; an exact path for linear curves |
| `Presence/LivingCore.cs`, `LivingCorePresenter.cs`, `Views/ChatPage.xaml` | Typing modulation (`InputSignal`) removed |
| `Presence/LivingCoreReaction.cs` | Docs |
| `tests/HAMMOR.App.Tests/Presence/` | Motion, life and design tests rewritten to the reference |

No Core, Infrastructure, security, permission, chat, speech, task or approval
behaviour changed. The scene, the frame, the state resolver and the frame
policy are unchanged.

### Tests

App tests go from 451 to 466:

| File | Before | After |
|---|---|---|
| `LivingCoreMotionTests` | 18 | 24 |
| `LivingCoreLifeTests` | 57 | 65 |
| `LivingCoreDesignTests` | 31 | 32 |

The rewritten tests assert what the video shows:

- the jumps listed above;
- the tempo changes;
- Blocked's 6.4 s glance;
- the message restart;
- the aura's lean in held states;
- Sleep's resting values under reduced motion.

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

Expected: 0 errors; **903 tests** pass (Core 437, App 466), 0 failed, 0
skipped.

Only the Living Core tests:

```powershell
dotnet test tests\HAMMOR.App.Tests -c Release --filter "FullyQualifiedName~HAMMOR.App.Tests.Presence"
```

First open **Settings › Appearance**: "Living Core motion" must say **Full
motion**, or every check below shows a still core by design.

### Side by side with the video

Play the reference video next to HAMMOR and compare each moment:

1. **App start, against 25.45–28.05 s.**
   - A point of light swells past full and settles.
   - Then the rim draws round, then the lens, then the cells and stars.
   - The halo and threads, and the aura, come last.
   - After 2.6 s it hands over to Idle.
2. **Idle, against 28.05–29.47 s.**
   - The white core sits forward and high with a small drift.
   - Everything turns and breathes.
   - Move the pointer: the white core follows inside its zone, cells with
     it, stars against it.
3. **Listening (type in the composer), against 15.3–18.5 s and 31.5 s on.**
   - The core centres at 112%, with the attention ring.
   - Rings draw inward.
   - The lateral lines brighten and run fast, and the stars shimmer quickly.
4. **Thinking (send), against 2.49–7.10 s.**
   - The core moves up and back, small and dim.
   - Links and signals run between the cells, and the lens rocks.
   - The threads jump forward in phase as they speed up.
5. **New message (the reply lands).**
   - One ripple leaves the membrane.
   - Back in Idle, a later reply draws a 1.2 s glance down toward the
     conversation.
6. **Speaking (a spoken reply), against 18.54–21.44 s and 29.47–31.54 s.**
   - The core snaps to 125%, glides to the centre and pulses.
   - Rings leave the halo, the voice wave runs, and the aura and threads
     pulse.
7. **Warning (a privileged tool call asking for approval), against
   21.44–23.64 s.**
   - The core holds back.
   - The lens narrows and the crest lifts at once.
   - The amber segment pulses and the threads slow.
8. **Blocked (a task waiting for approval), against 9.07–13.23 s.**
   - The core sits low.
   - Every 6.4 s it glances toward the sidebar and back.
   - The threads are off and parked at the top, with the gap ring.
9. **Success (approve it), against 7.10–9.07 s.**
   - The core lifts.
   - The warm bloom and the green ring, and the cells rise.
   - One cycle, then back.
10. **Error (a failed turn), against 23.64–25.45 s.**
    - The core dims to the right.
    - The crack and the danger rim appear, the inner ring stutters, and the
      cells spread.
11. **Sleep (minimise and restore, or ten quiet minutes), against
    13.23–15.3 s.**
    - The body drops and shrinks at once.
    - The ember appears, and the school lies down at the bottom.
    - Restoring plays Wake.
12. **Panel.** On the chat page with the pane collapsed, open it.
    - The core glances toward it, and the aura leans its way.
    - In Thinking the aura still leans, and the core stays put.
13. **Reduced motion.** Turn Animation effects off.
    - Every state still reads, held still.
    - The Appearance row says reduced.
14. **Conversation header.** The 112 px core shows the same states at
    30 fps.

### Visual checks needing a real screen

- **The jumps above.** They match the video. If one reads as a glitch in
  HAMMOR rather than as the reference's character, say which.
- **Sleep's sediment.** The prototype's band (38 down, 40% tall) puts the
  lowest cells over the rim at some rotations. This is in the video too.
- **10 fps asleep.** The slow loops step at 10 fps. If that shows, Sleep
  needs 20 fps.
- **Small sizes.** The warning segment, the success ring and the ember at
  112 px.

### Performance

Use the CPU and GPU commands in [LIVING-CORE.md › Performance](LIVING-CORE.md#performance):

- focused Idle;
- Speaking;
- another window focused, for 30 s and after 45 s;
- Sleep after ten quiet minutes (10 fps);
- minimised (about the baseline).

Report the numbers. Nothing has been measured yet.
