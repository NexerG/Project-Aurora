# Decision — two strokes, because there are two distance fields to stroke

**Date:** 2026-08-19
**Status:** LANDED, compile- and layout-verified. **NOT GUI-verified** — nothing authors either
stroke yet, so a run shows no change until someone writes `EdgeThickness` or `OutlineWidth`.
**Scope:** `ArctisAurora.Core.UISystem.Controls` (`VulkanControl`),
`Shaders/UIRasterizer/UI.vert` + `UI.frag` in all three host projects.

## Decisions

### 1. `EdgeThickness` strokes the box SDF, `OutlineWidth` strokes the mask

The user asked for "edge color" and, offered the fork, took **both**. They are not one feature with
two settings — they read different distance fields and land in different places:

| | field | traces | unit |
|---|---|---|---|
| `EdgeThickness` / `EdgeColorHex` | `sdRoundBox(fragLocal, fragHalfExtent, fragRadius)` | the control's rounded rectangle | design-space px |
| `OutlineWidth` / `OutlineColorHex` | the MSDF/MTSDF median distance | the shape inside the quad — the letter | screen px |

On a `GlyphControl` the edge draws a box around the letter's quad and the outline traces the letter.
Neither is a substitute for the other, which is why both exist rather than one `Edge*` pair with a
mode enum: the mode would be a switch nobody sets, and a control that wants a bordered box of
outlined text wants both at once.

**Unit split is deliberate and is the one wart.** The box SDF is exact in design space, so an edge
in design px is exact. The MSDF distance resolves in screen pixels (`screenPxRange` is derived from
`fwidth(fragUV)`), so an outline in design px would need the design→screen scale, which the fragment
stage does not have. `fwidth(boxDist)` looks like that scale but is not — `sdRoundBox` has unit
gradient, so `fwidth` of it swings between 1.0 axis-aligned and 1.414 on a diagonal, a 40% error.
Carrying a real scale factor means another varying and a vertex-side derivation. Moot today:
Periodic's `Window` is `WindowingMode="WindowSize"`, where design px *are* screen px. Revisit if a
host ships `ScaleUp`.

### 2. The edge carries its own coverage; the outline does not

```glsl
opacity = max(opacity, band);   // edge
opacity = clamp(screenPxDist + fragOutlineWidth + 0.5, 0.0, 1.0);  // outline, still * inside later
```

A container masked `invisible` has zero mask coverage everywhere — that is what makes it invisible,
and per `vault-browser-and-shell` it is what every `StackPanel`/`GridList` needs to avoid painting an
opaque quad. If the edge multiplied into the mask the way the fill does, `EdgeThickness` on a
container would draw nothing, and containers are the single most likely thing to want a border. So
the band contributes coverage directly.

The outline is the opposite case: it traces the mask's shape, so with no shape there is nothing to
trace, and multiplying is correct.

Both are still multiplied by `inside` (the box silhouette) and both still sit behind the clip
discard, so neither escapes the control's rectangle or its scroll viewport.

### 3. Zero is off, via a real branch

`clamp((boxDist - 0.0) / boxAA + 0.5, 0, 1)` at the silhouette boundary is `0.5`, so a
zero-thickness edge evaluated as an edge tints the antialiased boundary pixel half-way to
`edgeColor` on **every control in the tree**. Same for a zero-width outline via `mix(outlineColor,
color, fillAlpha)` at `fillAlpha == 0.5`.

Guarding with `if (fragEdgeThickness > 0.0)` makes the default path bit-identical to what shipped
before this change. The branch is on a `flat` input, so it is uniform across the primitive and does
not diverge within a quad.

### 4. Order: outline first, edge last

The edge is the outermost thing on the control, so it composites over the already-outlined colour
and wins where they overlap. Reversing it would let a glyph's outline bleed through its own control
border.

### 5. `ControlData` is 68 → 100 bytes

`vec3 + float` in `scalar` layout packs to 16 tight, same as a `vec4`, so the C# side stays readable
as four named fields rather than two packed `Vector4D<float>`s whose `.W` means something unrelated
to their `.XYZ`.

Verified on both sides rather than assumed — `Unsafe.SizeOf<ControlData>()` reports 100 with fields
at 0/32/44/48/64/68/80/84/96, and `spirv-dis` on the compiled `UI.vert` reports
`OpMemberDecorate` offsets 0/32/44/48/64/68/80/84/96 with `ArrayStride 100`. Exact match. Any drift
here and every control past the first reads shifted data, so this check is the point of the slice.

Four new `flat` varyings take the stage interface to 12 locations / 25 components, well inside the
64-component floor.

### 6. Hex only — no `EdgeColor`/`OutlineColor` enum twins

`controlColorHex` has a `controlColor` enum companion off the `ControlColor` palette. The new pairs
get the hex form only. A border colour is picked to sit against a specific ground, which is what a
hex is for; sixteen named constants would be authored roughly never. Trivial to add later — the
setter is four lines and `EnumColorToHex` already exists.

## Verified

- `Unsafe.SizeOf<ControlData>()` == 100, field offsets as listed above (probe against the built
  assembly, not by inspection).
- `spirv-dis` member offsets and `ArrayStride` match the C# layout exactly.
- All six shaders compile with `glslc --target-env=vulkan1.3`, unoptimized to match the artifacts
  already in the repo (an `-O` build is ~40% smaller and would have been a silent flag change).
- `dotnet build Periodic` — 0 errors, warning count unchanged.
- **Not** GUI-verified: no control authors an edge or an outline, so the render is unchanged by
  construction. `<Panel EdgeThickness="2" EdgeColorHex="#3A3A3A" CornerRadius="6"/>` is the
  one-liner that proves it.

## Still open

- `UITypeSchema.xsd` and `SchemaManifest.xml` regenerate on the next run — the four properties are
  not in the committed schema until then. Nothing validates at load (`XSDGenerator` writes schemas,
  no `XmlSchemaSet` reads them), so authoring works immediately; only IDE completion lags.
- Outline width in screen px vs edge thickness in design px, per §1.
- The outline is a hard stroke against a threshold, so a wide one on a small glyph will close up
  interior counters (the hole in an "o"). Inherent to threshold outlining, not a defect to fix here.
- `UI.frag` still MSDF-decodes every control including plain panels on the `invisible` mask, and now
  runs two threshold clamps on them. Same pre-existing item as in [[ui-clipping]].

Related: [[ui-clipping]], [[glyphs-as-pool-data]], [[vault-browser-and-shell]],
[[button-states-and-hover-bubbling]]
