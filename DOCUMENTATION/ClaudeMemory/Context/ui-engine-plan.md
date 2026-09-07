# UI Engine — state and resume point

**Rewritten:** 2026-09-06. **Landings 1–5, 6a, 6b0 and most of 6b1 are built and GUI-verified. The rest of
6b–6d is agreed and unbuilt.**

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

One `Control` emits **0..N** `VulkanControl` quads. A panel emits 1; a text run emits one per *visible*
glyph and none for itself. The old `Core.UISystem.Controls.VulkanControl` keeps its name until landing 6.

## What exists now

**Namespace `ArctisAurora.Core.UI`** — `UIData.cs` (the three structs, the enums, `LayoutRect`, `Thickness`,
`QuadUVs`), `Control.cs`, `WindowRoot.cs`, `PointerEvent.cs`, `TextRunControl.cs` (`StyleSpan`,
`IGlyphPressTarget`, `TextRunControl`), `NextCaretControl.cs`, `UIEngine.cs`.

**Pools**, in `Pools.pools.xml`, both `Ordered="true"`:

| Pool | Columns | Capacity | SortAction |
|---|---|---|---|
| `UIElements` | `ArrangeData` | 1024 | `UI.NextElementOrder` |

`VulkanControls` was deleted 2026-09-07 — the two GPU structs are fields on `Control` and a per-window walk
emits them into a `DrawList`. See [[ui-draw-list]].

`VulkanControlData` is the XSD name of the `VulkanControl` struct — renamed because `AnyXMLType.FindType`
resolves `[A_XSDType]` by **name alone, first declaration wins**, and the old class already owns
`"VulkanControl"`. Collision disappears at landing 6.

**Row sizes**, printed by `UIEngine.Bootstrap` at boot via `Unsafe.SizeOf`:

```
ArrangeData 140 B   ControlGeometry 96 B   VulkanControl 92 B
```

**`Control : Entity`** — `PoolName => "UIElements"`. Holds its own quad in two plain fields and copies them
into the draw list from **`Emit(DrawList)`**, which a control off its own clip skips. Exposes `arrange`
(a pool `ref`), `geometry` and `visual` (`ref` to the fields),
the authored layout properties over `ArrangeData`, `colorHex` / `alpha` (both **`virtual`**) / `cornerRadius` /
`edgeColorHex` / `edgeThickness` / `kind` / `sampler` / `gradient` / `SetUVRect`,
`Measure` / `Arrange` / `WriteArranged`, `InvalidateLayout` /
`InvalidateArrange`, `Hide` / `Show` and `RefreshSubtreeCache`. **`AddChild` throws on a second child**, as
the outgoing base does.

**`TextRunControl : Control`** — a paragraph as one control. `text` + `List<StyleSpan>` (spans tile in order,
the last absorbing the remainder) + a measured `BlockLayout`. `Emit` walks the lines, skips those outside
the clip band, and appends one quad per character of the rest; the run's own box never paints.
`IndexAt(point)` / `CaretAt(offset)` /
`TextOrigin` answer caret questions; `OnPointerPress` walks up to the first `IGlyphPressTarget`.
**`NextCaretControl : Control`** — the blink and 2px width, named around a serializable-id collision
(see [[parallel-stack-name-collisions]]).

**`WindowRoot : Control`** — the only node that holds siblings. Carries `WindowingMode`, `autoscaling`,
`ScalingAxis`, `ViewportSize`, `FitTo`, `ToDesignSpace`, and a `Measure`/`Arrange` that loops children by
alignment. Transparent (`alpha = 0f`) because there is no invisible mask to opt out with yet.

**The 6b0/6b1 subclasses**, all in `Core/UI/` — `NextLabelControl` (`"NextLabel"`, a `TextRunControl` that
hands the context up), `NextPanelControl` (`"NextPanel"`, a `Control` and its tag),
`NextStackPanelControl` (`"NextStackPanel"`, `ContainerControl`, the two-pass star layout ported whole; its
enum is `"NextOrientation"` because the old owns `"Orientation"`), `NextButtonControl` (`"NextButton"`, the
three-state tint over an overridden `colorHex` that keeps the authored rest colour) and `NextIconControl`
(`"NextIcon"`, `kind = MTSDFControl` plus one `SetUVRect` for the atlas cell). `TextRunControl` is now
`[A_XSDType("NextTextRun", "UI", isAbstract: true)]` with `Text` / `FontSize` / `FontName` authorable.

**`UIEngine`** — `RegisterDirtyRoot` / `ResolveLayout` at the `Interpolate` site, `Poll` at the top of
`HandleUI`, `HitTest` / `Dispatch` / `Forget` / `SetActiveControl`, the `NextHovering` / `NextActiveControl` /
`NextPressTarget` contexts, `BuildDrawLists` at the frame edge, the `UI.NextElementOrder` `PoolSort` action,
and `BuildProbe`.

**`PointerEvent`** (`target`, `point`, `delta`, `button`, `tapCount`) and **`PointerPhase`** — `Control` has one
`virtual bool OnPointerX(PointerEvent)` and one `Func<PointerEvent, bool>` per phase, and `RegisterOnX` sets
rather than combines.

**`UIEngineModule`** — second `RenderingModule` on every `RenderWindow` (`window.uiNext`, index 1 in
`modules`). `compositorOrder = 10`, transparent clear, so the old stack composites underneath. Owns the window's
`drawList` and mirrors its prefix per swapchain image through `MirrorDrawList`, every frame. Holds `uiRoot`;
the draw is `_drawCount` instances from zero. A window with no root draws nothing.

**Shaders** — `Shaders/UIEngine/UIEngine.vert` + `.frag`, compiled `--target-env=vulkan1.3`, mirrored
byte-identical into `Thorium/`, `AuroraEditor/` and `Carbon/`. Set 0 renderer global, set 1 module
(camera UBO + two `scalar` SSBOs + **the gradient table at binding 3**), **set 2 the texture table**
(`sampler2D samplers[]`, variable count, `TextureAsset.MaxTextures`). The frag branches on `fragType`; the
`edge` band is shared code over a `dist`/`aa` pair each branch fills in its own units. An image multiplies its
texel into colour and alpha, a panel's mask multiplies coverage in last, and both reads sit behind
`fragTextureIndex != NO_TEXTURE` (`VulkanControl.noTexture`, `uint.MaxValue`).

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
Landing 4 added a `document` (Left/Center): a `ProbeDocumentControl` holding a `TextRunControl` wrapping at
360 across three `StyleSpan`s and a `NextCaretControl`. Landing 5 added `BuildSampled` — `image` (the icon
atlas as flat colour, Center/Top), `icon` (the `folder` cell, Right/Center) and `swatch` (the `accent`
gradient, Right/Bottom). The landing 6 port removes all of it.

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
| ~~Row building is **incremental per element**, not a per-frame rebuild~~ **REVERSED 2026-09-07** | The 11 MB/frame was a list sized by the document. Culled, the list is sized by the screen — the probe emits 44 quads. See [[ui-draw-list]] |
| ~~Emit rows for **all** glyphs for now, not visible-only~~ **REVERSED 2026-09-07** | A run emits a quad per visible glyph, off the lines the measurer already produced. No per-document cache was needed; the clip band and `_layout.lines` were enough |
| An XML event attribute binds by **wrapping the tagged method into a func that returns `true`**, combined with `+=` | The tagged action pool feeds keybinds, context menus and `*.ui.xml` with three different delegate shapes; only one has a `PointerEvent`. See [[ui-engine-stack]] |
| Any **delegate**-typed member is an XSD attribute, not just `Action` | One shape test in the generator instead of a `Core.UI` type registered inside `Core.Registry` |
| `canBeActiveContext` is a **question that returns a control**, `Control.ActiveContextTarget()` | 6c's `TextBox`/`TextInput`/`DocumentEditor` need to focus a *child* run, which a "not me" bool cannot express |

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
(`RefreshWindowRanges` is gone as of [[ui-draw-list]]; the cache is now the walk's prune instead.)

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

### 4 — text and caret — **DONE 2026-09-06**

Built as planned, with six answered forks: spans reach the measurer as a **slice** over one string
(`TextMeasurer.Run.charStart`/`charCount`) rather than as substrings, because without spans a paragraph could
not hold a bold word at all; the document is **scaffolding** (`ProbeDocumentControl`) rather than a real
container pulled forward; `SelectionControl`, `DocumentEditorControl.CaretAtPoint` and the per-character XML
shape are all **deferred** — nothing can drive selection without drag, there are no blocks, and the new stack
parses no XML; a run **keeps** its own transparent `rows[0]` (as of [[ui-draw-list]] it emits no quad for
itself at all).

Departure taken mid-build: `CaretControl` had to become **`NextCaretControl`** — see
[[parallel-stack-name-collisions]].

→ *verified:* a three-span paragraph renders in Thorium wrapping at 360 into three lines, one `Control` and one
draw row per character; the amber bold span crosses a line boundary with the right characters in it; four
probed clicks put the caret at line 1 offset 0 (x 24), mid line 1 (clicked x 217 → caret 219), line 2's start
past the first character's midpoint (x 36), and line 3's end (x 334), with line tops exactly 24 px apart;
caret blinks; no resequence warning and no DEBUG subtree-cache mismatch through boot, layout and four clicks;
no Vulkan validation error beyond the pre-existing `DemoteToHelperInvocation` pair and the asset-upload
barrier ones.
**Not exercised:** editing the text after the first measure, a second run in one document, a font with a real
bold face (the default family may collapse `Bold` to regular), and `edge` on a glyph.

### 5 — images and icons — **DONE 2026-09-06**

Built as planned, with four answered forks: **one** sampler slot whose meaning `kind` selects, so an image
cannot also carry a mask (a second index plus a second UV set is the fix, and grows the 92 B row);
`uint.MaxValue` as the "no texture" sentinel rather than a reserved slot 0, because table slot 0 is a real
texture the old stack resolves `maskAsset = null` to; the property named **`sampler`** (user, over `texture`
and `maskAsset`); and the landing **kept** rather than handed to a subagent — descriptor plumbing, coverage
ordering and a shader are what §10 reserves, and there was no exact old→new text to brief.

No struct changed — every field was already there and written by nothing. The mask multiplies **last**, after
the edge band, so it cuts the stroke along with the fill. The gradient buffer is a **static** on
`UIEngineModule`, never freed, because the table is per-process.

→ *verified:* in Thorium, an `ImageControl` draws the icon atlas as flat colour — the 3×3 grid of icon shapes
with the MSDF's own colour fringing, cut by the texture's alpha; the `folder` cell renders as a clean amber
silhouette through the MTSDF path; the `accent` gradient ramps left to right across a rounded box and `glow`
renders as a radial ellipse with an alpha falloff; pointing `card.sampler` at the `close` cell collapses the
whole 360×220 quad, edge band included, to the glyph, and removing it restores the box exactly. Row sizes
still 140 / 96 / 92 at boot. No validation error beyond the pre-existing six barrier ones, the
`DemoteToHelperInvocation` pair and the `SamplerAsset` default — in particular none from set 1 binding 3.
**Not exercised:** an image and a mask together (not expressible), a gradient on an image, a second window's
copy of the gradient descriptor, and `edge` on a glyph.

### 6 — port and delete

**The count is 56, not 39** — the original number missed the text/document set and the host apps: 31 chrome,
container and interactable classes (3,838 lines), 15 text and document (4,894, incl. 3 private nested), 7 in
Thorium, Carbon and the Editor (1,576), plus 3,231 lines of support that moves with them.

**Forks the user settled, 2026-09-06:** `Next` prefixes rather than move-and-delete (green build throughout,
~56 renames at 6d); the document stack ports in this programme rather than later; `CornerRadii` restored;
`outline` stays merged into `edge`; `ContextMenus.menuFactory` carried across unchanged and remade later;
sliced into 6a–6d.

#### 6a — the base gap — **DONE 2026-09-06**

Built as planned, then narrowed. `Control` parses XML, `CornerRadii` and the `Thickness` converters are back,
a `ContainerControl` takes more than one child, and `UIEngine` holds `UICollisionHandling`'s scroll and
active-target half. Additive only — no subclass ported, no `Core.UISystem` file touched.

Three things not in the agreed file list, each forced by the verification: `ThicknessConverter` (without it
`Padding` cannot parse at all), `[A_XSDType]` on `WindowRoot` and `ContainerControl` (without a tagged root
and one concrete child, XML builds nothing), and `Control` made `partial` so `ControlXml.cs` reaches
`WriteArranged`. One thing deliberately left open: **binding an event attribute from XML** — see *Open*.

**Drag and context menus were built, verified, then removed and the drag partly restored** (user,
2026-09-06). Context menus are out entirely — `contextMenus`/`BuildContextMenu`/`OpenContextMenu` land with
the controls that host them. The drag then came back and was wired end to end: `Control.draggable` gates a
`StartDrag()` from the base `OnPointerPress`, which sets `NextDragging` and calls the parent's
`ChildDraggedOut`; `CheckDrag` runs each tick from `Poll` holding one drag target the way one control is
hovered, bubbling `DraggingOverStart` / `DraggingOver` / `DraggingOverEnd` from it — the hover's own walk
with `HitTest`'s new `skip` taking the dragged subtree out of the answer; and a left release calls
`UIEngine.EndDrag(point)` ahead of the release guards, which ends the over-pair, calls `Control.FinishDrag`
on the target and clears both. **What the claimant hears is still nothing.** Still out:
`ResolveDrag`/`StopDrag`/`draggingOpacity`, the stale-release guard, `HitFor`/`RaiseHovered`/`WindowOf`/
`WindowAt` and `Poll`'s `ownsDrag` exemption. Full list and who wants each in
[[ui-engine-stack]] § *The drag gap*.

→ *verified:* all four projects build clean; Thorium boots, row sizes still 140 / 96 / 92, 9 errors and all
of them the pre-existing set. `NextProbe.ui.xml` parses and draws over the old UI: the card is exactly
360×220 at (24,24) with a 2 px white edge band, its `CornerRadius="16,4,16,4"` measuring a 7 px diagonal
inset on both left corners against 3 px on both right ones (theory 7.7 / 4.2), `ClipToBounds` cutting a
320×260 child to its 200×140 parent on both axes, `ControlColor="teal"` resolving to `#008080`,
`Gradient="accent"` ramping `#393732` → `#2E2D28` → `#24231F` left to right, and a `Stretch`/`Bottom` bar
spanning the width. Driven with synthetic input: the wheel over the bar recoloured it amber up / purple down
through `PointerPhase.Scroll`. No resequence warning, no DEBUG subtree-cache mismatch, through boot, layout
and a scroll.
**Not exercised:** `hitTestable = false`, `canBeActiveContext = false`, and the swatch's
`CornerRadius="10,0"` bottom half, which the bar draws over.
**Verified before removal, and no longer in the tree:** the drag lifecycle — a press on a *child* bubbled to
the card, claimed the drag, held it `#FF3B30` for the drag's duration and restored `#3AA6FF` on release.
**Not re-verified after the drag and context-menu removal** (user: "go dont verify").

#### 6b0 — the label, and the active context becomes a question — **DONE 2026-09-06**

`canBeActiveContext` (bool) became `Control.ActiveContextTarget()` returning the control that takes the
context — itself by default, a decoration answering with its parent's answer. `UIEngine`'s `ActiveTarget`
walk is gone; both call sites ask the hovered control directly. `TextRunControl` is XSD-tagged and
`NextLabelControl` is the first override.

→ *verified:* `<NextLabel Text="Probe" FontSize="13" ColorHex="#5F5D56"/>` parses and draws in the probe. In
a `NextButton`, pressing the caption's glyphs and releasing on the button's own padding **fires the button**
— and with the override commented out the same gesture fires nothing, because the release identity check
compares the label against the button. Both halves were run.
**Not exercised:** a target that is not an ancestor, and the recursion cycle two mutual overrides would make.

#### 6b1 — chrome and interactables — **PARTIAL, 2026-09-06**

Built: `NextPanel`, `NextStackPanel`, `NextButton`, `NextIcon`. **`NextTitleBar` and `NextMenuButton` were
cut from the landing by the user**; `NextWindowFrame` is parked on a fork — it needs `onDrag`, which is in
the drag gap, so it lands with the drag stack rather than here. Two things established while reading it:
its grips must be **appended, not inserted at 0**, because the new hit-test walks last to first; and the
maximized-grip problem may not exist on the new stack, since `hitTestable = false` makes the walk `continue`
to the sibling behind rather than swallow the pixel.

→ *verified:* first as a floating 500×32 strip — `NextStackPanel` laying a label, a `WidthStar="1"` spacer
and three buttons left to right with 4 px `Spacing`, three distinct tints (`#EBEAE5` rest, `#E3E1D9` hover,
`#D7D5CD` press) restoring on exit, `NextIcon` drawing the atlas cells as clean silhouettes at 14 and 18 px,
and `onRelease` firing `Window.Minimize` from both an icon button and a captioned one. Then rebuilt as the
**real title bar**: `NextProbe.ui.xml`'s root padding dropped to 0 so a `Stretch`/`Top` stack sits flush at
y 0..32 across the full width, holding `Thorium`/`File`/`Edit`/`View` captions at 72/48/48/48 and the
minimize/maximize/close trio at 46 each, with `Gradient="titlebar"` across the bar. Hover tints one button
and leaves its neighbours at rest, close goes `#C42B1E` on hover and `#A82318` on press, and releasing close
runs `Shutdown.Request()` and exits. Nine errors at boot and all of them the pre-existing set — one
`SamplerAsset` default, six barrier `dstAccessMask`, the `DemoteToHelperInvocation` pair. No resequence
warning, no DEBUG subtree-cache mismatch.
**Not attributable:** the window buttons sit exactly over the old stack's, which are wired to the same
actions, so the *firing* does not prove which stack fired. The *tints* do — the new module composites on top.
**Not exercised:** `Spacing` on a vertical stack, star children in both axes at once, `MinWidth`/`MinHeight`
clamping, and a stack nested in a stack.
**The probe's root is now `Padding="0"`**, so `card` and `clipped` are flush to the window corners and the
title bar covers their top 32 px — `card`'s two top corner radii are no longer visible in the live probe.
Root padding was verified at landing 2 and is recorded there.

#### 6b — chrome, containers, interactables, hosts

**Unblocked 2026-09-06** — the event-attribute fork is settled and built; see *XML event attributes* below.

- Port the remaining engine chrome/container/interactable classes and the 7 host ones onto `Control` /
  `ContainerControl`, each `Next`-prefixed, each with a `Next`-prefixed `[A_XSDType]`.
- The `bubbleX` → walk-until-consumed conversion, per class.
- **Delete the six authored `BubbleClick="true"` sites** — every one is the same title-bar spacer
  `<Panel WidthStar="1" Height="32"/>`, there so the click reaches `TitleBar` for caption drag. The new base
  returns `false` with no handler, so it bubbles for free. No `Bubble*` attribute needs a new meaning.
- `ScrollableControl`'s wheel handling becomes an `OnPointerScroll` override.
- **The drag stack, back in with its first consumer** — `Control.StartDrag`/`ResolveDrag`/`StopDrag`/
  `ResolveDrop`/`ResolveDropHint`/`ClearDropHint`, `UIEngine.SetDragging`/`SolveDrag`/`HitFor`/`OfferDrop`/
  `UpdateDropHint`/`RaiseHovered`/`WindowOf`/`WindowAt`, the `NextDragging`/`NextHinted` contexts and
  `Poll`'s `ownsDrag` exemption. All written and GUI-verified at 6a, then removed; recoverable from the
  landing 6a commit.
- **The context-menu hooks, likewise** — `contextMenus`, `BuildContextMenu`, `OpenContextMenu` — plus
  `NextContextMenus` and the menu controls that host them, so `ContextMenu="…"` binds again.
- `NextDragGhost`, which needs `rangeRoot` on `UIEngineModule`.
- Delete `NextProbe.ui.xml` and `UIEngine.BuildProbe`.

→ *verify:* a `Next`-prefixed copy of Thorium's `UI.ui.xml` builds the real shell — title bar, tabs, splits,
the file browser — on the new stack.

#### 6c — text and documents

The 15 text/document classes. Not a port: `TextControl`, `LabelControl`, `GlyphControl` and `TextRun` are one
control per glyph, the model `TextRunControl` replaced. Pulls in `SelectionControl` (needs drag, now ported)
and `DocumentEditorControl.CaretAtPoint` (needs blocks).

→ *verify:* a note opens, edits and saves on the new stack.

#### 6d — delete

- Delete `Core.UISystem`, the `UIControls` pool, `UIModule`, `MCUI`, `UILayout`, `UI.vert`/`UI.frag`.
- Drop every `Next` prefix — ~56 classes, their XSD names, `VulkanControlData`, `NextCaretControl`, and the
  `NextHovering` / `NextActiveControl` / `NextPressTarget` / `NextDragging` / `NextHinted` contexts.
- Decide where `TextMeasurer`, `FontStyle`, `Glyph`, `AtlasMetaData`, `GlyphControl`'s cell constants,
  `Gradients` and the active-window latch live.
- Collapse the three copies of the atlas-cell arithmetic and the duplicated `CreatePipeline`.
- Regenerate `NAMESPACES.md`.

→ *verify:* Thorium boots entirely on the new stack.

**Handoff (CLAUDE.md §10):** landings 2–5 and 6a stayed with the model — layout correctness, dispatch, Vulkan,
and 6a is the design the rest is measured against. Landing 5 was originally marked for a subagent and was
kept: descriptor plumbing, coverage ordering and a shader are what §10 reserves. **6b's rebase is the sweep
that goes out** now the base is frozen — exact old→new text per class, one agent per folder. 6c stays.

## Open — not decided

- **Deferred out of landing 4, each waiting on something specific:** `SelectionControl` (needs drag ported),
  `DocumentEditorControl.CaretAtPoint` (needs blocks), the XML shape for per-character settings (needs the new
  stack to parse XML at all).
- **Where the font system lands when `Core.UISystem` is deleted.** `TextMeasurer`, `FontStyle`, `Glyph`,
  `AtlasMetaData` and `GlyphControl`'s cell constants are not the control stack, and `Core.UI` compiles
  against all of them.

- **`ContextMenus.menuFactory` must die** (user, explicit). One override, `Thorium.cs`, supplying a
  `WindowedContextMenuControl` with six hardcoded hex colours. Deleting the field alone breaks Thorium's menu
  styling; a replacement (theme roles + a windowed-vs-inline setting) was proposed and not yet approved.
- **`WindowFrameControl`'s maximized grips** — deferred to "context logic" (user, 2026-09-06).
- **Children and components as `(start, count)` ranges** — analysed, parked (user: "for now do nothing with
  this"). Not a slowdown; the cost is that mutation stops being O(1) and settles at the frame edge.
- **`_components` / `children` lazy allocation** — user chose *neither* (2026-09-06). 64 B/entity stands.
- **Ticking as a group** — [[entity-tick-group]], raised and parked.
- **`DataPool.Write<T>(handle, value)`** — the missing assign-plus-`MarkContentDirty` primitive.
- **A second sampler slot**, so an image can also carry a mask. Costs a `maskIndex` plus a second UV set and
  moves the 92 B stride; parked until something wants both.
- Whether per-window mirrors in `UIEngineModule` should become shared.
- ~~Whether visible-only glyph expansion lands, and on what evidence.~~ **Settled 2026-09-07** — it
  landed as part of [[ui-draw-list]]: a run emits only the lines meeting its clip. 342 characters in a
  40 px clipped box emit 141 quads.
- **Rebuild the draw list on change rather than every frame** — agreed, deliberately deferred so the
  concept is easier to watch running. One flag, and `HasPendingWork` stops answering `true`.

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
