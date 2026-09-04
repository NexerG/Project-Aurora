---
date: 2026-05-30
Status: Current
tags:
  - d_text
  - d_Font
  - d_Serialization
cssclasses:
  - Aurora.css
Linker:
  - "[[Aurora Font]]"
Class:
  - "[[Glyph]]"
Parent Class:
Interfaces:
Used by:
  - "[[Aurora Font]]"
  - "[[Atlas Meta Data]]"
Type:
  - Public
Attributes:
  - Serializable
Namespace: ArctisAurora.Core.UISystem
SourceFile: AuroraEngine/Core/UISystem/Glyph.cs
VerifiedAgainst: 2026-09-04
---
## Description

Per-glyph outline + metrics. Holds the raw quadratic-bezier `contours` parsed from the font's `glyf` table and the derived `edgeContours` used by MTSDF generation, plus the layout metrics serialized into the atlas data (`.agd`).

## Fields & Properties

The metrics live in a `GlyphMetrics` struct and the glyph carries one per face of the family, because every face measures its own advances and ink boxes for the same character. The outlines do not: only the regular face's contours are parsed into a `Glyph`, and each other face's measurements are folded onto it during the bake.

```C#
[@Serializable]
public struct GlyphMetrics
{
	// ink box, font units
	public short xMin, yMin, xMax, yMax;

	// cell and horizontal metrics, in em
	public float glyphWidth;
	public float glyphHeight;
	public float advanceWidth;
	public float leftSideOffset;
	public float tsb;
}

// one set per face of the family
public GlyphMetrics regular;
public GlyphMetrics bold;
public GlyphMetrics italic;
public GlyphMetrics boldItalic;

[@NonSerializable] public List<List<Edge>> edgeContours = new();  // built for MTSDF
[NonSerializable]  public List<Bezier> contours = new();          // raw outline
```

## Methods

### `BuildEdges` *(public)*
Converts the raw `contours` (on-/off-curve bezier points) into `edgeContours` of quadratic `Edge`s. Consecutive off-curve points get an *implied* on-curve midpoint inserted between them â€” standard TrueType outline reconstruction.

### `SetParams` *(public)*
Stores the glyph bounds and computes the normalized `glyphWidth`/`glyphHeight` from `unitsPerEm`.

### `Metrics` / `SetMetrics` *(public)*
Read and write one face's `GlyphMetrics` by [[Atlas Meta Data]]'s `FontStyle`. `SetMetrics` exists for the bake's fold loop, which walks the family's faces in block order and writes each one's measurements onto the regular face's glyph.

## Related
- [[Aurora Font]] â€” parses outlines into glyphs and bakes the MTSDF atlas
- [[Atlas Meta Data]] â€” stores the serialized glyphs
