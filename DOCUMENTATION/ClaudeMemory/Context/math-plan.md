# Math in notes (LaTeX math mode) — agreed plan, M1–M5 landed

**Agreed:** 2026-10-01 (user: inline + display math in notes, native route, recommended forks).
**Built:** M1 (math font), M2 (parser + layout), M3 (model, drawing, persistence), M4 (editing), M5 (amsmath environments, `\tag`, ams commands — landed 2026-10-06 as LaTeX L6; parser and layout are shared).
**Decisions:** [../Decisions/math-in-notes.md](../Decisions/math-in-notes.md).
**Checklist form:** the math item in `DOCUMENTATION/Work in Progress List.md`.

Each phase lands and is verified before the next starts; each one re-runs CLAUDE.md §2 (analyze, plan, ask).

## Scope
- `$…$` inline and `$$…$$` display, TeX **math mode** only — no documents, packages or macros.
- A formula is an atomic object span (one U+FFFC), like a picture; its TeX source is the persisted data.

## Forks taken
| # | Question | Taken | Rejected |
|---|---|---|---|
| F1 | math font | Cambria Math, face 1 of the OS `cambria.ttc` — TrueType `glyf` + `MATH`; Windows-only like Arial | bundling STIX Two Math (TTF build unverified; ships a font) |
| F2 | italic variables | `cambriai.ttf` as the import's Italic face | SMP math-italic (U+1D434…) — `char` → `int` across the atlas |
| F3 | stretchy glyphs | Unicode pieces (⎛⎜⎝ ⌠⎮⌡ ⎷) + uniform scaling | atlas cells keyed by glyph id from MATH variants |
| F4 | MATH constants storage | `{name}.math.xml` beside the `.agd`, written at import | fields on `AtlasMetaData` (bumps `importerVersion`, re-bakes every font) |
| F5 | display centring | measurer gives the display formula its own line, centred via `TextLine.left` | general paragraph alignment |
| F6 | editing | atomic formula + popup source editor, one undo record per commit | Obsidian-style source reveal when the caret enters |

**F5 needs a second look at M3:** paragraph alignment landed in the tree the same day (`markdown-blocks-and-alignment`), so the rejected alternative now exists.

## Phases

### M1 — math font — DONE 2026-10-01
- `FontImport.face` / `FontImportStamp.face` (`Face` attr), `AssetImporter.FaceOffset` for `.ttc`
- `MathConstants` — MathConstants subtable, lengths in em, percentages as read; `Read`/`Save`/`Load`
- `<Charset Name="Math">` (293 chars) + `<FontImport Source="cambria.ttc" Face="1" Italic="cambriai.ttf" Bold="cambriab.ttf" Charset="Math"/>`
- Test `Fonts.MathFontBaked`

### M2 — parser and layout (CPU only) — DONE 2026-10-01
Forks taken at M2: **F7** an error makes the whole formula its source text, flagged (rejected: per-command error nodes); **F8** `MathSymbols` written by hand (rejected: aurora-mechanic transcription — the spec was the table). Tests: suite `Math`, 10 tests.

- `MathParser`: `^ _ {}`, `\frac \dfrac \tfrac \sqrt[n]`, `\sum \prod \int \oint \lim` (limits in D, scripts in T), `\left \right`, `\text \mathrm \mathbf \mathbb` (BMP only), `\hat \bar \vec \dot`, `\, \; \quad \qquad`, Greek and symbols. Unknown command / unbalanced brace → error atom, never throws.
- `MathLayout` → `MathBox` (width, height, depth, positioned glyphs, rules); TeX Appendix G with `MathConstants`, D/T/S/SS, atom classes, inter-atom spacing.
- Handoff: aurora-mechanic transcribes the command → code point + atom class table from a written spec.
- Verify: logic tests — `x^2` taller than `x`; `\frac` depth > 0 and axis-centred; subscript below baseline; `\sum` larger in D than T; garbage → error box.

### M3 — model, render, persistence — DONE 2026-10-02
Forks taken at M3: **F5′** display = full-column advance, drawn centred, block alignment ignored; **F10** built on the dirty tree (user, after the peers proved idle); **F11** static box cache keyed by (source, display). Tests: `TextInput.MathRoundTrip`, `MathLineBox`, `MathCopyText`, golden `MathDraws`. Details in the decision note; the list below is the original plan.

- `Run.Math`, `Run.Display`; `StyleSpan` math fields + cached `MathBox`, `IsMath`, `AsText` → `$…$`.
- `TextMeasurer.Run` math flag → `PenChar` with depth below baseline; display breaks before/after and centres (F5).
- `TextRunControl.Emit` draws the box via `WriteGlyph` (math `FontAsset`) and `WriteRect`.
- `DocumentXml` `<Run Math Display/>`; `MarkdownFormat` `$`/`$$` with Obsidian's dollar rules.
- Needs a `FontAsset` manifest entry for `Fonts/cambria` (not added in M1).
- Needs a float glyph size on the draw path — `WriteGlyph` takes `int size`, scripts are 73% / 60% of the run's size.
- First visual check of M2's layout: scaled `√`, `⎷` + stem join, overlapping paren pieces, accents.
- Verify: `.xml`/`.md` round-trip, a golden of inline + display math, existing goldens unchanged.

### M4 — editing — DONE 2026-10-02
Forks taken at M4: **F12** Ctrl+M inline / Ctrl+Shift+M display (rejected: Alt+=, Ctrl+Shift+E); **F13** live preview in the note itself (rejected: preview inside the popup); **F14** a click selects a formula like a picture, double-click / Enter opens (rejected: single click opens); **F15** three `Note.menu.xml` rows (rejected: toolbar button, keys only). Tests: `TextInput.MathInsertCommit`, `MathInsertCancel`, `MathEditUndo`, `MathLivePreview`, `MathEscReverts`, `MathClickSelects`, `MathDoubleClickOpens`. Details in the decision note; the list below is the original plan.

- Insert keybinds (checked against `InputMap.inputs.xml`); double-click / Enter on a selected formula opens a popup source editor (TextBox in a context-menu popup, as `ColorPickerControl` is hosted); live preview; Enter/click-away commits, Esc reverts; `MathEdit : IEditRecord`.
- Verify: console-driven input test — insert, type, commit, undo, redo.

### M5 — amsmath environments — DONE 2026-10-06 (landed as LaTeX L6)
- `MathArray` (matrix family, `cases`, `array`, aligned family, `gathered`, `multline`), `MathTag`, `MathFramed`; `\substack`, `\binom`, `\overset`/`\underset`, `\boxed`, `\operatorname(*)`; `MathLayout.Grid`, display `\tag` flush right
- Notes do not auto-number; only `\tag` shows. `.tex` documents number `equation` and the align family in `TexTypesetter`
- Test-verified (`--test=Math` 17/17); golden `TextInput.MathArrays`; **NOT GUI-verified**
- Details: [[math-in-notes]] § M5, [[latex-editor]] § L6, [tex-plan](tex-plan.md)

## Left out
Macros, `\color`, `\mathcal`/`\mathfrak` (SMP), automatic equation numbers (`\tag` only), auto-converting typed `$…$`, paste converting `$…$`, Editor/Carbon verification.
