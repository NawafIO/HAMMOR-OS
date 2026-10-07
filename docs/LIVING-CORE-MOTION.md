# Step 4: Living Core motion

**Status: branch `claude/living-shell-sidebar`. UNVERIFIED ON WINDOWS.** It
was written in a Linux container without the .NET SDK, so it has not been
compiled, run or seen on a screen. What was checked:

- the C# was parsed;
- the XAML and resx files were checked as XML, and English and Arabic have
  the same keys;
- the WPF members used were checked against the .NET 8 reference assemblies;
- the motion engine's changes were mirrored in the Python port of the engine,
  and the new tests' numbers were checked against it (bounds, transitions,
  pointer behaviour, frame counts).

None of this replaces a compiler or a real screen.

## Why it looked still

The approved design and state model were already in place, but in Idle
almost everything except the light threads moved too little to see. At the
default 450 px hero (2.25 px per design unit):

| Idle element | Before | Step 4 |
|---|---|---|
| White core drift | ±0.8 units, about 1.8 px | Two slow sines per axis, up to 1.7 × 1.1 units: the hero board's 1.5% limit, never past it. Mean speed about 4 px/s |
| Stars | One rigid field turning once in 240 s, about 2 px/s | The same turn, plus the four star groups wandering against each other (depth parallax, up to 2.6 units) |
| Cells | A one-axis bob of 1.3 units | The bob, plus a two-axis wander per group (up to 2 units) and a glow that dims each group by up to 22% on its own clock |
| Halo rings | ±1.4% scale, 75–100% light | ±2.2% scale, 60–100% light, still on the 6.4 s clock a third apart |
| Threads | Constant light | Breathe 16% of their light over 5.2 s; their orbits are unchanged |
| Pupil attention | One glance about every 14 s | The same glances, and the white core now turns toward the pointer (below) |

Unchanged:

- the geometry, colours and layers;
- every state's pose, scale and light;
- the 1.6 s cascade and its order;
- the glance schedule and the Blocked request glance;
- the presenter's state mapping.

The thread, energy, lens and shimmer clocks keep their approved periods.

## Behaviour

- **Always alive in Idle.** The tests check that the white core is never
  still for two seconds, that the star groups never move in step, and that
  every wandering star and cell stays inside the membrane.
- **Pointer attention (Idle only).**
  - While the pointer moves over HAMMOR's window, the white core turns toward
    it, up to about half its zone. It follows with a short lag, holds 2.4 s
    after the pointer stops, then settles back over about a second.
  - A pointer on the core itself barely moves it, and the idle glances wait
    while it attends.
  - It uses only the pointer's direction from the core, only while the core
    is animating, and stores and reports nothing. There is no camera,
    microphone or speech-to-text.
  - In Arabic the drawing is not mirrored, so the offset is turned back to
    physical left and right.
  - Listening, Thinking, Speaking, Blocked and Error ignore the pointer: they
    keep their approved poses.
- **States.** The six states keep the approved looks. The new life follows
  each look's own strengths, so it fades in and out through the cascade:
  - **Listening**: full parallax and wander; the core stays centred and held.
  - **Thinking**: parallax at 60%; the cells hold for the links and signals.
  - **Speaking**: parallax at 50%; the cells hold.
  - **Blocked**: parallax at 25%, near stillness.
  - **Error**: parallax and wander at 50%.

  A test checks that the settled states differ clearly from each other.
- **Smooth, no bounce.**
  - Every new motion is a sine or a first-order follow, and neither can
    overshoot.
  - Every easing token, including the ring curves, is checked to never pass
    its target.
  - A state change continues from the frame on screen, and the tests check
    that no frame jumps (at most 3.5 design units of core movement per frame
    at 60 fps on the cascade's settle curve, against pose changes of up to 20
    units).
- **Reduced motion.**
  - The rule is unchanged: Windows Settings › Accessibility › Visual effects
    › **Animation effects** off holds the core still.
  - Wander, parallax, cell glow, ring and thread breathing, and pointer
    attention all stop, and every state keeps its pose and light.
  - **Settings › Appearance › Living Core motion** now says which mode is
    active, read only. If the core looks frozen on Windows, check this first.

## Performance

`LivingCoreFramePolicy`, a pure class with tests, decides when and how often
the core draws:

| Situation | Frames |
|---|---|
| Minimised, hidden, unloaded | None: the per-frame callback is unhooked |
| Reduced motion, settled state | None, after the last frame |
| Window active, hero core | Up to 60 fps, also on 120 and 144 Hz displays (they draw every second refresh) |
| Window active, core 160 px or smaller (the 112 px conversation header) | 30 fps |
| Window in the background | 30 fps |
| Window in the background for 45 s or more | 20 fps |

- **No new allocations per frame.** The new values go into preallocated frame
  arrays. The scene applies them to existing visuals: one offset for each of
  the four star groups, and one opacity for each of the three cell groups.
  Unchanged values are skipped, as before.
- **The pointer costs almost nothing.** A move only stores a number, and only
  while the core is animating in Idle. It never asks for an extra frame.
- **Clocks resume.** The new wanders run on time that advances in clamped
  steps, so coming back from minimised continues the motion instead of
  jumping.

## Files

| File | Change |
|---|---|
| `Presence/LivingCoreMotion.cs` | Visible idle life: drift, parallax, cell wander and glow, ring and thread breathing, pointer attention |
| `Presence/LivingCoreDesign.cs` | The wander clocks and the drift limit |
| `Presence/LivingCoreLook.cs`, `LivingCoreLooks.cs` | `StarParallax` strength for each state |
| `Presence/LivingCoreFrame.cs`, `LivingCoreScene.cs` | Star group offsets and cell glow, applied to the existing visuals |
| `Presence/LivingCoreFramePolicy.cs` (new) | The frame budget |
| `Presence/LivingCore.cs` | Uses the policy; tracks background time; reports the pointer |
| `Presence/LivingCoreMotionStatus.cs` (new), `Views/SettingsPage.xaml(.cs)` | The read-only motion row in Appearance |
| `Localization/Strings*.resx` | 4 new strings in each language; nothing existing changed |
| `tests/HAMMOR.App.Tests/Presence/` | `LivingCoreLifeTests` (28), `LivingCoreFramePolicyTests` (20), `LivingCoreStringsTests` (4); the idle-glance test's drift bound updated to the new drift; ring easings added to the overshoot test; the determinism snapshot covers the new values |

No Core, Infrastructure, security, Shell, Settings behaviour, Chat, Projects,
Search or AI provider code changed.

## Windows verification

```powershell
git fetch origin claude/living-shell-sidebar
git checkout claude/living-shell-sidebar
git log -1 --oneline
dotnet build -c Release
dotnet test -c Release
.\src\HAMMOR.App\bin\Release\net8.0-windows\HAMMOR.exe
```

Expected: 0 errors; **796 tests** pass (Core 437, App 359: 307 before plus 52
for Step 4), 0 failed, 0 skipped.

First open **Settings › Appearance**: "Living Core motion" must say **Full
motion**. If it says reduced, turn Windows' Animation effects on, or every
check below will show a still core by design.

Then check, at the default window size and maximised:

1. **Idle on the Home.** Watch it hands-off for a minute.
   - The white core drifts slowly and glances about every 14 s.
   - The stars move against each other, with depth.
   - The cells wander and softly dim and brighten.
   - The rings breathe and the threads orbit and breathe.
   - Nothing should flash, jump or bounce, and nothing should leave the
     membrane.
2. **Pointer.**
   - Move the pointer around the core: the white core turns toward it, a
     little behind, never past its zone.
   - Stop: it holds a moment, then settles back.
   - Move the pointer out of the window: it settles.
   - In Arabic it still turns toward the physical side of the pointer.
3. **States.**
   - **Listening:** type in the composer.
   - **Thinking:** send a message.
   - **Speaking:** speak a reply.
   - **Blocked:** a task waiting for approval.
   - **Error:** a failed turn, for example with no network.

   Each must be clearly different, and every change must be a smooth glide
   with no pop.
4. **Reduced motion.**
   - Turn Animation effects off: the core holds still in its state and the
     Appearance row says reduced.
   - Turn it on again and the life returns.
5. **Conversation header.** The 112 px compact core is alive too.

### Performance

Use the CPU and GPU commands in [LIVING-CORE.md › Performance](LIVING-CORE.md#performance):

- focused;
- another window focused, for 30 s and then after 45 s (20 fps);
- minimised (should be about the baseline);
- Settings (the baseline).

Report the numbers. Nothing has been measured yet. On a 120 or 144 Hz display,
PresentMon should show the core's frames at about 60 or 72 per second, not
the display rate.
