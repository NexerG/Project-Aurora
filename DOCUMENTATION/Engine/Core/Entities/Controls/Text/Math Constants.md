---
date: 2026-10-01
Status: Current
tags:
  - d_text
  - d_Font
cssclasses:
  - Aurora.css
Linker:
  - "[[Arctis Aurora]]"
System:
Class:
  - "[[Math Constants]]"
Parent Class:
Interfaces:
Used by:
  - "[[Aurora Font]]"
Type:
  - Public
Attributes:
  - A_XSDType
Namespace: ArctisAurora.Core.Filing
SourceFile: AuroraEngine/Core/Filing/MathConstants.cs
VerifiedAgainst: 2026-10-01
---
## Description

The layout constants of an OpenType math font, read once at import from the font's `MATH` table and kept beside the baked atlas as `{font}.math.xml`. Math layout reads them instead of the font: every length is stored as a fraction of the em, so a formula scales with its run's font size, and the three percentages (script scale-downs and the radical degree's raise) are kept as the font states them.

> Only the first step of math in notes exists. The font is baked (Cambria Math, face 1 of `cambria.ttc`, with Cambria Italic for variables); the parser, layout and drawing are planned in `ClaudeMemory/Context/math-plan.md`.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `Read(string fontPath, int face)` | static | Read a face's MathConstants subtable; null when the face has no `MATH` table. |
| `Save(string path)` | public | Write every constant as an attribute of one `<MathConstants>` element. |
| `Load(string path)` | static | Read a `.math.xml` back. |

## Fields & Properties

```C#
// percentages
float scriptPercentScaleDown, scriptScriptPercentScaleDown, radicalDegreeBottomRaisePercent;

// lengths, in em — the rest of the MathConstants subtable in spec order
float delimitedSubFormulaMinHeight, displayOperatorMinHeight, mathLeading, axisHeight, …, radicalKernAfterDegree;
```

## Methods

### `Read` *(static)*
	seek to the face's table directory (`AssetImporter.FaceOffset` — 0 for a plain font, the face's entry for a `.ttc`)
	scan the directory for `MATH` and `head`
	if no `MATH`
		return null
	unitsPerEm ← `head` + 18
	constants ← `MATH` + its MathConstants offset
	read the two script percentages
	read the two unsigned heights ÷ unitsPerEm
	for each of the 51 MathValueRecords
		value ÷ unitsPerEm, skip the device-table offset
	read the radical degree percentage

### `Save`
	for each scalar `[A_XSDElementProperty]` member
		add an attribute, invariant culture
	save the element

### `Load` *(static)*
	apply the file's attributes onto a new instance through `XmlReflection.ApplyAttributes`

## Related
- [[Aurora Font]] — the import that writes the file after baking the atlas
- [[Atlas Meta Data]] — the atlas the constants sit beside
