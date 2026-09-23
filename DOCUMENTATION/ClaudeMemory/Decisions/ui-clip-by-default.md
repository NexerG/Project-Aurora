# Decision — a control clips its children to its bounds unless it opts out

**Date:** 2026-09-23
**Scope:** `ArctisAurora.Core.UI` — `Control` (constructor, `clipOutOfBounds` / XML `ClipToBounds`)

## What changed
- `Control`'s constructor sets `ArrangeFlags.Clip` alongside the two dirty flags, so `ClipToBounds`
  defaults to true. `WriteArranged` is unchanged: a clipping control's `ClipRect` is its own rect
  intersected with its parent's clip, and its subtree inherits that.
- Opt out with `ClipToBounds="false"` in XML or `clipOutOfBounds = false` in code. XML applies only
  authored attributes, so the constructor default holds unless one is written.
- The XSD description reads "On by default. Will not render or hit-test children outside bounds."

## Why these choices

**A child that leaves its parent is cut at the parent's edge and stops taking the pointer there.**
Before, only the window root and the few controls that set the flag limited anything, so an overflowing
child painted across its neighbours — [[splitter-and-pane-sizing]] records one that painted across the
window.

**Clipping "only when something overflows" is the same thing.** Intersecting with a rect the child
already fits inside changes neither pixels nor hit-tests, so there is no separate mode to build.

**Rejected: inverting the flag** into an overflow bit that is clear by default. It renames the flag for
the same result as one line in the constructor.

**No control opts out today.** Checked: context menus and dropdown lists are the root's last child or
their own window; the caret and selection highlights are the document's own children, inside it; every
zero-size `Arrange` hides the control, so clipping it only hides it more.

## Consequences
- A `LabelControl` wider than its box is cut at the box instead of running past it.
- Children of a stack or container that do not fit are cut at its edge.
- Mid-tween folder rows in `FileTreeControl` are cut at the row rather than spilling over neighbours.
- `UIEngine.Collect` culls more: a subtree outside its tighter clip leaves the walk sooner.
- The existing `clipOutOfBounds = true` sets (`ScrollableControl`, `TextBoxControl`, `KeyCaptureControl`,
  `FileBrowserControl`, `TabViewControl`'s strip, Carbon's `SpanChartControl` and `ZoneTableControl`) and
  Carbon's two `ClipToBounds="true"` are now redundant. Left in place.

## Verified
- Builds clean. Thorium: a crop of the boot layout matches the earlier capture of the same layout, except
  the OS window border and corner, which follow window focus; a sidebar context menu draws whole; a folder
  expanded and its rows draw fully after the tween.
- Carbon: capture against a baseline build without the default — identical except the window border and a
  row of text tops at the bottom edge that moves between runs in both builds. Pre-existing, not this change.
- **NOT GUI-verified:** the folder tween frame by frame, AuroraEditor.

Related: [[ui-clipping]], [[ui-engine-clip-as-coverage]], [[tab-view-control]], [[splitter-and-pane-sizing]]
