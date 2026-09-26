# Decision — pools double when full and halve after staying a quarter full

**Date:** 2026-09-25
**Scope:** `ArctisAurora.Core.Data` — `DataPool` (`TryShrink`, `Resize`, `_freeIds`, `_versionFloor`), `PoolColumn`, `PoolCursor`; `ArctisAurora.EngineWork.Rendering.Modules.UIEngineModule` (`MirrorTooBig`); `Pools.pools.xml`

## What changed
- **Every pool that was `Additive` is now `Multiplicative` ×2** (`UIQuads`, `Paints`, `Animations`, `Signals`, `Keyframes`, `AnimationValues`). `Gradients`/`Effects` stay additive 32.
- **`DataPool.TryShrink`** runs in `FrameEdge` after compaction and resequence. When `count ≤ capacity / 4` for `DataPool.ShrinkAfterSeconds` (2 s, one engine-wide value), capacity halves while `capacity / 2 ≥ max(initial capacity, 2 × peak count in the window, highest live stableId + 1)`.
- **`Grow` and shrink share `Resize`.** It reallocates columns, slot/back maps, owners, scratch and versions; `StructuralDirty` + full dirty, so the GPU `TableMirror`s recreate at the new `Backing` length as they already did on growth. `PoolColumn.Grow` copies `min(old, new)`.
- **Free stableIds come out lowest first** (`PriorityQueue<int,int>`, was a LIFO `Stack`), so live ids stay packed and one survivor does not pin capacity. Shrink drops free ids ≥ the new capacity and clamps `_highStableId`.
- **`_versionFloor`**: shrink raises it past every truncated slot's version; regrown slots start there, so a handle to a truncated id cannot alias a later occupant.
- **`PoolCursor.TryConsumeStructural`** diffs to its own `_provisioned` length, treating ids past the snapshot as free.
- **`UIEngineModule` quad mirror** (grow-only before) shrinks per image by the same rule: draw count ≤ a quarter of the mirror for 2 s → recreate at `pow2(max(count, 2 × peak))`, at least 256 rows.

## Why these choices

**Additive growth made a large burst quadratic.** Every `Grow` copies every column; 200k rows at +256 is ~780 copies. 200k `Tween`s took 3.65 s and the first `Emit` 937 ms (optimized Debug JIT). Doubling: the burst's worst frame 4,658 → 192 ms (Release+PROFILE). This reverses the user's additive-512 choice for `UIQuads` ([[ui-quads-pool]]) — user, 2026-09-25.

**Shrink at a quarter, grow at full.** The gap is the hysteresis: a pool cannot oscillate between two capacities on one count. The 2 s window keeps a one-frame dip from reallocating GPU buffers. User asked for it (a 1M structure held for 200k live is waste). One engine value, not a per-pool XML attribute — user, 2026-09-25.

**Lowest-free-id (user, 2026-09-25).** Rejected: keep LIFO — the newest-freed ids are the high ones after a burst, so a surviving high id pins capacity. Cost: O(log n) allocate/free.

**Stable ids are never renumbered** — handles would break. So the highest live id bounds a shrink.

## Measured (2026-09-25, Release+PROFILE, scratch ladder `{ 200000, 20000 }`, `--profile-pools`)
- `UIElements` 262,144 → 65,536 ~2.8 s (655 frames) after the 20k grid replaced 200k.
- `Animations` and `AnimationValues` stayed 262,144 — see Known gaps.

## Known gaps
- **`Animations`, `Signals`, `Keyframes`, `LayoutDirty`, `AnimationDone` have no edge step in `Frame.frame.xml`** (`AnimationValues` until it was deleted 2026-09-26), so `FrameEdge` — and `TryShrink` — never runs for them.
- **`Animations` rows are indexed by track id and never freed** (`Animations.Row` appends up to the id); its count is the high-water mark, and `Animations.bindings` / `free` never shrink either. Memory only since 2026-09-25: the step walks the awake list, not the rows ([[animation-core]] § Awake list).
- Quad mirror shrink **not verified by a capture** (no per-image mirror zone); ran without validation errors.
- `PoolCursor` change not exercised by a shrink that coincides with a free — reasoned, not tested.

Related: [[ecs-rework-data-pools]], [[ui-quads-pool]], [[cross-system-change-notification]], [[animation-core]], [[engine-profiling]]
