# Decision — a modifier is a named role, so engine code never names the key that fills it

**Date:** 2026-08-17
**Status:** LANDED. Builds; verified at runtime that both declarations parse into the right group
(temporary probe in `AddModifier`, removed).
**Scope:** `ArctisAurora.EngineWork` (`InputModifier`, `NamedModifier`, `IKeybindMapChild`,
`InputHandler`, `GestureMatcher`), `...Controls.Text` (`TextInputActions`),
`...Controls.Text.Document` (`DocumentEditorControl`), `Periodic/…/Inputs/InputMap.xml`.

## Context

Shift-to-extend-a-selection was `InputHandler.instance.IsKeyDown(Keys.LeftShift) ||
IsKeyDown(Keys.RightShift)`, written twice — once in `TextInputActions.Move` for arrows, once in
`DocumentEditorControl.ShiftHeld` for click and drag. Shift is the industry standard and that is not
the point: **an editing key was hardcoded in engine C#**, which is exactly what the XML keybind
system exists to prevent (user, 2026-08-17). A host could not rebind it.

## Decisions

### 1. The unit is a *role*, not a key

`InputModifier` is an engine enum — one member, `Extend` — and XML binds keys to it:

```xml
<NamedModifier Modifier="Extend" Key="LeftShift" />
<NamedModifier Modifier="Extend" Key="RightShift" />
```

Engine code asks `InputHandler.IsModifierDown(InputModifier.Extend)` and never learns which key
answered. Any one declaration being down satisfies the role, so left and right shift are two
declarations rather than a special case — and `KeybindModifier`'s list is AND-ed, so this OR could
not have been expressed as an ordinary modifier anyway.

An enum rather than a string name, matching the settings rework: the query is typed end to end, the
schema enumerates the legal roles, and a typo is a compile error instead of a silent false.

### 2. It had to be queryable state, because clicks are not keybinds

The obvious shape was a second set of actions — `Text.SelectLeft` and friends, bound with
`<Modifier Key="LeftShift"/>`, using only mechanisms that already exist and getting shadowing for
free. It was rejected on two counts. It is 8 new actions and 16 XML entries (two per direction, one
per shift). And it **cannot reach shift+click or shift+drag at all**: caret placement comes from the
hit-test path through `UICollisionHandling.ResolveOnClick`, not from `ActivateKeybinds`, so there is
no action for a modifier to attach to. A queryable role covers both paths with one declaration.

**Also rejected: a latched `Text.ExtendSelection` action** bound `<Continuous/>`, setting a flag the
engine clears each tick. One action and two XML lines, and it covers clicks — but it needs a defined
clear point in the main tick, and any tick that misses the clear leaves the editor stuck in select
mode. That is the same failure class as the stuck drag in [[document-selection]] decision 9, which
had just been fixed.

### 3. Modifiers are grouped exactly like keybinds

`GestureMatcher` holds `_modifierGroups` beside `_groups` and `_activeModifiers` beside
`_activeBinds`, and `SetActiveGroup` swaps both. A keybind group is a mode, so switching to a game
group rebinds `Extend` along with everything else rather than leaving an editor binding live.

`AddModifier` mirrors `AddKeybind` line for line instead of being folded into a shared generic
helper — the two differ in which "active" field they refresh, and the abstraction would be longer
than the duplication.

### 4. `KeybindMap` needed a second kind of child

Its `AllowedChildren` was `typeof(KeybindDefinition)`. It is now `typeof(IKeybindMapChild)`, a new
marker interface on `KeybindDefinition` and `NamedModifier` — the same pattern `Keybind` already uses
with `IKeybindChild`. A separate interface rather than reusing `IKeybindChild`, or `<Press/>` would
become a legal child of the root.

`ParseXML` walks every root element as a keybind, so `NamedModifier` is a branch at the top of that
loop. Note it would **not** have thrown without the branch: with no `Trigger` and no `Action` it
would have registered a silent phantom bind on the default `Keys` value.

### 5. Nothing falls back to shift

A role no XML declares reads as never down, so extend-select simply does nothing. No engine-side
default, because a default is the hardcoding this removes. The cost is that a host that never
declares `Extend` loses shift-selection with no diagnostic — the schema enumerating the roles is what
makes it discoverable.

## Verified

- Builds clean.
- `Periodic` boots to all three threads, no stderr.
- A temporary `Console.WriteLine` in `AddModifier` confirmed both declarations parse into the
  `InputMap` group as `Extend = LeftShift` / `Extend = RightShift`. Probe removed. This mattered
  because a clean boot alone would **not** have caught the branch failing (see decision 4).
- Generated `InputTypeSchema.xsd` carries the `InputModifier` enumeration and `NamedModifier` in
  `KeybindMap`'s sequence.
- **Not GUI-verified:** that extend-selection still works through the new path.

## Still open

- **One member.** `Extend` is the only role. Word-wise motion (Ctrl+Arrow) and delete-word
  (Ctrl+Backspace) are the obvious next ones and are the reason this is an enum rather than a bool.
- **The rest of the engine still reads raw keys.** This closes the two text-editing sites only;
  anything else calling `IsKeyDown` with a literal has the same problem.

Related: [[document-selection]], [[engine-side-text-input]], [[document-structural-editing]]
