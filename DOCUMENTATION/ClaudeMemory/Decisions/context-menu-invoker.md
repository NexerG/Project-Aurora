# Decision — a menu entry acts on the control its menu was opened on

**Date:** 2026-08-20
**Status:** LANDED. Builds clean and boots; **nothing here is GUI-verified.**
**Scope:** `ArctisAurora.Core.UISystem` (`ContextMenus`, `ConfirmWindow`, `NoteNameWindow`),
`ArctisAurora.Core.UISystem.Actions` (`WindowActions`, `ViewActions`),
`ArctisAurora.Core.UISystem.Controls.Containers` (`FileBrowserControl`, `FileRowControl`,
`FileTreeControl`, `TabViewControl`), `Periodic` (`VaultBrowserControl`).

## The defect

Minimize, Maximize and Close **crashed the app when invoked from a context menu**, while the same
three actions on the title bar buttons had always worked.

`WindowActions.Acting()` resolved its window as `RenderWindow.Of(UICollisionHandling.hovering)`.
`hovering` is one process-wide static, and an open menu is a real window with input wired — so by the
time an entry fires, the pointer is inside `ContextMenuWindow`'s own window and `hovering` is the
`ContextMenuItemControl` in it. All three acted on the menu window: iconify it, maximize it, or
`Engine.CloseWindow` it — which destroys `uiRoot` and leaves `ContextMenuWindow._window`/`_menu`
pointing at a torn-down window for the next right click. A title bar button was never affected
because there `hovering` *is* the button.

`ViewActions.Acting()` had the same defect silently: `Split right`/`Split down` from a menu walked up
from the menu item, found no `TabViewControl`, and no-opped.

Reachable from the whole app because `UI.xml`'s root `<Window>` names `ContextMenu="window"` and
`VulkanControl.OpenContextMenu` walks up until something offers entries — which the file browser and
its rows did not.

## Decisions

### 1. The invoker is published for the entry's duration, not passed as an argument

`ContextMenus.invoker` holds the control the running entry's menu was opened on.
`ContextMenuBuilder.Add` wraps every entry — XML-authored and code-added alike — so it is set before
the action and cleared in a `finally`. Both `Acting()` helpers read
`ContextMenus.invoker ?? <previous fallback>`.

**Rejected: give the actions a `VulkanControl` parameter.** The machinery already exists —
`ContextMenuItemDefinition.invokeOnTarget` binds `Action<VulkanControl>` when the method takes one.
But the same three methods are bound by two other readers that have no control to give:
`VulkanControl.ResolveAttributes` binds `onRelease="Window.Minimize"` via
`Delegate.CreateDelegate(typeof(Action), …)`, and `InputHandler.LoadInputs` does the same for the
`Backslash` keybinds on `View.Split*`. A one-parameter method throws at boot in both. Making all
three binders agree means teaching the keybind path to invent a target it does not have.

**Rejected: point `hovering` back at the owner when the menu closes.** Hover state is the
bookkeeping behind enter/exit callbacks; forging it desynchronises them.

Cost, accepted: an ambient static that the actions read without declaring they do. It is the same
shape as `hovering`, which they already depended on — this narrows an existing ambient dependency
rather than introducing a new kind of one.

### 2. A row carries its own menu, because the browser cannot tell which row was clicked

`FileRowControl : ButtonControl` holds the `FileObject` it was built for plus the browser that built
it, and forwards `BuildContextMenu` to `FileBrowserControl.BuildRowMenu(file, menu)`. That hook is
`protected internal virtual` — `internal` so the row can call it, `protected` so a leaf in another
assembly can override it.

Without the row type the walk runs label → content panel → row → rows panel → browser, and the
browser learns only that a right click arrived somewhere in it.

Concrete and carrying no `[A_XSDType]` — a row is built in code and never authored.
`ContextMenuItemControl` is the concrete precedent; derivative tracking still comes off
`VulkanControl`'s own registration, so this is not the trap in [[vulkancontrol-needs-xsdtype]].

### 3. Delete both asks and is recoverable

User's call, 2026-08-20 — **both**, not one or the other. `ConfirmWindow` prompts, and the file goes
to the recycle bin rather than being unlinked.

`Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs,
RecycleOption.SendToRecycleBin)` needs **no package reference** on `net10.0-windows10.0.22621.0` —
`Microsoft.VisualBasic.dll` is in the shared framework. Probed on a throwaway project before the plan
was written, not assumed.

`ConfirmWindow` is the same one-window-per-process shape as `NoteNameWindow` and **has no keyboard**:
that one gets Enter/Escape from its `TextBoxControl`'s `onCommit`/`onCancel`, and a prompt with no
field has no such route. Its message is one non-wrapping `LabelControl` in a 380px window, so a long
note name overflows.

### 4. The tab closes before the file goes, and closes unwritten

`TabViewControl.CloseTab` **saves** the page first, so using it on a note being deleted writes the
note straight back to disk. `DiscardTab` is `FinishClose` made public — teardown with no save.

### 5. A name collision never overwrites

`FreePath` walks `Name`, `Name 2`, `Name 3`. Used by new-note *and* duplicate, so a name the user
types that is already taken yields a second note rather than replacing the first or silently doing
nothing. Not in the agreed plan, which was silent on collisions.

### 6. A new note is one block holding one empty run

`DocumentEditorControl.LoadDocument` builds no blocks of its own and the caret is placed on a *run*,
so a document with an empty `blocks` list opens as a note that cannot be typed into. The temporary
`ContentBlock`/`TextRun` are entities — construction allocates a pool row — so they are `Destroy()`ed
once the file is written.

A duplicate is `File.Copy` plus a rewrite of the copy's own `Name` attribute, because that attribute
is what a tab captions itself with. Load-and-resave through `DocumentXml` would build a whole control
tree to throw away.

## Known bug, deliberately left

**The `"window"` menu sits on the root `<Window>`, so it is the fallback for the entire app.**
Right-clicking anything with no menu of its own — a splitter, the outer stack panel — still offers
Minimize/Maximize/Close. `TabWindow.xml`'s root names no `ContextMenu` at all, so a torn-off window
offers them nowhere, **including its own title bar**. The fix is moving the attribute onto
`<TitleBar>` in both files. Deferred at the user's call (2026-08-20): the crash is fixed, the
placement is a separate decision.

## Verified

- `dotnet build AuroraEngine/ArctisAurora.sln` — 0 errors. Warning count unchanged from baseline.
- **Boot**: `Periodic.exe` runs past `Renderer.Initialize` and is still alive when killed at 15s, no
  exception on stderr. `ContextMenus.LoadMenus` and `InputHandler.LoadInputs` both still resolve
  every action name they bind — that is what a broken action signature would have thrown at.
- The XSD generator reports every schema unchanged, confirming no new authorable type was introduced.
- **Not verified — none of it is GUI-verified.** The row menu appearing at all, the three window
  entries acting on the app window instead of the menu window, the name prompt, the confirm prompt,
  and every one of new/duplicate/delete are unexercised.

## Still open

- The window-menu placement bug above.
- No rename, no new folder, no delete folder; a folder row offers only `New note`.
- `ConfirmWindow` is mouse-only and its message does not wrap.
- Nothing watches the vault, so a note created by another app still appears only after a rebuild.
- Deleting a note that is open in a **torn-off** window closes that tab and, if it was the last one,
  takes the window with it through `CloseIfEmptied` — unexercised.

Related: [[file-browser-tree]], [[vault-browser-and-shell]], [[tab-view-control]],
[[note-naming-and-text-field]], [[vulkancontrol-needs-xsdtype]], [[verify-what-the-user-sees]]
