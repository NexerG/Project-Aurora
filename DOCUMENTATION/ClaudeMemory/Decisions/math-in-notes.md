# Decision — math in notes is laid out natively on a Cambria Math atlas

**Date:** 2026-10-01
**Scope:** `ArctisAurora.Core.Filing` — `MathConstants`; `ArctisAurora.Core.Filing.Serialization` — `AssetImporter` (`FaceOffset`, `ImportFont`, `ReadFace`), `FontImport.face`, `FontImportStamp.face`; `EngineFonts.imports.xml`; `ArctisAurora.Core.UI` — `MathParser`, `MathSymbols`, `MathLayout`, `MathBox`, `MathGlyph`, `MathRule`, `MathClass`, `MathStyle`

**Status: PARTIAL** — M1 (the font) and M2 (parser + layout, CPU only) landed; model, drawing and editing are planned in [../Context/math-plan.md](../Context/math-plan.md).

## M2 — parser and layout (2026-10-01)
- `MathParser.Parse(string) → MathNode` (`ArctisAurora.Core.UI`): recursive descent; nodes `MathList`, `MathSymbol`, `MathText`, `MathScripts`, `MathFraction`, `MathRadical`, `MathDelimited`, `MathAccent`, `MathSpace`, `MathError`. Any failure makes the **whole formula** a `MathError` carrying its source (F7) — no partial render, never throws.
- `MathSymbols`: command → (char, `MathClass`), big operators, operator names, accents, `\mathbb` letters, delimiter names, and the reverse class map for characters typed straight in. Written by hand, not handed off (F8): a spec exact enough for a mechanic was the table itself.
- `MathLayout.Layout(node, display, atlas, constants) → MathBox`: em at font size 1, y up from the baseline; `MathGlyph` (char, face, x, y, scale) and `MathRule` (x, top y, width, height). TeX Appendix G driven by `MathConstants`; D/T/S/SS + cramped; TeX's inter-atom table in mu, Bin demotion.
- Glyph height/depth come from the atlas: ink height `glyphHeight` split by `yMax/(yMax−yMin)`. Italic correction is `max(0, leftSideOffset + glyphWidth − advanceWidth)` for every glyph — no MATH italics table.
- `'` is a plain `′` glyph after the atom, not a superscript — Cambria's U+2032 is already raised.
- `MathText` carries a class and `limits`, so operator names (`\lim`, `\max`) take limits in display style.
- Tall `\sqrt` (past 2× natural) is `⎷` + a stem rule; the sign's width is the hook's ink right edge, so the overbar starts flush with the stem.
- Tall delimiters past 2.4× natural assemble from U+239B–U+23AD; when the top and bottom pieces already exceed the target they overlap and no extender is placed. `|`/`‖` grow as rules.

## Measured, M2
| formula | width | height | depth |
|---|---|---|---|
| `x` | 0.462 | 0.467 | 0 |
| `x^2` (2 at y 0.37, scale 0.73) | 0.908 | 0.858 | 0 |
| `\frac{a}{b}` T / D | 0.624 / 0.766 | 0.933 / 1.232 | 0.509 / 0.677 |
| `\sum_{i=1}^{n}` T / D (∑ scale 1 / 1.34) | 1.899 / 1.149 | 0.862 / 1.487 | 0.325 / 0.930 |
| `a+b=c` | 3.975 | 0.698 | 0.028 |

`a+b=c` is wide because Cambria Math's operators advance ~0.7 em, not because of spacing.

## What changed
- `<FontImport Face="n">` picks a face out of a `.ttc`; `AssetImporter.FaceOffset` reads the collection header and `ReadFace` starts its table directory there. Table offsets in a TTC are absolute, so nothing past the header moved.
- `Face` is in the stamp and in `Matches`; a stamp without it reads 0, so existing bakes stay current.
- After a bake, a face with a `MATH` table gets `{name}.math.xml` — `MathConstants`, every MathConstants-subtable field, lengths divided by `unitsPerEm`, the three percentages as read (73, 60, 65 for Cambria Math).
- `<Charset Name="Math">`: ASCII, Greek (incl. ϑ ϕ ϖ ϱ ϵ), letterlike ℂℕℙℚℝℤℏℓ℘ℑℜℵ, operators, relations, arrows, ⟨⟩⌈⌉⌊⌋‖, delimiter pieces U+239B–U+23AE, ⌠⌡⎷, spacing accents and U+20D7 — 293 chars.
- `cambria.ttc` face 1 baked with `cambriai.ttf` / `cambriab.ttf` as Italic / Bold → asset folder `Fonts/cambria`.

## Why these choices

**Cambria Math from the OS, not a bundled font.**
Face 1 of `cambria.ttc` is TrueType (`glyf`/`loca`) with a `MATH` table and a format-4 BMP cmap, so the existing outline reader takes it after one header read. Latin Modern Math and most STIX builds are CFF, which would need a Type 2 charstring parser. It is resolved from the system font folder like Arial, so nothing is redistributed; the cost is Windows-only math, the same constraint every engine font already has.

**Variables come from Cambria Italic, not from math-italic code points.**
Math italic lives at U+1D434…, outside the BMP; the atlas, `AtlasMetaData.chars` and `GetGlyphAndIndex` are `char`-keyed. Declaring `cambriai.ttf` as the Italic face reuses the four-face bake. A face missing a character bakes `.notdef`, so the bold and italic blocks hold boxes for every operator — never drawn, because only letters are styled.

**MATH constants live in their own file.**
Adding them to `.agd` means a new `AtlasMetaData` field and an `importerVersion` bump, which re-bakes every font in every host. A sibling XML is read only by math layout and written only for a face that has the table.

**Stretchy glyphs will be Unicode pieces, not MATH variants.**
MATH size variants and assembly parts are glyph ids with no code point; the atlas has no cell for them. Pieces + scaling is below KaTeX for very tall delimiters and is the accepted v1 quality.

## Known gaps
- Bake cost: 293 glyphs × 3 faces, ~7 min of a first Thorium `--test` launch; it must not be killed (`never-kill-thorium-mid-import`).
- No `FontAsset` manifest entry for `Fonts/cambria` yet — M3 adds it when something draws with it.
- A `Face` index past the collection's face count is not checked; it reads garbage.
- Baked in Thorium only so far; AuroraEditor and Carbon bake on their next Debug launch.
- The asset folder is named after the file stem (`cambria`), so importing face 0 of the same `.ttc` would collide.
- Layout is test-verified on box geometry only; nothing is drawn until M3, so how the scaled `√`, the `⎷` + stem join, overlapping paren pieces and accent placement look is unchecked.
- `TextRunControl.WriteGlyph` takes `int size`; scripts need a float size on that path (M3).
- Not in M2: `\limits`/`\nolimits`, `\operatorname`, environments (`\begin` fails), macros, `\color`, `\mathcal`, MATH kerning/italics tables, accent skew over italic letters.

Related: [[bold-italic-face]], [[asset-manifest-and-import]], [[note-images]], [[text-layout-one-measurer]]
