# Decision — the vault is a settings path, and the browser finds the editor by walking the tree

**Date:** 2026-08-17
**Status:** LANDED (P5). Builds; verified at runtime that the vault resolves, the rows build and the
first note loads. **Not GUI-verified** — nothing was eyeballed or clicked.
**Scope:** `Periodic` (`PeriodicSettings`, `VaultBrowserControl`, `Periodic.Main`, `UI.xml`,
`Data/Notes/*`), `ArctisAurora.Core.Filing` (`FileObject`).

## Decisions

### 1. The vault is a `User`-scoped setting, defaulting to a folder in the app's Data

`<Vault Path="Notes"/>` on a `Periodic` settings category. A relative path resolves through
`VirtualFileSystem.ResolveDir`, an absolute one is used as it is — the same rule
`DocumentEditorControl.LoadPath` already applies to `Source`.

Defaulting inside the app's own Data rather than `%APPDATA%` keeps the notes in the repo as
version-controlled fixtures and means the app has something to open on first run. Pointing it at a
real vault is one attribute in `UserSettings.xml`.

`SampleNote.xml` moved out of `Data/XML/Documents` into `Data/Notes`, and a second note landed in
`Data/Notes/Reference` so the browser has a folder to indent and switching has somewhere to switch
to. The old home is engine config — `Bootstrap.xml`, `Registry.xml`, `UI.xml` — and a browser
pointed at it would have listed all of them as notes.

### 2. Siblings find each other by walking the tree, because controls have no names

`Entity.name` exists but is not an XML attribute and nothing indexes it, so a row's click handler
cannot say "the editor called X". `Find<T>(root)` is a DFS from `EntityRegistry.uiTree` returning
the first match, and it is used twice — the browser finding the editor, and startup finding the
browser.

It walks the document's glyphs in the worst case, which is tens of thousands of controls, but only
on a click and only until the first match. **Rejected: adding a `Name` attribute plus a registry
lookup** — that is a naming system for the whole UI, designed off one call site.

### 3. Opening the first note happens in `Main`, not in `OnStart`

`OnStart` was the obvious hook and **crashes the engine**: `Engine.Interpolate` drains
`entitiesOnStart` with a `foreach`, and loading a document creates one entity per glyph, so the list
mutates mid-enumeration. `OnTick` has the identical shape over `entities`.

So `Periodic.Main` calls `VaultBrowserControl.OpenFirstNote()` immediately after assigning
`EntityRegistry.uiTree` — outside any iteration, and late enough that the whole tree exists. This is
app startup policy anyway, next to `SetActiveKeybindGroup` and `SetWriteRoot`.

**The engine defect is untouched and is not mine to leave quiet:** no control can create an entity
from `OnStart` or `OnTick` today. The fix is to drain into a snapshot and clear before invoking, the
way `ProcessDestroys` already works — deliberately not done here, since it is engine tick code well
outside this slice.

### 4. Switching notes saves the one being left

`Open` calls `editor.Save()` before `LoadPath`. There is no dirty flag and no undo, so a silent
discard would be unrecoverable, and clicking another note is the only way out of a document. Saving
an untouched note rewrites the same bytes — `DocumentXml`'s round-trip is byte-identical between
passes, so it is a no-op on disk.

**Consequence to accept:** clicking a note *commits* whatever is in the current one. There is no
"discard changes".

### 5. Rows are a flat indented list, not a collapsible tree

Folders are labels, notes are buttons, depth is left margin. No expand/collapse, no icons, no
selection highlight. `FileObject.Icon` exists and is never set by anything. A tree widget is worth
building when there are enough folders for collapsing to matter.

Files are filtered to `*.xml`. The first note in tree order is the one opened at startup, and
`FileObject` lists directories before files, so a note inside a subfolder wins over one at the root.

### 6. The shell is dark by default, and a container that does not say otherwise paints white

Window and editor ground `#1e1e1e`, sidebar `#171717`, against the engine's default `#FFFFFF` text.
Dark is the standing default for Periodic (user, 2026-08-17), not a theme toggle — there is no theme
system and none is planned yet.

It was found the hard way. The first shell rendered a **blank white window with no note in it**, and
the note was fine: layout probed correct (heading block at x=220, first glyph at 220,10, colour
`#FFFFFF`). The cause is that **a container control paints an opaque default-coloured quad unless it
sets `maskAsset` to `"invisible"`**. `WindowControl`, `DocumentControl` and `DocumentEditorControl`
all set it; `StackPanelControl` does not. So wrapping the shell in a `StackPanel` put a white sheet
over the entire window, and white text on it vanished.

The same trap bit twice in one sitting: the browser's inner `rows` panel then covered the sidebar,
which is why the `Reference` folder label was invisible in the first screenshot. The viewport paints
the sidebar; the panel inside it is masked invisible.

**Verified by screenshot**, which is the only thing that would have caught it — the probe said every
rect and colour was correct.

### 7. `FileObject` was doubling every descendant

Pre-existing: the constructor calls `TreeBranch` for a directory, and `TreeBranch` then called
`child.TreeBranch(...)` on each subdirectory it had just constructed — so everything below the first
level was added twice. Fixed, because the browser is the first caller.

## Verified

- Builds clean; boots to all three threads, no stderr.
- A temporary probe confirmed: vault resolved to `Periodic\Data\Notes` off the setting, the browser
  was found in the tree, **3 rows** (one folder label + two notes — proof the `FileObject` fix
  holds, or there would have been five), and the editor loaded the first note with **6 blocks**.
  Probe removed.
- **Screenshotted:** dark two-pane shell renders — sidebar at `#171717` with the `Reference` folder
  label and both note rows, and the first note's headings and body text laid out beside it.
- **Row clicks were BROKEN and are fixed (2026-08-17, later).** The rows used a `ButtonControl` with
  a `TextInputControl` caption, and `TextInputControl.ResolveOnClick` swallows the click without
  bubbling — so clicking a note did nothing at all. Rows now use `LabelControl`. See
  [[window-chrome-and-label]]. This is exactly what "not GUI-verified" was hiding.
- **Not GUI-verified:** clicking a row to actually switch notes end to end, and scrolling the sidebar.

## Still open

- **`Thickness` has no `TypeConverter`, so `Margin` and `Padding` cannot be authored in XML at all** —
  `Padding="8"` throws `NotSupportedException` out of `ResolveAttributes` and kills the parse. The
  browser sets its padding in code instead. Every other control in the engine has the same hole.
- **Hovering the sidebar stops typing.** `UICollisionHandling.activeControl` is hover-derived, so
  moving the pointer over a row's label repoints it away from the document and `TextInputActions`
  can no longer resolve an editor. Known and recorded in [[document-selection]] decision 8; the
  sidebar is the first thing that makes it easy to hit. The answer is real focus, not a patch here.
- No new-note, rename or delete. Rename is the `ContextMenuControl` item already on the WIP list.
- The browser never refreshes — `Rebuild()` exists and nothing calls it after construction, so a
  note added on disk while the app runs does not appear.

Related: [[settings-registry]], [[settings-categories]], [[document-structural-editing]],
[[periodic-editor-architecture]]
