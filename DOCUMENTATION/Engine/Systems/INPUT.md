---
date:
Status: Current
tags:
  - Engine
Linker:
  - "[[Arctis Aurora]]"
System:
  - "[[INPUT]]"
Dependencies:
  - "[[GLFW]]"
  - "[[XSD / Data Layer]]"
Implementors:
  - "[[InputHandler]]"
  - "[[KeyStateTracker]]"
  - "[[GestureMatcher]]"
---
## System
The input system is responsible for catching hardware events from the OS (keyboard, mouse, scroll), converting them into engine-native key states, and evaluating gesture-based keybinds every engine tick. It is a passive exposure system — it catches and exposes input state, it does not route events outward to controls or systems. Other systems read from it or have their actions invoked by it.

The general hierarchy is:
`InputHandler`
	`KeyStateTracker`
	`GestureMatcher`
		`KeybindDefinitions`
			`Modifiers`
			`Conditions`

### Input Handler
The `InputHandler` is the top-level singleton that owns the entire input pipeline. It holds the `KeyStateTracker`, the `GestureMatcher`, character input queues, mouse position, and scroll delta. GLFW window callbacks are wired directly into methods on this class — it is the only place raw OS input enters the engine.

The `InputHandler` exposes three separate input streams, each handled by its own path:
- **Keyboard** → `ProcessKeyboard()` → maps `Silk.NET.GLFW.Keys` to Aurora's `Keys` enum → enqueues a `RawInputEvent` into the `KeyStateTracker`
- **Mouse buttons** → `ProcessMouseClick()` → maps `MouseButton` to `Keys` (mouse buttons live in the same enum as keyboard keys) → same enqueue path
- **Character input** → `ProcessCharInput()` → raw Unicode codepoints pushed into `charInputWriteQueue` — this is a separate stream from keyboard events because character input is layout-aware (gives you `ä` instead of just `A`), used by text editing controls

Mouse position and scroll are handled separately. `ProcessMouseMove()` writes directly to a static `mousePos` field. `ProcessScrollWheel()` accumulates into `scrollDeltaWrite` which gets swapped into `scrollDelta` once per tick.

Bootstrap happens via `[A_XSDActionDependency("InputHandler.LoadInputs", "Bootstrap")]`. This calls `ParseXML` which loads all `*.xml` files from the inputs directory, constructs keybind definitions, and hands them to the `GestureMatcher`. After parsing, the default keybind group is activated.

#### Per-Tick Flow
Every engine tick, `ActivateKeybinds()` runs on the main thread. The sequence is:
1. Double-buffer swap the character input queues (under lock)
2. Copy scroll delta from write buffer, zero the write buffer
3. `keyTracker.Update()` — swap raw event queues, process events into key states, update hold durations
4. `gestureMatcher.Update()` — evaluate all active keybind definitions against current key states
5. If a modified keybind fired this frame, suppress the character input queue (prevents `Ctrl+S` from also producing an `s` character)

#### Double Buffering
Both the raw key event queue and the character input queue are double-buffered. GLFW callbacks run on the OS callback thread and write to the write queue. Once per tick on the main thread, write and read queues are swapped under a lock, then the read queue is drained. This means input is never lost between frames and the callback thread never blocks on the main thread for longer than a pointer swap.

The scroll wheel uses a simpler pattern — accumulate into `scrollDeltaWrite`, copy to `scrollDelta` at tick start, zero the write buffer. No lock needed because the write is a float accumulation and the read only happens after the swap.

### KeyStateTracker
The `KeyStateTracker` maintains per-key state for every key the engine has seen. It stores a `Dictionary<Keys, KeyStateEntry>` where each entry tracks:
- `isDown` — whether the key is currently held
- `justPressed` / `justReleased` — per-frame edge flags, reset every tick before processing new events
- `downTimestamp` / `upTimestamp` — exact time of the last press/release (using `Engine.totalTime`)
- `holdDuration` — how long the key has been continuously held, incremented by `deltaTime` each tick
- `tapCount` — how many consecutive presses have happened within the `tapWindow` (0.3s default). Decays to 0 when the window expires
- `consumed` — set by the `GestureMatcher` when a keybind fires for this trigger, used to prevent less-specific binds from also firing and to suppress character input

The tracker is purely a state machine. It doesn't know about keybinds. It converts raw `Down`/`Up` events into rich per-key state that conditions and matchers can query.

#### Update Sequence
`Update(currentTime, deltaTime)`:
1. Swap write/read queues under lock
2. Reset per-frame flags (`justPressed`, `justReleased`, `consumed`) on all existing entries
3. Process each `RawInputEvent` in order:
   - **Down**: if not already down → set `justPressed`, `isDown`, record `downTimestamp`, update tap count (increment if within `tapWindow`, otherwise reset to 1)
   - **Up**: if currently down → set `justReleased`, clear `isDown`, record `upTimestamp`
4. Clear the read queue
5. Update hold durations for all keys that are currently down
6. Decay tap counts — if a key is up and `tapWindow` has elapsed since last tap, reset `tapCount` to 0

### Keybind Definitions
A `KeybindDefinition` represents a single input binding. It has:
- **Trigger** — the primary `Keys` value that initiates evaluation (e.g. `S`, `MouseLeft`, `F5`)
- **Action** — the `Action` delegate to invoke when the keybind fires. Resolved at XML parse time via `[A_XSDActionDependency]` attribute scanning across all assemblies
- **Modifiers** — a list of `KeybindModifier`, each specifying a `Keys` value that must be held simultaneously (e.g. `LeftControl`). All modifiers must be down for the keybind to be considered
- **Conditions** — a list of `InputCondition` objects that define *when* relative to the trigger key's state the action should fire. If no conditions are specified, the default behavior is `Press` (fire on key down)

Mouse and keyboard share the `Keys` enum. Mouse buttons are `MouseLeft`, `MouseRight`, `MouseMiddle`, `MouseButton4`–`MouseButton8`. This means keybinds can freely mix mouse and keyboard — a `Trigger="MouseLeft"` with `Modifier Key="LeftShift"` works exactly like any keyboard combo.

`KeybindDefinition` also contains the GLFW-to-Aurora key mapping via two static methods: `MapKey()` for keyboard and `MouseKey()` for mouse buttons. These are exhaustive switch expressions that translate from `Silk.NET.GLFW.Keys` / `Silk.NET.GLFW.MouseButton` to the engine's `Keys` enum. Unknown keys map to `Keys.unknown`.

### Conditions
Conditions are the composable building blocks that define gesture behavior. They are `InputCondition` subclasses, each with an `Evaluate()` method that returns one of four states:
- `Idle` — not relevant, nothing happening
- `Ongoing` — in progress but not ready to fire (e.g. hold timer counting up)
- `Triggered` — fire now
- `Canceled` — was ongoing but failed (e.g. released too early for a hold)

A keybind can have multiple conditions. They are AND-combined: all must return `Triggered` in the same tick for the action to fire. If any returns `Canceled`, all conditions reset.

Current condition types:

| Condition | Fires when | XML Attributes |
|-----------|-----------|----------------|
| `Press` | The frame the key goes down | — |
| `Release` | The frame the key goes up | — |
| `Hold` | Once after holding for threshold | `Threshold` (default 0.3s) |
| `HoldRelease` | On release, only if held longer than threshold | `Threshold` (default 0.3s) |
| `MaxHoldTime` | On release, only if held *less* than threshold | `Threshold` (default 0.5s) |
| `MultiTap` | On Nth consecutive press within tap window | `Count` (default 2) |
| `Continuous` | Every tick while held | — |
| `Repeat` | On press, then repeats after delay at rate | `Delay` (default 0.35s), `Rate` (default 0.03s) |
| `Toggle` | Alternates active/inactive state on each press | — |
| `HoldContinuous` | Every tick, but only after holding for threshold | `Threshold` (default 0.3s) |
| `Chord` | Requires another specific key to be held | `Key` |

Conditions are stateful — `Repeat` tracks its accumulator internally, `Hold` tracks whether it has already fired, `Toggle` tracks its on/off state. They get reset when the keybind fires or when evaluation is canceled.

All condition types are tagged with `[A_XSDType]` and their parameters with `[A_XSDElementProperty]`, so they appear in the generated XSD schemas and are fully declarable from XML.

### GestureMatcher
The `GestureMatcher` owns the evaluation loop. It holds all keybind definitions organized by named groups and references the `KeyStateTracker` for key state queries.

#### Groups
Keybind definitions are organized into groups. Each XML file in the inputs directory becomes a group named after the file (e.g. `default.xml` → group `"default"`). Only one group is active at a time. Swap with `SetActiveGroup(string)` — this is how you switch keybind contexts for different game modes or editor states. When a group is set, the matcher's `_activeBinds` list is pointed at that group's keybind list directly.

#### Evaluation Loop
`Update(deltaTime)` runs once per tick after `KeyStateTracker.Update()`:

The loop runs in two passes to handle priority:
- **Pass 0** — only keybinds that have modifiers (Ctrl+S, Shift+Click, etc.)
- **Pass 1** — only keybinds without modifiers (bare key presses)

This ensures modified binds always evaluate first. Within each pass:

1. Skip if this trigger key was already consumed by a more specific (more modifiers) bind — checked via `IsShadowed()` which verifies whether a superset bind exists and all its modifiers are currently held
2. Look up the trigger key's state from the tracker. Skip if it doesn't exist (key was never pressed)
3. Check all modifiers are held. Skip if any are not
4. Evaluate all conditions via `EvaluateConditions()` — AND-combination logic as described above
5. If `Triggered`: invoke the action delegate, mark the trigger key as `consumed`, track it in the consumed set

The shadowing system prevents `S` from firing when `Ctrl+S` fires in the same frame. A bind is shadowed if there exists another bind for the same trigger key that has strictly more modifiers, all of the simpler bind's modifiers are a subset of the complex bind's modifiers, and all of the complex bind's modifiers are currently held.

#### Character Input Suppression
After the evaluation loop, `ShouldSuppressCharInput()` checks whether any modified keybind had its trigger consumed this frame. If so, the character input queue is cleared — this prevents `Ctrl+S` from also producing an `s` character in any listening text control.

### XML Format
Keybinds are defined in XML files placed in the inputs directory (`Paths.XMLDOCUMENTS_INPUTS`). Each file is one group. The root element is a `KeybindMap` containing `Keybind` elements.

Structure:
```xml
<KeybindMap>
  <Keybind Trigger="S" Action="SaveDocument">
    <Modifier Key="LeftControl" />
    <Press />
  </Keybind>

  <Keybind Trigger="MouseLeft" Action="SelectItem">
    <MultiTap Count="2" />
  </Keybind>

  <Keybind Trigger="A" Action="MoveLeft">
    <Repeat Delay="0.35" Rate="0.03" />
  </Keybind>

  <Keybind Trigger="G" Action="GrabMode">
    <Hold Threshold="0.5" />
  </Keybind>
</KeybindMap>
```

- `Trigger` — the `Keys` enum value string
- `Action` — resolved by scanning `[A_XSDActionDependency]` methods across all assemblies by method name
- Child `Modifier` elements specify required held keys
- Child condition elements (`Press`, `Release`, `Hold`, `Repeat`, etc.) are parsed by type name and their XML attributes are reflected onto the condition object's properties/fields
- If no condition elements are present, `Press` is the implicit default

All keybind types, conditions, and modifiers are part of the XSD system via their `[A_XSDType]` and `[A_XSDElementProperty]` attributes. The schemas are auto-generated — do not hand-edit them.

### Keys Enum
Aurora defines its own `Keys` enum independent of GLFW. It covers:
- A–Z letters
- Num0–Num9 (number row)
- Numpad0–Numpad9 + numpad operators
- F1–F25
- Navigation (arrows, Home, End, PageUp, PageDown, Insert, Delete)
- Modifiers (LeftControl, RightControl, LeftShift, RightShift, LeftAlt, RightAlt, LeftWin, RightWin)
- Special (Space, Enter, Tab, Backspace, Escape, CapsLock, etc.)
- Symbols (Apostrophe, Comma, Minus, Period, Slash, Semicolon, etc.)
- Mouse buttons (MouseLeft, MouseRight, MouseMiddle, MouseButton4–MouseButton8)
- `AnySymbol` — a special wildcard value; intended for text input listeners
- `unknown` — fallback for unmapped keys
