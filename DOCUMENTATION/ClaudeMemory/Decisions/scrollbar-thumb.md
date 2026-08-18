# Decision — the scroll thumb goes at the head of `children`, and the content gets a gutter

**Date:** 2026-08-18
**Status:** LANDED. **GUI-verified** — thumb renders, sizes, tracks the wheel and drags the document.
**Scope:** `ArctisAurora.Core.UISystem.Controls.Containers` (`ScrollableControl`),
`ArctisAurora.Core.UISystem.Controls.Interactable` (`ScrollThumbControl`).

## Decisions

### 1. Hit-testing walks children forward and takes the first match, so the thumb goes first

Appending the thumb put it **on top in paint order and last in hit order**, which are opposite ends
of the same list: `CollectDFS` walks `children` forward so later siblings draw over earlier ones,
while `FindDeepestValid` walks forward and returns the *first* subtree that contains the point. A
press on the thumb reached the document underneath and started a text selection instead of a drag.

**Rejected: reversing the sibling walk in `FindDeepestValid`** so the topmost-drawn sibling is
hit-tested first, which is what other UI toolkits do. Rejected by the user (2026-08-18): once the UI
tree becomes data ([[ui-data-control-split]]) the hit-test is a flat forward loop in DFS order, and
walking it back to front would be cache-hostile. Ordering is expressed by *where a control sits in
the list*, not by the direction it is read.

So the thumb is `children.Insert(0, …)` — the same position `DocumentControl` gives its selection
boxes, for the same reason from the other side.

### 2. Which makes the thumb draw behind the content, so the content is inset instead

Head of the list means it paints first. The document tree paints nothing (`DocumentControl`,
`TextControl` and their blocks are all masked `invisible`), but a vault-browser row is an opaque
`ButtonControl` stretched to full width and would have covered the bar once the sidebar had enough
notes to scroll.

So the scrollbar is **not an overlay**: `Gutter` is subtracted from the inner rect in both `Measure`
and `Arrange`, and the thumb is placed in the space that leaves. Nothing overlaps, so paint order
stops mattering at all.

The gutter is reserved **whenever the axis can scroll**, not only when a thumb is showing. Reserving
it on demand feeds back on itself — showing the bar narrows the content, which makes it taller,
which is the input to deciding whether the bar shows. A constant inset has no such loop.

### 3. The content child is found by scanning, not cached

`ScrollableControl` used `children[0]` as its one content child, which the thumb now occupies. A
cached `content` field would go stale, because `DocumentEditorControl.LoadDocument` calls
`children.Clear()` on every note load and never tells the base class. `Content` scans for the first
non-thumb child instead, and `AddChild` guards on it, so a cleared list correctly reads as empty.

`EnsureThumb` rebuilds the thumb when it is missing for the same reason — the load destroys it. It
runs from `Arrange`, which is safe: `UILayout.ResolveLayout()` is called *after* `Interpolate`'s
`foreach (Entity entity in entities)` closes, so it is not inside the enumeration the engine defect
is about.

### 4. Vertical only

`ScrollDirection` has `Horizontal` and `Both`, and neither has a thumb — `ArrangeThumb` returns
early unless `CanScrollVertical`. Both live scrollables (`VaultBrowserControl`,
`DocumentEditorControl`) are `Vertical`, so a horizontal thumb would be a second code path with no
caller. Wheel scrolling on those axes is unaffected.

### 5. Thumb length is the viewport/content ratio, floored

`length = viewport² / content`, clamped to at least 24px and at most the track, and its position is
`travel * scrollOffset / maxScroll`. The floor is what keeps a very long document's thumb grabbable.
`ThumbTravel` is published because the thumb's drag needs the same number to map pointer travel onto
scroll range.

## Verified

- Builds clean.
- **Screenshotted:** the thumb appears on a 40-section note, sized to a fraction of the track;
  wheel-scrolling 14 ticks moved the document to Section 6 and slid the thumb down proportionally;
  dragging the thumb from y=100 to y=420 jumped the document to Section 19+ with **no text
  selection**, which is the exact failure the head-insert fixes.
- The sidebar shows **no** thumb — its three rows fit — confirming the collapse-when-fitting path.
- Note switching still works, which exercises `EnsureThumb`'s rebuild after `children.Clear()`.

## Still open

- A new thumb entity is created on every `LoadDocument`. The old one is destroyed properly, so it is
  churn rather than a leak, but a scrollable that reloads often pays for it.
- No track behind the thumb, no arrows, no click-in-track paging. The thumb is the whole scrollbar.

Related: [[ui-clipping]], [[splitter-and-pane-sizing]], [[ui-data-control-split]],
[[vault-browser-and-shell]], [[document-selection]]
