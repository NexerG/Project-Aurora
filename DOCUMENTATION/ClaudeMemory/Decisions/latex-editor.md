# Decision — LaTeX editing is native: `.tex` opens in the note editor, a C# TeX expander sits under a future interpreter

**Date:** 2026-10-05
**Scope:** `ArctisAurora.Core.UI` — `TexSourceFormat`, `RichTextDocument`, `DocumentEditorControl`, `SyntaxTokenizer`, `SyntaxState`; `ArctisAurora.Core.Tex` — `TexExpander`, `TexReader`, `TexToken`, `TexCatcode`, `TexError`, `TexTypesetter`, `TexNode` and its subtypes, `TexParStyle`, `TexFormat`, `TexGlue`, `TexFont`, `TexStyle`, `ITexFontMetrics`; `ArctisAurora.Core.UI` — `TexLowering`, `TexAtlasMetrics`, `MathLayout`, `TextRunControl`; `ArctisAurora.Core.Filing` — `CffOutlines`, `AuroraFont`; `ArctisAurora.Core.Filing.Serialization` — `AssetImporter`

**Status: PARTIAL** — L1 (file + source editor + expander) L2 (CFF import + Latin Modern atlases) and L3 (typesetter + lowering to `<Document>`) landed 2026-10-05; L4–L8 not started. Plan: [../Context/tex-plan.md](../Context/tex-plan.md).

## What changed
- `.tex` files open in the note editor as LaTeX source: `TexSourceFormat.Read(text, name)` → `<Document>` of `<Block StylingType="Code" Language="latex" Wrap="true">`, one per line (one trailing `\r` stripped per line); `Write(document, newline)` is the inverse.
- `RichTextDocument`: `.tex` in `extensions`; fields `sourceNewline` (`"\r\n"` if the file contains CRLF, else `"\n"`) and `sourceBom`; private `TexSource(path)` reads bytes and detects a UTF-8 BOM; `Save` writes `.tex` with `new UTF8Encoding(sourceBom)` — a save is byte-identical.
- `DocumentEditorControl.LoadDocument`: `.tex` sets `document.layout.page = new PageLayout { mode = PageMode.Pageless }`; `content.plainText` is true for `.txt` and `.tex` (Markdown shortcuts off).
- `SyntaxTokenizer`: `Mode.Latex` for language `tex`/`latex` (`Latex`, `ControlEnd`, `units` set); `SyntaxState.TexMath` new member.
	- Control sequences → Keyword; `%` to end of line → Comment (not `\%`).
	- Math `$…$`, `$$…$$`, `\(…\)`, `\[…\]` → String; `TexMath` carries across lines and closes on the next `$`, `\)` or `\]`.
	- Numbers not preceded by a letter, plus a TeX unit (`pt pc in cm mm bp dd cc sp em ex mu fil fill filll`) → Number.
- Thorium `VaultBrowserControl` unchanged: `Accepts` reads `RichTextDocument.extensions`, `BuildTab` takes any note path.
- New namespace `ArctisAurora.Core.Tex`, pure CPU, nothing in the UI calls it yet:
	- `TexCatcode` — the 16 categories in `\catcode` order. `TexToken` (readonly struct) — `name` (control sequence) or `ch` + `cat`, `param` (1–9 body slot), `line`, `column`. `TexError` — line, column, message.
	- `TexReader` (internal) — TeX's mouth: lines split on `\n`, trailing spaces cut, `\r` end-of-line char appended; states NewLine/MidLine/SkipBlanks; a control word skips spaces after it; a blank line is `\par`; `%` drops the rest of the line; categories are read live, so `\catcode` takes effect mid-line.
	- `TexExpander(string source)`, `errors`, `Next(out TexToken)` — returns the next unexpandable token for a typesetter (characters, `{`/`}` after their group is opened/closed, `\relax`, `\par`).
- Expander coverage:
	- Groups and save stack with TeX's global/local level rule (a global change survives the group's restore); `\global`, `\long`; `\begingroup`/`\endgroup`.
	- `\def \gdef \edef \xdef` with delimited parameters and brace stripping; `\let`, `\futurelet`; `\expandafter`, `\noexpand`, `\csname`/`\endcsname`, `\string`, `\the`.
	- `\if \ifx \ifcat \ifnum \ifdim \ifodd \ifcase \iftrue \iffalse \ifdefined \ifcsname \else \or \fi`.
	- Count/dimen/skip/toks registers, `\chardef \countdef \dimendef \skipdef \toksdef`, `\advance \multiply \divide`, `\catcode`.
	- Numbers by tex.web's scan_int (`'` octal, `"` hex, `` ` `` char code); dimensions by tex.web's scan_dimen arithmetic in scaled points (round_decimals, xn_over_d, units `pt in pc cm mm bp dd cc sp true`, internal quantities as units, `fil`/`fill`/`filll` in glue); `\the` prints via print_scaled. `em` = 10pt and `ex` = 4.30554pt (cmr10), fixed unless `quad`/`xHeight` are set (L3).
	- LaTeX level natively: `\newcommand`/`\renewcommand`/`\providecommand` (star = not long, `[n][default]`), `\newenvironment`/`\renewenvironment`, `\begin`/`\end` (an environment group; mismatched `\end` reported LaTeX-style), `\makeatletter`/`\makeatother`, `\@ifnextchar`, `\@ifstar`, `\newif`, `\newcounter` (global, registers from 256; `[within]` resets x, cascading, when within steps — L3), `\setcounter`, `\addtocounter`, `\value`.
- Errors are collected, never thrown. Recovery is TeX-style: undefined cs skipped, runaway argument stops at `\par` for non-long, a missing `}` reported at end of file, open groups/conditionals reported at end. 1,000,000 expansion steps or input-stack depth 10,000 halts the run with an error.
- Tests: engine suite `Tex.tests.xml` (`TexTests`) — `Tex.SourceRoundTrip`, `Tex.Catcodes`, `Tex.DelimitedDef`, `Tex.ExpandAfter`, `Tex.Edef`, `Tex.Groups`, `Tex.Conditionals`, `Tex.Registers`, `Tex.NewCommand`, `Tex.NewEnvironment`, `Tex.Errors`; `Syntax.Latex` in the Syntax suite; Thorium TextInput suite `Tex.SourceEnterPaste`, `Tex.SourceColours` (golden `Tex.SourceColours.Source.png`).
- L2 — CFF font import, Latin Modern atlases (2026-10-05):
	- New `CffOutlines` (internal static): `Read(BinaryReader, AuroraFont.TableEntry cff)` → `(charStrings, global, local)` INDEXes (header, Name/Top DICT/String/Global Subr INDEXes, CharStrings via Top DICT op 17, Private DICT op 18 → local Subrs op 19); `Interpret(charstring, global, local)` → `List<Bezier>`; `Bounds(List<Bezier>)` → exact ink box including cubic extrema.
	- `Interpret` is Type 2: r/h/v moveto and lineto, rrcurveto, hh/vv/hv/vhcurveto, rcurveline, rlinecurve, flex/hflex/hflex1/flex1, callsubr/callgsubr (bias 107/1131/32768, depth 10), return, endchar; stems, hintmask and cntrmask skipped; width operand skipped; 16.16 fixed numbers. Cubic controls are `Bezier.Point` with `isCubicControl = true`; the closing point repeating the start is dropped.
	- `AuroraFont.ReadFaceGlyphs`: a `CFF ` table goes to new private `GetCffGlyph(glyphIndex, cffData, unitsPerEm)`; `loca`/`glyf` are read only for TrueType. New private `Normalise(Glyph, xMin, yMin, xMax, yMax)` (unit-box normalisation, `BuildEdges`, `MTSDFGen.ColorEdges`) is shared by `GetGlyphOutline` and `GetCffGlyph`.
	- Empty-glyph rule: `glyphHeight = lineHeight` and `glyphWidth = advanceWidth` now apply when `contours.Count == 0`, not when the metric still equalled the `Glyph()` placeholder 1.0.
	- `AssetImporter.ResolveSystemFont`: third probe `<repo>/_Build/FontSources/<file>` (from `Paths.DATA/../..`) after the machine and per-user font folders. `.gitignore`: `_Build/FontSources/`.
	- New `ThoriumFonts.imports.xml`: `<Charset Name="Latex">` (348 chars: ASCII, NBSP, Latin-1 minus soft hyphen, Latin Extended-A, ȷ, spacing accents ˆˇ˘˙˚˛˜˝, – — ‘ ’ ‚ “ ” „ † ‡ • … ‰ ‹ › €, ligatures ﬀ ﬁ ﬂ ﬃ ﬄ); FontImports `lmroman10-regular.otf` (Italic/Bold/BoldItalic declared), `lmmono10-regular.otf`, `latinmodern-math.otf` (Italic `lmroman10-italic.otf`, Bold `lmroman10-bold.otf`, Charset `Math`).
	- `ThoriumAssets.assets.xml` FontAssets: `latin-modern` (Fonts/lmroman10-regular), `latin-modern-mono` (Fonts/lmmono10-regular), `latin-modern-math` (Fonts/latinmodern-math).
	- Baked Thorium data, committed (~5.4 MB): `Data/Fonts/lmroman10-regular/`, `lmmono10-regular/`, `latinmodern-math/` — `.agd`, `_atlas.png`, `.afm` per face, `.import.xml`, `.math.xml` for LM Math.
	- Tests: engine `Fonts.CffCharstring` (Fonts suite, hand-built charstrings, runs on any machine); Thorium suite `LatinModern.tests.xml` (`LatinModernTests`) — `LatinModern.Baked`, `LatinModern.Draws` (golden `Goldens/LatinModern.Draws.Lines.png`). Named LatinModern, not Fonts: a host suite file of the same name shadows the engine's Fonts suite through the virtual file system.
- L3 — typesetter and lowering to `<Document>` (2026-10-05), nothing in the UI calls it yet (split view is L4):
	- New in `ArctisAurora.Core.Tex`: `TexGlue` (public struct width/stretch/shrink + orders; was private `TexExpander.Glue`), `TexFont` (record struct family/bold/italic/size sp), `TexStyle` (record struct font/color/underline), `TexFamily` {Roman, Sans, Mono} — in `TexFont.cs`.
	- `TexNode.cs`: horizontal `TexChar`, `TexGlueNode` (`interword`), `TexKern`, `TexPenalty` (`Forced = -10000`), `TexMathNode` (source, display), `TexHBox`; vertical `TexParagraph` (`TexParStyle style`, `list`), `TexVGlue`, `TexVPenalty`, `TexRuleNode`; `TexParStyle` (kind, level, align, list, listDepth, listKindDepth, itemNumber, item), enums `TexAlign`, `TexParKind` {Text, Heading, Code, Quote}, `TexListKind`.
	- `ITexFontMetrics` (`Quad`, `XHeight`); `TexFormat.Prelude` (LaTeX commands as TeX macros, read before every source); `TexTypesetter(source, metrics?)` — `Run()`, `vlist`, `endnotes`, `errors`, `documentClass`, `sizeOption`, `paper`, `marginTop/Bottom/Left/Right` (mm, null = class default), `normalSize`, `expander`.
	- `TexExpander`: constructor `TexExpander(string source, string prelude = "")`; public fields `quad`, `xHeight` (`Func<int>?`, em/ex in sp, null = fixed 10pt), `passUndefined`; public methods `DefinePrimitive(name)` (Op.Caller — `Next` returns the token under its own name), `Save(Action restore)`, `Insert(TexToken[])`, `CanReadRaw`, `ReadRaw(terminator)`, `ReadRawChar(out char)`, `Counter(name)`, `StepCounter(name)`; made public: `Error`, `ScanInt`, `ScanDimen()`, `ScanGlue`, `ScanKeyword`, `Argument`, `Optional`, `NameArgument`, `TakeStar`; new primitive `\stepcounter`; `\newcounter{x}[within]` resets x (cascading) when within steps; `Next` returns `\par`/`\relax` under those names even when reached via a `\let` alias.
	- `TexReader`: constructor takes `prelude` (prelude lines report line numbers 0 and below); `ReadRaw`, `ReadRawChar`.
	- `ArctisAurora.Core.UI.TexLowering` (`UI/TexLowering.cs`): `Compile(source, out List<TexError>)`, `Lower(TexTypesetter)`, constants `RomanFont = "latin-modern"`, `MonoFont = "latin-modern-mono"`, `MathFont = "latin-modern-math"`; `TexAtlasMetrics : ITexFontMetrics` (em = font size; ex = the face's 'x' glyphHeight from `FontAsset.atlasMetaData`, cmr10 ratio 0.430554 when the asset is missing).
	- `UI.TextRunControl`: `mathBoxes` keyed `(source, display, FontAsset)`; `MathBoxFor(in StyleSpan, FontAsset)`; a math span uses its own resolved font when that font has `mathConstants`, else the `"math"` asset (Cambria). `UI.MathLayout`: new private `Present(Glyph, FontStyle)` used by `Glyph` and `Ink`.
	- Lowering: one Block per paragraph, split at forced breaks (`\\`); a Run per stretch of one `TexStyle`; interword glue → space (NBSP inside an hbox); other glue and kerns → NBSPs rounded to their width, nothing under 0.4 of a space; vertical glue and penalties dropped; `\hrule` → Rule block. Run attributes only where they differ from the block's `TextStyle` (FontName, FontSize+FontSizeAuthored); Bold/Italic/Underline/ColorHex as set. Layout: LineHeight 1.2, BlockSpacing 0.5×base px, ListIndent 1.875×base px, TextStyles Text/Quote (latin-modern), Code (latin-modern-mono) at base px, Heading levels at the size of the first heading of that level. 1pt = 96/72.27 px. Margins: article `\textwidth` 345/360/390pt (10/11/12pt) centred on the paper; vertical = (paper height − 550pt)/2 — approximations of LaTeX defaults.
	- Lists: itemize/enumerate items → `List="Bullet"`, `Level` = total depth − 1, Marker by depth within that kind (enumerate Decimal, LowerAlpha, LowerRoman, UpperAlpha; itemize Disc, Circle, Square, SquareOutline), enumerate items carry `Start` = their number; description items are plain Text blocks with the bold label inline; a non-first paragraph inside an item is a plain Text block (not indented).
	- Sections: article numbers to subsubsection (secnumdepth 3), report/book to subsection (2) with chapters; section `\Large` bold, subsection `\large`, subsubsection normalsize, chapter "Chapter N" line at `\huge` (Text block) + title at `\Huge` Heading1; `\paragraph`/`\subparagraph` are run-in bold. `\chapter` in article is an error and set as a section.
	- Prelude also gives `\label` (nothing), `\ref`/`\pageref` → ??, `\eqref` → (??), `\cite[…]{…}` → [?] (as LaTeX's first pass), `\newpage`/`\clearpage` → `\par`, `\hspace`/`\vspace`, `\hfill`, `\smallskip`…, `\maketitle` via `\centering\LARGE\@title…`.
	- Tests: engine `Tex.tests.xml`/`TexTests` add `Tex.StepCounter`, `Tex.EmFromFont`, `Tex.Typeset.Paragraphs`, `.Fonts`, `.Sections`, `.Lists`, `.Math`, `.Ligatures`, `.Accents`, `.Verbatim`, `.Errors`, `Tex.Lowering`; Thorium `LatinModern.tests.xml`/`LatinModernTests` adds `Tex.Preview` (golden `Goldens/Tex.Preview.Page.png`).

## Why these choices

**The whole LaTeX editor is native, not a port of tex.web.** (agreed with the user 2026-10-05)
A full port (TRIP-verified, XeTeX-compatible, running the real `latex.ltx` and package `.sty` sources, Aurora only rendering shipped pages) was considered and rejected by the user. It would run any package's source but costs the port's T3–T5 (~25k lines of WEB) before anything shows; native gets the user's documents sooner and reuses Aurora's runs, `TextMeasurer`, `Paginate` and math. Cost accepted: support is bounded by what we reimplement — every package environment (amsmath `align` two-pass, `tabular`, …) is C#, and a package we have not rewritten (e.g. `mathtools` patching amsmath internals) does not work. The analysis behind the choice: Aurora's typesetting cannot run TeX macro code that reads layout mid-expansion (`\lastskip`, `\wd` of a just-built box, `\pagetotal`, `\output`), because `DocumentControl.Paginate` is a post-measure placement pass and `TextLine` is float geometry with no glue. The drawing layer is reusable either way.

**L-F1: no NuGet, no external tools.** `\usepackage` is accepted; unknown packages are logged and ignored. Package set is "as many as possible" (user), starting from amsmath, amssymb, graphicx, xcolor, hyperref (links), geometry, booktabs, enumitem, fancyhdr and growing.

**L-F2 level 2: a real TeX tokenizer + expander, document commands and package environments as C# primitives.**
Rejected level 1 (a command table with template `\newcommand`): it breaks on `\def\foo#1.#2`, `\expandafter`, `\newif` and `\makeatletter` code.

**L3 emits a TeX-like node list (chars, glue, kerns, penalties, boxes), lowered to `<Document>` afterwards.** (user: "your recommendation")
It keeps `\hbox`/`\setbox`/`\wd`, `\vskip`, `\lastskip`/`\unskip`/`\lastbox` addable later; emitting straight to `<Document>` closes them. `\output` and the page-builder primitives stay out — that is the port route.

**L-F3 / L-F4 / L-F5 / L-F6.** Overleaf-style split view (source + read-only paged preview). The preview is the `<Document>` tree, Aurora's layout gaining glue gaps, a Knuth-Plass mode, hyphenation, footnotes, running heads and float placement in L7. CFF (Type 2 charstrings) font support is added — the atlas baker already takes cubic `Bezier` contours (`SvgPath` feeds cubics to the MTSDF generator), so the work is the CFF table reader. Endnotes in v1, real footnotes in L7.

**The file/editor and the expander were planned as two phases and merged into L1** (user).

**`.tex` reuses `DocumentEditorControl` instead of a new editor control, each source line a Code block.** Undo, selection, clipboard and code colouring come with it.

**Line ending and BOM live on `RichTextDocument`**, so a save is byte-identical; precedent `SheetDocument.csvBom`. `.txt` is deliberately not changed.

**The expander has its own namespace `ArctisAurora.Core.Tex`** (pure, no UI dependency) rather than sitting flat in `Core.UI` like `MathParser`.

**Meanings are nested private types inside `TexExpander`** (Primitive/Macro/CharMeaning/Register) with one generic save-stack `Table<TKey,TValue>` for meanings, catcodes and registers. `\begin`/`\end` close the environment group through an internal sentinel control sequence pushed after `\end<name>`'s code.

**`DocumentControl.EndCodeBlock` is off in `plainText`** (user, 2026-10-05): Enter on an empty last code line would turn it into text, so a `.tex` would lose its LaTeX lines while typing at the end. `Tex.SourceEnterPaste` covers it.

**`.tex` shares the `.txt` refusals** (user, 2026-10-05) — `DocumentEditorControl.CanAlign`, `InsertTable`, `PasteImage`: a table, picture or alignment has nowhere to go in source. No test.

**Latin Modern ships as a baked atlas only** (user, 2026-10-05): the `.otf` files are downloaded to bake from, not committed and not needed on users' machines; the atlas is what the repo carries.
Fonts bake only in Debug (`AssetImporter`: `if (!Engine.isDebug) return true;`), and a Debug machine without the source logs a Warn and keeps the committed atlas, so committing just the atlas already works. Rejected: committing the `.otf` files (users do not need them).

**Sources live in gitignored `_Build/FontSources/`, probed after the system font folders.** Rejected: copying into the per-user Windows font folder (writes into the OS font directory); an absolute path in the import XML (machine-specific).

**Latin Modern is imported by Thorium only (`ThoriumFonts.imports.xml`).** Atlases are baked per host into each host's own `Data/Fonts` (Thorium, Carbon and AuroraEditor each track their own `cambria/`), so engine imports would bake and commit three copies. Only Thorium edits `.tex`.

**The `Latex` charset is 348 chars, including the ligatures and the accent, quote and dash characters**, so L3's TeX ligatures (`` `` `` → “, `--` → –, fi, ffi …) and accents need no re-bake. Rejected: the existing `Latin` charset.

**Faces: Roman 10 ×4 (regular, italic, bold, bold-italic), Mono 10, LM Math with Roman italic/bold for variables** (same pattern as Cambria). Optical sizes (5–17) and Sans are left out — the 10pt master is scaled.

**The ink box uses exact cubic extrema**, not the control-point hull, which is looser.

**CFF needed no atlas-side work.** Cubic `Bezier.Point.isCubicControl` already existed (made for SVG icons), the MTSDF generator fills by nonzero scanline winding so CFF's counter-clockwise outer contours need no reversal, and the sfnt table directory reader already handles `OTTO`.

**`importerVersion` is deliberately not bumped** — existing atlases do not re-bake.

**Empty-glyph fix** (user approved). The old check treated `glyphHeight == 1` / `glyphWidth == 1` (the `Glyph()` placeholder) as "empty"; LM Math's ∑ (−250…750) and √ (−960…40) are exactly 1000 units = 1 em tall, got squashed to 0.1 em and drew as a few dots. Cambria's never hit it. Now "empty" means no contours. Existing atlases are unchanged until re-baked; empty glyphs (spaces) behave as before.

Measured: the whole bake took ~40 s for ~2,600 cells (Roman 24 s, Mono 4 s, Math ~10 s), far below the ~20 min estimated from Cambria's ~7 min first-launch note; that note in `math-in-notes` may refer to something else and was not re-measured.

**L3 forks (user: "go with recommended").**
- F1 — LaTeX commands that are plain macros live in a TeX prelude, a C# string in `TexFormat`, run before the source. Rejected: every command as a C# primitive (more code, loses LaTeX's own structure); a data file `Data/Tex/aurora.ltx` (path plumbing into three hosts, not XML).
- F2 — formulas in LM Math: a math run's `FontName` picks the math face, absent = `"math"`. Rejected: keep Cambria (formulas would not match the body).
- F3 — package scope: core + geometry (paper/margin keys), xcolor (`\textcolor`, `\color`, named colours with `name!pct!other` tints, HTML/rgb/RGB/gray), hyperref `\href`/`\url` as underlined/mono text only. Rejected: core only.
- F4 — footnote marker is a math run `{}^{n}` (`<Run>` has no superscript); notes become endnotes under a "Notes" Heading1. Rejected: `[n]` text.
- F5 — default paper letterpaper as LaTeX; a4paper by class option or geometry. Rejected: A4 (Aurora's default).

**A C# primitive or a prelude macro: anything `MathParser` also knows (`\,`, `\quad`, `\ldots`, `\{`) must be a caller primitive.** Inside math the typesetter writes tokens back as source with expansion on, so a macro would expand to `\kern…` and break the formula. Text symbols, accents and spacing are therefore C# tables in `TexTypesetter`.

**Math is collected as source text with `passUndefined` on.** User macros expand and unknown commands (`\frac`, `\alpha`) pass through to `MathParser`; a control word gets a trailing space. `\par`, an `\end…` or an internal `@end…` sentinel inside math ends it with "Missing $ inserted".

**Typesetter state is saved through the expander's save stack via `Save(Action)`**, so `{\bf x}` and environments restore as TeX does. Headings and footnotes suspend/resume the outer state through an internal frame stack, ended by sentinels `\@endhead`, `\@endnote`; `\@runin` (run-in `\paragraph`) and `\@label` (`\item[x]`) skip the following space. Alignment is taken when a paragraph ends (TeX's rule), so `{\centering text\par}` centres and `{\centering text}` does not.

**Ligatures live in the typesetter, Roman/Sans only:** `` `` `` → “, `''` → ”, ` → ‘, ' → ’, `--` → –, `---` → —, `!``/`?`` → ¡/¿, ff fi fl ffi ffl → U+FB00–FB04; braces break a ligature; none in mono. **Accents compose with Unicode NFC** (base + combining mark); `\i`/`\j` as accent bases become i/j; an accent over nothing gives the spacing accent; no precomposed form → base letter + Warn.

**A fragment with no `\documentclass` is typeset as a body** (lenient; tests use fragments). Text in the preamble is "Missing \begin{document}" once. Nothing after `\end{document}` is read.

**Fork A (after the first golden; user: hand to a subagent, recommended option).** LM Math's italic face is LM Roman Italic, which has no Greek, so `\pi` (`MathParser` sets Greek italic) drew blank. `MathLayout.Present` uses Regular when the requested face's ink box is empty (0,0,0,0 — how a face missing a character bakes, checked for π/α in LM Math Italic and Bold) and Regular's is not; Greek in LM Math is therefore upright, not italic as in LaTeX. Rejected: LM Math's italic Greek at U+1D6FC+ (outside the 16-bit char atlas — large change); reverting F2. Cambria is unaffected (its italic has Greek); all existing note-math goldens pass.

**Fork B (user: c).** The line before a display formula is justified across the whole width (TeX never stretches it). Left to L7. Rejected for now: splitting the paragraph at display math in lowering; making Aurora's justify treat a line ending at a display run as a last line (the general fix; notes likely share the bug — unverified).

## Known gaps
- Pasted tabs become spaces (existing paste path). Mixed line endings in one file normalise to CRLF on save.
- Expander unsupported: `#{` parameter, `^^` notation, `\input`, `\number`/`\romannumeral`/`\meaning`, `\aftergroup`/`\afterassignment`, `\refstepcounter`, `\outer`, mu units.
- NOT GUI-verified: opening a `.tex` from the vault browser in a normally launched Thorium, typing in it, saving.
- Boot fails on a pre-existing `[Assets] failed to load the default sampler asset` error (in `engine.log` before this work).
- L2 verification: builds clean. Test-verified: `Fonts.CffCharstring` (lines, rrcurveto, hvcurveto, width + stems + one-byte hint mask, local and global subrs with bias, flex, ink box follows a curve not its controls); `LatinModern.Baked` (a/g/ﬁ/—/é distinct; a = 0.5 em and m = 0.833 em like cmr10; Roman has italic/bold/bold-italic; mono is monospaced; LM Math constants non-zero; ∑ ∫ √ α ⎛ ⎝ present). Golden-verified: `LatinModern.Draws` — PNG inspected (Roman line with ffi ligature glyph, em dash, curly quotes, é; mono line; math line ∑ ∫ √ α β γ ≤ ≠ ∞ and paren pieces; √ sits low by design, 0.96 em depth, confirmed by Windows' GlyphTypeface). Glyph indices for ∑ (3060) and √ (3077) cross-checked against Windows' `GlyphTypeface.CharacterToGlyphMap`. Full Thorium `--test`: 162 passed, 2 failed — pre-existing Boot (`default sampler asset`) and `Sheet.FixedSize` golden, which belongs to another session's concurrent uncommitted sheet work. NOT GUI-verified (nothing user-visible uses Latin Modern yet).
- Ligatures are applied in the typesetter (L3); kerning is not applied anywhere in layout (L7).
- CID-keyed CFF, CFF2/variable fonts, `seac` accent composition (logged) and the deprecated charstring arithmetic operators (logged, glyph stops) are unsupported. No Sans, no optical sizes.
- `LatinModern.Baked`'s missing-glyph detection is a heuristic (more than two characters sharing one box); it reported only true look-alikes (tabular digits 3/6/8/9, Latin/Greek H N O, circled operators).
- Atlases are committed only for Thorium; Carbon and AuroraEditor have no Latin Modern.
- The `.otf` sources must be downloaded again on any other dev machine to re-bake (CTAN `fonts/lm/fonts/opentype/public/lm/` and `fonts/lm-math/opentype/latinmodern-math.otf`); without them the committed atlas is used and a Warn is logged per Debug launch.
- L3 verification: builds clean. Test-verified: engine Tex suite 23/23 pass (11 existing + 12 new) in Thorium `--test=Tex`. Golden-verified: `Tex.Preview` PNG inspected — title block, numbered sections, nested itemize/enumerate with numbers, code block, endnote, inline and display LM Math, π drawing after Fork A; also checks `TexAtlasMetrics` ex at 10pt is 4.2–4.45pt. Full Thorium `--test`: 174 passed, 2 failed — Boot (pre-existing default sampler asset error) and `Sheet.FixedSize` golden (pre-existing, another session's uncommitted sheet work). NOT GUI-verified: nothing user-visible calls `TexLowering` until L4.
- The em dash (U+2014) does not draw at 13px in a note (lowering emits it — verified; a `LabelControl` at 13px draws it faintly). Likely a sub-pixel-tall glyph quad missing every pixel centre; renderer behaviour, not fixed, unproven. The golden records it missing.
- The line before display math is stretched by justify (Fork B → L7). The golden records it.
- No paragraph indent, Aurora block spacing, glue approximated by NBSPs, no kerning, no hyphenation/Knuth-Plass (L7).
- Not in L3: `\label`/`\ref` resolution, floats, `tabular`, `\includegraphics` (undefined → error), bibliography (L5); equation numbers, amsmath environments, matrices — `\begin{…}` inside math is an "Environment undefined" error (L6); `\hbox to`/`\setbox`/`\wd`/`\lastskip` (node types allow, not implemented); `\input`; PDF (L8).
- Small caps set upright (logged once); Sans set in Roman (no LM Sans); itemize label `\item[x]` shows the bullet and the label; continuation paragraphs in an item lose the indent; `\verb` crossing a line end is reported but kept; `\url` arguments with `%`/`#` not handled; `\textbf` etc. inside math expand to `\bfseries` which `MathParser` does not know.
- Copying from the preview (L4) will carry U+FB0x ligature characters. Greek in LM Math formulas is upright.

Related: [[note-file-formats]], [[code-block-colouring]], [[math-in-notes]], [[markdown-blocks-and-alignment]]
