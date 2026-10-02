---
date: 2026-02-19
Status: Current
tags:
  - d_UI
  - d_text
  - d_Font
cssclasses:
Linker: "[[Text]]"
Class: AtlasMetaData
Parent Class:
Interfaces: "[[IDeserialize]]"
Type: Public
Attributes:
  - Serializable
---
## Description

The purpose of this class is to store relevant data of the font (characters, some [[Glyph]] data) in order to be able to use the glyph atlas.
## Input/Output

- public void `Deserialize(string name)` -> deserializes the meta data relevant for using the glyph atlas given the font name.

## Fields & Properties

```C#
public int glyphCount;
public char[] chars;
public Glyph[] glyphs;
public float pxRange;

// which faces the family bake actually found
public bool hasBold;
public bool hasItalic;
public bool hasBoldItalic;

// char to index into chars and glyphs, built on load
[NonSerializable]
private Dictionary<char, int> charIndex;
private static readonly CharHash charHash = new CharHash();

private sealed class CharHash : IEqualityComparer<char>
{
	public bool Equals(char a, char b) => a == b;
	public int GetHashCode(char c) => c;
}

// em advances of the chars below AdvanceTableSize, one row per face, built on load
public const int AdvanceTableSize = 256;
[NonSerializable]
private float[] advances;

public int styleCount => 1 + (hasBold ? 1 : 0) + (hasItalic ? 1 : 0) + (hasBoldItalic ? 1 : 0);
public int cellCount => glyphCount * styleCount;
```

## Faces and the atlas grid

The atlas holds one cell per (character, face), laid out as consecutive per-face blocks in a square grid of `ceil(sqrt(cellCount))` cells to a side — regular first, then whichever of bold, italic and bold-italic the family had a file for. Nothing above this class knows how many faces there are: `CellIndex` turns a character index and a [[Glyph]] style into a flat cell number, and the caller divides it into the grid. Adding a face is therefore arithmetic here and nothing at all in the shader.

## Advances without a lookup

Measuring text asks for one advance per character, and a large note asks a million times a frame when it rewraps. A dictionary probe, a [[Glyph]] read and a metrics copy per character were two thirds of an optimized measure, so `BuildCharIndex` also lays the advances of the first 256 characters out flat, one row per face, and `TableAdvance` is a single array read. A character the font lacks takes space's advance there, the same fallback the measurer applies above 256 through the dictionary. See `ClaudeMemory/Decisions/large-note-measure-cost.md`.

`Effective` is the honesty step. A family with no italic file still gets asked for italic by any run whose author toggled it, and the answer is regular — the style collapses in the metrics and in the cell together, so a run never measures against one face and draws in another. It does not cascade: bold-italic on a family that has bold but no bold-italic draws regular, not bold, because claiming a weight the family does not carry for that style is the worse failure. See `ClaudeMemory/Decisions/bold-italic-face.md`.

## Methods / Functions

### Public
#### Deserialize
get meta data file location from name
open binary file to read
	read glyph count
	foreach glyph
		read char
	foreach glyph
		read xMin/yMin/xMax/yMax/width/height/right-left-top side bearings

## Helpers

```C#
public FontStyle Effective(FontStyle style) => style switch
{
	FontStyle.Bold when hasBold => FontStyle.Bold,
	FontStyle.Italic when hasItalic => FontStyle.Italic,
	FontStyle.BoldItalic when hasBoldItalic => FontStyle.BoldItalic,
	_ => FontStyle.Regular
};

public int StyleBlock(FontStyle style) => style switch
{
	FontStyle.Bold when hasBold => 1,
	FontStyle.Italic when hasItalic => hasBold ? 2 : 1,
	FontStyle.BoldItalic when hasBoldItalic => 1 + (hasBold ? 1 : 0) + (hasItalic ? 1 : 0),
	_ => 0
};

public int CellIndex(int charIndex, FontStyle style) => StyleBlock(style) * glyphCount + charIndex;

public void BuildCharIndex()
{
	charIndex = new Dictionary<char, int>(glyphCount, charHash);
	for (int i = 0; i < glyphCount; i++)
		charIndex.TryAdd(chars[i], i);
	advances = new float[4 * AdvanceTableSize];
	for each char c below AdvanceTableSize
		glyph = GetGlyph(c), or GetGlyph(' ') when missing
		if no glyph: leave 0
		for each face f
			advances[f * AdvanceTableSize + c] = glyph's advanceWidth in face f
}

public float TableAdvance(char character, FontStyle face) => advances[(int)face * AdvanceTableSize + character];

public Glyph GetGlyph(char character)
{
	if (charIndex.TryGetValue(character, out int index))
	{
		return glyphs[index];
	}
	return null;
}

public (Glyph, int) GetGlyphAndIndex(char character)
{
	if (charIndex.TryGetValue(character, out int index))
	{
		return (glyphs[index], index);
	}
	return (null, -1);
}

public int GetIndexOfChar(char character)
{
	if (charIndex.TryGetValue(character, out int index))
	{
		return index;
	}
	return -1;
}
```