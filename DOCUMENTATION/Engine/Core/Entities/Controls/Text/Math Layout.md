---
date: 2026-10-01
Status: Current
tags:
  - d_text
cssclasses:
  - Aurora.css
Linker:
  - "[[Arctis Aurora]]"
System:
Class:
  - "[[Math Layout]]"
Parent Class:
Interfaces:
Used by:
Type:
  - Public
  - Static
Attributes:
Namespace: ArctisAurora.Core.UI
SourceFile: AuroraEngine/Core/UI/MathLayout.cs
VerifiedAgainst: 2026-10-01
---
## Description

Places a parsed formula from [[Math Parser]] as glyphs and rules, the way TeX's math typesetting does, using the font's own constants from [[Math Constants]] and glyph sizes from the Cambria Math atlas ([[Atlas Meta Data]]). The result is a `MathBox`: its width, height above the baseline and depth below it, a list of glyphs and a list of filled rules. Everything is in em at font size 1 with y measured up from the baseline, so whoever draws it multiplies by the text's font size.

Inline formulas use text style and display formulas display style; scripts shrink to script and script-script style by the font's own percentages. Big operators grow in display style and take their limits above and below; in text style the limits move to the side. Tall delimiters grow by scaling until they are 2.4 times their natural height and are then built from the Unicode bracket pieces, and a tall square root switches from a scaled `√` to the `⎷` hook with a drawn stem.

> Nothing draws a `MathBox` yet. See `ClaudeMemory/Context/math-plan.md`.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `Layout(MathNode root, bool display, AtlasMetaData atlas, MathConstants constants)` | static | The formula as a `MathBox` in em; a `MathError` lays out its source as plain text with `error` set. |

## Fields & Properties

```C#
// MathBox
float width, height, depth;
List<MathGlyph> glyphs;   // char, face, x, y, scale
List<MathRule> rules;     // x, top y, width, height
bool error;
```

## Methods

### `Layout` *(static)*
	if the root is a MathError
		lay out its source as regular text and flag it
	otherwise lay out the root in display or text style

### Laying out a list
	give every atom its class
		a binary operator with nothing usable before it becomes ordinary
		a binary operator right before a relation, closer or punctuation becomes ordinary
	for each atom
		add TeX's space between the previous class and this one, skipping the medium and thick ones in script styles
		place the atom on the baseline
		after a plain symbol, add its italic correction

### Laying out scripts
	if the nucleus is a big operator or operator name that takes limits, in display style
		centre the limits above and below the operator by the font's limit gaps
	otherwise
		raise the superscript and lower the subscript by the font's shifts
		keep the gap between them at least the font's minimum
		put the superscript after the nucleus's italic correction
		add the font's space after the scripts

### Laying out a fraction
	numerator and denominator one style smaller, the denominator cramped
	push them apart until each clears the rule by the font's gap
	centre the rule on the math axis
	pad either side by the null delimiter space

### Laying out a square root
	if a `√` up to twice its size covers the body
		scale it and line its top up with the bar
	otherwise
		place the `⎷` hook at the bottom and draw the stem up to the bar
	draw the bar over the body
	place the degree on the sign's left, raised by the font's percentage

### Laying out `\left…\right`
	size the delimiters to cover the body around the axis, as TeX does
	for each delimiter
		natural size if it is tall enough
		scaled up to 2.4 times
		beyond that, top piece, bottom piece, a middle piece for braces, and overlapping extenders between them
		vertical bars become rules

## Related
- [[Math Parser]] — produces the nodes
- [[Math Constants]] — the font's layout constants
- [[Atlas Meta Data]] — glyph sizes
