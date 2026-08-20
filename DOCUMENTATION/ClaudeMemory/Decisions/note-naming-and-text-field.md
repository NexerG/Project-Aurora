# Decision — a note carries its own name, and the engine grew a text field to ask for one

**Date:** 2026-08-20
**Status:** LANDED. Builds clean; every trigger **GUI-verified by screenshot with synthetic input**.
**Scope:** `ArctisAurora.Core.UISystem` (`NoteNameWindow`, `UICollisionHandling`, `WindowActions`,
`TabViewControl`, `TextControl`, `TextInputActions`, `TextBoxControl`, `DocumentEditorControl`,
`DocumentEditSession`, `RichTextDocument`), `ArctisAurora.EngineWork.Rendering` (`RenderWindow`),
`Periodic` (`VaultBrowserControl`, `InputMap.xml`).

## Decisions

### 1. `RichTextDocument.name` is authored or absent — never derived

`<Document Name="Release checklist">`. A heading is not a name (`SampleNote.xml` opens with "Welcome
to Periodic") and a file name is not one either, since the file can be renamed underneath the note.

**This reverses the same day's call that the loader should stamp the file name into `name` on first
save.** With a prompt in the picture that decision is unimplementable: if `Load` filled the field,
`name` would never be null and nothing could ever detect an unnamed note. "The first save names the
note" still holds — the prompt is what does the naming. Display falls back to the file name at the
point of use, never in the model.

`DocumentXml` needed no change at all. `ApplyAttributes` reads the attribute and `WriteElement`
already skips nulls, so an unnamed note writes nothing and a named one round-trips.

### 2. The dirty flag exists because two answers collided

`isDirty` on `DocumentEditSession`, set by the five mutation paths (`Backspace`, `Delete`,
`SplitBlock`, `DeleteSelection`, and `TextInputActions.Write`) and cleared by `Save`.

It was not asked for. The trigger for prompting is "no authored name", which **every note in the
vault satisfies**, and cancelling the prompt aborts the close — so without a way to tell an edited
note from one that was merely opened, the app could not be quit at all without naming everything on
screen. The flag is what makes the pair livable, and it was the user's pick over adding a third
"don't name" button.

Verified in the negative, which is the half that matters: closing the app with one note edited and
one untouched prompts once, and the untouched note's file is **byte-identical** afterwards.

### 3. "Already open" is the editor's loaded path, normalized

`TabViewControl.FindOpenDocument(path, out view)` walks every window in `Engine.windows`, every
`TabViewControl` in each tree, and compares `DocumentEditorControl.session.path`. Replaces
`FindTab(name)`, whose one caller was the vault browser.

Matching on the tab's name could never have worked: a tab seeded from `UI.xml` carries no `Name`, so
a note already on screen was invisible to the check and opened a second time.

**The normalization is not incidental.** `Paths.Doc` resolves a relative `Source` through
`Path.Combine`, which does not collapse `..` — so the XML-seeded tab held
`…\Data\XML\Documents\..\..\Notes\Reference\Keybinds.xml` while the browser passed
`…\Data\Notes\Reference\Keybinds.xml`. Same file, different strings, and the first fixed build still
opened duplicates. `LoadPath` now stores `Path.GetFullPath(...)` and the compare is
`OrdinalIgnoreCase`.

`RenderWindow.Focus()` is public so a host can raise the window the tab lives in; it restores an
iconified window first, because focusing a minimized one leaves it minimized.

### 4. A text field is a container, because a `TextControl` cannot own anything but glyphs

`TextBoxControl : AbstractContainerControl` holding, in paint order, a `SelectionControl`, a private
`FieldLine : TextInputControl` carrying the text and its glyphs, and a `CaretControl`.

The obvious shape — rebase the old `TextBoxControl` stub onto `TextInputControl` and give it a caret
child — **cannot work**. `SyncGlyphs` treats `children` as exactly the glyph array and
`RemoveRange(text.Length, …)` + `DiscardGlyph`s everything past the last character, so a caret
parented to the text is destroyed by the next keystroke. This is why `DocumentControl`, not the run,
owns the document's caret; the field is the same split one level down.

Cost of the shape, accepted: `TextBoxControl` is not a `TextControl`, so `Text`, `FontSize` and the
text colour are forwarding properties onto the inner line.

Supporting pieces:
- `TextControl.WrapWidth(available)` — a new `protected virtual` returning `preferredWidth > 0 ?
  preferredWidth : available`, which `FieldLine` overrides to `float.MaxValue`. A field has a fixed
  width but its text must not wrap at it, and the two were the same expression before.
- `FieldLine.ResolveOnClick` calls `BeginEdit()` then forwards to its parent explicitly, exactly as
  `TextRun` already does — `TextInputControl.ResolveOnClick` swallows clicks and calling `base` would
  have swallowed this one too.
- The caret and the selection box collapse to `LayoutRect.Empty` when the field is not being edited,
  the same way the scrollbar thumb hides itself.

### 5. Standalone input routing, and Enter/Escape

`Text.Backspace`, `Text.Delete` and every caret action only ever called `Editor()`; only `Text.Write`
had a fallback. Each now checks the document editor first and falls through to `Box()` — the nearest
`TextBoxControl` above `activeControl` — so a host declares no input code for a field either.

`Text.NewBlock` (Enter) commits a field when there is no document, and a new `Text.Cancel` action is
bound to Escape in Periodic's `InputMap.xml`. Vertical and page moves collapse onto the field's ends,
since it is one line.

### 6. The prompt takes the active context as it opens

`NoteNameWindow` mirrors `ContextMenuWindow` — one `OpenMenuWindow` per process, built on the first
ask, hidden between them, centred over the source window, focus handed back on close.

It has to call `UICollisionHandling.SetActiveControl(_field)` itself. `activeControl` only ever moved
on a click, so until the user clicked the prompt, `Editor()` would still resolve to the note being
closed and every keystroke meant for the name would land in the document. `SetActiveControl` is the
four-line add/remove dance lifted out of `SolveLMBPress`, which now calls it — the fourth writer of
that field, and the one the `Context.Set` unification on the WIP list will have to absorb.

The previous active control is restored on close.

### 7. Closing is deferred through a callback, not blocked

`DocumentEditorControl.SaveNamed(onDone, onCancelled)` is the one entry point: it writes immediately
when the note has a name, and otherwise opens the prompt and writes from the confirm callback.

- `CloseTab` splits into the naming check and `FinishClose(item)`, which the callback invokes. The
  teardown that used to run on the next line now runs on the answer.
- `WindowActions.Close` recurses through `CloseWhenNamed(window, discarded)`: name the first unnamed
  editor in the window, then re-check. Cancelling any of them leaves the window open. The window is
  captured rather than re-derived, because `Acting()` reads `UICollisionHandling.hovering` and the
  pointer is over the prompt by then.
- `Engine.Stop()` is reached only after the last prompt answers, so shutdown is unchanged — it is
  simply called later.

### 8. A named note is never asked about, and closing the window writes it

Named notes skip the prompt entirely and are written silently, on tab close and on window close alike
(user, 2026-08-20). **The window-close half was a hole in decision 7 as first written:** it only
looked for editors that `needsNaming`, so a note that already had a name lost its edits when the
window went, without a word. `SaveNamedNotes` now runs over the window before `Engine.CloseWindow`.

The prompt therefore only ever appears for an *unnamed, edited* note, and its third button —
`Don't save` — is the answer that closes without writing. A note discarded that way is remembered in
a set for the rest of the close, or `FirstUnnamed` would keep finding it and the walk would never
finish.

The button is opt-in: `Ask` takes an `onDiscard` and leaves the button out when it is null, so Ctrl+S
shows two buttons — there, discarding is what Cancel already means. Hiding it also drops its
`preferredWidth` to 1, because `Hide()` collapses what a control draws but still reserves its slot in
the row.

A clean note is still rewritten on tab close. Byte-identical, but it touches the file's mtime.

## Verified

- `dotnet build AuroraEngine/ArctisAurora.sln` — 0 errors.
- **Dedup**: clicking `Keybinds` in the browser while it is open in the *other* pane no longer adds a
  second tab. Screenshotted against the duplicate the previous build produced.
- **Prompt on window close**: type one character into `SampleNote`, press the X — the naming window
  opens centred, pre-filled with `SampleNote`, the text selected and the caret drawn at its end, and
  the process is still alive. `Keybinds`, open but untouched, does not prompt.
- **Cancel** leaves the app running. **Confirm** writes
  `<Document Name="Release checklist" …>` and the app then exits. The untouched note's file hashes
  identical before and after the whole run.
- **Three buttons** render as `Don't save | Cancel | Save`. `Don't save` exits the app with the
  note's file **hash-identical** to before the edit.
- **A named note is written on window close with no prompt** — a note carrying
  `Name="Release checklist"`, edited and then closed with the X, exits straight away and comes back
  from disk carrying the edit. This fails on the build before decision 8.
- **Tab header**: a temporary note whose file is `Named.xml` and whose `Name` is `Release checklist`
  shows `Named` in the browser row and `Release checklist` on the tab.
- No stderr on any run.
- **Not verified**: the prompt over a torn-off window, more than one unnamed note in a single close,
  Escape-to-cancel through the keybind (only the Cancel button was clicked), and click-to-position
  and shift+arrow selection inside the field — select-all-then-type was exercised, the rest were not.

## Verifying this by synthetic input is unreliable, and here is why

Two probe runs produced results that looked like product defects and were not.

**Runs of `a` appearing inside notes** (`rendered` → `renaaaadered`, `between` → `beaaaatween`).
`SendKeys.SendWait` sends a key down and a key up, and the prompt window taking focus between them
means the app never sees the up. The engine then has a key it believes is still held, and
`Text.Write` is bound `<Continuous />` — so it keeps firing. Driving the same keystroke with
`keybd_event` and an explicit key-up, settled before the next action, produced a clean note. **The
engine has no recovery for a key released while it was not focused**, which is the same class of
problem `SolveDrag` already guards against for a release nobody saw; worth closing, not closed here.

**"The prompt stopped appearing."** A probe had left `Name="SampleNote"` in the fixture, so the note
loaded already named and correctly did not prompt. The regression was in the test data, not the code.
Any probe that writes a note has to restore it, and the check for whether a note is named must not be
`Select-String 'Name='` — that matches `FontName=` and the namespace attributes too.

Both fixtures were restored from `HEAD`.

## Still open

- No new-note, rename or delete, so a note can be named but not renamed afterwards.
- Nothing prompts for a note with no file at all — there is still no way to create one.
- `TextBoxControl` has no clipboard, no double-click-to-select-word, and no horizontal scroll when
  the text outruns the box.
- `ResolveOnDoubleClick` is still dispatched from nowhere.

Related: [[file-browser-tree]], [[vault-browser-and-shell]], [[engine-side-text-input]],
[[document-selection]], [[tab-view-control]], [[xml-save-skips-defaults]], [[window-chrome-and-label]]
