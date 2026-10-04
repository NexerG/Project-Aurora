# Decision — math in notes is laid out natively on a Cambria Math atlas

**Date:** 2026-10-01
**Scope:** `ArctisAurora.Core.Filing` — `MathConstants`; `ArctisAurora.Core.Filing.Serialization` — `AssetImporter` (`FaceOffset`, `ImportFont`, `ReadFace`), `FontImport.face`, `FontImportStamp.face`; `EngineFonts.imports.xml`; `ArctisAurora.Core.UI` — `MathParser`, `MathSymbols`, `MathLayout`, `MathBox`, `MathGlyph`, `MathRule`, `MathClass`, `MathStyle`, `Run.math`/`display`, `StyleSpan.mathSource`/`mathDisplay`/`IsMath`/`IsObject`, `TextMeasurer.Run.math`/`depth`, `TextRunControl` (`MathBoxFor`, `Emit`), `DocumentControl.CopySelection`, `MarkdownFormat` (`MathAt`, `MathRun`); `ArctisAurora.Core.Registry.Assets.FontAsset.mathConstants`; `EngineAssets.assets.xml` (`math`)

**Status: LANDED** — M1 (the font), M2 (parser + layout), M3 (notes hold, draw, save and copy formulas) and M4 (insert and edit) landed; plan in [../Context/math-plan.md](../Context/math-plan.md). M4 scope: `FormulaPopup`, `MathEdit`, `DocumentControl` region `formulas` (`SelectedMath`, `StoredMath`, `SetMath`, `PlaceMath`, `RecordPlaced`, `RecordMath`, `RemovePlaced`, `MathAnchor`), `BlockControl.SetMath`, `TextRunControl` (`MathAt`, `MathBox`), `DocumentEditorControl` (`InsertFormula`, `EditFormula`), `TextBoxControl.onEdited`, `TextInputActions` (`Math.Insert`, `Math.InsertDisplay`, `Math.Edit`).

## Sheet cells in formulas (sheets S2c, 2026-10-04)
- A formula may hold `\sheet{Budget.sheet.xml#Data!B1}`: `TextRunControl.MathBoxFor` caches and parses `SheetLinks.ExpandMath(source)`, not the source. Number → `{value}`, text → `\text{value}`, range → `\text{#VALUE!}`, missing → `\text{#REF!}`.
- A changed cell value is a new cache key, so the cache never goes stale; `MathParser` is unchanged and still pure. Substitution only — nothing is evaluated. Rationale and gaps: [[sheets]] § S2c.
- `FormulaPopup.PasteLink` (Ctrl+Shift+V in the source box) inserts `\sheet{ref}` for the last copied sheet cells; a sheet rename rewrites the references in open notes and on disk (`SheetLinks.RenameMath`).

## M4 — inserting and editing (2026-10-02)
- **Ctrl+M** inserts an inline formula, **Ctrl+Shift+M** a display one (Thorium `InputMap`; the two-modifier bind declared first). Right-click: Insert formula, Insert display formula, Edit formula.
- **A click on a formula selects it** (`TextRunControl.OnPointerPress` falls back to `MathAt`, then the picture path's `PicturePressed`); **double-click or Enter** on a selected formula opens the editor. `Text.NewBlock` tries `EditFormula` before `SplitBlock`.
- **The editor is a `TextBoxControl` in a `ContextMenuContent`, opened under the formula's drawn box** (`MathAnchor`; the caret slot while a just-placed formula is not laid out). Enter, Esc, typing and field undo reach it through the existing `Box()` routing — no new input code.
- **Live preview is in the note itself (F13):** `TextBoxControl.onEdited` (from `Record` and `Restore`) rewrites the span through `SetMath` with no undo record; the paragraph reflows as you type.
- **One undo record per commit.** Insert places an empty formula raw (`InsertBetween`), and commit pushes an `InsertRangeEdit` holding the final source, labelled "Insert formula"; edit pushes `MathEdit(before, after)`, labelled "Edit formula"; unchanged source pushes nothing. Rejected: holding an `EditScope` open across frames, or recording the insert and the edit as two steps.
- **Esc reverts; an empty new formula, committed or cancelled, is removed unrecorded. Click-away commits** (`onBlur`, after `DismissUnlessInside` closed the panel; no refocus, so the press keeps its target). A `done` flag stops the blur that follows Enter/Esc's refocus from finishing twice.
- **Refused:** read-only notes, `.txt` notes, code blocks and rule blocks — logged at Info, as `PasteImage` refuses.
- A formula keeps inline/display for life; edit does not switch it.

## M3 — formulas in notes (2026-10-02)
- A formula is a `<Run Math="…" Display="true"/>`: one U+FFFC in the block, a `StyleSpan` with `mathSource`/`mathDisplay`. Note XML needs no code — `DocumentXml` reads and writes `Run`'s scalars by reflection.
- `StyleSpan.IsObject` (picture, formula or — since sheets S2b3 — sheet link) replaces `IsPicture` where the rule is about an atomic character: `InsertText`/`TextSpanBeside` (typing beside it lands in a text span), `DropEmptySpans`, `SameStyle` (never merged), `Runs()` (no text). Picture-only sites (`SetPicture`, handles, wrap, textures, `PictureAt`) stay on `IsPicture`.
- Measuring: `TextMeasurer.Run` gains `math` + `depth`; `Flatten` gives the character the box's width, `max(box height, line ascent)` and `max(box depth, line descent)`, flagged `picture` so it cannot hang and breaks either side.
- **Display formula = the full column as its advance, drawn centred inside it (F5′).** That alone puts it on a line of its own — text before it breaks ahead of it, text after wraps — with no break code in the measurer. Block alignment is ignored for it; Obsidian centres display math regardless. Rejected: honouring `BlockControl.alignment` (display math would default to the left) and the original forced-break + `TextLine.left`.
- **Laid-out boxes live in a static cache keyed by (source, display) in `TextRunControl` (F11)**, not on the span: `StyleSpan` is a struct copied through snapshots, and caching on it would mean writing spans back during measure. Since sheets S2c the key's source is the expanded source: `\sheet{…}` is replaced by the cell's value before parsing (see below).
- Drawing: each `MathGlyph` through `WriteGlyph` (whose `size` became `float`) from the `math` font asset; each `MathRule` through `WriteRect` with its top rounded to a pixel and a 1 px floor — the underline's precedent. Sub-pixel rules straddled two rows: a 0.85 px rule drew as a dark 2-row bar beside a faint 1.04 px one.
- A formula that fails to parse draws its source in `PaletteRole.Danger`.
- `FontAsset.Load` reads `{font}.math.xml` into `mathConstants` when it exists; `EngineAssets.assets.xml` names the asset `math` → `Fonts/cambria`. The Thorium bake was copied byte-for-byte into AuroraEditor and Carbon — host bakes are identical (same `arial` hashes).
- Plain-text copy writes a formula as `$src$` / `$$src$$`; our own paste keeps the span.
- Markdown: `$…$` by Obsidian's rules (no space inside either fence, no digit after the closer, `\$` escapes), `$$…$$` inline is display, a `$$` line opens a display block closed by the next `$$` line (flushed at end of file). A Text block holding only a display formula writes as `$$` / source / `$$`. `$` joined `escapable` and the writer's escape set; the writer's re-read check means a literal `$` is escaped only when the plain form would read back as math.
- Delimiters may stop short of the content by TeX's rule (0.901 / 0.5 em shortfall); a deep denominator sticks out below the paren, as in TeX.

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
- A `Face` index past the collection's face count is not checked; it reads garbage.
- The asset folder is named after the file stem (`cambria`), so importing face 0 of the same `.ttc` would collide.
- `AuroraEngine/Data/Fonts` has no `cambria` — that folder's bakes are already stale (WIP item); no host runs from it.
- M4 is test-verified only: popup placement, the live reflow, and click-away were never looked at in the GUI.
- If the app loses focus, `ContextMenus.Tick` closes the popup without a blur; an inserted formula would stay empty (zero-width) in the note. Unverified either way.
- A popup that does not fit in the window goes to its own OS window (`HostInWindow`); keyboard reaching the box there is unverified.
- An empty formula is zero-width while its source is being typed; there is no placeholder.
- The source box is one line; a pasted newline becomes a space.
- Underline, strikethrough, gradients and effects do not reach a formula; bold/italic on its run is ignored.
- A display formula beside a float is centred in the column, not in the slot the float leaves; a formula wider than the column overflows.
- `\$` in an existing `.md` note now reads as `$`, not `\$` — CommonMark's reading, but a change for notes written before.
- Script-size glyphs at a 16 px body (11.7 px) are soft; inherent to the size.
- Not in M2: `\limits`/`\nolimits`, `\operatorname`, environments (`\begin` fails), macros, `\color`, `\mathcal`, MATH kerning/italics tables, accent skew over italic letters.

Related: [[bold-italic-face]], [[asset-manifest-and-import]], [[note-images]], [[text-layout-one-measurer]]
