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

Per-character advances are deliberately **not** stored. One float per character is megabytes at 100 pages against a cache budget of ~100 KB, and the operations that need them — hit-testing a click, placing a caret — only ever need the single line they landed on, so they re-derive that line's advances through the same [[TextMeasurer]] call the lines were measured with.

A line is **not** one run: a paragraph with a bold word mid-sentence puts three runs on one visual line, so a line owns a list of `LineSegment` — `(runIndex, charStart, charCount, width)` — plus its own `width`, `ascent`, `descent` and `top` (Y within the block, so the measurer never sees document coordinates). Segments are cut wherever the run index changes, which means walking a line's advances for hit-testing is a walk across its segments in order.

## Line height
Line boxes follow the CSS model that Obsidian gets from `line-height` and Word from its spacing multiple, rather than the ink of the characters that landed on the line: box height is `fontSize × DocumentLayout.lineHeight`, the font's own ink box is centred in it, and the leftover splits evenly above and below as half-leading, putting the baseline at `halfLeading + fontAscent`. Two lines in one style are therefore the same height whether or not either holds a capital or a descender — measuring per-glyph ink instead made a line grow the moment someone typed a "g". Where a line mixes styles it takes the tallest box on it, which is what CSS and Word both do.

`IGlyphMetrics.GetLineMetrics(fontName)` supplies the font's em-normalized ascent/descent. The production implementation derives them as the tallest ascent and deepest descent across the font's whole glyph set, because the `hhea` ascender/descender that should own this are read by [[Aurora Font|GenerateGlyphAtlas]] and then discarded rather than stored in the `.agd`; swapping that method's body is the entire migration once they are persisted, at the cost of re-baking atlases.

## Text sizing
Text names a role rather than a size: a block carries a `StylingType` — `Text`, `Heading1` through `Heading6`, `Comment`, `Code`, `Quote` — and `DocumentLayout.FontSizeFor(type)` is the one resolver everything calls, so the measurer and the controls drawn from it cannot disagree about how big a heading is. A heading is therefore not a class of its own; `ContentBlock` is the only concrete block, and what used to be `HeadingBlock` is a block that says `StylingType="Heading1"`.

Runs carry the same setting and default to `Inherit`, meaning "whatever my block is". `ContentBlock.ApplyLayout` walks its runs, takes the run's own type where it has one and the block's otherwise, and writes the resolved size into `TextRun.fontSize` — so per-run styling is available without every run having to restate the line's role.

The scheme itself is data, not a table in code: `DocumentLayout` carries a list of `TextStyle` — `(type, fontSize)` — and however many entries exist is the whole scheme. The editor-wide scheme is a settings group, `DocumentSettings`, so it arrives through the [[Settings Registry]] rather than being loaded by the document layer at all: `Data/XML/Settings/DocumentSettings.xml` cascades from the engine's copy, through an application's, to the host's write root, merging **per attribute**, so an app restyles every note it opens by naming one value and inherits the rest. A note then overrides per-document by embedding a `<DocumentLayout>` of its own. An empty style list means inherit, and declaring even one style replaces the whole set rather than merging entry-by-entry against defaults the note's author cannot see. A heading past the last one the scheme defines takes that last one, so an H9 renders as the smallest heading instead of collapsing to body text.

Because the note format omits any attribute left at its default, a run that never stated a size is not pinned to the size it had when it was written — it is whatever the scheme says now, which is what makes editing `DocumentSettings.xml` restyle every existing note.

## Geometry policy
All document geometry resolves on the cache — mouse clicks, caret placement, arrow keys / PageDown / Ctrl-End, selection drag (including auto-scroll past the viewport, where the anchor has no control), find-next, `ScrollIntoView`, pagination. The control tree's only hit-testing job is the app shell's: deciding the click landed on the document editor at all rather than a toolbar or file tree. Inside the editor, glyph / run / block controls are **not** hit-targets; the editor converts the click to document space and asks the cache. One code path for clicks and keyboard alike, and it works for content that has no controls materialized.

## Caret slots
A caret sits between characters, so a line of `n` characters has `n + 1` slots and the boundary ones are shared. Two rules settle who owns them. Horizontally, a click resolves to the nearest slot rather than the character it landed inside — past a character's midpoint belongs to the slot after it — because snapping to the containing cell instead would make it impossible to click the end of a word. Vertically, the offset one past a segment's last character is a real slot on that line when another run follows it there, but at the end of a **wrapped** line that same offset is also the first slot of the line below, and the line below claims it: the caret then sits in front of the wrapped word rather than trailing off the right edge of the line above. Full affinity tracking — where the two are distinct positions the user can toggle between — is not implemented and is not needed until selection rendering makes the difference visible.

## Advance formula & testability
The pen advances by `glyph.advanceWidth * px`, each glyph quad is offset within its pen cell by `leftSideOffset * px` and sized `glyphWidth * px` — all three are em-normalized in [[AuroraFont]]. The legacy [[ShortTextControl]] and `TextEntity` already used this formula, and [[INPUT|TextInputControl]] was corrected onto it too, so pen advance is no longer in dispute; what the flow controls still do differently is wrap **per character**, which the measurer replaces with word-boundary wrapping rather than matching.

Because the measurer needs only per-glyph metrics, it takes a narrow glyph-metrics lookup (char → [[Glyph]], plus the font's line box) rather than a [[Vulkan Control]] or GPU handle — so a test can feed fabricated glyphs with known advances and assert line breaks with no font file, no asset registry and no GPU (the L1 verification vehicle, kept NuGet-free). The overload taking a `ContentBlock` is the one place that reads text off a control, and it exists precisely so the measuring overload beneath it stays plain data: blocks and runs are [[Vulkan Control|VulkanControls]] whose constructor reaches the asset registry, the entity registry and the data pool, so anything taking one cannot run headless.

[[DocumentLayoutCache]] does not inherit that testability — it holds a [[Rich Text Document]], whose blocks *are* controls, and it reaches through them for run text and heading level, so it cannot be exercised without booting. That is a consequence of the P0 decision that the document model is the control tree, not an oversight to fix here: narrowing the cache to plain data would mean a second model beside the one that renders. It is the reason the cache's own verification is deferred to the in-app test/profiling platform rather than done the way the measurer's was.

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
A mode on the engine — the [[Rich Text Document]] model never knows about pages. Pageless: one column at min(viewport, max content width), blocks stacked, extent = `blockTops` last entry. Paged: a paginator pass deals cached lines onto pages of fixed content height (so paragraphs split across breaks) and the view draws page-background panels with gaps between them.

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
- **Sequenced after Periodic and the profiler.** The UI ships as it is, Periodic reaches its first version, the test/profiling platform comes up on that UI, and only then is the engine's UI redone against the split — with the profiler available to say what the numbers are instead of inferring them from control counts. Design, open questions and the tension with [[ecs-rework-data-pools]]'s "the UI tree stays OO": `DOCUMENTATION/ClaudeMemory/Decisions/ui-data-control-split.md`.
- Paged mode (L3) is unaffected — it paginates measured lines and never needed the cache to be a separate object. Phase order: `DOCUMENTATION/ClaudeMemory/Context/periodic-editor-architecture.md`.
