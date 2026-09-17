# Decision — a control's colour comes from a palette role, carried as a paint word

**Date:** 2026-09-17
**Scope:** `ArctisAurora.Core.UI` — `Palettes`, `PaletteDefinition`, `PaletteRole`, `PaletteSetting`, `UISettings`, `Control` (paint region, `WriteArranged`), `ButtonControl`, `TextRunControl`, `VulkanControl`, the composite controls under § Thorium on the palette; `ArctisAurora.EngineWork.Rendering.Modules.UIEngineModule`; `Shaders/UIEngine/UIEngine.vert`; data `*/Data/XML/Documents/Palettes/*.palette.xml`, `Thorium/Data/XML/Settings/UI.settings.xml`

Slice 1 of 3, then slices 2 and 3 for everything Thorium shows (same day). What is left of them is under Known gaps.

## What changed

- **Paint word.** `VulkanControl.tint` (vec4) → `paint` (uint) + `alpha` (float); `edgeColor` (vec3) → `edgePaint` (uint). Row 92 → 76 bytes.
  - top bit set → low 24 bits are `0xRRGGBB`, inline (`Palettes.Inline`)
  - top bit clear → slot in the paint table (`Palettes.Table`, `vec4[]`)
  - slot 0 is transparent black, so a zeroed row paints nothing
- **Paint table on the GPU.** Set 1 binding 4 `PaintBuffer`, vertex stage. `UIEngineModule.MirrorPaints` keeps one host-visible mapped buffer per swapchain image and rewrites it when `Palettes.Table` is a different array (reference identity is the version). The table is replaced whole on load, never written in place.
- **Palettes.** One `<Palette>` per `XML/Documents/Palettes/*.palette.xml`, unioned across mounts (`VirtualFileSystem.EnumerateAll`); a same-named file in an app replaces the engine's. Bootstrap step `Palettes.LoadPalettes`, after `Gradients.LoadGradients`.
  - authored: `Ground Surface Chrome Field SubField Line Accent Danger` (surfaces), `DarkInk LightInk`, `Step`, `Muted` — 12 values, all required except `Step`/`Muted`
  - baked per palette, a 42-slot block: 8 surfaces × rest/hover/press, ink + muted ink per surface, the two raw inks
- **The app's palette is a setting.** `UISettings.palette` (`PaletteSetting`, `<UI><Palette Name="…"/>`, default `default`) names `Palettes.Default`. `Settings.LoadAll` is step 1 of bootstrap, so `LoadPalettes` just reads it; boot halts when no loaded palette has that name. Read once — a change in the Settings window applies on the next launch. Thorium's `UI.settings.xml` names `thorium-light`.
- **Derivation.** Text colour = whichever of `DarkInk`/`LightInk` has the higher WCAG contrast against the ground. Muted = ink blended toward the ground by `Muted`. Hover/press = colour blended toward its ink by `Step` × 1 / × 2.
- **Slot rule.** A derived colour is a slot only when every input is a slot of that palette. Ink on another palette's surface or on an inline ground picks the raw ink slot; muted ink or a stepped inline colour is computed on the CPU and written inline.
- **Roles.** `Control.role` (`Role=`): `None` (no palette), `Clear` (paints nothing, ground passes through), the 8 surfaces, `Ink`, `MutedInk`. Defaults: `PanelControl`, `ContainerControl`, `ButtonControl` → `Clear`; `TextRunControl` → `Ink`; `IconControl` → `MutedInk`; `WindowFrameControl` → `Ground`; `TitleBarControl` → `Chrome`; `SplitterControl` → `Line`; `CaretControl` → `Ink`; `HintControl` → `Accent`; `ContextMenuControl` → `Ground`; everything else `None`.
- **Authored wins.** The `colorHex` setter marks the control authored; resolution never repaints it.
- **Palette reference.** `Control.paletteName` (`Palette=`), nearest ancestor wins, `Palettes.Default` when none. Naming a palette does not paint a ground.
- **Resolution is inherited in `WriteArranged`, like the clip.** `InheritPaint` takes `palette` and `groundBelow` from the parent (a root sits on its palette's `Ground`), paints the role, stores both. A control is always arranged before it is drawn, and attach, move and show all end in an arrange.
  - `Role` / `Palette` set at runtime → `InvalidateArrange`, as alignment does
  - paint that moves outside layout (button state, `ColorHex` or `Alpha` at runtime) → `RepaintChildren`, the `CollapseClip` of colour; skipped while arrange-dirty, stops at a child whose ground did not move
  - ground = the control's paint when it is an opaque plain panel (`alpha > 0`, `PanelControl` kind, no sampler); otherwise its parent's
- **`ButtonControl`.** Authored state hex wins; an authored rest keeps today's fallback chain; a palette rest steps. A `Clear` rest has alpha 0 until hovered.
- **`TextRunControl`.** `_runColors` → `_runPaints` (`List<uint>`). A palette repaint patches the entries whose span has no colour, without a re-measure.

## Thorium on the palette (2026-09-17)

- **`Control.PaintOr(string? hex, PaletteRole role)`** — a hex sets `colorHex`; null clears `colorAuthored` and sets `role`. **`Control.CopyPaint(Control source)`** — the source's authored hex, else its role; used by `SplitViewControl.NewPane` and `BlockControl.SplitAt`.
- **A composite's `*ColorHex` properties are `string?`, null = palette**, applied to its parts through `PaintOr`: `DocumentToolbarControl`, `FileBrowserControl`, `TabViewControl` (and `EditableTabsControl`, `SplitViewControl` grips), `ScrollableControl` thumbs, `DocumentEditorControl`/`DocumentControl` caret and selection, `TextBoxControl`, `EditableLabelControl`. XML that still authors them renders as before.
- **`TextBoxControl.PaintText` / `EditableLabelControl.PaintText(string? hex, PaletteRole role)`** — text colour passes through both halves; folder rows need `MutedInk`, not the text default.
- **Built in code, no attribute:** `ContextMenuControl` (+ `ApplyRole` override writing `edgePaint` from `Line`), `ConfirmWindow`, `NoteNameWindow`, `SettingsWindow` rows, `CheckBoxControl` mark, `DropdownControl`/`KeyCaptureControl` captions; Thorium `VaultsWindow` rows, `VaultBrowserControl.BuildTab`.
- **Document ink.** `BlockControl` authors no colour, so blocks paint `Ink`. A run saved as exactly `#2C2B26` (the old block ink, `legacyInkHex`) loads uncoloured and drops the attribute on its next save. The toolbar's "Default" swatch applies `StyleDelta(colorHex: "")`: an empty colour clears the span's. `CaretStyle.colorHex` is null for a palette-inked span; the toolbar readout paints it `Ink`, or `MutedInk` with no editor.
- **Thorium's six `*.ui.xml` carry no colour** except the title-bar close buttons' red `HoverColorHex`/`PressColorHex`. `Vaults.ui.xml` no longer names a palette.

| Part | Role |
|---|---|
| window frame / stacks | `Ground` (Settings, Vaults, dialogs: `Surface`) / `Clear` |
| title bar; its buttons, menu buttons, spacer | `Chrome`; `Clear` (the gradient shows through at rest) |
| title-bar, button and dialog-button captions, icons, idle toolbar ink, folder names + chevrons, vault paths, locked keybinds | `MutedInk` |
| sidebar, Settings categories | `Surface` |
| splitters, pane grips, toolbar separators, menu rules, scroll thumbs, selection | `Line` |
| toolbar buttons, file rows, menu rows | `Clear` |
| active / inactive tab (and its close area) | `Ground` / `Surface` |
| toolbar px field | `Field` |
| rename fields, every Settings editor, new-note name field | `SubField` |
| dialog and Settings buttons, Vaults rows | `Chrome` |
| active toolbar ink, drop hint | `Accent` |
| text, captions, caret, checkbox mark | `Ink` |

## Why these choices

**One `uint` holds either a palette slot or the authored colour itself.**
The ask was an index into a global styling array instead of per-control colour values, without palettes taking full authority. `ColorHex` is 6 hex digits — exactly 24 bits — so inline costs no precision against what authoring can express. Interning authored colours into the table instead was rejected: per-letter animated colour (a standing decision) and Carbon's chart colours would grow it without bound. The byte saving is modest (16 per row); the real gain is that a palette colour changes in one table entry.

**Hover, press and muted text are calculated, not authored per role.** (user, 2026-09-16)
It keeps a palette small and is the only form that stays correct on an arbitrary ground — an authored muted grey is wrong on an accent or danger ground. Checked against Thorium's hand-picked values: hover within 5 levels, muted within 3, press within 9 (Thorium's press steps are wider than 2 × `Step`).

**`SubField` is its own surface.** (user, 2026-09-17)
thorium-light's `Field` equals its `Surface` (`#F2F1ED`), so a field sitting on a panel — a rename field on a sidebar row, every Settings editor, the dialog fields — vanished into it. Rejected: retuning `Field`, which also moves the toolbar's px field on the window ground. `default` gets `#1B1B1B`, the engine dialogs' old field ground; thorium-light gets `#EAE8E2`, the vault browser's old rename ground.

**The app's palette is a setting, not a root attribute.** (user, 2026-09-17)
Windows built in code — context menus hosted in their own window, the confirm and note-name dialogs — have no authored root to carry `Palette=`, so they resolved against `default` (dark) inside a light app. Naming `Palettes.Default` once covers every root. Rejected: overriding the engine's `default.palette.xml` from Thorium (loses the named `thorium-light`), and plumbing a palette name into each code-built window. Settings load before anything parses UI, so reading at boot needs no live-switch machinery.

**Thumbs and selection use `Line`.** (user, 2026-09-17)
The palette has nothing darker short of the inks. They are fainter than Thorium's hand-picked `#D7D5CD`: thumb rest `#E6E4DE`, hover `#DFDDD7`, press `#D8D6D0`. Rejected: a dedicated handle surface.

**The old block ink is migrated at load.** (user, 2026-09-17)
A run carrying exactly `#2C2B26` is what the "Default" swatch wrote before palettes, so it meant "default", not a chosen colour. Rejected: keeping it as authored, which pins those runs dark on any future dark palette.

**Split copies go through `CopyPaint`.** (user, 2026-09-17)
Copying `colorHex` from an unauthored source reads the base default `#FFFFFF` and authors it. Rejected: dropping the copy, which stops an authored pane colour reaching the panes split off it.

**Uncoloured containers default to `Clear`.** (user, 2026-09-16)
It is where most of the attribute saving comes from — authors repeated the parent's `ColorHex` on every structural container. The one visible change found: Carbon's Scale slider no longer paints an opaque `#FFFFFF` box behind itself.

**Resolution rides `WriteArranged` because the clip already does.**
Two earlier designs were rejected by the user. A per-frame check in `UIEngine.Collect` using stamps misses a reparent — a moved control's stamp is newer than its new parent's. Remembering the palette and ground each control resolved against fixed that, as did stamps plus invalidation in `AddChild`, but attach has ~10 direct `children`/`parent` write sites and neither fix read as intuitive. The lifetime already has an inherited channel that every attach, move and show reaches: `WriteArranged` inherits the clip, and `Hide` pushes the clip down directly through `CollapseClip`. Colour took the same two paths, so `Collect` is untouched and the drag ghost needs nothing.

**A control that names a palette does not paint its `Ground`.** (user, 2026-09-17)
Naming a palette only changes which palette applies below. Consequence: text in a region of palette B sitting on palette A's surface picks B's raw ink on the CPU; it will not re-flip on its own if A's values change later.

**Animation is skipped for now.** (user, 2026-09-17)
The shape leaves room for it: a shared animation (theme crossfade) would write table slots, a per-control one (hover ease) would write that control's inline word. See [../Context/ui-animation-plan.md](../Context/ui-animation-plan.md).

## Known gaps

- **A selection inside a `SubField` text box is invisible** — `Line` `#E6E4DE` on `SubField` `#EAE8E2`, 4 levels (thorium-light; seen selecting "Untitled" in the new-note dialog). The document editor's selection on `Ground` is fine. Needs a decision.
- **Slice 2, what is left:** `SliderControl` (Carbon only, authored there), Carbon's custom controls, `ButtonControl`'s `#8C8C8C` fallback for a `None` role, `TabViewControl`'s red close-hover consts, the toolbar's seven fixed swatches (One Dark values; Yellow is low-contrast on a light ground).
- **Slice 3, what is left:** engine `Settings.ui.xml`, Carbon `UI.ui.xml`. Gradient stops stay authored hex.
- A palette change in the Settings window applies on the next launch.
- A subtree's own `Palette=` does not reach a context menu or window opened from it in a window of its own — those take `Palettes.Default`.
- Contrast against a gradient or image ground uses the control's own paint word, not what is drawn.
- A button's text colour is keyed to its ground's role, not its state; a mid-tone at the contrast crossover could get low-contrast ink on press.
- Gradients still upload once through `CreateBuffer`; moving them onto the per-image path stays a separate item (user, 2026-09-17).
- Inline colours are 8-bit per channel — identical to hex today, but a long animated fade on a dark colour could band.
- **Verified, slice 1:** builds clean. Step 1 pixel-diffed against pre-change captures — Thorium main window, its menu, Vaults, Carbon idle and with a capture loaded: identical except the 1px OS border. Palette math in a scratch harness (the planned `AuroraTesting` project does not exist). After roles: Thorium main and menu still identical; Vaults within 12 levels per channel; close-button hover turns its icon light and returns on leave; minimise hover steps to `#E4E3DE`; Settings shows only its authored colours; a temporary nested `Palette="default"` recoloured only its row.
- **Verified, Thorium:** builds clean; Thorium boots with the same 7 errors as before the change. Captured before and after, pixels sampled against the predicted values, all matching: main window (ground, title-bar gradient, sidebar, splitters, thumb, folder/file/note/menu ink), menu with a hovered row, Settings (Thorium, UI with the Palette row reading `thorium-light`, Logging with dropdowns, text fields and a checkbox), Vaults, the new-note dialog; hover on a tab `#F3F2F0`, a sidebar row `#EAE9E5`, a toolbar button `#F3F2F0`, minimise `#E4E3DE`. Carbon pixel-identical inside the window border.
- **NOT verified:** rename fields (sidebar and tab), the confirm dialog, a torn-off tab window, a split pane, splitting a block with Enter, the "Default" swatch clearing a colour, the `#2C2B26` migration (no note in the vault carries it), a runtime `Role`/`Palette` change, a reparented control, the drag ghost, the Editor.

Related: [[ui-gradients]], [[ui-quads-pool]], [[ui-draw-list]], [[ui-engine-stack]], [[control-edge-and-outline]], [[vault-browser-and-shell]], [[settings-categories]]
