---
date: 2026-10-02
Status: Current
tags:
  - d_text
cssclasses:
  - Aurora.css
Linker:
  - "[[Arctis Aurora]]"
System:
Class:
  - "[[Formula Popup]]"
Parent Class:
Interfaces:
Used by:
  - DocumentEditorControl
Type:
  - Internal
  - Sealed
Attributes:
Namespace: ArctisAurora.Core.UI
SourceFile: AuroraEngine/Core/UI/FormulaPopup.cs
VerifiedAgainst: 2026-10-02
---
## Description

Edits one formula's TeX source in a one-line field that opens in a popup just under the formula. Ctrl+M inserts an inline formula and Ctrl+Shift+M a display one; the note's right-click menu has the same two plus Edit formula. A click on a formula selects it the way a click selects a picture, and a double-click or Enter on the selected formula opens the popup.

The note itself is the preview. Every change in the field, including the field's own undo, rewrites the formula in place and the paragraph reflows around it, without touching the note's undo history. Only finishing the edit records anything, and it records a single step: "Insert formula" for a new one, "Edit formula" for an existing one whose source changed.

Enter or a click anywhere else commits. Esc puts the old source back, and a new formula left empty, whether committed or cancelled, disappears without a trace in the history. A formula cannot be placed in a read-only note, a plain text note, a code block or a horizontal rule.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `Open(DocumentEditorControl editor, DocumentControl document, DocumentAddress at, bool placed)` | static | Opens the field on the formula at `at`; `placed` marks one that was just inserted. |

## Methods

### `Open` *(static)*
	remember the formula's current source
	fill the field with it, all of it selected
	on every change in the field, write the field's text into the formula
	on Enter or a click away, finish
	on Esc, cancel
	open a popup holding the field under the formula's drawn box
	give the field the keyboard

### Finish
	if already finished, stop
	close the popup
	if the formula was just inserted
		if its source is empty, take it out of the note
		otherwise record it as one inserted formula
	otherwise, if its source changed
		record one edit from the old source to the new
	after Enter, put the keyboard back on the note's caret

### Cancel
	if already finished, stop
	close the popup
	if the formula was just inserted, take it out of the note
	otherwise put the old source back
	put the keyboard back on the note's caret

## Related
- [[Math Layout]] — lays out what the field holds
- [[Math Parser]] — reads the source
