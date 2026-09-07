# In-engine file chooser — to retire the OS folder dialog

**Raised:** 2026-08-30. **Nothing built.** The shape below is wanted, not yet agreed in detail.
**Builds on:** `../Decisions/file-browser-tree.md` — the vault browser tree, LANDED 2026-08-20. This is
the *chooser dialog*, not that tree.
**Checklist form:** the file-browser item in `DOCUMENTATION/Work in Progress List.md`.

## Why

`FolderPicker` shells out to Win32 `IFileOpenDialog` on a spawned STA thread, because picking a vault
folder needed *some* answer and the engine has no file chooser of its own.

| Cost today | |
|---|---|
| ~150 lines | COM vtable declarations |
| one thread | whose only job is to own an apartment |
| a modal disable | the owner-window disable Windows enforces |

**None of it exists on any platform but Windows**, which makes this the first hard blocker under the
engine's own portability.

## Wanted shape

A menu window over a `FileBrowserControl` derivative. The pieces already exist: the vault browser draws
exactly this tree, and `FileTreeControl` already owns the expand/collapse.

**One screen answering both "pick a folder" and "pick a file":**

- a `Mode` deciding whether folders are selectable or only navigable
- an `Accepts` filter, for the file case
- a path field, so a location can still be pasted

## What it retires

- `FolderPicker`
- `AGlfwWindow.Hwnd` — this is its only caller
- the STA thread

**`Engine.Post` stays.** It is not the dialog's.
