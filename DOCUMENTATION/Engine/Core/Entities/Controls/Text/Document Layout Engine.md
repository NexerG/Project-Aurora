---
Status: Planned
tags:
  - Engine
  - d_UI
  - d_Data
Class:
  - "[[TextMeasurer]]"
Type:
  - Public
---
> [!warning] Half of this page describes deleted code (2026-08-07)
> `DocumentLayoutCache` and the virtualized view built on it were implemented and then **reverted**
> — the cache, `TextRunControl` and `DocumentCanvasControl` no longer exist. [[TextMeasurer]] survives
> and is now the only thing in the engine that decides where a line breaks; each text control holds
> its own `BlockLayout` and answers `OffsetAt` / `CaretAt` for itself, so the control tree is the
> hit-test again. Everything below headed **Cache shape**, **Virtualization** and the cache rows of
> the memory budget is kept as the record of an argument that was tested, not as a description of the
> code. What replaced the *conclusion* is in [[#Status]].

## Description
The geometry layer between the [[Rich Text Document]] model and the drawn text. It measures every block from font metrics alone — no controls, no GPU — into lines, and every geometry question about the document is answered from those lines. Where they live is what changed: the plan was one cache per document, and what shipped is one `BlockLayout` per text control.

The cache existed because view materialization looked like the memory problem: each character is a [[GlyphControl]] — a full [[Vulkan Control]] entity with a `ControlData` row and a slot in the [[UI Rasterizer Module|UIModule]] 50,000-cap descriptor array. 100 pages ≈ 300k characters is 6× over that cap and O(300k) per reflow, while the visible viewport is only ~3–6k glyphs. Measuring it is what killed the idea: virtualizing the view removed a second, parallel set of glyphs and left the first one untouched, because parsing a note builds every glyph before any view is consulted. Two geometry systems bought a factor of two against a ceiling made of something else. See [[#Status]].

## Cache shape
The layer splits in two: [[TextMeasurer]] is stateless and turns one block's runs into lines, and [[DocumentLayoutCache]] is the stateful half that holds those lines for a whole document and answers every geometry question about it. Per document: `blockTops[]` — the prefix sum of block heights and the gaps between them, so `blockTops[i]` is block `i`'s Y in document space and the final entry is the total scroll extent, which is why it carries one entry more than there are blocks. Per block: its height plus a line table. Line granularity (not block granularity) is deliberate — it is what lets paged mode split a paragraph across a page break and lets hit-testing binary-search inside a block.

The gap between blocks is `DocumentLayout.blockSpacing` rather than a number on the view, for the same reason heading sizes moved there: the cache stacks blocks by it and the view draws them by it, and a value the two disagreed on would put every cached block top further out of step with the drawn one the further down the document you scrolled.

Per-character advances **are** stored, once per block, inside the block's own `BlockLayout`: a float advance and a flag byte (break, picture, tab) per character, about 5 MB on a million-character note. A rewrap — the page or wrap width changing while no text did — then only re-breaks the stored advances instead of looking every glyph up again, which took rewrapping a 1,000-paragraph note from ~8 to ~3 ms a frame. The store checks itself against the runs on every measure (same string, same slice, same atlas, face and size) rather than trusting invalidation, so any change to a run's text or style rebuilds it. Hit-testing a click and placing a caret still re-derive their one line's advances through the same [[TextMeasurer]] call the lines were measured with. See `ClaudeMemory/Decisions/rewrap-advance-cache.md`.

[[TextMeasurer]] breaks a block greedily by default. A document whose `DocumentLayout` sets `OptimalBreaks="true"` — every LaTeX preview, no note — breaks its blocks Knuth-Plass instead (L7a, 2026-10-06): the breaker picks the break set with the lowest total demerits at tolerance 200, and when no set is feasible that block falls back to the greedy loop. A text run laid around a floating picture and a block containing a tab always stay greedy. On a Knuth-Plass line justify may shrink spaces down to a third as well as stretch them, and a line before a display formula is never stretched, in notes too. See `ClaudeMemory/Decisions/latex-editor.md`.

The soft hyphen U+00AD (L7b, 2026-10-06) is a break opportunity with no width of its own: [[TextMeasurer]] gives it a zero advance, and a line that breaks at it ends with a hyphen whose width (`TextLine.hyphen`) is included in the line's width. The greedy loop and Knuth-Plass both take a soft break only when that hyphen fits, and never put one on a block's last character. Knuth-Plass charges TeX plain's costs for hyphenated lines: `HyphenPenalty` 50, `DoubleHyphenDemerits` 10000 for consecutive hyphenated lines and `FinalHyphenDemerits` 5000 when the second-last line is hyphenated. `TextRunControl` draws the hyphen as "-" at a line end and draws nothing for a soft hyphen elsewhere, justify and alignment count the hyphen in the line's visible width, and copying a selection drops it. Notes behave the same way; LaTeX lowering is what inserts soft hyphens into words. See `ClaudeMemory/Decisions/latex-editor.md`.

A line is **not** one run: a paragraph with a bold word mid-sentence puts three runs on one visual line, so a line owns a list of `LineSegment` — `(runIndex, charStart, charCount, width)` — plus its own `width`, `ascent`, `descent` and `top` (Y within the block, so the measurer never sees document coordinates). Segments are cut wherever the run index changes, which means walking a line's advances for hit-testing is a walk across its segments in order.

A remeasure does not throw the old lines away. Each text control hands its own `BlockLayout` back to [[TextMeasurer]], which keeps the old lines aside, clears them and fills them again, so rewrapping a large note at a new width costs no new line objects — on a 1,000-paragraph note that was 3.5 MB a frame. The consequence is that a control's lines are the same objects before and after a remeasure: anything that wants the old geometry has to copy it before layout runs. See `ClaudeMemory/Decisions/large-note-measure-cost.md`.

## Line height
Line boxes follow the CSS model that Obsidian gets from `line-height` and Word from its spacing multiple, rather than the ink of the characters that landed on the line: box height is `fontSize × DocumentLayout.lineHeight`, the font's own ink box is centred in it, and the leftover splits evenly above and below as half-leading, putting the baseline at `halfLeading + fontAscent`. Two lines in one style are therefore the same height whether or not either holds a capital or a descender — measuring per-glyph ink instead made a line grow the moment someone typed a "g". Where a line mixes styles it takes the tallest box on it, which is what CSS and Word both do.

`IGlyphMetrics.GetLineMetrics(fontName)` supplies the font's em-normalized ascent/descent. The production implementation derives them as the tallest ascent and deepest descent across the font's whole glyph set, because the `hhea` ascender/descender that should own this are read by [[Aurora Font|GenerateGlyphAtlas]] and then discarded rather than stored in the `.agd`; swapping that method's body is the entire migration once they are persisted, at the cost of re-baking atlases.

## Text sizing
Text names a role rather than a size: a block carries a `StylingType` — `Text`, `Heading1` through `Heading6`, `Comment`, `Code`, `Quote` — and `DocumentLayout.FontSizeFor(type)` is the one resolver everything calls, so the measurer and the controls drawn from it cannot disagree about how big a heading is. A heading is therefore not a class of its own; `ContentBlock` is the only concrete block, and what used to be `HeadingBlock` is a block that says `StylingType="Heading1"`.

Runs carry the same setting and default to `Inherit`, meaning "whatever my block is". `ContentBlock.ApplyLayout` walks its runs, takes the run's own type where it has one and the block's otherwise, and writes the resolved size into `TextRun.fontSize` — so per-run styling is available without every run having to restate the line's role.

The scheme itself is data, not a table in code: `DocumentLayout` carries a list of `TextStyle` — `(type, fontSize)` — and however many entries exist is the whole scheme. The editor-wide scheme is a settings group, `DocumentSettings`, so it arrives through the [[Settings Registry]] rather than being loaded by the document layer at all: `Data/XML/Settings/DocumentSettings.settings.xml` cascades from the engine's copy, through an application's, to the host's write root, merging **per attribute**, so an app restyles every note it opens by naming one value and inherits the rest. A note then overrides per-document by embedding a `<DocumentLayout>` of its own. An empty style list means inherit, and declaring even one style replaces the whole set rather than merging entry-by-entry against defaults the note's author cannot see. A heading past the last one the scheme defines takes that last one, so an H9 renders as the smallest heading instead of collapsing to body text.

Because the note format omits any attribute left at its default, a run that never stated a size is not pinned to the size it had when it was written — it is whatever the scheme says now, which is what makes editing `DocumentSettings.settings.xml` restyle every existing note.

## Geometry policy
All document geometry resolves on the cache — mouse clicks, caret placement, arrow keys / PageDown / Ctrl-End, selection drag (including auto-scroll past the viewport, where the anchor has no control), find-next, `ScrollIntoView`, pagination. The control tree's only hit-testing job is the app shell's: deciding the click landed on the document editor at all rather than a toolbar or file tree. Inside the editor, glyph / run / block controls are **not** hit-targets; the editor converts the click to document space and asks the cache. One code path for clicks and keyboard alike, and it works for content that has no controls materialized.

## Caret slots
A caret sits between characters, so a line of `n` characters has `n + 1` slots and the boundary ones are shared. Two rules settle who owns them. Horizontally, a click resolves to the nearest slot rather than the character it landed inside — past a character's midpoint belongs to the slot after it — because snapping to the containing cell instead would make it impossible to click the end of a word. Vertically, the offset one past a segment's last character is a real slot on that line when another run follows it there, but at the end of a **wrapped** line that same offset is also the first slot of the line below, and the line below claims it: the caret then sits in front of the wrapped word rather than trailing off the right edge of the line above. Full affinity tracking — where the two are distinct positions the user can toggle between — is not implemented and is not needed until selection rendering makes the difference visible.

## Advance formula & testability
The pen advances by `glyph.advanceWidth * px`, each glyph quad is offset within its pen cell by `leftSideOffset * px` and sized `glyphWidth * px` — all three are em-normalized in [[AuroraFont]]. The legacy [[ShortTextControl]] and `TextEntity` already used this formula, and [[INPUT|TextInputControl]] was corrected onto it too, so pen advance is no longer in dispute; what the flow controls still do differently is wrap **per character**, which the measurer replaces with word-boundary wrapping rather than matching.

Because the measurer needs only per-glyph metrics, it takes a narrow glyph-metrics lookup (char → [[Glyph]], plus the font's line box) rather than a [[Vulkan Control]] or GPU handle — so a test can feed fabricated glyphs with known advances and assert line breaks with no font file, no asset registry and no GPU (the L1 verification vehicle, kept NuGet-free). The overload taking a `ContentBlock` is the one place that reads text off a control, and it exists precisely so the measuring overload beneath it stays plain data: blocks and runs are [[Vulkan Control|VulkanControls]] whose constructor reaches the asset registry, the entity registry and the data pool, so anything taking one cannot run headless.

[[DocumentLayoutCache]] does not inherit that testability — it holds a [[Rich Text Document]], whose blocks *are* controls, and it reaches through them for run text and heading level, so it cannot be exercised without booting. That is a consequence of the P0 decision that the document model is the control tree, not an oversight to fix here: narrowing the cache to plain data would mean a second model beside the one that renders. It is the reason the cache's own verification is deferred to the in-app test/profiling platform rather than done the way the measurer's was.

#### Measure Block (runs, content width, glyph metrics, document layout)
`keep` = every `run` in `runs` matches the run stored at its index last measure   // same string, slice, atlas, face, size
if not `keep`
	while the block's `advances` are shorter than the characters in `runs`
		double their length
`count` = 0
for each `run` in `runs`
	store `run` key, its start = `count`, and its line box   // resolved once per run, not per character
	if `run` is a picture or a formula
		write its characters' advance and flags   // their width follows the wrap width, so always rewritten
	else if `keep`
		skip its characters   // advances and flags are still the ones from last measure
	else
		for each `char` in `run` text
			`advances`[`count`] = advance width of `char` × run font size
			`flags`[`count`] = break after a space or tab, tab for a tab
	`count` += characters in `run`
`lineStart` = 0, `lastBreak` = none, `penX` = 0
for each `c` in the first `count` stored characters
	if `c` is not whitespace and `penX` + `c` advance > `content width` and line is not empty
		`breakAt` = `lastBreak` if set else previous character   // no break opportunity = split mid-word
		emit line from `lineStart` to `breakAt`, stacked under the block's height so far
		`lineStart` = `breakAt` + 1, `lastBreak` = none
		`penX` = total advance of the characters that moved down with the wrapped word
	`penX` += `c` advance
	if `c` is whitespace
		`lastBreak` = `c`
emit final line from `lineStart` to end
`block` cache entry = (`lines`, total height)

#### Emit Line (chars, from, to)
`line` top = block height so far
for each stored `run` holding characters between `from` and `to`
	`segment` = (run index, char index of its first character on the line)
	for each `c` of `run` between `from` and `to`
		accumulate `c` advance into `segment` width and `line` width
	close `segment` into `line`
	`line` ascent / descent = max with `run` line box
block height += `line` height

#### First Line Indent (offset)
`offset` = the block's `firstIndent` × text zoom
the first line starts at `left` += `offset`
the first line's `room` = (`room` or content width) − `offset`   // the same in greedy, Knuth-Plass, empty block and floating-picture paths
`layout.width` counts `line.left`

#### Spacer Run (run)
for each `char` in `run` text
	`advances`[`count`] = `run` space   // the run's fixed advance, in place of the glyph's
	the glyph draws nothing
the run's space is part of its reuse key
typing beside a spacer goes into a text span, never into the spacer
copying turns each spacer character into a space

#### Invalidate Block (index)
[[#Measure Block]] (`block`, content width)
`delta` = new height − old height
shift `blockTops` after `index` by `delta`

#### Hit Test (point in document space)
`block` = binary search `blockTops` for `point` y   // clamps, so a drag off the top or bottom edge still resolves
`line` = binary search `block` line tops for `point` y − block top
`pen` = 0
for each `segment` in `line`
	if `point` x is past `pen` + `segment` width
		`pen` += `segment` width, next `segment`   // skipped on its cached width, no characters measured
	for each `char` in `segment`
		if `point` x < `pen` + half of `char` advance   // nearest slot, not the containing cell
			return (`block` index, `segment` run index, `char` offset)
		`pen` += `char` advance
return the slot after the last `segment`

#### Char To Point (block index, run index, char offset)
for each `line` in `block`
	`x` = 0
	for each `segment` in `line`
		`end is a slot here` = `line` is the block's last, or `segment` is not the line's last
		if `segment` covers (`run index`, `char offset`), counting its end slot only when `end is a slot here`
			`x` += advance widths from `segment` `charStart` up to `char offset`
			return (`x`, `blockTops[block index]` + `line` top, `line` height, `line` baseline)
		`x` += `segment` width
return the end of the block's last `line`   // clamp — a caret always has somewhere to be

## Virtualization
The document view keeps its [[Rich Text Document#^scrollable|ScrollableControl]] base but takes its scroll extent from `blockTops`, not from child measurement. On scroll or resize it binary-searches the visible range ± one viewport of buffer, materializes controls entering the range and releases leaving ones. Read-only runs are presented by a lightweight `TextRunControl` (glyphs + style tint), not the editable [[INPUT|TextInputControl]] — and the unit materialized is the **line segment**, not the block, because a segment cannot wrap by construction and so needs no wrapping code at all: its x, y and width are read straight off the cache.

The deferred-Vulkan-cleanup prerequisite this section used to carry is **done**. Glyph teardown is complete — `DiscardGlyph` destroys rather than detaches, `ProcessDestroys` unregisters and frees the pool row once per tick, and there is no per-control Vulkan buffer left to leak because `ControlData` now lives in the pooled SSBO.

Materialization is split by what invalidates it. Content width and the scroll extent resolve in `Measure`, because a width change rewraps every block and changes what a line or segment index *means* — which is why `SetContentWidth` reports whether it rewrapped, so the view can drop every control it had keyed against the old indices. The visible range resolves in `Arrange`, because scrolling only invalidates arrangement. Materializing inside `Arrange` does invalidate layout again, and it converges rather than oscillating: the frame after a range changes finds the same range and adds nothing.

The buffer of one viewport either side is load-bearing rather than slack. `Destroy()` only enqueues, so a released strip still draws until `ProcessDestroys` runs at the top of the next tick; the buffer is what guarantees the frame it survives is a frame spent a full viewport outside the clip rect. Measured on a 400-block, 22,392-pixel note scrolled end to end, the view holds 57–59 strips and roughly 4,800 glyphs at every position, the `"Controls"` group stays flat across the whole sweep, and memory sawtooths without trending — so the churn neither leaks nor grows. `GlyphControl` pooling is therefore still unbuilt: strips are rebuilt at the buffer edge rather than recycled, and nothing measured yet argues for the extra machinery.

**The view is no longer the binding constraint, and the remaining one is not a drawing problem.** Every character exists as a [[GlyphControl]] the instant a note is *parsed*: blocks and runs are controls, so assigning a run's text runs `SyncGlyphs` at load. Virtualizing the view removed the second, parallel set of glyphs it used to build for itself — a note cost two full copies and now costs the model plus a viewport — but the model's own copy is untouched and is fixed before the view is consulted. On that 400-block note the total sits past the 50,000-slot descriptor array on the strength of the model alone. Making the document model plain data is the precondition for moving it; culling off-screen controls does not substitute, since culled entities keep their pool rows and their slots.

## Paged vs pageless
Landed 2026-09-27, without the cache. A note is always laid out on paper: its `DocumentLayout` carries a `<Page>` (a `PageLayout`) naming `Paged` or `Pageless`, a paper size (A0–A6, B4, B5, Letter, Legal, Tabloid, Executive or Custom in millimetres), orientation, margins and the gap between pages. A note without one uses the editor-wide page from `DocumentSettings`, which is A4 portrait with 1-inch margins. There is no mode that fills the viewport, because a note that takes whatever width it is given would break a later pin board that shows several notes side by side.
Text is measured at the paper width minus the side margins. After every block is measured, `DocumentControl` walks them top to bottom and each block restacks its own measured lines: a line that would cross the bottom of a page's text area moves to the top of the next page's text area, so a paragraph splits across a break. Because the caret, hit-testing, selection and drawing all read those same line tops, they follow the break without knowing pages exist. Pageless is the same pass with a text area that never ends: one page as wide as the paper and as tall as the content.
The pages themselves are plain panels drawn behind the highlights and the text. The whole stack of pages sits one page gap in from the pane's edges, so the first page never touches the top or the left; pages are centred when the pane is wider than the paper, and the editor scrolls sideways when it is narrower. When the note's page asks for page numbers, each panel carries its number centred in its bottom margin; pageless notes never show one. The format bar's page button switches mode, size, orientation and page numbers for the open note, and has two millimetre fields for a custom paper size that take effect on Enter. Every page change is one undo step, and an `.xml` note saves its choice. See `ClaudeMemory/Decisions/document-pages.md`.

#### Paginate (paper)
	top = margin top; text area = paper height − margins (unbounded when pageless); stride = paper height + gap
	y = top
	for each block
		if not the first block: y += the block's space before when it has one, else block spacing   // a table's too; a code run has no gap unless it sets one
		y = push(y, first line's height)            — the whole block moves if its first line would cross
		for each line of the block
			y = push(y, line height)
			line.top = y − block top
			y += line height
		record block top and height
	paged: page count = page y ends on + 1; pageless: one page, height max(paper, y + bottom margin)

#### Push (y, span height)
	band top = the text-area top of the page y falls on
	if y is above it: return band top
	if the span fits before the band's end: return y
	if the span is taller than a whole text area and starts inside it: return y
	return next page's band top

Landed 2026-10-06 (L7d and L7e): a block can break the page before it, a page style puts running heads and feet in the margins, and a footnote is reserved room at the foot of the page its anchor lands on. Footnote and float blocks stay in the block list in source order but are not part of the flow; they are placed from their own lists.

Landed 2026-10-06 (L7f): a float group is placed by LaTeX's `[htbp!H]` rules with LaTeX's default parameters: here in the text, at the top or foot of the current page, held back and placed on a later page, or on a page of floats. A float is handled at its position among the blocks, which is its reference point; a float inside a paragraph is emitted after the paragraph, so its reference point is the paragraph's end. `\clearpage` and the end of the document put out every float still waiting, as pages of floats. A footnote sits above the bottom floats of its page.

#### Break (y)
	band top = the text-area top of the page y falls on
	if y has not left the band top: return y
	return the next page's band top

#### Paginate with footnotes and floats (only when the document has footnote or float blocks)
	plain notes take the Push path; this one adds a `PageSpace` for the pass and, with floats, a `PageFloats`
	for each child, by index so that a page can be rewound
		a footnote block is skipped
		a float group arrives at the current y (Float Arrival below), then the loop moves past the group
		a block or table with a page break moves to Break(y)
			a Clear break first flushes the waiting floats (Flush below)
		for each line of the block
			the line's anchors are the footnote ids it holds
			ask the `PageSpace` whether the line plus its unplaced footnotes fit above the page's reserved foot
			if they do not: the line moves to the next page's top and takes its footnotes with it
			reserve the footnotes on the page the line lands on
	at the end of the pass
		a footnote whose anchor is gone is reserved at the foot of the last page, or one page more if it does not fit
		the waiting floats are flushed
		each footnote is placed at the foot of its page's text area, stacked under a short rule, above the page's bottom floats
			a footnote taller than the text area starts below the last text on that page instead of above it
		the floats are stacked into place (Stack below)
	the incremental "settled" shortcut is off while any footnote or float block exists, so the pass is always full

#### Float Arrival (group, y)
	a float already decided is skipped on a re-run; one set here is placed inline again
	`H` is always here, with `\intextsep` above and below, and never floats; pageless mode sets every float here
	if an earlier float of the same kind is waiting: the float waits
	if the placement allows `h` and the float fits in the room left plus `\intextsep`: place it here
	if the placement allows `t` and the float fits on the current page, within topnumber, topfraction, totalnumber and textfraction, with the text already on the page still fitting under it
		reserve the page's top for it
		rewind: lay the page's text again from the first flow block that reaches that page
			footnotes reserved from that page on are rolled back
			picture floats of the rewound blocks are dropped and registered again
			at most two rewinds per page
	if the placement allows `b` and the float fits below the text so far, within bottomnumber, bottomfraction, totalnumber and textfraction: place it at the foot
	otherwise the float waits
	`!` ignores the counts and fractions, and still needs the float to fit
	on page 1 the top reservation includes the note editor's header

#### Fresh Page (page)
	run once per page, the first time anything lands on it
	try a page of floats from the waiting `p` floats, in order, without passing a failed float of the same kind
		the page must be filled to at least floatpagefraction
	otherwise place the waiting floats at the top or foot in order
	a page of floats is skipped by Push

#### Flush (start page)
	run by a Clear break and at the end of the document
	put out every waiting float as pages of floats, ignoring floatpagefraction
		each page as full as fits, at least one float per page

#### Stack
	for each page of floats
		spread the leftover as `\@fptop`, `\@fpsep` per gap, `\@fpbot` = 1 : 2 per gap : 1 fil, centred for one float
	for each other page
		top floats stack down from the band top, `\floatsep` between them and `\textfloatsep` to the text
		bottom floats stack up from the band foot

#### Number Pages
	for each page panel
		its only child is the page's margin layer, which holds six slot labels and the head, foot and footnote rules
		write the running head and foot from the page's style: slot text with the page number and the page's marks filled in
		the plain page number is the foot-center slot

## Memory budget (100 pages ≈ 300k chars)

| Layer | Cost |
|---|---|
| Model (strings + runs) | < 1 MB |
| Layout cache (line tables + prefix sums) | ~100 KB |
| View (visible ~3–6k glyph controls, pooled) | constant, viewport-sized |
| Font atlases (MTSDF, per font used, lazy) | ~1–4 MB each |

## Status
- **L1 landed and is what runs.** [[TextMeasurer]] (word-boundary wrap, uniform line boxes, per-run segments) is the only wrapper for all text in the engine, and `DocumentLayout` (line height, block spacing, the styling-type scheme) is a settings group. Geometry is verified by caret round-trip on real font metrics — every caret slot in the sample note returns the identical point through `CaretAt → OffsetAt → CaretAt`.
- **L2 is dropped, not pending.** It was built and reverted on 2026-08-07. `DocumentLayoutCache`, `TextRunControl` and `DocumentCanvasControl` are deleted; there is one layout path for all text and the document is a plain control tree. The measurements it produced were real and are kept above as evidence about that design.
- **What replaced it is engine-wide, not document-local.** The UI splits into **data and visualization**: the parent/child tree becomes pool data, a control stays one object per element presenting its row, and it all rides the existing `UIControls` pool. Layout and hit-test become flat forward loops in DFS order rather than dispatch down an object graph. It is deliberately **not** a fix for the glyph count — one control per element leaves that where it is, and that ceiling stays accepted with the run-holds-its-text escape hatch as the only lever on it. Nothing view-side reaches it either: a culled control keeps its entity and its pool row.
- **Sequenced after Thorium and the profiler.** The UI ships as it is, Thorium reaches its first version, the test/profiling platform comes up on that UI, and only then is the engine's UI redone against the split — with the profiler available to say what the numbers are instead of inferring them from control counts. Design, open questions and the tension with [[ecs-rework-data-pools]]'s "the UI tree stays OO": `DOCUMENTATION/ClaudeMemory/Decisions/ui-data-control-split.md`.
- Paged mode (L3) is unaffected — it paginates measured lines and never needed the cache to be a separate object. Phase order: `DOCUMENTATION/ClaudeMemory/Context/thorium-editor-architecture.md`.
