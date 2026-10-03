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
VerifiedAgainst: 2026-10-02
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

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `LoadPath(path)` | public | Loads a sheet file and shows its first page. |
| `Save()` | public | Writes the sheet back to its file. |
| `Select(row, column, extend)` | public | Moves the active cell; with extend, keeps the anchor so the selection grows. |
| `Move(rows, columns, extend)` | public | `Select` relative to the active cell. |
| `Enter(back)` / `Tab(back)` | public | Commits an open edit and steps down or right (up or left with back). |
| `BeginEdit(keep)` | public | Opens the field on the active cell, with its text (keep) or empty. |
| `TypeOver(input)` | public | Opens the field empty and types the queued characters into it. |
| `Clear()` | public | Empties the selected cells of the edited layer, as one undo step. |
| `Copy()` / `Cut()` / `Paste(text)` | public | The selection as tab-separated rows, out and in. |
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

`Sheet.*` actions sit on the same keys as the `Text.*` ones and do nothing unless a sheet has the keyboard. Arrows move (Shift extends), Enter and Tab commit and step (Shift reverses), F2 edits, Delete and Backspace clear, typing replaces the cell, Ctrl+Z / Ctrl+Y undo and redo, Ctrl+S saves, Ctrl+A selects the used range. Esc and the arrows inside an open cell belong to the field.

## Related
- [[Text Box]] — the field a cell is edited in
- [[Label]] — the text of each visible cell
