# Decision — the GPU list is walked out of the tree, not stored in a pool

**Date:** 2026-09-07
**Status:** landed
**Scope:** `ArctisAurora.Core.UI` — `DrawList`, `Control.Emit`, `Control.geometry`/`visual`,
`UIEngine.BuildDrawLists`, `UIEngine.NextElementOrder`, `TextRunControl.Emit`, `LayoutRect.Overlaps`;
`ArctisAurora.EngineWork.Rendering.Modules` — `UIEngineModule`;
`AuroraEngine/Data/XML/Documents/Pools.pools.xml`

**Reverses:** the landing 1 and landing 4 storage decisions in [ui-engine-stack](ui-engine-stack.md),
and the "row building is incremental per element" row in
[../Context/ui-engine-plan.md](../Context/ui-engine-plan.md).

## What changed

`ControlGeometry` and `VulkanControl` stopped being pool rows. They are two plain fields on `Control`.
The **`VulkanControls` pool is deleted** — the manifest entry, the `UI.NextControlOrder` sort, the
per-window instance ranges and the `PoolCursor` the module drew through.

In their place, one DFS pre-order walk per window fills a `DrawList` — two parallel growable arrays,
`ControlGeometry[]` and `VulkanControl[]`, addressed by the same instance index the shader already
used. `UIEngineModule` copies the list's prefix into its per-image mapped mirrors and draws
`count` instances from zero.

| Was | Is |
|---|---|
| draw order = the pool sorted by `SortAction` at `FrameEdge` | draw order = the order the walk emits |
| a reparent permutes 188-byte rows | a reparent changes nothing; the next walk is already right |
| a run owns one pool row per character | a run owns no rows; it emits a quad per **visible** glyph |
| upload = the pool's dirty span, mirrored | upload = the whole list, every frame |

## Why the per-frame rebuild is affordable

Because the walk culls. **The list is bounded by what fits on screen, not by the size of the tree** —
a 56.7k-glyph note and a ten-line one emit the same few thousand quads. That is the whole argument;
without the cull this is the 11 MB/frame the plan rejected, with it the probe emits 44.

Two prunes, both against rects the layout pass already maintains:

- `Collect` stops at a subtree whose `subtreeBounds` misses its inherited `clip`. Sound because a
  child's clip is always a subset of its parent's — `WriteArranged` either inherits it or intersects
  it — and the chain terminates at the window root's own rect, so "off screen" and "outside an
  ancestor's clip" are one test.
- `Control.Emit` drops a control whose own `arranged` misses its `clip`. Children are still offered
  the walk: the clip is inherited, so theirs may sit somewhere else entirely.
- `TextRunControl.Emit` skips whole lines outside the clip band and breaks past the bottom. The pen
  restarts per line, so dropping one costs the next nothing.

Measured on the probe: a 342-character wrapped run in a 40 px `ClipToBounds` box emits **141 glyph
quads**, and the visible band is exactly the lines that meet the clip.

## What this cost

- **The per-glyph atlas and metric lookups now run per frame** for visible glyphs, where they used to
  run once per arrange. Bounded by the screen; the first thing a rebuild-on-change flag removes.
- **`HasPendingWork` returns `true`**, so every image re-records every frame. Same flag fixes it.
- **Main rebuilds the list while render copies it.** Same coarse race the pool mirrors already ran;
  `MirrorDrawList` reads both array references and the count into locals once, so a growth mid-copy
  cannot throw. The fix, when it matters, is a 3-deep ring of lists, not a lock.
- `DrawList.Next()` does **not** clear the slot it hands back. Both emitters write their structs whole
  — `TextRunControl.WriteGlyph` sets `edgeColor` for no other reason.

## What did not change

- **`UIElements`/`ArrangeData` is still a pool**, still `Ordered`, still sorted by
  `UI.NextElementOrder`. Only the GPU-facing columns moved.
- **Painter order.** DFS pre-order, parent before children, is what the pool was sorted into and what
  the walk emits. Depth testing is off, so this is load-bearing.
- **The shaders.** No binding, no indirection, no `.spv` recompile — the draw is still
  `GEO.rows[gl_InstanceIndex]`, the list is just shorter.
- **Clipping is still a fragment `discard`.** The cull decides whether a quad is *submitted*; a
  partially visible one is still cut per pixel by `UIEngine.frag`.

## Rejected

| Alternative | Why not |
|---|---|
| An index buffer of visible rows, mirrors left whole | Needs set 1 binding 4, a vert change and four `.spv` copies, and leaves every invisible glyph occupying pool memory and upload bandwidth. Culls the draw only |
| Partition the pool by visibility via `SortAction` | Permutes up to 56.7k rows of real memory per scroll frame and dirties the whole range — worse than permuting ints, far worse than emitting them |
| Keep the pool, emit copies out of it | Pays for the walk without collecting on it: the run still allocates a row per character, and the pool's dirty machinery feeds nothing |

## Open

- **Rebuild on change.** Agreed and deliberately not built (user, 2026-09-07) — every frame is easier
  to watch. One flag set by `ResolveLayout`, `Hide` and scroll; `HasPendingWork` becomes its answer.
- **Horizontal per-glyph culling** within a line. Vertical is what documents need.
- **Scroll as a uniform**, so arrange stops re-baking every glyph matrix. Unrelated to this note, and
  now the larger remaining cost.
- `Entity.AllocateIn` / `FreeIn` / `_extraHandles` lost their only caller and are left in place.
- `ControlGeometry` and `VulkanControlData` still carry `[A_XSDType(..., "DataPools")]` though no pool
  declares them. Harmless; the generator just emits two unused types.
