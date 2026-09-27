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
- The first page sits flush against the top/left of the viewport, and the page fill (`Surface`) is only slightly darker than the gap. Readable, not polished.
- No zoom: A4 is 794px wide, so a narrow pane scrolls horizontally, and 18px body text is large for A4.
- A click in the gap between pages resolves to the nearest line **above** the gap (`LineAt` takes the last line whose top ≤ y).
- No headers/footers, page numbers, widow/orphan or keep-with-next rules. A line taller than a page's text area overflows rather than being pushed.
- Page changes are not undoable. Custom size is XML-only (no input on the bar).
- Caret Up/Down across a break is not GUI-verified (extended keys can't be scripted).

Related: [[text-layout-one-measurer]], [[document-format-bar]], [[note-file-formats]], [[document-selection]]
