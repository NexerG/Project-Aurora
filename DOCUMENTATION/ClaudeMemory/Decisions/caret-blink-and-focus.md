# Decision — the caret blinks itself, and a run tells the document when focus left

**Date:** 2026-08-23
**Status:** LANDED. Builds clean (0 errors; no warning on any touched file). **Not GUI-verified.**
**Scope:** `ArctisAurora.Core.UISystem.Controls.Text.Document` — `CaretControl`, `DocumentControl`,
`TextRun`.

## What was asked

"Animate the caret to blink. Remove the caret if the editor becomes unfocused." Nothing ever hid the
caret before: `DocumentControl.Arrange` placed it on every pass while `caretRun != null`, so clicking
the vault browser left a solid caret sitting in a note nobody was typing into.

## Decisions

### 1. The blink lives in `CaretControl`, on `OnTick`

Every control is an `Entity`, so `Engine.Interpolate` already calls `OnTick()` on it — on the main
thread, which is also the `UIControls` pool's owner thread, so writing `alpha` from there needs no
new plumbing. `CaretControl` is the first control in the engine to override `OnTick`; there is still
no tween or animation system, and this deliberately does not start one.

Consequence, chosen knowingly (user, 2026-08-23): `TextBoxControl` uses the same class, so the
note-name prompt and the rename field blink too. Their caret is collapsed with
`Arrange(LayoutRect.Empty)` when the box is not editing, so an invisible one toggling `alpha` costs
two pool writes a second and shows nothing.

### 2. On/off, not a fade

`alpha` is `1` or `0` at `0.53s` each way (the Windows caret rate). Rejected: easing the alpha, which
writes a pool row **every tick** instead of twice a second, for an effect nobody asked for. The write
is guarded by `if (alpha != value)` so the idle cost is exactly the two flips.

`phase` accumulates `Engine.deltaTime` and is read modulo the full cycle rather than being flipped by
a countdown, so a long tick cannot desynchronise the wave from the clock.

### 3. CPU alpha, not the frame time already in set 0

`UI.frag` has `engine.totalTime` / `engine.wrappedTime` from the renderer's global set
([[gpu-global-frame-data]]), so the blink could have been pure shader with zero per-frame CPU. It
would have cost a `ControlData` field, a shader edit and the `.spv` copied to all three projects, for
an animation that is two writes a second. Rejected (user, 2026-08-23). The shader path is the right
answer for per-glyph animation later, not for this.

### 4. Focus is pushed, not polled

Chosen by the user (2026-08-23) over a poll in `OnTick`. `TextRun` overrides `OnContextAdded` /
`OnContextRemoved` for `"ActiveControl"` and forwards to `DocumentControl.RegainFocus` /
`LoseFocus` — the same shape `FieldLine` uses to raise `TextBoxControl.onBlur`.

The pair is consistent only because **`SetCaret` repoints `UICollisionHandling.activeControl` at the
caret's run**, directly, on every call. So whenever a caret exists the run holds the context, and the
run is therefore the control that will be told when it goes. A click past the text makes the *block*
or the editor active for one instant — `SolveLMBPress` runs before `ResolveOnClick` — and `SetCaret`
overwrites it with the run a moment later, which is also what makes the note-name prompt's
`_restoreActive` restore land back on a `TextRun` and re-show the caret.

That direct field write is also why keyboard movement between runs raises nothing: it bypasses
`Context.Set`, so no `OnContextRemoved` fires and the caret cannot blink out mid-arrow.

### 5. The focus scope is the editor, not the document

`LoseFocus` walks up from the *new* `activeControl` looking for `DocumentControl.parent` — the
`DocumentEditorControl` — and keeps the caret if it meets it. Scoping to `DocumentControl` itself
would have been shorter and wrong: `ScrollableControl` parents its thumb beside the content, not
inside it, so dragging the scrollbar would have taken the caret down with it.

This works only because `SetActiveControl` assigns the field **before** raising `OnContextRemoved`,
the invariant `TextBoxControl` already depends on ([[inline-rename]] decision 3).

### 6. Hidden is `alpha = 0`, not a collapsed rect

`TextBoxControl` hides its caret with `Arrange(LayoutRect.Empty)`. Here that would mean a `focused`
flag on `DocumentControl`, a guard in `Arrange`, and an `InvalidateArrange` on every focus change.
The caret already owns `alpha` for the blink, so `Blur()` reuses it and the whole thing stays inside
`CaretControl`: no arrange pass runs when focus moves.

## Where the caret is made solid again

A caret that vanishes mid-keystroke reads as input lag, so the phase restarts from the top of the
cycle on every caret placement:

- `DocumentControl.SetCaret(run)` — the funnel every click, arrow, delete, undo and block split ends
  in, and where the caret is created.
- `DocumentControl.TypeChar` — typing does **not** go through `SetCaret`; `WriteChar` bumps
  `cursorPosition` itself.

`TextBoxControl` is left alone: it has no single caret-move funnel to hook, so its caret blinks but
does not go solid while you type.

## Left out

- **Alt-tab.** No GLFW focus callback is registered anywhere, so OS focus is not observable yet. The
  caret survives the app losing focus.
- **The selection highlight** stays visible in an unfocused editor.
- **A blink interval setting.** `UISettings` could carry and cascade it; it is a `const` for now.
- **A context menu over the editor.** Clicking an item would make it the active control and leave the
  caret hidden until the next click in the text. Nothing opens one over the document today.

## Verification

- `dotnet build AuroraEngine/ArctisAurora.sln` — 0 errors, and no warning on `CaretControl.cs`,
  `DocumentControl.cs` or `Inlines.cs`.
- **Not GUI-verified.** What to walk: the caret blinks at ~1Hz; typing and arrows keep it solid;
  clicking the vault browser removes it; clicking back restores it; dragging the scrollbar keeps it;
  the note-name prompt removes it and confirming brings it back.
