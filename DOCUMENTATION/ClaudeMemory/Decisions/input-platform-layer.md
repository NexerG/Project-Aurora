# Decision — input is platform-neutral in front of the GestureMatcher: one Post queue, GLFW is a backend

**Date:** 2026-10-16
**Status:** LANDED as L1–L4 of a keybind redesign aimed at Windows/Linux/Mac now and mobile later. L2 was replaced the same day (see decision 3). L5 (touch gestures) deferred with mobile.
**Scope:** `ArctisAurora.EngineWork` — `InputHandler` (`Post`, `Drain`, `ApplyScroll`, `PointerState`, `primaryPointer`), `RawInputEvent`, `InputEventKind`, `PointerKind`, `KeyStateTracker` (`HeldModifiers`), `GestureMatcher` (`commandIsControl`, `Held`, `Down`, `Command`, `SameModifier`); `ArctisAurora.EngineWork.Rendering` — `GlfwInput`, `TouchpadInput`; `ArctisAurora.Core.UI` — `UIEngine` (`Poll`, `Event`), `PointerEvent`, `TitleBarControl`; `ArctisAurora.Core.Testing.TestContext`; `Thorium/Tests` — `TextInput.CommandIsControl`

## What changed
- **L1 ingress:** NEW `GlfwInput` (internal static): `Wire(RenderWindow)` sets the GLFW cursor-pos, mouse-button, key, char, cursor-enter and scroll callbacks; `Engine.WireInput` calls `GlfwInput.Wire(window)`. `MapKey(Silk.NET.GLFW.Keys)` and `MouseKey(MouseButton)` MOVED here from `KeybindDefinition` (were public static there), now private.
- GLFW `SuperLeft/SuperRight` map to `Keys.LeftSuper/RightSuper` (were `LeftWin/RightWin`; `LeftSuper/RightSuper` were never produced before, so `KeyStateTracker.IsModifier` never saw the Win/Cmd key). The Cmd≡Ctrl equivalence relies on this.
- REMOVED from `InputHandler`: `ProcessCharInput`, `ProcessMouseMove`, `ProcessMouseClick`, `ProcessKeyboard`, `ProcessScrollWheel`. NEW: `public static void Post(in RawInputEvent)` (locked double buffer `_posted`/`_draining`), private `Drain()` (first thing in `ActivateKeybinds`, before `SettleScroll`), private `ApplyScroll(RenderWindow, dx, dy)` (the old `ProcessScrollWheel` body). Regions now "event ingress", "pointers", "scroll input", "scroll gestures".
- `RawInputEvent` widened: `kind` (NEW enum `InputEventKind`: Key, Button, PointerMove, Scroll, ScrollContact, Text), `key`, `action`, `timestamp`, `window` (`RenderWindow`), `pointerId`, `pointerKind` (NEW enum `PointerKind`: Mouse, Touch, Pen), `position` (window pixels), `delta` (scroll), `codepoint`. Its constructor was removed.
- `KeyStateTracker`: own queue/lock and `EnqueueEvent` REMOVED; `Update(List<RawInputEvent> events, double currentTime, double deltaTime)` takes the drained list and only reads Key/Button events.
- **L3 pointers:** NEW `InputHandler.PointerState` (nested public class): `id`, `kind`, `window`, `position`, `down`/`pressed`/`released` (button bit masks indexed like `PointerEvent.leftButton`=0 / `rightButton`=1, from `Keys.MouseLeft..MouseButton8`), `taps[]`, `lastTapAt[]`, `JustPressed(int)`, `JustReleased(int)`. `InputHandler.primaryPointer` = first pointer seen. A move on the primary pointer still writes `RenderWindow.mousePos`, so its readers are unchanged. Mouse buttons still also go to the key tracker, so `IsKeyDown(Keys.MouseLeft)`, `<Keybind Trigger="MouseLeft">` and `TitleBarControl`'s `keyTracker...tapCount` still work.
- `UIEngine.Poll` reads `primaryPointer.JustPressed/JustReleased` and `taps[button]` instead of `keyTracker.GetState(Keys.MouseLeft/MouseRight)`. `UIEngine.Event` fills NEW `PointerEvent.pointerId` and `PointerEvent.kind`.
- **L2 Cmd≡Ctrl on a Mac:** NEW `GestureMatcher.commandIsControl` (public static bool, defaults to `OperatingSystem.IsMacOS()`, tests flip it). While it is set, `Held(Keys)` lets Cmd (LeftSuper/RightSuper) fill a Ctrl modifier through the new private `Down(Keys)` plus `internal static Command(Keys)`. `Command` maps LeftControl↔LeftSuper and RightControl↔RightSuper. `SameModifier(Keys, Keys)` counts the pair as one. `IsDown(InputModifier)` now checks via `Held` rather than raw `_tracker.IsDown`, so Cmd fills Word, Copy and Zoom on a Mac. Twins also apply to roles now, which is harmless because declarations list both sides.
- `KeyStateTracker.HeldModifiers` (rebind capture) records Super as Control while the flag is set and drops duplicates, so a rebind saved on a Mac works on Windows.
- **L2 earlier form, REMOVED the same day:** `InputPlatform`, `InputHandler.platform`, the `Platform` attribute on `NamedModifier` and `<Keybind>` with the parser skip, `InputModifier.Primary`, `KeybindModifier.Role`, and `GestureMatcher` `Held(KeybindModifier)`, `SameModifier(KeybindModifier, KeybindModifier)`, `Fills`, `KeyOf`. `SettingsWindow` labels are back on `m.key`. The three `InputMap.inputs.xml` files are byte-identical to before the change: plain `<Modifier Key="LeftControl"/>`, and Thorium's Word/Copy/Zoom roles are on Left/RightControl with no platform. The test `TextInput.PrimaryRole` is gone.
- **L4 touchpad:** `TouchpadInput` public `down`/`lifts` REMOVED (`down` is a private field); on a contact change it posts `InputEventKind.ScrollContact` with `RawAction.Down/Up`. `InputHandler` keeps private `contactDown`/`contactLifts`, set in `Drain`; `ApplyScroll`/`SettleScroll` read those where they read the `TouchpadInput` statics.
- `TestContext` (`Point`, `Raw`, `Scroll`, `Type`) and `TitleBarControl.OnPointerPress` (the synthesized left release after a caption drag) call `InputHandler.Post`. `TestContext.Handle()` removed.
- Test: NEW `TextInput.CommandIsControl` in `Thorium/Tests`, listed in `TextInput.tests.xml`: with the flag off, Super+C does not copy; with it on, RightSuper+C copies; the test then restores the flag.

## Why these choices

**One ingress, and backends are callers of it.**
`InputHandler.Post` is the only entry. No `IInputBackend` interface: a backend is just code that calls `Post` (GLFW now; a mobile or WM_POINTER source later), so an interface would be an abstraction with one implementation. Timestamps stay `Engine.totalTime`; scroll keeps its Stopwatch `ScrollClock`. Clock unification left out.

**Rejected: keeping per-kind paths.**
Keys through the tracker queue, chars through a static queue, mouse position written straight onto `RenderWindow.mousePos`, scroll summed onto the window: the order between kinds is lost, which a touch stream cannot afford. Every path was also GLFW-typed (`WindowHandle*`, `MouseButton`, `InputAction`, `KeyModifiers`), so a host without GLFW (Android/iOS) could not feed the matcher at all.

**Drain order preserves the old behaviour.**
GLFW callbacks run inside `PollEvents` right before `ActivateKeybinds`, so draining first and then running `SettleScroll` matches the old callbacks-then-settle order. Test input posted from `Main.Logic` reaches `UIEngine.Poll` on the next `Main.Input`, as before.

**One keybind for every platform; the maps never name a platform (user: "the platform its for should not be there in keybinds. one keybind for all platforms. stuff like ctrl and mac's command should be mapped together").**
The equivalence lives in the matcher and is on for Mac only (option a, user's choice). Extends [[named-input-modifiers]].
- **Rejected: the earlier `Primary` role plus `Platform` attribute** — 6 declarations per role, and the maps described the platform
- **Rejected (b): equivalence on every platform** — on Windows Super is the Win key, and Win+V or Win+Z would open the OS feature *and* fire paste or undo
- **Rejected (c): a Qt-style backend remap of Cmd to Control on Mac** — holding Ctrl and Cmd and releasing one would make the tracker think Ctrl was released while the other is held, and a bind could never tell them apart
- **Cost:** on a Mac, word motion is Cmd+Arrow rather than Option+Arrow, and Ctrl+key also fires Ctrl binds there

**Pointers: the primary pointer drives the UI; `PointerEvent` carries `pointerId`/`kind`.**
L3 was built ahead of need with only the mouse feeding it, because swapping `UIEngine.Poll`'s input source later is the expensive part (user approved including it). Agreed touch-to-UI rule for later (F4(a), user's choice), recorded but not built:
- the primary touch contact drives the same Press/Release/Tap/Move
- a long-press arrives as a secondary-button press/release, so every right-click consumer works
- a one-finger pan over a scroller becomes Scroll with an end
- **Rejected: touch as its own phase set every control opts into** — every existing control would need touch code

**Scroll contact is an event.**
`InputHandler` no longer reads Win32 statics; the Windows Raw Input HID source is one producer. Linux and Mac keep the 0.15 s wheel-quiet fallback (no native phase source yet). Extends [[scroll-gesture-end]].

**Mobile is deferred (user, 2026-10-16: "Defer mobile to a later stage, but for the design stage keep that in mind").**
Room left: `Post`, `pointerId`/`pointerKind`. Open fork F3 for when it returns: bindable touch gestures as a new `<Gesture Kind="Tap|DoubleTap|LongPress|Pan|Pinch|Swipe" Fingers Direction Action>` element in `KeybindMap` evaluated by `GestureMatcher` (recommended, carries pinch/pan magnitudes) vs new `Keys` members like `TwoFingerTap` (cheaper, but `Keys` cannot carry continuous values). Planned with it: a `TouchGestures` recognizer, `TestContext.Touch`, `InputSettings` `<LongPress Delay>` / `<TouchSlop>`, tests Touch.Tap / LongPressOpensMenu / PanScrolls / Pinch.

## Verified
- **Test-verified on Windows:** `_Build/test.sh` full suite 326 passed, 1 failed (Boot, the pre-existing baseline error, no new error kinds), 1 skipped. No `[Vulkan]` lines. `InputTypeSchema.xsd` is back to its pre-change form.
- **NOT checked:** a real Mac; the `GlfwInput` callback path by hand (test runs install no OS callbacks), so real typing/clicking/scrolling through GLFW is **not GUI-verified**; the touchpad contact path not checked live.

## Known gaps
- Mac untested on hardware
- Key labels show "LeftControl" on a Mac, not "Cmd"
- `GlfwInput` path not GUI-verified (type, click, double-click the title bar, and touchpad-scroll the calendar once)
- Android/iOS: no change; no mobile backend, soft keyboard or IME
- `Keys.LeftWin/RightWin` are now never produced (GLFW Super → `LeftSuper`); a user's saved `KeybindOverride` naming `LeftWin` would no longer match (none of the shipped maps use either)
- Other engine code still reads raw keys via `IsKeyDown` (unchanged)
- `primaryPointer` is null until the first pointer event; `UIEngine.Poll` skips buttons until then

Related: [[named-input-modifiers]], [[scroll-gesture-end]], [[engine-side-text-input]], [[keybind-intent-plan]], [[engine-testing]]
