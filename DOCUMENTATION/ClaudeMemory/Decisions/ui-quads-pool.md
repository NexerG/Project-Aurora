# Decision — the draw list is one handle-less pool, UIQuads, shared by every window

**Date:** 2026-09-16
**Status:** landed
**Scope:** `ArctisAurora.Core.Data` — `DataPool.Rewind`, `DataPool.Append`; `ArctisAurora.Core.UI` — `UIEngine.Quads`,
`UIEngine.BuildDrawLists`, `UIEngine.Collect`, `Control.Emit`, `Control.WriteArranged`, `TextRunControl.Emit`/`WriteGlyph`;
`ArctisAurora.EngineWork.Rendering.Modules` — `UIEngineModule.PublishQuadRange`, `MirrorDrawList`;
`AuroraEngine/Data/XML/Documents/Pools.pools.xml`

**Reverses in part:** [ui-draw-list](ui-draw-list.md) — "not stored in a pool" and "two plain fields on `Control`". The
walk, the cull and painter order are unchanged. **Replaces** the `DrawList` mechanics of
[ui-draw-list-publish](ui-draw-list-publish.md); its accepted in-place tear still stands.

## What changed

- **`DrawList` is deleted.** Its two arrays are the columns of one pool, `UIQuads`: `System="Main"`, `Capacity="1024"`,
  `Ordered="false"`, `Growth="Additive"`, `GrowthValue="512"`, `ControlGeometry` then `VulkanControl`. `UIEngine` owns
  it — `UIEngine.Quads`, beside `Elements`.
- **`DataPool` gained a handle-less fill.** `Rewind()` sets the count to zero. `Append()` calls `Grow()` when full,
  widens the dirty range like `Allocate`, returns the dense row. Both owner-asserted. No stable id, no free, no
  compaction. Rows are written through `GetSpan<T>()[row]`.
- **`BuildDrawLists`** rewinds once, then per window takes `first = Quads.Count`, walks, and hands
  `(first, count)` to `UIEngineModule.PublishQuadRange` — one `long`, `first << 32 | count`, `Volatile.Write`.
- **`Control` lost `_geometry`, `geometry`, `depth` and `SetGradientSpace`.** `Emit(float z)` builds the geometry on
  the spot: matrix from `arrange.arranged` and z, clip from `arrange.clip`, gradient rect = `arrange.arranged`.
  `WriteArranged` only records the rect and derives the clip. `ClipRect` no longer mirrors anywhere.
- **z comes from the walk.** `Collect(root, Control.rootDepth)`; a child gets `z + depthStep`.
  `TextRunControl.Emit(z)` puts glyphs at `z + depthStep`, as before.
- **`visual` stays a plain field** and is copied whole into the row.
- **`UIEngineModule.MirrorDrawList`** reads `Quads.Backing<T>()` and the range once, clamps count to the geometry
  array, copies `[first, first + count)` to the **same offsets** in its mirrors (`WriteMappedRange` writes at the source
  offset). The draw passes `first` as `firstInstance`; `gl_InstanceIndex` includes it, so no shader change. Mirrors
  and descriptor ranges stay sized to the pool's capacity.

| Was | Is |
|---|---|
| one `DrawList` per window | one `UIQuads` pool, a range per window |
| `DrawList._cursor` + `Publish()` count | the pool's count is the cursor; the module holds the published range |
| `ControlGeometry` baked at arrange, stored on the control | built at emit from `ArrangeData` and the walk's z |
| growth ×2 per list | +512, from the manifest |
| drawn from instance 0 | drawn from `firstInstance = first` |

## Why these choices

**Every row is rebuilt every frame, so a stored geometry was a duplicate** (user, 2026-09-16).
`ArrangeData` already holds the arranged rect and the clip; the matrix and gradient rect are functions of them plus a
depth the walk knows.

**`visual` stays stored because it is the parsed form of authored strings** (user chose, 2026-09-16).
Rebuilding it at emit re-runs `HexToRGB` twice — four string allocations each — per visible control per frame, and
`SetUVRect` has no other home. The gradient index was already cached at set time (`Control.gradient` setter,
`TextRunControl.BuildRuns`); nothing looks a gradient up by name per frame.

**Large per-frame arrays live in a manifest-declared pool** (user).
Growth follows `Pools.pools.xml`, writes are owner-asserted, the render thread reads through `Backing<T>()` — the
sanctioned cross-thread read — and `DataManager.FrameEdge` reports count, capacity and bytes to the profiler with no
extra code.

**Handle-less, because a draw row has no identity.**
A per-frame refill needs no stable id, deferred free or resequence. The bookkeeping arrays still grow with the pool:
`ReservedBytes` is 212 B a row against 188 B of data.

**One shared pool** (user).
Pools are declared by name and `DataManager` has no unregister, so a pool per window would be registered at runtime
and leak on window close.

**z is passed down the walk, not stored.**
Storing it adds a field to `ArrangeData` for what the walk already knows. A drag ghost's `rangeRoot` now starts at
`rootDepth` instead of its tree depth — harmless, depth testing is off.

## Rejected

| Alternative | Why not |
|---|---|
| Rebuild `visual` at emit from the authored values | two `HexToRGB` and a gradient lookup per control per frame; `uvs` needs a new home |
| Parsed paint values as separate fields on `Control` | the same bytes as the struct, more code |
| A pool per window | runtime registration, no unregister |
| z as an `ArrangeData` field | a pooled field for a value the walk has |
| Multiplicative growth | user chose additive, 512 |
| Copy the range to mirror offset 0, draw from instance 0 | `WriteMappedRange` writes at the source offset; `firstInstance` needs no helper change |

## Measured (2026-09-16, Thorium `--profile-scenario --profile-pools`)

- 240 captured frames: `UIQuads` count 2414–3877 across typing and resize, capacity 4096 (1024 + 6 × 512),
  868,352 B reserved.
- Typing adds one row per character.

## Known gaps

- **Cross-window tear — accepted** (user, 2026-09-16). Main refills the shared pool while render copies. When an
  earlier window's quad count changes, a later window can draw some of the earlier window's quads for a frame. Same
  class as the in-window tear [[ui-draw-list-publish]] accepts.
- **A growth between the two `Backing<T>()` reads mis-sizes the control mirror.** `MirrorDrawList` sizes the geometry
  mirror from the new array and the control mirror from the old one, records the new capacity, and never re-checks —
  the next copy writes past the control mirror. Pre-existing with `DrawList`; additive growth makes growth events more
  frequent while a large note first opens. Not fixed.
- Each quad costs `Append` plus two type-keyed `GetSpan<T>` lookups (and the DEBUG owner check) where `DrawList`
  indexed an array. Not measured separately.
- Nothing stops `Allocate`/`Free` on `UIQuads`; mixed with `Rewind`/`Append` they corrupt the stable-id tables.
- `HasPendingWork` still returns `true` and no `PoolCursor` consumes `UIQuads` — the rebuild-on-change item in
  [[ui-draw-list]] § Open.
- **Verified:** builds clean; the Thorium profile scenario (1M-char note, 120 ticks typing, 120 ticks resize) ran to
  its own shutdown with no exception; Thorium's shell and two open notes GUI-verified by capture. **NOT GUI-verified:**
  drag ghost, a second OS window, the snap-resize `Pump` path, Carbon, the Editor.

Related: [[ui-draw-list]], [[ui-draw-list-publish]], [[ecs-rework-data-pools]], [[render-thread-reads-pool-row]], [[engine-profiling]]
