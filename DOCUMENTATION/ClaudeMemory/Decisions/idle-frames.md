# Decision — an idle app waits for OS events instead of running frames

**Date:** 2026-09-27
**Scope:** `ArctisAurora.Core.Threading` — `FrameScheduler` (`Run`, `RequestFrameAt`, `Resumed`, `WaitForFrame`), `MainSystem` (`Pending`, `IdleWait`), `RenderSystem.Tick`, `IdleSetting`; `ArctisAurora.Core.Animation.AnimationSystem.Advance`; `ArctisAurora.Core.UI` — `CaretControl.OnTick`, `Control.RestartEffect`, `TextRunControl` glyph loop, `Effects.Duration`; `ArctisAurora.EngineWork` — `Engine.Post`, `Engine.HasPosted`, `KeyStateTracker.AnyDown`; `ArctisAurora.Core.Diagnostics.ProfileScenario`; data `Thorium/Data/XML/Settings/Threading.settings.xml`, `Carbon/Data/XML/Settings/Threading.settings.xml`

## What changed
- **Setting** `<Threading><Idle Wait="true"/></Threading>`, default `false`. On in Thorium and Carbon. Thorium also sets `<FrameCap MaxFps="120"/>`.
- **End of frame** (`FrameScheduler.Run`): if `Idle.Wait` and `!MainSystem.Pending(ref _wakeAt)`, main calls `glfwWaitEvents` (no wake time) or `glfwWaitEventsTimeout` (until the wake time) instead of `WaitOut`. The frame after sets `Resumed`.
- **Pending** = awake animation tracks, rows in `AnimationDone` or `LayoutDirty`, posted work, any key or mouse button down, or a wake time already reached. Input itself is not tracked: whatever ends the wait runs one full frame.
- **Wake times** — `FrameScheduler.RequestFrameAt(totalTime)` keeps the earliest; `Pending` drops it once a frame's `Engine.totalTime` reaches it. A time at or before the current `Engine.totalTime` is ignored (2026-09-27). Main thread only. Callers: the caret (next blink toggle), `Control.RestartEffect` and `TextRunControl` (a GPU effect's end — start, per-glyph stagger, `Effects.Duration`), the console's `Wait` (`CommandConsole`).
- **`--test` and `--profile-scenario` set `idle.wait = false` in memory** (`TestRunner.Session`, `ProfileScenario` constructor) — a run keeps going with nobody at the keyboard. The scenario's per-tick `RequestFrameAt(+1 ms)` was removed with it (user, 2026-09-27).
- **`Engine.Post`** also calls `glfwPostEmptyEvent`, so work posted from another thread (`FolderPicker`) ends the wait.
- **Render draws once per Main frame**, idle or not: `RenderSystem.Tick` blocks on `FrameScheduler.WaitForFrame` (an `AutoResetEvent` set after `EndFrame`, and once more when `Run` exits) until Main's epoch moves. The epoch counts as drawn only when every visible window presented — a pass that only built a window's GPU resources, or whose `Draw` returned without advancing `frameCounter` (acquire out-of-date → rebuild), runs again at once. Minimized windows (0-area) count as presented.
- **`AnimationSystem.Advance`** uses `dt = 0` on a `Resumed` frame.

## Why these choices

**A frame is pending on state, not on input.** Every input already ends the wait and gets a whole frame; what that frame starts (a spring, a tween, a relayout) is what keeps the next frames coming. Flagging every GLFW callback was rejected: it would only duplicate that, across seven callbacks.

**No heartbeat (user, 2026-09-27).** A missed wake source then shows as "it only updated when I moved the mouse", which is easy to spot. A periodic wake would hide it.

**Default off (user, 2026-09-27).** Ticking entities do not keep frames coming — only wake times do — so a game on the engine would freeze between inputs. Tools opt in.

**Animation gets `dt = 0` after a wait, Main keeps real time.** A track woken by the input that ended a 10 s wait would otherwise step 10 s at once and land on its end. Main's `deltaTime`/`totalTime` must stay wall-clock: the caret counts real seconds, and GPU effects progress against `totalTime`.

**A wake time already reached is ignored (user, 2026-09-27).** It could never produce a frame — `Pending` drops a reached time at frame end — but it took the one earliest-wins slot and so erased any later request made earlier in the same frame. `TextRunControl` asks for a finished effect's end on every emit; probed through the console's `Wait` ([[dev-console]]), 0.56 replaced 8.07 at `totalTime` 7.77 and main slept until the next OS event. **Rejected:** only `TextRunControl` checking its end is ahead — `Control.RestartEffect` with a zero-length effect asks for "now" too; the console keeping frames running while it waits — the engine bug stays.

**Render follows Main's epoch in every mode.** Redrawing without a new Main frame reproduces the same image (the GPU's `totalTime` comes from Main), so the wait also removes Render's own 2 ms pacing spin. Presentation is detected through `frameCounter` rather than a `bool` from `Renderer.Draw`, so the Vulkan pipeline's signatures stay as they are (user chose this over changing `Draw`).

## Measured (2026-09-27, Debug, 16 logical cores, idle 10 s sample)

| Idle | cores | % of 16 |
|---|---|---|
| Thorium uncapped (Task Manager, user) | ~2.7–3.4 | 17–21 |
| Thorium `MaxFps=120` | 0.61 | 3.8 |
| Thorium idle wait, no focused note | 0.002 | 0.01 |
| Thorium idle wait, caret blinking | 0.008 | 0.05 |
| Carbon idle wait | 0.006 | 0.04 |

## Known gaps
- **A `Loop`/`PingPong` GPU effect freezes while idle** — only `Once` effects request a wake time. None is used today.
- **A secondary window's OS window is reaped on Main's next frame.** Render frees its GPU side without waking Main; in testing the Vaults window's close still went away with no further input, but nothing guarantees a frame follows.
- **A key stuck `isDown`** (release landed on another window) keeps frames running at the cap until it is pressed again.
- **`WaitOut` still spins its last 2 ms** while frames are running.
- **Verified:** builds clean; Thorium and Carbon boot, draw, and close through the X; Thorium's caret blinks while idle (captures); File menu opens; Vaults window opens, draws and closes. **NOT GUI-verified:** hover springs, file-tree expand/collapse, typing, key repeat, resize, Alt+F4, the folder picker's cross-thread post, the Settings window, `--profile-scenario`.

Related: [[frame-scheduler]], [[animation-core]], [[render-window-owns-the-swapchain]]
