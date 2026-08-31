# Decision — undo is inverse data records, and redo replays the forward operation

**Date:** 2026-08-22
**Status:** LANDED. Solution builds clean (0 errors, no warnings from the new or changed files);
`Thorium` boots to all three threads with `Text.Undo` and `Text.Redo` resolving at
`InputHandler.LoadInputs`, which throws on a name it cannot bind. **Not GUI-verified** — no undo has
been pressed.
**Scope:** new `ArctisAurora.Core.Editing`, new
`ArctisAurora.Core.UISystem.Controls.Text.Document.Edits`, plus `DocumentControl`,
`DocumentEditorControl`, `DocumentEditSession`, `TextInputActions`, and
`Thorium/Data/XML/Documents/Inputs/InputMap.xml`.

## What was there before

Nothing. [[document-structural-editing]] closed with "undo does not exist, and deletion is the first
thing that makes that hurt". [[engine-side-text-input]] had already recorded the intended shape:
revert is a reload from disk, and undo, when it comes, is an edit log over the live tree.

## The forks, as the user settled them

- **Record shape: inverse data records.** Rejected do/undo closures — a closure captures control
  references, which are dead the moment undo rebuilds a run, and it cannot be inspected, so it can
  never coalesce. `SystemCommand`'s own header already makes this argument for the pool commands.
- **Granularity: one press, one undo.** No merge predicate, no idle timer, no word-boundary rule, no
  break signals. Coalescing is deferred, not designed-out — the records are inspectable, which is
  what a later merge pass needs.
- **Precise inverses, not block snapshots.** The alternative stored the touched blocks before and
  after, which is one generic undo function that cannot drift, at the cost of copying a whole
  paragraph twice for a Backspace that merely crossed a run boundary.

## Decisions

### 1. Redo replays the forward primitive; only Undo is hand-written

After an undo the document is back in its pre-edit state, so the addresses recorded before the edit
resolve again. `DeleteRangeEdit.Redo` is `DeleteRange(from, to)` and `SplitEdit.Redo` is
`SplitBlockAt(at)` — the same code the keypress ran. Half the duplication risk of a precise inverse
disappears, because only one of the two directions is a second implementation.

The consequence is that **`UndoStack._applying` is load-bearing, not insurance**: redo re-enters the
recording primitives, and without the guard it would grow the undo stack while consuming the redo
stack.

### 2. Every conditional in the forward code becomes a recorded bit

`DeleteRange` keeps an emptied head run but destroys an emptied tail run; `SplitBlockAt` drops its
carried clone when it is empty *and* other runs moved in. The inverses do not re-decide either — they
read `DocumentFragment.tailRunDestroyed` and `SplitEdit.carriedRunDropped`, recorded from the branch
that actually ran. If a forward rule is changed later, records made under the old one still reverse
correctly.

This is the governing rule of the whole design. A precise inverse is only safe while it never
re-derives a decision the forward path made.

### 3. Addresses are indices, because Destroy is one-way

`Entity.Destroy()` sets `_destroyed`, detaches, and queues for `ProcessDestroys` → `pool.Free`. There
is no revive, so a record cannot hold a run reference and re-attach it. `DocumentAddress` is
`(block, run, offset)`, resolved through `Blocks()` and a run index inside the block.

Addresses are stable across an undo by construction: the head run keeps its block and run index
through a delete, and `InsertFragment` rebuilds the tail block with its own destroyed runs ahead of
the tail run, so the tail run lands back on the index it had.

### 4. `DocumentFragment` is the deleted content and nothing else

`blocks[0].runs[0]` is always the text cut out of the head run and `blocks[^1].runs[^1]` is always
the text cut out of the tail run; everything between is a run destroyed whole, split into one
snapshot per block the range touched. More than one block means the range crossed a block boundary.

The surviving context is never copied, which is the whole point against snapshots: a Backspace that
crosses a run boundary in a 500-character paragraph records about ten bytes rather than two copies of
the paragraph.

`spannedBlocks` was in the plan and is **not** in the code — it is `blocks.Count > 1`, and a recorded
copy of a derivable fact is exactly the duplicate state decision 2 exists to avoid.

**`DocumentFragment` + `InsertFragment` is also what cut/copy/paste wants**, which is unbuilt and
coming. It is the argument that turned the fork: the snapshot design would have built nothing
reusable, and paste will be the cross-check `InsertFragment` does not have today.

### 5. A scope is mandatory even at one-press-one-undo

`TextInputActions.Write` calls `DeleteSelection()` and then types; `DocumentControl.SplitBlock` does
the same before splitting. One press, two mutations. Recorded per-mutation, Ctrl+Z would restore the
deleted selection and leave the typed characters — the half-undo the user ruled out. `EditScope`
collects a step, so those land as one.

Scopes are opened where "one press" is visible: inside `DocumentEditorControl.Backspace`/`Delete`/
`SplitBlock`, which are 1:1 with an action, and inside `TextInputActions.Write`'s drain loop, which
is not — `Text.Write` is bound `<Continuous/>` and drains the whole character queue, so it opens one
scope per character.

Scopes do not nest anywhere today. `UndoStack` reference-counts depth anyway, so a future nested call
folds into the outer step rather than committing a fragment of one.

### 6. `CollapseSelection` had to move inside the drain loop

It ran once after the loop. With `DeleteSelection()` now per-iteration, the second character would
see the anchor the first left behind and delete what was just typed — [[document-selection]]
decision 8's trap, one line away.

### 7. History hangs off `DocumentEditSession`

One open note, one stack. Tabs already give two live sessions, and a global stack would let Ctrl+Z in
one tab reach the note in another. `DocumentControl.undo` is assigned from the session in
`LoadDocument`; a note with no session gets `default(EditScope)`, which discards.

### 8. ~~Undo does not scroll to the caret~~ — SUPERSEDED 2026-08-23

It shipped without one, for the reason `SplitBlock` had none ([[document-structural-editing]]
decision 7): a rebuilt block has no `arrangedRect` until the next layout pass, and `ScrollIntoView`
reads a zero rect as "above the viewport". `Undo` and `Redo` now request a deferred scroll like every
other editing path — see [[document-caret-scrolling]].

### 9. `Ctrl+Y` for redo, and `<Press/>` rather than `<Repeat/>`

`Ctrl+Shift+Z` works, but only if it is declared **before** `Ctrl+Z` in the XML: `InputHandler.Update`
processes both in pass 0 in list order, and `IsShadowed` only skips the *later*, less specific bind.
Declared the other way round both fire and undo is immediately redone. Left out rather than made
order-dependent.

`<Repeat/>` fires at `KeyRepeat.Rate`, 0.03s, so holding Ctrl+Z would burn some thirty granular steps
a second.

## Verified

- Builds clean; the 476 warnings are pre-existing and none are in the new or changed files.
- `Thorium` boots through all 24 bootstrap steps to all three threads. `InputHandler.LoadInputs`
  throws on an action name it cannot resolve, so `Text.Undo` and `Text.Redo` binding is a real check.
- `XSDGenerator` regenerated `actionSchema.xsd` with both new action names.
- **Not GUI-verified:** every behaviour. Nothing has been undone by hand.

## Still open

- **`InsertFragment` has no forward counterpart to cross-check it.** ~50 lines that exist only to be
  the inverse of `DeleteRange`, exercised by nothing else until paste lands. The cross-block case
  with a destroyed tail run is where to look first.
- **Holding Backspace is one undo step per repeat firing** — `<Repeat/>` at `KeyRepeat.Rate`, 0.03s,
  so a two-second hold is roughly 60 steps against a 500-step cap. This is what one-press-one-undo
  means, and it is the first thing coalescing would fix. Worth re-checking now that repeat actually
  works — key repeat had never fired at all until 2026-08-23, so this was theoretical.
- **Undo restores the caret, not the selection.** Undoing a range delete brings the text back with
  the caret at the start of it. Re-selecting needs an anchor field on `DeleteRangeEdit`. It does now
  scroll to that caret — see [[document-caret-scrolling]].
- **Undoing a forward `Delete` leaves the caret after the restored character** rather than before it,
  because `RunTextEdit` puts the caret at the end of what it re-inserts. Correct for Backspace, one
  character off for Delete.
- **No undo in standalone `TextBoxControl`** — the rename field and the note-name prompt keep
  Escape-to-cancel and nothing else.
- **`isDirty` is not tied to the stack position**, so undoing back to the saved state still reads as
  edited.
- **A non-`ContentBlock` block would be rebuilt as a `ContentBlock`.** `ContentBlock` is the only
  concrete `Block` today; lists and quotes will need `BuildBlock` to carry a kind.
- **No `Ctrl+A`**, which sits next to undo on the WIP list and is independent of all of this.

Related: [[document-structural-editing]], [[document-selection]], [[engine-side-text-input]],
[[ui-data-control-split]]
