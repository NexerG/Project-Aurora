---
date: 2026-10-02
Status: Current
tags:
  - d_UI
  - d_Entity
cssclasses:
  - Aurora.css
Linker:
  - "[[Arctis Aurora]]"
System:
Class:
  - "[[Sheet Editor]]"
Parent Class:
  - ScrollableControl
Interfaces:
  - IClipboardTarget
  - IFileEditor
Used by:
  - VaultBrowserControl
Type:
  - Public
Attributes:
Namespace: ArctisAurora.Core.UI
SourceFile: AuroraEngine/Core/UI/SheetEditorControl.cs
VerifiedAgainst: 2026-10-03
---
## Description

One open sheet in a tab: a scroll viewport over a `SheetControl` grid, the `SheetDocument` it shows, the file it came from and its undo history. A sheet is a `*.sheet.xml` file in the vault, opened from the browser like a note.

A sheet file holds pages, and each page holds layers stacked over the same grid. A cell shows the text of the topmost visible layer that has it, and edits go to the topmost layer. The editor shows the first page; the page tabs and the layer list come later.

Only the cells inside the viewport have controls. The grid keeps a pool of labels and lines and rebinds them to whatever cells are in view, so a sheet of ten thousand rows costs the same number of controls as one of twenty.

## File format

```xml
<Sheet Name="Budget">
	<Page Name="Sheet 1">
		<Column At="B" Width="140"/>
		<Row At="4" Height="30"/>
		<Layer Name="Layer 1">
			<Cell At="A1">Item</Cell>
			<Cell At="B1">12.5</Cell>
		</Layer>
	</Page>
</Sheet>
```

Empty cells are not written. Cells are written row by row. An element the reader does not know is kept and written back unchanged.

## Formulas

A cell whose text starts with `=` is a formula. The file keeps the formula text; the grid shows its value, and F2 opens the formula again. A number typed into a cell keeps the text it was typed as, so `1.50` stays `1.50`; a computed number is written with up to fifteen significant digits, so `=0.1+0.2` shows `0.3`.

A formula takes numbers, `+ - * /`, `^`, unary minus and brackets, cell references like `B3`, ranges like `A1:B3`, references into another page of the same file like `Expenses!B3` or `'My page'!A1:A5`, and `SUM`. Precedence follows Excel: unary minus binds tighter than `^`, so `=-2^2` is 4, and `^` groups left to right, so `=2^3^2` is 64. A reference reads the cell as shown, the topmost visible layer holding it. An empty cell counts as 0, and `SUM` skips text.

Errors are values and pass through whatever reads them: `#DIV/0!` for a division by zero, `#VALUE!` for text in arithmetic or a range outside a function, `#NAME?` for an unknown function or word, `#REF!` for a page that does not exist or a cell past the grid (16,384 columns, 1,048,576 rows), `#NUM!` for a result too large to hold, `#CYCLE!` for a cell that ends up reading itself and everything that reads it, and `#ERROR!` for a formula that does not parse.

A formula can read another sheet file in the vault: `[Budget]Expenses!B3`, or `'[My budget]Sheet 1'!B3` when a name has a space. The page is required. The file part is found the way a `[[link]]` is, by the sheet's name or the end of its path (`[Finance/Budget]`), first match winning. A sheet that is not open is loaded from disk to answer; a file or page that cannot be found reads `#REF!`. Renaming a sheet in the vault browser rewrites every reference to it, in open sheets and in sheets on disk; deleting it turns its readers to `#REF!`.

Copy writes the values, not the formulas. Ctrl+V pastes values. Ctrl+Shift+V pastes a link: one formula per copied cell that reads it, `=B3` from the same page, `=Page!B3` from another page and `=[File]Page!B3` from another file. If the clipboard has changed since the copy, it pastes plainly.

Every loaded sheet is held once by `SheetBook`, so two tabs of the same file share one document and one undo history, and an edit in one sheet reaches every sheet that reads it while both are open. One `SheetCalc` covers all of them: the parsed formula of each formula cell, its value, and the cells each formula reads, recorded both ways so a change finds what reads it. A range adds one edge per cell it covers. Changes made to a sheet file outside Thorium are seen on the next launch or vault switch.

### Recalculating after a change
	for each changed cell
		forget its formula and the edges it had
		if its shown text is a formula, parse it and record the cells it reads
	collect the changed formula cells and everything that reads a changed cell, transitively
	for each collected cell, count how many of the cells it reads are also collected
	queue the cells whose count is zero
	while the queue has a cell
		evaluate it
		for each collected cell that reads it, lower the count, and queue it at zero
	every collected cell still waiting is in or behind a cycle, and shows #CYCLE!

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `LoadPath(path)` | public | Loads a sheet file and shows its first page. |
| `Save()` | public | Commits an open edit, then writes the sheet back to its file. |
| `Select(row, column, extend)` | public | Moves the active cell; with extend, keeps the anchor so the selection grows. |
| `Move(rows, columns, extend)` | public | `Select` relative to the active cell. |
| `Enter(back)` / `Tab(back)` | public | Commits an open edit and steps down or right (up or left with back). |
| `BeginEdit(keep)` | public | Opens the field on the active cell, with its text (keep) or empty. |
| `TypeOver(input)` | public | Opens the field empty and types the queued characters into it. |
| `Clear()` | public | Empties the selected cells of the edited layer, as one undo step. |
| `Copy()` / `Cut()` / `Paste(text)` | public | The selection's values as tab-separated rows, out and in. |
| `PasteLink()` | public | Formulas reading the last copied cells, from the active cell; a plain paste if the clipboard changed. |
| `ViewState()` / `RestoreView(view)` | public | The active cell, anchor and scroll, for session restore. |

## Methods

### BeginEdit
	if already editing, stop
	remember the active cell as the cell being edited
	fill the field with that cell's text, or nothing
	give the field the keyboard and select its text
	if keeping the text, put the caret at its end
	lay the grid out again and scroll the cell into view

### Finishing an edit
	if not editing, stop
	stop editing
	write the field's text into the edited cell as one undo step
	after Enter or Tab, give the keyboard back to the grid
	after a click elsewhere, leave the keyboard where the click put it

### Paste
	if editing, let the field take it
	split the text into rows, and each row at its tabs
	drop a trailing empty row
	write every value from the active cell down and right, as one undo step
	select the block that was written

### Arrange
	lay out the viewport and the grid
	if a restored view is waiting
		scroll to its offsets
	otherwise, if the active cell moved
		scroll it into view, keeping it clear of the pinned headers
	if the scroll changed, lay out the viewport once more

## Keys

`Sheet.*` actions sit on the same keys as the `Text.*` ones and do nothing unless a sheet has the keyboard. Arrows move (Shift extends), Enter and Tab commit and step (Shift reverses), F2 edits, Delete and Backspace clear, typing replaces the cell, Ctrl+Z / Ctrl+Y undo and redo, Ctrl+S saves, Ctrl+A selects the used range, Ctrl+Shift+V pastes a link. Esc and the arrows inside an open cell belong to the field. `Sheet.PasteLink` is the one exception to doing nothing without a sheet: outside one it runs a plain paste, because its Ctrl+Shift+V bind sits ahead of Ctrl+V and would otherwise take that key from notes.

## Related
- [[Text Box]] — the field a cell is edited in
- [[Label]] — the text of each visible cell
