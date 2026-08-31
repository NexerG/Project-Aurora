# Decision — a style chosen with nothing selected is armed, not discarded

**Date:** 2026-08-31
**Status:** LANDED. Builds clean, 0 errors, no new warnings. **NOT GUI-verified.**
**Scope:** `ArctisAurora.Core.UISystem.Controls.Text.Document` (`DocumentControl`,
`DocumentEditorControl`, `DocumentToolbarControl`),
`ArctisAurora.Core.UISystem.Controls.Text` (`TextInputActions`)

Reverses **"a collapsed caret changes nothing"** in [[document-format-bar]] (user, 2026-08-31),
retracted by the user the same day: bold, italic, a size and a colour picked with nothing selected
now hold and are spent on the next character typed.

## What changed
- `StyleDelta` gains `With(over)` — merge, the newer one wins wherever it speaks — and
  `Changes(run)` — would applying this move anything.
- New `CaretStyle` struct: `bold`, `italic`, `strikethrough`, `colorHex`, `fontSize`, all
  non-nullable, built from a `TextRun` with a `StyleDelta` laid over it.
- `DocumentControl.pending`, a `StyleDelta`. Armed by `ArmStyle`, cleared by the three-argument
  `SetCaret`, spent by `TypeChar`.
- `StyleSource` changes type `TextRun?` → `CaretStyle?` on both `DocumentControl` and
  `DocumentEditorControl`. It picks the same run it always did.
- `ApplyStyle` falls back to `ArmStyle` when `SelectedRange` finds nothing, and returns false —
  nothing was written, so the note is not dirty.
- `TextInputActions.Toggle` takes `Func<CaretStyle, StyleDelta>`; both action descriptions say
  "over the selection, or for what is typed next".
- The bar's px field arms instead of doing nothing when its captured range was empty.
- The colour entry calls `FocusCaret` after applying, so an armed colour is typeable.

## Why these choices

**The armed style is a `StyleDelta`, not a detached `TextRun`.**
A run holding the pending style would have let `StyleSource`, the bar and `Toggle` all stay
untouched, since they only read style fields off a run. Rejected: `TextRun` is an `Entity`, and
`Entity`'s constructor allocates a `DataPool` slot and adds itself to the `Entities` group with a
start enqueued. A UI state flag would have been ticked every frame and held pool memory for as long
as it was armed.

**Nothing is inserted into the document at arm time.**
The other shape is to split the caret's run immediately and put an empty styled run at the boundary,
which needs no consumption step at all. Rejected on three counts: `Normalize` walks a slot forward
past any run whose length it meets, so a zero-length run is a caret slot that cannot be occupied;
`MergeRuns` folds it away on the next style operation; and an arm the user never spends leaves a
stray `<Run />` in the saved note.

**The character is written first and then restyled, rather than typed into a new run.**
`TypeChar` writes into the run the caret is already in — unchanged — and then calls `ApplyStyleTo`
over the one character it just wrote. This buys the whole feature with **no new edit record**: the
split, the merge and the caret re-resolve are `ApplyStyleBetween`'s, and the records are a
`RunTextEdit` plus a `StyleRangeEdit` that both join the open `Typing` step.

Undo order works out rather than needing care. `EditStep` reverses, so the `StyleRangeEdit` undoes
first — restoring the block's partition with the character still in it — and the `RunTextEdit`
undoes second and removes it. The caret restore that stands is the text record's, which is the
character's own position, not `RestoreBlocks`' block start.

The cost is one split/apply/merge over one block per **armed** character. It is paid once: after the
first character the caret sits in a run that carries the style, so the arm is spent and everything
after it types normally.

**`CaretStyle` is resolved, so nothing that reads it knows an arm exists.**
The alternative was to expose `pending` and merge at each call site — four `??` in the bar's
`OnTick`, and a two-argument delegate in `Toggle`. A resolved struct keeps both sites reading
the same shape they read before, which is why the diff in `DocumentToolbarControl` is two lines.

**Cleared by `SetCaret(run, offset, extend)`, deliberately not by `SetCaret(run)`.**
Every caret *move* funnels through the three-argument overload; the one-argument overload has
exactly two callers, itself and `FocusCaret`. That split is load-bearing: the px field takes the
active control to be typed into and hands it back with `FocusCaret`, so clearing in the
one-argument overload would disarm the size on the way out of the field that set it.

**An arm that agrees with the caret's run is dropped rather than held.**
`ArmStyle` merges, then discards the result when `Changes(run)` is false. This is what makes a
second Ctrl+B disarm — `Toggle` reads `CaretStyle`, sees the armed `bold: true`, and arms
`bold: false`, which the run already is — instead of pinning the run's own style as a change.

## Known gaps
- **Not GUI-verified.** Nothing has typed an armed character on screen.
- An arm dies on **any** caret move, Enter and Backspace included. Word keeps it across both.
  Matching that needs the arm pinned to a caret position, which was not asked for.
- Typing an armed size equal to the run's current size disarms, so it does not set
  `fontSizeAuthored` and the run stays scheme-resolved. Choosing 16 on a 16px run is a no-op.
- `Text.Bold`/`Text.Italic` over a *mixed selection* still flip from the first run's state — the
  gap [[document-format-bar]] already records, untouched here.
- The styling dropdown was already caret-only through `SetBlockStyling` and is unaffected.

Related: [[document-format-bar]], [[document-undo]], [[document-selection]],
[[document-structural-editing]], [[text-styling-types]], [[entity-lifecycle-queues]]
