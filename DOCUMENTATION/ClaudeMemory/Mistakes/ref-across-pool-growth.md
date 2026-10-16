# Mistake — holding a ref into a DataPool column across a call that can allocate

**Date:** 2026-10-16
**Scope:** `ArctisAurora.Core.UI.LayoutEngine` — `ArrangeRow`, `MeasureRow`, `MeasureSingle`, `MeasureStack`; `Thorium` `Planner.GanttScrollGrow` test

## Symptom
- Planner Gantt jolts for one frame while scrolling; every ancestor up to the window root shows stale `subtreeBounds` (healed next frame, because ArrangeDirty stayed set).
- DEBUG `LayoutEngine.VerifyLayout` errors: "skipped layout left '…' (GanttChartControl|PlannerScroller|PlannerEditorControl|WindowRoot) row N subtreeBounds stale", plus `[UIEngine] subtreeBounds != recomputed`.
- Intermittent: only on the frame a scroll crosses the `UIElements` pool's capacity (1024, Multiplicative x2). `DataPool.TryShrink` halves capacity after a quiet spell, so it recurs.

## Cause
- The Gantt creates its pooled parts (bands, lines, bars, name and header labels) inside `ArrangeCore` as it scrolls.
- An allocation that fills `UIElements` grows the pool; every column is reallocated.
- Every `ArrangeRow` frame still on the stack held `ref ArrangeData` into the old column array, and wrote `subtreeBounds` and the dirty-flag clear into that discarded array.

## Rule
- A `ref` into a `DataPool` column (`GetSpan` / `Backing`) is valid only until the next call that can allocate a row in that pool. Any child measure or arrange qualifies, because controls may create controls there.
- Re-take the ref after such a call (`a = ref arrange[row]` / `pool.GetSpan<ArrangeData>()`); do not hold it across.
- Fixed by re-taking after the child/custom call in `ArrangeRow` (before `subtreeBounds` and flags), `MeasureRow` (before `measuredOffer` and flags), `MeasureSingle` (before reading padding/preferred size and writing `desired`), `MeasureStack` (after both child passes, before writing `desired`). `MeasureWrap` already did this; `ArrangeStack`/`ArrangeWrap` already re-fetch their span after each child.

## Rejected
- **Pre-create the Gantt's parts outside layout.** It cannot know the count before arranging, and the fix would cover one control; the layout fix covers every control that allocates during layout.
- **Forbid allocation during layout.** The Gantt's viewport-only virtualization depends on it.

## Guard
- `Planner.GanttScrollGrow` (`Thorium/Tests/PlannerTests.cs`, listed in `Planner.tests.xml`): 60 tickets in one category plus 60 empty categories, fills `UIEngine.Elements` to `Capacity` with free-standing `PanelControl`s, scrolls `editor.scroller` 90 rows down, checks capacity grew and "Empty 40" has a name label. The harness fails it on the logged layout errors.
- Failed before the fix with exactly the four `subtreeBounds stale` errors plus `subtreeBounds != recomputed`; passes after.

## Known gaps
- The `ref ArrangeData ca` child refs inside `MeasureStack`/`ArrangeStack` loops still read `margin`/`desired` through a ref taken before the child call. Reads only, values are copied at growth, so harmless today; not changed.
- NOT GUI-verified; not perf-measured.
- Wrong glyphs for a frame while scrolling is a different bug (the UIQuads tear) — see [[ui-draw-list-publish]].

Related: [[ui-draw-list-publish]], [[render-thread-reads-pool-row]], [[verify-what-the-user-sees]]
