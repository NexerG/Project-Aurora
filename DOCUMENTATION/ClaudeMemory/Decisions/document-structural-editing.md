# Decision — deletion is one range operation, and Enter is its inverse

**Date:** 2026-08-17
**Status:** LANDED (P4 step 2). Builds; `Periodic` boots to all three threads with no stderr and all
three new actions resolve. **Not GUI-verified.**
**Scope:** `ArctisAurora.Core.UISystem.Controls.Text.Document` (`DocumentControl`,
`DocumentEditorControl`, `TextRun`), `...Controls.Text` (`TextInputActions`),
`Periodic/Data/XML/Documents/Inputs/InputMap.xml`.

## Context

Before this, a note could be typed into and never shortened: `TextInputControl.Backspace()` and
`Delete()` existed but nothing bound them, there was no `Text.Backspace` action, and `Enter` did
nothing at all — no block-level insert, remove, split or merge existed anywhere in `Document/`.
Selection had landed the day before and mutated nothing.

## Decisions

### 1. Backspace and Delete are a caret move plus a range delete

There is one deletion primitive, `DeleteRange(from, to)`, and everything routes through it. With no
selection, Backspace/Delete first call `MoveCaret(Left|Right, extend: true)` — which leaves the
anchor and moves the focus — and then delete the one-slot selection that produces.

This is not a shortcut, it is what keeps the boundary rules in one place. `MoveLeft`/`MoveRight`
already know that a run boundary inside a block is one caret slot and a block boundary is two, that
a left step out of a run lands at the previous run's end, and that `Normalize` claims the canonical
slot at a shared boundary. Writing "the character before the caret" separately means writing all of
that a second time, in a second place, and the two drift the first time either is touched.

It also makes the block-merge case fall out rather than being handled: Backspace at the start of a
block selects the zero-character range from the previous block's end to this block's start, and a
range spanning two blocks merges them by construction.

**Rejected:** `TextInputControl.Backspace()`/`Delete()`, which mutate one run's `text` and know
nothing about runs, blocks or selection. They are still there, still unbound, and are now the
single-line-textbox path rather than the document one.

### 2. The head run keeps the caret even when it keeps no text

`DeleteRange` truncates the head run to its prefix and the tail run to its suffix, destroys every run
strictly between them, and puts the caret at the head run's prefix end. An emptied *tail* run is
destroyed; an emptied *head* run is not.

The asymmetry is deliberate. The caret has to land somewhere, and a run carries the style that the
next character typed will take — Word and Obsidian both keep the formatting at the caret after
deleting a selection. Keeping the head run keeps that style; destroying it would silently hand the
caret to a neighbouring run with different formatting. The tail run has no such claim, and leaving
empty runs behind grows the saved file by `<Run />` elements that read back as real runs.

The head run always survives, so the block can never end up with zero runs — the case that would
make `Normalize`, `OrderedRuns` and `RunAt(0)` all answer for nothing.

### 3. A cross-block delete collapses into the *head* block, which keeps its styling type

The tail block's surviving runs move into the head block; every block from the second to the tail
inclusive is destroyed. The merged block therefore keeps the head block's `StylingType` — deleting
from a Heading1 into the paragraph below leaves a heading, not a paragraph. Same rule as every
editor: the first block wins.

`ApplyLayout` is re-run on the head block afterwards, or the moved runs would keep the font size the
scheme resolved for the block they came from.

### 4. Enter splits the block and the new block takes the same styling type

`SplitBlock` clones the caret's run for its style, gives the clone the text from the caret onward,
truncates the original to the text before it, and moves every run after it in the block across whole.
The clone is dropped when it is empty *and* other runs moved in — otherwise it stays, because an
empty block still needs one run to hold a caret.

The new block takes the old block's `stylingType` rather than falling back to `Text`. Splitting a
heading mid-word has to give two headings; the alternative rule ("Enter at the *end* of a heading
gives a paragraph") is a second rule keyed on caret position, and it is a one-line change to
`SplitBlock` if it is wanted later. Not built on the Simplicity-First rule.

### 5. `DocumentControl` holds the `RichTextDocument`, because the block list is duplicated state

The blocks are simultaneously `RichTextDocument.blocks` (what `DocumentXml.Save` writes from) and
children of `DocumentControl` (what `Measure`/`Arrange`/`OrderedRuns` walk). Same objects, two lists.
Every structural edit therefore updates both, which needs the model reference the control did not
have.

**Rejected: rebuilding `document.blocks` from the control tree at save time.** Fewer moving parts at
the edit site, and it matches the recorded "the model is the control tree" decision — but it leaves
`document.blocks` silently stale for the whole session for any other reader, and the vault browser
and undo are both going to be readers. Explicit sync fails loudly instead.

The duplication itself is the P0 model-as-controls decision showing through again, and it goes away
with [[ui-data-control-split]], not here.

### 6. Typing over a selection deletes it first

`Text.Write` calls `DeleteSelection()` before draining the character queue, and re-resolves the
target afterwards — the caret's run changes when the range crossed runs. The existing
`CollapseSelection()` at the end stays: `WriteChar` advances `cursorPosition` directly, so without it
typing selects what it just typed.

### 7. Enter does not scroll to the caret

Every caret move ends in `ScrollIntoView`. A split cannot: the new block has no `arrangedRect` until
the next layout pass, and `ScrollIntoView` reads a zero rect as "above the viewport" and scrolls the
note to the top. So `SplitBlock` skips it.

**Consequence, accepted:** pressing Enter on the last visible line leaves the caret just below the
viewport until something else scrolls. The fix is a scroll-on-next-Arrange flag, which needs an
`Arrange` override on `DocumentEditorControl` that does not otherwise exist.

### 8. `TextRun.Clone()` was dropping `stylingType`

Pre-existing and latent: `Clone` copies bold/italic/strikethrough/colour/font/size/text and its only
callers were `ContentBlock.Clone` → `RichTextDocument.Clone`, which nothing calls. `SplitBlock` made
it live — splitting a run with its own styling type produced a half that had lost it. One field
added.

### 9. Plain `Backspace` sits under the existing `Ctrl+Backspace`

`InputMap.xml` already binds `Ctrl+Backspace` to `ExitApplication`. Modified binds are evaluated in
the first pass and consume the trigger, and `IsShadowed` then skips the unmodified bind, so the two
coexist without a change to either. Backspace and Delete use `<Repeat />`; so does Enter, so holding
it makes paragraphs the way holding a letter makes letters.

## Verified

- Builds clean (0 errors; the 583 warnings are pre-existing).
- `Periodic` boots through `InputHandler.LoadInputs` to all three threads with no stderr. That step
  **throws** on an action name it cannot resolve, so it is a real check that `Text.Backspace`,
  `Text.Delete` and `Text.NewBlock` bound.
- **Not GUI-verified:** every behaviour above. Nothing here was exercised by hand.

## Still open

- **Undo does not exist**, and deletion is the first thing that makes that hurt — a mis-aimed
  Ctrl+A-less drag delete is unrecoverable except by reloading the note and losing the session.
- **No `Ctrl+A`.** There is no select-all action, so a whole-note delete means dragging.
- **Empty runs can still accumulate** through the head-run rule in decision 2: delete a run's whole
  contents without crossing into another run and the empty head run stays for the rest of the
  session, and saves as `<Run />`. A split at a run boundary leaves one behind the same way.
- **A split builds the caret run's glyphs twice.** `Clone()` copies `text`, so the clone builds a
  full glyph set before `SplitBlock` trims it to the suffix — Enter in a 500-character paragraph
  allocates 500 `GlyphControl`s and frees roughly half. One-off per keystroke against a load that
  already builds tens of thousands, so it is noted rather than fixed; the fix is a style-only clone.
- **Step 3 — Ctrl+B/I** is unchanged by this: `bold`/`italic` are still read by nothing, and the run
  split it needs is now written (`SplitBlock`'s clone-and-carry), but at block granularity rather
  than mid-run style granularity.

Related: [[document-selection]], [[engine-side-text-input]], [[text-layout-one-measurer]],
[[periodic-editor-architecture]]
