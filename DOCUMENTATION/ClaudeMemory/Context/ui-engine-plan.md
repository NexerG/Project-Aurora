# UI Engine — state and resume point

**Rewritten:** 2026-09-06. **Landings 1–3 are built and GUI-verified. Landings 4–6 are agreed and unbuilt.**

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

**Namespace `ArctisAurora.Core.UI`** — `UIData.cs` (the three structs, the enums, `LayoutRect`, `Thickness`,
`QuadUVs`), `Control.cs`, `WindowRoot.cs`, `PointerEvent.cs`, `UIEngine.cs`.

**Pools**, in `Pools.pools.xml`, both `Ordered="true"`:

| Pool | Columns | Capacity | SortAction |
|---|---|---|---|
| `UIElements` | `ArrangeData` | 1024 | `UI.NextElementOrder` |
| `VulkanControls` | `ControlGeometry`, `VulkanControlData` | 4096 | `UI.NextControlOrder` |

`VulkanControlData` is the XSD name of the `VulkanControl` struct — renamed because `AnyXMLType.FindType`
resolves `[A_XSDType]` by **name alone, first declaration wins**, and the old class already owns
`"VulkanControl"`. Collision disappears at landing 6.

**Row sizes**, printed by `UIEngine.Bootstrap` at boot via `Unsafe.SizeOf`:

```
ArrangeData 140 B   ControlGeometry 96 B   VulkanControl 92 B
```

**`Control : Entity`** — `PoolName => "UIElements"`, second row via `Entity.AllocateIn("VulkanControls")` in an
`AllocatePooledData` override. Exposes `arrange`, `geometry`, `visual` as `ref` accessors, the authored layout
properties over `ArrangeData`, `colorHex` / `alpha` / `cornerRadius` / `edgeColorHex` / `edgeThickness`,
`Measure` / `Arrange` / `WriteArranged`, `InvalidateLayout` / `InvalidateArrange`, `Hide` / `Show` and
`RefreshSubtreeCache`. **`AddChild` throws on a second child**, as the outgoing base does.

**`WindowRoot : Control`** — the only node that holds siblings. Carries `WindowingMode`, `autoscaling`,
`ScalingAxis`, `ViewportSize`, `FitTo`, `ToDesignSpace`, and a `Measure`/`Arrange` that loops children by
alignment. Transparent (`alpha = 0f`) because there is no invisible mask to opt out with yet.

**`UIEngine`** — `RegisterDirtyRoot` / `ResolveLayout` at the `Interpolate` site, `Poll` at the top of
`HandleUI`, `HitTest` / `Dispatch` / `Forget` / `SetActiveControl`, the `NextHovering` / `NextActiveControl` /
`NextPressTarget` contexts, `RefreshWindowRanges` at the frame edge, the two `PoolSort` actions, and
`BuildScaffolding`.

**`PointerEvent`** (`target`, `point`, `delta`, `button`, `tapCount`) and **`PointerPhase`** — `Control` has one
`virtual bool OnPointerX(PointerEvent)` and one `Func<PointerEvent, bool>` per phase, and `RegisterOnX` sets
rather than combines.

**`UIEngineModule`** — second `RenderingModule` on every `RenderWindow` (`window.uiNext`, index 1 in
`modules`). `compositorOrder = 10`, transparent clear, so the old stack composites underneath. Mirrors both GPU
columns per swapchain image through `MirrorPool`, one dirty range each. Holds `uiRoot`, `firstInstance` and
`instanceCount`; the draw is the window's slice. A window with no root draws nothing.

**Shaders** — `Shaders/UIEngine/UIEngine.vert` + `.frag`, compiled `--target-env=vulkan1.3`, mirrored
byte-identical into `Thorium/`, `AuroraEditor/` and `Carbon/`. Set 0 renderer global, set 1 module
(camera UBO + two `scalar` SSBOs). No sampler set yet.

**`ERendererTypes.UIEngine`** and its `AuroraCamera` case — ortho over `WindowRoot.ViewportSize`, falling back
to the raw swapchain extent when the window has no root.

**Bootstrap** — `<Step Action="UIEngine.Bootstrap"/>` is the last step of `Bootstrap.bootstrap.xml`. It logs
the row sizes and builds a **scaffolding tree** on `Engine.primary`: a root at window size, padding 24,
holding `card` (360×220, padding 16, Left/Top) → `inner` → `leaf` (120×60), `clipped` (200×140, Right/Top,
`clipOutOfBounds`) → `overflow` (320×260), the overlapping `under` (200×120) and `over` (120×200) both
centred, and `bar` (height 48, Stretch/Bottom). Built back to front, so pool allocation order is nothing like
DFS order and the resequence has real work.

`card`, `bar`, `under` and `over` are `ProbeControl`, which recolours on enter/exit/press/release; `leaf` is a
`ProbeControl` that **consumes nothing**, so hovering it bubbles through `inner` (a plain `Control`) to `card`.
The landing 6 port removes all of it.

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
| Split entry: `Poll()` at the `HandleUI` site, `ResolveLayout()` at the `Interpolate` site | Layout must run after `OnTick`, or an `OnTick` invalidation lands a frame late |
| Hover is one control; ancestors hear the bubbled event, they are not hovered | Hovering a glyph does not make the window root hovered |
| The hit-test walks children last to first | Depth testing is off, so the later sibling is the one drawn on top |
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

**A new stack root paints unless told not to.** A `Control` with default `alpha = 1` and the window's rect is a
full-window opaque quad, and the compositor puts it over everything the old stack drew — the window goes blank
white and reads as the old stack having broken. `WindowRoot` sets `alpha = 0f`. This is the same trap the
`aurora-verify` skill records for `maskAsset`, in the stack that has no masks yet.

**The old stack does not re-lay out on a `MoveWindow` resize.** Its content stays at the previous width and
clips, while the new stack's tree refits correctly from the very next line of the same GLFW callback.
Reproduced on the unmodified HEAD build (commit before landing 2), so it predates this work — but it means a
resize capture shows a torn old stack and that is not evidence of a regression.

## Landings

### 2 — tree and layout — **DONE 2026-09-06**

Built as planned, with three answered forks: cumulative child offsets **deferred** to the landing that reads
them; verification by nested plain `Control`s plus a DEBUG recompute rather than by pulling `StackControl`
forward; `WindowControl`'s design-space scaling **ported** onto `WindowRoot` rather than dropped.

`subtreeBounds`/`subtreeCount` are refreshed by `ResolveLayout` after `Arrange` rather than inside it, and
`RefreshWindowRanges` counts fresh rather than reading the cache — see [[ui-engine-stack]] for both reasons.

→ *verified:* the scaffolding tree arranges and draws over the old stack in Thorium; nesting, padding, margin,
Left/Right/Stretch alignment, bottom anchoring and an inherited clip on a 320×260 child inside a 200×140 parent
all correct; a `MoveWindow` resize re-lays the tree; painter order correct with pool allocation order reversed,
so the DFS resequence ran; no resequence warning, so the walk reached every live row in both pools; the DEBUG
recompute logged no mismatch; a second window (the File menu) draws none of the primary's rows.
**Not exercised at runtime:** a remove or a reparent after the first resequence.

### 3 — input — **DONE 2026-09-06**

Built as planned. Three answered forks: contexts registered under `Next`-prefixed names now and renamed at
landing 6; delegate handlers built now alongside the virtuals; the hit-test walks children **last to first**
so the sibling drawn on top takes the hit, which the outgoing stack gets backwards.

Hover is **one control**, not a chain — ancestors are not hovered, they only hear the bubbled event.
`Enter` and `Exit` go through the same walk-up-until-consumed dispatch as press and release.

Not ported, deliberately: scroll, drag, context-menu gating, `canBeActiveContext` / `takesActiveControl`.

→ *verified:* hovering `leaf` lights `leaf` and bubbles through `inner` (no handler, unchanged) to `card`,
which consumes it; leaving returns both to their resting colours; two overlapping siblings hand the hit to the
one drawn on top and to the exposed arm of the one underneath; a press recolours the pressed control.
**Not exercised:** double-tap, right button, and the pointer leaving the window.

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
