---
date: 2026-10-15
Status: Current
tags:
  - Engine
  - d_UI
  - d_XML
Class:
  - "[[XmlEditorControl]]"
Type:
  - Public
---
## Description
The control that shows an XML file as coloured, editable XML source, next to the file's normal view. Thorium can open any `.xml` file in the vault this way: a note, a `*.sheet.xml`, a `*.planner.xml` or a `[name].[type].xml` data file. The two views stay in step both ways: an edit in the note, sheet or planner regenerates the XML text, and every keystroke in the XML that leaves it well formed (and that the model's reader accepts) is copied into the model. Half-typed XML leaves the model alone.

The source view is the `.tex` source path made generic: a [[Rich Text Document|RichTextDocument]] with `sourceLanguage` set holds one wrapping `Code` block per line with `Language="xml"`, coloured by the `SyntaxTokenizer` XML mode, and saves byte-for-byte keeping its newline and BOM. It is not a tree view.

## Opening a file as XML
A workspace of kind `Xml` lists only `.xml` files and always opens them as XML. Elsewhere, right-clicking an `.xml` file in the vault offers "Open as XML". Inside the XML workspace the same menu offers "Open as note", "Open as sheet" and "Open as planner". The + in an XML workspace creates a `.xml` note and opens it as XML. A `[name].[type].xml` data file still opens as a note outside the XML workspace.

## Members
- `source` — the `DocumentEditorControl` showing the text, over its own unregistered `DocumentEditSession`.
- `status` — a label in the Danger role reading "Line N: message" while the text is invalid.
- `valid` — whether the text is currently well formed and accepted by the model's reader.
- `LoadPath`, `Save`, `Repath`, `ViewState`, `RestoreView` — the `IFileEditor` surface; `ViewState` sets `asXml` on the `SessionTab`, so a restored session reopens the tab as XML.
- A private `Target` hides which model is behind the file: a note through `NoteSessions.Open`, a sheet through `SheetBook.Get` and `SheetBook.changed`, a planner through `PlannerBook.Open` and `Close`.

## Sync
#### Source Changed ()
mark the text as needing an apply

#### Model Changed ()
if the change was not caused by this view
	end the open undo record
mark the text as needing a regeneration

#### On Tick ()
if an apply is pending -> Apply ()
if a regeneration is pending
	if the tab is on screen -> Regenerate ()
	else mark it stale

#### Arrange Core ()
if a regeneration is stale and none is posted yet
	post the regeneration through `Engine.Post`

#### Apply ()
parse the text and hand it to the model's reader (`DocumentXml.Parse`, `SheetXml.Parse` or `PlannerXml.Parse`)
if either throws
	set `valid` false and show the error, "Line N: message" when it is an XML syntax error
	return
set `valid` true and hide the status line
if an `XmlReplaceEdit` is open
	set its `after` to the parsed model
else
	push one `XmlReplaceEdit` labelled "XML edit", `join: true`, holding a snapshot of the model as `before`
copy the parsed model into the target (`ReplaceAll`) and mark it unsaved

#### Regenerate ()
serialise the model the way Save does (`DocumentXml.ToFile`, or `new XDocument(decl, ToXml)` for a sheet or planner)
if the text is the same as shown -> return
write it into `source` with the view's original newline and BOM
clear the XML view's own undo

The apply is coalesced to one per frame, so a held key or a paste is one parse. A regeneration for a hidden tab is held back and posted from the next arrange when the tab is shown, so the tab shows one frame of old text.

## Undo
An XML change applied to the model is one undo step on the model's stack, labelled "XML edit". Consecutive keystrokes extend the same `XmlReplaceEdit` by changing its `after` snapshot instead of pushing a new record, so memory stays bounded. The record keeps snapshots of the model's object references rather than text, because sheet and planner edit records hold page, layer and ticket object references; restoring the original objects keeps earlier undo steps valid. Any model change not caused by this view ends the open record.

## Replacing the whole model
An applied edit calls `ReplaceAll(from)` on the model, which raises `NoteChangeKind.Replaced` for a note, `SheetBook.Restructured` for a sheet and `Changed` for a planner. A note view handles `Replaced` by saving its view state, reloading the document and restoring the state, refocusing the caret if focus was inside. Every applied keystroke therefore rebuilds all block controls of the open note views of that file. After an applied XML edit a sheet view rebuilds its grid and its selection returns to A1 on the same page.

## Saving
Saving broken XML writes the text verbatim. The view's dirty state is the model's while its text is valid and its own while it is broken. After a verbatim save of valid text the model is marked saved through `DocumentEditSession.MarkSaved`.

## Known gaps
- Large notes and sheets re-parse, copy in and rebuild their views on every valid keystroke; not measured.
- Once the model changes, the XML text is whatever the serialiser writes: attribute order, whitespace, no trailing newline, and comments dropped. Saving from the normal view drops comments typed in the XML view.
- A model change while the XML text is broken regenerates the text and discards the broken in-progress edit.
- If the file on disk cannot be read by its model when the XML tab opens, the view stays unsynced for that tab.
- After the normal view saves while the XML is broken, it writes its last good state over the broken text.
- `.md`, `.txt`, `.tex` and `.csv` cannot be opened as XML.

See `ClaudeMemory/Decisions/xml-editor.md` for the choices and what was rejected.
