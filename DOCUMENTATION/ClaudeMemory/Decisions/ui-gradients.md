# Decision — gradients are a shared table, not per-control data

**Date:** 2026-08-22
**Status:** LANDED. **GUI-verified** — both kinds screenshotted and pixel-sampled in Periodic.
**Scope:** `ArctisAurora.Core.UISystem` (`Gradients`), `ArctisAurora.Core.UISystem.Controls`
(`VulkanControl`, `TextControl`, `TextInputControl`, `TextRun`),
`ArctisAurora.EngineWork.Rendering` (`MCUI`, `UIModule`),
`Shaders/UIRasterizer/UI.vert` + `UI.frag` in all three host projects.

## Decisions

### 1. A table indexed by gradient, not stops carried per control

`ControlData.gradientIndex` names a row in a `GpuGradient[]` uploaded once at bootstrap. This is the
same answer [[glyphs-as-pool-data]] took for textures — index the *thing*, not the instance — for the
same reason: the alternative puts ~184 bytes of stops on every row in a pool that holds a control per
glyph, and duplicates a definition the author wrote once.

It is also what the user actually asked for ("define them in xml and use them on multiple ui
elements"): editing a gradient is one table row, not N control rows.

**Slot 0 is reserved and never nameable.** A zeroed `ControlData` is therefore gradient-free with
nobody writing to it, the shader branch is `gradientIndex > 0u`, and the buffer is never zero-sized
even when a host authors no gradients at all. `IndexOf("")` returns 0; `IndexOf(unknown)` throws,
following [[context-menu-invoker]]'s "a bad name fails the boot, not the interaction".

### 2. Procedural, evaluated per fragment, no texture anywhere

The user rejected a texture-backed design outright. `sampleGradient` takes an index, a point and a
rect, and walks the stops with a progressive `mix`:

```glsl
color = mix(color, g.stops[i].color, clamp((t - from) / max(to - from, 1e-5), 0.0, 1.0));
```

The clamp is what makes a chain of `mix`es equal a piecewise ramp — before stop `i` the factor
clamps to 0 (keeps what came before), after it clamps to 1 (takes the new colour). No branch per
stop, no lookup table, no sampler.

**8 stops, fixed, inline in the struct.** One SSBO, no offset/count indirection. 184 B/gradient, so
a 32-gradient theme is 6 KB — the variable-length form buys nothing at this scale and costs a second
buffer. Raising the cap is a constant in two places (`Gradients.MaxStops`, the GLSL array size).

### 3. Gradients ramp across a `gradientRect`, not the control's own quad

This is the decision that made per-run gradient text possible, and it is why `ControlData` grew 20
bytes rather than 4.

`fragLocal`/`fragHalfExtent` (what the edge SDF uses) are the *control's* box. Every glyph is its own
control, so a gradient measured that way restarts on every letter — the word "Welcome" would be eight
independent ramps. So the gradient reads a `vec4 gradientRect` in the same design space as `fragPos`
and `clip`, which a control can be handed by something above it.

**The mirror is the `arrangedRect` setter**, exactly the trick [[ui-clipping]] used for `ClipRect`.
Fifteen sites assign `arrangedRect = finalRect` across eight `Arrange` overrides; making the property
mirror into the pool row means **none of them changed**. A `TextControl` then overwrites its glyphs'
rects with its own after `glyph.Arrange`, so the ramp spans the run.

**A run's rect is the block's inner width, not the run's ink extent** (`TextBlockControl.Arrange`
hands it `inner.width`, and `firstLineOffset` moves the glyphs but not the rect). Chosen deliberately
over computing the glyph-rect union: it keeps two runs sharing a line continuous under one gradient,
and a run wrapped across lines gets a sane box instead of one spanning lines it does not fill. The
cost is that a run starting mid-line measures its ramp from the column edge. Labels are unaffected —
a caption's rect *is* its ink.

### 4. The gradient replaces the fill colour; its alpha lands after the outline

```glsl
if (fragGradientIndex > 0u) { color = ramp.rgb; gradientAlpha = ramp.a; }
// outline branch — assigns opacity outright
opacity *= gradientAlpha;
```

The colour substitution happens **before** the outline so the outline's `mix(outlineColor, color,
fillAlpha)` composites against the gradient. The alpha multiply happens **after** it, because that
branch *assigns* `opacity` rather than multiplying into it — putting the multiply first would let a
control with an outline silently discard the gradient's alpha.

The edge still wins over both, unchanged from [[control-edge-and-outline]] §4.

**Coverage is untouched.** A gradient recolours; it does not paint. So it shows exactly where
`ColorHex` shows and is equally invisible on an `invisible`-masked container — no new trap, just the
existing one.

### 5. Angle is baked CPU-side and spans the box corner to corner

`Angle` → a unit `direction` at load, so the per-pixel path is a dot product. Normalising by the
box's support function
(`span = |d.x|·extent.x + |d.y|·extent.y`) is what makes 0..1 cover the rect *for that angle* — the
CSS behaviour — instead of skewing with aspect ratio the way a normalised-UV gradient would.

Convention: **0° left→right, 90° top→bottom** (+y is down). Radial is an ellipse fitted to the
farthest corner, centred by normalised `CenterX`/`CenterY`.

Because the rect is in design space, the angle is now in the same space as `EdgeThickness` — this
does *not* add a third unit to the [[control-edge-and-outline]] §1 wart.

### 6. Interpolation is in the tint's own space

`HexToRGB` divides by 255 with no de-gamma, and the tint has always been used that way. Ramping in
the same space keeps a two-stop gradient between X and Y consistent with `ColorHex="X"` and
`ColorHex="Y"` on neighbouring controls. Perceptually-correct interpolation would mean fixing the
whole colour pipeline, which is not this change. Saturated complementary stops will read muddy in the
middle.

### 7. Three style lists needed the field, and none of them are obvious

Reflection carries `Gradient` through XML load *and* save for free — `DocumentXml` is attribute-driven
and needed no writer change. But the document's in-memory editing path hand-copies run style in three
places, and each one silently loses a gradient if missed:

| Site | What breaks without it |
| --- | --- |
| `TextInputControl.StyleEquals` | two runs with different gradients merge into one |
| `TextInputControl.SplitAt` | splitting a run drops the gradient on the right half |
| `TextRun.Clone` | structural edits drop it |

In all three the gradient is assigned **before** `text`, because the `text` setter is what builds the
glyphs and `SyncGlyphs` pushes the gradient into each new one.

## Verified

- `Unsafe.SizeOf<ControlData>()` == 136 with fields at 0/32/48/52/68/84/96/100/112/**116**/**120**;
  `spirv-dis` on the compiled `UI.vert` reports the identical offsets and `ArrayStride 136`. Probed
  against the built assembly, not read off the source.
- `Unsafe.SizeOf<GpuGradient>()` == 184 / `GpuGradientStop` == 20 / `GradientStops` == 160, matching
  `spirv-dis` `ArrayStride 184` and `20` in `UI.frag`. `[InlineArray(8)]` — first use in this repo —
  lays out as a plain 160-byte run with no padding.
- All six shaders compile with `glslc --target-env=vulkan1.3`, unoptimized to match the artifacts
  already committed. All three copies agree on stride 136.
- `dotnet build Periodic` — 0 errors, no warning in any touched file.
- Boots clean with validation layers on: `Gradients.LoadGradients` runs, all 25 bootstrap steps pass,
  no validation message from the new set0/b3 binding.
- **GUI-verified.** Linear across a run: the `Heading1` "Welcome to Periodic" ramps `#386699` →
  `#4268A4` → `#6660B7` left to right *across the whole run*, not per glyph. Linear on a container:
  the title bar ramps `#1E1E1E` → `#141414` top to bottom where it is exposed (the middle is flat
  because the spacer `<Panel>` paints over it). Radial: peaks `#2A4663` at centre and falls off on
  both axes, elliptical, alpha-faded.

## Still open

- **No gradient on the edge or the outline.** Both would be another index each; nothing structural
  stops it.
- **No per-state gradients.** `ButtonControl` has `HoverColorHex`/`PressColorHex` with no gradient
  twin, so a gradient button does not respond to hover.
- **The table is built once and never updated.** Animating a gradient, or editing one at runtime,
  needs the buffer re-uploaded — `MCUI.CreateGradientTable` is the only writer, so it is a dirty flag
  away, but nothing is wired.
- **A gradient cannot span more than one run.** A heading of two runs gets two ramps. The generic fix
  is a `GradientSpace="Self|Inherit"` on `VulkanControl` letting the `arrangedRect` setter take the
  parent's rect — about five lines, deliberately not built without a use for it.
- **Angle is design-space**, so a host on `WindowingMode="ScaleUp"` gets a gradient that scales with
  the design box. Correct, but untested — Periodic is `WindowSize`.

Related: [[glyphs-as-pool-data]], [[ui-clipping]], [[control-edge-and-outline]],
[[text-styling-types]], [[ui-data-control-split]]
