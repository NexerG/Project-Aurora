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
VerifiedAgainst: 2026-05-30
---
## Description

Per-glyph outline + metrics. Holds the raw quadratic-bezier `contours` parsed from the font's `glyf` table and the derived `edgeContours` used by MTSDF generation, plus the layout metrics serialized into the atlas data (`.agd`).

## Fields & Properties

```C#
public short xMin, yMin, xMax, yMax;
public float glyphWidth = 1;
public float glyphHeight = 1;

[@NonSerializable] public List<List<Edge>> edgeContours = new();  // built for MTSDF
[NonSerializable]  public List<Bezier> contours = new();          // raw outline

public float advanceWidth;     // horizontal advance (Ã· unitsPerEm)
public float leftSideOffset;   // left side bearing
public float tsb = 0;          // top side bearing (when yMin < 0)
```

## Methods

### `BuildEdges` *(public)*
Converts the raw `contours` (on-/off-curve bezier points) into `edgeContours` of quadratic `Edge`s. Consecutive off-curve points get an *implied* on-curve midpoint inserted between them â€” standard TrueType outline reconstruction.

### `SetParams` *(public)*
Stores the glyph bounds and computes the normalized `glyphWidth`/`glyphHeight` from `unitsPerEm`.

## Related
- [[Aurora Font]] â€” parses outlines into glyphs and bakes the MTSDF atlas
- [[Atlas Meta Data]] â€” stores the serialized glyphs
