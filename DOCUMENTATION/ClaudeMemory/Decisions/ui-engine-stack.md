# Decision — the UI is rebuilt beside the old one, not migrated in place

**Date:** 2026-09-06
**Status:** **PARTIAL** — landings 1–5 built and GUI-verified; landing 6 agreed, not built.
**Scope:** `ArctisAurora.Core.UI` — `UIEngine`, `Control`, `WindowRoot`, `TextRunControl`, `StyleSpan`,
`IGlyphPressTarget`, `NextCaretControl`, `ArrangeData`, `ControlGeometry`, `VulkanControl`,
`VulkanControlType`, `ArrangeFlags`, `HorizontalAlignment`, `VerticalAlignment`, `DockMode`, `PointerEvent`,
`PointerPhase`;
`ArctisAurora.Core.ECS.EngineEntity` — `Entity.FreeIn`;
`ArctisAurora.Core.UISystem.Controls.Text.Document` — `TextMeasurer.Run`;
`ArctisAurora.Core.UISystem` — `Gradients`, `GpuGradient`;
`ArctisAurora.EngineWork.Rendering.Modules` — `UIEngineModule`, `CompositorModule`;
`ArctisAurora.EngineWork.Rendering` — `AuroraCamera`, `AGlfwWindow`;
`Shaders/UIEngine/UIEngine.vert`, `Shaders/UIEngine/UIEngine.frag`;
`AuroraEngine/Data/XML/Documents/Pools.pools.xml`, `Bootstrap.bootstrap.xml`

**Supersedes in approach:** [ui-data-control-split](ui-data-control-split.md), which planned an in-place
migration of `UIControls`. The resume plan lives in
[../Context/ui-engine-plan.md](../Context/ui-engine-plan.md).

> **Superseded in part by [ui-draw-list](ui-draw-list.md) (2026-09-07).** The `VulkanControls` pool is
> gone: `ControlGeometry` and `VulkanControl` are fields on `Control`, and a per-window DFS walk emits
> the visible ones into a `DrawList`. Everything below about draw *rows*, `rows[]`, `AllocateRow`,
> `Publish` and per-window instance ranges describes storage that no longer exists — the geometry,
> the shaders and the layout it feeds are unchanged.

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

## What changed — landing 5, images, icons and gradients

**No struct changed.** `uvs`, `textureIndex` and `gradientIndex` were already in `VulkanControl` and
`gradientRect` already in `ControlGeometry`, all written by nothing. The row sizes are still 140 / 96 / 92.

- **`VulkanControl.noTexture`** = `uint.MaxValue`. Table slot 0 is a real texture (`defaultMask.png`), so 0
  cannot mean "none". `Control`'s constructor writes the sentinel and a full-rect UV set into `rows[0]`.
- **`Control.sampler`** (`TextureAsset?`), **`Control.kind`** (`VulkanControlType`), **`Control.gradient`**
  (`string` → `Gradients.IndexOf`) and **`Control.SetUVRect(u0, v0, u1, v1)`**, which lays the four corners
  out in the quad mesh's vertex order — `uv1` is the far corner, the order `TextRunControl.WriteGlyph` uses.
- **Frag**: the gradient table at **set 1, binding 3** with `sampleGradient` copied verbatim from
  `UIRasterizer/UI.frag`; an `ImageControl` branch multiplying the texel into colour *and* alpha; a
  `PanelControl` mask branch multiplying the MTSDF coverage in last. Both texture reads sit behind
  `fragTextureIndex != NO_TEXTURE`, and `fragType` is `flat`, so the derivatives inside `msdfDistance` stay
  in primitive-uniform control flow.
- **Vert**: two more flat outs, `fragGradientIndex` and `fragGradientRect`, off fields already declared.
- **`UIEngineModule`**: binding 3 appended to the four parallel set-1 lists as a `FragmentBit` storage
  buffer, the storage pool size 2 → 3, and a **static** `_gradientBuffer` built once in `PrepareObjects`.
  Static because the table is per-process and never changes after bootstrap; **not** freed in
  `DestroyGpuResources`, or closing one window would dangle every other window's descriptor.
- Scaffolding gains `image` (the icon atlas as flat colour, Center/Top), `icon` (the `folder` cell through
  the MTSDF path, Right/Center) and `swatch` (the `accent` gradient, Right/Bottom).

## What changed — landing 6a, the base gap

The base the port lands on, frozen before any subclass moves. No subclass ported, no `Core.UISystem` file
touched, no old file deleted — 6a is additive so the tree still builds and boots on its own.

- **`Control` parses XML.** `ControlXml.cs` is a second file of the same `partial class Control`, holding
  `ParseXML` / `Parse` / `RecursiveParse` / `ResolveAttributes` ported from `VulkanControl`. Partial rather
  than a separate static class because `Parse` writes a `WindowRoot`'s first arranged rect through the
  `protected` `WriteArranged`. The `WindowControl` special case becomes `WindowRoot`: `WriteArranged` at the
  authored size plus `RegisterDirtyRoot`, where the old one also wrote a transform the new stack has not got.
- **`Control` is `[A_XSDType("NextVulkanControl", "EntityRegistry", isAbstract: true)]`**, `WindowRoot` is
  `NextWindow`, `ContainerControl` is `NextContainer` — `Next`-prefixed, [[parallel-stack-name-collisions]].
- **~25 authored members gained `[A_XSDElementProperty]`** under the old attribute names, so the same
  `Width`, `Padding`, `ColorHex`, `HorizontalAlignment` an existing `*.ui.xml` writes bind unchanged.
- **`ContainerControl`** — multi-child `AddChild`, stretch defaults. `AbstractContainerControl`'s replacement,
  and like it, it does **not** override `Measure`/`Arrange`; a container that lays out more than one child is
  a subclass, and those arrive at 6b.
- **`CornerRadii` restored** (`topLeft`, `topRight`, `bottomLeft`, `bottomRight`) with its `TypeConverter`,
  replacing landing 1's single float. `Thickness` gained its two- and four-argument constructors and
  `ThicknessConverter`, without which `Margin="8,4"` cannot parse at all.
- **`ControlColor`** and `EnumColorToHex` ported. Untagged, like the alignment and dock enums —
  `ResolveAttributes` parses an enum by reflection, never through `AnyXMLType`.
- **The gap the subclasses call**: `width`/`height`/`size`/`SetSize`/`SetWidth`/`SetHeight`, `hitTestable`
  (now honoured by `UIEngine.HitTest`), `canBeActiveContext`, `takesActiveControl`, `FindByName`.
- **`UIEngine` took `SolveScroll` and `ActiveTarget`** out of `UICollisionHandling` — the wheel and the
  walk-up to whatever may hold the active context, the latter gating `SolvePress` on `takesActiveControl`.
- **`PointerPhase.Scroll`** and `Control.OnPointerScroll`, so the wheel walks up like every other phase and
  the `ScrollableControl` special case the old `SolveScroll` carries becomes a subclass override at 6b.
- The landing 2–5 scaffolding is gone. In its place `BuildProbe` parses **`NextProbe.ui.xml`**, which is the
  only thing XML can build until 6b tags the subclasses, and registers one delegate handler on it.

**Context menus were built and then removed** (user, 2026-09-06) — `contextMenus`, `BuildContextMenu` and
`OpenContextMenu` arrive with the controls that host them, and their removal also took the
`ContextMenuBuilder` coupling to `Core.UISystem` back out.

**The drag runs end to end, minus delivery to its claimant.** A left press on a control whose `draggable` is
set calls `StartDrag()` from the base `OnPointerPress`, which sets the `NextDragging` context and tells the
parent `ChildDraggedOut`. `UIEngine.CheckDrag` runs each tick from `Poll` to find what the drag is over. A
left release calls `UIEngine.EndDrag(point)` **ahead of the release guards**, because a drag ends wherever the
pointer is and that is rarely still over the control the press landed on; `EndDrag` fires `DraggingOverEnd`,
then `Control.FinishDrag(dragged, point)` on the target, then clears both the target and `NextDragging`.

**`draggable` is a flag rather than an override, and the press wiring is in the base.** `StartDrag()` stays
public so a control can claim a drag from something other than a plain press — the old stack's five in-place
draggers (splitter, scroll thumb, text selection, text box, Carbon's chart) all claim from their own handlers
and want no flag. The flag exists so the base can do it for the tear-out case without every control becoming
draggable. Gated on the left button, because `OnPointerPress` fires for both and nothing drags with the right.

**One control is the drag target, exactly as one control is hovered**, and the events bubble from it:
`DraggingOverStart` when the drag arrives, `DraggingOver` every tick it stays, `DraggingOverEnd` when it
leaves — each walking up until one returns true, the same shape every other event on this stack uses. The
target is the deepest hit, *not* whoever consumed the event, which is the same split
[[button-states-and-hover-bubbling]] settles for hover: identity is the hit, notification bubbles.
`UIEngine.FinishDrag()` ends the pair by firing `DraggingOverEnd` and clearing the target; it does not yet
clear `NextDragging`, offer the drop or tell the claimant, and nothing calls it.

**`ChildDraggedOut` is the reparenting hook, not the exit half of the over-trio.** A container is told which
child a drag took out of it — a tab strip losing a tab is the case — and it fires from `StartDrag`, while the
child is still parented, so "out" means the gesture began rather than that a detach happened. Only the parent
side exists: the dragged control needs no telling, since it is the one being dragged. A matching
`DraggedOut(oldParent)` on the child was added and removed the same day (user, 2026-09-06).

**`CheckDrag` reuses the hover's own walk rather than the old stack's geometry search.** `HitTest` gained a
`skip` parameter, rejected at the subtree root so nothing beneath it is reached either, and `CheckDrag`
passes the dragged control — which is under the pointer by definition and would otherwise answer every time.
Skipping the node alone was rejected: a dragged tab's label is also under the pointer and would take the hit
in its place. The old stack does not skip at all, and only gets away with it because `TabViewControl` is an
ancestor of the button being dragged, so the walk up reaches the right answer by accident.

This is a different mechanism from the old `HitFor`, which finds the target by screen geometry — screen
point, the window whose rect holds it, that window's tree — because a captured pointer means no other window
is ever told it is being hovered. `CheckDrag` works inside one window only. Dragging *between* windows still
wants `HitFor`, `WindowAt` and `Poll`'s `ownsDrag` exemption.

## The active context is a question, not a flag — 6b0, 2026-09-06

`canBeActiveContext` was a bool and `UIEngine.ActiveTarget` walked the parent chain over it. It is now
`Control.ActiveContextTarget()`, returning the control that takes the context — `this` by default, and a
decoration answering `(parent as Control)?.ActiveContextTarget()`.

**Why, given the bool covered every override that exists.** All three old overrides (`Label`, `Glyph`,
`Icon`) mean "not me, walk up", which the bool says fine. The case it cannot say is 6c's: `TextBoxControl`,
`TextInputControl` and `DocumentEditorControl` each wrap a run in padding and chrome, and pressing the
padding should focus **the run** — a child. A bool can only decline; the walk only goes up. The migration
cost is symmetric either way, so there was no reason to defer it to the landing that needs it.

**What it costs.** `grep canBeActiveContext` used to show every opt-out with its answer on the same line;
now each override's body has to be read. And two overrides pointing at each other is an infinite recursion
where a `while` over `parent` could not loop — it fails as a stack overflow in the pointer path. Accepted
unguarded: tree depth is small and a cycle shows up on the first frame. An alternative was considered and
rejected — overrides return one hop and `UIEngine` iterates to a fixed point with a depth cap, which keeps
termination in one place at the cost of more machinery in the engine.

**What makes it observable.** `SolveRelease` drops a release whose `ActiveContextTarget()` differs from
`pressTarget`. So a button whose caption does *not* override it fires only when press and release land on
the same one of the two — press the glyphs, release on the button's padding, and nothing happens. That is
the negative control that was actually run, not reasoned about.

## Ports — 6b1, 2026-09-06

- **`NextButtonControl` overrides `colorHex` rather than writing the tint.** The old button kept the authored
  colour in `controlColorHex` and wrote state into `controlData.style.tint` directly. On the new stack
  `colorHex` *is* the tint writer, so the override keeps the authored value in a private `restColorHex` and
  `ApplyState` assigns `base.colorHex`. `get` returns the authored colour, not the shown one.
- **The pointer overrides call base and then return `true` unconditionally.** Base invokes the registered
  delegate — which is where an XML `onRelease` fires — and the button consumes regardless, because a button
  owns its own pointer events.
- **`NextStackPanel`'s enum is `"NextOrientation"`.** Same `FindType`-resolves-by-name collision as
  `VulkanControlData`; the attribute stays `Orientation=` because attribute names are per-type.
- **`WindowRoot.Arrange` ignores its children's `margin`.** It positions by alignment inside the padded box
  and never reads `ca.margin`. Pre-existing — `NextProbe.ui.xml`'s `swatch` carries a `Margin` that does
  nothing — and it means a root child cannot be offset by margin.

## A composited module's alpha factor must be One, not SrcAlpha — 2026-09-07

`UIEngineModule`'s blend attachment had `SrcAlphaBlendFactor = SrcAlpha` alongside
`DstAlphaBlendFactor = OneMinusSrcAlpha`, so the alpha channel accumulated `a_src² + a_dst(1−a_src)`
instead of `a_src + a_dst(1−a_src)`. A glyph edge at 0.5 coverage over an opaque panel came out at alpha
**0.75**, and the compositor's `result = src + result·(1 − src.a)` then let 25 % of whatever the module
below drew bleed through every antialiased edge. Against the old stack drawing the same title bar at the
same coordinates that reads as a sub-pixel double image — text that looks chunky rather than soft.

**`UIModule` carries the identical mistake and it is inert there**, because it is composited first and its
alpha never reaches the output. Do not copy its blend state when the two `CreatePipeline` bodies are
collapsed at 6d — copy `UIEngineModule`'s.

**Not the glyph maths.** Both fragment shaders resolve MTSDF identically — `screenPxRange * (sd - 0.5)`
from `pxRange = 4.0` and `fwidth(fragUV)`, with the same median-vs-true-distance reconciliation — so
`aa = 1.0` in the new shader is correct, the distance already being in screen pixels.

**Still unverified:** the compositor samples module outputs with `Filter.Linear`. That is free only if the
offscreen extent matches the swapchain's and the fullscreen triangle lands on texel centres; nobody has
checked that they do.

## XML event attributes — landing 6b prerequisite, 2026-09-06

`Control`'s seven pointer handlers carry `[A_XSDElementProperty("onEnter"…"onScroll", "UI")]` and bind from a
`*.ui.xml` the way the old stack's `Action` fields did. Three edits made it work:

- `XSDGenerator.IsAttributeMember` returns true for any `Delegate`, and `ResolveTypeName` emits
  `actions:{Category}` for one — replacing the `mapped == "Action"` special case. `AnyXMLType.typeMap` is left
  alone: registering `Func<PointerEvent, bool>` there would put a `Core.UI` type inside `Core.Registry`.
- `ControlXml.ResolveAttributes` gained a `Func<PointerEvent, bool>` branch that resolves the tagged method as
  an `Action`, wraps it `_ => { act(); return true; }`, and combines with `+=`.

**Signatures could not change.** The tagged-action pool feeds three binding sites with three shapes —
keybinds take `Action`, `ContextMenus.BindAction` picks `Action` or `Action<VulkanControl>` via
`TakesTarget`, and `*.ui.xml` takes `Action` — and only one of them has a `PointerEvent` to hand over. Every
one of the nine methods bound from a `*.ui.xml` is `public static void X()`. Adapting at the binding site is
what `ContextMenus` already does.

**The wrapper returns `true`, so an XML-bound handler consumes.** Right for a button; a non-consuming XML
handler would be a new attribute, not a flag on this one.

**`+=` on a `Func<,bool>` is multicast, so only the last registration's return value survives.** Harmless as
wired — `ResolveAttributes` runs after the constructor, so an XML handler is appended after a C# one and its
`true` is the answer. It bites the other way round.

**`MakeGenericType` arity is a live trap in this file.** Three sites do
`typeof(IEnumerable<>).MakeGenericType(memberType.GetGenericArguments())` to ask "is this a collection", which
**throws** on any member whose type has two type arguments. Nothing tagged had two until `Func<PointerEvent,
bool>` did, and `XSDGenerator.ReferencedTypeOf` crashed the whole boot from `GenerateXSD`. Guarded there with
`GetGenericArguments().Length == 1`. The third site, in `GenerateComplexType`, is unreachable for a delegate
because `IsAttributeMember` now returns true first — a tagged `Dictionary<K,V>` would still reach it.

## The drag gap

The gesture runs start to finish; what is missing is everything told to the **claimant**. Seven controls claim
a drag in the old stack —
`TabStripButtonControl`, `ScrollThumbControl`, `SplitterControl`, `DocumentEditorControl`, `TextBoxControl`,
`WindowFrameControl` (on a grip) and Carbon's `SpanChartControl` (twice). **Five of them need only per-tick
delivery**; the drop, hint and ghost machinery exists for tab dragging alone — `TabViewControl` is the only
`ResolveDrop` / `ResolveDropHint` / `ClearDropHint` implementor in the repo, and `TabStripButtonControl` the
only `DragGhost.Show` caller.

| Missing | What it is | Wanted by |
|---|---|---|
| `Control.onDrag`, `RegisterOnDrag`, `ResolveDrag` | the per-tick callback | all 7 |
| `UIEngine.SolveDrag`, called from `Poll` | delivers it each tick, and holds the **stale-release guard** — `Poll` returns early while the pointer is outside the window, so a button released out there never reaches `SolveRelease` and `justReleased` is gone by the next tick; without the guard the drag stays live for good | all 7 |
| `Control.onDragStop`, `RegisterDragStop`, `StopDrag` | the end-of-gesture callback **on the claimant**. `EndDrag` tells the target, never the thing being dragged | all 7 |
| the **stale-release** guard | `Poll` returns early while the pointer is outside the window, so a button released out there never reaches `SolveRelease`, `justReleased` is gone by the next tick, and the drag stays live for good. The old `SolveDrag` checked the button's real state every tick; nothing does now | all 7 |
| `Poll`'s `ownsDrag` exemption + `UIEngine.WindowOf` | the press captures the pointer to the window it went down in, so that window must keep polling once `isInWindow` goes false. Without it `CheckDrag` stops at the window edge and the release is never seen | all 7 |
| `UIEngine.HitFor` + `WindowAt` | `CheckDrag` across windows, where the hover walk cannot reach — found by **geometry, not hover**, since no other window is told the pointer is over it. Screen point → the window whose rect holds it (active preferred, ghosts and closing windows skipped) → that window's tree | tabs |
| `UIEngine.OfferDrop` + `Control.ResolveDrop` | superseded — `EndDrag` calls `Control.FinishDrag` on the target. What it does **not** do is walk up: the target either handles the drop or it is lost, where `OfferDrop` offered each ancestor in turn | tabs |
| `Control.ResolveDropHint` | superseded — the `DraggingOverStart`/`Over`/`End` trio is the per-tick hint, and `_dragTarget` replaced the `NextHinted` context. A field, not a context, because nothing outside `UIEngine` asks yet | — |
| `UIEngine.RaiseHovered` | brings the window under the drag forward, once per crossing; needs the active-window latch | tabs |
| `DragGhost` + `Control.draggingOpacity` | the preview window. A second view of the dragged control's own pool rows, not a copy — needs a `rangeRoot` on `UIEngineModule`, which does not exist, and `DragGhost.Follow` is called from `Engine.HandleUI` | tabs |

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

**One sampler slot per row, its meaning selected by `kind`** — the distance field on `MTSDFControl`, a
coverage mask on `PanelControl`, the colour source on `ImageControl`. Same shape as the `edge` pair below, and
for the same reason: one slot with three readings beats three slots with one consumer each. The cost is real
and was taken knowingly — **an image cannot also carry a mask**, because there is one `textureIndex` and one
UV set. The fix, if something ever wants both, is a second index plus a second UV set, which grows
`VulkanControl` from 92 B and moves both `scalar` strides. Nothing in the tree wants it.

Named `sampler` by the user over `texture` and `maskAsset`, both of which name one of the three readings.
`SamplerAsset` and `Silk.NET.Vulkan.Sampler` already exist in the same render path — no C# ambiguity, since
`Core.UI` imports neither, but the word now means two things there.

**`uint.MaxValue` as the "no texture" sentinel, not a reserved slot 0.** Gradients could reserve slot 0
([[ui-gradients]]) because that table is built by one loader. The texture table is filled in asset-load order
and slot 0 is `defaultMask.png`, a real texture the old stack still resolves `maskAsset = null` to. Reserving
it would have meant a null texture, a change to `TextureAsset`, and a behaviour change in the stack being
deleted.

**The mask multiplies last, after the edge band.** A mask is the final silhouette, so it cuts the stroke along
with the fill — a masked panel with an edge gets the edge clipped to the mask's shape rather than a rounded
rectangle's outline floating around an arbitrary silhouette. Verified by pointing `card` at the `close` cell:
the whole quad, edge band included, collapsed to the glyph.

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

## Why these choices — landing 6a

**The port cannot go beside the old stack under its own names, so 6a froze the base instead of moving
anything.** `RegisterSerializableTypes` keys on the simple name and `[@Serializable]` is inherited, so
`Core.UI.PanelControl` and `Core.UISystem.Controls.PanelControl` cannot both exist —
[[parallel-stack-name-collisions]]. The user chose the `Next` prefix over move-and-delete (2026-09-06), which
keeps the build green through 6b at the cost of renaming ~56 classes back at 6d. 6a therefore adds only what
has no counterpart to collide with, and every XSD name it does introduce is already prefixed.

**XML parsing came across verbatim rather than being rebuilt around the new event model.** `ResolveAttributes`
binds an `Action`-typed member from a tagged static method; `Control`'s handlers are
`Func<PointerEvent, bool>`, so that branch is inert and no XML event attribute binds yet. Rebuilding it would
have meant settling what `BubbleClick="true"` means when bubbling is "return false" — a fork worth one
round-trip, not a silent choice. Everything else an existing `*.ui.xml` writes binds today.

**`ContainerControl` is concrete where `AbstractContainerControl` is abstract**, purely so the probe document
has an element to instantiate. It costs nothing: a container with no layout override behaves exactly as a
plain `Control` with the one-child restriction lifted.

**`Thickness` needed its converter before anything else could be verified.** It was not in the agreed file
list — `TypeDescriptor.GetConverter` on the landing-1 `Thickness` returns the default, which cannot read
`"16"`, so `Padding` and `Margin` silently fail and the XML pass proves nothing.

**Drag and context menus came out after being built.** Both were in the agreed 6a list and both worked — the
drag lifecycle was driven end to end with synthetic input before removal. The user took them out (2026-09-06)
because neither has a consumer until the controls that use them are ported, and both are shaped by the open
event-binding fork: `onDragStop` is `Action`-typed, so it would have been the one ported event able to bind
from XML today, and `contextMenus` is read by `ContextMenus.Compose`, which is still typed to
`VulkanControl`. Freezing them now would have settled that fork by accident.

## Known gaps

- Landings 6b–6d are unbuilt. See [../Context/ui-engine-plan.md](../Context/ui-engine-plan.md).
- **No XML event attribute binds.** `onClick`, `onEnter`, `onScrollUp` and the `Bubble*` flags have no
  member on `Control` to bind to, and the shape they should take is the open fork above. Handlers reach the
  new stack through `RegisterOnPress` / `RegisterOnDrag` / `RegisterOnScroll` in C# only.
- **`WindowRoot.Arrange` ignores a child's `margin`.** Landing 2 behaviour, found by 6a's probe when a
  `Margin="0,0,56,0"` swatch arranged flush to the padding edge. Not 6a's to fix; a container that honours
  margins is what 6b brings.
- **A drag never reaches its claimant.** The target hears everything; the thing being dragged hears nothing —
  no per-tick position, no end-of-gesture callback. See *The drag gap* above.
- **A release outside the window leaves the drag live for good**, because `Poll` returns early and nothing
  re-checks the button's real state. The `ownsDrag` exemption and the stale-release guard are one fix.
- **`EndDrag` does not walk up.** `Control.FinishDrag` is called on the drag target alone, where the old
  `OfferDrop` offered each ancestor in turn until one took it.
- **`EndDrag` clears `NextDragging` after the callbacks**, not before, so a handler that reads the context
  rather than its `dragged` argument still sees a live drag. Both handlers are given `dragged` outright.
- **`draggable` on an ancestor still fires**, because the press bubbles — a `draggable` container drags when
  a child does not consume the press. Correct, and worth knowing before setting the flag high in a tree.
- **A subclass overriding `OnPointerPress` without calling base never drags**, flag or not.
- **`ChildDraggedOut` has no caller from a real reparent** — `StartDrag` fires it while the child is still
  attached. Nothing on the new stack detaches anything.
- Nothing opens a menu; `ContextMenus`, `DragGhost` and `pressSwallowed` stay in the old stack.
- **`ContextMenu="…"` in a `*.ui.xml` binds to nothing** and is silently skipped — unmatched attributes are
  ignored by `ResolveAttributes`, not an error. Seven live sites across `UI.ui.xml` and `Workspace.ui.xml`.
- **`TextRunControl` treats every character as a glyph**, `\n` included, exactly as the outgoing stack does.
  Paragraph breaks are a block concern and blocks arrive at landing 6.
- **No `firstLineOffset` / `lastLineEndX` handshake.** A run measures from x = 0 of its own box; the flow that
  lets a bold run continue a line its predecessor started is a block concern too.
- `IndexAt`/`CaretAt` duplicate `TextControl.OffsetAt`/`CaretAt` while both stacks live. The outgoing copy
  dies at landing 6.
- `Core.UI` compiles against `Core.UISystem` for `TextMeasurer`, `FontStyle`, `Glyph`, `AtlasMetaData`,
  `GlyphControl.CellScale`/`atlasInkMargin` and `Gradients`. Neither the font system nor the gradient table is
  the control stack and both should survive the landing-6 delete; where they land is not decided.
- **An image with a mask is not expressible** — one `textureIndex`, one UV set, meaning chosen by `kind`.
- **A gradient on an `ImageControl` replaces the texture**, because the ramp assigns `color` outright after
  the image multiply. Consistent with the old stack, and untested — nothing sets both.
- **No `IconControl` or `ImageControl` C# subclass.** The kinds are reached through `Control.kind` +
  `sampler` + `SetUVRect`; the atlas cell arithmetic is duplicated between `TextRunControl.WriteGlyph`, the
  outgoing `IconControl.Rebind` and `UIEngine.SetIconCell`. The landing-6 port is what collapses it.
- **`Gradients.Table` is uploaded once and never re-uploaded**, same gap [[ui-gradients]] records; the new
  module's writer is `UIEngineModule.CreateGradientTable`.
- `_gradientBuffer` is never destroyed — process lifetime, matching `MCUI`.
- `TextRunControl.spans` is a public `List<StyleSpan>` mutated through `SetSpans`, which re-measures wholesale.
  Nothing edits one span in place yet.
- **Only the wheel came across.** `SolveScroll`'s `ScrollableControl` walk did not — the wheel now bubbles as
  a phase and the container consumes it, which is a 6b subclass override.
- `UIEngine.Bootstrap` parses a probe document into the primary window. 6b removes it with `NextProbe.ui.xml`.
- **Only `WindowRoot` and `ContainerControl` hold siblings**, and neither lays more than one child out —
  `WindowRoot` aligns them all in its own box, `ContainerControl` inherits the one-child `Measure`/`Arrange`.
  A container that arranges a list is a 6b subclass.
- `ArrangeData.width` / `height` back `Control.width`/`height`/`size`, which exist for the subclasses that
  call them and are read by nothing in the layout math — that uses `preferredWidth`/`preferredHeight`.
- Cumulative child offsets (the drop-targeting cache) are deferred to the landing that reads them.
- `UIEngineModule.UpdateModule` re-records its command buffer every frame rather than on `isDirty`. Landing 1
  scaffolding; it makes the range change land, and it contradicts the record-only-when-dirty rule.
- `UIEngineModule` mirrors the whole pool **per window**. Correct, but wants revisiting before the pool is large.
- `CreatePipeline` is duplicated verbatim from `UIModule`. Collapse at landing 6, not before — sharing it now
  would mean refactoring the module being deleted.
- `DemoteToHelperInvocation` validation errors fire for this shader pair and the old one alike. Pre-existing,
  non-fatal, `discard` under SPIR-V 1.6.

Related: [[ui-data-control-split]], [[entity-transform-split]], [[text-layout-one-measurer]],
[[glyphs-as-pool-data]], [[control-edge-and-outline]], [[mapped-streaming-buffers]], [[gpu-global-frame-data]],
[[ecs-rework-data-pools]], [[parallel-stack-name-collisions]], [[caret-blink-and-focus]],
[[document-selection]], [[ui-gradients]]
