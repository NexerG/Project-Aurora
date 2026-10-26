# Decision — sorted checklist is a ListKind value, ticking permutes the run

**Date:** 2026-10-22
**Scope:** `ArctisAurora.Core.UI` — `ListKind`, `BlockControl`, `DocumentControl`, `DocumentEditorControl`, `InspectorControl`, `DocumentToolbarControl`, `NoteFormats`

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
- Inspector "Checked" row (2026-10-23): `InspectorControl` fields `checkedCaption` (`LabelControl`), `checkedEnd` (`SwitchControl`, options Bottom, Top), `shownChecked` (`SortedTasksSetting.CheckedEnd?`). `ReflectNote` shows caption + switch only when the List shape is `SortedTasks` and keeps `checkedEnd.selected` in sync with the setting. Picking writes `SettingsRegistry.Get<UISettings>().sortedTasks.checkedEnd`, calls `SettingsRegistry.Commit()` (saved to disk, as the Settings window does), then `editor.SortCaretChecklist()`.
- `DocumentEditorControl.SortCaretChecklist()` (public): `BeginStep("Sort checklist")`, `content.SortChecklistAt(index of caret block)`, `MarkDirty()` only when something moved.
- `DocumentControl.SortChecklistAt(int index)` (internal, returns bool): finds the block's run via `SortedGroups` and calls `SortChecklists(runFirst, runLast)`, so nested sorted sub-lists are re-sorted too.
- `DocumentControl.SortChecklists(int, int)` and `ReorderGroups`: void → bool (true when anything moved; `ReorderGroups` false when the order was unchanged, true after a restore). `SetCheckedSorted` and `ToggleList` ignore the result.
- Test: `Inspector.Docs` (`Thorium` `RibbonTests`, Ribbon suite) extended: the note has a sorted list after the first paragraph; the row is hidden outside it, shows Bottom inside, picking Top sets the setting and re-sorts (sub-items included), one undo restores the order.
- Slide (2026-10-23): every list item whose content moved glides from where its content was drawn to its new spot (0.25 s, CubicOut).
- `BlockControl.reveal` (public float) — `[A_Animatable(typeof(ArrangeData), nameof(ArrangeData.reveal), LayoutChange.Arrange)]`, setter `InvalidateArrange()`; 1 at the old spot, 0 at its own. Reuses the `ArrangeData.reveal` slot (`ContextMenuControl`, `ExpanderControl` give it their own meanings; `BlockControl` did not use it).
- `BlockControl.slideFrom` (internal `float?`) — old drawn screen y, set by the document; `slideDistance` (private float).
- `BlockControl.ArrangeCore` — on the first arrange after `slideFrom` is set, `slideDistance = slideFrom - finalRect.y` and `slideFrom` clears; every arrange offsets `finalRect.y` by `slideDistance * reveal` before `base.ArrangeCore`, so text, list marker, code/rule ground and the caret (`TextOrigin`) all follow.
- `DocumentControl.ReorderGroups` — records each moved block's current `arrangedRect.y` (by its new position) before `RichTextDocument.RestoreBlocks`; after the restore, for each block whose content moved: sets `slideFrom`, `reveal = 1`, `Animations.Tween(block, nameof(BlockControl.reveal), Vector4.Zero, slideSeconds, Curve.Ease(EaseKind.CubicOut), onDone removes it from sliding)`.
- `DocumentControl.slideSeconds` (private const 0.25f); `DocumentControl.sliding` (private `List<BlockControl>`) — flow blocks (parent is the document) still sliding.
- `DocumentControl.CollectChildren` — after the culled block range, also collects any block in `sliding` whose index lies outside that range, so an item whose destination (or origin) is off screen still draws while it travels.
- Test: `TextInput.SortedChecklist` extended — one frame after a tick the moved item's `reveal` > 0; 20 frames later `reveal` is 0, it moved down from its old y monotonically, and the item that took its slot ended exactly at the old y.
- No data XML changes.

## Why these choices

**A new `ListKind` value, not a per-block `Sorted` flag.**
.axml serialization comes free through the existing enum parse, and `ToggleList` already handles switching kinds. A flag would have meant a new field on `NoteBlock`, `BlockSnapshot`, `TakeKind`, plus a new XML attribute and its own toggle logic. Cost: every `== ListKind.Task` site must match `SortedTask` too (marker, numbering, toolbar, inspector, Markdown writer).

**The move is a permutation, not a block insert/remove.**
Every item in the run is already a list block, so block controls stay put and their contents are permuted: snapshot the run, reorder, `RestoreBlocks`. The tick and the move are one `BlockStateEdit`, so one undo reverses both.

**Sub-items move with their parent** (like Apple Reminders) rather than ending the run.

**Markdown has no syntax for it.** An .md note saves it as a plain task list and reopens as `Task`. Accepted loss.

**Nothing re-sorts on typing.** An item added with Enter inside the ticked section stays until the next tick.

**The end is an app setting, not per list.** Per list would have been the per-block flag the `ListKind` choice avoided (`NoteBlock`, `BlockSnapshot`, `TakeKind`, `DocumentXml`, inspector). Flipping it in the Settings window re-sorts nothing: existing lists keep their order until the next tick or conversion, which places its item by the new rule. Flipping it from the inspector re-sorts the caret's list (see below).

**The inspector only surfaces the app-wide end; it does not make it per list.**
The row appears only while the caret is in a sorted list. Rejected: forcing the inspector open when a sorted list is pressed, as intrusive — every click in a list would open the column. The row is hidden outside a sorted list and does not change whether the inspector is shown.

**An inspector flip re-sorts the whole run at the caret's level, nested sorted sub-lists included.**
Same range convention `ToggleList` uses. Rejected: re-sorting only the caret's level (`SortChecklists(first, first)`) — nested sub-lists would keep the old order after a flip. `MarkDirty` runs only when something moved, so a flip on an already-ordered list does not mark the note modified. The re-sort is one undo step, "Sort checklist".

**The slide is an arrange offset on the block, driven by a 1→0 progress tween started at the reorder (click time), not a tween of a pixel offset.**
The new top is only known after the next measure/paginate, and starting a tween during `Main.Layout` would write animation rows from the layout step. So the block captures the pixel distance itself on its first arrange (old y − new y) and scales it by `reveal`. Rejected: precomputing the new tops at reorder time from heights — pagination gaps make it inexact.

**It reuses `ArrangeData.reveal` instead of a new `ArrangeData` field.**
A new field would grow every UIElements row's `ArrangeData` for one control's use; `reveal` is already a per-control slot with control-specific meaning.

**The offset lives in `BlockControl.ArrangeCore`, not `DocumentControl.ArrangeCore`,** so lists inside table cells slide too.

**Every block whose content moved slides (FLIP-style), not only the ticked item.**
Otherwise the neighbours jump a row while it glides.

**It lives in `ReorderGroups`,** so tick, untick, the inspector Bottom/Top flip and conversion to a sorted list all slide.

**Old y is the y as currently drawn** (`arrangedRect.y`, slide included), so re-ticking mid-slide continues from where the item is.

**Culling needs the `sliding` list.**
`CollectChildren` culls by final `blockTops`, so without `sliding` an item headed off screen would vanish at the tick. Only flow blocks are tracked; table cells cull their own.

## Known gaps
- Slide: undo/redo still jump (they go through `BlockStateEdit`, not `ReorderGroups`).
- Slide: moving items overlap their neighbours while passing; no fade, no z reordering.
- Slide: inside table cells it runs but the culling fix does not apply (the table culls its own blocks).
- Slide: the off-screen culling path is not covered by any test; the whole effect is not GUI-verified.
- If the caret is in a nested sorted sub-list, only that sub-list's run (and deeper) is re-sorted, not the parent list.
- Inspector "Checked" row: test-verified (`Inspector.Docs`, Ribbon suite 11 passed, only the known baseline Boot failure). Not GUI-verified in a live window.
- Inspector "Sorted" option: the List switch's click path is test-verified (`Inspector.Docs`); the fit is resolved (inspector column back to 240 px). Not GUI-verified in a live window.
- Undo restores contents but does not move the caret back with the item.
- Other views of the same note (split session) keep their caret on the block index, not the item.
- No keybind or toolbar button of its own.

Verified: `TextInput.SortedChecklist` passes (tick, untick past a sub-item, caret follows, undo/redo one step each, .axml, Markdown, conversion sorts and undoes in one step; with `Checked="Top"` tick, untick and conversion mirror). Slide: test-verified, `TextInput.SortedChecklist` passes with the slide checks (TextInput suite 99 passed, 1 failed — the known baseline Boot sampler-asset error; no `[Vulkan]` lines); not GUI-verified in a live window. Settings window switch not GUI-verified. Rest of the TextInput suite passes apart from the known baseline Boot sampler-asset error.

Related: [[list-markers]], [[note-file-formats]], [[workspaces]]
