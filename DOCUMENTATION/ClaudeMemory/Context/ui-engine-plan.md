# UI Engine — agreed plan and resume point

**Agreed:** 2026-09-04. **Nothing built.** No code, no XML, no shaders changed yet.
**Supersedes in scope, not in content:** [../Decisions/ui-data-control-split.md](../Decisions/ui-data-control-split.md),
which designed the split but concluded it could not lower the control count. This plan combines that
split with the escape hatch recorded in [../Decisions/text-layout-one-measurer.md](../Decisions/text-layout-one-measurer.md),
which does lower it.

This file exists so the work can be picked up cold. It carries the decisions already taken, the facts
that were expensive to establish, and enough per-landing detail to build from without redesigning.

## Goal

Split the UI into a **data tier** and a **renderable tier**. The data tier holds every element and is
sized for ~1M rows; the renderable tier holds only what is actually drawn, ~5k rows, and is the only
thing the GPU mirrors. Text runs hold a string, not one control per character; glyphs materialize when
visible and are discarded when they are not.

Entry point is `UIEngine.Poll()` — a new static class in `Core.UISystem`, **on the main thread**, no
thread of its own. It is shaped like `UICollisionHandling` (static, one per-tick driver) but written
against the new data.

**Out of scope, deliberately:** a second UI thread; replacing the Vulkan pipeline; changing the ECS
storage model outside the two UI pools.

## The two tiers

| | Data tier | Renderable tier |
|---|---|---|
| Pool | `UIControls` | `Renderables` (new) |
| Sized for | ~1M rows | ~5k rows |
| Row holds | layout inputs, arranged + clip rect, style/role, kind, content handle | `GpuTransform`, `ControlData`, `RenderableKind` |
| Mirrored to GPU | no | yes — `MCUI` mirrors this instead |
| Text | one row per run, holding a string | one row per **visible** glyph quad |

The renderable tier is expected to collapse to three kinds: **panel** (one colour or none),
**glyph/vector**, **image**. `RenderableKind` is that field.

Because the renderable pool holds only what is drawn, it **is** the compacted visible set —
`firstInstance`/`instanceCount` are plain ranges in it. No index buffer, no indirect draw, no
compaction pass.

## Standing decisions

Settled with the user 2026-09-04. Do not re-litigate without asking.

| Decision | Why |
|---|---|
| One new class, `UIEngine`, static, main thread, entry `Poll()` | The old system stays runnable beside it while the migration happens |
| **Split entry, not one call.** `Poll()` at the `HandleUI` site does input + animations; `PollLayout()` is called from `Interpolate` where `ResolveLayout` sits | Layout runs *after* `OnTick` today. One call at the input site would run it before, and anything invalidating layout from `OnTick` (glyph sync, caret) would land a frame late |
| New columns are **appended** to `UIControls`, never reordered | Column ids come from `Pools.pools.xml` declaration order and in-flight `SystemCommand`s carry them |
| Layout stays **recursive** through the existing virtual `Measure`/`Arrange` for now | Going flat needs the layout-kind switch across every container at once, and `TextBlockControl`'s inline flow may not survive it. Separable from the column work; do the columns first |
| Hit-test becomes a **backward scan** over dense order — last drawn wins, so first hit is deepest | Behaviour-identical today because every UI transform is scale+translate. Must return to a quad test if per-letter rotation ever ships |
| Visibility early-out is **opt-in per container** — `childrenMonotonic`, false by default, true only on `StackPanelControl` (vertical), `TextBlockControl`, `GridListControl` | Those three advance a layout cursor monotonically. A container that positions a later child above an earlier one (docking, overlays) would silently drop it |
| A theme is chosen by a **file inside the vault** | The user's framing: the vault declares which style it uses. Travels with the vault; not a property of this machine |
| Theme roles name a **mask** as well as colours | There is no XML attribute for a mask today and 15 C# sites hardcode one. A role owning it makes the invisible-mask trap data |
| Per-character authored style must live in the **run's data**, never on the glyph object | A glyph dematerializes; its object cannot be the home of state that has to survive. This is the condition under which [../Decisions/glyphs-as-pool-data.md](../Decisions/glyphs-as-pool-data.md)'s per-letter colour/rotation/animation requirement still holds |
| Only **content** elements virtualize; chrome controls stay alive | `TabViewControl`, `FileBrowserControl` and others wire handlers imperatively with capturing lambdas, which cannot be rebuilt from a data row |
| Dematerialization is **refused** for an element holding focus, an active drag, or an open edit | Otherwise `activeControl` dangles |
| Draw count changing per frame is handled by **re-recording**, not indirect draw | See the facts below — the UI command buffer is ~15 calls |

## Facts that were expensive to establish

Measured or read out of the code on 2026-09-04. Re-deriving these is most of the cost of a cold start.

**Memory, per control, today**

- Pool row: `TransformData` 36 B + `ControlData` 136 B (`Pack=1`) + `GpuTransform` 64 B = **236 B**, allocated at pool **capacity**, not count.
- Pool indirection: 24 B per capacity slot (`_slots`, `_backMap`, `_versions`, `_publishedSlotVersion` at 4 each, `_owners` at 8).
- Heap object: `VulkanControl` ≈ **432 B** (16 B header, ~64 B `Entity`, ~352 B own — 19 reference fields, 20 four-byte scalars, two `Thickness`, two `LayoutRect`, `CornerRadii`, `Sampler`, `DateTime`, 13 bools), plus **two `List<>` objects at 32 B each** allocated in field initialisers. `_components` is allocated for every entity and never used by a control.
- GPU: 200 B per row (`GpuTransform` + `ControlData`) **per swapchain image**; `TransformData` is CPU-only.
- All in: **~1.2 KB per control.** At the recorded 56.7k-control note, ~28 MB managed + ~13 MB pool + ~23–34 MB device.

**Things that are not what an older note says**

- **Descriptors do not scale with control count.** Set 0 is 4 descriptors (camera UBO + transforms, control-data and gradient SSBOs); set 1 is `TextureAsset.MaxTextures` = **256** samplers, indexed by `textureIndex`. The "already past `UIModule`'s 50,000-cap descriptor array" line in [thorium-editor-architecture.md](thorium-editor-architecture.md) is **stale** — that array became a texture table.
- **Corner radius already works end to end.** `CornerRadii` + its `TypeConverter` → `VulkanControl.ResolveAttributes` (which uses `TypeDescriptor.GetConverter`) → `ControlData.cornerRadius` → `UI.vert`'s `fragRadius` → `sdRoundBox` → `opacity *= inside`. Four `.ui.xml` files author `CornerRadius="4"` today. The styling work is a theme, not a repair.

**What makes the plan cheap**

- **Visibility is already computed.** `VulkanControl.Arrange` sets `ClipRect = Intersect(finalRect, parent.ClipRect)` when `clipOutOfBounds`, and `Hide()` collapses it to a degenerate rect. So *visible ⇔ arranged ∩ clip has positive area*. No new data is needed to compute it.
- **`DataPool.OwnerAt(dense)` walks in DFS order today**, because `UIControls` is `Ordered` with `SortAction="UI.DFSOrder"`. A visibility pass can iterate in draw order before any column work lands.
- **`UILayout.RefreshWindowRanges` already publishes per-window `firstInstance`/`instanceCount` and already sets `ui.isDirty[i] = true` when they change.** The re-record trigger exists; the pass replaces the body, not the plumbing.
- **The UI command buffer is ~15 Vulkan calls** — begin, barrier, begin-rendering, bind pipeline, bind global set, viewport, scissor, bind vertex, bind index, two descriptor binds, draw, end-rendering, barrier, end. Re-recording it when the visible set changes is negligible, so `vkCmdDrawIndexedIndirect` is **not needed**.
- **`TextMeasurer` runs from a string and `IGlyphMetrics` alone** — no atlas, no `FontAsset`, no GPU, by design so a test can fabricate metrics. `TextControl` already holds a `BlockLayout`, and `LineSegment` is already `(runIndex, charStart, charCount, width)`. Text can be laid out fully without a single glyph control existing.
- **`TransformData` and the arranged rect are the same information.** `VulkanControl.WriteArrangedTransform` derives position and scale from `finalRect`, and `CommitTransform` bakes the matrix. The data tier carries the rect; both transform columns belong to the renderable tier. This is what gets the data row from 236 B to ~100 B, and 1M rows from 236 MB to ~100 MB.

**Costs the plan should not re-discover**

- `ControlData` mixes layout output (`clip` 16 B, `gradientRect` 16 B) with paint (104 B: uvs, tint, textureIndex, cornerRadius, edge, outline, gradientIndex). Every `Arrange` dirties the whole 136-byte row to change 32 bytes, so `MCUI.MakeInstanced` re-uploads **4.25× more than changed** on every resize.
- `ref` accessors over a pool column cost a `Dictionary<Type, IPoolColumn>` lookup plus a slot indirection **per access**. Moving layout fields onto columns while the driver is still recursive is a knowing regression, paid back when the loop flattens.
- Masks are hardcoded at **15 C# sites** as `AssetRegistries.GetAsset<TextureAsset>("invisible")` (`SplitViewControl`, `TabViewControl`, `WorkspaceControl`, `FileBrowserControl`, `DocumentControl`, `DocumentEditorControl`, `ContextMenuControl`, `EditableLabelControl`, `ConfirmWindow`, `DocumentToolbarControl`). There is no XML attribute for a mask.
- `SettingsWindow.Editor` builds a `DropdownControl` only for enums, optionally narrowed by `[A_XSDDomain]`. A string setting falls through to a `TextBoxControl`, so a theme picker needs a new editor kind: a string setting with a runtime option list.
- Vault switching pattern, from [../Decisions/vault-list-and-switching.md](../Decisions/vault-list-and-switching.md): writing the setting **is** the switch; nothing caches it and everything downstream re-reads. A theme should follow the same shape.

## Landings, in agreed order

### 1 — data structure

**1.1 Layout and geometry off the object.** New `LayoutData` and `GeometryData` structs in `Core.UISystem` (not `Core.Data` — they use `Thickness` and `LayoutRect`, and `Core.Data` must not depend upward; precedent is `ControlData`, already a UI-owned column). Appended to `UIControls` in `Pools.pools.xml`.

- `LayoutData` — width/height, preferred and min sizes, star weights, margin, padding, h/v position, both alignments, a flags byte (clip, hidden, measure-dirty, arrange-dirty, hit-testable), a depth byte.
- `GeometryData` — `arranged`, `clip`, `desiredSize`.
- `VulkanControl`'s ~20 layout fields become properties over `ref Pool.GetRef<LayoutData>(dataHandle)`, keeping their names and their setter side effects. **No control subclass changes** — they read these by name and the names survive.

→ *verify:* builds clean; `Unsafe.SizeOf<>` on both matches the field census; Thorium renders identical. Invisible by construction.

**1.2 `Renderables` as a second pool.** Declared in `Pools.pools.xml` holding `GpuTransform` + `ControlData` + `RenderableKind`. `MCUI` mirrors `Renderables` instead of `UIControls`; `UIControls` sheds both transform columns. Every control still gets a renderable row at this step — the point is that the tiers exist and the GPU is fed from the renderable one.

→ *verify:* renders identical; `UIControls` row drops to ~100 B; both pools report expected counts at boot.

**1.3 Text as data.** `TextRun`'s authoritative content becomes `text` + `BlockLayout`; `SyncGlyphs()` stops firing from the `text` setter and becomes deferrable. Still materializes everything at this step — only the trigger moves.

→ *verify:* renders identical; typing, caret, selection and undo unchanged.

### 2 — cache and visibility

The pass **builds** the renderable pool rather than filtering a draw range. Walk the data tier in DFS
order; visible ⇔ arranged ∩ clip; allocate a renderable row per visible element; expand a visible
run's `BlockLayout` into glyph renderables for its visible lines only.

- Per-document cache on `DocumentControl` — block → run → line ranges, binary-searched by Y, so a scroll does not touch every glyph.
- A container whose own rect and visibility are unchanged keeps its rows wholesale.
- Early-out per `childrenMonotonic` above.
- Runs first as a **full scan** (correct, verifiable), then the cache on top, kept only if a Carbon capture says it beat the full scan.

→ *verify:* a shadow-compare flag runs both paths and logs divergence; the union of the new visible set must equal what the old path drew that was actually on screen, across a scroll sweep, a resize, a tab switch and a `Hide()`/`Show()`.

### 3 — theme and animation

- `Theme.theme.xml` on the `Gradients` pattern, parsed by a `Theme.LoadTheme` bootstrap step beside `Gradients.LoadGradients`. A `Role` carries a base colour, hover/press variants, and a mask name.
- New `Role` attribute on `VulkanControl`. `ButtonControl.ApplyState` resolves from the role. **Explicit `ColorHex` beats the role**, so existing `.ui.xml` renders unchanged.
- The 15 hardcoded mask sites become role defaults.
- Theme named by a vault-local file, held as a setting, live-reapplied across the `"Controls"` registry group.
- `UIEngine.Animate(control, property, target, animation)` writing through immediately; `Animations.animations.xml` declaring name/duration/easing, parsed into a table; `Ease(Easing, float t)` complete; `PollAnimations()` present and returning.

### 4 — all else

Flat forward loop with the layout-kind switch. `ControlData` collapsed to what each of the three
renderable kinds actually needs, and the `UI.frag` branch that stops MSDF-decoding plain panels — the
`.spv` recompile across four trees, via the `shader-pipeline` skill.

## Open — not decided

- How the run → glyph expansion interacts with the caret, selection rectangles, and `TextBlockControl`'s `firstLineOffset`/`lastLineEndX` handshake. Design against landed 1.3 rather than guessing now.
- Whether the data tier keeps parent/child links as `(start, count)` ranges or something else. [../Decisions/ecs-rework-data-pools.md](../Decisions/ecs-rework-data-pools.md) has the `(start, count)` answer for children; strings and style inheritance are still unsolved.
- Whether every current container survives becoming a layout-kind switch.
- Where per-character style spans live once per-letter styling is actually authored.

Related: [[ui-data-control-split]], [[text-layout-one-measurer]], [[glyphs-as-pool-data]],
[[ecs-rework-data-pools]], [[thorium-editor-architecture]], [[vault-list-and-switching]],
[[control-edge-and-outline]]
