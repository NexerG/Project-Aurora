# UI Engine — state and resume point

**Rewritten:** 2026-09-06. **Landing 1 is built and GUI-verified. Landings 2–6 are agreed and unbuilt.**

This file exists so the work can be picked up cold. The decisions and their reasoning are in
[../Decisions/ui-engine-stack.md](../Decisions/ui-engine-stack.md) and
[../Decisions/entity-transform-split.md](../Decisions/entity-transform-split.md); this file is what to *do
next* and what already exists.

The 2026-09-04 version of this file planned an in-place migration of `UIControls` with columns appended to it.
**That approach is dead** — the user chose a parallel build (attempt 3, 2026-09-05/06). Nothing below assumes it.

## Vocabulary — do not get these backwards

| Name | Side | Is |
|---|---|---|
| `Control` | CPU | tree node, logic, layout. One per element |
| `VulkanControl` | GPU | one drawn quad. Struct, kind-tagged, no behaviour |
| `VulkanControlType` | — | `MTSDFControl`, `PanelControl`, `ImageControl` |

One `Control` owns **0..N** `VulkanControl` rows. A panel owns 1; a text run owns one per glyph. The old
`Core.UISystem.Controls.VulkanControl` keeps its name until landing 6.

## What exists now

**Namespace `ArctisAurora.Core.UI`** — `UIData.cs` (the three structs, the enum, `LayoutRect`, `Thickness`,
`QuadUVs`), `Control.cs`, `UIEngine.cs`.

**Pools**, in `Pools.pools.xml`, both `Ordered="true"` with no `SortAction` yet:

| Pool | Columns | Capacity |
|---|---|---|
| `UIElements` | `ArrangeData` | 1024 |
| `VulkanControls` | `ControlGeometry`, `VulkanControlData` | 4096 |

`VulkanControlData` is the XSD name of the `VulkanControl` struct — renamed because `AnyXMLType.FindType`
resolves `[A_XSDType]` by **name alone, first declaration wins**, and the old class already owns
`"VulkanControl"`. Collision disappears at landing 6.

**Row sizes**, printed by `UIEngine.Bootstrap` at boot via `Unsafe.SizeOf`:

```
ArrangeData 140 B   ControlGeometry 96 B   VulkanControl 92 B
```

**`Control : Entity`** — `PoolName => "UIElements"`, second row via `Entity.AllocateIn("VulkanControls")` in an
`AllocatePooledData` override. Exposes `arrange`, `geometry`, `visual` as `ref` accessors, plus
`colorHex` / `alpha` / `cornerRadius` / `edgeColorHex` / `edgeThickness` and `SetArranged(LayoutRect)`.

**`UIEngineModule`** — second `RenderingModule` on every `RenderWindow` (`window.uiNext`, index 1 in
`modules`). `compositorOrder = 10`, transparent clear, so the old stack composites underneath. Mirrors both GPU
columns per swapchain image through `MirrorPool`, one dirty range each.

**Shaders** — `Shaders/UIEngine/UIEngine.vert` + `.frag`, compiled `--target-env=vulkan1.3`, mirrored
byte-identical into `Thorium/`, `AuroraEditor/` and `Carbon/`. Set 0 renderer global, set 1 module
(camera UBO + two `scalar` SSBOs). No sampler set yet.

**`ERendererTypes.UIEngine`** and its `AuroraCamera` case — ortho over the raw swapchain extent.

**Bootstrap** — `<Step Action="UIEngine.Bootstrap"/>` is the last step of `Bootstrap.bootstrap.xml`. It logs
the row sizes and creates a **smoke panel** at `(80, 80, 320, 180)`, `#3AA6FF`, radius 16, white 2 px edge.
That panel is scaffolding and the arrange pass removes it.

## Settled — do not re-litigate without asking

| Decision | Why |
|---|---|
| Parallel build in a new namespace; delete the old at landing 6 | The old stack stays whole and runnable throughout |
| Three data categories: `ArrangeData` CPU-only, `ControlGeometry` + `VulkanControl` on the GPU | Arrange and paint dirty different bytes; today's `ControlData` re-uploads 4.25× more than changed |
| A run is one `Control` emitting one row per glyph (1:N) | Kills the 56.7k-glyph-object ceiling. Per-character style must live in the run's data |
| Hit-test walks the **CPU tree** with a `subtreeBounds` early-out | Strictly tighter than today's inherited-`ClipRect` test |
| Every control is hit-testable; handler returns `true` to consume | Deletes `hitTestable` (15 sites) and ~16 `bubbleXxx` bools |
| Caret: glyph notifies the document, document spawns the caret | User's model. Replaces today's geometry-only `CaretAtPoint` |
| Both insert caches — `subtreeCount`/`subtreeBounds` **and** cumulative child offsets | Tree-insert/collision, and drop targeting |
| One `edge` pair, design pixels, meaning selected by `type` | Two names for two distance fields, neither with a consumer |
| Masks serve all three kinds | A mask is a capability, not a hazard — the branch is "no mask assigned", not "panels never sample" |
| Split entry: `Poll()` at the `HandleUI` site, `PollLayout()` at the `Interpolate` site | Layout must run after `OnTick`, or an `OnTick` invalidation lands a frame late |
| `Control : Entity`, per entity-kind columns | Animation runs on `OnTick` + components. See [[entity-transform-split]] |
| Row building is **incremental per element**, not a per-frame rebuild | ~56.7k glyph rows × 200 B is ~11 MB/frame — not affordable |
| Emit rows for **all** glyphs for now, not visible-only | Visible-only is a strict improvement that drops in at the same seam; it needs a per-document line cache and its own correctness surface |

## Facts that were expensive to establish

**Depth.** The ortho box is z ∈ [−512, −0.01].
`z_ndc = z·(−0.0019531632) − 1.9531632e−05`, so **world z ≤ −0.01 or the near plane clips it**. The old stack
puts a window root at **−10** and steps +0.001 per depth level. `Control.rootDepth = -10f`. A control at z = 0
draws nothing at all.

**`CompositorModule` never specialized `MODULE_COUNT`.** `stages` was built as a `stackalloc` **copy** of
`fragStage`, and `PSpecializationInfo` was assigned to the local afterwards. Fixed to `stages[1].…`. Symptom
if it regresses: a second module renders correctly into its own image and the compositor never samples it —
no validation error, nothing on screen.

**The compositor blends `result = src + result·(1−src.a)` ascending**, so higher `compositorOrder` composites
on top. `UIModule` clears opaque; anything above it must clear transparent.

**`Renderer` drives modules generically** — feature merge, `PrepareObjects`, `CreateOutputImages`,
`CreatePipeline`, `UpdateModule`/`UpdateFrameData`, the command-buffer submit and `RecreateSwapchain` all loop
`window.modules`. Adding a module needs no renderer change. **But `Renderer.PrimaryRendererType` is
`modules[0].rendererType`**, so `ui` must stay at index 0.

**`PoolColumn<T> where T : struct`** — managed references cannot live in a pool column. Any `(start, count)`
scheme for children or components must keep the objects in a plain managed array beside the pool.

**`DataPool.GetRef<T>` costs a `Dictionary<Type, IPoolColumn>` lookup plus a slot indirection per call**, on
top of `AssertOwner` (a `[ThreadStatic]` read and two branches). Hoisting `Backing<T>()` out of a loop removes
the dictionary; a recursive walk that calls `GetRef` per node pays it per node.

**`TextMeasurer` runs from a string and `IGlyphMetrics` alone** — no atlas, no `FontAsset`, no GPU. It already
produces `BlockLayout`, `TextLine`, `LineSegment(runIndex, charStart, charCount, width)` and
`CaretGeometry(x, top, height, baseline)`. A run can be laid out and a caret placed with no glyph object.

**`hitTestable = false` has 15 sites** in the old stack. Fourteen are decorations whose parent handles the
click and which consumed-bubbling covers. The fifteenth, `WindowFrameControl`'s resize grips, hands pixels to a
**sibling drawn behind them** when maximized — bubbling to a parent does not reproduce that. The user has
deferred it to "context logic" rather than `Hide()`.

**Neither `edge` nor `outline` had a single consumer** — no C# control set either, no `.ui.xml` authored either.

## Landings

### 2 — tree and layout

- `Control` uses the inherited `Entity.children` (`List<Entity>`); `AddChild` enforces Control-only; walks cast.
  **Required** — `Entity.Destroy()`'s `EnqueueSubtree` walks that list.
- `Measure`/`Arrange` recursive, per the old shape. `ArrangeData`'s flags byte carries clip / hidden /
  measure-dirty / arrange-dirty. The alignment and dock bytes get their enums here; landing 1 left them bare.
- `subtreeBounds` and `subtreeCount` maintained on the way *out* of `Arrange`.
- Cumulative child offsets on the container object as a `float[]` — the one piece that cannot be a fixed-stride
  pool column.
- A DFS `SortAction` for both pools, mirroring `UILayout.DFSOrder`.
- Per-window `firstInstance`/`instanceCount`, mirroring `UILayout.RefreshWindowRanges`.
- Deletes the smoke panel.

→ *verify:* nested panels, a stack, a clipped scroll region arrange correctly; `subtreeBounds`/`subtreeCount`
equal a brute-force recompute at every node; row ranges stay contiguous in DFS order across an insert, a remove
and a reparent.

### 3 — input

- `UIEngine.Poll(RenderWindow)` at the `HandleUI` site; `UIEngine.PollLayout()` where `ResolveLayout` sits.
- Recursive hit-test with the `subtreeBounds` early-out. Deepest wins, always.
- `struct PointerEvent { Control target; Vector2D<float> point, delta; int button, tapCount; }` — carries the
  original target so an ancestor handler knows what was under the pointer.
- Dispatch walks up until a handler returns `true`.
- Hover/enter/exit, press, release. **Dragging is out** (user, deferred).
- Contexts (`Hovering`, `ActiveControl`, `PressTarget`) keep today's `Context.Set` / `IContext` wiring.

→ *verify:* hover lights a button, click and release fire, deepest wins on overlap, a decoration over a button
does not eat the click.

### 4 — text and caret

- `TextRunControl` holds `text` + `BlockLayout` + per-character style spans, and emits one `VulkanControl` per
  glyph. The single `controlHandle` becomes a `(first, count)` range here.
- `MTSDFControl` path in the shader: sampler set returns as **set 2**, `edge` strokes the MSDF silhouette.
- Press path: `run.OnPress(point)` → `layout.IndexAt(point)` → `document.GlyphPressed(run, index)`. The document
  owns the caret and positions it from `CaretGeometry`.
- `CaretControl`, `SelectionControl` and `DocumentEditorControl.CaretAtPoint` are rebuilt, not ported.
- **New XML shape needed**: per-character settings (bold, colour, italic, size, animation) as spans over a
  string. Nothing in today's document format expresses it.

→ *verify:* a paragraph renders with one `Control` per run and one `VulkanControl` per glyph; pressing a glyph
puts the caret on the correct side of it, at every line's start, end and wrap point.

### 5 — images and icons

- `ImageControl` path; `textureIndex` and `uvs` become live for all three kinds.
- Gradient table returns as binding 3.

→ *verify:* an icon and a texture render; a control with no mask assigned takes no texture sample.

### 6 — port and delete

- Port the 39 `VulkanControl` subclasses to `Control`.
- Delete `Core.UISystem`, the `UIControls` pool, `UIModule`, `MCUI`, `UI.vert`/`UI.frag`.
- Rename the `VulkanControlData` XSD type back to `VulkanControl`.
- Collapse the duplicated `CreatePipeline`.
- Regenerate `NAMESPACES.md`.

→ *verify:* Thorium boots entirely on the new stack.

**Handoff (CLAUDE.md §10):** landings 2–4 stay with the model — layout correctness, dispatch, Vulkan. Landing 5
and the mechanical half of 6 (39 subclasses, the 15 `hitTestable` sites) go to a subagent once the base is
frozen and old/new text is exact.

## Open — not decided

- **`ContextMenus.menuFactory` must die** (user, explicit). One override, `Thorium.cs`, supplying a
  `WindowedContextMenuControl` with six hardcoded hex colours. Deleting the field alone breaks Thorium's menu
  styling; a replacement (theme roles + a windowed-vs-inline setting) was proposed and not yet approved.
- **`WindowFrameControl`'s maximized grips** — deferred to "context logic" (user, 2026-09-06).
- **Children and components as `(start, count)` ranges** — analysed, parked (user: "for now do nothing with
  this"). Not a slowdown; the cost is that mutation stops being O(1) and settles at the frame edge.
- **`_components` / `children` lazy allocation** — user chose *neither* (2026-09-06). 64 B/entity stands.
- **Ticking as a group** — [[entity-tick-group]], raised and parked.
- **`DataPool.Write<T>(handle, value)`** — the missing assign-plus-`MarkContentDirty` primitive.
- Whether per-window mirrors in `UIEngineModule` should become shared.
- Whether visible-only glyph expansion lands, and on what evidence.

## How to run and verify

Launch from the exe's own folder — `Paths.GetPath` resolves `..\..\..` against the process working directory:

```
Thorium/bin/Debug/net10.0-windows10.0.22621.0/Thorium.exe
```

Capture: `SetWindowPos(hwnd, HWND_TOPMOST, …, 0x43)` → `CopyFromScreen` → `SetWindowPos(…, HWND_NOTOPMOST, …)`.
`PrintWindow` returns blank on a Vulkan surface. See the `aurora-verify` skill.

Shaders: edit the `AuroraEngine` copy, `glslc --target-env=vulkan1.3`, mirror the source *and* the `.spv` to
`Thorium/`, `AuroraEditor/` and `Carbon/`, then confirm byte-identical. See the `shader-pipeline` skill — note
its paths still say `Periodic`, which is now `Thorium`, and it does not mention `Carbon`.

Related: [[ui-engine-stack]], [[entity-transform-split]], [[entity-tick-group]], [[ui-data-control-split]],
[[text-layout-one-measurer]], [[glyphs-as-pool-data]], [[ecs-rework-data-pools]], [[thorium-editor-architecture]]
