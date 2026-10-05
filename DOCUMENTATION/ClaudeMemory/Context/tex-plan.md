# LaTeX editor (native) — agreed route, L1–L3 landed

**Agreed:** 2026-10-05 (user: native route, "as many packages as possible", L3 node list on the recommendation).
**Built:** L1, which merged the file/source editor with the TeX expander (user). L2 (CFF + Latin Modern) landed 2026-10-05. L3 (typesetter + lowering) landed 2026-10-05. L4 onward not started; nothing designed beyond the phase list.
**Decisions:** [../Decisions/latex-editor.md](../Decisions/latex-editor.md).
**Checklist form:** the LaTeX item in `DOCUMENTATION/Work in Progress List.md`.

Each phase lands and is verified before the next starts; each one re-runs CLAUDE.md §2 (analyze, plan, ask).

## Forks taken
| # | Question | Taken | Rejected |
|---|---|---|---|
| Route | how TeX is run | native: Aurora's typesetting, C# primitives for document commands and package environments | full port of tex.web (TRIP-verified, XeTeX-compatible, real `latex.ltx` and `.sty`) — costs T3–T5, ~25k lines of WEB, before anything shows |
| L-F1 | dependencies, packages | no NuGet, no external tools; `\usepackage` accepted, unknown packages logged and ignored; "as many as possible" starting amsmath, amssymb, graphicx, xcolor, hyperref (links), geometry, booktabs, enumitem, fancyhdr | — |
| L-F2 | macro level | level 2: real tokenizer + expander (catcodes, `\def` with parameter text, `\expandafter`, `\csname`, conditionals, registers, groups) | level 1: command table + template `\newcommand` — breaks on `\def\foo#1.#2`, `\expandafter`, `\newif`, `\makeatletter` |
| L3 | interpreter output | TeX-like node list (chars, glue, kerns, penalties, boxes), lowered to `<Document>` | emitting straight to `<Document>` — closes `\hbox`/`\setbox`/`\wd`, `\vskip`, `\lastskip`/`\unskip`/`\lastbox` |
| L-F3 | editor shape | Overleaf split view: source + read-only paged preview | — |
| L-F4 | preview | the `<Document>` tree; Aurora's layout gains glue gaps, Knuth-Plass mode, hyphenation, footnotes, running heads, float placement (L7) | — |
| L-F5 | fonts | add CFF (Type 2 charstrings) support; the atlas baker already takes cubic `Bezier` contours | — |
| L-F6 | footnotes | endnotes in v1, real footnotes in L7 | — |
| L1 | source editor | `.tex` reuses `DocumentEditorControl`, one Code block per line | a new editor control |
| L2-A | where the `.otf` sources live | gitignored `_Build/FontSources/`, probed after the system font folders | copying into the per-user Windows font folder; an absolute path in the import XML; committing the `.otf` files |
| L2-B | which hosts import Latin Modern | Thorium only (`ThoriumFonts.imports.xml`) | engine imports — would bake and commit three copies (Thorium, Carbon, AuroraEditor) |
| L2-C | charset | `Latex`, 348 chars incl. ligatures, accents, quotes, dashes | the existing `Latin` charset |
| L2-D | faces | Roman 10 ×4, Mono 10, LM Math with Roman italic/bold | optical sizes 5–17 and Sans left out |
| L2-E | ink box | exact cubic extrema | control-point hull (looser) |
| L3-F1 | where LaTeX commands that are plain macros live | a TeX prelude, a C# string in `TexFormat`, run before the source | every command as a C# primitive (more code, loses LaTeX's own structure); a data file `Data/Tex/aurora.ltx` (path plumbing into three hosts, not XML) |
| L3-F2 | font of a formula | a math run's `FontName` picks the math face, absent = `math` | keep Cambria (formulas would not match the body) |
| L3-F3 | package scope | core + geometry (paper/margin keys), xcolor (`\textcolor`, `\color`, named colours with tints, HTML/rgb/RGB/gray), hyperref `\href`/`\url` as underlined/mono text only | core only |
| L3-F4 | footnote marker | a math run `{}^{n}` (`<Run>` has no superscript); notes become endnotes under a "Notes" Heading1 | `[n]` text |
| L3-F5 | default paper | letterpaper as LaTeX; a4paper by class option or geometry | A4 (Aurora's default) |
| L3-A | Greek in LM Math italic | `MathLayout.Present` uses Regular when the requested face's ink box is empty and Regular's is not; Greek upright | LM Math's italic Greek at U+1D6FC+ (outside the 16-bit char atlas — large change); reverting L3-F2 |
| L3-B | the line before a display formula is justified across the width | left to L7 | splitting the paragraph at display math in lowering; making justify treat a line ending at a display run as a last line (the general fix; notes likely share the bug — unverified) |

Cost accepted with the native route: support is bounded by what is reimplemented; a package not rewritten (e.g. `mathtools` patching amsmath internals) does not work.

## Phases

### L1 — file, source editor, expander — DONE 2026-10-05
- `TexSourceFormat`, `.tex` in `RichTextDocument.extensions`, byte-identical save (`sourceNewline`, `sourceBom`), pageless, `Mode.Latex` colouring
- `ArctisAurora.Core.Tex`: `TexExpander`, `TexReader`, `TexToken`, `TexCatcode`, `TexError` — expansion only, nothing in the UI calls it
- Test- and golden-verified; **NOT GUI-verified**

### L2 — CFF + Latin Modern — DONE 2026-10-05
- `CffOutlines` (CFF reader + Type 2 charstring interpreter) feeds `AuroraFont.GetCffGlyph`; forks L2-A to L2-E above
- Latin Modern (Roman 10 ×4, Mono 10, LM Math) ships as a baked Thorium atlas only; the `.otf` files sit in gitignored `_Build/FontSources/`, never committed or needed on users' machines (user, 2026-10-05)
- Also fixed (user approved): the bake's empty-glyph check broke any glyph exactly 1 em tall or wide (LM Math ∑, √); "empty" now means no contours
- Test- and golden-verified (`Fonts.CffCharstring`, `LatinModern.Baked`, `LatinModern.Draws`); **NOT GUI-verified**
- Details: [[latex-editor]]

### L3 — interpreter → node list → `<Document>` — DONE 2026-10-05
- `TexTypesetter` (expander tokens → node list), `TexFormat.Prelude` (LaTeX commands as TeX macros), `TexLowering.Compile(source, out errors)` → `<Document>`; nothing in the UI calls it until L4
- The expander's `em`/`ex` now come from the font (`quad`/`xHeight`, `TexAtlasMetrics`); `\stepcounter` and `\newcounter{x}[within]` resets added
- Forks L3-F1 to L3-F5 above; Fork A (L3-A) and Fork B (L3-B) came after the first golden
- Test- and golden-verified (`Tex` suite 23/23, `Tex.Preview`); **NOT GUI-verified**
- Details: [[latex-editor]]

### L4 — split view
- Debounced recompile, error list, click-to-source

### L5 — floats, `tabular`, `\label`/`\ref`, `.bib`

### L6 — amsmath natively
- `align` two-pass, `gather`, `cases`, matrices — shared with notes math M5

### L7 — Aurora layout adaptations
- Glue gaps, Knuth-Plass mode, hyphenation, footnotes, running heads, float placement

### L8 — in-house PDF export

## Left out overall
- TikZ/PGF, beamer, BibLaTeX/biber, catcode-level arbitrary TeX, `\output`, multi-column, `\input`/`\include` (for now)

Related: [[latex-editor]], [[math-in-notes]]
