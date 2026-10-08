# Note model plan — N0–N4, a data model under the note controls

**Status:** agreed (user, 2026-10-07). N0 and N1 landed 2026-10-07, N2 and N3 landed 2026-10-08; N4 measured and declined 2026-10-08 (user). The plan is complete.
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
- `BlockControl.note` and `TableControl.model` are references, block/table fields forward to them; `TableControl.cellRules` replaced by `CellOf`; `DocumentControl` kept the model in step in `InsertBlockAfter`/`RemoveBlock`/`PutTable` (N1 only; N2 made them view-only); `ViewOf(NoteNode)` finds a model entry's view
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

## N2 — LANDED 2026-10-08 — edits go to the model, views only react
**Built:**
- Every edit goes through `RichTextDocument` (region `editing`: `InsertText`, `RemoveText`, `DeleteBetween`, `InsertFragment`, `RestoreKind`, `SplitBlockAt`, `JoinBlockWithNext`, `ChangeBlocks`, `RestoreBlocks`, `ApplyStyleBetween`, `SetPicture`, `SetMath`, `PutTable`, `SetPage`, `SetPalette`, `SetLayout`, `SetFrontmatterValue`, `SetReadOnly`, …), which raises `changed(NoteChange)`; `DocumentControl.OnNoteChanged` (region `model changes`) only reacts
- NEW `NoteChange` struct + `NoteChangeKind` enum; the shape gained `anchor?`/`caret?` over the planned (kind, block range, address, length), so the caret an edit leaves travels in the change
- Range insert on the model: a multi-block paste is one array reallocation
- All 9 edit records and `PageEdit` hold `RichTextDocument`; `DocumentControl.document` is a property that subscribes; `DocumentEditorControl.ApplyPage` gone, its `NoteChanged` handler runs `ApplyPalette`
- Typing: the caret caches its flat block index (`DocumentControl.caretIndex`), `TypeChar` hands the model the block; `Scenario.Type` p95 0.087 → 0.014 ms, allocation per typing frame 1.7 → 0.2 KB
- Verify: full Thorium suite 255 passed / 3 failed / 40 skipped (the 3 are the pre-existing baseline; 2 extra passes are `NoteModel.Changes`, `NoteModel.EditHeadless`). Test-verified, golden-verified, perf-measured; **NOT GUI-verified**
- Detail and the perf runs: [note-model](../Decisions/note-model.md)

**Planned (original):**
- Primitives move from `DocumentControl` (`undo primitives` region: `InsertText`, `RemoveText`, `DeleteBetween`, `InsertBetween`, `SplitBlockAt`, `JoinBlockWithNext`, `RestoreBlocks`, `SetPicture`, `SetMath`, `SetTable`) plus page / palette / layout / frontmatter / read-only setters onto `RichTextDocument`
- New `RichTextDocument.changed` carrying a `NoteChange` (kind, block range, address, length)
- Edit records' `document` field changes type `DocumentControl` → `RichTextDocument`; post-undo caret placement stays with the view that ran it
- `DocumentControl`'s 23 direct block-mutation sites plus the table, picture, float and math regions route through the model
- On `changed` the view adds/removes `BlockControl`s, re-measures changed blocks, re-paginates from the changed range
- Derived state (list numbers, syntax colour, sheet-link values) stays view-side
- `DocumentEditSession` as one per path via `NoteSessions.Open(path)`/`Close`, ref-counted — moved to N3 (user, 2026-10-07): in N2 only one view per path exists, so the ref count is always 1
- Verify: suite + goldens + undo tests; tests that each primitive emits the right `NoteChange`
- The model needs a range insert (built in N2): `NoteNode[]` reallocated once per insert, so a multi-block paste reallocated once per pasted block
- **Risky phase** — `DocumentControl` is 4,258 lines with 157 `BlockControl` references; land region by region, suite green between

## N3 — LANDED 2026-10-08 — several views of one note
**Built:**
- NEW `NoteSessions` (static, in `RichTextDocument.cs`): `Open(path)`/`Close(session)`, ref-counted, one `DocumentEditSession` per path (case-insensitive); `DocumentEditSession.views`, `lead`; `Repath` re-keys
- NEW `NoteChange.Map(DocumentAddress)`; `NoteChange.length` also carries the tail offset on `Inserted`/`Removed`; NEW `DocumentControl.MapAddress`, which shifts caret, selection anchor and text-drag press; a picture/formula selection the change touches collapses onto the caret
- Only `session.lead` (the view whose editor was last the `ActiveControl`) obeys a change's `anchor`/`caret`; a single view always does
- `DocumentEditorControl.onEdited` removed; `TexEditorControl` recompiles on `changed`
- Tab menu "Split right / down" on a note or `.tex` tab opens a second view of the note (`TabActions.Split`); sheet and planner tabs and dragging still move
- Naming prompt on close only when `session.views == 1`; `NoteActions.discarded` holds sessions; `SheetLinks.RewriteNotes` goes through the first open tab only
- Verify: full Thorium suite 257 passed / 3 failed / 40 skipped (3 pre-existing baseline; 2 extra passes are `NoteModel.TwoViews`, `NoteModel.RestoreTwice`). Test-verified; **NOT GUI-verified** (the menu path was not driven); not perf-measured
- Detail: [note-model](../Decisions/note-model.md)

**Planned (original):**
- `DocumentControl.MapAddress(NoteChange)` shifts caret, selection anchor and text-drag anchor
- A remote change touching a selected picture or formula clears that selection
- Tab menu "Split right / down" for notes; opening from the vault still focuses the existing tab
- Rename/repath reach every view (`TabViewControl.FindOpenDocuments` already walks all)
- `NoteSessions.Open(path)`/`Close` — one ref-counted `DocumentEditSession` per path (moved here from N2)
- Only the view that started an edit obeys the change's `anchor`/`caret`; in N2 the one view always obeys
- One save; the last view closing releases the session; `TexEditorControl`'s source editor shares likewise
- Verify: two views on one session — typing in A shows in B, undo in B reverts A, B's caret shifts after an insert above, deleting B's caret block clamps, closing A keeps B, closing both releases, session restore of a note open twice; one GUI run via `--send`

## N4 — DECLINED 2026-10-08 — virtualization
**Measured:** 1,000-block / 1,000,000-character note (`PerfTests.OpenNote`), Release+PROFILE, `--test=Perf` 3 runs, `Step.Main.Layout` on Main
- `Perf.TypeLargeNote`: p50 0.09–0.10 ms, p95 0.28–0.35 ms, max 10.8–12.7 ms (max is the pre-existing budget failure)
- `Perf.RewrapLargeNote`: p50 2.10–2.22 ms, p95 2.55–2.69 ms, max 5.9–6.5 ms; `Text.MeasureBlock` p50 1.37–1.51 ms at about 1,005 calls; 305 KB per frame, worst frame 10,240 KB
- `Perf.ResizeLargeNote` (same arrange work as a scroll): p50 0.16–0.17 ms, p95 0.21–0.28 ms, max 0.30–0.38 ms
- `--profile-scenario` UI preset, whole timeline: p95 0.71–0.82 ms

**Why declined:**
- Every steady-state frame is already under the 3.3 ms target (300 fps); `MeasureCore` skips unchanged blocks and `CollectChildren` draws only what the clip touches
- The cache still measures every block, so the dominant cost (text measuring in rewrap, 10 MB worst-frame allocation) stays; N4 removes only about 0.6 ms of rewrap and about 0.2 ms of scroll or resize
- Every path holding a `BlockControl` (caret, multi-block selection, `DocumentControl.ViewOf`, tables, floats, sheet links) would handle controlless blocks — a larger refactor than N2
- N3 already delivered multi-view
- Not measured: open time and memory per view. Reopen if a large note opens with a hitch, memory per view bites, or notes far beyond 1M characters become real
- Detail: [note-model](../Decisions/note-model.md)

**Planned (original):**
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
