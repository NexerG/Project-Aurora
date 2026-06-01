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
```

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