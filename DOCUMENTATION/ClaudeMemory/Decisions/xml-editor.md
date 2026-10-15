# Decision — an XML file opens as coloured XML source beside its normal view, with live two-way sync

**Date:** 2026-10-15
**Status:** LANDED (both phases in one go). Test-verified; **NOT GUI-verified**; not perf-measured.
**Scope:** `ArctisAurora.Core.UI` — `XmlEditorControl`, `XmlReplaceEdit`, `RichTextDocument`, `DocumentEditSession`, `DocumentEditorControl`, `DocumentControl`, `NoteChangeKind`, `TexSourceFormat`, `DocumentXml`, `SheetDocument`, `PlannerDocument`, `WorkspaceKind`, `WorkspaceActions`, `SessionTab`, `SessionLayout`, `TabActions`; `Thorium.Editor.CustomControls.VaultBrowserControl`; `VaultNoteXml.menu.xml`, `VaultAsNote.menu.xml`, `VaultAsSheet.menu.xml`, `VaultAsPlanner.menu.xml`, `WorkspaceAdd.menu.xml`, `ThoriumAssets.assets.xml`

## What changed
- New `XmlEditorControl` (`IFileEditor`, `StackPanelControl`): `source` (a `DocumentEditorControl` with its own unregistered `DocumentEditSession`), `status` label (Danger role, "Line N: message" when invalid), `valid`, `LoadPath`, `Save`, `Repath`, `ViewState` (sets `asXml`), `RestoreView`.
- Sync region: `SourceChanged`, `ModelChanged`, `OnTick` (Apply then Regenerate, once per frame), `ArrangeCore` (posts the held-back regeneration via `Engine.Post`), `Apply`, `Regenerate`.
- Nested private `Target`: `NoteTarget` (`NoteSessions.Open`), `SheetTarget` (`SheetBook.Get`, `SheetBook.changed`), `PlannerTarget` (`PlannerBook.Open/Close`).
- New `XmlReplaceEdit : IEditRecord` — before/after model snapshots, `after` mutable.
- `RichTextDocument`: new `sourceLanguage` field; new `LoadSource(path, language)`, `SourceFromBytes(bytes, name, language)`, `SourceText()`, `ReplaceAll(from)`. Private `TexSource` removed — now `LoadSource(path, "latex")`. `Save` writes source text when `sourceLanguage` is set (the `.tex` case kept).
- `DocumentEditSession.MarkSaved()` new.
- `NoteChangeKind.Replaced` new (enum in `NoteModel.cs`); `DocumentControl.OnNoteChanged` ignores it; `DocumentEditorControl.NoteChanged` handles it by ViewState → LoadDocument → RestoreView, refocusing the caret if focus was inside.
- `DocumentEditorControl.LoadSession(DocumentEditSession)` new; private `Source` property (`sourceLanguage` set or `.tex`) replaces the six `.tex` checks: pageless, plainText, no properties panel, `CanAlign`, table refused, picture refused.
- `TexSourceFormat.Read(text, name, language = "latex")`.
- `DocumentXml.ToFile(document, path)` → `XDocument`, split out of `DocumentXml.Save`.
- `SheetDocument.ReplaceAll(from)` (raises `SheetBook.Restructured`), `PlannerDocument.ReplaceAll(from)` (raises `Changed`).
- `WorkspaceKind.Xml`; `WorkspaceActions.NewXml` (action `Workspace.NewXml`). The XML workspace lists only `.xml` files and always opens them as XML.
- `SessionTab.asXml` (`AsXml` XSD attribute, Settings). **Signature change:** `SessionLayout.tabFactory` is now `Func<string, bool, TabItemControl>` (path, asXml); `TabActions.CopyOf` passes `editor is XmlEditorControl`.
- Thorium `VaultBrowserControl`: `IsXml`; `Shows`/`FilterName` handle `Xml` ("XML"); `RowContextMenu` picks `vault-note-xml` / `vault-as-note` / `vault-as-sheet` / `vault-as-planner`; actions `Notes.OpenAsXml`, `Notes.OpenNormally`; `Open(path)` → `Open(path, asXml)` (asXml = XML workspace shown and file is `.xml`; only reuses a tab of the same kind); `BuildTab(path, asXml)` + `BuildXmlTab` (header = file name with extension); `RenameNote` retitles XML tabs with the file name; `NewNote(folder, extension = ".md")` / `CreateNote(folder, name, extension)` — the + in an XML workspace makes a `.xml` note and opens it as XML.
- Menus: right-click on an `.xml` file offers "Open as XML"; inside the XML workspace it offers "Open as note" / "Open as sheet" / "Open as planner". `WorkspaceAdd.menu.xml` gains "New XML workspace".
- Tests: `Syntax.XmlSourceRoundTrip` (`SyntaxTests.cs`, `Syntax.tests.xml`); `Thorium/Tests/XmlTests.cs` + `Xml.tests.xml`: `Xml.NoteSync`, `Xml.Invalid`, `Xml.SheetSync`, `Xml.PlannerSync`, `Xml.Restore`.

## Why these choices

**The source view reuses the `.tex` path, not a tree view.**
One wrapping `Code` block per line, `Language="xml"`, `SyntaxTokenizer` XML mode; the save is byte-for-byte, keeping newline and BOM. Rejected: a tree view / split tree — a new control, layout and hit-testing.

**Side by side with live two-way sync** (user's choice over "one view replaces the other").

**XML → model on every keystroke, applied only when the text is well formed AND the model's reader accepts it.**
Keystrokes are coalesced to one apply per frame in `OnTick`. The readers are `DocumentXml.Parse`, `SheetXml.Parse`, `PlannerXml.Parse`; otherwise the model stays at its last good state and the status line names the line. Rejected: applying after a ~400 ms pause (the user wanted per keystroke).

**An applied XML change is ONE undo step on the model's stack — label "XML edit", `join: true`.**
Consecutive keystrokes extend the same record by mutating `after` instead of pushing (bounded memory). The record holds model snapshots of object references, not text, because sheet and planner edit records hold page/layer/ticket object references — restoring the original objects keeps earlier undo steps valid (note records are address-based, so either would work). The XML view's own undo is cleared whenever its text is regenerated from the model. Any model change not caused by this view ends the open record.

**Saving broken XML writes the text verbatim** (user's choice over refusing).
The XML view's dirty state is the model's while its text is valid, its own when broken. After a verbatim save of valid text the model is marked saved (`MarkSaved` / `unsaved = false`).

**The model is swapped whole via `NoteChangeKind.Replaced` + `ReplaceAll`, over diffing block lists into Inserted/Removed.**
Existing Inserted needs a preceding block, and tables are separate. Cost: every applied keystroke rebuilds all block controls of the open note views of that file; diffing would keep unchanged blocks (faster on large notes) but was rejected for code size.

**Regenerated text comes from the same serialisation Save uses.**
`DocumentXml.ToFile`, so schema attributes and relative picture paths match disk; sheets and planners use `new XDocument(decl, ToXml)`. The view's original newline and BOM are kept.

**A hidden XML tab holds a regeneration back (stale flag) and posts it from the next arrange when shown.**
Cost: one frame of old text. Rejected: regenerating while hidden (a serialise per change).

**Smaller settled points.**
- The sheet view after an applied XML edit rebuilds its grid through the existing `BookChanged` path, so the selection goes back to A1 on the same page (accepted).
- `[name].[type].xml` data files still open as a note outside the XML workspace (user: "for now"); a suffix-based default-to-XML rule was considered and dropped.
- The workspace tab title reads "Xml" (from `kind.ToString()`), accepted.

## Known gaps
- Per-keystroke whole-model swap: large notes/sheets re-parse, copy in, and rebuild views (sheets also recalc) per valid keystroke; not measured.
- Formatting drift: once the model changes, the XML text is whatever the serialiser writes (attribute order, whitespace, no trailing newline, comments dropped); saving from the normal view drops XML comments typed in the XML view.
- A model change while the XML text is broken regenerates the XML and discards the broken in-progress text.
- If the file on disk is not readable by its model when the XML tab opens, the XML view opens unsynced and stays unsynced for that tab.
- After the normal view saves while the XML is broken, it writes the last good state over the broken text.
- `.md`/`.txt`/`.tex`/`.csv` cannot be opened as XML (by design).
- Not GUI-verified: the right-click menu entries, the + button in the XML workspace, the status line's look, keyboard typing in the XML view in the real app. Not perf-measured.
- Verification: builds clean (whole solution incl. Thorium); `--test=Xml` 5/5, `--test=Syntax` 5/5, `--test=Tex` 46/46, `--test=NoteModel` 6/6, `--test=Workspace` 8/8; Boot fails on its pre-existing baseline error only. Full `--test` 316 passed, 1 failed (Boot, baseline), 1 skipped.

Related: [[note-file-formats]], [[workspaces]], [[latex-editor]], [[note-model]], [[session-restore]]
