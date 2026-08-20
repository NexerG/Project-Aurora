# Decision — the file browser is an engine control over a lazy `FileObject` tree

**Date:** 2026-08-20
**Status:** LANDED. Builds clean; expand/collapse and opening a nested note **GUI-verified by
screenshot with synthetic clicks**; the model probed directly out of `ArctisAurora.dll`.
**Scope:** `ArctisAurora.Core.Filing` (`FileObject`),
`ArctisAurora.Core.UISystem.Controls.Containers` (`FileBrowserControl`, `FileTreeControl`),
`Periodic` (`VaultBrowserControl`).

## Decisions

### 1. `FileObject` lists a folder on first access, not in the constructor

The constructor used to walk the whole subtree. A tree view whose folders start closed must never
read a folder nobody opened, so `children` became a private field behind a `Children` property that
calls `Load()` the first time it is asked. A file returns a shared empty list rather than null, so a
caller can iterate any node without asking what it is first.

`Refresh()` drops the listing so the next access re-reads. Nothing calls it yet — `Rebuild()` throws
the whole root away instead — but it is the hook a filesystem watcher would use.

Also new: `name` (`Path.GetFileName`, extension intact) and `parent`. The extension stays on because
**the model does not decide how anything is displayed** — `VaultBrowserControl` strips `.xml` in its
`DisplayName` override. `parent` has no consumer in this slice; it is what the editor's
folder-descending view will walk to go up, and it costs one assignment in `Load()`.

`Load()` sorts directories and files separately with `OrdinalIgnoreCase`. NTFS already hands them
back alphabetically, so this changes nothing observable today — it is there so the browser's order
is the browser's, not the filesystem's.

### 2. The browser is two engine controls, not one, and neither is authorable

- `FileBrowserControl` (abstract) — the root, the row's look, `Rebuild()`, and four hooks a leaf
  fills in: `RootPath`, `PopulateRows`, `Activate`, and optionally `Accepts`/`DisplayName`.
- `FileTreeControl` (abstract) — the openable tree: an `expanded` set of full paths, recursion only
  into open folders, and the `>`/`v` toggle.
- `VaultBrowserControl` (Periodic, concrete) — vault root off `PeriodicSettings`, `.xml` filter,
  extension-stripped names, and the existing tab-opening `Open`.

**This reverses the boundary held since P5** ("`VaultBrowserControl` is Periodic's; the engine gained
nothing for the browser") — user's call, 2026-08-20, taken so the editor's folder view is a sibling
leaf rather than a fork of Periodic's row-building code. The cost, accepted knowingly: the base has
exactly **one** leaf until that view is built.

The split is base-vs-tree rather than one class with a mode because the two presentations share only
the model and the row; a tree recurses and remembers what is open, a folder view holds one current
folder and remembers nothing. **Rejected: a `Mode` enum on one control** — that is two populate
methods behind a switch with two sets of dead state.

Neither engine class carries `[A_XSDType]`. `AbstractContainerControl` and `TextControl` are the
precedent: an abstract control that is never authored in XML has no attribute. This is *not* the
trap in [[vulkancontrol-needs-xsdtype]] — that is about **stripping** an existing registration off a
type the EntityRegistry tracks, not about declining to add one.

### 3. The expander is a gutter column, not a prefix on the name

A row is `Button > StackPanel(Horizontal) > [gutter Label, name Label]`. The gutter is a
fixed-width label holding `>`, `v`, or nothing; a file keeps the empty gutter so its name starts on
the same x as the folder names around it.

**Rejected: one label reading `"> Name"` and `"  Name"`.** Arial's space is narrower than its `>`,
so padding a single label misaligns files against folders by a few pixels per row. A fixed column is
exact and reads as a gutter on purpose.

`>` and `v` are ASCII and already in the atlas — the chrome captions had to settle for `-`/`[]`/`X`
because `−`/`□`/`✕` are not, and a disclosure triangle would have hit the same wall.

### 4. Toggling rebuilds every row

`Toggle` flips the path in `expanded` and calls `Rebuild()`, which discards the root and re-reads
from disk. Repointing the one gutter label and splicing rows in would be cheaper — a rebuild churns
Button + StackPanel + two Labels + their glyphs per row — but the vault is tens of rows, the row
count only changes on a toggle, and re-reading gives refresh-on-toggle for free.

Expansion survives because `expanded` holds **full paths**, not node references; the rebuilt tree is
new `FileObject` instances over the same paths.

Creating entities from the toggle is safe: click handlers run in `Engine.HandleUI`, which is ahead
of `Interpolate` in the tick, so nothing is mid-enumeration. `Open` has always created a whole
document from a click handler.

### 5. Palette and metrics are XML attributes with the sidebar's values as defaults

Ten `[A_XSDElementProperty]` fields — row height, indent, spacing, inset, gutter width, font size,
and the five colours — following `TabViewControl`'s `TabColorHex`/`ActiveTabColorHex`. They cannot
stay `const` in the browser: `#171717` is Periodic's sidebar ground, and this is engine code now.

Defaults are exactly the constants they replaced, so `UI.xml` needed no edit and the look the user
signed off on is unchanged.

**Known limit:** rows are built in `VaultBrowserControl`'s constructor and `ResolveAttributes` runs
*after* it, so a value authored in XML reaches the control but not rows already built. Nothing
authors one today. The fix is either a `Rebuild()` after parse or deferring the first build to the
first `Measure`; neither was in scope.

## The defect this found

**A `StackPanelControl` bubbles nothing by default, and putting one between a button and its caption
kills the button.** The row's content panel swallowed both hover and release: `bubbleEnter` and
`bubbleRelease` default to false, so glyph → label → panel walked up and stopped, and `ButtonControl`
never heard a thing. The row rendered perfectly and did nothing at all.

`LabelControl` calls `BubbleAll()` in its constructor, which is why the old flat rows — Button
holding a Label directly — worked. Fixed by calling `BubbleAll()` on the content panel.

Caught only because the harness was checked against a control known to work first: the close button
tinted red under a synthetic hover while the row did not, which ruled out the harness in one run
rather than sending the search into the input system. See [[synthetic-input-false-defect]].

Side effect worth knowing: `UICollisionHandling.activeControl` now resolves to the row's content
panel rather than to the `ButtonControl`, because `ActiveTarget` stops at the first control whose
`canBeActiveContext` is true and `StackPanelControl` does not override it to false the way
`LabelControl` does. Nothing reads it here — `FocusedTabs` walks up from it and finds no `TabView`
either way — and press/release still match, since both resolve to the same panel.

## Verified

- `dotnet build AuroraEngine/ArctisAurora.sln` — 0 errors.
- **Model**, probed from a throwaway console host referencing the built `ArctisAurora.dll`: root
  reports `not listed` until `Children` is touched; the two root entries come back
  `Directory:Reference | File:SampleNote.xml`; `Reference`'s subtree stays unlisted until it is
  opened, then reports `Keybinds.xml`; `Refresh()` puts it back to unlisted; `parent` resolves.
- **GUI, by screenshot with synthetic clicks** (window held `HWND_TOPMOST` for the whole run — the
  first attempt released it between shots and every click landed in the terminal instead):
  `> Reference` and `SampleNote` with names aligned in one column → click → `v Reference` with
  `Keybinds` indented one level under it → click → collapsed again. Hover plate appears under the
  pointer. Clicking the nested `Keybinds` opened a tab in the left `TabView` with the note loaded.
  No stderr across the run.
- **Not verified:** scrolling a vault long enough to overflow the sidebar, and any vault deeper than
  two levels — `Data/Notes` has one folder holding one note.

## Still open

- Rows are still rebuilt wholesale, and nothing calls `Refresh()` — a note added on disk while the
  app runs appears only after a toggle.
- No new-note, rename or delete; no selection highlight; `FileObject.icon` is still set by nothing.
- `ResolveOnDoubleClick` remains dead engine-wide — no dispatch site exists. The editor's folder
  view needs one before it can be built.
- The editor's `FolderViewControl`, and whatever project root it would list, are not written.

Related: [[vault-browser-and-shell]], [[periodic-editor-architecture]],
[[button-states-and-hover-bubbling]], [[tab-view-control]], [[synthetic-input-false-defect]],
[[verify-what-the-user-sees]]
