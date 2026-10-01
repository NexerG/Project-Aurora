# Decision — a text drag carries the source's drop marker, so any editor under the pointer can take it

**Date:** 2026-09-29
**Status:** LANDED. Test-verified (`TextInput.DragMovesText`, `TextInput.DragBetweenNotes`). **Ctrl-drop copy
and dragging into a torn-off window NOT verified** — the test context cannot hold a key through a drag, and only
the primary window is testable.
**Scope:** `ArctisAurora.Core.UI` — `DocumentControl` (region `text drag`, `PressAt`, `DropSelection`,
`InsertAt`, `ArrangeCaretAt`), `DocumentEditorControl` (region `text drop`); `ArctisAurora.EngineWork` —
`InputModifier.Copy`

## What changed
- A press inside the selection (inclusive of its ends, Extend not held) starts a text drag instead of placing
  the caret; the selection must sit in one container (`SelectedFragment`), else the press is an ordinary one
- The dragged control is the source `DocumentControl`'s `dropCaret` (a second `CaretControl`,
  `hitTestable = false`). `DocumentControl.TextDragSource(dragged)` recognises it
- `DocumentEditorControl.DraggingOver` shows its own `dropCaret` at `CaretOffText(point)` and auto-scrolls;
  `FinishDrag` takes the drop:
  - same note → `DropSelection(slot, copy)`: delete, shift the drop address past the removed range, insert,
    select — one `Move`/`Copy` step. A slot inside the selection is a click: caret there
  - other note → `InsertAt` under the target's step, then the source's `DeleteSelection` under the **source's**
    step. Two histories, as Word does. The target takes the focus
- `InputModifier.Copy` held at release copies instead of moving (Thorium binds both Ctrls)
- A release no editor took, with the drag never over an editor (`textDragHovered`), is a click at the press
  slot — a click has no pointer movement, so `CheckDrag` never assigns a target

## Why these choices

**The marker is the drag token, not the editor.** `UIEngine.CheckDrag` hit-tests with the dragged subtree
skipped. The existing selection drag starts on the editor, so a text drag started the same way could never be
dropped back into its own note. A leaf control with no children removes nothing from the hit test, and
`DraggingOver`/`FinishDrag` then serve the same note, another tab, a split and another window identically.

**Cross-note moves record in both histories.** One step spanning two sessions would let Ctrl+Z in one tab
edit a note in another — exactly what [[document-undo]] decision 7 rules out.

**Delete before insert within one note, then shift the address.** Inserting first would need the range
addresses shifted instead; the drop address is one value, the range is two plus the anchor.

## Known gaps
- No drag preview (`DragGhost`) of the text — the marker is the only feedback
- A drag started across a table edge is refused (the press places the caret)
- Ctrl-drop and torn-off-window drops are unverified (above)

Related: [[document-selection]], [[text-clipboard]], [[document-undo]], [[ui-engine-stack]]
