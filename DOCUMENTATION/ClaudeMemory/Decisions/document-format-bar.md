# Decision — one range-styling primitive, and a bar that never takes the caret

**Date:** 2026-08-30, px field 2026-08-31
**Status:** LANDED. Builds clean; GUI-verified except picking a dropdown entry and the px field
(see Open).
**Scope:** `ArctisAurora.Core.UISystem` (`Glyph`, `GlyphControl`),
`ArctisAurora.Core.UISystem.Controls.Text` (`TextControl`, `TextInputActions`),
`ArctisAurora.Core.UISystem.Controls.Text.Editing` (`TextInputControl`),
`ArctisAurora.Core.UISystem.Controls.Text.Document` (`TextMeasurer`, `Inlines`, `Blocks`,
`DocumentControl`, `DocumentEditorControl`, `DocumentToolbarControl`, `DocumentXml`),
`.Document.Edits` (`StyleRangeEdit`, `BlockSnapshot`),
`Thorium` (`UI.ui.xml`, `TabWindow.ui.xml`, `InputMap.inputs.xml`, `SampleNote.xml`).

## What the bake left unfinished

[[atlas-is-unorm-not-srgb]]'s successor commit baked bold and italic into the family atlas and gave
`Glyph` three metric sets — and then wired **none of it to a run**. Three facts, all found by reading
rather than by a failure:

- `TextControl.SyncGlyphs` built every `GlyphControl` at `FontStyle.Regular`. `TransmuteText`, added
  in the same commit to re-cut a run at another face, had **zero callers**.
- `TextMeasurer.MeasureAdvance` read `glyph.regular.advanceWidth` **unconditionally**.
- `TextInputControl.bold`/`italic` were declared and read by nothing but `StyleEquals`.

So a bold run would have drawn at bold advances and measured at regular ones. Nothing was visibly
wrong only because nothing ever set the style.

**`fontName` swap to `arial-b` was rejected** (the older WIP plan): the family atlas already carries
the metrics, `arial-b` has no italic sibling, and it only works for families that happen to be
registered twice.

## Style is a property of the run, resolved once

`TextControl.glyphStyle` is `protected virtual`, `FontStyle.Regular` at the base; `TextInputControl`
overrides it as `bold ? Bold : italic ? Italic : Regular`. It is read in exactly three places —
`SyncGlyphs`, `RepointGlyphs`, and the `TextMeasurer.Run` built by `Measure`/`OffsetAt`/`CaretAt`.

**Bold wins over italic** because the importer bakes at most three faces; there is no bold-italic
cell to point at. Verified on screen: Ctrl+A then Ctrl+I over the sample note left the pre-existing
bold run bold rather than italicising it.

`IGlyphMetrics` gained `Effective(fontName, style)` so the measurer collapses a missing face the same
way `GlyphControl` does. That is the load-bearing bit — both sides now compute
`glyph.Metrics(atlasMetaData.Effective(style)).advanceWidth * px` from the same asset, so they cannot
drift by construction rather than by care.

`LineBox` deliberately still asks for the family's regular metrics: the line box is a property of the
font, not of the characters on the line, so a bold word must not make its line taller.

`glyphStyle` is a virtual property and not a field because `bold`/`italic` live one class down —
`TextBlockControl` is a *sibling* of `TextControl`, the same reason [[text-styling-types]] declares
`stylingType` twice.

## One primitive for every range restyle

`ApplyStyleBetween(from, to, delta)` — split both ends, apply, merge back. It is the only thing that
restyles text; Ctrl+B, Ctrl+I and the colour picker are all one `StyleDelta` away from each other.

`StyleDelta` is a struct of **nullables**, not a run template. A null member is one the change does
not speak to, which is what lets bold over a multicoloured selection keep every colour in it — and
what a record can carry, unlike an `Action<TextRun>`.

**Character offsets within a block are the addressing, internally.** Run indices are not stable
across the operation: a split renumbers every run after it and the merge renumbers them back. The
*public* signature still takes `DocumentAddress`, because the text is untouched, so the pre-change
addresses resolve identically after undo rebuilds the partition — the conversion to block-relative
offsets happens inside, purely to survive the re-partition.

Order inside a block is **split end, split start, `ApplyLayout`, apply delta, merge**. `ApplyLayout`
sits in the middle on purpose: a run the split just cloned carries no line height, and running it
after the delta would overwrite a font size the delta had just set.

`MergeRuns` compares `previous.stylingType == run.stylingType && previous.StyleEquals(run)`.
**`StyleEquals` does not look at `stylingType`** — the extra clause is in the caller rather than in
`StyleEquals`, so nothing else that may come to depend on that method changes underneath it.

`SplitAt`/`StyleEquals` were dead code written for exactly this and never called. `TextRun.SplitAt`
is `new`, hiding `TextInputControl.SplitAt`, which returns the wrong type for a document and stays as
the pre-existing dead code it was.

## The record is the partition, not a fragment

`StyleRangeEdit` holds the touched blocks' run partition as `List<BlockSnapshot>` and replays the
forward primitive for redo, per [[document-undo]]. A style change moves no text, so the inverse needs
no `DocumentFragment` and no structural surgery — undo destroys those blocks' runs and rebuilds them
from snapshots, and the blocks themselves are untouched because neither primitive adds or removes
one.

**Cost, accepted:** the snapshot is the text of every block the range touched, so Ctrl+A then Ctrl+B
on a large note records the note. Same cost class as a select-all delete's fragment.

Undo puts the caret at the first restored block's start. It does not restore the selection — the same
gap `document-undo.md` already records.

## The bar resolves its target; it does not hold one

`DocumentToolbarControl` owns no editor. Every button calls `TextInputActions.FocusedEditor()` when
pressed — the same walk up from `UICollisionHandling.activeControl` the keybinds use. That only works
because **nothing in the bar takes the active control**: the toolbar and its buttons all override
`takesActiveControl => false`, so the caret's run keeps it and the walk still starts inside the note.

The consequence is that **a tool button acts on press, not release**. `SolveLMBRelease` gates release
on `ActiveTarget(hovering) == activeControl`, which a non-stealing control never satisfies — so no
release ever arrives. `MenuButtonControl` is the precedent; `DropdownControl` acts on release and
therefore could **not** be reused here.

Menu entries capture the editor **at press time** and close over it. A `ContextMenuItemControl` is a
plain `ButtonControl` and *does* take the active control, so an entry that re-resolved the editor
would find the menu instead of the note — the ambient-invoker problem [[context-menu-invoker]] solved
for window actions, avoided here by not being ambient.

**The bar is one per window, above the split — not per editor.** A `TabItem` is `MaxChildren = 1` and
`TabViewControl.EditorOf` is `children[0] as DocumentEditorControl`; wrapping the editor in a stack
panel to seat a bar beside it would break tab close, find and tear-off.

State is reflected in `OnTick`, gated on a cached last-written value. `controlColorHex`'s setter has
no equality guard and would otherwise write the pool every frame for every icon.

`StyleSource` is the run the selection **starts** in, not `caretRun`. The focus end normalizes
forward, so a range ending on a run boundary sits in the run *after* the one it covers, and a toggle
read from there would report the wrong state.

## The px field — the one control that does take the caret

A size box cannot be typed into without the active control, which is the one thing the rest of the
bar must never take. The answer is not to avoid taking it but to **capture the target on the way in
and put the context back on the way out**.

`PxBox` is a private `TextBoxControl` subclass whose only addition is an `onPress` hook. The hook is
the **press**, not the focus: `SolveLMBPress` calls `SetActiveControl` *before* it dispatches
`ResolveOnClick`, so by the time any click handler runs the note has already lost the context — but
nothing has yet touched its selection, so the press is the last moment the range can be read. A
`TextBoxControl.onFocus` was rejected: `NoteNameWindow`, `SettingsWindow` and `EditableLabelControl`
would grow a field none of them use, and the press already reaches the box through `FieldLine`.

**The range is captured as two `DocumentAddress`es, not as a live query.** `ApplyStyle(delta)` reads
`OrderedSelection` at the moment it is called, which for the field is the moment it commits. Nothing
clears a document's selection when it loses the active control today — `LoseFocus` only blurs the
caret — so reading it late *happens* to work, and that is exactly the kind of load-bearing accident
worth removing. `ApplyStyle` is now `SelectedRange(...) && ApplyStyleTo(...)`, and the field holds the
addresses across its own edit.

**A blur is not a cancel.** The previous shape wired `onBlur` to the same handler as Escape, which
called `SetActiveControl(caretRun)` — and `LoseFocus` fires from inside `SetActiveControl`'s
`OnContextRemoved`, *after* the new control is already in the context. Clicking from the field to a
tab or the vault list therefore re-entered and yanked the context back into the note, and the outer
call then sent `OnContextAdded("ActiveControl")` to a control that was no longer active. Blur now
ends the session and touches nothing else: the active control is the user's to place.

Commit and Escape both end the session **before** refocusing, because `FocusCaret` fires the field's
own blur on the way through. The locals are read out first; `EndPx` then makes the re-entry a no-op.

`DocumentControl.FocusCaret` is `SetActiveControl` *plus* `SetCaret`, not either alone. The run
stopped editing when it lost the context (`TextInputControl.OnContextRemoved` calls `CommitEdit`), and
`SetActiveControl` alone early-outs whenever `ApplyStyleBetween` has already raw-assigned
`activeControl` to the post-restyle focus run.

**A collapsed caret changes nothing** (user, 2026-08-31). Applying to the caret's run or block
restyles text nobody selected, and "the size applies to what I type next" is a pending-style feature
that does not exist. Non-numeric input is rejected at commit rather than filtered at the keystroke —
filtering would mean making `TextBoxControl.WriteChar` virtual for one caller.

### What makes a size stick

`fontSizeAuthored` on `TextRun` is the whole difference between a size and a heading.
`ContentBlock.ApplyLayout` writes the scheme's size into every run it finds, and it runs *between* the
split and the delta inside `StyleSpan` — so without the flag the scheme would overwrite the size the
delta was about to set. `StyleDelta.Apply` sets the flag with the size, `TextRun.Clone` and
`BlockSnapshot` both carry it (a split or an undo would otherwise drop it), and `DocumentXml.IsResolved`
stops treating the size as scheme-resolved once it is set, so an authored size round-trips to the file
even when it agrees with the scheme on the day it was saved.

## Icons

Three new SVGs in `AuroraEngine/Data/Icons/default/svg` — `bold`, `italic`, `chevron-down` — filled
paths only, counters wound opposite for nonzero fill, no `A` command. `SvgPath` rejects `fill:none`
outright and does not implement arcs. Winding was checked by rendering the file in a browser before
baking, not after.

Adding them changes the folder hash, so the icon set re-bakes on the next boot of every project.
Thorium's baked output is **tracked** (`Thorium/Data/Icons/default/*`) and was regenerated;
`AuroraEngine/Data/Icons/default/*` and the editor's copy are stale until those apps run — the same
three-physical-copies shape the shaders have.

## Verified

- `dotnet build AuroraEngine/ArctisAurora.sln -t:Rebuild` — 0 errors. Four new warnings, all the
  nullable-annotation shapes the surrounding files already emit in bulk.
- **GUI, on the sample note:** Ctrl+A then Ctrl+B bolded the whole note **and the text re-wrapped at
  bold advances** — which is the measurement half, since the wrap points moved. The selection
  highlight tracked the new wrap exactly.
- **One Ctrl+Z restored all seven blocks exactly**, bold run included, wrap included.
- Ctrl+A then Ctrl+I italicised everything and left the bold run bold.
- The bar renders with the baked icons; B and I light on the accent when the caret's run carries
  them; the styling caption tracks the caret's block (it read `Heading 1` with the caret in the
  heading, `Text` in a paragraph).
- The styling dropdown opens and lists all ten types.

## Open

- **Picking a dropdown entry is not GUI-verified.** Synthetic clicks (`SendInput`) reach the app
  fine but never reach the menu's own window — the menu stayed open and no entry fired. That is
  most likely an artifact of the synthetic driver against a self-hosted menu window rather than a
  product fault, but it means `SetBlockStyling` and the colour path have not been exercised
  end-to-end. **Click one by hand before trusting them.**
- **The px field is not GUI-verified.** Builds clean and the focus path is reasoned end to end, but
  nothing has typed a size into it on screen. Check four things by hand: Enter resizes the selection
  and returns the caret to the note, Escape returns it and changes nothing, clicking from the field
  to the vault list leaves the vault list active, and one Ctrl+Z restores the old size.
- No alignment. It needs a block-level line-width pass that has not existed since the L2 revert —
  runs measure themselves, so no run knows the width of a visual line it shares.
- Strikethrough is still declared and unread, now the only one of the three left that way.
- `Text.Bold`/`Text.Italic` toggle from the selection's first run, so a mixed selection flips to the
  opposite of whatever that run was rather than to all-on.

Related: [[document-undo]], [[document-selection]], [[document-structural-editing]],
[[text-styling-types]], [[text-layout-one-measurer]], [[context-menu-invoker]],
[[button-states-and-hover-bubbling]], [[asset-pipeline-bake]]
