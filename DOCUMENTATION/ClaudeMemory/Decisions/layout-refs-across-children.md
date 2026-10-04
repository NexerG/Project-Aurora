# Decision — layout holds row refs across child calls; spans are re-fetched

**Date:** 2026-10-04
**Scope:** `ArctisAurora.Core.UI` — `LayoutEngine` (`MeasureRow`, `ArrangeRow`, `MeasureSingle`, `MeasureStack`, `ArrangeStack`), `BlockControl` (span edits)

## What changed
- `MeasureRow`, `ArrangeRow`, `MeasureSingle`, `MeasureStack` take one `ref ArrangeData a` to their own row and
  write `desired`, `measuredOffer`, `arranged`, `subtreeBounds` and the flag clears through it, after the child calls.
  No second `ref … b` re-fetch, no `preferredWidth`/`preferredHeight`/`padding` locals.
- `MeasureStack` reads each child's `margin` through `ca` after `MeasureRow`; no `Thickness margin` copy.
- The `Span<ArrangeData>` is still re-fetched after every child call, before indexing **another** row
  (`ArrangeRow`'s `subtreeBounds` fold, the next `ca` in both stack loops).
- `BlockControl` edits spans in place through `CollectionsMarshal.AsSpan(spans)[i]` — `ApplyLayout`, `InsertText`,
  `RemoveText`, `StyleRange`, `SetPicture`, `SetMath`, `SplitSpanAt`, `MergeSpans` — instead of copy, change, write back.
  A ref is never used after an `Insert`/`RemoveAt` on the list.

## Why these choices

**A held row ref and a held span fail differently, so they get different rules.**
`DataPool.GetSpan` is `data.AsSpan(0, _count)`. Any row allocated during layout (a Custom's grips, thumbs, highlights,
materialized segments) lies outside a span fetched earlier → `IndexOutOfRangeException`. Holding the span across child
calls crashed Thorium on frame 0, every run. A `ref` to one row survives allocations; only a capacity `Grow`
(`DataPool.Resize` copies every column into new arrays) leaves it pointing into the dead array.

**Row refs across child calls are an accepted risk (user, 2026-10-04).** No detection was added — option (a) of three;
a capacity-change `Log.Warn` in `UIEngine.ResolveLayout` and re-dirtying that pass's roots were both declined.

**The win is noise-level.** The copies removed were 16–150 B each. Release+PROFILE, `--test=Perf`, 3 runs each side,
`Step.Main.Layout`:

| test | before p50 / p95 (ms) | after p50 / p95 (ms) |
|---|---|---|
| `RewrapLargeNote` | 1.738–1.801 / 2.090–2.127 | 1.576–1.652 / 1.735–1.936 |
| `ResizeLargeNote` | 0.065–0.071 / 0.103–0.121 | 0.064–0.068 / 0.076–0.088 |
| every `Controls.*.Relayout`, `TypeLargeNote`, `AnimationLayoutClip` | overlapping ranges | overlapping ranges |

Rewrap moved with the `StyleSpan` change alone too (`SetPage` → `ApplyLayout` on every block), so most of it is
probably that, not layout.

## Known gaps
- **Symptom if the risk fires:** a pool `Grow` during layout — a control created inside `MeasureCore`/`ArrangeCore`
  while `UIElements` is exactly at capacity. Every parent up the stack writes `desired`/`arranged`/`clip`/`subtreeBounds`
  and its flag clears into the dead array: controls misplaced or zero-sized, hit-tests against old bounds. The dirty
  flags in the live array stay set, so it repairs on the next layout pass that reaches those rows; whether an idle host
  runs one unprompted is not verified. Intermittent by nature — it needs the count to cross capacity mid-pass.
- `LayoutNode[] nodes` (`pool.Backing<LayoutNode>()`) was already held across child calls before this change; same hazard.
- `[[ecs-rework-data-pools]]` says growth happens only between frames; layout allocation contradicts it. Not reconciled.
- `RelayoutLabels` logged a 160 KB `Step.Main.Layout` allocation in the third run of two changed-build batches
  (0 of 3 baseline runs, 0 of 2 extra runs). Ref locals cannot allocate; cause not found.

Related: [[ecs-rework-data-pools]], [[pool-shrink]], [[engine-testing]]
