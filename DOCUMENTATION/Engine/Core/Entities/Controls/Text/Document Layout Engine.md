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
Per document: `blockTops[]` — prefix sum of block heights, so `blockTops[i]` is block `i`'s Y in document space and the last entry is the total scroll extent. Per block: its height plus a line table; each line is `(runIndex, charStart, charCount, width, height, baseline)`. Line granularity (not block granularity) is deliberate — it is what lets paged mode split a paragraph across a page break and lets hit-testing binary-search inside a block.

## Geometry policy
All document geometry resolves on the cache — mouse clicks, caret placement, arrow keys / PageDown / Ctrl-End, selection drag (including auto-scroll past the viewport, where the anchor has no control), find-next, `ScrollIntoView`, pagination. The control tree's only hit-testing job is the app shell's: deciding the click landed on the document editor at all rather than a toolbar or file tree. Inside the editor, glyph / run / block controls are **not** hit-targets; the editor converts the click to document space and asks the cache. One code path for clicks and keyboard alike, and it works for content that has no controls materialized.

## Advance formula & testability
The pen advances by `glyph.advanceWidth * px`, each glyph quad is offset within its pen cell by `leftSideOffset * px` and sized `glyphWidth * px` — all three are em-normalized in [[AuroraFont]]. The legacy [[ShortTextControl]] and `TextEntity` already use this formula; the current flow controls [[INPUT|TextInputControl]] and [[TextBlockControl]] instead advance by ink bounding-box width and wrap per character, so they are reconciled onto the measurer in L2, not matched. Because the measurer needs only per-glyph metrics, it takes a narrow glyph-metrics lookup (char → [[Glyph]]) rather than a [[Vulkan Control]] or GPU handle — so a test can feed a fabricated `Glyph[]` with known advances and assert line breaks with no font file and no GPU (the L1 verification vehicle, kept NuGet-free).

#### Measure Block (block, content width)
`lines` = empty
for each `run` in `block`
	for each `char` in `run` text
		`advance` = [[AuroraFont|atlas metadata]] advance width of `char` × run font size
		if line width + `advance` > `content width`
			break line at last word boundary, start new line
		accumulate `advance` into current line
`block` cache entry = (`lines`, total height)

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
- Planned — L1 (measurer + cache, pageless) and L2 (virtualized view + `TextRunControl` + glyph GPU cleanup) precede the P3 edit session so caret/hit-test math is written once against the cache. Paged mode is L3. Phase order: `DOCUMENTATION/ClaudeMemory/Context/periodic-editor-architecture.md`.
