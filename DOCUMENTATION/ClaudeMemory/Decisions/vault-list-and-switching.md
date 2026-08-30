# Decision — the vault list is a settings group, and the screen switches by writing the setting

**Date:** 2026-08-30
**Status:** LANDED. Builds clean; boots all 24 steps; probed at runtime (list, prune, rows, persistence).
**NOT GUI-verified** — nothing was clicked. No row click, no picker, no vault switch end to end.
**Scope:** `Periodic` (`PeriodicSettings`, `VaultsWindow`, `Vaults.ui.xml`, `UI.ui.xml`,
`ContextMenus.menus.xml`, `PeriodicAssets.assets.xml`, `Periodic.Main`), `ArctisAurora.Core`
(`Engine.Post`, `MenuScreen`, `FolderPicker`, `AGlfwWindow.Hwnd`).

Extends [[vault-browser-and-shell]], which made *one* vault a setting. This makes the set of them data.

## Decisions

### 1. Known vaults are an `ISettingsGroup`, not a `SettingCategory`

`KnownVaults` holds `List<KnownVault>` and each entry carries one absolute `Path`. It is the
`InputBindings` shape, and it is forced: a `SettingCategory`'s children are one `Setting` each and
`SettingsRegistry.WriteDiff` walks them as *attributes*. A category structurally cannot hold a list.
The non-category path already walks `ChildListFields`, so the list persists to
`%APPDATA%\Periodic\Settings\UserSettings.settings.xml` for free and stays out of the settings
screen's category list — `SettingsWindow.Categories()` yields only `SettingCategory`.

The entry type is `KnownVault`, not `Vault`, because `AnyXMLType.FindType` resolves the *type's*
`[A_XSDType]` name globally and `VaultSetting` already owns `Vault`.

**Two things track a vault, deliberately:** `PeriodicSettings.vault.path` is the one you are in,
`KnownVaults.vaults` is the set you have been in. Collapsing them into "the list, plus an index"
was rejected — the active vault has to keep working when the list is empty, and it is what every
existing reader already asks for.

### 2. Removal is automatic and only automatic

`Prune()` drops every entry whose folder does not `Directory.Exists`, called at startup and again
each time the screen opens. There is no "remove from list" entry, because none was asked for and a
remove button *inside* a row button is a hit-test fight this codebase has already lost once — see
`FileBrowserControl`'s note about a `StackPanel` between a button and its caption eating the row.

**Consequence to accept:** a vault on a drive that is merely unplugged is forgotten, not greyed out.
Distinguishing "gone" from "offline" needs a reachability notion the app does not have.

### 3. Switching writes the setting; everything downstream re-reads it

`Switch` sets `vault.path`, `Commit()`s, closes every tab, `Rebuild()`s the browser and calls
`OpenFirstNote()` — which had been dead code since `UI.ui.xml` started seeding both panes and is now
wired back up. Nothing caches the vault, so the setting *is* the switch; `VaultBrowserControl.RootPath`
reads it live and moved onto the shared `KnownVaults.Resolve` in the same pass.

Tabs close rather than staying (user, 2026-08-30) — a window straddling two vaults is not a state
worth having. `CloseTab` writes each note first.

**Known hole:** `CloseTab` on an edited note that was never *authored* a name opens the naming prompt,
and `NoteNameWindow` refuses a second ask while one is up, so two such tabs would leave the second
open. Every note written by `CreateNote`/`DuplicateNote` carries a `Name`, so this needs a hand-made
note to reach. The general fix is the `Shutdown` "return false and re-enter" pattern, which is far
more machinery than this earns.

Only the primary window's panes are closed. A torn-off tab window keeps showing the old vault's notes.

### 4. Rows are posted, not invoked

A row's release does `Engine.Post(() => Switch(path))` rather than switching inline. Switching closes
the vaults window, and `Engine.CloseWindow` destroys the tree the click is *still bubbling through* —
`VulkanControl.ResolveOnRelease` walks to its parent after invoking. Posting moves the teardown to the
top of the next tick, outside input dispatch.

### 5. `Engine.Post` exists because a background thread cannot touch the UI

A `ConcurrentQueue<Action>` drained in `MainTick` between `ReapClosedWindows` and
`ApplyPendingFocus` — before `HandleUI`, so a posted action's layout lands in the same tick. First
main-thread dispatch in the engine; there was none, which is why the picker had nowhere to hand its
answer.

### 6. The folder picker is Win32 COM on a spawned STA thread

`IFileOpenDialog` with `FOS_PICKFOLDERS`, created through `CoCreateInstance` — no `IFileOpenDialog`
interface is declared at all, only `IFileDialog`, which carries every method the picker uses. The
declaration order *is* the vtable, so all 24 slots are listed and the 19 unused ones are `void`.

**Rejected: WinForms `FolderBrowserDialog`** (user, 2026-08-30) — 5 lines instead of 150, and the same
dialog underneath since .NET 5, but it means `<UseWindowsForms>true</UseWindowsForms>` and the whole
WinForms stack inside a Vulkan engine for one dialog.

**It runs on its own thread and does not block** (user, 2026-08-30). `Main` is MTA and the shell
dialog wants an apartment, so the thread is created STA and `CoInitializeEx`/`CoUninitialize` bracket
its life explicitly rather than leaning on the runtime's lazy init. The engine keeps polling,
ticking and rendering the whole time.

**The owner window is passed, so Windows disables it** — that is what modal means, and it is enforced
cross-thread. The main window stops accepting input but does not stop rendering, so it reads as
modal rather than hung. `IntPtr.Zero` would leave the app live and let the dialog get lost behind it.

**All of it is a stopgap.** None of this exists off Windows, and it is the first hard portability
blocker under the engine. The replacement is on the WIP list: an in-engine file browser over
`FileBrowserControl`, which already draws exactly this tree.

### 7. An application cannot open a window, so the engine grew `MenuScreen`

`RenderWindow.os` and `AGlfwWindow` are `internal` with no `InternalsVisibleTo`, and `os.handle` is a
`WindowHandle*` — so the raise/parse/resize/centre/show dance `SettingsWindow` performs is engine-only.
`MenuScreen.Open(name, document, width, height, source)` does it and returns the parsed
`WindowControl` for the caller to fill, or `null` when the screen was already up and was raised
instead.

**Rejected: making `os` public and enabling `AllowUnsafeBlocks` in `Periodic`** (user, 2026-08-30) —
it publishes the whole GLFW wrapper to widen one seam and puts OS-window handling in an app project.

`SettingsWindow` still has its own copy of the sequence and was deliberately left alone (§4);
collapsing it onto `MenuScreen` is a separate change.

### 8. The entry is in both menus

`Vaults` is listed under `periodic` *and* `file` (user, 2026-08-30). The menu system has no submenus —
`ContextMenuItemDefinition` does not nest — and building nesting for one entry was rejected.

## Verified

- Builds clean, no new warnings beyond the nullable noise the file set already carries.
- Boots: 24/24 bootstrap steps, phase clean, no stderr. `ContextMenus.LoadMenus` passing *is* proof
  both action names resolve — `FindAction` throws on a miss.
- `Vaults.ui.xml`, `UI.ui.xml` and `ContextMenus.menus.xml` validate against the regenerated
  `UITypeSchema.xsd` with zero issues, which is what confirms `HorizontalAlignment`, `Padding` and
  `HorizontalPos` are real attributes on the controls they were authored onto.
- Temporary probe (removed): the boot vault is remembered and resolved absolute; `VaultsWindow.Open`
  builds the window through `MenuScreen`; the `Rows` panel is found and gets one row per known vault;
  the browser is reachable by name for the switch.
- **Persistence round-trips** — `SaveAll` wrote `<Vaults>` with two `<KnownVault Path=.../>` entries,
  and the next boot read them back.
- **Prune works** — a third entry pointing at a folder that does not exist was written into the user
  file by hand; the next boot loaded 2, not 3, and built 2 rows.
- **NOT GUI-verified:** no click. The menu entry, a row click, the switch, the tab close, the picker
  and the settings button's new width are all correct by inspection only.

## Still open

- Nothing watches the vault folder, and nothing refreshes the screen while it is open.
- If the *active* vault is the one that went missing, prune drops it from the list but `vault.path`
  still names it and the sidebar renders empty. There is no recovery path.
- No MRU order, no last-opened stamp, no per-vault display name — the folder name is the name.
- `VaultBrowserControl`'s `FindByName` walks still start at `Engine.primary.ui.uiRoot`; the switch
  inherits that multi-window bug unchanged.

Related: [[vault-browser-and-shell]], [[file-browser-tree]], [[settings-registry]],
[[window-chrome-and-label]], [[context-menu-invoker]]
