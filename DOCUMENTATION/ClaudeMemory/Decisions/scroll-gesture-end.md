# Decision — a scroll gesture has an end, from touchpad finger lifts

**Date:** 2026-10-10
**Scope:** `ArctisAurora.EngineWork.Rendering` — `TouchpadInput`, `AGlfwWindow` (`FrameProc`, `CreateWindow`); `ArctisAurora.EngineWork` — `InputHandler` (region "scroll gestures"); `ArctisAurora.Core.UI` — `Control.OnPointerScrollEnd`, `UIEngine` (`scrollOwner`, `Poll`, `Forget`), `CalendarControl`; `ArctisAurora.Core.Testing.TestContext.Scroll`; `Thorium/Tests` — `Planner.CalendarSnap`

## What changed
- NEW `TouchpadInput` (internal static): `Register()` (RegisterRawInputDevices, HID digitizer page 0x0D / touchpad usage 0x05, hwndTarget null so it follows keyboard focus; called once from `AGlfwWindow.CreateWindow()`), `Handle(IntPtr rawInput)` (one WM_INPUT), a private `down` (fingers on pad); a contact change posts `InputEventKind.ScrollContact` with `RawAction.Down/Up`. Updated 2026-10-16: the public `down`/`lifts` statics were removed, `InputHandler` keeps private `contactDown`/`contactLifts` set in `Drain` — see [[input-platform-layer]].
- Parsing via hid.dll: `HidP_GetCaps`, `HidP_GetValueCaps` (finger link collections = those holding Contact ID 0x51; Contact Count 0x54), `HidP_GetUsageValue` (contact count), `HidP_GetUsages` (tip switch 0x42 per finger collection). A report with contact count > 0 opens a frame; finger slots are consumed until the count is met (hybrid-mode reporting); frame complete sets `down` = any tip.
- `AGlfwWindow.FrameProc` passes `WM_INPUT` (0x00FF, const `rawInput`) to `TouchpadInput.Handle` and still forwards it to GLFW's proc.
- `InputHandler` region "scroll gestures": consts `wheelQuiet` 0.15 s, `momentumGap` 0.05 s, `momentumFloor` 0.15 (GLFW scroll units); public `scrollEnded` (true for the tick a gesture finished); private `SettleScroll()` called first in `ActivateKeybinds`.
- `ApplyScroll` (was `ProcessScrollWheel`, now reached through `InputHandler.Post`) opens/tracks the gesture, marks it touched when a delta arrives with a finger down, ends it when a post-lift delta falls below `momentumFloor`, and swallows the rest of the momentum tail (deltas while no finger is down, arriving within 50 ms of each other) after an end.
- Gesture end: wheel = 0.15 s since the last delta; touchpad = lifted and 50 ms with no delta (or a below-floor delta). Touching the pad again un-lifts the gesture (momentum stops; the next lift ends it). Uses `FrameScheduler.RequestFrameAt` so idle frames wake for the end. Clock is Stopwatch seconds.
- `Control.OnPointerScrollEnd()` — new virtual, default no-op.
- `UIEngine`: private `scrollOwner` (the control whose `OnPointerScroll` returned true; `SolveScroll` now walks up itself instead of `Dispatch` capturing the consumer). `Poll` calls `scrollOwner.OnPointerScrollEnd()` once when `InputHandler.instance.scrollEnded`; `Forget` clears it.
- `CalendarControl`: removed `lastScroll`, `snapDelay`, `snapSeconds` and the `RequestFrameAt` in `OnPointerScroll`. Added `snapDuration` (0.15 s) and fields `forward`, `settling`, `settleFrom`, `settleTo`, `settleStart`. The `OnPointerScrollEnd` override starts the settle (`settleTo` = 1 if the last delta moved forward, else 0). `OnTick` eases by elapsed time since `settleStart` (cubic ease-out, fixed duration) and steps a day/week on reaching 1. A new scroll or a snapped step (Step/ShowSpan/GoToday set `unsnapped` false) cancels it.
- NEW `TestContext.Scroll(Control, Vector2 delta)` — moves the pointer to the control's centre and posts a Scroll event through `InputHandler.Post` (the real wheel path; was `ProcessScrollWheel`).
- Supersedes the 2026-10-09 calendar snap (0.15 s of quiet, per-frame exponential ease) described in [[planner]].

## Why these choices

**Snap on gesture end, not on a quiet timer.**
The user scrolls a touchpad arbitrarily slowly, so any timeout fires mid-gesture; the slide now holds wherever it is left while scrolling. GLFW has no scroll phases, so the end comes from Windows Raw Input HID (finger tip switches).

**Rejected DirectManipulation** (what Chrome uses; gives Running/Inertia/Ready phases natively): COM interop, a viewport and update manager, several times the code, and it takes over the window's touchpad scrolling.

**"Fingers lifted" alone is not the end.**
Measured on the user's touchpad (2026-10-09, 14 gestures, temporary probe logs since removed):

| Gesture | After lift |
|---|---|
| slow lift (9 of 14) | no wheel events |
| small flick | 4-7 events over 74-140 ms (-0.26 to -0.03) |
| medium flick | 21 events over 336 ms (-3.05 to -0.04) |
| hard flick | 47-60 events over 727-949 ms (+-3 to 4.4 down to +-0.04) |

Flicks end silently or when a finger touches again; event spacing ~16 ms.

**Momentum, then snap** (chosen by the user from three options).
Momentum keeps moving the view after the lift; the end is a post-lift delta below 0.15 or 50 ms of silence; the snap goes to the next whole day/week in the direction of travel and the remaining tail is ignored. Rejected: snapping at lift and discarding momentum (a flick would only travel as far as the fingers did); projecting the landing point from the first tail deltas (needs a guessed model of Windows' inertia decay).

**End detection lives in `InputHandler`/`UIEngine`, not the calendar**, so any scroller can use `OnPointerScrollEnd`.

**Mouse-wheel fallback keeps a 0.15 s quiet period** (a wheel has no lift); this also covers non-precision touchpads and non-Windows hosts.

**A fixed-duration ease replaces the per-frame exponential ease.**
After an idle wait the first frame's deltaTime was ~0.15 s, which moved ~92% (1 - e^-2.5) of the distance in one frame: the "jumpy" snap the user reported.

**The settle keeps frames coming (2026-10-10).** Each `CalendarControl.OnTick` still mid-settle (t < 1) requests `RequestFrameAt(Engine.totalTime + 0.001)`; otherwise under `<Idle Wait>` the ease advanced only on unrelated wakes and jumped. See [[ontick-easing-sleeps-under-idle-wait]], [[idle-frames]]. Verified: no regressions in tests; NOT GUI-verified.

**Cost:** while a finger rests on the pad, raw reports (~125 Hz) wake idle frames; cursor motion already does.

## Known gaps
- The all-day-lane easing is exponential on `Engine.deltaTime`; the first frame after an idle wait carries the whole idle gap as dt, so a lane opening right after idle can still take most of its step at once (AnimationSystem uses dt = 0 on a `Resumed` frame; the calendar does not). The settle is unaffected (fixed duration from `settleStart`).
- Raw input follows keyboard focus: scrolling over an unfocused Thorium window gets no touchpad lift. The gesture is marked touched only if a finger contact was down (`contactDown`), so such scrolls are treated as wheel scrolls (0.15 s quiet), the old timer behaviour.
- `momentumFloor` 0.15 and `momentumGap` 50 ms are tuned from one device's log.
- Direction-of-travel snap: even a tiny forward nudge settles a whole day/week forward.
- Vertical scrollers (`ScrollableControl`) receive `OnPointerScrollEnd` but ignore it.
- macOS/Linux have no phase source; wheel fallback only.
- Preparsed HID buffers (NativeMemory) are never freed (one per device, process lifetime).
- **Verified:** test-verified. `_Build/test.sh` full run 306 passed, 1 failed (Boot, the baseline default-sampler error), 1 skipped; no `[Vulkan]` lines. `Planner.CalendarSnap`: a slide holds for 1 s without a gesture end; one frame after `OnPointerScrollEnd` it has moved partially (eases, no jump); within 0.3 s forward settles on the next day, column-aligned; backward settles on the day before; a wheel scroll through `TestContext.Scroll` (real `ProcessScrollWheel` -> `SettleScroll` -> `UIEngine` -> calendar) ends on its own and settles; Month 0.6-week forward settles on the next week. The HID parsing was checked live against the user's touchpad with probe logs (5 finger slots, contact count present, clean lifts, no false lifts mid-swipe) before the momentum logic existed.
- **NOT GUI-verified:** the finished touchpad path (lift + momentum + swallow) in a running app; non-precision touchpads; multiple monitors/windows with keyboard focus on a different window than the one under the cursor.

Related: [[input-platform-layer]], [[planner]], [[ui-engine-stack]], [[date-time-settings]]
