# Note model plan — N0–N4, a data model under the note controls

**Status:** agreed (user, 2026-10-07). N0 and N1 landed 2026-10-07; N2–N4 not started.
Decision record: [note-model](../Decisions/note-model.md). Why: the Thorium workspace-switcher remake (workspaces = Blender-style title-bar tabs, design in the "Workspace Switcher" canvas artifact) needs the same note open in two workspaces and in two panes side by side; the user chose to build a note model first rather than share one editor control between workspaces.

## N0 — LANDED 2026-10-07
- `TextRunData` extracted from `TextRunControl`; `data` holds `text` and `spans`; `text`, `spans`, `Length` forward to it
- `BlockControl`'s pure text/span operations moved onto `TextRunData`; `BlockControl` keeps same-signature public forwarders and does the layout invalidation
- `TextRunControl.Edited(string before)` re-measures only when the string changed
- No behaviour change. Test-verified and golden-verified; **not GUI-verified**. Suite 251 passed / 3 failed / 40 skipped, identical to the baseline (3 failures pre-exist: `Boot`, `Sheet/Sheet.FixedSize`, `Perf/Perf.TypeLargeNote`)

## N1 — LANDED 2026-10-07 — model types; load and save through them
**Built:**
- NEW `NoteModel.cs`: `NoteNode` (base: `spaceBefore`, `pageBreak`, `insert`), `NoteBlock` (`run` + block fields + `AppendRun`/`SplitAt`/`Snapshot`/`SliceSnapshot`/`Restore`/`TakeKind`/`From`), `NoteCell` (`span`, `rules`, `NoteBlock[] blocks`), `NoteTable` (`float[] widths`, rule arrays, `NoteCell[][] rows`, borders, alignment, padding)
- `RichTextDocument.blocks` is `NoteNode[]`; `DocumentXml` reads and writes the model (`Fit` normalises table rows); headless `Load` builds no controls
- `BlockControl.note` and `TableControl.model` are references, block/table fields forward to them; `TableControl.cellRules` replaced by `CellOf`; `DocumentControl` keeps the model in step in `InsertBlockAfter`/`RemoveBlock`/`PutTable`; `ViewOf(NoteNode)` finds a model entry's view
- Arrays, not lists, wherever the dynamism is not hot (user rule): `TextRunData.spans` stays a `List`
- Verify: suite 253 passed / 3 failed / 40 skipped (the 3 are the pre-existing baseline failures; the 2 extra passes are `NoteModel.SaveIsStable`, `NoteModel.LoadHeadless`); round-trip of every vault sample note and the all-features fixture byte-identical before and after. Test-verified and golden-verified; **not GUI-verified**; not perf-measured

**Planned (original):**
- `NoteBlock` = `TextRunData` + block fields moved off `BlockControl`: `stylingType`, `alignment`, `firstIndent`, `spaceBefore`, `pageBreak`, `pageStyle`, `markLeft`/`markRight`, `insert`, `language`, `codeWrap`, list state
- `NoteTable` = columns (width, rules), rows, cells (span, rules, `List<NoteBlock>`)
- `RichTextDocument.blocks` becomes the model list
- `DocumentXml.Parse`/`ToXml`, `MarkdownFormat`, `PlainTextFormat`, `TexSourceFormat` read and write the model — as built, only `DocumentXml` changed: the three formats convert text ↔ `<Document>` XML and reach the model through it
- `BlockControl(NoteBlock)` reads through; `DocumentControl` builds its view from the model
- Headless `RichTextDocument.Load` creates no controls
- Verify: suite + goldens; round-trip test loading and saving every note under `Thorium/Data/Notes` and the test fixtures byte-identically; a test that headless Load allocates no controls
- `SplitAt`, `Snapshot`, `SliceSnapshot`, `Restore`, `TakeKind`, `From` move here

## N2 — edits go to the model, views only react
- Primitives move from `DocumentControl` (`undo primitives` region: `InsertText`, `RemoveText`, `DeleteBetween`, `InsertBetween`, `SplitBlockAt`, `JoinBlockWithNext`, `RestoreBlocks`, `SetPicture`, `SetMath`, `SetTable`) plus page / palette / layout / frontmatter / read-only setters onto `RichTextDocument`
- New `RichTextDocument.changed` carrying a `NoteChange` (kind, block range, address, length)
- Edit records' `document` field changes type `DocumentControl` → `RichTextDocument`; post-undo caret placement stays with the view that ran it
- `DocumentControl`'s 23 direct block-mutation sites plus the table, picture, float and math regions route through the model
- On `changed` the view adds/removes `BlockControl`s, re-measures changed blocks, re-paginates from the changed range
- Derived state (list numbers, syntax colour, sheet-link values) stays view-side
- `DocumentEditSession` (path, undo, dirty) becomes one per path via `NoteSessions.Open(path)`/`Close`, ref-counted
- Verify: suite + goldens + undo tests; tests that each primitive emits the right `NoteChange`
- The model needs a range insert: `NoteNode[]` reallocates once per insert, so a multi-block paste reallocates once per pasted block until then
- **Risky phase** — `DocumentControl` is 4,258 lines with 157 `BlockControl` references; land region by region, suite green between

## N3 — several views of one note
- `DocumentControl.MapAddress(NoteChange)` shifts caret, selection anchor and text-drag anchor
- A remote change touching a selected picture or formula clears that selection
- Tab menu "Split right / down" for notes; opening from the vault still focuses the existing tab
- Rename/repath reach every view (`TabViewControl.FindOpenDocuments` already walks all)
- One save; the last view closing releases the session; `TexEditorControl`'s source editor shares likewise
- Verify: two views on one session — typing in A shows in B, undo in B reverts A, B's caret shifts after an insert above, deleting B's caret block clamps, closing A keeps B, closing both releases, session restore of a note open twice; one GUI run via `--send`

## N4 — virtualization (gated)
- Decide after N3 by measuring a large note
- Per-view layout cache of per-block heights/lines from `TextMeasurer.MeasureBlock(IReadOnlyList<Run>, …)` (already data-in); only viewport + margin blocks become `BlockControl`s
- Scroll range from the cache, not from the children; `Paginate` over the cache
- Verify: `aurora-perf` before/after on a large note

## After
- The workspace plan resumes with real multi-view
- Sharing undo per path for sheets and caching `PlannerDocument` per path join its phase W2

## Left out
- Block IDs, collaborative/CRDT editing, reload on external file change, any on-disk format change (output stays byte-identical)

Related: [[note-model]], [[document-undo]], [[planner-plan]]
