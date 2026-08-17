# Decision — selection is two caret slots, and the engine's drag lifecycle got finished to carry it

**Date:** 2026-08-17
**Status:** LANDED (P4 step 1 — selection exists and renders; it mutates nothing). Builds and boots
clean; **not GUI-verified**.
**Scope:** `ArctisAurora.Core.UISystem.Controls.Text.Document` (`CaretSlot`, `SelectionControl`,
`DocumentControl`, `DocumentEditorControl`), `...Controls.Text` (`TextInputActions`),
`ArctisAurora.Core.UISystem` (`UICollisionHandling.SolveLMBRelease`),
`...Controls` (`VulkanControl.StartDrag`).

## Decisions

### 1. The caret is the focus; only the anchor is new state

A selection is `anchor` and `focus`, each a `(TextControl run, int offset)`. `caretRun` +
`cursorPosition` already **are** the focus, so storing a second copy would have created two sources
of truth for where the caret is. `anchor == focus` is simultaneously "nothing selected" and exactly
the behaviour that existed before, so every path that predates selection keeps working untouched —
including `Text.Write`, which still reads `cursorPosition` off the run.

### 2. Slots are normalized on write, or the equality above is a lie

The end of run A and offset 0 of run B, where B follows A **in the same block**, are the same point
on screen: `TextBlockControl` hands B's `firstLineOffset` the value of A's `lastLineEndX`. As pairs
they differ. Release a drag exactly on a run boundary and the code sees a non-empty selection over
zero characters — a stray highlight, and later a Ctrl+B that bolds nothing and reports success.

`Normalize` walks a slot forward while its offset is at its run's end and a next run exists in the
same block. One point, one pair. It also subsumes the special case `MoveRight` was carrying, which is
now deleted from it. **`MoveLeft` still needs its own version** of the rule, because normalizing only
ever moves a slot forwards.

Known edge, accepted: an empty run (length 0) can never hold the caret, since offset 0 is also its
end. No empty runs exist today; when deletion can produce one, left-arrow into it is a dead keypress
rather than a hang — `Normalize` advances one run per iteration and terminates.

### 3. Ordering goes through reading order, not offsets

A drag can run backwards, so the two ends have to be sorted before anything is drawn. A run does not
know where it sits in the document, so `OrderedRuns()` (extracted from `AdjacentRun`, which now uses
it) is walked and the ends compared as `(run index, offset)`. Rebuilt per call rather than cached —
a few hundred entries per tick during a drag, and a cache would need invalidating on every tree
mutation.

### 4. Highlight geometry reuses `CaretAt`

One box per **visual line** the range covers, not one per selection. Per run, the selected span is
clipped against each line's character span, and the box's x edges are `CaretAt(clipStart).x` and
`CaretAt(clipEnd).x` — the same function that places the caret, verified 607/607 on real metrics. No
new advance maths, and the highlight cannot drift from the caret drawn inside it.

**The one place `CaretAt` cannot answer** is a wrapped line's final slot: by the caret-affinity rule
that slot belongs to the line *below*, so asking for it returns the next line's x and top. The line's
own `width` is the right edge in that case.

### 5. Two things about the boxes are load-bearing

- **Reused, never destroyed** — unused boxes are arranged to zero size. Creating and freeing them as
  the mouse moves is a pool allocation, a `MarkTreeOrderDirty` and a full DFS permute **per tick**,
  which is the exact cost `GlyphControl.SetCharacter` exists to avoid.
- **Inserted at the head of `children`** — paint order is the tree's DFS order, which is why the
  caret, added last, draws over the text. A highlight added last would cover the letters. Growth
  therefore uses `children.Insert`, not `AddChild`, and deliberately skips `InvalidateLayout` since
  it happens inside `Arrange`.

Colour is hardcoded on `SelectionControl`, matching `CaretControl`'s hardcoded `#FFFFFF`. A
`DocumentSettings` entry was considered and not taken — no settings screen exists to change it.

### 6. `StartDrag` finished a half-built engine API (user, 2026-08-17)

**`UICollisionHandling.dragging` was declared and read but never assigned anywhere in the repo.**
`SolveDrag` and `SolveLMBRelease` both branch on it, `StopDrag()` fired its callback without
clearing it, and there was no `StartDrag`. So `ResolveDrag` had never once fired, and
`ResizeableControl.RegisterOnDrag(Drag)` — resize-by-dragging — was dead for the same reason.

Closed with two additions: `VulkanControl.StartDrag()` sets `dragging = this`, and
`SolveLMBRelease` clears the field before invoking the callbacks, so a handler asking whether a drag
is live gets the right answer.

**Opt-in from a click handler, not automatic on press.** The control the mouse hits is the deepest
one — inside a document that is a `GlyphControl` — and what wants the drag is whatever above it
knows what dragging means. Automatic assignment from `hovering` would also have silently made
`ResizeableControl` start resizing for the first time; opt-in leaves it dead until someone adds the
call, which is a decision for whoever owns that control.

**Rejected: an editor-local flag polled from `OnTick`.** No engine files touched, but it is a private
parallel mechanism for a concept the engine already half-had, and it leaves the half unfinished.

### 7. Extend is a boolean through the existing moves, not a second set of actions

`MoveCaret(move, extend)` and `SetCaret(run, offset, extend)`. `extend` keeps the anchor; without it
the selection collapses onto the new position. Covers extend+arrows (read in `TextInputActions.Move`)
and extend+click (read in `ResolveOnClick`) with the code that already existed.

**Amended 2026-08-17:** it started as a literal `IsKeyDown(LeftShift) || IsKeyDown(RightShift)` in
both places, which hardcoded editing keys in engine code — the thing the XML keybind system exists to
avoid. It is now a **named modifier**: see [[named-input-modifiers]].

### 8. The typing target is the caret's run, not `activeControl` (found by GUI test, 2026-08-17)

Reported as "sometimes it misses a char and types it on the next keypress". `Text.Write` read
`UICollisionHandling.activeControl`, and **merely hovering a glyph repoints it** — `SolveHover` fires
`GlyphControl.OnContextAdded`, which assigns `activeControl = parent`, with no click involved.
Hovering *away* is worse: `OnContextRemoved` reaches `TextInputControl.CommitEdit()` and clears
`isEditing`. So with the pointer resting over a different run than the caret, `Write` bailed, the
character stayed in `charInputReadQueue`, and the next swap carried it to a later tick — the symptom.

`Text.Write` now targets `editor.CaretRun` whenever an editor resolves, and falls back to
`activeControl` (with the `isEditing` gate) only for a text control outside a document. Order is
preserved either way: the read queue is never cleared on a bail, and leftovers are older than
anything the next swap brings in.

**Not fixed, and the real shape of it:** `activeControl` is hover-derived, so it is not a focus
concept and this only papers over the case where the pointer is still somewhere inside the same
editor. Move the pointer off the editor entirely and typing stops. That is the `ICharacterInput` /
real-focus job CLAUDE.md has claimed exists for months — see [[engine-side-text-input]] decision 2.

### 9. A drag has to end even when nobody saw the release (found by GUI test, 2026-08-17)

Reported as a freeze — no typing, clicks doing nothing, no crash and no log. `Engine.HandleUI`
returns early while `isInWindow` is false, so a button released outside the window never reaches
`SolveLMBRelease`, and `justReleased` is cleared by the next `ResetFrame`. `dragging` then stayed set
permanently: `ResolveDrag` re-snapped the caret to the pointer and auto-scrolled every tick, which
swallowed clicks.

Latent before this slice only because `dragging` was never assigned at all (decision 6). `SolveDrag`
now ends a drag whose button is no longer down, which self-corrects whatever the reason the release
was missed. It cannot run until the pointer re-enters the window, since `HandleUI` gates it too.

**Not fixed:** dragging outside the window does nothing at all, so a selection cannot be extended by
dragging past the window edge — auto-scroll only works inside it. Pre-existing `HandleUI` behaviour,
untouched.

### 11. Decorations are excluded from the hit-test, and a collapsed quad hits nothing

Reported as: type over a selection and the mouse stops responding for good, while typing keeps
working. Two things compounding.

**Mine.** `FindDeepestValid` returns the *first* child whose quad contains the point, and selection
boxes are inserted at the head of `DocumentControl.children` so they draw behind the text — which
puts them first in the hit-test too. `bubbleClick` defaults to **false**, so the box swallowed the
click rather than passing it to the run. New `VulkanControl.hitTestable` (default true) is set false
on `SelectionControl` and `CaretControl`, and `FindDeepestValid` skips them. Bubbling would not have
worked: `DocumentEditorControl.ResolveOnClick` resolves the run from
`UICollisionHandling.hovering`, which would still be the box, so `RunUnder` would find no
`TextControl` and place no caret.

**Pre-existing, and the reason it was permanent.** `IsPointInQuad` returns **true for a collapsed
quad**: every edge is the zero vector, so every cross product is zero, `sameSide` is set to false on
the first edge and no later edge disagrees. Unused highlight boxes are arranged to zero size, so from
the first selection onward every point in the document hit a parked invisible box. `SolvePositions`
now rejects a zero-scale control up front. **Any** zero-sized control had this behaviour, not just
these — worth knowing before something else is arranged to nothing.

Typing was unaffected throughout because character input never goes through the hit-test.

## Verified

- Builds; `Periodic` boots to all three threads with no stderr, with selection state and the drag
  lifecycle live.
- **GUI, by the user:** left/right caret movement works. Up/down did **not** — fixed, see below.
  Typing selected its own output, and typing intermittently dropped a character — both fixed
  (decisions 8 and 10). A stuck drag froze input — fixed (decision 9).
- **GUI, by the user (2026-08-17, second pass):** selection works — highlights, drag and
  shift+click / shift+arrows all confirmed good.
- **Still unverified:** drag auto-scroll only, and not because it failed — the sample note is
  shorter than the viewport, so there is nothing to scroll past.

### 10. Up and down have to exclude the caret's own line

`CaretAtPoint` picks the band nearest in y, and up/down probed one pixel outside the current line.
Inside one wrapped paragraph that works, because lines are contiguous. Across a block boundary it
cannot: `blockSpacing` is 8, so the current line sits 1px from the probe and the neighbouring block's
line sits 7px away — the current line wins and the caret never crosses. Most of the sample note is
single-line blocks, so it read as "up/down don't work at all".

`CaretAtPoint` gained `bandMin`/`bandMax`; up asks for the nearest line entirely above the caret's
own band, down for the nearest entirely below. Page moves and line start/end keep the unrestricted
form, which is what they want — a page move should land on whatever line is nearest the target y.

No desired-x memory: up/down carry the caret's current x, so moving through a short line and back
loses the original column. Standard editors keep one; unbuilt.

## Still open

- **Clipping.** `ClipRect` is computed every `Arrange` and has no consumer, so nothing is scissored.
  Scrolled text already overflows the viewport; a filled highlight box makes that obvious rather than
  subtle. It is on the WIP list under UI. ~~This is the next thing to hit~~ — **deferred by the user
  on 2026-08-17** in favour of the editing binds, so expect the overflow meanwhile.
- ~~**Step 2 — selection-aware editing.**~~ **DONE 2026-08-17**, along with the Backspace/Delete/Enter
  binds that were missing entirely. See [[document-structural-editing]].
- **Step 3 — Ctrl+B/I.** `bold` and `italic` are declared on `TextInputControl` and **nothing reads
  them**; `arial-b` is a separate font asset. So Ctrl+B is a `fontName` swap, and the run split has
  to carry it. `StyleEquals` already compares `fontName`, so the merge side is ready.
- **`WinForms` name clashes keep recurring** — `Keys` needed the same aliasing treatment
  `ScrollableControl` already had. Third occurrence; the project-level fix is turning WinForms off,
  which nothing seems to need.

Related: [[engine-side-text-input]], [[text-layout-one-measurer]], [[periodic-editor-architecture]]
