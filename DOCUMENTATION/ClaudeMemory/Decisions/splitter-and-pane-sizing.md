# Decision — a splitter writes one pane's size and lets the star pane absorb the rest

**Date:** 2026-08-18
**Status:** LANDED. **GUI-verified** — dragged both directions, repeatedly, with the clamp holding.
**Scope:** `ArctisAurora.Core.UISystem.Controls.Interactable` (`SplitterControl`),
`Periodic/Data/XML/Documents/UI.xml`.

## Decisions

### 1. The splitter resizes the pane *before* it, and nothing else

Dragging writes `preferredWidth` (or `preferredHeight`) on the previous sibling. The pane after it
carries `WidthStar="1"` and `StackPanelControl.Arrange` already gives a star child whatever main-axis
space is left, so the second pane follows for free and only one number is ever authoritative.

**Rejected: redistributing star weights between the two neighbours.** That is the general case and
would work star-against-star, but it is a second code path with no caller — the shell is one fixed
pane and one star pane, and the timeline UI in Phase C would be the first thing to need the other
shape. Building it now would be designing off a use case that does not exist.

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

Related: [[ui-clipping]], [[vault-browser-and-shell]], [[button-states-and-hover-bubbling]]
