# Decision — overscroll is extra range on `MaxScrollOffset`, not a second offset

**Date:** 2026-08-30
**Status:** LANDED. Builds clean (0 errors; every warning on the two files is pre-existing and none
falls on a changed line). **Not GUI-verified.**
**Scope:** `ArctisAurora.Core.UISystem.Controls.Containers` (`ScrollableControl`),
`ArctisAurora.Core.UISystem.Controls.Text.Document` (`DocumentEditorControl`).

## Decisions

### 1. Widen the range, do not add a second offset

`ScrollableControl.overscroll` is a fraction of viewport height folded into `MaxScrollOffset.Y`.
Every consumer of the scroll range already reads that one property — `ClampScrollOffset`,
`ResolveOnScrollDown`, `ArrangeThumb`'s position, `ScrollThumbControl.ResolveDrag` — so the wheel,
the clamp, the thumb and the thumb drag all reached the new range without being touched.

**Rejected: a separate `overscrollOffset` added to the child's Y in `Arrange`.** It would have kept
`MaxScrollOffset` meaning "content overflow", but then five call sites each need to know whether to
include it, and `ScrollThumbControl` — which lives in another namespace and only knows the two
published numbers — could not have mapped a drag onto the full travel at all.

**Rejected: padding the document with an empty trailing block.** Overscroll would then be in the
document model, so it would serialize, be undoable, and be reachable by the caret and by
`OrderedRuns`. It is a view property; nothing about the note changes.

### 2. A fraction of the viewport, not pixels

`overscroll = 0.5f` on `DocumentEditorControl` — half a pane, so the last line can be pulled to the
middle. Pixels would read differently on every window size, and the whole point is a stable amount
of room under the line being typed. Precedent is `DocumentLayout.lineHeight`, which is a multiple
rather than a size for the same reason.

Not a setting and not authored per-note. It is a constant until someone asks for it to be neither.

### 3. Only once the content already overflows

```
overflowY = max(0, content.Y - viewport.Y)
maxScroll.Y = overflowY > 0 ? overflowY + viewport.Y * overscroll : 0
```

The guard is what keeps a note shorter than the pane unscrollable. Without it every fitting document
gains half a viewport of phantom range, which would put a thumb on the sample note, make
`ResolveOnScrollDown` consume wheel ticks that should bubble to a parent scrollable, and let a
three-line note be scrolled until it is empty.

The X axis is untouched — bottom-only, the way every editor does it. There is no top overscroll and
no horizontal one.

### 4. The thumb had to stop reading `contentSize`

`ArrangeThumb` sized the thumb `viewport² / contentSize.Y` while positioning it off `MaxScrollOffset`
— fine while those were two views of one number, wrong the moment overscroll separated them. The
denominator is now `innerRect.height + maxScroll`, which is the same value whenever `overscroll` is
0, so nothing that does not opt in changed. See [[scrollbar-thumb]] decision 5.

### 5. Static travel, not a rubber band

Presented as a fork and settled by the user (2026-08-30): scroll-past-end, VS Code's
`scrollBeyondLastLine`. A rubber band needs a per-frame animation driver the UI does not have, and
the wheel is discrete ticks with no release to spring on — it only feels right under a touch or
trackpad gesture the engine does not receive. The driver is now a WiP item in its own right,
alongside gradient and position animation.

## Consequences

- `FileBrowserControl` and any authored `<ScrollableControl>` default to 0 and are unchanged.
- `Overscroll` is an `[A_XSDElementProperty]`, so `UITypeSchema.xsd` regenerates with a new
  attribute on `ScrollableControl` and everything deriving from it.
- Clicking in the empty gap below the document already resolves to end-of-document —
  `DocumentControl.CaretOffText` takes the last run whenever `y` is past the last block's bottom, and
  that path predates this change.
- `ScrollIntoView` and `RequestScrollToCaret` only ever scroll far enough to expose the target, so a
  looser clamp does not move the caret. The caret still lands flush against the viewport edge, which
  is [[document-caret-scrolling]]'s open item and is not what this fixes.

## Still open

- **Not GUI-verified.** The three claims worth checking first are that a short note still has no
  thumb, that the thumb reaches the bottom of its track exactly at full overscroll, and that the gap
  below the note is empty rather than showing a stretched last block.
- Typing on the last line still stops with the caret at the very bottom. Overscroll makes the room
  reachable; nothing scrolls into it on its own.

Related: [[scrollbar-thumb]], [[document-caret-scrolling]], [[document-selection]], [[ui-clipping]]
