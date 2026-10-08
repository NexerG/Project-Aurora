# Decision — a note gets a data model separate from its controls

**Date:** 2026-10-07
**Status:** landed. N0 and N1 landed 2026-10-07, N2 and N3 landed 2026-10-08; N4 (virtualization) measured and declined (user, 2026-10-08), so the plan is complete. Phases: [[note-model-plan]].
**Scope:** `ArctisAurora.Core.UI` — `TextRunData`, `TextRunControl`, `NoteNode`, `NoteBlock`, `NoteTable`, `NoteCell`, `BlockControl`, `TableControl`, `RichTextDocument`, `DocumentXml`, `DocumentControl`, `DocumentEditorControl`, `PdfExport`, `DocumentEditSession`, `NoteChange`, `NoteChangeKind`, `NoteSessions`, `TexEditorControl`, `TabActions`, `TabViewControl`, `NoteActions`, `SheetLinks`

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
- `TableControl`: `public readonly NoteTable model`; ctor `TableControl(List<float>)` → `TableControl(NoteTable)`; `widths` is `model.widths`; `leftRules`/`rightRules` are the model's arrays; `showBorders`, `spaceBefore`, `alignment`, `cellPadding`, `pageBreak`, `insert` forward to the model; REMOVED the `cellRules` dictionary → `internal NoteCell CellOf(StackPanelControl)` (removed in N2 as an orphan); `AddRow` is private `AddRow(NoteCell[])`; a column drag writes `model.widths` and its undo XML comes from `DocumentXml.WriteTable(model)`
- `DocumentControl` (as built in N1; N2 made `InsertBlockAfter`/`RemoveBlock` view-only and `PutTable` became `ShowTable`): `InsertBlockAfter`/`RemoveBlock` also update the model (`document.blocks`, or the cell's `NoteCell.blocks` through `TableControl.CellOf`) via private static `Inserted<T>`/`Removed<T>`; new `internal Control ViewOf(NoteNode)`; `PutTable` builds `new TableControl(DocumentXml.ReadTable(xml))` and places it via `ViewOf`; `InsertTable`, `DeleteTable`, `RecordTableResize`, `ChangeTable` use `Array.IndexOf(document.blocks, …model)` and `WriteTable(table.model)`
- `DocumentEditorControl.LoadDocument` builds a view per model entry; `SetLayout` walks the view's children; `PageAt` goes through `ViewOf`. `PdfExport` builds its views from the model; `TexEditorControl` click-to-source uses `Array.IndexOf(blocks, CaretBlock.note)`
- `ProfileScenario`, `PerfTests` build model objects; `VaultBrowserControl.CreateNote` writes a one-`NoteBlock` document
- Tests: `TexTests`, `TextInputTests`, `LatinModernTests`, `SheetTests` read the model for headless checks and the editor's views for layout checks (helpers `Content(editor)`, `View(editor, i)`, `Views(...)`); every "destroy the parsed blocks" loop removed. NEW suite `NoteModel.tests.xml` / `NoteModelTests.cs`: `NoteModel.SaveIsStable` (every vault sample note plus an all-features fixture, a `.txt` and a `.tex`: `Save(Load(x)) == Save(Load(Save(Load(x))))`) and `NoteModel.LoadHeadless` (the `UIElements` pool count is unchanged across `DocumentXml.Parse` of the fixture)
- No behaviour change; in N1 edits still ran through the controls (N2 moved them)

## What changed (N2)
All in `ArctisAurora.Core.UI`.
- NEW in `NoteModel.cs`: `enum NoteChangeKind { Text, Spans, Kind, Inserted, Removed, Table, Page, Palette, Layout, Properties, ReadOnly }`; `readonly struct NoteChange` — `kind`, `first`, `count` (flat block index range; a note-level index for `Table`), `at` + `length` (where text went in/out; negative = removed), `anchor?` + `caret?` (where the edit leaves the selection/caret; null leaves it)
- NEW on `RichTextDocument` (region `editing`): `event Action<NoteChange> changed`; `Blocks()` (flat list, table cells included, same order as the view's); `BlockAt(int)` (same lookup without building a list); `Resolve`, `CellOf(NoteBlock)`
- NEW edit functions on `RichTextDocument`: `InsertText(DocumentAddress, string)` and overload `InsertText(NoteBlock, DocumentAddress, string)` for a caller already holding the block, `RemoveText`, `DeleteBetween`, `CaptureFragment`, `InsertFragment(at, fragment, anchor?, caret?)`, `RestoreKind(at, kind, anchor?, caret?)`, `InsertBetween`, `SplitBlockAt`, `JoinBlockWithNext`, `ChangeBlocks(first, last, Action<NoteBlock>)`, `RestoreBlocks`, `SetBlockStylingBetween`, `ApplyStyleBetween`, static `MarkCode(NoteBlock, from, to)`, `SetPicture`, `SetMath`, `RenameSheetLinks`, `PutTable(index, present, xml, caret?)` (returns the new `NoteTable`), `SetPage`, `SetPalette`, `SetLayout`, `SetFrontmatterValue`, `SetReadOnly`
- A multi-block insert is one range insert: one array reallocation per paste
- MOVED from `DocumentControl` to `RichTextDocument`: the whole former `undo primitives` region (`InsertText`, `RemoveText`, `DeleteBetween`, `CaptureFragment`, `InsertFragment`, `RestoreKind`, `InsertBetween`, `JoinBlockWithNext`, `SetTable` → model `PutTable`), plus `SetPicture`, `SetMath`, `RestoreBlocks`, `ApplyStyleBetween`, `SetBlockStylingBetween`, `MarkCode`, the body of `RenameSheetLinks` (the view keeps a one-line forwarder), the `Inserted`/`Removed` array helpers
- `DocumentControl`: `document` is a property that subscribes to `changed`; new `OnDestroy` unsubscribes; region `undo primitives` replaced by region `model changes` (`OnNoteChanged`: adds/removes `BlockControl`s, re-lays out, rebuilds tables, places the caret the change names); view `PutTable` → `ShowTable(int)` (builds/destroys a table viewport to match the model); `InsertBlockAfter`/`RemoveBlock` are view-only (no longer edit `document.blocks`/`NoteCell.blocks`); view `SplitBlockAt` records, then calls the model; `SetBlockList` takes `Action<NoteBlock>` and goes through `ChangeBlocks`; new `BlockAt(int)` walk without a list
- `DocumentControl` caret index: new `caretIndex` field + `CaretIndex` property (cached flat index of `caretBlock`) + DEBUG-only `CheckCaretIndex()` which throws if the cache is stale. `AddressOf` uses the cache for the caret block; `BlockAt` returns `caretBlock` when the index matches; `CaretTo`/`Select` set it; `SetCaret` to another block and every `Inserted`/`Removed`/`Table` change reset it to -1
- `DocumentEditorControl`: `ApplyPage` removed; `SetPage`/`SetPalette`/`SetLayout`/`SetFrontmatterValue`/`SetReadOnly` call the model setters; new `NoteChanged` handler (runs `ApplyPalette` on a `Palette` change), subscribed in `LoadDocument`, unsubscribed in `OnDestroy`. Page/Layout/ReadOnly reactions live in `DocumentControl.OnNoteChanged`
- `DocumentEdits.cs`: all 9 records (`TextEdit`, `SplitEdit`, `StyleRangeEdit`, `DeleteRangeEdit`, `InsertRangeEdit`, `PictureEdit`, `MathEdit`, `BlockStateEdit`, `TableEdit`) hold `RichTextDocument` instead of `DocumentControl`; `PageEdit` holds `RichTextDocument` instead of `DocumentEditorControl`. `DeleteRangeEdit.Undo` passes its anchor/caret into `InsertFragment`/`RestoreKind` instead of calling `DocumentControl.Select`
- `FormulaPopup` calls `document.document.SetMath` (the model)
- REMOVED as orphans: `BlockControl.AppendBlock`, `SliceSnapshot`, `InsertSlice`, `AppendSlice`, `TakeKind`, `From`, `StyleRange`, `AppendSpans`, `SetMath`, `SplitSpanAt`, `MergeSpans`; `TableControl.CellOf(StackPanelControl)`. Kept on `BlockControl` (tests use them): `InsertText`, `RemoveText`, `SplitAt`, `Restore`, `Snapshot`, `SetPicture`, `StyleAt`, `AllSpans`
- Tests: NEW `NoteModel.Changes` and `NoteModel.EditHeadless` in `Thorium.Tests.NoteModelTests`, listed in `NoteModel.tests.xml`

## What changed (N3)
One note can be open in several views at once; every view of a path shares one `DocumentEditSession` (one model, one undo stack, one dirty flag, one save). All in `ArctisAurora.Core.UI`.
- NEW static `NoteSessions` (in `RichTextDocument.cs`, beside `DocumentEditSession`): `Open(string path)` returns the open session for that full path (case-insensitive) with its count raised, or loads one; `Close(DocumentEditSession)` lowers the count and drops the session at 0; `internal Rekey(session, newPath)`
- `DocumentEditSession`: NEW `int views { get; internal set; }` (open views), NEW `internal DocumentControl? lead` (the view that obeys an edit's caret); `Repath` re-keys the session in `NoteSessions`
- NEW `NoteChange.Map(DocumentAddress)` (`NoteModel.cs`): where an address from before the change sits after it
	- `Text`: offsets strictly after `at` shift (an insert exactly at the address leaves it in front); a removal clamps into `at`
	- `Inserted`: later blocks shift down by `count`; the part of `at.block` past `at.offset` moves into the last inserted block at `offset - at.offset + length`
	- `Removed`: later blocks shift up; the last removed block goes to `at.offset + max(0, offset - length)`; other removed blocks and the head past `at.offset` go to `at`
	- other kinds return the address unchanged
- `NoteChange.length` now also carries data on `Inserted` (text in front of the moved tail in the last inserted block: 0 for `SplitBlockAt`, the last fragment block's text length for `InsertFragment`) and on `Removed` (`to.offset` for `DeleteBetween`, 0 for `JoinBlockWithNext`). Before N3 both reported 0
- NEW `DocumentControl.MapAddress(NoteChange, out caretTo, out anchorTo, out pressTo)` (region `model changes`): run before the view applies a change it did not make; after the apply `OnNoteChanged` resolves the mapped addresses back to controls (an address past the end falls back to the last block)
	- a `Spans` change does not re-place the caret (it moves no text, and `SplitBlockAt` raises `Spans` before `Inserted` while the model is already split — placing then would clamp to the shortened head), unless it collapses a picture/formula selection
	- a picture or formula selection that the change touches collapses onto the caret
- NEW `DocumentControl.session` (internal field). `OnNoteChanged` obeys `anchor`/`caret` only when `session == null || session.lead == null || session.lead == this`
- `DocumentEditorControl.LoadPath` goes through `NoteSessions.Open`; NEW private `ReleaseSession()` (clears `lead` if it was this view, `NoteSessions.Close`) called by `LoadPath` and `OnDestroy`. `LoadDocument` makes the view lead when the session has none; `OnContextAdded("ActiveControl")` makes it lead. `NoteChanged` refreshes the properties panel on `Properties` or `ReadOnly` in a view that is not the lead
- REMOVED `DocumentEditorControl.onEdited`; `MarkDirty` only marks the session
- `TexEditorControl` recompiles on `source.activeDocument.changed` (private `Edited(NoteChange)`), subscribed in `LoadPath`, unsubscribed in a NEW `OnDestroy` override, so a second `.tex` view's preview follows edits made in the first
- `TabActions.SplitRight`/`SplitDown` → NEW private `TabActions.Split(edge)`: for a `DocumentEditorControl` or `TexEditorControl` tab it builds a copy through `SessionLayout.tabFactory(path)`, `RestoreView(ViewState())` of the source, then `owner.SplitOff(copy, edge)` (destroys the copy if no pane was made); otherwise `SplitOff(item, edge)` as before. Action descriptions changed. Sheet and planner tabs still move; dragging any tab still moves it
- `TabViewControl.CloseTab`: the naming prompt only when `session.views == 1`
- `NoteActions.discarded` is `HashSet<DocumentEditSession>` (was editors), so an unnamed note open twice asks once at shutdown
- `SheetLinks.RewriteNotes` renames links through the first open tab of a note only (`FindOpenDocument`), not every tab — a shared document would otherwise get a row/column `Shift` applied twice
- Tests: NEW `NoteModel.TwoViews`, `NoteModel.RestoreTwice` in `Thorium.Tests.NoteModelTests`, listed in `NoteModel.tests.xml`
- `ContextMenus.menus.xml` unchanged: `EnabledWhen` is not implemented on the new menu format, so nothing gates the entries

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

**Second view (F4): "Split right / down" on a note tab opens a second editor on the same document; opening from the vault still focuses the existing tab (landed in N3; dragging a tab still moves it).**

**Virtualization (F5, N4): declined (user, 2026-10-08), after measuring a large note as the gate required.**
Measured on the `PerfTests.OpenNote` fixture (1,000 blocks, 1,000,000 characters), Release+PROFILE, `--test=Perf` run 3 times; figures are `Step.Main.Layout` on Main, each range the spread across the 3 runs:
- `Perf.TypeLargeNote`: p50 0.09–0.10 ms, p95 0.28–0.35 ms, max 10.8–12.7 ms (the max is the pre-existing budget failure, see Known gaps)
- `Perf.RewrapLargeNote` (page width changes, every block re-wraps): p50 2.10–2.22 ms, p95 2.55–2.69 ms, max 5.9–6.5 ms; `Text.MeasureBlock` is p50 1.37–1.51 ms at about 1,005 calls per frame; allocation 305 KB per frame, worst frame 10,240 KB
- `Perf.ResizeLargeNote` (window resize, every block moves, the same arrange work as a scroll): p50 0.16–0.17 ms, p95 0.21–0.28 ms, max 0.30–0.38 ms, 0.1 KB per frame
- `--profile-scenario` UI preset over the whole timeline, 3 runs: `Step.Main.Layout` p95 0.71–0.82 ms

Why declined:
- Every steady-state frame on a 1M-character note is already under the 3.3 ms frame target (300 fps); `MeasureCore` already skips unchanged blocks and `CollectChildren` draws only what the clip touches
- The planned cache still has to measure every block, so the dominant cost stays: text measuring in rewrap and its 10 MB worst-frame allocation. N4 would remove only the per-control share, about 0.6 ms of rewrap and about 0.2 ms of scroll or resize
- Building it means every path that holds a `BlockControl` handles blocks that have no control: caret, multi-block selection, `DocumentControl.ViewOf`, tables, floats, sheet links. That is a larger refactor than N2
- Multi-view, the reason for the note model, was delivered by N3; N4 was never needed for it

Rejected: building N4 as planned — gains of 0.2 to 0.6 ms per frame for a refactor bigger than N2.
Rejected: adding a `Perf.OpenLargeNote` test before deciding — the user chose to close N4 without it.
Not measured: open time (`DocumentEditorControl.LoadDocument` builds one `BlockControl` per block in one frame) and memory per view (each view of a note holds its own controls).
Triggers to reopen: opening a large note hitches noticeably, memory per view becomes a problem, or notes much larger than 1M characters become a real use case.
If reopened: under virtualization the scroll range must come from the per-view layout cache, not from the children (they are only the visible slice). Today `DocumentEditorControl : ScrollableControl` already sits outside `DocumentControl` and takes its range from `child.Measure` → `contentSize`.

**Arrays, not lists, wherever the dynamism is not hot (user rule, 2026-10-07).**
`RichTextDocument.blocks` is `NoteNode[]`, `NoteTable.widths` `float[]`, `rows` `NoteCell[][]`, `NoteCell.blocks` `NoteBlock[]`. `TextRunData.spans` stays a `List`, because every keystroke rewrites span counts and splits insert into it. Cost: a block insert/remove reallocates the array once per structural edit, and a multi-block paste did it once per pasted block until N2 gave the model a range insert (now one reallocation per paste).

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

**N2: edits are addressed by flat position (`DocumentAddress`), not by reference.**
Undo records must outlive controls, and a second view (N3) has its own controls. The model raises a change; the view reacts. Derived state (list numbers, syntax colour, sheet-link values) stays view-side.

**N2: caret placement after an edit travels in the change (`NoteChange.anchor`/`caret`).**
The edit function sets it on its last change; in N2 the one view always obeys it, N3 adds "only the view that started the edit obeys". Rejected: a separate `RichTextDocument.placed` event raised by the records — a second channel for the same moment. This adds `anchor`/`caret` to the `NoteChange` shape the plan wrote as (kind, block range, address, length).

**N2: `NoteSessions.Open/Close` (one ref-counted `DocumentEditSession` per path) was deferred to N3 (user, 2026-10-07) and landed there.**
In N2 only one view per path can exist, so the ref count is always 1 and nothing exercises it.

**N2: the model's edit functions live in a region of `RichTextDocument.cs`, not a new file.**
A new file would have made the class `partial`.

**N2: frontmatter moved onto the model and raises `Properties` although nothing listened until N3 (user chose the plan as written); N3's `DocumentEditorControl.NoteChanged` is the first listener.**

**N2: `NoteChange` is a struct so typing allocates no event objects.**

**N2: the caret caches its flat block index, and `TypeChar` passes `caretBlock.note` to the model (user's direction: the caret already knows its block and where to type).**
Measured Release+PROFILE, `--profile-scenario` on a 1M-char note, `Scenario.Type` zone on Main, 3 runs each side, HEAD and working tree run alternately.
- First cut built a flat list per lookup (model resolve, view re-measure, view caret placement — three new lists per keystroke on top of the pre-existing `AddressOf` one): p95 0.083/0.074/0.089 → 0.219/0.223/0.320 ms, allocation per typing frame 1.7 → 6.2 KB (worst 18.4 → 67.2), Main alloc 5.9 → 10.5 KB/frame. A real regression: every run's p95 moved
- Option A, walk without building a list (`BlockAt`): allocation back to 1.7 KB, but p95 0.080/0.073/0.094 → 0.099/0.156/0.114 ms — four linear walks per keystroke remain
- Rejected B: cache a flat position → block list on each side, rebuilt on structural change — removes all walks but adds state to keep valid, and N3's second view adds more
- Chosen (the cached index): `Scenario.Type` p50 0.047/0.052/0.051 → 0.007/0.007/0.007 ms, p95 0.087/0.085/0.095 → 0.014/0.019/0.014 ms, allocation per typing frame 1.7 → 0.2 KB, Main alloc over the whole scenario 6.1/5.9/5.9 → 4.5/4.5/4.5 KB/frame. Over the typing phase alone (frames I 30..152): `Step.Main.Logic` p50 ~0.05 → ~0.009 ms, Main alloc 19.5 → 3.8 KB/frame; frame time unchanged (p50 ~1.6–1.7 ms, p95 ~4.7–5.3 ms, max ~10–12 ms), dominated by `Step.Main.DrawLists` (~0.95 ms/frame) and `Step.Main.Layout` (~0.8 ms/frame). Typing is faster than before N2

**N3: "Split right / down" on a note makes a second view; dragging still moves (fork 1, user, 2026-10-08).**
Rejected: keep Split as a move and add "Open to the right / below" entries — two near-identical entries in the tab menu.

**N3: the view that obeys an edit's caret is `session.lead` (fork 2, user).**
Set when its editor becomes the `ActiveControl` and on first load. A single view always obeys, so N2 behaviour is unchanged. Rejected: no stored state, obey when the editor is under `UIEngine.activeControl` or is the only view — it still needs the view count, and headless tests set no active control.

**N3: a remote insert exactly at another view's caret leaves that caret in front of the insert (fork 3, user).**
Rejected: move the caret past it.

**N3: Tex recompile listens to `changed` and `onEdited` is deleted (fork 4, user).**
Rejected: keep `onEdited` and add `changed` alongside it — two triggers for the same moment.

**N3: mapping is done in address space.**
Map the pre-change flat address, apply the change, resolve back: views keep their own `BlockControl`s and a removed block's control is destroyed by the apply.

**N3: `Inserted`/`Removed` carry the tail offset in `length`.**
To map a split, paste, join or range delete exactly. Extends N2's `NoteChange` shape again.

**N3: closing a view that is not the last still saves when dirty.**
It writes the shared model, which is harmless; it only skips the naming prompt.

**N3: `NoteSessions` lives in `RichTextDocument.cs` next to `DocumentEditSession`, not a new file.**

Extends [[document-undo]]: the records are already data-addressed operations (`DocumentAddress`, redo replays the forward primitive), which N2 retargeted at the model: the nine records and `PageEdit` hold `RichTextDocument`, not a view.

## Known gaps
- N4 measured and declined 2026-10-08 (no code changed; perf-measured, Release+PROFILE, 3 runs). Open and unrelated to N4: `Perf.TypeLargeNote` still fails its 8 ms Max (pre-existing) — in the failing frame `Document.MeasureBlocks` is about 5–6 ms and `Layout.Arrange` about 4.6–5 ms while `Text.MeasureBlock` stays at 0.01 ms, so text measuring is not the cause; not investigated
- Rewrap's 10 MB worst-frame allocation in `Text.MeasureBlock` is the real large-note cost; still open, separate from N4
- Open time and memory per view of a large note are unmeasured
- N3 verification: builds clean (0 errors; the warnings in touched files are pre-existing, a CS0108 on `MarkDirty` checked against HEAD). Test-verified: full Thorium suite `bash _Build/test.sh` 257 passed / 3 failed / 40 skipped; the 3 are the pre-existing baseline (`Boot`, `Sheet/Sheet.FixedSize`, `Perf/Perf.TypeLargeNote`), the 2 extra passes are `NoteModel.TwoViews` and `NoteModel.RestoreTwice` (before the change 255 / 3 / 40)
	- `TwoViews` checks: one session for two views of a path (`views == 2`); typing in A (real keystrokes via `t.Type`) shows in B; B's caret stays put; an insert before B's caret in its block shifts it; an insert at B's caret leaves it in front; a split above moves it down a block; a join above moves it back; a split before it carries it into the tail; a join brings it back; a paste before it carries it into the last pasted block; Ctrl+Z in B reverts A's typing and A's caret follows the text back; deleting B's caret block clamps B's caret to the cut; destroying A keeps B working with `views == 1`; clearing the tree drops `views` to 0
	- `RestoreTwice`: two tabs built through `SessionLayout.tabFactory` for one path share one session, and each keeps the caret its `SessionTab` restored
	- One throwaway in-process readback (deleted afterwards): two editors side by side on one session after A typed and pressed Enter — both panes show the same text, B's caret drawn in front of the same word. Not via the tab split path (a bare `TabViewControl` in a test does not lay out its editor)
	- **NOT GUI-verified**: `--send` runs only parameterless actions and "Split right/down" acts on the tab button the context menu was opened on, so the menu path was not driven; no real-window run. Not perf-measured (a remote `Text` change elsewhere costs the other view an index compare; DEBUG `CheckCaretIndex` walks)
- N3 gaps: the tab-menu split itself is untested (no test drives the context menu); `Tab.Split` was exercised only by reading the code and by the equivalent `tabFactory` + `SplitOff` calls
- N3 gaps: text rewritten inside `ChangeBlocks` (inline markdown) or `RestoreBlocks`, and a `Table` rebuild, map another view's caret by clamping (same flat index, offset clamped), not precisely
- N3 gaps: `RenameSheetLinks` raises `Spans` after rewriting link text; another view's caret is not re-clamped there (as before N3 for the single view)
- N3 gaps: `NoteActions.SettleWindow` on a torn-off window holding one view of a note still open elsewhere still prompts/saves for it
- N3 gaps: an in-progress picture resize/rotate/move in one view is not cancelled by a remote change; only the selection collapses
- N3 gaps: a view that was never focused shows its caret drawn (the caret control starts focused); unchanged from before
- N3 gaps: an editor that is never destroyed keeps its session (and its in-memory edits) alive; a later `LoadPath` of the same path gets that session, not the file
- N2 verification: test-verified and golden-verified — full Thorium `--test` suite 255 passed / 3 failed / 40 skipped; the 3 failures are the pre-existing baseline (`Boot`, `Sheet/Sheet.FixedSize`, `Perf/Perf.TypeLargeNote`), the 2 extra passes are the new tests; the DEBUG stale-caret-index check never fired in the run log. Perf-measured as above. **NOT GUI-verified** (no real-window typing, picture drag, table edit, undo)
- N2 gaps: picture live resize/rotate/move now re-select the picture on every pointer move (the edit function's change carries the selection); it is already selected, so expected to look identical — unverified by eye
- N2 gaps: blocks added by a paste/undo now copy their neighbour's paint (`CopyPaint`), as a split always did; invisible unless a block has non-default paint- N2 gaps: live column resize still writes `NoteTable.widths` directly with no change raised; the drag's commit goes through `TableEdit`
- N2 gaps: paste, delete, Enter and other non-typing paths still resolve by walking (a list build for non-caret blocks); not per keystroke
- N2 gaps: `caretIndex` is correct only while every change to block order arrives as a model change; the DEBUG `CheckCaretIndex` throws otherwise
- N2 gaps: the model's flat order must equal the view's flat order (cells in `TableControl` children order = model rows/cells order)
- For N3's address mapping: text rewritten inside `ChangeBlocks` (inline markdown) reports as `Kind` without `at`/`length`; a split reports `Spans` + `Inserted` with `at` = the split point
- `ApplyStyleBetween` raises one `Spans` change over many blocks; the view then builds one block list
- N1 verification: builds clean (0 errors; no warnings in the touched files). Full suite `bash _Build/test.sh`: 253 passed, 3 failed, 40 skipped — the 2 extra passes are the new NoteModel tests; the 3 failures are the same pre-existing ones as the baseline, and every ERROR kind logged is listed in `_Build/test-baseline.txt`. Round-trip output of every vault sample note and the all-features fixture byte-identical before and after N1. Test-verified and golden-verified (all document/table/LaTeX goldens pass unchanged). **Not GUI-verified.** Not perf-measured: `Perf.TypeLargeNote` fails on its pre-existing logged errors either way, so no timing comparison was made
- N1 gaps: array reallocation per structural edit (a multi-block paste is one range insert since N2); no perf before/after for the array change; `ViewOf` is a linear scan of the content's children, used only on table rebuilds, `PageAt` and in tests
- N0 verification: builds clean (0 errors, no warnings in `TextRunData.cs`, `TextRunControl.cs`, `BlockControl.cs`). Full suite `bash _Build/test.sh`: 251 passed, 3 failed, 40 skipped — identical to the baseline taken immediately before the change. The document goldens pass unchanged. Test-verified and golden-verified. **Not GUI-verified.**
- 3 pre-existing failures, not caused by N0 and not investigated: `Boot` (1 error logged), `Sheet/Sheet.FixedSize` (golden Page 10060 px differ), `Perf/Perf.TypeLargeNote` (3 errors logged)
- `NoteSessions` exists since N3; `NoteChange`, `NoteChangeKind` exist since N2, `NoteNode`, `NoteBlock`, `NoteTable`, `NoteCell` since N1

Related: [[note-model-plan]], [[document-undo]], [[document-tables]], [[document-structural-editing]]
