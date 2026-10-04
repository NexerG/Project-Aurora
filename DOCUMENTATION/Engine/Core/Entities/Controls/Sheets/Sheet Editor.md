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
VerifiedAgainst: 2026-10-04
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
			<Format At="B3" Bold="true" Fill="#C8E6A0" Number="#,##0.00"/>
		<Layer Name="Layer 1">
			<Cell At="A1">Item</Cell>
			<Cell At="B1">12.5</Cell>
		</Layer>
	</Page>
</Sheet>
```

Empty cells are not written. Cells are written row by row, and so are formats, after the rows and columns of the page. A file without `Format` elements loads unchanged. An element the reader does not know is kept and written back unchanged.

## Formulas

A cell whose text starts with `=` is a formula. The file keeps the formula text; the grid shows its value, and F2 opens the formula again. A number typed into a cell keeps the text it was typed as, so `1.50` stays `1.50`; a computed number is written with up to fifteen significant digits, so `=0.1+0.2` shows `0.3`.

A formula takes numbers, `+ - * /`, `^`, unary minus and brackets, cell references like `B3`, ranges like `A1:B3`, references into another page of the same file like `Expenses!B3` or `'My page'!A1:A5`, and `SUM`. Precedence follows Excel: unary minus binds tighter than `^`, so `=-2^2` is 4, and `^` groups left to right, so `=2^3^2` is 64. A reference reads the cell as shown, the topmost visible layer holding it. An empty cell counts as 0, and `SUM` skips text.

Errors are values and pass through whatever reads them: `#DIV/0!` for a division by zero, `#VALUE!` for text in arithmetic or a range outside a function, `#NAME?` for an unknown function or word, `#REF!` for a page that does not exist or a cell past the grid (16,384 columns, 1,048,576 rows), `#NUM!` for a result too large to hold, `#CYCLE!` for a cell that ends up reading itself and everything that reads it, and `#ERROR!` for a formula that does not parse.

A formula can read another sheet file in the vault: `[Budget]Expenses!B3`, or `'[My budget]Sheet 1'!B3` when a name has a space. The page is required. The file part is found the way a `[[link]]` is, by the sheet's name or the end of its path (`[Finance/Budget]`), first match winning. A sheet that is not open is loaded from disk to answer; a file or page that cannot be found reads `#REF!`. Renaming a sheet in the vault browser rewrites every reference to it, in open sheets and in sheets on disk; deleting it turns its readers to `#REF!`.

Copy writes the values, not the formulas. Ctrl+V pastes values. Ctrl+Shift+V pastes a link: one formula per copied cell that reads it, `=B3` from the same page, `=Page!B3` from another page and `=[File]Page!B3` from another file. If the clipboard has changed since the copy, it pastes plainly.

Every loaded sheet is held once by `SheetBook`, so two tabs of the same file share one document and one undo history, and an edit in one sheet reaches every sheet that reads it while both are open. One `SheetCalc` covers all of them: the parsed formula of each formula cell, its value, and the cells each formula reads, recorded both ways so a change finds what reads it. A range adds one edge per cell it covers. Changes made to a sheet file outside Thorium are seen on the next launch or vault switch.

## Formatting and resizing

A cell can be bold, filled with a colour and shown through a number format. Ctrl+B toggles bold the way Excel does: if the active cell is bold the whole selection is unbolded, otherwise the whole selection is bolded. Right-clicking the grid opens a menu with Cut, Copy, Paste and Paste link, then Bold, Fill and Number format; a right press on a cell outside the selection selects it first. Fill offers the same swatches as note highlights, and Number format offers General, Number (`#,##0.00`), Percent (`0.00%`) and Currency (`€#,##0.00`). A change over the selection is one undo step.

A format belongs to the page and is shared by all of its layers, the way column widths and row heights are, so a cell can be formatted while it is empty. Delete and Clear empty a cell and keep its format, and cut and paste move values only, so formats stay where they are. A number format is a .NET custom numeric format string drawn with the invariant culture, and General shows up to fifteen significant digits, switching to E-notation for very large and very small numbers. Only a number goes through the format; text is shown as it is.

When a cell's value is a number, a number is copied: Copy writes the unformatted value, so `12.50%` copies as `0.125` and `€1,234.50` as `1234.5`, and a copy pasted back into a sheet gives numbers. A note's plain-text copy of a sheet link does the same, and `\sheet{…}` in note math stays unformatted because it has to parse.

Dragging the right edge of a column header or the bottom edge of a row header resizes that column or row. The grab zone is four pixels either side of the edge, and the cursor turns into a resize shape over it. The size follows the pointer while dragging, never below 8 pixels, and the drag is recorded as one undo step when it ends.

Not done yet: italic, underline, text colour, alignment, borders and font size in cells; dates, custom number formats and increase or decrease decimals; formats do not travel with copy and paste; the open edit field does not show bold; no autofit on a double-click of an edge, no resizing several selected columns at once and no Esc to cancel a drag.

## Sheet links in notes

A note can show sheet cells live. A link to one cell is drawn inline as that cell's current value, in the font and style of the text around it. A link to a range is drawn as a read-only grid on a line of its own, the way display math is, left-aligned and cut at the column edge. Either is a single object character in the note, like a picture or a formula, so it is selected, deleted, copied and undone like them.

A link is written `Budget.sheet.xml#Data!B1` for a cell and `Budget.sheet.xml#Data!A1:B2` for a range: the sheet's file name with its extension, then `#`, the page, `!` and the address. In a note's XML it is the `Sheet` attribute of a `Run` (`<Run Sheet="Budget.sheet.xml#Data!B1"/>`); in Markdown it is `![[Budget.sheet.xml#Data!B1]]`. Markdown reads that as a link only when the file part ends in `.sheet.xml`, so `![[Budget#…]]` stays a note-heading embed.

The grid of a range shows values and one-pixel rules, with no column or row headers. Its column widths and row heights are the page's, scaled by the note's zoom. Numbers show in the page's number format and right-align when they fit, errors such as `#REF!` or `#DIV/0!` draw in the danger colour, and a link to a sheet that does not exist reads `#REF!`. The link keeps no value of its own: the note asks the sheet each time it measures, and every open note re-measures its links when any sheet is edited, so the values follow the sheet while both are open.

Ctrl+Shift+V in a note pastes a link to the cells last copied in a sheet, as one undo step. It only does so while the clipboard still holds that copy and the sheet has a file; otherwise, and in plain-text notes, read-only notes, code blocks and rules, it pastes plainly. Ctrl+V of a sheet copy in a note stays plain text. Copying a link out of a note writes its current values, one value or tab-separated rows, so another program gets values rather than the reference; pasting it back into a note keeps the live link.

Renaming a sheet in the vault browser rewrites the links to it in the notes on disk and in the notes that are open. Only the link text in each file changes, and a note's unsaved state is left alone, so what is in memory and on disk agree whether the note is later saved or discarded. Undoing past a rename restores the old reference, which then reads `#REF!`.

A formula in a note can read a cell too: `\sheet{Budget.sheet.xml#Data!B1}` inside its TeX typesets the cell's current value, using the same reference text as a link. Before the formula is parsed, each `\sheet{…}` is replaced by the value: a number as math digits, text as `\text{…}` with braces and backslashes dropped, a range as `\text{#VALUE!}` and a missing file, page or cell as `\text{#REF!}`. The laid-out formula is remembered under the replaced text, so a changed cell is simply a new entry and the formula redraws after any sheet edit. This is substitution only: the formula is still typeset, never evaluated. In the formula popup, Ctrl+Shift+V puts `\sheet{…}` for the last copied cell at the caret of the source field, and renaming a sheet rewrites the references in formulas the way it rewrites links. Obsidian does not know `\sheet{…}` and shows it as an unknown command, and copying a formula as plain text writes its source rather than the value.

A linked cell shows its number format but not its bold or fill, and a linked range grid follows a column or row resize only when the drag ends. Not done yet: a range taller than a page does not split across pages, an empty linked cell is invisible inline, clicking a link does nothing, and a formula that reads cells is not evaluated. A file name holding `#` or `]]`, or a page name holding `!`, cannot be linked.

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
| `ToggleBold()` | public | Unbolds the selection if the active cell is bold, otherwise bolds it; one undo step. |
| `SetFill(hex)` | public | Fills the selected cells with a colour, or clears the fill with null; one undo step. |
| `SetNumberFormat(format)` | public | Sets the number format of the selected cells, null for General; one undo step. |
| `ResizeBand(column, index, before, after)` | internal | Records a column or row resize as one undo step, null meaning the default size. |

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

### Paste link in a note
	if a formula popup is open
		if the clipboard text is the last copy made in a sheet
			insert \sheet{reference} into the popup's source field at its caret
		otherwise
			paste plainly into the field
		stop
	find the note editor holding the keyboard
	if the clipboard text is not the last copy made in a sheet, or that sheet has no file
		paste plainly
	otherwise, if the note is plain text or read-only, or the caret is in a code block or a rule
		paste plainly
	otherwise
		build the reference of the copied cells
		insert it at the caret as a sheet link object, as one undo step named "Paste link"

### Changing a format
	remember the format of every cell in the selection
	for each selected cell
		for bold, the opposite of the active cell's bold
		for fill or number format, the chosen value
		a format equal to the default removes the cell's entry
	write the new formats as one undo step
	tell the sheet book the page changed, so the tab goes unsaved and open notes redraw

### Resizing a column or row
	on a press, if the point is within four pixels of a column header's right edge or a row header's bottom edge
		remember that band and its size, and start the drag
	while dragging
		write the pointer's size into the band, no smaller than 8 pixels
		lay the grid out again
	when the drag stops
		record the size before and after as one undo step

### Arrange
	lay out the viewport and the grid
	if a restored view is waiting
		scroll to its offsets
	otherwise, if the active cell moved
		scroll it into view, keeping it clear of the pinned headers
	if the scroll changed, lay out the viewport once more

## Keys

`Sheet.*` actions sit on the same keys as the `Text.*` ones and do nothing unless a sheet has the keyboard. Arrows move (Shift extends), Enter and Tab commit and step (Shift reverses), F2 edits, Delete and Backspace clear, typing replaces the cell, Ctrl+Z / Ctrl+Y undo and redo, Ctrl+S saves, Ctrl+A selects the used range, Ctrl+B toggles bold, Ctrl+Shift+V pastes a link. Esc and the arrows inside an open cell belong to the field. `Sheet.PasteLink` is the one exception to doing nothing without a sheet: outside one it runs Paste link in the note (a plain paste when no link applies), because its Ctrl+Shift+V bind sits ahead of Ctrl+V and would otherwise take that key from notes.

## Related
- [[Text Box]] — the field a cell is edited in
- [[Label]] — the text of each visible cell
