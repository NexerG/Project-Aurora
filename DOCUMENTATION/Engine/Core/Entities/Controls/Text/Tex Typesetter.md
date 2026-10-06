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

The amsmath environments are numbered here, not in [[Math Parser]]. `equation` and the `align` family take a number per row, `\label` inside one points at that number and `\eqref` resolves to it in parentheses. The typesetter does it by rewriting the source: each numbered row gets a `\tag{n}` before the formula reaches the parser. `\DeclareMathOperator` is a prelude macro over `\operatorname`.

It never throws. Problems are collected in `errors` and the run recovers the way LaTeX does: an `\end` that matches nothing is reported, a `$` that never closes is "Missing $ inserted", an undefined environment is an error.

> The route, the forks taken and the phases still to come are in `ClaudeMemory/Context/tex-plan.md`.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `TexTypesetter(string source, ITexFontMetrics? metrics, string? folder = null)` | constructor | Starts a run; no metrics means a fixed 10pt em and ex. `folder` is where pictures and `.bib` files are looked for. |
| `Run()` | method | Typesets the whole source, then `TypesetBibliography()` and `Resolve()`. |
| `vlist` | field | The vertical list: paragraphs, vertical glue, penalties and rules. |
| `footnotes` | field | The footnotes, one `TexFootnote` (id and vlist) per `\footnote`; they replace the old endnotes and are lowered to `<Footnote>` groups. |
| `footnoteSkips` | field | `\skip\footins` in points, 9, 10 and 10.8 for a 10, 11 and 12pt class. |
| `pageStyle`, `pageStyles` | fields | The document's page style name and the named styles, each a `TexPageStyle`. |
| `errors` | field | The `TexError` list. |
| `hyphenation` | field | Word to break points, filled by `\hyphenation{…}`; document exceptions beat the pattern file's. |
| `parIndent` | field | The paragraph indent in points, 15, 17 or 18 for a 10, 11 or 12pt class. |
| `displaySkips` | field | The display skips in points, 10, 11 and 12 for a 10, 11 and 12pt class. |
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
| `TexVGlue`, `TexVPenalty` | vertical glue and penalties; a `TexVGlue` with `merge` set is `\addvspace` glue, which takes the larger of itself and the pending glue instead of adding |
| `TexRuleNode` | a horizontal rule |
| `TexRefNode` | a `\ref` or `\cite`: keys, whether it is a cite, its style and source position, and a mutable `text` that starts as "??" |
| `TexImage` | an `\includegraphics`: path, width and height in scaled points, scale, angle, keep-aspect |
| `TexBibliographyMark` | where `\bibliography` stood, so the bibliography can be spliced in there |
| `TexColumn` | a tabular column: alignment and width in scaled points, 0 meaning natural |
| `TexTableCell` | one tabular cell: its vertical list and its column span |
| `TexTable` | a tabular: columns, rows, its rules, the `\|` count at each column boundary, its alignment and its source line |
| `TexTableRule` | one horizontal rule at a row boundary: its kind (`TexRuleKind`: Plain, Heavy, Light or Cmid), the columns it spans, whether each end is trimmed, and a width in scaled points, 0 meaning the kind's own |

`TexParStyle` carries the paragraph's kind (`TexParKind`: Text, Heading, Code, Quote), heading level, alignment (`TexAlign`), list kind (`TexListKind`), list depth, depth within its list kind, item number and whether it starts an item, and `indent`, which marks an indented paragraph. `TexFont` is family (`TexFamily`: Roman, Sans, Mono), bold, italic and size in scaled points; `TexStyle` is font, colour and underline.

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

### Math environments
	the names matrix, pmatrix, bmatrix, Bmatrix, vmatrix, Vmatrix, smallmatrix, cases, array, aligned, alignedat, gathered and split are primitives
		inside math, `Append` writes them back as `\begin{x}` and `\end{x}` source for the parser
		outside math, they report "Missing $ inserted"
	`Ends` lets an `\end` of one of them through, so it does not close the formula being collected
	equation, align, gather, flalign, alignat and multline, starred or not, are collected as display math with `MathUntil`

### Numbering an equation environment
	split the collected body into rows at top-level `\\`, outside braces and nested `\begin…\end`
	if the environment is equation or multline
		it takes one number, multline's on the last row
	for each row
		skip it when it is empty or holds `\notag` or `\nonumber`
		take the next equation number, `n` in an article and `chapter.n` in a report or book
		put `\tag{n}` in the row
		map its `\label` key to n, or to the tag's own text when the row already has a `\tag`
	write the body back wrapped in `\begin{env}…\end{env}`, except for equation

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

### Hyphenation commands
	`\-` adds a U+00AD character, and the prelude no longer defines it as empty
	for each word in `\hyphenation{…}`
		`TexHyphenator.Exception` splits "as-so-ciate" into the word and its break points
		store them in `hyphenation`

### Sections
	article numbers down to subsubsection, report and book to subsection and add chapters
	section is bold at `\Large`, subsection at `\large`, subsubsection at the normal size
	a chapter is a "Chapter N" Text block at `\huge` and its title as a Heading1 at `\Huge`
	`\paragraph` and `\subparagraph` are bold run-in headings
	`\chapter` in an article is an error and is set as a section

### Vertical glue and indent
	`VSkip` appends vertical glue in natural widths, with no stretch or shrink
		`\vskip`, `\vspace` and `\bigskip` add
		structural skips (sections, lists, floats, verbatim, quote and center) set `merge`, so they take the larger of themselves and the pending glue like `\addvspace`
	a heading is preceded and followed by a skip, `ex` being the normal font's
		chapter 50pt above and 40pt below
		section 3.5ex above and 2.3ex below
		deeper levels 3.25ex above and 1.5ex below
		a run-in heading is not indented
	for a list
		`ListSkip` gives `\topsep` above and below, `\itemsep + \parsep` between items, `\parsep` between paragraphs of one item
		the skip is full at depth 1, half at depth 2 and a quarter deeper
		`\topsep` is 8, 9, 10pt and `\itemsep` 4, 4.5, 5pt for 10, 11, 12pt
	a float sets no glue of its own; `\floatsep` and `\intextsep` are 12, 12, 14pt for 10, 11, 12pt (`floatSeps`) and `\textfloatsep` is 20pt (`TextFloatSep`), written to `<Page>` by lowering
	`EndEnvironment` sets `afterEnvironment`
	`\maketitle` in the prelude puts `\vskip2em` above, 1.5em and 1em between title, author and date, and 1.5em below
	a paragraph is marked indented, `parIndent` wide, unless
		`\noindent` was seen, which only counts in vertical mode (`noIndent`)
		it follows a heading, until text (`afterHeading`)
		it follows a list or trivlist environment or a float, until a blank line (`afterEnvironment`; `\par` in vertical mode clears it)
		it is inside a list or quote
		it is not justified: centred, flush or ragged
		it is a `\bibitem` entry

### Floats and tabulars
	`BeginFloat` keeps the `[...]` option through `Placement`
		the letters `htbpH!` are kept and others dropped
		an empty option becomes `tbp`, a lone `h` becomes `ht`
	the body is redirected into `TexFloat.vlist` by pushing a frame, `openFloat` marks it
		the float sits in the vertical list, or in the hlist when it is met mid-paragraph
	`EndFloat` closes `figure`, `table` and the starred forms
		after a mid-paragraph float the space that follows is eaten when one preceded it (`\@esphack`)
	a float inside a box, footnote, caption, heading, table cell or another float reports "LaTeX Error: Not in outer par mode" and stays inline
	where the float lands on a page is `Paginate`'s decision, not the typesetter's
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
		a `|` in a `\multicolumn` spec is ignored
	`|` in the column spec is counted into the tabular's `vrules`, one count per column boundary
	`\hline`, `\cline{a-b}`, `\toprule`, `\midrule`, `\bottomrule` and `\cmidrule` leave a `TexTableRule`
		`\cmidrule` reads its optional width `[w]` and its `(l)`, `(r)` trims
		the rule goes at the row boundary under the last row when no row is open, otherwise above the open row
	the tabular's alignment is taken when the paragraph holding it ends, as TeX does for text
		`\centering`, `center` and `flushright` give a centred or right table
		a paragraph that ended justified gives a left table

### Pages
	`\newpage` and `\clearpage` (`PageBreak`) leave a `TexPageBreak`, with `clear` set for `\clearpage`
		`\cleardoublepage` is `\clearpage` and `\pagebreak` is `\newpage`, in the prelude
	`\pagestyle` and `\thispagestyle` (`SetPageStyle`) name a style from `pageStyles`
		`DefaultPageStyles` gives empty, plain, headings and myheadings
		the document starts in plain for an article or report, headings for a book
		`\maketitle` does `\thispagestyle{plain}`
	`\markboth` and `\markright` (`Mark`) are kept on the next paragraph as `markLeft` and `markRight`
		`\markright` keeps the left half
	a heading sets marks (`HeadingMarks`), a starred heading none
		headings style: an article `\section` is `\markright{UPPER(num␣␣title)}`, a report or book `\chapter` is `\markright{CHAPTER n. TITLE}`
		fancyhdr: an article `\section` is `\markboth{UPPER(num␣␣title)}{}` and a `\subsection` `\markright{num␣␣title}`
		fancyhdr: a report or book `\chapter` is `\markboth{CHAPTER n. TITLE}{}` and a `\section` `\markright{UPPER(num. title)}`
	`\thepage`, `\leftmark` and `\rightmark` (`Field`) write `PageField`, `LeftMarkField` or `RightMarkField` (U+E000–E002), the field characters a slot's text carries
		outside a running-head slot they print "??"
	`\fancyhf`, `\fancyhead`, `\fancyfoot`, `\lhead`, `\chead`, `\rhead`, `\lfoot`, `\cfoot`, `\rfoot` (`FancySlots`, `SetSlot`) fill a `TexPageStyle`'s slots by place name
	`\fancypagestyle` (`FancyPageStyle`) defines a named style
	`\headrulewidth` is 0.4pt and `\footrulewidth` 0pt, global to the document
	`\pageref` leaves a `TexRefNode` with `page` set, and `\label` leaves a `TexLabelMark` where it stands
		the number is filled after layout, see `TexEditorControl.ResolvePages` below

### Footnotes
	`\footnote` sets its text in `\footnotesize` into a `TexFootnote`
		the id is the footnote's sequence number, not the counter, so `\setcounter{footnote}` cannot make two share an id
	the mark in the text is a math node with `note` set, which lowering writes as a run with `Note="id"`

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
	`Hyphenate` first, over `vlist`, each footnote's `vlist` and each table cell, top level only so nothing inside an `\mbox` or `\hbox` is touched
		for each paragraph
			for each word, a run of letters in one style that is not monospaced
				capitals are lowered, and the ligatures U+FB00 to U+FB04 are taken apart for lookup
				leave the word as written when it holds any other letter (é, ï) or already holds a `\-`
				`TexHyphenator.Points` gives the break points: document exceptions, then the file's exceptions, then the patterns, with minimums 2 and 3
				insert a U+00AD character node at each point, none inside a ligature
	`Advance` gives U+00AD no width
	for each paragraph in `vlist`
		start one Block, and split it at every forced break (`\\`)
		for each stretch of one `TexStyle`
			write a Run
			write Run attributes only where they differ from the block's TextStyle: FontName, FontSize with FontSizeAuthored
			Bold, Italic, Underline and ColorHex are written as set
		interword glue becomes a space, or a no-break space inside an hbox
		`\quad`, `\hspace` and `\kern` become a spacer run of their exact width, `<Run Text=" " Space="px"/>`, which draws nothing and never breaks a line
		penalties are dropped
		a `Spacing` holds the pending skip, the paragraph indent and the display skips
			vertical glue (`LowerVertical`) adds to the pending skip, or takes the larger of the two when it merges
			`Place` writes the pending skip as the next block's or table's `SpaceBefore` and clears it
			the paragraph indent is written as the block's `Indent`
			`Displays` cuts a paragraph at each display formula
				the text before is one block
				the display is its own block, with `\abovedisplayskip` above it
				the continuation is a block with `\belowdisplayskip` above it, and is never indented
		`\hrule` becomes a Rule block
		a `TexTable` becomes a `<Table>` with `Borders="false"`, `Padding` of `\tabcolsep` across and 0 down, and `Align`
			each column's `<Column Width>` is its natural width: Latin Modern glyph advances, the `MathLayout` width for math, the picture size for images
			add `\tabcolsep` of 6pt each side, minimum 28 px; a `p{w}` column uses w
			a merged cell wider than its columns widens the last of them
			a spanning cell is a `<Cell ColumnSpan>`
			a column's `|` become `RuleLeft` and `RuleRight`, and a rule becomes `RuleAbove` or `RuleBelow` with `TrimAbove` or `TrimBelow` on each cell it reaches
				two plain rules at one place become a double rule
			a tabular nested in a cell is flattened to one paragraph per row
		a `TexImage` becomes a Run with Image, Width, Height and Rotation
		a `TexRefNode` becomes its text in its style
	list items become `List="Bullet"` with `Level` as the total depth minus one
		the Marker follows the depth within that kind: enumerate Decimal, LowerAlpha, LowerRoman, UpperAlpha; itemize Disc, Circle, Square, SquareOutline
		an enumerate item carries `Start`, its number
		a description item is a plain Text block with its bold label inline
		a paragraph inside an item after the first is a plain Text block
	write the layout: LineHeight 1.2, BlockSpacing 0, which is LaTeX's `\parskip`, ListIndent 1.875 times the base size, OptimalBreaks true so Aurora breaks the lines Knuth-Plass
	TextStyles Text and Quote use `latin-modern`, Code uses `latin-modern-mono`, Heading levels take the size of the first heading of that level
	1pt is 96/72.27 pixels
	margins: article `\textwidth` is 345, 360 or 390pt for 10, 11 and 12pt, centred on the paper; vertical is (paper height − 550pt)/2

### Pages, footnotes and `PageRefs` in `TexLowering`
	`Layout` writes `<Page Style FootnoteSkip>` and its `<PageStyle>` and `<Slot>` children
	a `TexPageBreak` becomes `PageBreak` on the next block, through `Spacing.pageBreak` in `Place`
	a paragraph's `pageStyle`, `markLeft` and `markRight` are written on its first block
	`Footnotes` writes a `<Footnote Id>` group right after the paragraph, or the table, that holds the mark
		the mark's math run gets `Note`
	`PageRefs(root)` returns a `TexPageRefs`
		`refs` are the `\pageref` runs
		`labels` map each label key to its block index and offset
		both are found through the XElement annotations `PageRefNote` and `LabelSite`
	`TexEditorControl.ResolvePages` runs after layout
		for each label, read its page through `PageAt`
		if a `\pageref` number changed, reload the preview, at most twice

### `TexAtlasMetrics`
	em is the font size
	ex is the 'x' glyph height from the face's `atlasMetaData`
	when the asset is missing, ex is 0.430554 of the em, the cmr10 ratio

## PDF export
`File → Export PDF` in a `.tex` split view writes `<name>.pdf` beside the source, overwriting an earlier export and asking nothing. The PDF is vector: text is real selectable text in Type 3 fonts drawn from the font's own glyph outlines, pictures are the original files, and rules are vector rectangles. Colours come from the engine's `print` palette, so a dark theme does not give a dark PDF; "print" therefore also appears in the theme list.

### `PdfExport.Export(tree, target, title)`
	parse the preview's current `<Document>` tree into a fresh `RichTextDocument`
	build a detached `DocumentControl` that is in no window, at zoom 4, with the `print` palette
	measure and arrange it
	set `UIEngine.recorder`
	run the normal `UIEngine.Collect` walk
		the four draw points record instead of appending quads
			`TextRunControl.WriteGlyph` records font, face, char, size, pen, baseline, colour and clip
			`TextRunControl.WriteRect` and `TextRunControl.WriteImage` record rules and pictures
			`Control.Emit` records panels as rects
		a spacer run (`\quad`, `\hspace`) records a space glyph so copied text keeps the gap
	for each recorded item
		put it on the page sheet that contains it (`DocumentControl.PageRects`)
		convert it to page-local points, px × 72/96 / 4, y flipped
	for each page
		write one content stream, with a clip per clip change
		move glyphs in the bottom margin to the end of the stream so the page number copies last
	for each (font asset, style) used
		find the font file beside the atlas, then in the system font folders
		write a `PdfType3Font` holding only the used glyphs, at most 256 codes, with a ToUnicode map (ligatures U+FB00–FB06 map back to their letters)
	pictures: a JPEG is passed through as DCTDecode, anything else is decoded to RGB with a soft mask when translucent
	`PdfDocument.Save` writes the file

### `TexEditorControl.ExportPdf`
	if an edit is pending, recompile first
	if `\pageref` numbers are still being filled, mark the export waiting and return
	`OnTick` writes the PDF once they settle

Not exported: clickable `\href` links, bookmarks, floating pictures, gradients and effects (flat colour). Math copies as a jumble of symbols, as pdfLaTeX output does.

## Not supported yet
`\suppressfloats`, `placeins` and `\FloatBarrier`, `afterpage`, `tabularx`, `tabular*`, `longtable`, `\multirow`, natbib, `\autoref`, `\nameref`, `\listoffigures`, `\listoftables`, subfigure, hanging indent in bibliography entries, clickable references, `\arrayrulecolor`, `\parindent` on left-aligned tables, `\numberwithin`, `subequations`, `\intertext`, `\shoveleft`, `\shoveright`, `\hdotsfor`, mathtools, `\hbox to`, `\setbox`, `\wd`, `\lastskip`, `\input`. There is no kerning. Vertical glue has no stretch or shrink, `\hfill` and other infinite glue vanish, `\partopsep` is not added, bibliography entries have no `\itemsep` between them, a quotation's `\listparindent` is not modelled, a `\cline` under part of a merged cell rules the whole cell, booktabs gaps do not interrupt vertical rules, a spacer never breaks a line, and display formulas inside table cells get no display skips. Hyphenation is US English only, so accented words are never hyphenated; an explicit hyphen in a word ("well-known") is not a break opportunity; `\lefthyphenmin`, `\righthyphenmin`, `\uchyph` and `\hyphenpenalty` are not read. The preview breaks a paragraph Knuth-Plass only where a break set exists at tolerance 200; the rest stay greedy. Small caps are set upright and Sans is set in Roman. The em dash and the en dash do not draw at 13 px in a note. A `\def` inside a tabular cell leaks into later cells. Two-sided page styles (`[LE,RO]` reads only the odd entry, `\cleardoublepage` is `\clearpage`), `\pagenumbering`, `\footnotemark`, `\footnotetext`, footnotes split across pages, `\footnotesep`, `\MakeUppercase` and `\renewcommand` of `\sectionmark` are not supported; a mid-document `\pagestyle` sets the whole document's style, and a `\label` inside display math or a table cell has no page site.

## Related
- [[Tex Expander]] — the tokens this reads
- [[Math Layout]] — draws the formulas; a math run's font picks its math face
- [[Rich Text Document]] — the `<Document>` this lowers to
