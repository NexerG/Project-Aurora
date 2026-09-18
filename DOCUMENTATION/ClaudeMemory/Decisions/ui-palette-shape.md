# Decision — a palette carries shape as well as colour: radii, accent widths, window corners

**Date:** 2026-09-18
**Scope:** `ArctisAurora.Core.UI` — `PaletteDefinition`, `CornerRole`, `AccentRole`, `WindowCorners`, `Control` (`cornerRole`, `accentRole`, `edgeRole`, `ApplyShape`, `InheritPaint`), `FileBrowserControl`, `TabViewControl`, `ContextMenuControl`, `SettingsWindow`, `NoteNameWindow`, `ConfirmWindow`; `ArctisAurora.EngineWork.Rendering` — `AGlfwWindow.RoundCorners`

## What changed
- `PaletteDefinition` gained optional shape attributes, parsed in `Palettes.Parse`:

| Attribute | Default | Consumed by |
|---|---|---|
| `RowRadius` | 0 | `CornerRole.Row` — file browser rows |
| `TabRadius` | 0 | `CornerRole.Tab` (top-left + top-right), `CornerRole.TabEnd` (top-right only, the tab's close button) |
| `ControlRadius` | 4 | `CornerRole.Control` — Settings dropdowns, key capture and buttons; NoteName and Confirm buttons |
| `PopupRadius` | 6 | `CornerRole.Popup` — `ContextMenuControl` |
| `RowAccentWidth` | 3 | `AccentRole.Row` — left edge on the current browser row |
| `TabAccentWidth` | 2 | `AccentRole.Tab` — top edge on the active tab and its close button |
| `WindowCorners` | `Round` | `AGlfwWindow.RoundCorners` → DWM `DWMWCP_ROUND` / `ROUNDSMALL` / `DONOTROUND` |

- `Control.cornerRole` / `accentRole` are **C#-only** (no XSD attribute). `ApplyShape` writes `cornerRadius` / `edgeThickness` from `palette ?? Palettes.Default`; it runs from both setters and from `InheritPaint`, so a live palette switch (which invalidates arrange) reshapes rows and tabs without rebuilding them. Setting `accentRole` back to `None` zeroes `edgeThickness`.
- `Control.edgeRole` (`EdgeRole` in XML): an unauthored edge paints that surface role instead of `EdgeAccent`. Thorium's title bar authors `EdgeThickness="0,0,1,0" EdgeRole="Line"`.
- Settings' palette dropdown calls `window.os.RoundCorners()` on every `Engine.windows` entry before invalidating arrange.
- Thorium palettes, from the Periodic Shell Refresh canvas: `RowRadius` 4 on light/bone/violet/terracotta/glacier/aurora, 3 on phosphor/oxblood/yellow, 0 (default) on cyberpunk/printstream/void; yellow sets `RowAccentWidth`/`TabAccentWidth` 3.

## Why these choices

**Shape lives on the palette, not a separate style file (user, option A).**
The Periodic artboards pair each colour scheme with its geometry (Phosphor's 3px rows, Void's hard edges), so one file per theme matches the design. A separate `*.style.xml` named by the palette would let 12 palettes share three geometry sets; rejected as more machinery than the token count needs.

**No token references from `.ui.xml` (user).**
`CornerRadius="Row"` would need the converter to take names and a re-resolve on palette switch. Only code-built controls consume tokens; XML keeps plain numbers. This is also why `RuleWidth` was dropped — nothing could read it.

**Roles resolved in `InheritPaint`, not values stamped at build.**
Rows and tabs are not rebuilt on a palette switch; `InheritPaint` is what the switch already re-runs, and radius/edge width are visual-only so writing them there costs no layout.

**CheckBox and Slider keep fixed radii (user, 2(b)).**
One `ControlRadius` cannot preserve their 2/3; only buttons, dropdowns and fields follow it. Settings dropdowns and key capture moved from 3 to the default 4.

**The close button takes `TabEnd`.**
It sits at the tab's right edge painting the tab's ground; square, it would cover a rounded tab's top-right corner.

**Window corners are the DWM enum, not pixels.**
Arbitrary radii need a transparent swapchain and a shader clip — Vulkan pipeline work, fenced off.

**No shadows (user).** The UI shader has no shadow; popups stay flat.

## Known gaps
- `ContextMenuControl` rows keep a fixed 4px radius — sharp palettes get round menu rows inside a square popup.
- Browser rows sit flush against the window edge (`VaultBrowser Padding="8,0,8,0"` is top/bottom), so the left corners are mostly under the edge; Periodic inset rows 8px.
- Menu windows and the drag ghost are not in `Engine.windows`; they take new window corners only when next created.
- No palette sets `WindowCorners` or `TabRadius` yet.
- Periodic's 2px row accent is not adopted; everything except yellow keeps 3.
- **GUI-verified** (thorium-phosphor, 2026-09-18): title rule pixel = `Line` #18261D; tab top accent 2px; row corner anti-aliased at the window edge. **NOT verified:** live switch reshaping, `WindowCorners` Small/Square, `TabRadius` > 0 with the close button, Settings/NoteName/Confirm button radii.

Related: [[ui-palettes]], [[control-edge-and-outline]]
