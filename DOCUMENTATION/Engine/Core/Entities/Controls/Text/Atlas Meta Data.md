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

public int styleCount => 1 + (hasBold ? 1 : 0) + (hasItalic ? 1 : 0) + (hasBoldItalic ? 1 : 0);
public int cellCount => glyphCount * styleCount;
```

## Faces and the atlas grid

The atlas holds one cell per (character, face), laid out as consecutive per-face blocks in a square grid of `ceil(sqrt(cellCount))` cells to a side — regular first, then whichever of bold, italic and bold-italic the family had a file for. Nothing above this class knows how many faces there are: `CellIndex` turns a character index and a [[Glyph]] style into a flat cell number, and the caller divides it into the grid. Adding a face is therefore arithmetic here and nothing at all in the shader.

`Effective` is the honesty step. A family with no italic file still gets asked for italic by any run whose author toggled it, and the answer is regular — the style collapses in the metrics and in the cell together, so a run never measures against one face and draws in another. It does not cascade: bold-italic on a family that has bold but no bold-italic draws regular, not bold, because claiming a weight the family does not carry for that style is the worse failure.

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

public Glyph GetGlyph(char character)
{
	int index = Array.IndexOf(chars, character);
	if (index >= 0 && index < glyphs.Length)
	{
		return glyphs[index];
	}
	return null;
}

public (Glyph, int) GetGlyphAndIndex(char character)
{
	int index = Array.IndexOf(chars, character);
	if (index >= 0 && index < glyphs.Length)
	{
		return (glyphs[index], index);
	}
	return (null, -1);
}

public int GetIndexOfChar(char character)
{
	int index = Array.IndexOf(chars, character);
	if (index >= 0 && index < glyphs.Length)
	{
		return index;
	}
	return -1;
}
```