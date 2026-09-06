# Decision — the UI is rebuilt beside the old one, not migrated in place

**Date:** 2026-09-06
**Status:** **PARTIAL** — landings 1–4 built and GUI-verified; landings 5–6 agreed, not built.
**Scope:** `ArctisAurora.Core.UI` — `UIEngine`, `Control`, `WindowRoot`, `TextRunControl`, `StyleSpan`,
`IGlyphPressTarget`, `NextCaretControl`, `ArrangeData`, `ControlGeometry`, `VulkanControl`,
`VulkanControlType`, `ArrangeFlags`, `HorizontalAlignment`, `VerticalAlignment`, `DockMode`, `PointerEvent`,
`PointerPhase`;
`ArctisAurora.Core.ECS.EngineEntity` — `Entity.FreeIn`;
`ArctisAurora.Core.UISystem.Controls.Text.Document` — `TextMeasurer.Run`;
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

## What changed — landing 3, input

- `PointerEvent` (`target`, `point`, `delta`, `button`, `tapCount`) and `PointerPhase`
  (`Enter`, `Exit`, `Move`, `Press`, `Release`, `Tap`).
- `Control` gains six `virtual bool OnPointerX(PointerEvent)` plus one `Func<PointerEvent, bool>` field and a
  `RegisterOnX` per event. `OnDestroy` calls `UIEngine.Forget`.
- `UIEngine.Poll(RenderWindow)` at the top of `Engine.HandleUI`, ahead of that method's own guards.
- `UIEngine.HitTest` — `subtreeBounds` early-out, then the node's own `clip` and `arranged`, children walked
  **last to first**.
- `UIEngine.Dispatch` — one walk from the hit control up through its parents until a handler returns `true`.
  Every phase goes through it, `Enter` and `Exit` included.
- Contexts `NextHovering`, `NextActiveControl`, `NextPressTarget`, with `Context.Set` / `IContext` /
  `Forget` wired as the outgoing stack wires them.
- The scaffolding gains `ProbeControl`, which recolours on enter/exit/press/release and consumes or does not.

## What changed — landing 4, text and caret

- **`Control.controlHandle` became `Control.rows`**, a `DataHandle[]`. `rows[0]` is the control's own quad;
  `AllocateRow` appends and `TrimRows` releases from the tail. `geometry`/`visual` are `rows[0]`;
  `GeometryAt(i)` / `VisualAt(i)` / `Publish(i)` address the rest.
- `Entity.FreeIn(DataHandle)` — releases one extra row and drops it from `_extraHandles`, which previously
  only ever emptied at destroy.
- `UIEngine.CollectDFS` emits every handle in `rows`; `CountSubtree` sums `rows.Length`; the detached-subtree
  sweep in `DFSOrder` de-duplicates by control, because a run is met once per row it owns.
- **`TextMeasurer.Run` gained `charStart`/`charCount`**, a slice over a shared string. The old
  four-argument constructor still spans the whole string, so every outgoing-stack call site is unchanged.
- **`TextRunControl`** — `text`, `fontName`, `fontSize`, `lineHeight`, `style` and a `List<StyleSpan>` that
  tiles the string in order, the last span absorbing the remainder. `Measure` builds one `TextMeasurer.Run`
  per span and calls `MeasureBlock`; `Arrange` walks the lines and writes one glyph per row at
  `rows[1 + charIndex]`. `IndexAt`, `CaretAt` and `TextOrigin` answer caret questions off the same
  `BlockLayout`.
- **`NextCaretControl`** — the outgoing `CaretControl`'s blink, `Focus`/`Blur` and 2px width, on a `Control`.
- `IGlyphPressTarget` — `TextRunControl.OnPointerPress` resolves the index and walks up to the first parent
  implementing it, so the run never owns a caret.
- **Shader**: the vert passes `fragUV` (per-vertex from the row's `uvs`), `fragTextureIndex` and `fragType`;
  the frag gains the texture table at **set 2, binding 0** and branches on `MTSDFControl`, taking the median
  distance with a true-distance fallback. Both branches share one `dist`/`aa` pair, so the `edge` band is
  one piece of code for the glyph silhouette and the rounded box alike.
- `UIEngineModule` grows the sampler set: `variableSetCount` 2, `GetVariableDescriptorCount`, the descriptor
  indexing features, a `CombinedImageSampler` pool size, `WriteTextureTable` keyed on
  `TextureAsset.TableVersion`, and a `firstSet: 2` bind.
- The scaffolding gains `ProbeDocumentControl` — the smallest thing that can own a caret across a run — and a
  three-span paragraph wrapping at 360.

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

**Hover is one control, not a chain.** Hovering a glyph does not make its document, its panel and the window
root "hovered" — only the deepest hit is. Ancestors hear about it because the event bubbles to them, which is
the same mechanism press and release use, and it is what lights a button when the pointer is over its label.
An earlier draft modelled hover as a set and proposed diffing the old and new chains on every move; there is
no chain to diff. `Exit` fires on the outgoing control, `Enter` on the incoming one, both bubbling.

**The hit-test walks children last to first.** Depth testing is off on both UI pipelines, so what is on top is
purely what was drawn last, and dense order is DFS — a later sibling and its subtree paint over an earlier one.
The old `FindDeepestValid` iterates forward and returns the *first* subtree containing the point, so an
overlapping pair hands the click to the sibling drawn underneath. It never compares candidates; "first" just
means "added first". `WindowControl.AddOverlay` is the case that would bite, and it only escapes because
`SolveHover` re-roots at an open menu before the comparison happens. Reversed here.

**Clip plus an axis-aligned box, not a transformed quad.** The old test builds four corners from the pooled
transform and runs four cross products, with a guard because a collapsed quad passes the edge test for every
point on the plane. Nothing rotates — `WriteArranged` bakes scale and translation, `ArrangeData` has no
rotation field, and the old `TransformToWorld`'s rotation is commented out — so `arranged.Contains` is exact
and a degenerate rect is simply false. `HitsNode` is the single method a future rotation would change.

**The new contexts are `Next`-prefixed.** `A_ActiveContext` registers by name into one dictionary and the last
registration wins, so a second `[A_ActiveContext("ActiveControl")]` takes the old stack's slot — and
`Thorium.contexts.xml` derives `ActiveTabViewer` from `ActiveControl`, so Thorium's tab context would quietly
stop tracking. Same shape as the `VulkanControlData` XSD-name workaround; landing 6 renames both.

**One handler per event, last registration wins.** A multicast `Func<PointerEvent, bool>` returns only the last
delegate's answer, and OR-ing the invocation list allocates an array on every dispatch — including the `Move`
that runs each tick the pointer is over anything. A subclass override is the primary mechanism; the delegate is
for outside wiring. Revisit at landing 6, when it is known what actually registers.

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

**A run keeps a quad of its own, transparent.** `WriteArranged`, `depth`, `ClipRect` and `SetGradientSpace`
all write through `rows[0]`, so a run owning no row of its own would have made four members of the base class
learn about a rowless control — for one saved quad per paragraph. `rows[0]` stays, its tint alpha is zeroed in
the constructor, and the glyphs are `rows[1..]`. The cost is one full-rect alpha-zero quad shaded per run.

**Spans are a slice over one string, not a substring each.** `TextMeasurer.MeasureBlock` already takes a list
of runs and already reports `LineSegment.runIndex`, so N styled spans map onto it one-to-one — but `Run.text`
was a whole string, which would have meant cutting N substrings on every measure, i.e. on every keystroke.
Adding `charStart`/`charCount` to `Run` costs the struct two ints and `Flatten` a changed loop bound, and
`PenChar.charIndex` then comes out absolute in the shared string, which is exactly what the glyph rows and the
caret want. Rejected: one style per run, because a plain `Control` throws on a second child and containers do
not arrive until landing 6 — without spans a paragraph could not have a bold word in it at all.

**Spans tile in order and the last one absorbs the remainder.** A span carrying its own `start` has to be
re-cut every time the text changes and can overlap or leave holes, both of which silently drop glyph rows. A
`(count, style, colour)` list walked with a running cursor cannot express either, and an edit at the end needs
no span edit at all.

**A colour change re-arranges rather than repainting the rows.** `colorHex` and `alpha` are `virtual` on
`Control` and overridden to leave `rows[0]` alone; resolving which span owns a given character outside the
arrange walk would need a per-character span map. `Arrange` already rewrites every glyph row's tint, so the
override invalidates arrange instead — the same full rewrite the outgoing `RepointGlyphs` does.

**The document owns the caret, so the scaffolding grew a document.** The settled model is that a glyph
notifies the document and the document spawns the caret; a run owning its own caret gets the count wrong the
moment a document holds two runs. `ProbeDocumentControl` is scaffolding beside `ProbeControl`, deleted with it
at landing 6. Rejected: a real `DocumentRoot` in `Core.UI` now, which pulls a container forward out of the
landing it is planned in.

**Deferred, and why they are not just missing.** `SelectionControl` is driven by drag, and drag is not ported
— building it now means a class nothing can exercise. `DocumentEditorControl.CaretAtPoint` resolves a point
across *blocks*, and there are no blocks. The XML shape for per-character settings has no reader: the new
stack parses no XML at all yet.

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

**A third type sharing a name with the outgoing stack broke the boot.**
`AssetRegistries.RegisterSerializableTypes` hashes `t.Name` — the *simple* name — and `[@Serializable]` is
inherited, so every `Entity` subclass is registered. `Core.UI.CaretControl` and
`UISystem.Controls.Text.Document.CaretControl` both hash to 334778237 and the second `Add` threw at bootstrap.
Renamed `NextCaretControl`, the same workaround as `VulkanControlData` and the `Next`-prefixed contexts.
Rejected: keying the map on `FullName`, which changes the id of every serialized type and stops every saved
note, scene and session file reading. See [[parallel-stack-name-collisions]].

## Known gaps

- Landings 5–6 are unbuilt. See [../Context/ui-engine-plan.md](../Context/ui-engine-plan.md).
- **`TextRunControl` treats every character as a glyph**, `\n` included, exactly as the outgoing stack does.
  Paragraph breaks are a block concern and blocks arrive at landing 6.
- **No `firstLineOffset` / `lastLineEndX` handshake.** A run measures from x = 0 of its own box; the flow that
  lets a bold run continue a line its predecessor started is a block concern too.
- `IndexAt`/`CaretAt` duplicate `TextControl.OffsetAt`/`CaretAt` while both stacks live. The outgoing copy
  dies at landing 6.
- `Core.UI` compiles against `Core.UISystem` for `TextMeasurer`, `FontStyle`, `Glyph`, `AtlasMetaData` and
  `GlyphControl.CellScale`/`atlasInkMargin`. The font system is not the control stack and should survive the
  landing-6 delete; where it lands is not decided.
- The sampler set serves the MTSDF path only. **No mask branch** — `maskAsset`, and with it a panel with an
  arbitrary silhouette, is landing 5.
- `TextRunControl.spans` is a public `List<StyleSpan>` mutated through `SetSpans`, which re-measures wholesale.
  Nothing edits one span in place yet.
- **Scroll, drag and context-menu gating are not ported.** `SolveScroll` and its `ScrollableControl` walk,
  `ContextMenus.OpenIn` / `DismissedBy`, `SolveDrag` and the whole drop-hint path stay in the old stack.
- `canBeActiveContext` and `takesActiveControl` are not ported either — 6 subclasses override them, so they
  arrive with the landing 6 port. Until then every hit control can take the active context.
- `UIEngine.Bootstrap` builds a scaffolding tree on the primary window. The port at landing 6 removes it.
- **Only `WindowRoot` holds siblings.** A plain `Control` throws on a second child, as the old base does, and
  its `Measure`/`Arrange` handle one. Containers arrive with the landing 6 port; nothing between now and then
  can lay out a list.
- `ArrangeData.width` / `height` are unused — copies of the outgoing stack's vestigial `width`/`height`, which
  default to 72 and are read only by `size`. The layout math uses `preferredWidth`/`preferredHeight`.
- Cumulative child offsets (the drop-targeting cache) are deferred to the landing that reads them.
- `UIEngineModule.UpdateModule` re-records its command buffer every frame rather than on `isDirty`. Landing 1
  scaffolding; it makes the range change land, and it contradicts the record-only-when-dirty rule.
- The gradient table is still absent from `UIEngineModule` and its shaders. It returns as binding 3 — the
  numbering the old shader already uses, so nothing renumbers. `ControlGeometry.gradientRect` and
  `VulkanControl.gradientIndex` are written and read by nothing.
- `UIEngineModule` mirrors the whole pool **per window**. Correct, but wants revisiting before the pool is large.
- `CreatePipeline` is duplicated verbatim from `UIModule`. Collapse at landing 6, not before — sharing it now
  would mean refactoring the module being deleted.
- `DemoteToHelperInvocation` validation errors fire for this shader pair and the old one alike. Pre-existing,
  non-fatal, `discard` under SPIR-V 1.6.

Related: [[ui-data-control-split]], [[entity-transform-split]], [[text-layout-one-measurer]],
[[glyphs-as-pool-data]], [[control-edge-and-outline]], [[mapped-streaming-buffers]], [[gpu-global-frame-data]],
[[ecs-rework-data-pools]], [[parallel-stack-name-collisions]], [[caret-blink-and-focus]],
[[document-selection]]
