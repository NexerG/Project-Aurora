# Decision — a note gets a data model separate from its controls

**Date:** 2026-10-07
**Status:** PARTIAL. N0 and N1 landed 2026-10-07; N2–N4 not started. Phases: [[note-model-plan]].
**Scope:** `ArctisAurora.Core.UI` — `TextRunData`, `TextRunControl`, `NoteNode`, `NoteBlock`, `NoteTable`, `NoteCell`, `BlockControl`, `TableControl`, `RichTextDocument`, `DocumentXml`, `DocumentControl`, `DocumentEditorControl`, `PdfExport`, `DocumentEditSession`

## What changed (N0)
- NEW `TextRunData` (sealed class): `text`, `spans` (`List<StyleSpan>`, readonly), `Length`; `InsertText`, `RemoveText`, `InsertSpans` (was the body of `BlockControl.InsertSlice`), `AppendSpans` (now public), `StyleAt`, `AllSpans`, `StyleRange` (no invalidation), `SetPicture` / `SetMath` (now return `bool`, true when a span starts there), `SplitSpanAt`, `MergeSpans`, `Runs()`; private `SpanForInsert`, `TextSpanBeside`, `Typable`, `ZeroText`, `DropEmptySpans`, `SameStyle` moved from `BlockControl`
- `TextRunControl` holds `public readonly TextRunData data`; `spans` is a get-only property `=> data.spans`; `text` (XML `Text`) reads and writes `data.text`, still invalidating layout only when the string changes; `Length => data.Length`; new `protected void Edited(string before)` re-measures when an edit through `data` changed the string
- `BlockControl`'s pure text/span operations (`InsertText`, `RemoveText`, `InsertSlice`, `AppendSlice`/`AppendBlock`, `StyleAt`, `AllSpans`, `StyleRange`, `SetPicture`, `SetMath`, `SplitSpanAt`, `MergeSpans`, `Runs`) are same-signature forwarders to `data` that do the layout invalidation
- Block-level methods (`SplitAt`, `Snapshot`, `SliceSnapshot`, `Restore`, `TakeKind`, `From`) unchanged in N0 — they moved to `NoteBlock` in N1
- `LabelControl` and `TextBoxControl.FieldLine` each get a private `TextRunData`; no code change
- No behaviour change

## What changed (N1)
- NEW `NoteModel.cs` (`ArctisAurora.Core.UI`):
	- `NoteNode` (abstract): `spaceBefore`, `pageBreak`, `insert` (`PageInsert?`)
	- `NoteBlock : NoteNode` (sealed): `run` (readonly `TextRunData`), `stylingType`, `alignment`, `firstIndent`, `pageStyle`, `markLeft`, `markRight`, `language`, `codeWrap`, `listKind`, `listLevel`, `isChecked`, `listMarker`, `listStart`, `legacyInkHex`; `AppendRun(Run)`, `SplitAt(offset) → NoteBlock`, `Snapshot`, `SliceSnapshot`, `Restore`, `TakeKind`, static `From(BlockSnapshot)`
	- `NoteCell` (sealed): `span` (default 1), `rules` (`CellRules?`), `blocks` (`NoteBlock[]`)
	- `NoteTable : NoteNode` (sealed): `widths` (`float[]`, readonly), `leftRules`/`rightRules` (`TableRule[]`), `rows` (`NoteCell[][]`), `showBorders`, `alignment`, `cellPadding`; ctor `NoteTable(float[] widths)`
- `RichTextDocument.blocks`: `List<Control>` → `NoteNode[]`
- `DocumentXml`: `ReadBlock → NoteBlock`, `ReadTable → NoteTable` (internal), `ReadInsert → IEnumerable<NoteNode>`, `WriteTable(NoteTable)`, `ToXml` walks `NoteNode`s; new private `Fit(cells, columns)` clamps a row's spans to the columns, fills a short row with empty cells and gives every empty cell one empty block
- Loading a note (`RichTextDocument.Load`, `DocumentXml.Parse`) builds no controls
- `BlockControl`: `public readonly NoteBlock note`; ctors `BlockControl()` and `BlockControl(NoteBlock)` (reads `note.run` as its `data`); the 16 block fields are properties forwarding to `note`; `AppendRun`/`SplitAt`/`Snapshot`/`SliceSnapshot`/`Restore`/`TakeKind` wrap `note`'s and do the view work; new `internal void StartEffects()`; `PictureChar` stays on the control
- `TextRunControl`: `data` no longer initialised inline; new `protected TextRunControl(TextRunData)`; the parameterless ctor chains to it with a new `TextRunData`
- `TableControl`: `public readonly NoteTable model`; ctor `TableControl(List<float>)` → `TableControl(NoteTable)`; `widths` is `model.widths`; `leftRules`/`rightRules` are the model's arrays; `showBorders`, `spaceBefore`, `alignment`, `cellPadding`, `pageBreak`, `insert` forward to the model; REMOVED the `cellRules` dictionary → `internal NoteCell CellOf(StackPanelControl)`; `AddRow` is private `AddRow(NoteCell[])`; a column drag writes `model.widths` and its undo XML comes from `DocumentXml.WriteTable(model)`
- `DocumentControl`: `InsertBlockAfter`/`RemoveBlock` also update the model (`document.blocks`, or the cell's `NoteCell.blocks` through `TableControl.CellOf`) via private static `Inserted<T>`/`Removed<T>`; new `internal Control ViewOf(NoteNode)`; `PutTable` builds `new TableControl(DocumentXml.ReadTable(xml))` and places it via `ViewOf`; `InsertTable`, `DeleteTable`, `RecordTableResize`, `ChangeTable` use `Array.IndexOf(document.blocks, …model)` and `WriteTable(table.model)`
- `DocumentEditorControl.LoadDocument` builds a view per model entry; `SetLayout` walks the view's children; `PageAt` goes through `ViewOf`. `PdfExport` builds its views from the model; `TexEditorControl` click-to-source uses `Array.IndexOf(blocks, CaretBlock.note)`
- `ProfileScenario`, `PerfTests` build model objects; `VaultBrowserControl.CreateNote` writes a one-`NoteBlock` document
- Tests: `TexTests`, `TextInputTests`, `LatinModernTests`, `SheetTests` read the model for headless checks and the editor's views for layout checks (helpers `Content(editor)`, `View(editor, i)`, `Views(...)`); every "destroy the parsed blocks" loop removed. NEW suite `NoteModel.tests.xml` / `NoteModelTests.cs`: `NoteModel.SaveIsStable` (every vault sample note plus an all-features fixture, a `.txt` and a `.tex`: `Save(Load(x)) == Save(Load(Save(Load(x))))`) and `NoteModel.LoadHeadless` (the `UIElements` pool count is unchanged across `DocumentXml.Parse` of the fixture)
- No behaviour change; edits still run through the controls (N2 moves them)

## Why these choices

**Notes need a model because the live note is the control tree.**
Until N1 `RichTextDocument.blocks` was a `List<Control>`; `Run` and `<Document>` exist only at load and save. Sheets (`SheetDocument`, shared per path via `SheetBook.Get`) and planners (`PlannerDocument`, three views) already separate data from view; notes are the only kind that do not. Drivers: the same note open in two workspaces and in two panes at once (Thorium workspace-switcher remake — workspaces as title-bar tabs), headless readers (Link Graph, vault search, backlinks; PDF export today builds a detached `DocumentControl`), and virtualization of large notes.

**Rejected: one shared editor control moved between workspaces (`SetParent` on switch).**
Cheap, but a control has one parent and draws in one place, so it cannot show one note in two panes of one workspace.

**Rejected: replaying edit records into N independent `DocumentControl` copies.**
Cheaper than a model, but only safe if every mutation is a record. `SetPalette`, `SetLayout`, frontmatter edits and the sheet-link refresh are not, and a missed path makes the copies diverge silently with the last save winning.

**Data lives once; views reference it (user fork F1, chosen over views holding synced copies).**
A `BlockControl` holds a reference to its model block and reads it directly (N1), so there is no copy to sync. `TextRunData` is that shared object's text half.

**Names (F2): keep `RichTextDocument` as the model type; new `NoteBlock`, `NoteTable`, `TextRunData`.**

**No stable block IDs.**
Indices plus a change event naming the affected range keep views in sync. IDs only if the Link Graph later needs block anchors.

**N0 invalidation is exactly the old behaviour.**
Text-changing operations invalidate only when the string actually changed (the old `text` setter's `==` check, now `Edited(before)`); `StyleRange`, `SetPicture`, `SetMath` invalidate explicitly as before. Rejected: invalidating unconditionally after every data edit — harmless extra measures, but a behaviour change in a refactor step.

**Remote caret (F3): another view's edit shifts this view's caret and selection through the change.**
Rejected: snapping to the start of the nearest block — no mapping per change kind, but typing in one pane throws the other pane's caret off its word.

**Second view (F4): "Split right / down" on a note tab opens a second editor on the same document; opening from the vault still focuses the existing tab.**

**Virtualization (F5): in the plan as N4 but gated — decide after N3 by measuring a large note.**
Rejected: committing to it up front — `MeasureCore` already skips unchanged blocks and `CollectChildren` draws only what the clip touches, so the gain is unmeasured.
Under virtualization the scroll range must come from the per-view layout cache, not from the children (they are only the visible slice). Today `DocumentEditorControl : ScrollableControl` already sits outside `DocumentControl` and takes its range from `child.Measure` → `contentSize`.

**Arrays, not lists, wherever the dynamism is not hot (user rule, 2026-10-07).**
`RichTextDocument.blocks` is `NoteNode[]`, `NoteTable.widths` `float[]`, `rows` `NoteCell[][]`, `NoteCell.blocks` `NoteBlock[]`. `TextRunData.spans` stays a `List`, because every keystroke rewrites span counts and splits insert into it. Cost: a block insert/remove reallocates the array once per structural edit, and a multi-block paste does it once per pasted block until N2 gives the model a range insert.

**One base type `NoteNode` for blocks and tables.**
It holds the fields both share (`spaceBefore`, `pageBreak`, `insert`). Rejected: `List<object>` — no shared fields, a cast everywhere.

**Views reference the model; nothing is copied.**
`BlockControl`'s block fields and `TableControl`'s state are forwarding properties; `widths` and the rule arrays are the model's own instances, so a column drag writes the model. `TableControl`'s `cellRules` dictionary (keyed by the cell `StackPanelControl`) was replaced by a cell → `NoteCell` map (`CellOf`) because rules belong to the model cell.

**Table normalisation moved into the reader.**
Short rows, over-wide spans and empty cells were fixed up by `TableControl.AddRow`; `DocumentXml.Fit` now does it, so the model already matches what the view shows and what gets written back.

**`AppendRun` is split into a model half and a view half.**
The span/text append is `NoteBlock.AppendRun`, which owns `legacyInkHex`. Restarting a run's text effect is view work: `BlockControl.AppendRun` still does it, and views built from a loaded model call `StartEffects` (`LoadDocument`, `PdfExport`, `TableControl` rows). A split tail or an undo-restored block does not restart effects, as before.

**Round-trip was verified against a pre-change baseline, not against the files on disk.**
Today's writer is not guaranteed to reproduce every file byte for byte. Before N1 the stability test also dumped `Save(Load(x))` for each source; after N1 the same dump diffed empty (`diff -r`, identical). The dump was then removed; the committed check is save stability.

**Thorium tests reach views by walking the editor's children, not through new public engine API.**
`ViewOf` and `Blocks()` stay `internal`.

Extends [[document-undo]]: the records are already data-addressed operations (`DocumentAddress`, redo replays the forward primitive), which N2 retargets at the model.

## Known gaps
- N2–N4 not started; edits still mutate through the controls
- N1 verification: builds clean (0 errors; no warnings in the touched files). Full suite `bash _Build/test.sh`: 253 passed, 3 failed, 40 skipped — the 2 extra passes are the new NoteModel tests; the 3 failures are the same pre-existing ones as the baseline, and every ERROR kind logged is listed in `_Build/test-baseline.txt`. Round-trip output of every vault sample note and the all-features fixture byte-identical before and after N1. Test-verified and golden-verified (all document/table/LaTeX goldens pass unchanged). **Not GUI-verified.** Not perf-measured: `Perf.TypeLargeNote` fails on its pre-existing logged errors either way, so no timing comparison was made
- N1 gaps: array reallocation per structural edit and per block during a multi-block paste (N2 range insert); no perf before/after for the array change; `ViewOf` is a linear scan of the content's children, used only on table rebuilds, `PageAt` and in tests
- N0 verification: builds clean (0 errors, no warnings in `TextRunData.cs`, `TextRunControl.cs`, `BlockControl.cs`). Full suite `bash _Build/test.sh`: 251 passed, 3 failed, 40 skipped — identical to the baseline taken immediately before the change. The document goldens pass unchanged. Test-verified and golden-verified. **Not GUI-verified.**
- 3 pre-existing failures, not caused by N0 and not investigated: `Boot` (1 error logged), `Sheet/Sheet.FixedSize` (golden Page 10060 px differ), `Perf/Perf.TypeLargeNote` (3 errors logged)
- `NoteChange`, `NoteSessions` are PLANNED names; neither exists in code (`NoteNode`, `NoteBlock`, `NoteTable`, `NoteCell` do, since N1)

Related: [[note-model-plan]], [[document-undo]], [[document-tables]], [[document-structural-editing]]
