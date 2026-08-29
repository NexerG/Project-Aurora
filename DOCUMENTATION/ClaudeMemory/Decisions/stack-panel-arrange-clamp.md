# Decision — a stack clamps its children in Arrange, and keeps offering MaxValue in Measure

**Date:** 2026-08-29
**Scope:** `ArctisAurora.Core.UISystem.Controls.Containers` — `StackPanelControl.Arrange`

## What changed

- Both orientation branches of `Arrange` ended their size calculation with `MathF.Max(0, childH)` /
  `MathF.Max(0, childW)`. Each is now a `Math.Clamp` against what is left of the panel's own inner box from
  the running `cursor`, minus that child's margin on the main axis.
- `Measure` is untouched. A non-star child is still offered `float.MaxValue` on the main axis.

## The symptom

`VulkanControl.Measure` gives a control with no `Width`/`Height` the whole offer, and a vertical stack offers a
non-star child `float.MaxValue` on the main axis. So `<Button ControlColor="green"/>` — no size, no star —
measured `DesiredSize.Y == float.MaxValue`, `Arrange` used that verbatim, and:

- the quad was written ~3.4e38 tall, which covered the whole window in that child's colour and painted over
  every earlier sibling, because paint order is the tree's DFS order;
- `cursor += childH` sent every later sibling to `+∞`, off-screen and clipped away.

The editor's placeholder `UI.xml` has three unsized buttons in a row, so its window had been solid green with
everything else invisible. Confirmed pre-existing by capturing the unmodified tree — identical.

## Why these choices

**Clamp in `Arrange`, not by changing the offer in `Measure` (user, 2026-08-29).**
The `MaxValue` offer is how a child reports its *natural* size on an unbounded axis, and that number is what a
parent above the stack legitimately consumes. Bounding the offer would change what every child reports upward,
including children that are supposed to exceed their viewport. `Arrange` is where a box is finally decided, so
clamping there bounds what is drawn without touching what is measured.

**Safe against scrolling, which was the risk.**
`ScrollableControl.Arrange` hands its content `MathF.Max(child.DesiredSize, innerRect)` on the scrollable axis,
so a stack inside a viewport already receives a `finalRect` as tall as its own content. `inner.Bottom` is then
the content extent, not the viewport, and the clamp is inert. It only bites where the panel is genuinely
bounded — which is the broken case.

**Both axes, though only the vertical one was reachable.**
An unsized child in a horizontal stack blows up X by the identical path. Same two lines in the same method;
fixing one and not the other would have left an asymmetry inside one `Arrange`.

## Known gaps

- **`Measure` still reports the absurd number upward.** A `ScrollableControl` wrapping a stack that contains an
  unsized child stores `contentSize = MaxValue` and computes a nonsense scroll range from it. The clamp fixes
  what is drawn, not what is reported.
- **Siblings after the offender collapse to zero**, because the offender takes everything that was left. That is
  the honest consequence of the clamp, not a repair for authoring an unsized child in a bounded stack.
- **Not the `maxCross` star-child defect**, which is still open — that one is pass 1 of `Measure` probing a star
  child at `0` on the main axis and polluting the cross measurement. Different axis, different cause.

Related: [[splitter-and-pane-sizing]], [[scrollbar-thumb]], [[ui-clipping]]
