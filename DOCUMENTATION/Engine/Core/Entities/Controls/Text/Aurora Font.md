---
date: 2026-05-30
Status: Current
tags:
  - d_UI
  - d_text
  - d_Font
cssclasses:
  - Aurora.css
Linker:
  - "[[Arctis Aurora]]"
System:
Class:
  - "[[Aurora Font]]"
Parent Class:
Interfaces:
  - "[[IDeserialize]]"
Used by:
Type:
  - Public
Attributes:
  - Serializable
Namespace: ArctisAurora.Core.UISystem
SourceFile: AuroraEngine/Core/UISystem/AuroraFont.cs
VerifiedAgainst: 2026-05-30
---
## Description

Parses a TrueType (`.ttf`) font and bakes an **MTSDF** glyph atlas. Two roles: (1) a serializable container for the font's table directory + character set (loaded from `.afm`), and (2) the static atlas generator that reads the `.ttf` tables, reconstructs glyph outlines, colors their edges, and renders a multi-channel-plus-true-distance atlas PNG along with the `.agd` metadata ([[Atlas Meta Data]]).

> Text rendering is an active WIP area. The pipeline now produces **MTSDF** (RGB edge channels + an alpha "true distance" channel), having moved on from plain MSDF.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `Deserialize(string path)` | public | Load `.afm` â†’ `FontMeta`, the `TableEntry[]` directory, and the char set. |
| `GenerateGlyphAtlas(AuroraFont fontData, string fontName, int perGlyphSize)` | static | Bake the atlas + `.agd` from a system `.ttf`. |

## Fields & Properties

```C#
[StructLayout(Sequential, Pack=1), @Serializable] public struct FontMeta   { uint version; ushort tableCount; }
[StructLayout(Sequential, Pack=1), @Serializable] public struct TableEntry { string name; uint checksum, offset, length; }
[StructLayout(Sequential, Pack=1), @Serializable] public struct TextData   { int characterCount; char[] characters; }

[@Serializable] public FontMeta fontMeta;
[@Serializable] public TableEntry[] tableEntries;
[@Serializable] public TextData textData;
```

## Methods

### `Deserialize` *(public)*
Reads the `FontMeta`, the `TableEntry` directory, and the character list from the `.afm` binary.

### `GenerateGlyphAtlas` *(static)*
Reads the `.ttf` from the **system fonts folder** (`Environment.SpecialFolder.Fonts`), then:
	`maxp` â†’ glyph count
	`head` â†’ `unitsPerEm` + `indexToLocFormat`
	`loca` â†’ glyph offsets
	per character: `cmap` (`GetGlyphIndex`) â†’ `glyf` (`GetGlyphOutline`) â†’ a [[Glyph]] of bezier contours
	`hhea`/`hmtx` â†’ `advanceWidth` + left side bearings
	derive `tsb`
	serialize the [[Atlas Meta Data]] â†’ `.agd`
	render each glyph cell via `GenerateMTSDF`
	save `{font}_atlas.png` (plus a debug `SDF_A.png`)

## Helpers

- `GetGlyphIndex` â€” `cmap` format-4 segmented lookup (char â†’ glyph index).
- `GetGlyphOutline` â€” parse `glyf` contours into beziers, `BuildEdges`, then assign MSDF **edge colors** (R/G/B) by corner detection so channels can be combined.
- `GenerateMTSDF` â€” per pixel: three per-channel signed distances + a true distance, packed into RGBA.
- `GetClosestDistanceOfChannel` Â· `ClosestTOnBezier` (coarse sample + Newton refinement) Â· `ComputeWindingNumber` (ray-cast via quadratic roots) Â· `SolveCubic` / `SolveQuadratic`.

## Related
- [[Glyph]] â€” the outline/metrics type produced here
- [[Atlas Meta Data]] â€” the serialized atlas metadata
- [[IDeserialize]] Â· [[Serializer]]
