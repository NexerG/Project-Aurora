# Decision — sorted checklist is a ListKind value, ticking permutes the run

**Date:** 2026-10-22
**Scope:** `ArctisAurora.Core.UI` — `ListKind`, `DocumentControl`, `DocumentEditorControl`, `InspectorControl`, `DocumentToolbarControl`, `NoteFormats`

## What changed
- `ListKind.SortedTask` beside `Task`. Same `CheckBoxControl` marker; ends numbering like `Task` (`DocumentControl.RenumberLists`).
- Behaviour: tick moves the item after the last unticked group (top of the ticked); untick moves it before the first ticked group (bottom of the unticked). Sub-items (deeper levels) move with their parent.
- Which end the ticked items go is app-wide: `UI.SortedTasksSetting` (`UISettings.sortedTasks`, `<UI><SortedTasks Checked="Bottom|Top"/>`, default `Bottom`), a Bottom | Top `SwitchControl` in the Settings window ([[switch-controls]]). `Top` mirrors everything: ticked above unticked, a tick lands at the bottom of the ticked, an untick at the top of the unticked — always the boundary. `SetCheckedSorted`/`SortChecklists` compare `isChecked != top` ("second section") instead of `isChecked`.
- `DocumentControl.SetCheckedSorted(int index, bool value)` (internal) — ticks and moves the item.
- `DocumentControl.SortChecklists(int first, int last)` (internal) — sorts every sorted checklist starting in the range, deepest level first so nested runs stay sorted when outer groups move.
- `DocumentControl.SortedGroups` (private static) — finds the run: unbroken blocks in the same container; items at the ticked item's level are `SortedTask`, deeper blocks belong to the group above; one group per item at that level.
- `DocumentControl.ReorderGroups` (private) — rewrites the run's block snapshots in the new order via `RichTextDocument.RestoreBlocks`, pushes one `BlockStateEdit`, re-points the caret at its item if it moved.
- `DocumentControl.ToggleList(ListKind.SortedTask, …)` calls `SortChecklists` after converting (ticked at the set end, order kept), in the same undo step.
- `DocumentEditorControl.SetChecked` routes `SortedTask` blocks to `SetCheckedSorted`; new `ToggleSortedTasks()` ("Sorted task list" undo step).
- `InspectorControl`: "Sorted" option (`ListShape.SortedTasks`) of the List `SwitchControl` in Paragraph → List ([[switch-controls]]). `DocumentToolbarControl`: tasks button lights for `SortedTask` too.
- `NoteFormats` Markdown writer writes `SortedTask` as `- [ ]` / `- [x]`.
- .axml: `<Block List="SortedTask">`; no new attribute, `DocumentXml` reads/writes `List` through `Enum.TryParse`/`ToString`.
- Test: `TextInput.SortedChecklist` (`TextInputTests`, listed in `TextInput.tests.axml`).

## Why these choices

**A new `ListKind` value, not a per-block `Sorted` flag.**
.axml serialization comes free through the existing enum parse, and `ToggleList` already handles switching kinds. A flag would have meant a new field on `NoteBlock`, `BlockSnapshot`, `TakeKind`, plus a new XML attribute and its own toggle logic. Cost: every `== ListKind.Task` site must match `SortedTask` too (marker, numbering, toolbar, inspector, Markdown writer).

**The move is a permutation, not a block insert/remove.**
Every item in the run is already a list block, so block controls stay put and their contents are permuted: snapshot the run, reorder, `RestoreBlocks`. The tick and the move are one `BlockStateEdit`, so one undo reverses both.

**Sub-items move with their parent** (like Apple Reminders) rather than ending the run.

**Markdown has no syntax for it.** An .md note saves it as a plain task list and reopens as `Task`. Accepted loss.

**Nothing re-sorts on typing.** An item added with Enter inside the ticked section stays until the next tick.

**The end is an app setting, not per list.** Per list would have been the per-block flag the `ListKind` choice avoided (`NoteBlock`, `BlockSnapshot`, `TakeKind`, `DocumentXml`, inspector). Flipping it re-sorts nothing: existing lists keep their order until the next tick or conversion, which places its item by the new rule.

## Known gaps
- Inspector "Sorted" option: the List switch's click path is test-verified (`Inspector.Docs`); the fit is resolved (inspector column back to 240 px). Not GUI-verified in a live window.
- Undo restores contents but does not move the caret back with the item.
- Other views of the same note (split session) keep their caret on the block index, not the item.
- No keybind or toolbar button of its own.

Verified: `TextInput.SortedChecklist` passes (tick, untick past a sub-item, caret follows, undo/redo one step each, .axml, Markdown, conversion sorts and undoes in one step; with `Checked="Top"` tick, untick and conversion mirror). Settings window switch not GUI-verified. Rest of the TextInput suite passes apart from the known baseline Boot sampler-asset error.

Related: [[list-markers]], [[note-file-formats]], [[workspaces]]
