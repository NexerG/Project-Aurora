---
Status: Planned
tags:
  - Engine
  - d_UI
  - d_Data
Class:
  - "[[DocumentLayoutEngine]]"
Type:
  - Public
---
## Description
The geometry layer between the [[Rich Text Document]] model and the document view. It measures every block from font metrics alone — no controls, no GPU — into a compact per-block line cache, and every geometry question about the document is answered from that cache. This is the middle of a three-layer split: model (always fully loaded, < 1 MB at 100 pages) → layout cache (~100 KB at 100 pages) → virtualized view (controls exist only for the visible viewport).

The split exists because view materialization, not data, is the memory problem: each character today is a [[GlyphControl]] — a full [[Vulkan Control]] entity with its own `ControlData` GPU struct, storage buffer and slot in the [[UI Rasterizer Module|UIModule]] 50,000-cap descriptor array. 100 pages ≈ 300k characters would be 6× over that cap and O(300k) per reflow, while the visible viewport is only ~3–6k glyphs. So the cache holds geometry for everything, and controls are disposable presenters placed *from* cache coordinates — the cache and the visuals cannot disagree.

## Cache shape
Per document: `blockTops[]` — prefix sum of block heights, so `blockTops[i]` is block `i`'s Y in document space and the last entry is the total scroll extent. Per block: its height plus a line table. Line granularity (not block granularity) is deliberate — it is what lets paged mode split a paragraph across a page break and lets hit-testing binary-search inside a block.

A line is **not** one run: a paragraph with a bold word mid-sentence puts three runs on one visual line, so a line owns a list of `LineSegment` — `(runIndex, charStart, charCount, width)` — plus its own `width`, `ascent`, `descent` and `top` (Y within the block, so the measurer never sees document coordinates). Segments are cut wherever the run index changes, which means walking a line's advances for hit-testing is a walk across its segments in order.

## Line height
Line boxes follow the CSS model that Obsidian gets from `line-height` and Word from its spacing multiple, rather than the ink of the characters that landed on the line: box height is `fontSize × DocumentLayout.lineHeight`, the font's own ink box is centred in it, and the leftover splits evenly above and below as half-leading, putting the baseline at `halfLeading + fontAscent`. Two lines in one style are therefore the same height whether or not either holds a capital or a descender — measuring per-glyph ink instead made a line grow the moment someone typed a "g". Where a line mixes styles it takes the tallest box on it, which is what CSS and Word both do.

`IGlyphMetrics.GetLineMetrics(fontName)` supplies the font's em-normalized ascent/descent. The production implementation derives them as the tallest ascent and deepest descent across the font's whole glyph set, because the `hhea` ascender/descender that should own this are read by [[Aurora Font|GenerateGlyphAtlas]] and then discarded rather than stored in the `.agd`; swapping that method's body is the entire migration once they are persisted, at the cost of re-baking atlases.

## Text sizing
A block's font size is a property of the block's role, not of the runs inside it — `TextRun.fontSize` is unused until per-run sizing lands. `DocumentLayout.FontSizeFor(block)` resolves it, and both the layout cache and [[Document Editor Control|DocumentEditorControl]] call it, so cached geometry and the controls drawn from it cannot disagree about how big a heading is.

Heading levels are data, not a table in code: `DocumentLayout` carries a list of `HeadingStyle` — `(level, fontSize)` — and however many entries exist is how many levels exist. Resolution cascades over two tiers. `Data/XML/Documents/DocumentStyles.xml` holds the editor-wide scheme, and because the [[Virtual File System]] resolves an application's copy ahead of the engine's, an app restyles every note it opens without touching engine data; a note then overrides per-document by embedding a `<DocumentLayout>` of its own. An empty style list means inherit, and declaring even one style replaces the whole set rather than merging level-by-level against defaults the note's author cannot see. A heading deeper than the scheme defines clamps to the nearest defined level, so an H9 renders as the smallest heading instead of collapsing to body text.

The scalar settings (`lineHeight`, `paragraphFontSize`) do **not** cascade yet — they fall back to code defaults matching the shipped file, because inheriting them needs "was this attribute present in the XML" tracking that the reflection parser does not carry.

## Geometry policy
All document geometry resolves on the cache — mouse clicks, caret placement, arrow keys / PageDown / Ctrl-End, selection drag (including auto-scroll past the viewport, where the anchor has no control), find-next, `ScrollIntoView`, pagination. The control tree's only hit-testing job is the app shell's: deciding the click landed on the document editor at all rather than a toolbar or file tree. Inside the editor, glyph / run / block controls are **not** hit-targets; the editor converts the click to document space and asks the cache. One code path for clicks and keyboard alike, and it works for content that has no controls materialized.

## Advance formula & testability
The pen advances by `glyph.advanceWidth * px`, each glyph quad is offset within its pen cell by `leftSideOffset * px` and sized `glyphWidth * px` — all three are em-normalized in [[AuroraFont]]. The legacy [[ShortTextControl]] and `TextEntity` already used this formula, and [[INPUT|TextInputControl]] was corrected onto it too, so pen advance is no longer in dispute; what the flow controls still do differently is wrap **per character**, which the measurer replaces with word-boundary wrapping rather than matching.

Because the measurer needs only per-glyph metrics, it takes a narrow glyph-metrics lookup (char → [[Glyph]], plus the font's line box) rather than a [[Vulkan Control]] or GPU handle — so a test can feed fabricated glyphs with known advances and assert line breaks with no font file, no asset registry and no GPU (the L1 verification vehicle, kept NuGet-free). The overload taking a `ContentBlock` is the one place that reads text off a control, and it exists precisely so the measuring overload beneath it stays plain data: blocks and runs are [[Vulkan Control|VulkanControls]] whose constructor reaches the asset registry, the entity registry and the data pool, so anything taking one cannot run headless.

#### Measure Block (runs, content width, glyph metrics, document layout)
`chars` = empty
for each `run` in `runs`
	`box` = line box of `run` style   // resolved once per run, not per character
	for each `char` in `run` text
		append (`run` index, `char` index, advance width of `char` × run font size, `box`) to `chars`
`lineStart` = 0, `lastBreak` = none, `penX` = 0
for each `c` in `chars`
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
`segment` = (run index of `from`, char index of `from`)
for each `c` from `from` to `to`
	if `c` run index ≠ `segment` run index
		close `segment` into `line`, start a new one at `c`
	accumulate `c` advance into `segment` width and `line` width
	`line` ascent / descent = max with `c` line box
close final `segment` into `line`
block height += `line` height

#### Invalidate Block (index)
[[#Measure Block]] (`block`, content width)
`delta` = new height − old height
shift `blockTops` after `index` by `delta`

#### Hit Test (point in document space)
`block` = binary search `blockTops` for `point` y
`line` = binary search `block` line tops for `point` y − block top
`char` = walk advance widths of `line` from `charStart` until accumulated width > `point` x
return (`block` index, `line` run index, `char` offset)

#### Char To Point (block index, run index, char offset)
`line` = line of `block` containing (`run index`, `char offset`)
`x` = sum of advance widths from `line` `charStart` to `char offset`
return (`x`, `blockTops[block index]` + `line` top, `line` baseline)   // caret + ScrollIntoView

## Virtualization
The document view keeps its [[Rich Text Document#^scrollable|ScrollableControl]] base but takes its scroll extent from `blockTops`, not from child measurement. On scroll or resize it binary-searches the visible block range ± one viewport of buffer, materializes controls for blocks entering the range and releases leaving ones to a pool. Read-only runs are presented by a lightweight `TextRunControl` (glyphs + style tint), not the editable [[INPUT|TextInputControl]]. Prerequisite: the deferred-Vulkan-cleanup TODOs in [[INPUT|TextControl]] (`SyncGlyphs` / `RemoveGlyph`) must be finished and [[GlyphControl]]s pooled via the [[UI Rasterizer Module|UIModule]] deferred-deletion queue, because scrolling churns controls constantly.

## Paged vs pageless
A mode on the engine — the [[Rich Text Document]] model never knows about pages. Pageless: one column at min(viewport, max content width), blocks stacked, extent = `blockTops` last entry. Paged: a paginator pass deals cached lines onto pages of fixed content height (so paragraphs split across breaks) and the view draws page-background panels with gaps between them.

## Memory budget (100 pages ≈ 300k chars)

| Layer | Cost |
|---|---|
| Model (strings + runs) | < 1 MB |
| Layout cache (line tables + prefix sums) | ~100 KB |
| View (visible ~3–6k glyph controls, pooled) | constant, viewport-sized |
| Font atlases (MTSDF, per font used, lazy) | ~1–4 MB each |

## Status
- **L1 part done.** `TextMeasurer` (word-boundary wrap, uniform line boxes, per-run segments) and `DocumentLayout` (line height, text sizing, heading style cascade) exist and are verified against fabricated metrics. The per-block line cache and the prefix-summed `blockTops[]` with per-block invalidation are **not** built yet, so nothing calls the measurer — it is deliberately additive, so the cache can be raised alongside what already renders and compared against it before the view trusts it.
- L2 (virtualized view + `TextRunControl` + glyph GPU cleanup) precedes the P3 edit session so caret/hit-test math is written once against the cache. Paged mode is L3. Phase order: `DOCUMENTATION/ClaudeMemory/Context/periodic-editor-architecture.md`.
