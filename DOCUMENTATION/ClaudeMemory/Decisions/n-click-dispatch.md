# Decision — the release dispatches a tap count, and the receiver filters it

**Date:** 2026-08-23
**Status:** LANDED. Builds clean (0 errors, warning count unchanged at 476). **Not GUI-verified.**
**Scope:** `ArctisAurora.Core.UISystem` (`UICollisionHandling`), `ArctisAurora.Core.UISystem.Controls`
(`VulkanControl`, `EditableTabsControl`), `…Controls.Text.Document` (`TextRun`, `DocumentControl`,
`DocumentEditorControl`).

## What was asked

Generalise the double click into an N-click, move the one control that used a double click onto it as
N = 2, and give the document word selection and line selection. Counts settled by the user
(2026-08-23): **two clicks select the word, three select the visual line.**

## Decisions

### 1. The input layer needed nothing

`KeyStateEntry.tapCount` already counts uncapped within `Input.DoubleClick.Timeout`, and
`Engine.HandleUI` already passed it to `SolveLMBRelease`. The whole cap was one comparison —
`tapCount == 2`. It is now `>= 2`, passing the count through.

### 2. One `Action<int>`, filtered at registration

`onDoubleClick` / `bubbleDoubleClick` / `RegisterOnDoubleClick` / `ResolveOnDoubleClick` are gone —
no shim, one call site to migrate. In their place `onMultiClick` is an `Action<int>` and
`RegisterOnMultiClick(int count, Action action)` wraps the delegate in `taps == count`.

**Rejected: a `Dictionary<int, Action>` per control.** Nicer at the dispatch, but every control in the
tree carries the field, and glyphs are controls. One nullable delegate is what every other event on
`VulkanControl` costs.

Filtering at registration rather than at dispatch is what lets counts mean unrelated things on
different controls without any of them agreeing first: a tab renames on 2 and ignores 3, while the
document under it takes a word on 2 and a line on 3.

### 3. `TextRun` had to be taught to bubble

Glyph, block and `DocumentControl` all `BubbleAll()`; `TextRun` bubbles nothing, and
`TextInputControl.ResolveOnClick` swallows clicks outright (a known hole, worked around by `TextRun`
forwarding clicks by hand). So a double click on document text has always reached the run and died
there — the dispatch existed but nothing downstream of a glyph could ever see it. `TextRun` now sets
`bubbleMultiClick` in its constructor. The flag is on `TextRun`, not `TextControl`, so text boxes are
unaffected.

### 4. A word is a class run, crossing runs but not blocks

Three classes — whitespace, word (`IsLetterOrDigit` or `_`), symbol — so clicking punctuation selects
the punctuation and not the identifier beside it. The walk steps across run boundaries inside the
block via `AdjacentRun`, because a bolded half-word is two runs and one word, and stops at the block
edge, which is what every other range operation already treats as a boundary.

**Rejected: restricting the word to the clicked run.** Half the point of a rich text document is that
styling splits runs mid-word.

### 5. Either side being a word wins

`OffsetAt` returns the *nearest* slot, so clicking the right half of a word's last letter puts the
caret after the word, where the character ahead is a space. Taking the character ahead on its own —
the obvious rule — would select the space and never the word that was clicked. So the class is the
word class if **either** neighbour is a word character, and only otherwise does the character ahead
decide. Both-sides-empty returns without touching the selection.

### 6. The line is Home then Shift+End

`MoveCaret(LineStart)` then `MoveCaret(LineEnd, extend: true)`. No new geometry: both already resolve
a point through `CaretAtPoint`, and both already mean the **visual** line rather than the paragraph.
The user chose visual (2026-08-23), so a wrapped block selects the row that was clicked.

## Consequences and edges

- **Every click still lands first.** The Nth click's press runs `ResolveOnClick` → `SetCaret`, which
  collapses the selection, before the release re-selects. Visible only as the selection being
  rebuilt, not as a flicker, because both happen inside one frame's layout.
- **The word is selected on the release**, not the press, since that is where the count is dispatched
  from — inherited from the double click. Word-drag (holding after the second click and extending by
  whole words) does not exist; the drag extends by character.
- **A click past the end of a line selects nothing.** The release requires
  `ActiveTarget(hovering) == activeControl`, and an off-text click resolves those to different
  controls — `hovering` is the block or the editor while `SetCaret` has repointed `activeControl` at
  the caret's run. Pre-existing to this change; the same guard the double click always had.

## Left out

- `TextBoxControl` — the rename field and the name prompt get no word or line selection.
- `CaretMove.WordLeft` / `WordRight`. The class walk is exactly what Ctrl+Left/Right will want, but
  no bind asks for it yet, so it stays a `DocumentControl` method rather than a caret move.

## Verification

- `dotnet build AuroraEngine/ArctisAurora.sln` — 0 errors, 476 warnings, unchanged from baseline.
- **Not GUI-verified.** What to walk: two clicks on a word select it, including across a bold
  boundary and with punctuation beside it; two clicks on the last letter of a word take the word and
  not the space; three clicks take the visual line of a wrapped block; a tab still renames on two
  clicks and does nothing on three; two fast clicks across two neighbouring tabs still rename
  neither.
