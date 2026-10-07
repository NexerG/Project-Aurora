# Decision — a note table is a grid of block stacks, addressed through the flat block list

**Date:** 2026-09-29
**Scope:** `ArctisAurora.Core.UI` — `TableControl`, `GridListControl`, `DocumentControl`, `DocumentEditorControl`, `DocumentXml`, `RichTextDocument`, `ScrollableControl`, `TableEdit`, `TextInputActions`, `BlockControl`; `Thorium/Data/XML/Documents/Menus/Note.menu.xml`

## What changed
- `TableControl : GridListControl` — Fixed columns (`widths`, design px × zoom), Auto rows. A cell is a vertical
  `StackPanelControl` of `BlockControl`s; Enter in a cell is the ordinary `SplitBlock`, landing in the cell.
- Each table sits in a horizontal-only `ScrollableControl` built by `DocumentEditorControl.LoadDocument`; a
  table wider than the page scrolls sideways. The caret scroll also calls the table viewport's `ScrollIntoView`.
- `RichTextDocument.blocks` was `List<Control>` — a `BlockControl` or a `TableControl`. Superseded by [[note-model]] N1 (2026-10-07): it is `NoteNode[]` (`NoteBlock` or `NoteTable`); a table's widths, rules, cell rules and cells live on `NoteTable`/`NoteCell` and `TableControl(NoteTable)` is the build path.
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

## Column spans, borders, merge and split (2026-10-06)
- XML: `<Cell ColumnSpan="n">` and `<Table Borders="false">`, read and written by `DocumentXml`; absent = span 1 / borders true.
- `GridListControl`: internal `ColumnSpan(Control)`, `SetColumnSpan(Control, int)`. `TableControl`: public `showBorders` (default true); `AddRow(cells, List<int>? spans = null)` (since [[note-model]] N1 a private `AddRow(NoteCell[])`); `CellBlocks(row, column)` finds the cell covering that grid position; borders honour spans and `showBorders`.
- No placeholder cells behind a spanning cell; cells are found by grid position. `ReadTable`'s padding still fills a row whose spans sum to fewer than the columns.
- `DocumentControl`: `InsertTableColumn`/`DeleteTableColumn` are span-aware; new `MergeTableCellRight()`, `SplitTableCell()`; private helpers `Covering`, `Span`, `SetSpan`, `EmptyCell`.
- `TextInputActions`: `Table.MergeRight` (`MergeCellRight`), `Table.Split` (`SplitCell`). `Note.menu.xml` Table submenu: "Merge cell right", "Split cell".
- Why it exists: LaTeX `\multicolumn` ([[latex-editor]], L5-F4) needs a cell wider than one column.
- Tests: `TextInput.TableColumnSpan` (XML round-trip with ColumnSpan/Borders, merged cell width = sum of its columns, insert column inside/left of a span, delete inside a span, merge right, split, each undo restores exactly, merge on the last cell and split on a single cell refused).

**Real spans, not display-only (user, scope iii).**
Structural edits are span-aware: inserting a column inside a span widens it, deleting a column inside a span shrinks it. Rejected: display-only spans with structural edits refused.

**Merge is "Merge cell right", not a selection range (user picked the recommended form).**
The caret's cell absorbs the next cell in its row, blocks appended, an empty side dropped; refused on a row's last cell. "Split cell" turns a merged cell back into single cells with the content in the first; refused on a single cell. Both are one undo step (a table edit). Rejected: merge by selection range.

**`Borders` flag on `<Table>`.** Needed so a LaTeX table without rules draws no grid; the full grid or nothing, not per-edge rules (L5-F2 in [[latex-editor]]). Superseded in L7g: LaTeX tables always write `Borders="false"` and carry per-edge rules (next section).

**`<Table SpaceBefore>` (L7c).** `TableControl.spaceBefore` (float?) is the space above a table, read and written by `DocumentXml`; `DocumentControl.Paginate` uses it instead of the block spacing when set. See [[latex-editor]].

**`<Table PageBreak>` (L7d).** `TableControl.pageBreak` (enum `PageBreak`, XML `PageBreak`) puts a page break before the table, as a block's does. See [[document-pages]].

## Rules, alignment and padding (L7g, 2026-10-06)
- XML: `<Table Align Padding="across down">`, `<Column RuleLeft RuleRight>` (Plain|Double), `<Cell RuleAbove RuleBelow>` ("Kind" or "Kind widthPx"; kinds Plain, Double, Heavy, Light, Cmid), `<Cell TrimAbove TrimBelow>` (Left|Right|Both). Read and written by `DocumentXml` (`ReadCellRules`, `ReadRule`, `ReadPadding`, `WriteRule`).
- `TableControl`: public `alignment` (`TextAlignment`), `cellRules` (`Dictionary<StackPanelControl, CellRules>` — since [[note-model]] N1 gone: `NoteCell.rules`, found through `CellOf`), `leftRules`/`rightRules` (`TableRule[]` per column, the model's arrays), `cellPadding` (`Vector2?`, null = the 6 px `cellInset`), `ApplyInsets()`; enums `TableRule`, `RuleTrim`; struct `CellRules`. `ArrangeCore` narrows and moves the grid by `alignment`; `ArrangeRules` draws the rules in `PaletteRole.Ink`, at least 1 px, zoom-scaled, touching segments joined into one line, vertical rules split at page gaps.
- Rules are drawn at LaTeX's weights: arrayRule 0.4 pt, doubleRuleSep 2 pt, heavy 0.08 em, light 0.05 em, cmid 0.03 em, booktabs rule seps 0.4 ex above and 0.65 ex below, ex = 0.430555 em (Latin Modern's x-height, not the cell font's).
- Edits: `DocumentControl.SplitTableCell` copies Rule* attributes to the new cells (Left trim stays on the first piece, Right trim moves to the last). A new row or column has no rules, deleting drops its rules, merge keeps the left cell's.
- Why it exists: LaTeX tabulars with exact rules, centring and `\tabcolsep` ([[latex-editor]], L7g-F1 to L7g-F3).
- Tests: `TextInput.TableRules` (XML round-trip, thicknesses and positions at zoom 1 and 2, booktabs seps, cmidrule trims, joined segments, split carries rules and trims, undo, a new row has no rules); golden `Tex.Booktabs`.

**Rules live on the cells and columns, not as a rule per row boundary (L7g-F1, user, recommended).**
Row and column edits carry them free. Rejected: `<Rule From To Kind Trim>` per boundary — LaTeX's exact shape, but column insert and delete shift its indices. Cost: a rule under part of a merged cell is not expressible; it reaches any cell it overlaps.

**Structural edits and padding (L7g-F2, L7g-F3, user, recommended).**
Edits as above; rejected: dropping every rule on any structural edit. Padding is `<Table Padding>`: LaTeX tables get `\tabcolsep` across and 0 down, notes keep 6 px; rejected: 6 px everywhere plus booktabs gaps (wrong table heights).

## Measured
`--profile-scenario`, Release+PROFILE, 3 runs each, frames 31–270: `Scenario.Type` p95 0.113–0.131 →
0.153–0.178 ms; `Document.MeasureBlocks` p50 0.142–0.150 → 0.150–0.169 ms. Likely `Blocks()` type-testing
page panels; not pinned.

## Known gaps
- A row taller than a page runs across the break.
- No nested tables, no row spans (`\multirow`), no UI for per-edge rules, alignment or padding (a LaTeX preview writes them; L7g), no row height drag, no column resize by keyboard. Column spans landed 2026-10-06 (above).
- Column spans, Borders, span-aware edits and merge/split are test-verified only (`TextInput.TableColumnSpan`); **NOT GUI-verified** (the Merge/Split menu items). The test reads spans via XML because `GridListControl.ColumnSpan` is internal.
- A code block in a cell (from XML only) does not wrap and is not coloured — `CodeWidth` and `HighlightCode` see note-level blocks only.
- The grip of the last column overhangs the table by 3 px, inside the viewport's clip.
- Rules, alignment and padding (L7g) are test-verified (`TextInput.TableRules`) and golden-verified (`Tex.Booktabs`); **NOT GUI-verified**. `|` inside a `\multicolumn` spec is ignored, booktabs gaps do not interrupt vertical rules, and a rule under part of a merged cell rules the whole cell.
- Insert, row/column commands, resize and lists in cells are test- and golden-verified only; **NOT GUI-verified**
  (menu placement, the resize cursor, a real drag).
- Pointer presses in and around a table, other than the grip drag, are not tested.

Related: [[document-structural-editing]], [[document-selection]], [[document-undo]], [[document-pages]], [[note-file-formats]], [[scroll-overscroll]], [[latex-editor]]
