# Decision — a note is laid out on paper: paged or pageless, never viewport-wide

**Date:** 2026-09-27
**Scope:** `ArctisAurora.Core.UI` — `PageLayout`, `PageMode`, `PageSize`, `DocumentLayout.page`/`Page`, `PageBands`, `DocumentControl` (region `pages`), `TextRunControl.Paginate`, `DocumentEditorControl.SetPage`/`Page`, `DocumentToolbarControl.OpenPage`, `DocumentXml.ReadLayout`/`ToXml`; L7d/L7e: `PageStyle`, `RunningSlot`, `SlotPlace`, `PageBreak`, `PageInsert`, `PageSpace`, `PageMargins`, `DocumentControl.PageAt`/`RunningHeads`/`PlaceFootnotes`, `PageBands.Break`; L7f: `PageFloats`, `DocumentControl.MeasureFloats`/`ReserveOrphanNotes`; `AuroraEngine/Data/XML/Settings/DocumentSettings.settings.xml`

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
- `DocumentControl.Paginate` (L7c): the gap above a block or table is its `spaceBefore` when set, else `blockSpacing` (a code run has no gap unless `spaceBefore` is set); never above the first block. See [[latex-editor]], [[markdown-blocks-and-alignment]].
- Page breaks (L7d, 2026-10-06): `<Block PageBreak>` and `<Table PageBreak>` (enum `PageBreak`: None, Page, Clear; Clear flushes the waiting floats before it breaks, L7f). `Paginate` moves the block to `PageBands.Break(y)` — the top of the next text area, or this one if y has not left its top.
- Running heads and feet (L7d): `<Page Style>` names a `PageStyle` among the `<PageStyle>` children of `<Page>` (`PageLayout.StyleNamed`). A style has `HeadRule`/`FootRule` px, `HeadSep` mm (default 8.8), `FootSkip` mm (default 10.5) and `<Slot>` children; a `RunningSlot` has `Place` (`SlotPlace`: HeadLeft, HeadCenter, HeadRight, FootLeft, FootCenter, FootRight), `Text` with `{page}`/`{leftmark}`/`{rightmark}`, `FontName`, `FontSize` px, `Bold`, `Italic`. A block carries `PageStyle` (`\thispagestyle`), `MarkLeft`, `MarkRight` (`\markboth`/`\markright`); `RunningHeads()` resolves them per page, TeX-style: left mark = the page's last, right mark = its first, carried forward when a page has none.
- `NumberPages` writes the running heads, the plain page number and the footnote rule; private nested `PageMargins : ContainerControl` is each sheet's only child (six slot labels + head/foot/footnote rules; replaces the single number `LabelControl`). `DocumentControl.PageAt(Control, offset)` returns the 1-based page of a character (null until placed); `DocumentEditorControl.PageAt(blockIndex, offset)`.
- Footnotes (L7e): `<Footnote Id>` is a group of blocks in the note, its anchor a run with `Note="id"` (`Run.note`, `StyleSpan.note`); `PageLayout.footnoteSkip` (`FootnoteSkip` mm, default 3.2). `DocumentXml.ReadInsert` flattens the group (and `<Float Kind Placement>`) into `document.blocks`, each block carrying one `PageInsert`; `ToXml` regroups consecutive blocks sharing one.
- Footnotes stay in `children`/`document.blocks` in source order but are skipped by the flow loops (`Paginate`, `ArrangeCore`'s block loop, `MeasureCore`'s from/to count, `CollectChildren`'s binary search) and placed from `insertControls` (`MeasureFootnotes`, `PlaceFootnotes`, `IsInsert`); `blockTops` stays monotonic. When the document has inserts, `Paginate` makes a `PageSpace` (footnote heights by id, `Bottom(page)`, `TextEnd(page)`, `PushLine`, `Reserve`): a line whose footnote does not fit moves to the next page with it, and the footnotes are stacked at the foot of the page the anchor's line lands on, under a short rule. Plain notes take the unchanged `PageBands.Push` path.
- A footnote whose anchor is gone (orphan) is placed at the foot of the last page, or one page more if it does not fit; with duplicate anchors the first wins. Edit rules: [[latex-editor]] L7-F1.
- Floats (L7f, 2026-10-06): a `<Float Kind Placement>` group is placed by LaTeX's `[htbp!H]` rules and default parameters — here, top or foot of the current page, held back to a later page, or on a page of floats. `Paginate` visits `children` by index, handles a float group at its position (the reference point) and can rewind: a `t` float met mid-page reserves the page's top and the page's text is laid again, at most 2 rewinds per page. `PageBreak.Clear` and the end of the document flush every waiting float as pages of floats. Details of the rules and types: [[latex-editor]] L7f.
- `PageSpace` (L7f): per-page `Top(page)` reservation that `Push` starts the text area below; `Bottom(page)` is footnotes plus floats, `Notes(page)` footnotes only; float pages (`IsFloatPage`, `MarkFloatPage`) that `Push` skips; `Open(page)` runs an `opening` callback once per page when anything first lands there; `SetFloats`, `Rollback(page)` (takes back footnotes reserved from that page on), `Pages`. `PageFloats` (internal, same file) holds the groups (`Where`: None, Waiting, Here, Top, Bottom, FloatPage), `Arrive`, `Flush`, `Stack`. At the end of the pass: orphan footnotes are reserved (`ReserveOrphanNotes`), waiting floats flushed, footnotes placed (`PlaceFootnotes` only stacks and puts them above the page's bottom floats), then `PageFloats.Stack` fills `insertControls`/`insertTops`/`insertHeights`; `MeasureFloats` measures the groups.
- `PageLayout.floatSep`, `textFloatSep`, `inTextSep` (mm; XML `FloatSep`, `TextFloatSep`, `InTextSep`; defaults 4.22 / 7.03 / 4.22 mm, the 10pt values), written per class size by LaTeX lowering.
- Editor header on page 1 (PENDING user confirmation): `DocumentControl.header`, the properties expander on `.md`/`.xml` notes, sits above page 1's text, so `PageFloats` takes `header`, adds it to page 1's top reservation and `TopFits` counts it; top floats on page 1 go below it.

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

**Running-head slots are one style per slot, plain text with fields (L7-F2, user).**
Rejected: runs or mixed styles in a slot.

**The incremental "settled" paginate shortcut is off whenever the document has footnote or float blocks (L7-F4).**
A full pass; only LaTeX output has them and the preview is rebuilt each recompile anyway. Rejected: teaching the shortcut to compare page reservations and an empty float queue.

**Footnotes sit at the foot of the page's text area (L7-F5), above any bottom floats since L7f (L7f-F4).**
Rejected: directly under the text, which is what LaTeX does under `\raggedbottom` (footmisc `[bottom]` is the LaTeX fix). A footnote taller than the text area starts below the last text on its page (`PageSpace.TextEnd`) rather than rising above it — a 405 px footnote on a 367 px text area overlapped the previous page before this.

**A `t` float met mid-page goes to the current page's top, and `Paginate` rewinds (L7f-F1, user, recommended).**
Rejected: never the current page's top, always defer to the next page. Simpler, but it differs from LaTeX whenever a `[t]` float appears mid-page. Bounded by topnumber: at most 2 rewinds per page.

**A float inside a paragraph leaves the paragraph whole; its `<Float>` follows the paragraph (L7f-F2, user, recommended).**
Rejected: keeping the pre-L7f split of the paragraph at the float; an anchor run in the text for an exact reference line (needs a new `Run` attribute). Cost: the reference point is the paragraph's end, not its line.

**Float separations are `<Page>` attributes (L7f-F3) and footnotes sit above bottom floats (L7f-F4).**
The 12pt class has 14 pt separations, so fixed 10pt constants in code were rejected; the footmisc `[bottom]` order was rejected for LaTeX2e's default.

## Known gaps
- ~~The first page sits flush against the top/left of the viewport~~ — since 2026-10-02 the document is inset by one `PageGap` × zoom on every side (desired size grows by two gaps; `ArrangeCore` and `CollectChildren` start a gap in). The page fill (`Surface`) is still only slightly darker than the gap.
- No zoom: A4 is 794px wide, so a narrow pane scrolls horizontally, and 18px body text is large for A4.
- A click in the gap between pages resolves to the nearest line **above** the gap (`LineAt` takes the last line whose top ≤ y).
- No widow/orphan or keep-with-next rules. A line taller than a page's text area overflows rather than being pushed.
- ~~No headers/footers~~ — superseded by L7d (2026-10-06). The 2026-10-02 fork (plain one-line text with fields, against rich text edited in the margin) was answered with plain text with `{page}`/`{leftmark}`/`{rightmark}` fields, one style per slot; the idea that a footer pushes the page number to the right corner was not built — a page style simply names what goes where. Page numbers (2026-10-02): `PageLayout.pageNumbers` (`PageNumbers`), written by `DocumentControl.NumberPages` during measure; Paged only; toggled from the page menu; since L7d the number is the `SlotPlace.FootCenter` label of the sheet's `PageMargins` child (the old single `LabelControl` is gone). `.md` frontmatter does not carry it.
- L7d/L7e: `.md`/`.txt` cannot store page breaks, page styles, marks, footnote groups or `Note` anchors. Left out and open items are listed under L7d and L7e in [[latex-editor]] (two-sided layouts, `\pagenumbering`, footnotes split across pages, and more). Float placement (L7f) is built; its gaps (mid-paragraph floats anchor at the paragraph's end, `\suppressfloats`, `placeins`/`\FloatBarrier`, `afterpage`, no UI creates floats, nested floats stay inline) are in [[latex-editor]].
- ~~Page changes are not undoable. Custom size is XML-only~~ — `SetPage` records a `PageEdit` (before/after `layout.page`, null = the editor's) in a "Page" step; `ApplyPage` is the unrecorded write. The page menu hosts Custom width × height mm fields through `ContextMenuContent`; Enter calls `DocumentToolbarControl.SetCustomSize`. The fields themselves are NOT GUI-verified. Test: `TextInput.PageNumbersAndUndo`
- Caret Up/Down across a break is not GUI-verified (extended keys can't be scripted).

Related: [[text-layout-one-measurer]], [[document-format-bar]], [[note-file-formats]], [[document-selection]]
