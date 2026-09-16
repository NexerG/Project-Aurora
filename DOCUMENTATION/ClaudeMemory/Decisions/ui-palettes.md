# Decision — a control's colour comes from a palette role, carried as a paint word

**Date:** 2026-09-17
**Scope:** `ArctisAurora.Core.UI` — `Palettes`, `PaletteDefinition`, `PaletteRole`, `Control` (paint region, `WriteArranged`), `ButtonControl`, `TextRunControl`, `VulkanControl`; `ArctisAurora.EngineWork.Rendering.Modules.UIEngineModule`; `Shaders/UIEngine/UIEngine.vert`; data `*/Data/XML/Documents/Palettes/*.palette.xml`

Slice 1 of 3. Slices 2 and 3 are under Known gaps.

## What changed

- **Paint word.** `VulkanControl.tint` (vec4) → `paint` (uint) + `alpha` (float); `edgeColor` (vec3) → `edgePaint` (uint). Row 92 → 76 bytes.
  - top bit set → low 24 bits are `0xRRGGBB`, inline (`Palettes.Inline`)
  - top bit clear → slot in the paint table (`Palettes.Table`, `vec4[]`)
  - slot 0 is transparent black, so a zeroed row paints nothing
- **Paint table on the GPU.** Set 1 binding 4 `PaintBuffer`, vertex stage. `UIEngineModule.MirrorPaints` keeps one host-visible mapped buffer per swapchain image and rewrites it when `Palettes.Table` is a different array (reference identity is the version). The table is replaced whole on load, never written in place.
- **Palettes.** One `<Palette>` per `XML/Documents/Palettes/*.palette.xml`, unioned across mounts (`VirtualFileSystem.EnumerateAll`); a same-named file in an app replaces the engine's. Bootstrap step `Palettes.LoadPalettes`, after `Gradients.LoadGradients`. A palette named `default` is required — boot halts without one.
  - authored: `Ground Surface Chrome Field Line Accent Danger` (surfaces), `DarkInk LightInk`, `Step`, `Muted`
  - baked per palette, a 37-slot block: 7 surfaces × rest/hover/press, ink + muted ink per surface, the two raw inks
- **Derivation.** Text colour = whichever of `DarkInk`/`LightInk` has the higher WCAG contrast against the ground. Muted = ink blended toward the ground by `Muted`. Hover/press = colour blended toward its ink by `Step` × 1 / × 2.
- **Slot rule.** A derived colour is a slot only when every input is a slot of that palette. Ink on another palette's surface or on an inline ground picks the raw ink slot; muted ink or a stepped inline colour is computed on the CPU and written inline.
- **Roles.** `Control.role` (`Role=`): `None` (no palette), `Clear` (paints nothing, ground passes through), the 7 surfaces, `Ink`, `MutedInk`. Defaults: `PanelControl`, `ContainerControl`, `ButtonControl` → `Clear`; `TextRunControl` → `Ink`; `IconControl` → `MutedInk`; `WindowFrameControl` → `Ground`; `TitleBarControl` → `Chrome`; everything else `None`.
- **Authored wins.** The `colorHex` setter marks the control authored; resolution never repaints it. Constructor defaults written through `colorHex` count as authored, so every unmigrated control keeps its exact colour.
- **Palette reference.** `Control.paletteName` (`Palette=`), nearest ancestor wins, `default` when none. Naming a palette does not paint a ground.
- **Resolution is inherited in `WriteArranged`, like the clip.** `InheritPaint` takes `palette` and `groundBelow` from the parent (a root sits on its palette's `Ground`), paints the role, stores both. A control is always arranged before it is drawn, and attach, move and show all end in an arrange.
  - `Role` / `Palette` set at runtime → `InvalidateArrange`, as alignment does
  - paint that moves outside layout (button state, `ColorHex` or `Alpha` at runtime) → `RepaintChildren`, the `CollapseClip` of colour; skipped while arrange-dirty, stops at a child whose ground did not move
  - ground = the control's paint when it is an opaque plain panel (`alpha > 0`, `PanelControl` kind, no sampler); otherwise its parent's
- **`ButtonControl`.** Authored state hex wins; an authored rest keeps today's fallback chain; a palette rest steps. A `Clear` rest has alpha 0 until hovered.
- **`TextRunControl`.** `_runColors` → `_runPaints` (`List<uint>`). A palette repaint patches the entries whose span has no colour, without a re-measure.
- **Proof.** `Thorium` `Vaults.ui.xml` carries no colour except the close button's red states and the scroll thumbs; `Palette="thorium-light"`, `Role="Surface"` on the frame, `Role="Chrome"` on Add vault, `Role="MutedInk"` on its two captions.

## Why these choices

**One `uint` holds either a palette slot or the authored colour itself.**
The ask was an index into a global styling array instead of per-control colour values, without palettes taking full authority. `ColorHex` is 6 hex digits — exactly 24 bits — so inline costs no precision against what authoring can express. Interning authored colours into the table instead was rejected: per-letter animated colour (a standing decision) and Carbon's chart colours would grow it without bound. The byte saving is modest (16 per row); the real gain is that a palette colour changes in one table entry.

**Hover, press and muted text are calculated, not authored per role.** (user, 2026-09-16)
It keeps a palette to 11 values and is the only form that stays correct on an arbitrary ground — an authored muted grey is wrong on an accent or danger ground. Checked against Thorium's hand-picked values: hover within 5 levels, muted within 3, press within 9 (Thorium's press steps are wider than 2 × `Step`).

**Uncoloured containers default to `Clear`.** (user, 2026-09-16)
It is where most of the attribute saving comes from — authors repeated the parent's `ColorHex` on every structural container. The one visible change found: Carbon's Scale slider no longer paints an opaque `#FFFFFF` box behind itself.

**Resolution rides `WriteArranged` because the clip already does.**
Two earlier designs were rejected by the user. A per-frame check in `UIEngine.Collect` using stamps misses a reparent — a moved control's stamp is newer than its new parent's. Remembering the palette and ground each control resolved against fixed that, as did stamps plus invalidation in `AddChild`, but attach has ~10 direct `children`/`parent` write sites and neither fix read as intuitive. The lifetime already has an inherited channel that every attach, move and show reaches: `WriteArranged` inherits the clip, and `Hide` pushes the clip down directly through `CollapseClip`. Colour took the same two paths, so `Collect` is untouched and the drag ghost needs nothing.

**A control that names a palette does not paint its `Ground`.** (user, 2026-09-17)
Naming a palette only changes which palette applies below. Consequence: text in a region of palette B sitting on palette A's surface picks B's raw ink on the CPU; it will not re-flip on its own if A's values change later.

**Animation is skipped for now.** (user, 2026-09-17)
The shape leaves room for it: a shared animation (theme crossfade) would write table slots, a per-control one (hover ease) would write that control's inline word. See [../Context/ui-animation-plan.md](../Context/ui-animation-plan.md).

## Known gaps

- **Slice 2** — the composite controls still author hex defaults in C# (~60 `*ColorHex` attributes, ~134 literals): `DocumentToolbarControl`, `FileBrowserControl`, `TabViewControl`, `ScrollableControl` thumbs, `ContextMenuControl`, `ConfirmWindow`, `NoteNameWindow`, `SettingsWindow`, `TextBoxControl`, `SliderControl`, `DocumentControl` selection/caret, Carbon's custom controls. Needs a role-mapping table, then a mechanical sweep.
  - `BlockControl.SplitAt` copies `colorHex` into the new block, which will mark it authored once blocks are palette-driven; copy the paint state instead. `CaretStyle` falls back to `block.colorHex` for the toolbar's colour readout.
  - detached roots — context menus, confirm windows, the drag ghost — resolve against `default` until attached
- **Slice 3** — strip colours from the remaining `*.ui.xml` (Thorium `UI`, `Settings`, `TabWindow`, `Workspace`, `TabPane`; engine `Settings`; Carbon `UI`).
- Contrast against a gradient or image ground uses the control's own paint word, not what is drawn.
- A button's text colour is keyed to its ground's role, not its state; a mid-tone at the contrast crossover could get low-contrast ink on press.
- Gradients still upload once through `CreateBuffer`; moving them onto the per-image path stays a separate item (user, 2026-09-17).
- Inline colours are 8-bit per channel — identical to hex today, but a long animated fade on a dark colour could band.
- **Verified:** builds clean. Step 1 pixel-diffed against pre-change captures — Thorium main window, its menu, Vaults, Carbon idle and with a capture loaded: identical except the 1px OS border. Palette math in a scratch harness (the planned `AuroraTesting` project does not exist). After roles: Thorium main and menu still identical; Vaults within 12 levels per channel; close-button hover turns its icon light and returns on leave; minimise hover steps to `#E4E3DE`; Settings shows only its authored colours; a temporary nested `Palette="default"` recoloured only its row. **NOT verified:** a runtime `Role`/`Palette` change, a reparented control, the drag ghost, the Editor, confirm windows.

Related: [[ui-gradients]], [[ui-quads-pool]], [[ui-draw-list]], [[ui-engine-stack]], [[control-edge-and-outline]], [[vault-browser-and-shell]]
