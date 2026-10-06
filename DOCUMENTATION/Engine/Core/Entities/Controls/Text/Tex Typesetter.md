---
date: 2026-10-05
Status: Current
tags:
  - d_text
cssclasses:
  - Aurora.css
Linker:
  - "[[Arctis Aurora]]"
System:
Class:
  - "[[Tex Typesetter]]"
Parent Class:
Interfaces:
Used by:
Type:
  - Public
Attributes:
Namespace: ArctisAurora.Core.Tex
SourceFile: AuroraEngine/Core/Tex/TexTypesetter.cs
VerifiedAgainst: 2026-10-05
---
## Description

Reads the tokens a [[Tex Expander]] hands back and builds what TeX's stomach builds: a node list. Horizontal material is characters, glue, kerns, penalties, math and boxes; vertical material is paragraphs, vertical glue and rules. `TexLowering.Compile` then turns that list into a `<Document>` element that `DocumentXml.Parse` reads, so a LaTeX source becomes an ordinary paged note set in Latin Modern, with formulas in LM Math. The split view, `TexEditorControl`, calls it on a debounced recompile and shows what it collects in `errors`.

LaTeX commands that are plain macros are not C# at all. They live as TeX text in `TexFormat.Prelude`, which is read before every source, so `\textbf`, `\emph`, `\textcolor` and `\maketitle` keep LaTeX's own structure; sectioning, lists, sizes and font switches are C# primitives. Anything the math parser also knows (`\,`, `\quad`, `\ldots`, `\{`) is a C# primitive instead, because inside math the typesetter writes tokens back as source with expansion on, and a macro would expand to `\kern…` and break the formula. Text symbols, accents and spacing are therefore C# tables.

It never throws. Problems are collected in `errors` and the run recovers the way LaTeX does: an `\end` that matches nothing is reported, a `$` that never closes is "Missing $ inserted", an undefined environment is an error.

> The route, the forks taken and the phases still to come are in `ClaudeMemory/Context/tex-plan.md`.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `TexTypesetter(string source, ITexFontMetrics? metrics, string? folder = null)` | constructor | Starts a run; no metrics means a fixed 10pt em and ex. `folder` is where pictures and `.bib` files are looked for. |
| `Run()` | method | Typesets the whole source, then `TypesetBibliography()` and `Resolve()`. |
| `vlist` | field | The vertical list: paragraphs, vertical glue, penalties and rules. |
| `endnotes` | field | The footnotes, set after the body under a "Notes" heading. |
| `errors` | field | The `TexError` list. |
| `documentClass`, `sizeOption`, `paper` | fields | What `\documentclass[…]{…}` and `geometry` asked for. |
| `marginTop`, `marginBottom`, `marginLeft`, `marginRight` | fields | Margins in mm; null means the class default. |
| `normalSize`, `expander` | fields | The base size and the expander underneath. |
| `labels`, `citations`, `bibLabels` | fields | The reference tables `Resolve()` fills each `TexRefNode` from. |
| `paperWidth`, `paperHeight` | fields | The paper size in mm. |
| `textWidth`, `textHeight`, `textWidths`, `TextHeight` | fields | The text area in scaled points; `textWidths` and `TextHeight` are static. |

## Nodes

| Type | Holds |
| --- | --- |
| `TexChar` | a character with its `TexFont` and `TexStyle` |
| `TexGlueNode` | a `TexGlue` (width, stretch, shrink, orders); `interword` marks a space between words |
| `TexKern` | a fixed gap |
| `TexPenalty` | a break cost; `Forced` is -10000 |
| `TexMathNode` | the formula's source text and whether it is display |
| `TexHBox` | a horizontal box of nodes |
| `TexParagraph` | a horizontal list and its `TexParStyle` |
| `TexVGlue`, `TexVPenalty` | vertical glue and penalties |
| `TexRuleNode` | a horizontal rule |
| `TexRefNode` | a `\ref` or `\cite`: keys, whether it is a cite, its style and source position, and a mutable `text` that starts as "??" |
| `TexImage` | an `\includegraphics`: path, width and height in scaled points, scale, angle, keep-aspect |
| `TexBibliographyMark` | where `\bibliography` stood, so the bibliography can be spliced in there |
| `TexColumn` | a tabular column: alignment and width in scaled points, 0 meaning natural |
| `TexTableCell` | one tabular cell: its vertical list and its column span |
| `TexTable` | a tabular: columns, rows, whether it has rules, and its source line |

`TexParStyle` carries the paragraph's kind (`TexParKind`: Text, Heading, Code, Quote), heading level, alignment (`TexAlign`), list kind (`TexListKind`), list depth, depth within its list kind, item number and whether it starts an item. `TexFont` is family (`TexFamily`: Roman, Sans, Mono), bold, italic and size in scaled points; `TexStyle` is font, colour and underline.

## Methods

### Typesetting a paragraph
	read the next token from the expander
	a character
		set it in the current font, colour and underline
		a ligature in Roman or Sans merges with the characters that follow, braces break it, mono has none
	a space
		append interword glue
	a control sequence from a table
		append the text symbol, or compose the accent with its base
	`$` or `\(` or `\[`
		collect the formula as source text with undefined commands passed through
		append a `TexMathNode`
	`\par` or a blank line
		take the alignment now, as TeX does
		close the paragraph and append it to `vlist` with vertical glue

### Math collection
	turn on `passUndefined` so user macros expand and `\frac`, `\alpha` reach the math parser
	for each token until the closing delimiter
		write the token back as source, a control word gets a trailing space
		a `\par`, an `\end…` or an internal `@end…` sentinel ends the formula with "Missing $ inserted"

### Grouping and state
	font, colour, underline, alignment, list frame, quote and hbox are saved through the expander's save stack with `Save(Action restore)`
	so `{\bf x}` and an environment restore as TeX does
	a heading or footnote suspends the outer state on an internal frame stack
	the sentinels `\@endhead` and `\@endnote` close it
	`\@runin` and `\@label` skip the space that follows

### Ligatures and accents
	`` `` `` becomes “, `''` becomes ”, ` becomes ‘, ' becomes ’
	`--` becomes –, `---` becomes —, `!`` and `?`` become ¡ and ¿
	ff, fi, fl, ffi and ffl become the single characters U+FB00 to U+FB04
	an accent composes with its base through Unicode NFC (base plus combining mark)
	`\i` and `\j` as accent bases become i and j
	an accent over nothing gives the spacing accent
	a form with no precomposed character keeps the base letter and logs a warning

### Sections
	article numbers down to subsubsection, report and book to subsection and add chapters
	section is bold at `\Large`, subsection at `\large`, subsubsection at the normal size
	a chapter is a "Chapter N" Text block at `\huge` and its title as a Heading1 at `\Huge`
	`\paragraph` and `\subparagraph` are bold run-in headings
	`\chapter` in an article is an error and is set as a section

### Floats and tabulars
	a figure or table is set where it appears, placement is not applied
	`\caption` sets a centred "Figure n: text" paragraph, numbered chapter.n in a report or book
	`\includegraphics` looks for the file under the source's folder and each `\graphicspath` entry
		with no extension, try .png, .jpg, .jpeg
		a PDF, EPS or PS is reported as unsupported
		a missing file is reported as "File `x' not found"
	a tabular ends the paragraph it appears in
	for each cell
		start from the tabular's starting style, a cell is not an expander group
		`&` ends the cell and the spaces after it are skipped
	`\multicolumn` gives the cell its span

### Run, the end of the document
	typeset the body up to `\end{document}`
		`\ref` and `\cite` leave a `TexRefNode` carrying their style
		`\bibliography` leaves a `TexBibliographyMark`
	`TypesetBibliography`
		parse the `.bib` files with `TexBibliography.Parse`
		format the cited entries in the `\bibliographystyle` with `TexBibliography.Format` into thebibliography source
		`InsertSource` pushes that source into the expander
		typeset it into a list and splice the list in at the mark
	`Resolve`
		for each `TexRefNode`, fill its text
		a `\ref` inside a math node is rewritten to `\text{…}` in the math source

## Lowering

### `TexLowering.Compile`
	typeset the source with `TexAtlasMetrics` so em and ex come from the Latin Modern atlas, and hand it the source's folder for pictures and `.bib` files
	lower the vertical list to a `<Document>` element
	return it with the collected errors

### `TexLowering.Lower`
	for each paragraph in `vlist`
		start one Block, and split it at every forced break (`\\`)
		for each stretch of one `TexStyle`
			write a Run
			write Run attributes only where they differ from the block's TextStyle: FontName, FontSize with FontSizeAuthored
			Bold, Italic, Underline and ColorHex are written as set
		interword glue becomes a space, or a no-break space inside an hbox
		other glue and kerns become no-break spaces rounded to their width, nothing under 0.4 of a space
		vertical glue and penalties are dropped
		`\hrule` becomes a Rule block
		a `TexTable` becomes a `<Table>`, with `Borders="false"` when its spec and body carry no rules
			each column's `<Column Width>` is its natural width: Latin Modern glyph advances, the `MathLayout` width for math, the picture size for images
			add `\tabcolsep` of 6pt each side, minimum 24 px; a `p{w}` column uses w
			a merged cell wider than its columns widens the last of them
			a spanning cell is a `<Cell ColumnSpan>`
			a tabular nested in a cell is flattened to one paragraph per row
		a `TexImage` becomes a Run with Image, Width, Height and Rotation
		a `TexRefNode` becomes its text in its style
	list items become `List="Bullet"` with `Level` as the total depth minus one
		the Marker follows the depth within that kind: enumerate Decimal, LowerAlpha, LowerRoman, UpperAlpha; itemize Disc, Circle, Square, SquareOutline
		an enumerate item carries `Start`, its number
		a description item is a plain Text block with its bold label inline
		a paragraph inside an item after the first is a plain Text block
	write the layout: LineHeight 1.2, BlockSpacing 0.5 times the base size in pixels, ListIndent 1.875 times the base size
	TextStyles Text and Quote use `latin-modern`, Code uses `latin-modern-mono`, Heading levels take the size of the first heading of that level
	1pt is 96/72.27 pixels
	margins: article `\textwidth` is 345, 360 or 390pt for 10, 11 and 12pt, centred on the paper; vertical is (paper height − 550pt)/2

### `TexAtlasMetrics`
	em is the font size
	ex is the 'x' glyph height from the face's `atlasMetaData`
	when the asset is missing, ex is 0.430554 of the em, the cmr10 ratio

## Not supported yet
Float placement (`[htbp]`), `\pageref`, `tabularx`, `tabular*`, `longtable`, `\multirow`, natbib, `\autoref`, `\nameref`, `\listoffigures`, `\listoftables`, subfigure, hanging indent in bibliography entries, clickable references, booktabs rules drawn as rules (the full grid is drawn), equation numbers and `\eqref`, amsmath environments and matrices (`\begin{…}` inside math is an "Environment undefined" error), `\hbox to`, `\setbox`, `\wd`, `\lastskip`, `\input`, PDF output. There is no paragraph indent, no kerning and no hyphenation; glue is approximated by no-break spaces. Small caps are set upright and Sans is set in Roman. The line before a display formula is justified across the whole width. The em dash and the en dash do not draw at 13 px in a note. A `\def` inside a tabular cell leaks into later cells.

## Related
- [[Tex Expander]] — the tokens this reads
- [[Math Layout]] — draws the formulas; a math run's font picks its math face
- [[Rich Text Document]] — the `<Document>` this lowers to
