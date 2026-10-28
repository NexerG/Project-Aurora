# Decision — an idle app waits for OS events instead of running frames

**Date:** 2026-09-27
**Scope:** `ArctisAurora.Core.Threading` — `FrameScheduler` (`Run`, `RequestFrameAt`, `NextWake`, `Resumed`, `WaitForFrame`), `MainSystem` (`Pending(out)`, `IdleWait`), `RenderSystem.Tick`, `IdleSetting`; `ArctisAurora.Core.Animation.AnimationSystem.Advance`; `ArctisAurora.Core.UI` — `CaretControl.OnTick`, `Control.RestartEffect`, `TextRunControl` glyph loop, `Effects.Duration`; `ArctisAurora.EngineWork` — `Engine.Post`, `Engine.HasPosted`, `KeyStateTracker.AnyDown`; `ArctisAurora.Core.Diagnostics.ProfileScenario`; data `Thorium/Data/XML/Settings/Threading.settings.axml`, `Carbon/Data/XML/Settings/Threading.settings.axml`

## What changed
- **Setting** `<Threading><Idle Wait="true"/></Threading>`, default `false`. On in Thorium and Carbon. Thorium also sets `<FrameCap MaxFps="120"/>`.
- **End of frame** (`FrameScheduler.Run`): if `Idle.Wait` and `!MainSystem.Pending(out wakeAt)` (`wakeAt` = `FrameScheduler.NextWake()`, +infinity if none), main calls `IdleWait(wakeAt)` — `glfwWaitEvents` (no wake time) or `glfwWaitEventsTimeout` (until the wake time) — instead of `WaitOut`. The frame after sets `Resumed`.
- **Pending** = awake animation tracks, rows in `AnimationDone` or `LayoutDirty`, posted work, any key or mouse button down, or a wake time already reached. Input itself is not tracked: whatever ends the wait runs one full frame.
- **Wake times** — `FrameScheduler.RequestFrameAt(totalTime)` stores the time in a list (`_wakes`, `double[16]`, `_wakeCount`); `NextWake()` swap-removes every stored time `<= Engine.totalTime` and returns the earliest left. A time at or before the current `Engine.totalTime` is ignored (2026-09-27); within 0.5 ms of a stored time it merges into it; with all 16 taken the latest stored time is replaced if the new one is earlier, else the new one is dropped (2026-10-28). Main thread only. Callers: the caret (next blink toggle), `Control.RestartEffect` and `TextRunControl` (a GPU effect's end — start, per-glyph stagger, `Effects.Duration`), the console's `Wait` (`CommandConsole`).
- **`--test` and `--profile-scenario` set `idle.wait = false` in memory** (`TestRunner.Session`, `ProfileScenario` constructor) — a run keeps going with nobody at the keyboard. The scenario's per-tick `RequestFrameAt(+1 ms)` was removed with it (user, 2026-09-27).
- **`Engine.Post`** also calls `glfwPostEmptyEvent`, so work posted from another thread (`FolderPicker`) ends the wait.
- **Render draws once per Main frame**, idle or not: `RenderSystem.Tick` blocks on `FrameScheduler.WaitForFrame` (an `AutoResetEvent` set after `EndFrame`, and once more when `Run` exits) until Main's epoch moves. The epoch counts as drawn only when every visible window presented — a pass that only built a window's GPU resources, or whose `Draw` returned without advancing `frameCounter` (acquire out-of-date → rebuild), runs again at once. Minimized windows (0-area) count as presented.
- **`AnimationSystem.Advance`** uses `dt = 0` on a `Resumed` frame.

## Why these choices

**A frame is pending on state, not on input.** Every input already ends the wait and gets a whole frame; what that frame starts (a spring, a tween, a relayout) is what keeps the next frames coming. Flagging every GLFW callback was rejected: it would only duplicate that, across seven callbacks.

**No heartbeat (user, 2026-09-27).** A missed wake source then shows as "it only updated when I moved the mouse", which is easy to spot. A periodic wake would hide it.

**Default off (user, 2026-09-27).** Ticking entities do not keep frames coming — only wake times do — so a game on the engine would freeze between inputs. Tools opt in.

**Animation gets `dt = 0` after a wait, Main keeps real time.** A track woken by the input that ended a 10 s wait would otherwise step 10 s at once and land on its end. Main's `deltaTime`/`totalTime` must stay wall-clock: the caret counts real seconds, and GPU effects progress against `totalTime`.

**A wake time already reached is ignored (user, 2026-09-27).** It can never produce a frame, so it is not stored. While wake times shared one earliest-wins slot it also erased any later request made earlier in the same frame (the list, below, removed that hazard; the check stays). `TextRunControl` asks for a finished effect's end on every emit; probed through the console's `Wait` ([[dev-console]]), 0.56 replaced 8.07 at `totalTime` 7.77 and main slept until the next OS event. **Rejected:** only `TextRunControl` checking its end is ahead — `Control.RestartEffect` with a zero-length effect asks for "now" too; the console keeping frames running while it waits — the engine bug stays.

**Wake times are a small list, not one slot (user picked option A, 2026-10-28).** The slot kept only the earliest time, and `MainSystem.Pending` cleared it once a frame reached it: a later request made while an earlier one sat in the slot was dropped, and when the earlier one was cleared at frame end main slept in `glfwWaitEvents()` with no timeout until an OS event. The console hit it whenever a `Wait` followed another `Wait` (an `--exec` line is parked on an internal `Wait 0`, then `Wait 3000` asks for a later time in the frame the `Wait 0` wake is served): `Wait 3000; Quit` took 28 s on 1 of 3 launches, and `--exec "…; Tex.ExportPdf; …"` exports intermittently never finished. A fixed `double[16]` with linear scans keeps it allocation-free. The 0.5 ms merge is there because callers re-request every frame with float noise (the caret recomputes its next blink toggle from a phase each frame); without it a 120 fps caret would fill the list with near-duplicates. A full list keeps the 16 earliest; a dropped later request is the old failure, but it needs more than 16 distinct pending times at least 0.5 ms apart and nothing today comes close. **Rejected:** option B, the console `Pump.OnTick` re-asking for its earliest `resumeAt` every tick — the loss happens inside one frame: the stale slot is still occupied when both the original request and the re-ask are made, and it is cleared only at frame end, so the re-ask is dropped too. B would have needed an extra rule in `RequestFrameAt` (replace a stored time already reached), and every other caller would still have had to re-ask. The idle discipline is unchanged: no heartbeat, ticking entities still do not keep frames coming.

**Render follows Main's epoch in every mode.** Redrawing without a new Main frame reproduces the same image (the GPU's `totalTime` comes from Main), so the wait also removes Render's own 2 ms pacing spin. Presentation is detected through `frameCounter` rather than a `bool` from `Renderer.Draw`, so the Vulkan pipeline's signatures stay as they are (user chose this over changing `Draw`).

## Measured (2026-09-27, Debug, 16 logical cores, idle 10 s sample)

| Idle | cores | % of 16 |
|---|---|---|
| Thorium uncapped (Task Manager, user) | ~2.7–3.4 | 17–21 |
| Thorium `MaxFps=120` | 0.61 | 3.8 |
| Thorium idle wait, no focused note | 0.002 | 0.01 |
| Thorium idle wait, caret blinking | 0.008 | 0.05 |
| Carbon idle wait | 0.006 | 0.04 |

**A control easing itself in `OnTick` must request a wake time each frame it is still moving (2026-10-10).** `LayoutDirty` does not survive the frame — `Main.Layout` drains the row the same frame `OnTick` set it, so `Pending` sees nothing and main sleeps. `CalendarControl` (scroll settle, all-day-lane easing) is a caller of `RequestFrameAt(Engine.totalTime + 0.001)`; `--test` cannot detect a miss since it runs with `idle.wait = false`. See [[ontick-easing-sleeps-under-idle-wait]]. **Rejected:** `Animations.Tween` for the calendar — it needs an `[A_Animatable]` pool column per float.

**Wake list verified (2026-10-28):** builds clean. Not test-verifiable — `--test` runs with idle waiting off. Against a normally launched Debug Thorium, `--exec "Wait 3000; Quit"` on a one-paragraph .tex, 10 launches: the wait took 3.15–3.18 s every time (before: 3.3 s, 3.3 s, 28 s on three launches). Idle CPU with a .tex open and nothing happening: 0.016 CPU-seconds over 10 s, the same order as the table above. **NOT GUI-verified.**

## Known gaps
- **Overflow beyond 16 distinct pending times** silently keeps the earliest 16 (no log).
- **`TexEditorControl` asks for the next frame with `RequestFrameAt(Engine.totalTime)`** in three places (`Recompile`, `ResolvePages` twice). That is always ignored by the reached-time rule; nothing is shown to stall, it relies on `LayoutDirty`.
- **A `Loop`/`PingPong` GPU effect freezes while idle** — only `Once` effects request a wake time. None is used today.
- **A secondary window's OS window is reaped on Main's next frame.** Render frees its GPU side without waking Main; in testing the Vaults window's close still went away with no further input, but nothing guarantees a frame follows.
- **A key stuck `isDown`** (release landed on another window) keeps frames running at the cap until it is pressed again.
- **`WaitOut` still spins its last 2 ms** while frames are running.
- **Verified:** builds clean; Thorium and Carbon boot, draw, and close through the X; Thorium's caret blinks while idle (captures); File menu opens; Vaults window opens, draws and closes. **NOT GUI-verified:** hover springs, file-tree expand/collapse, typing, key repeat, resize, Alt+F4, the folder picker's cross-thread post, the Settings window, `--profile-scenario`.

Related: [[frame-scheduler]], [[animation-core]], [[render-window-owns-the-swapchain]]
