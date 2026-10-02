# Decision — a note is laid out on paper: paged or pageless, never viewport-wide

**Date:** 2026-09-27
**Scope:** `ArctisAurora.Core.UI` — `PageLayout`, `PageMode`, `PageSize`, `DocumentLayout.page`/`Page`, `PageBands`, `DocumentControl` (region `pages`), `TextRunControl.Paginate`, `DocumentEditorControl.SetPage`/`Page`, `DocumentToolbarControl.OpenPage`, `DocumentXml.ReadLayout`/`ToXml`; `AuroraEngine/Data/XML/Settings/DocumentSettings.settings.xml`

## What changed
- `PageLayout` (`<Page>`, XSD type `Page`): `Mode` (`Paged`|`Pageless`), `Size` (A0–A6, B4, B5, Letter, Legal, Tabloid, Executive, Custom), `Landscape`, `Width`/`Height` mm (Custom only), `MarginTop/Bottom/Left/Right` mm (default 25.4), `Gap` px (default 16). `SizePx()` = mm × 96/25.4, orientation applied.
- `DocumentLayout.page` is nullable; `Page` resolves `page ?? Defaults.page ?? new PageLayout()`. Engine default is `<UI:Page Mode="Paged" Size="A4"/>` → A4 portrait, 1-inch margins.
- `DocumentControl` measures every block at page width minus margins, then `Paginate` walks blocks in order: block spacing, push the first line past a break (the whole block moves), then `TextRunControl.Paginate` rewrites each line's `top`, pushing any line that crosses a text area's bottom to the next page's top. Stores `blockTops`/`blockHeights`; `ArrangeCore` places blocks from them.
- Page panels (`PanelControl`, `Surface`, 1px `Line` edge, not hit-testable) sit at the **head** of `children`; highlights are inserted after them (`pages.Count + highlights.Count`), text after that. Extra panels are arranged to zero, like highlights.
- Pageless = one panel of paper width, height `max(paper, content + bottom margin)`; `Paginate` runs with an unbounded text area so line tops are still rewritten.
- The page is centred when the viewport is wider; `DocumentEditorControl` scrolls `Both`, so a wider page scrolls horizontally.
- Measure depends on the paper, not the offer (2026-09-29): `MeasureCore` skips blocks, header, `Paginate` and `EnsurePages` unless the document is measure-dirty or `page.SizePx() * zoom` differs from `measuredPaper` (off under `LayoutEngine.NoSkip`). A viewport resize or scrollbar change stops at the page; arrange still translates every block. 1M-char resize: `Step.Main.Layout` p95 2.54–2.84 → 1.34–1.46 ms (Release+PROFILE, 3 runs each).
- Pagination is incremental (2026-09-29): `PageBands.Push` is pure in `y`, so a block that was not re-measured and lands at its old top paginates exactly as before, and so does everything after it. `MeasureCore` records `from`/`to` — first and last block index that was `Remeasured` (layout-engine flag, so edits, zoom and text-width changes all count) or is not the control at that index last pass (inserts, deletes). `Paginate(paper, from, to)` keeps `[0, from)`, overwrites `blockTops`/`blockHeights`/`blockControls` in place, stops at the first index past `to` whose top is unchanged (the end is then the last block's top + height), else trims the tail. Full pass when `paginatedBands` (top margin, text height, stride — every page setting and zoom) or the header height changed. Bands compared field by field: default struct `Equals` boxes each float.
  - Why `Remeasured` after `Measure`, not `isMeasureDirty` before: a left/right margin change re-measures every block through the offer without dirtying any. Why `to`: a style or paste can re-measure blocks 5 and 500 in one pass; stopping at 6 would leave 500's fresh lines unpaginated. Rejected: skip-don't-stop (walks all blocks per keystroke), a `SetPage` flag (two classes, misses the width case).
  - Typing still paginates the whole tail on a keystroke that adds or removes a line — every block below shifts. `VerifyLayout` checks block rects, not the line tops inside a block.
  - 1M-char typing (frames 40–149, Release+PROFILE, 3 runs each): `Document.Paginate` p50 0.62 → 0.03 ms, p95 0.88–0.89 → 0.05–0.08; `Step.Main.Layout` p50 1.21–1.24 → 0.62–0.70, p95 1.70–1.73 → 1.06–1.24.
- Format bar: a caption button (`A4`, `A4 L`, `Pageless`) opening Paged/Pageless, every size except Custom, and a Landscape/Portrait toggle. Each entry edits a `Clone()` and calls `SetPage`.
- `.xml` notes write `<Page>` inside `<DocumentLayout>` when the note has its own; `.md`/`.txt` can't store it and always show the editor default.

## Why these choices

**No viewport-wide ("fluid") mode.**
User call: a later pin board shows several notes at once, and a note that fills whatever width it's given breaks that. A note always has a paper width.

**Pagination rewrites `TextLine.top` in place instead of adding a second geometry.**
Every line consumer (`Emit`, `CaretAt`, `IndexAt`/`LineAt`, `CaretAtPoint`, selection highlights, the list marker) already reads `line.top`, so moving the tops made all of them page-aware with no other change. The pass is idempotent: it restacks from line heights every document measure, so a block the measurer skipped still gets correct tops. Pageless runs the same pass for the same reason; otherwise switching Paged→Pageless at the same width would keep the old breaks, because no block re-measures.

**Paper sizes are an enum with a table in code, not an XML document.**
They are ISO/ANSI constants, the same kind of thing as `TextStyleType`. An XML file would need a loader, a bootstrap step and a copy per app, all for values that never change. Custom covers everything else.

**A nullable `page` means "inherit the editor's", following the `textStyles` rule.**
A note's `DocumentLayout` scalars do not cascade from `Defaults` (a fresh instance's C# defaults win), so a scalar `Mode` on the note could never pick up the editor-wide setting. Declaring `<Page>` replaces the whole group.

**Panels are siblings of the blocks, not wrappers around them.**
`BlockControl`'s checkbox callback walks `parent.parent` to find the editor. A page layer between them would break that, and paint order already comes from the child list.

## Known gaps
- ~~The first page sits flush against the top/left of the viewport~~ — since 2026-10-02 the document is inset by one `PageGap` × zoom on every side (desired size grows by two gaps; `ArrangeCore` and `CollectChildren` start a gap in). The page fill (`Surface`) is still only slightly darker than the gap.
- No zoom: A4 is 794px wide, so a narrow pane scrolls horizontally, and 18px body text is large for A4.
- A click in the gap between pages resolves to the nearest line **above** the gap (`LineAt` takes the last line whose top ≤ y).
- No headers/footers, widow/orphan or keep-with-next rules. Headers/footers were planned and deferred (user, 2026-10-02): the open fork is plain one-line text with `{page}`/`{pages}` from page-menu fields, against rich text edited in the margin; agreed if built: a footer pushes the page number to the right corner. A line taller than a page's text area overflows rather than being pushed. Page numbers landed 2026-10-02: `PageLayout.pageNumbers` (`PageNumbers`), a `LabelControl` child of each sheet placed by `verticalPosition = 1` in the bottom margin, written by `DocumentControl.NumberPages` during measure; Paged only; toggled from the page menu. `.md` frontmatter does not carry it.
- ~~Page changes are not undoable. Custom size is XML-only~~ — `SetPage` records a `PageEdit` (before/after `layout.page`, null = the editor's) in a "Page" step; `ApplyPage` is the unrecorded write. The page menu hosts Custom width × height mm fields through `ContextMenuContent`; Enter calls `DocumentToolbarControl.SetCustomSize`. The fields themselves are NOT GUI-verified. Test: `TextInput.PageNumbersAndUndo`
- Caret Up/Down across a break is not GUI-verified (extended keys can't be scripted).

Related: [[text-layout-one-measurer]], [[document-format-bar]], [[note-file-formats]], [[document-selection]]
