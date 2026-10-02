# Decision — a note table is a grid of block stacks, addressed through the flat block list

**Date:** 2026-09-29
**Scope:** `ArctisAurora.Core.UI` — `TableControl`, `GridListControl`, `DocumentControl`, `DocumentEditorControl`, `DocumentXml`, `RichTextDocument`, `ScrollableControl`, `TableEdit`, `TextInputActions`, `BlockControl`; `Thorium/Data/XML/Documents/Menus/Note.menu.xml`

## What changed
- `TableControl : GridListControl` — Fixed columns (`widths`, design px × zoom), Auto rows. A cell is a vertical
  `StackPanelControl` of `BlockControl`s; Enter in a cell is the ordinary `SplitBlock`, landing in the cell.
- Each table sits in a horizontal-only `ScrollableControl` built by `DocumentEditorControl.LoadDocument`; a
  table wider than the page scrolls sideways. The caret scroll also calls the table viewport's `ScrollIntoView`.
- `RichTextDocument.blocks` is `List<Control>` — a `BlockControl` or a `TableControl`.
- `DocumentControl.Blocks()` flattens: note blocks and every cell's blocks, in reading order. `DocumentAddress`
  keeps its shape; the flat index covers cells. `CaretAtPoint`/`LastBlock` iterate `Blocks()`.
- Guards: `DeleteSelection` refuses a range whose blocks do not share one parent (`OneContainer`);
  `TypeMarkdownPrefix` is off in a cell; a refused Backspace/Delete puts the caret back (`DeleteOver`).
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

**Structural commands bring their own record.** Flat indices shift under a row or column insert, so each one is a
`TableEdit` pushed in the same step; LIFO undo keeps every older record's addresses valid.

## Structural editing (2026-10-02)
- `TableEdit : IEditRecord` — the table's note-level index (`document.blocks`), its XML before and after
  (`DocumentXml.WriteTable`; null = no table there), caret address before and after. Undo/redo call
  `DocumentControl.SetTable` → `PutTable` (destroy the viewport at the index when present, `ReadTable` + `Hosted()`
  the XML in its place) → `CaretTo`.
- Every command edits a copy of the table's XML and rebuilds the table (`ChangeTable`): row above/below, column
  left/right (the new column copies the caret column's width), delete row/column (the last one deletes the table),
  delete table (refused when no paragraph would be left). `after` is re-serialised from the rebuilt table, so redo
  is byte-identical.
- Insert: `DocumentEditorControl.InsertTable` — 3×3, columns `floor(text width / 3)` design px; refused with a log
  line on `.md`/`.txt` and inside a cell. Goes after the caret's block; when that block is the note's last entry it
  is split at its end first (`SplitEdit` in the same step), so a paragraph always follows a table.
- Menus: `Note.menu.xml` "Insert table" and a static "Table" submenu → `Table.*` actions →
  `DocumentEditorControl.ChangeTable(label, change)`; outside a table they do nothing.
- Column resize: `TableControl.ColumnGrip`, a transparent hit-testable `PanelControl` centred on each column's right
  edge, full table height. Press → `BeginResize` (XML snapshot), drag → `widths`/`columnDefinitions` live, release →
  one `TableEdit` in a "Resize column" step. Min 24 px, whole pixels. Refused on a read-only note.
- Tab past the last cell inserts a row below and moves into it (`InsertTableRow(below, toNew)`), as one "Insert row" step.
- Lists in cells: `TypeMarkdownPrefix` lost its `parent != this` guard (`TypeMarkdownLine` keeps it — no code or
  rules typed into cells); `BlockControl.Editor()` walks up for the checkbox; `ShiftListLevel` only nests under a
  previous item in the same container. Tab/Shift+Tab in a cell: at a list item's start with nothing selected it
  nests/un-nests (Shift+Tab only while `listLevel > 0`), otherwise it steps cells (user, Word-style).
- **Why rebuild from XML:** one path for forward, undo and redo, and the snapshot is the file format, so a command
  that round-trips through `DocumentXml` cannot leave a table the loader would build differently. Cost: the
  caret's cell is recovered as (row, column, line, offset), and the blocks are new controls after every command.
- Tests: `TextInput.TableInsertUndo`, `TableInsertRefusedInMarkdown`, `TableRowsAndColumns` (each command + undo +
  redo compared as XML; typing after a row insert lands through the new flat address), `TableTabAddsRow`,
  `TableListInCell`, `TableResizeColumn` (+ golden `Resized`).

## Measured
`--profile-scenario`, Release+PROFILE, 3 runs each, frames 31–270: `Scenario.Type` p95 0.113–0.131 →
0.153–0.178 ms; `Document.MeasureBlocks` p50 0.142–0.150 → 0.150–0.169 ms. Likely `Blocks()` type-testing
page panels; not pinned.

## Known gaps
- A row taller than a page runs across the break.
- No nested tables, no merged cells, no row height drag, no column resize by keyboard.
- A code block in a cell (from XML only) does not wrap and is not coloured — `CodeWidth` and `HighlightCode` see note-level blocks only.
- The grip of the last column overhangs the table by 3 px, inside the viewport's clip.
- Insert, row/column commands, resize and lists in cells are test- and golden-verified only; **NOT GUI-verified**
  (menu placement, the resize cursor, a real drag).
- Pointer presses in and around a table, other than the grip drag, are not tested.

Related: [[document-structural-editing]], [[document-selection]], [[document-undo]], [[document-pages]], [[note-file-formats]], [[scroll-overscroll]]
