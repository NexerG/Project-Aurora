# Decision — a splitter writes one pane's size and lets the star pane absorb the rest

**Date:** 2026-08-18
**Status:** LANDED. **GUI-verified** — dragged both directions, repeatedly, with the clamp holding.
**Scope:** `ArctisAurora.Core.UISystem.Controls.Interactable` (`SplitterControl`),
`Thorium/Data/XML/Documents/UI.xml`.

## Decisions

### 1. The splitter resizes the pane *before* it, and nothing else

Dragging writes `preferredWidth` (or `preferredHeight`) on the previous sibling. The pane after it
carries `WidthStar="1"` and `StackPanelControl.Arrange` already gives a star child whatever main-axis
space is left, so the second pane follows for free and only one number is ever authoritative.

**Rejected: redistributing star weights between the two neighbours.** That is the general case and
would work star-against-star, but it is a second code path with no caller — the shell is one fixed
pane and one star pane, and the timeline UI in Phase C would be the first thing to need the other
shape. Building it now would be designing off a use case that does not exist.
**Superseded 2026-09-03 — Carbon's charts are that caller. See the second amendment below.**

A splitter whose parent is not a `StackPanel`, or that has nothing before it, is **inert rather than
throwing** — `PreviousPane()` returns null and both handlers return early.

### 2. Orientation is read from the parent, not authored

`IsVertical` asks the parent `StackPanelControl` for its orientation. A splitter in a vertical stack
moves heights, in a horizontal stack widths. An `Orientation` attribute of its own could only ever
disagree with the panel it sits in.

### 3. The size is computed from the grab, not accumulated per tick

`ResolveOnClick` records the pointer position and the pane's *arranged* size; each `ResolveDrag`
recomputes `grabSize + (now - grab)`. Accumulating `delta` per tick would work until the clamp bites:
once the pane stops at its minimum the accumulated total keeps growing, so the pane would not start
moving again until the pointer had travelled all the way back. Grab-relative self-corrects.

`arrangedRect` is the seed rather than `preferredWidth`, so a pane that was auto-sized (`0`) starts
from the size it actually has on screen instead of jumping to zero on the first drag tick.

### 4. The floor is the pane's existing `MinWidth`/`MinHeight`

No new property. Every control already carries `minWidth`/`minHeight` as `[A_XSDElementProperty]`,
they were simply never read by anything. The default is `0`, so a pane can be collapsed entirely
unless the XML says otherwise — `UI.xml` gives the vault browser `MinWidth="120"`. There is no upper
clamp: dragging fully right collapses the star pane, which is recoverable by dragging back.

### 5. It derives from `ButtonControl` for the state tints

`ButtonControl` is a panel with `hoverColorHex`/`pressColorHex` falling back press → hover → base,
and that is exactly the feedback a grip wants. Deriving reuses `ApplyState` and the four resolve
overrides that drive it; the alternative was copying that logic onto a `PanelControl`. The inherited
click/release plumbing goes unused.

### 6. The cursor changes on enter and exit, not on hover

`AGlfwWindow.ChangeCursor` **creates a new GLFW standard cursor on every call and never destroys
one.** `RegisterHover` fires every tick, so driving the cursor from the hover callback — which is
what the dead `ResizeableControl` does — leaks a cursor per frame. Enter/exit is twice per hover
episode instead.

**The leak itself is untouched and is not mine to leave quiet:** `ChangeCursor` had no live callers
before this (only dead `ResizeableControl`), so the splitter is what makes it real. A dictionary of
shape → cursor is the fix; deliberately not done here because it is engine window code outside this
slice.

## Verified

- Builds clean.
- **Screenshotted, fresh app each time:** drag right widens the sidebar and the document reflows to
  the narrower column; drag left narrows it; a right-then-far-left sequence lands on exactly the
  `MinWidth="120"` floor. Repeated drags in one session work.
- **Not verified:** the cursor shape itself — `CopyFromScreen` does not capture the pointer.

## Trap for later

Pressing at the *exact* boundary pixel between a pane and its splitter hits the **pane**, not the
splitter: `LayoutRect.Contains` is inclusive on both edges and `FindDeepestValid` returns the first
matching child in declaration order, which is the pane. A drag test aimed at the pane's right edge
looks like a dead splitter. Aim at the middle of the grip.

---

# Amendment — a pinned split measured its star pane at float.MaxValue (2026-08-21)

**Status:** LANDED. Builds clean, boots. **Not GUI-verified.**
**Scope:** `ArctisAurora.Core.UISystem.Controls.Containers` (`SplitViewControl`).

## The defect

Splitting a pane made the new split cover the whole window and squeezed its neighbour to nothing —
but **only when the source pane had a fixed size**. Splitting the star pane was always fine (user,
2026-08-21: "only happens when done on the tab views left of the most right one"). Both `Split right`
and `Split down`.

`StackPanelControl.Measure` offers a **non-star** child `float.MaxValue` on the main axis, and
`SplitViewControl` inherits that Measure. `Split` copies the source's sizing onto the new split, so
splitting `Tabs` (`Width="525"`) produced a split with `preferredWidth = 525` — non-star — which then
measured itself against `MaxValue`:

```
inner.width  = MaxValue
remaining    = MaxValue - 265        (the fixed pane plus the grip)
starUnit     = MaxValue
fresh pane   -> DesiredSize.X = MaxValue
w            = MaxValue
```

The trailing `if (preferredWidth > 0) w = MathF.Max(w, preferredWidth)` is a **floor, not an
override**, so `Max(MaxValue, 525)` stayed `MaxValue`. The outer stack then arranged the split at
`DesiredSize.X`, and because `clipOutOfBounds` is false on these containers nothing clipped it back —
it painted across the window and left `starPool = 0` for everything after it.

Splitting `TabsRight` (`WidthStar="1"`) was fine because the new split inherited the star, and star
children are measured in pass 2 against a real finite `starUnit` and arranged at `widthStar *
starUnit`, never at `DesiredSize`.

`Split down` hit the same thing one axis over: `SizePane` zeroes *both* `preferredWidth` and
`preferredHeight` and then sets only the main one, so after a vertical split the panes carry no width
and the cross-axis offer of `MaxValue` propagated instead.

Pre-existing, and reachable from the drag-to-edge path too — not something the tab menu introduced.
It just became easy to hit once a menu entry could split a named pane.

## Decision — a stack measures its children against its own box, not the offer

Fixed in `StackPanelControl.Measure` for every stack, at the user's call (2026-08-21), after first
landing as a `SplitViewControl`-only override. The override was removed; this replaces it.

```
Measure(availableSize)
    boxWidth  = preferredWidth  > 0 ? preferredWidth  : availableSize.X
    boxHeight = preferredHeight > 0 ? preferredHeight : availableSize.Y
    inner = (boxWidth, boxHeight) shrunk by padding
    ... passes 1 and 2 unchanged, against inner ...
```

A pinned axis is the box the children divide, not a floor under whatever the parent happened to
offer. This recurses correctly: the left pane of a split carries a fixed main size, so splitting it
again produces another pinned split that resolves the same way.

The trailing `if (preferredWidth > 0) w = MathF.Max(w, preferredWidth)` was **left as a floor**. Once
`inner` is right it is a no-op for the pinned case, and turning it into an override would separately
change what happens when children genuinely exceed a pinned size — a different question, and not
this defect.

### Why fixing it for everyone was safe

`StackPanelControl` carries the title bars, every browser row, the tab strip, the tab captions, the
prompt columns and the menu column. Enumerated before the change, each is one of:

- **no pinned size** — root stacks, `rows`, `content`, `strip`, the tab `row`, both prompt columns,
  `ContextMenuControl.column`. `boxWidth`/`boxHeight` fall through to the offer. Unchanged by
  construction.
- **pinned cross axis only, children carrying explicit sizes on it** — `TitleBar Height="32"`, the
  `buttons` rows at `preferredHeight = 30`. The cross offer tightens from `float.MaxValue` to the real
  height, but every child pins that axis itself, so `maxCross` lands on the same number.
- **pinned main axis with a star child** — only the splits built by `Split`. The broken case.

So the change is inert everywhere except where it fixes something. It does **not** address the
separate pass-1 cross-measurement defect (a star child probed at main-axis `0`); constraining the
cross offer can only reduce over-measurement there, never worsen it.

**Rejected: give both panes stars.** `SplitterControl` resizes by writing the *fixed* pane's size and
letting the star pane absorb the rest — the arrangement this whole note is about. Two stars would
have nothing to write. **Superseded 2026-09-03 — two stars now trade weight; see below.**

---

# Amendment — two star panes trade weight instead of one writing a size (2026-09-03)

**Status:** LANDED. Builds clean. **GUI-verified** — Carbon booted with the panes at 1:2, and the
drag itself confirmed by the user.
**Scope:** `ArctisAurora.Core.UISystem.Controls.Interactable` (`SplitterControl`),
`Carbon/Data/XML/Documents/UI/UI.ui.xml`.

## The caller §1 was waiting for

Carbon's flame chart and timeline are both star panes in one vertical stack, and the user asked for a
grip between them starting at `1*` / `2*` (2026-09-03). The original path is inert there: `Arrange`
gives a star child `heightStar * starUnit` and never reads `preferredHeight`, so the write lands on a
field nothing consults. §1 named this exact shape as the thing that would need the other path.

## Decisions

### 1. The pair's weight sum is held, so only the boundary moves

`ResolveOnClick` records the two panes' combined arranged main size (`grabTotal`) and their combined
weight (`grabWeight`); each `ResolveDrag` recomputes

```
main       = clamp(grabSize + (now - grab), floor, grabTotal - nextFloor)
prev.star  = grabWeight * main / grabTotal
next.star  = grabWeight - prev.star
```

Holding the sum keeps every *other* star sibling's share intact — the pair divides only what the pair
already had. Grab-relative for the same reason §3 gives.

**Rejected: writing one pane's size and clearing the other's star.** It would reuse the existing path
exactly, but the first drag would silently rewrite the XML's authored intent, and a window resize
after it would no longer split 1:2 — the ratio is the thing the author asked for.

### 2. Which path runs is read off the panes, not authored

`grabStar` is set at grab time: both neighbours star on the parent's main axis, and their combined
arranged size above zero. Anything else falls to the original write, so Thorium's shell, Carbon's
sidebar and every `SplitViewControl` split are untouched — none has a star pane before its grip.
`SplitViewControl.SizePane` still builds fixed-before-star deliberately.

### 3. A weight may not reach zero

`IsHeightStar` is `heightStar > 0f`, so a pane dragged to exactly zero stops being a star child
mid-drag and falls back to `DesiredSize` — which is large. Both ends of the clamp therefore floor at
`max(minHeight, 1)` pixel. Carbon authors `MinHeight="10"` on both charts (user, 2026-09-03).

**`minHeight` on a star pane is only honoured by this drag.** `StackPanelControl.Arrange` clamps a
star child to `star * starUnit` and the remaining space, never to `minHeight`, so shrinking the
window can still squeeze a pane below its floor. Not fixed here — that is the panel's rule, and
changing it would move every existing star pane.

### 4. The stars are fields, so the drag invalidates by hand

`preferredWidth`/`preferredHeight` are properties that call `InvalidateLayout` on write; `widthStar`
and `heightStar` are plain fields. `DragStars` calls `pane.InvalidateLayout()` itself, once — the
panel re-measures and re-arranges every child, so dirtying one of the pair is enough.

## Known gaps

- **The ratio is not persisted.** `SessionLayout.SplitPane` captures `preferredWidth`/`preferredHeight`
  only, so a star split reopens at whatever the XML authored. Carbon does not use `SessionLayout` at
  all; a `SplitViewControl` would need a stars field in `SessionPane` to keep a dragged ratio.
- Nothing exercises the horizontal star-against-star case yet — the arithmetic is shared, but only the
  vertical one has been driven.

Related: [[ui-clipping]], [[vault-browser-and-shell]], [[button-states-and-hover-bubbling]],
[[tab-view-control]], [[carbon-frame-viewer]], [[session-restore]]
