# Decision — the UI is rebuilt beside the old one, not migrated in place

**Date:** 2026-09-06
**Status:** **PARTIAL** — landing 1 built and GUI-verified; landings 2–6 agreed, not built.
**Scope:** `ArctisAurora.Core.UI` — `UIEngine`, `Control`, `ArrangeData`, `ControlGeometry`, `VulkanControl`,
`VulkanControlType`; `ArctisAurora.EngineWork.Rendering.Modules` — `UIEngineModule`, `CompositorModule`;
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

## What changed

- New namespace `ArctisAurora.Core.UI` beside `Core.UISystem`. The old stack is untouched and still runs.
- Two pools in `Pools.pools.xml`: `UIElements` (`ArrangeData`) and `VulkanControls` (`ControlGeometry` +
  `VulkanControlData`). Both `Ordered="true"`, no `SortAction` yet.
- `UIEngine` — static, main thread, `Bootstrap` step registered as the last step of `Bootstrap.bootstrap.xml`.
  Holds the two pool accessors and (landing 1 only) a smoke panel.
- `Control : Entity`, `PoolName => "UIElements"`, second row taken through `Entity.AllocateIn`.
- `UIEngineModule` — second `RenderingModule` on every `RenderWindow`, `compositorOrder = 10`, transparent
  clear so the old stack composites underneath.
- `ERendererTypes.UIEngine` plus its `AuroraCamera` case (ortho over the raw swapchain extent).
- `CompositorModule` — `stages[1].PSpecializationInfo` instead of assigning to the already-copied local.

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

- Landings 2–6 are unbuilt. See [../Context/ui-engine-plan.md](../Context/ui-engine-plan.md).
- `UIEngine.Bootstrap` creates a smoke panel that draws in every window of every host. Scaffolding; the arrange
  pass removes it.
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
