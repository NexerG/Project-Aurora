# Decision — the UI is rebuilt beside the old one, not migrated in place

**Date:** 2026-09-06
**Status:** **PARTIAL** — landings 1–2 built and GUI-verified; landings 3–6 agreed, not built.
**Scope:** `ArctisAurora.Core.UI` — `UIEngine`, `Control`, `WindowRoot`, `ArrangeData`, `ControlGeometry`,
`VulkanControl`, `VulkanControlType`, `ArrangeFlags`, `HorizontalAlignment`, `VerticalAlignment`, `DockMode`;
`ArctisAurora.EngineWork.Rendering.Modules` — `UIEngineModule`, `CompositorModule`;
`ArctisAurora.EngineWork.Rendering` — `AuroraCamera`, `AGlfwWindow`;
`Shaders/UIEngine/UIEngine.vert`, `Shaders/UIEngine/UIEngine.frag`;
`AuroraEngine/Data/XML/Documents/Pools.pools.xml`, `Bootstrap.bootstrap.xml`

**Supersedes in approach:** [ui-data-control-split](ui-data-control-split.md), which planned an in-place
migration of `UIControls`. The resume plan lives in
[../Context/ui-engine-plan.md](../Context/ui-engine-plan.md).

## Vocabulary — the two things called "control"

Getting these backwards makes every other sentence wrong.

| Name | Side | Is |
|---|---|---|
| `Control` | CPU | tree node, logic, layout. One per element |
| `VulkanControl` | GPU | one drawn quad. A struct, kind-tagged, no behaviour |
| `VulkanControlType` | — | `MTSDFControl`, `PanelControl`, `ImageControl` |

The old `ArctisAurora.Core.UISystem.Controls.VulkanControl` keeps its name until landing 6. The two coexist
because the namespaces differ.

One `Control` owns **0..N** `VulkanControl` rows. A panel owns 1; a text run owns one per glyph.

## What changed — landing 1, the stack draws

- New namespace `ArctisAurora.Core.UI` beside `Core.UISystem`. The old stack is untouched and still runs.
- Two pools in `Pools.pools.xml`: `UIElements` (`ArrangeData`) and `VulkanControls` (`ControlGeometry` +
  `VulkanControlData`). Both `Ordered="true"`.
- `UIEngine` — static, main thread, `Bootstrap` step registered as the last step of `Bootstrap.bootstrap.xml`.
- `Control : Entity`, `PoolName => "UIElements"`, second row taken through `Entity.AllocateIn`.
- `UIEngineModule` — second `RenderingModule` on every `RenderWindow`, `compositorOrder = 10`, transparent
  clear so the old stack composites underneath.
- `ERendererTypes.UIEngine` plus its `AuroraCamera` case.
- `CompositorModule` — `stages[1].PSpecializationInfo` instead of assigning to the already-copied local.

## What changed — landing 2, tree and layout

- `Control.Measure`/`Arrange` — the outgoing stack's two-pass shape, single-child, ported onto `ArrangeData`.
  `WriteArranged` bakes the matrix into `ControlGeometry` and inherits or intersects the clip.
- `ArrangeFlags` (`Clip`, `Hidden`, `MeasureDirty`, `ArrangeDirty`) packs into `ArrangeData.flags`;
  `HorizontalAlignment`, `VerticalAlignment` and `DockMode` give the bare bytes their meaning. None carry
  `[A_XSDType]` — the old stack owns those names and `AnyXMLType.FindType` resolves by name alone.
- `InvalidateLayout` / `InvalidateArrange` walk to the topmost clean ancestor and register it with
  `UIEngine.RegisterDirtyRoot`; `UIEngine.ResolveLayout` drains the set at the `Interpolate` site.
- `Control.RefreshSubtreeCache` fills `subtreeBounds` / `subtreeCount`, run by `ResolveLayout` after `Arrange`.
- `WindowRoot : Control` — the one node that holds siblings, carrying `WindowingMode`, `autoscaling`,
  `ScalingAxis`, `ViewportSize`, `FitTo` and `ToDesignSpace` from the outgoing `WindowControl`.
  `AuroraCamera`'s ortho box is now `ViewportSize`, not the raw extent, and `AGlfwWindow`'s resize callback
  refits it.
- `UIEngine.NextElementOrder` / `NextControlOrder` — `SortAction` on each pool, one DFS walk, keyed by
  `dataHandle` for `UIElements` and `controlHandle` for `VulkanControls`.
- `UIEngineModule.uiRoot`, `firstInstance` and `instanceCount`; the draw is the window's slice, not the whole
  pool. Published by `UIEngine.RefreshWindowRanges` at the frame edge.
- The smoke panel is replaced by a scaffolding tree on the primary window, built back to front so pool
  allocation order is nothing like DFS order.

## Row layout — measured, not estimated

Printed by `UIEngine.Bootstrap` via `Unsafe.SizeOf` at boot.

| Struct | Bytes | Written by | Uploaded |
|---|---|---|---|
| `ArrangeData` | 140 | measure/arrange | never |
| `ControlGeometry` | 96 | arrange | yes |
| `VulkanControl` | 92 | paint | yes |

`ControlGeometry` is `matrix` + `clip` + `gradientRect`. `VulkanControl` is `type` + `uvs` + `tint` +
`textureIndex` + `cornerRadius` + `edgeColor`/`edgeThickness` + `gradientIndex`.

## Why these choices

**Three categories, two of them GPU-side, because a resize and a repaint dirty different bytes.**
The old `ControlData` is 136 B mixing arrange output (`clip` 16, `gradientRect` 16) with paint (104), so every
`Arrange` dirties the whole row to change 32 bytes and `MCUI.MakeInstanced` re-uploads 4.25× more than
changed. Splitting gives two dirty ranges. **The upload saving only materialises if the build is incremental**
— if rows are rebuilt per frame, both buffers upload in full and the split buys CPU cache locality in the
passes plus the shader branch, nothing more.

**Parallel build, not in-place migration.** The conversation-1 plan appended columns to `UIControls` and kept
the recursive driver, which meant every intermediate state had to keep Thorium running. A second namespace,
second pool and second module let the old stack stay whole until landing 6 deletes it.

**One `edge` pair, not `edge` + `outline`.** They stroke two different distance fields — `outline` thresholds
the MSDF texture further out (screen pixels), `edge` bands the analytic rounded box inward (design pixels).
Neither had a single consumer anywhere in the repo. Merged to one `edgeColor`/`edgeThickness` pair whose
meaning `type` selects: the MSDF silhouette on `MTSDFControl`, the rounded box otherwise, **design pixels for
both**. Cost: a control can no longer carry a glyph outline *and* a box band at once, which
`IconControl` documented and nothing used. Saved 16 B/row.

**Masks are a capability, not a hazard.** An earlier draft of this note framed the default `maskAsset` as a
trap. It is the mechanism by which a control gets an arbitrary silhouette, and users are meant to reach for
it. So the sampler set serves **all three kinds**, not just MTSDF and Image, and the frag's branch is
"no mask assigned skips the sample", not "panels never sample".

**Glyphs are GPU rows, not CPU objects.** A run is one `Control` holding the string and its settings and
emitting one `VulkanControl` per glyph. `TextMeasurer` already produces `BlockLayout` / `TextLine` /
`LineSegment` / `CaretGeometry` from a string and `IGlyphMetrics` alone, so a run can be laid out with no glyph
object in existence. Per-character style must therefore live in the run's data, never on a glyph.

**Every control is hit-testable.** `hitTestable` is gone (15 assignment sites in the old stack). The deepest
`Control` always wins the hit and dispatch walks up until a handler returns `true`, replacing ~16 `bubbleXxx`
bool fields and `BubbleAll()`.

**The subtree caches are refreshed after `Arrange`, not inside it.** Maintaining them on the way out of
`Arrange` would put the accumulation in the base method, which every override then has to remember to call —
39 subclasses arrive at landing 6, and one that forgets produces a silently short window range and a hit-test
that misses. Sealing `Arrange` behind a template method and a new `ArrangeCore` virtual would fix that, but it
renames the thing the plan already names and buys nothing a separate walk does not. `ResolveLayout` walks the
dirty subtree once more instead: one extra pointer-chase over nodes that were just measured and arranged, and
no override can get it wrong. A DEBUG-only recompute in `ResolveLayout` compares the two.

**`RefreshWindowRanges` counts the subtree fresh rather than reading `subtreeCount`.** `Entity.Destroy`
detaches through `parent.children.Remove`, not `RemoveChild`, so a destroy moves `StructuralVersion` without
invalidating any layout — the cache would be stale at exactly the moment the range recomputes. The cache is
for the hit-test's early-out and drop targeting; the range walk stays independent of it, as in the old stack.

**A `WindowRoot` is transparent, not masked.** The outgoing `WindowControl` opts out of painting with
`maskAsset = "invisible"`; the new stack has no sampler set until landing 5, so a root drew an opaque
full-window quad over everything the old stack had composited underneath. `alpha = 0f` in the constructor is
the same opt-out with the mechanism available. Revisit when the sampler set lands — a mask is the more honest
expression of "this node is structural".

## Traps this landing discovered

**The camera's ortho box is z ∈ [−512, −0.01].** `CreateOrthographicOffCenter(…, 0.01f, 512f)` with an
identity view gives `z_ndc = z·(−0.0019531632) − 1.9531632e−05`, so **world z must be ≤ −0.01 to survive the
near plane**. The old stack puts a window root at **−10** and steps +0.001 per depth level toward the camera.
A control at z = 0 is clipped entirely and draws nothing. `Control.rootDepth = -10f` matches it.

**`CompositorModule` had never actually specialized `MODULE_COUNT`.** `stages` is built as
`stackalloc[] { vertStage, fragStage }` — a *copy* — and `PSpecializationInfo` was then assigned to the local
`fragStage` afterwards, so the pipeline never saw it and the shader kept its default of `1`. Invisible while
exactly one module existed; a second module renders into an output image the compositor never samples, with no
validation error. Anything adding a third module should confirm the constant still arrives.

## Known gaps

- Landings 3–6 are unbuilt. See [../Context/ui-engine-plan.md](../Context/ui-engine-plan.md).
- `UIEngine.Bootstrap` builds a scaffolding tree on the primary window. The port at landing 6 removes it.
- **Only `WindowRoot` holds siblings.** A plain `Control` throws on a second child, as the old base does, and
  its `Measure`/`Arrange` handle one. Containers arrive with the landing 6 port; nothing between now and then
  can lay out a list.
- `ArrangeData.width` / `height` are unused — copies of the outgoing stack's vestigial `width`/`height`, which
  default to 72 and are read only by `size`. The layout math uses `preferredWidth`/`preferredHeight`.
- Cumulative child offsets (the drop-targeting cache) are deferred to the landing that reads them.
- `UIEngineModule.UpdateModule` re-records its command buffer every frame rather than on `isDirty`. Landing 1
  scaffolding; it makes the range change land, and it contradicts the record-only-when-dirty rule.
- The sampler set, the gradient table and the MSDF path are absent from `UIEngineModule` and its shaders.
  They return as set 2 and binding 3 — the numbering the old shader already uses, so nothing renumbers.
- `UIEngineModule` mirrors the whole pool **per window**. Correct, but wants revisiting before the pool is large.
- `CreatePipeline` is duplicated verbatim from `UIModule`. Collapse at landing 6, not before — sharing it now
  would mean refactoring the module being deleted.
- A `Control` holds one `DataHandle` into `VulkanControls`, not a `(first, count)` range. Landing 4 changes it.
- `DemoteToHelperInvocation` validation errors fire for this shader pair and the old one alike. Pre-existing,
  non-fatal, `discard` under SPIR-V 1.6.

Related: [[ui-data-control-split]], [[entity-transform-split]], [[text-layout-one-measurer]],
[[glyphs-as-pool-data]], [[control-edge-and-outline]], [[mapped-streaming-buffers]], [[gpu-global-frame-data]],
[[ecs-rework-data-pools]]
