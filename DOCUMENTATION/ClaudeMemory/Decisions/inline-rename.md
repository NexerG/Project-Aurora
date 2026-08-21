# Decision — a row's name is a label that becomes a field, and a rename resyncs by walking the tree

**Date:** 2026-08-21
**Status:** LANDED. Builds clean; **not GUI-verified.**
**Scope:** `ArctisAurora.Core.UISystem.Controls.Text` (`EditableLabelControl`, `TextBoxControl`,
`TextInputControl`), `ArctisAurora.Core.UISystem` (`UICollisionHandling`),
`ArctisAurora.Core.UISystem.Controls.Containers` (`FileBrowserControl`, `FileRowControl`,
`TabViewControl`), `ArctisAurora.Core.UISystem.Controls.Text.Document` (`DocumentEditSession`),
`Periodic` (`VaultBrowserControl`).

## What shipped

`Rename note` on a vault row turns the row's name into a one-line field with the name selected.
Enter commits, Escape restores, clicking away commits. A committed rename moves the file, rewrites
the `Name` inside it, and repoints every editor already holding the note open.

## Decisions

### 1. The control holds both parts for its whole life; it does not swap them in the tree

`EditableLabelControl` owns a `LabelControl` and a `TextBoxControl` from construction and shows one
at a time through `Hide()`/`Show()`. `Measure`/`Arrange` touch only the visible one, so the hidden
part keeps the collapsed clip `Hide()` gave it — the same arrangement `TabViewControl` uses for
inactive pages.

**Rejected: replace the label with a box in the parent's child list.** `AddChild` appends; there is
no insert-at-index on any container, so a swap either adds one to `StackPanelControl` or reorders
the row. Building that API to serve one gesture is more surface than holding two children.

It derives from `AbstractContainerControl`, not `PanelControl`: `VulkanControl.AddChild` **throws on a
second child** ("Plain VulkanControl supports only one child"), so a two-part control has to take the
container base. The base forces `Stretch` on both axes, which is inert here — `StackPanelControl`
gives a non-star child `DesiredSize` on the main axis regardless of alignment, and on the cross axis
`preferredWidth`/`preferredHeight` of `0` already fills it.

**Rejected: teach `LabelControl` to edit itself.** A text control's children *are* its glyphs —
`SyncGlyphs` trims `children` to the character count — so a caret parented to it is destroyed by the
next keystroke. That is exactly why `TextBoxControl` is a container in the first place.

### 2. The field's width is fixed at `BeginEdit`, not star-allocated

`box.preferredWidth = max(120, label.DesiredSize.X + 24)`, set once when the edit opens.
`StackPanelControl` offers a horizontal child `float.MaxValue` on the main axis, which
`TextBoxControl.Measure` would take literally, and a `widthStar` would walk into the pass-1
cross-measurement defect already logged against `StackPanelControl`. A field wider than the sidebar
is clipped by the browser's own viewport, which is a `ScrollableControl` and always clips.

Consequence: typing past the field's width runs the text under the clip rather than scrolling to
follow the caret.

### 3. Blur commits, and `SetActiveControl` had to assign before it notified

`TextBoxControl` gains `onBlur`, raised from `IContext.OnContextRemoved("ActiveControl")` — on the
box itself and forwarded from its inner `FieldLine`, because either can be the control the collision
handler makes active depending on whether the pointer landed on the text or on the box's padding.
`TextInputControl`'s two `IContext` methods became `virtual` so `FieldLine` can extend rather than
replace the commit-on-blur the base already does for its own edit.

`UICollisionHandling.SetActiveControl` now assigns `activeControl` **before** raising
`OnContextRemoved`, matching what `SetDragging` already did. Without it the outgoing control cannot
see where the context went, so a box could not tell "focus left me" from "focus moved from my text
line to my own padding" — and clicking past the text inside the same field would end the edit.
`TextInputControl` is the only other `IContext` implementor in the tree and reads no context in its
callback, so nothing else observes the order.

### 4. Resync walks the control tree; there is no register of open sessions

`TabViewControl.FindOpenDocuments(path)` yields every tab whose editor loaded that file, across every
window; `FindOpenDocument` is its first result. A rename repaths each session, updates the in-memory
`document.name`, and recaptions the tab through `TabViewControl.Retitle`.

**Rejected: a static registry of `DocumentEditSession`s keyed by path.** It duplicates state the tree
already holds and needs unregistering on every teardown path — tab close, window close, tear-off.
The walk cannot go stale because the thing it walks *is* the truth.

`DocumentEditSession.path` gained a private setter and `Repath`. Without it a renamed note that was
open wrote itself back to the old path on the next Ctrl+S, recreating the file it was renamed away
from.

### 5. `FreePath` for the target, so a rename never writes over a note

Same helper `New note` and `Duplicate note` already use: a taken name becomes "Name 2". Renaming to
the name it already has is a no-op rather than a "Name 2".

## Deliberately out

- **Two views of one note.** `RichTextDocument.blocks` holds `Block` controls that are the same
  objects `DocumentControl` parents as children, and an entity has one parent — so a second view
  re-parents the blocks out of the first and blanks it. Real multi-view is blocked on the UI
  data/visualization split. `Open in new tab` was dropped for this reason (user, 2026-08-21).
- **Folder rename.** `Directory.Move` invalidates the path of every open tab beneath it.
- **`[A_XSDType]` on `EditableLabelControl`.** Nothing authors it in XML; it is built by
  `FileBrowserControl.AddRow`.

## Known consequences

- Clicking a *row* while renaming commits the edit and then activates that row, so the click both
  finishes the rename and opens a note.
- Opening a rename while another one is live commits the first, whose `Rebuild()` then destroys the
  row the second was just opened on — so the second rename silently does not take. The destroy is
  queued, so nothing dereferences a freed control; the row is simply replaced. Any `Rebuild()` during
  an open edit does this, a folder toggle included.
- `RichTextDocument`'s class comment still claims editing mutates a working copy. It does not — the
  control tree is the model, as `DocumentEditSession`'s own comment says. Pre-existing, left alone.
