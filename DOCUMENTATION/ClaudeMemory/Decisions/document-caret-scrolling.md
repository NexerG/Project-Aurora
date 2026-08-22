# Decision — the caret scroll is requested, and Arrange performs it

**Date:** 2026-08-23
**Status:** LANDED. Builds clean (0 errors; the warnings in `DocumentEditorControl` are pre-existing
and none are on the new lines) and `Periodic` boots to all three threads. **Not GUI-verified.**
**Scope:** `DocumentEditorControl` only.

## What was wrong

Reported by the user: the editor does not scroll to the caret when Enter opens a new line, and does
not scroll when typing below the fold with the view somewhere above.

`ScrollToCaret()` had exactly two callers, `MoveCaret` and `DeleteOver`. **The typing path had
none** — `Text.Write` → `TypeChar` → `WriteChar` never asked to scroll on any branch. `SplitBlock`
deliberately skipped it ([[document-structural-editing]] decision 7) and so did undo/redo
([[document-undo]] decision 8), both for the same reason: a block created this tick has no
`arrangedRect`, and `ScrollIntoView` reads a zero rect as "above the viewport" and jumps to the top
of the note.

`TabViewControl` is a plain `AbstractContainerControl`, so the editor is the only scroll container in
the chain and the fix is local to it.

## Decisions

### 1. One deferred mechanism, not two

`ScrollToCaret()` became `RequestScrollToCaret()` — a flag plus `InvalidateArrange()` — and the real
`ScrollIntoView` moved into a new `DocumentEditorControl.Arrange` override, which is the first moment
the caret's run holds a rect for the layout it now lives in.

**The two paths that already worked were switched over too** (user's call: "do which you think is
better"). Leaving arrows and Backspace on the immediate path would have kept a second mechanism alive
that reads *last* frame's geometry — type a character that wraps, then press Down, and the arrow
scroll resolves against a layout that predates the wrap. One mechanism costs one frame of latency on
arrow scrolling, which is invisible, and removes the whole stale-geometry class.

### 2. `Arrange` must never exit with `isArrangeDirty` set

`InvalidateArrange` walks up the tree and **returns without registering a dirty root** the moment it
meets a control already marked dirty — including the control it started on. Called from inside
`Arrange`, every ancestor is mid-pass and still dirty, since `ScrollableControl.Arrange` clears its
own flag only at the end, after its children return.

Two consequences, and the first cut of this got the second one wrong:

- A scroll that merely invalidated would move `scrollOffset` and never lay the content out against
  it, so the caret would trail by one *edit*. Hence the second `base.Arrange`.
- **A flag left set makes the editor permanently dirty.** `ScrollIntoView` invalidates whether or not
  it actually scrolled, so on the common path — caret already visible, offset unchanged, no
  re-arrange — the editor exited `Arrange` dirty with no root registered. Every later invalidate from
  it or any run beneath it then hit the stuck flag and was silently dropped: wheel scrolling moved
  the offset and nothing redrew, and arrow keys moved `cursorPosition` while the caret control was
  never repositioned. It unstuck only when something elsewhere dirtied up to the window root and
  cascaded back down. Shipped broken 2026-08-23, fixed the same day.

```
Arrange(finalRect):
    base.Arrange(finalRect)
    if no scroll was requested: return
    clear the request
    if the caret has no point: return
    remember the offset, ScrollIntoView the caret rect
    if the offset moved: base.Arrange(finalRect) again
    otherwise:           clear isArrangeDirty, which ScrollIntoView set for nothing
```

The early returns are safe as they stand — nothing between them and `base.Arrange` dirties anything.

**Rejected: calling `base.Arrange` unconditionally.** It exits clean without touching the flag, but
pays a full document arrange on every keystroke, which is the cost the guard exists to avoid.

**Not fixed, and pre-existing:** when the editor is itself the top dirty root, that same
`InvalidateArrange` finds clean ancestors and dirties the chain to the window, scheduling a redundant
full-window arrange next frame. Every caret scroll has always done this — `MoveCaret` →
`ScrollToCaret` → `ScrollIntoView` did it from outside `Arrange`. Not made worse here.

### 3. Every editing path requests

`MoveCaret`, `DeleteOver`, `SplitBlock`, `TypeChar`, `Undo`, `Redo`. `TypeChar` sets a bool per
character, which is free, so the drain loop needs no special case.

`ResolveOnClick` and `ResolveDrag` are left alone — a click lands where the user is already looking,
and a drag has its own `AutoScroll`.

## Verified

- Builds clean; no warning falls on a new or changed line.
- `Periodic` boots through all 24 bootstrap steps to all three threads.
- **Not GUI-verified:** Enter on the last visible line, typing below the fold with the view scrolled
  up, and Ctrl+Z on an off-screen edit.

## Still open

- **The caret lands flush against the viewport edge**, since `ScrollIntoView` scrolls it just barely
  into view. Editors normally keep a line or two of margin. Not added — it is `ScrollIntoView`'s
  behaviour and the arrow keys have always had it.
- **Dragging outside the window still does nothing** — `Engine.HandleUI` returns early while the
  pointer is outside, so a selection cannot be extended past the window edge.

Related: [[document-structural-editing]], [[document-undo]], [[document-selection]]
