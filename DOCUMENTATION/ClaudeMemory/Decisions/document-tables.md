# Decision — a note table is a grid of block stacks, addressed through the flat block list

**Date:** 2026-09-29
**Scope:** `ArctisAurora.Core.UI` — `TableControl`, `GridListControl`, `DocumentControl`, `DocumentEditorControl`, `DocumentXml`, `RichTextDocument`, `ScrollableControl`

## What changed
- `TableControl : GridListControl` — Fixed columns (`widths`, design px × zoom), Auto rows. A cell is a vertical
  `StackPanelControl` of `BlockControl`s; Enter in a cell is the ordinary `SplitBlock`, landing in the cell.
- Each table sits in a horizontal-only `ScrollableControl` built by `DocumentEditorControl.LoadDocument`; a
  table wider than the page scrolls sideways. The caret scroll also calls the table viewport's `ScrollIntoView`.
- `RichTextDocument.blocks` is `List<Control>` — a `BlockControl` or a `TableControl`.
- `DocumentControl.Blocks()` flattens: note blocks and every cell's blocks, in reading order. `DocumentAddress`
  keeps its shape; the flat index covers cells. `CaretAtPoint`/`LastBlock` iterate `Blocks()`.
- Guards: `DeleteSelection` refuses a range whose blocks do not share one parent (`OneContainer`);
  `TypeListPrefix` is off in a cell; a refused Backspace/Delete puts the caret back (`DeleteOver`).
- `InsertBlockAfter` inserts into the cell when `after` lives in one; `RemoveBlock` already worked (`Destroy`).
- Tab / Shift+Tab in a cell step cells (`TableControl.StepCell`, branch at the top of `ShiftListLevel`).
- Page splits between rows: `TableControl.Paginate` writes each push into the previous row's `gapAfter`;
  `MeasureCore` zeroes the gaps first. `DocumentControl.Paginate` pushes the table's top by its first row.
- Borders are 1px `PanelControl`s added straight to `children` (not through `AddChild`, which would claim a
  cell): top + left per cell, right on the last column, bottom where no row follows directly.
- Highlights and the caret clip to the table viewport (`DocumentControl.TableViewport`).
- XML: `<Table>` → `<Column Width>`* → `<Row>` → `<Cell>` → `<Block>`*. `ReadTable` pads short rows and widens
  for long ones (default width 150). `.md` and `.txt` never carry tables — their writers skip non-`Block`s.
- `GridListControl` fixes: Auto rows measure each child at its spanned column width (Fixed → Auto columns →
  Star columns → rows → Star rows); a cell's rect stops before its last band's `gapAfter`.
- `ScrollableControl.OnPointerScroll`: a vertical wheel never scrolls X. A horizontal-only viewport passes it
  on, so the note scrolls under a wide table.

## Why these choices

**Flat addressing instead of (table, cell, offset).**
Left/right, up/down by point, range styling, snapshots and every undo record already work over a flat index;
the table cost only structural guards. The price: a drag through a table selects in reading order, not by rows,
and a delete across containers is refused rather than clearing cells.

**A cell is a stack of blocks, not one block with hard breaks.**
Hard breaks would have changed the one `TextMeasurer` on the typing path; split/join/undo already existed for
blocks. Rejected by the user in favour of the stack (2026-09-29).

**Page pushes ride `gapAfter`, not a `RowOffsets` hook on the base.**
No new base API. It forced two things: gaps must be zeroed before measure (the DEBUG `VerifyLayout` re-measure
logged `desired stale` otherwise), and the base had to stop stretching a cell into its trailing gap.

**Fixed widths, may exceed the page.** User requirement; overflow scrolls per table, not the page.

**No `TableEdit` record yet.** Nothing inserts or deletes a table, row or column at runtime, so flat indices
never shift under an existing record. The first structural command must bring its record with it.

## Measured
`--profile-scenario`, Release+PROFILE, 3 runs each, frames 31–270: `Scenario.Type` p95 0.113–0.131 →
0.153–0.178 ms; `Document.MeasureBlocks` p50 0.142–0.150 → 0.150–0.169 ms. Likely `Blocks()` type-testing
page panels; not pinned.

## Known gaps
- No UI inserts a table; tables come from `.xml` notes only.
- No row/column insert or delete, no column resize drag, no Tab-past-last-cell row.
- Lists in cells are off (the task checkbox finds its editor through `parent?.parent`).
- A row taller than a page runs across the break.
- Inserting a table into a `.md`/`.txt` note would lose it on save — the insert slice must refuse or warn.
- Pointer presses in and around a table are not tested.

Related: [[document-structural-editing]], [[document-selection]], [[document-undo]], [[document-pages]], [[note-file-formats]], [[scroll-overscroll]]
