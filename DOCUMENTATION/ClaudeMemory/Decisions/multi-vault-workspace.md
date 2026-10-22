# Decision — several vaults open at once; stage A (the data-level foundation) landed

**Date:** 2026-10-22
**Scope:** `ArctisAurora.Core.Security` — `VaultLock`; `ArctisAurora.Core.UI` — `SheetBook`, `SheetCalc`, `SheetFormula`, `SheetLinks`, `TextRunControl`, `DocumentControl`, `SessionLayout`, `TabViewControl`, `WorkspaceBarControl`, `WorkspaceControl`, `WorkspaceActions`, `UnlockPrompts`; `ArctisAurora.EngineWork.Rendering` — `RenderWindow`, `AGlfwWindow`; `ArctisAurora.EngineWork` — `Engine`; `Thorium.Editor.CustomControls` — `VaultBrowserControl`

**Status:** PARTIAL — stage A landed; stages B, C, D open, see [[multi-vault-plan]]. No user-visible change yet: Thorium still boots one vault into the primary window and switches it through `VaultsWindow` exactly as before.

## What changed
- `VaultLock`: fields `root`/`master` replaced by `open` (`(string root, byte[] master)[]`, replaced whole on every change). New field `convertingRoot`.
- `VaultLock.Use(vault, key)` no longer closes other vaults (only that vault first). New `Close(string vault)` zeroes and drops one vault's key and stops its conversion; `Close()` zeroes every key. New private `MasterOf(vault)`, `Containing(path)`. `IsOpen`, `KeyFor`, `Covers` look up the open vault containing the path. `SetState(root, state)`, `Inside(root, path)`, `StartConversion(root, encrypt)`, `ConvertFile(path, master)` take the vault explicitly.
- Only one conversion runs at a time across all vaults; a second request is logged and skipped, and the next unlock of that vault converts it.
- `SheetBook`: `findSheet` is `Func<string?, string, string?>` (home path, file); `vaultSheets` and `vaultNotes` are `Func<string?, IEnumerable<string>>` (home path). `Resolve(string? home, string file)`, `IsLocked(string? home, string file)`. New `PathOf(SheetDocument?)` (the file a loaded document was read from). `PageRenamed`/`Shifted` pass their home to the callbacks.
- `SheetCalc.PageNamed` and `SheetFormula.Reference.Missing(SheetPage home)` pass the formula's own sheet path as home.
- `SheetLinks`: `Layout(string? home, …)`, `Plain(string? home, string reference)`, `ExpandMath(string? home, string source)`; private `MathValue`, `Missing`, `PageOf` take home.
- `TextRunControl`: new private `NotePath()` (walks parents to the `DocumentControl`, returns `session?.path`); `MathBoxFor` became an instance method; sheet-link layout and math-link expansion pass the note path. `DocumentControl.PlainText(BlockSnapshot, string? home)` (copy passes `session?.path`).
- `RenderWindow.anchor` (new bool): a window that stays open when its last tab or workspace leaves, as the primary does. Honoured in `TabViewControl.CloseIfEmptied`, `WorkspaceControl.Move` (the last workspace cannot leave) and `WorkspaceControl.Released`.
- `Engine.SetTitle(RenderWindow, string)` (new) over internal `AGlfwWindow.SetTitle` (glfwSetWindowTitle). No caller yet; stage B gives vault windows their vault's name.
- `TabViewControl.TearOff` and `WorkspaceBarControl.TearOff`: the torn window copies `sessionScope` from its source window.
- `SessionLayout`: new `Open(string key, RenderWindow? into = null)` — opens a scope's recorded windows tagged with `key`; the record marked `Primary` fills `into` when given, otherwise opens as an anchored window; after a `Close`, only the windows that `Close` closed are reopened. New `Close(string key, Func<RenderWindow, bool> kept)` — records and closes the scope's windows except the kept ones; the primary is recorded but never closed. New private static `closedWindows` and private `RecordOf(window)` (extracted from `Record`).
- `SessionLayout.Record` writes every window under its own `sessionScope` partition (null → `scope`, as before; `freeScope` as before; any other key → its own partition); a scope with no open windows is left as recorded unless it is in `closedWindows`. The `Primary` flag in a record now means "the primary or an anchored window".
- `SessionLayout.scope` and `ChangeScope` stay, as the partition for windows with no `sessionScope`.
- Thorium `VaultBrowserControl`: `RootPath` = the vault of its own window (`sessionScope`, falling back to the `Vault` setting). New private `VaultOfWindow(RenderWindow?)`, `VaultOf(string? home)` (deepest known vault containing the file, else the setting), `VaultWindow(RenderWindow?)`, `Home()`. `Browser()`, `Open`, `FocusedTabs(window)`, `ShownTabs(window)`, `ShownKind(window)`, the workspace filter and the vault-lock prompts follow the invoking window instead of `Engine.primary`. `FindSheet(string? home, string file)`, `VaultSheets(string? home)`, `VaultNotes(string? home)`.
- `WorkspaceActions.Workspace()` and `UnlockPrompts.Prompt` use `UIActions.Invoking()` instead of `Engine.primary`.
- Tests: `ArctisAurora.Tests.VaultTests.TwoOpen` (`Vault.TwoOpen`, in `Vault.tests.axml`); new `Thorium.Tests.VaultScopeTests` (`VaultScope.CloseKeepsStickies`, `VaultScope.SheetInHomeVault`) with new suite `VaultScope.tests.axml`. Sheet and encryption tests updated to the new callback signatures.

## The whole feature (user's design, 2026-10-22)
Stage A is the base for this; the stages are in [[multi-vault-plan]].
- Thorium opens into a vault picker; several vaults can be open at once, each in its own editor window.
- The primary window is always an editor, never the picker (the picker is a secondary window).
- When the primary editor's vault is closed, the primary is emptied and hidden (it cannot be destroyed because the renderer's device is tied to it) and the next vault opened goes into it.
- Closing a vault closes its editor window and tear-offs but leaves its stickies.
- Closing the last vault asks Close / Tray / Cancel (the existing close prompt). Close quits and the stickies close with the app; Tray keeps the app and the stickies.
- The picker never opens by itself: only at launch and from the Vaults menu. The tray's Open shows the picker when no vault is open.
- An encrypted vault's key stays in memory while an open sticky still shows an encrypted note of it; otherwise it is dropped when the vault closes.

## Why these choices

**One key per open vault, held as a copy-on-write array.**
Background writers (`FileWriteQueue`) call `KeyFor`/`Covers` off the main thread; a `Dictionary` mutated on main while read there could corrupt. Replacing the array whole gives a reader a consistent snapshot.

**One conversion at a time across vaults.**
A second concurrent queue would double the per-tick budget. A second request is skipped and resumes on that vault's next unlock.

**Sheet lookups carry the path of the file asking, and Thorium resolves within the deepest known vault holding that path.**
Rejected: searching every open vault and taking the first match — smaller, but wrong when two open vaults have a sheet of the same name. Note-side links needed the note path threaded through `TextRunControl` and `DocumentControl` (user approved 2026-10-22, option "1a"). A null home falls back to the `Vault` setting.

**`SessionLayout.scope`/`ChangeScope` are kept beside the new per-scope `Open`/`Close`.**
User approved 2026-10-22. Removing them would rewrite about 12 Thorium tests and the other hosts' behaviour for no gain. Windows with no `sessionScope` keep using `scope`.

**`Close` keeps the closed windows' records in memory (`closedWindows`) and writes them beside the kept windows on each capture.**
The alternative was a new attribute on the saved record. This way a vault whose stickies outlive its editor keeps its editor layout, and reopening it reopens only what was closed (no doubled stickies).

**What `Close` keeps is a predicate the host passes.**
The engine does not have to know which UI document is a sticky.

**`RenderWindow.anchor` instead of comparing against `Engine.primary`.**
Vault windows other than the primary must also stay open when emptied.

## Verification
- Test-verified: full suite 377 passed, 1 failed (`Boot`, the pre-existing baseline), 1 skipped; no `[Vulkan]` lines.
- `Vault.TwoOpen`: two locked vaults open together, each file gets its own key, closing one leaves the other readable and refuses writes to the closed one.
- `VaultScope.CloseKeepsStickies`: `Close` closes all but the kept window, a later capture keeps both records with one marked primary, `Open` reopens only the closed window anchored.
- `VaultScope.SheetInHomeVault`: a same-named sheet in two known vaults resolves in the vault of the asking file.
- NOT checked: anything by hand (no UI uses this yet); note-side sheet links with a real note in a second vault (only the resolver is tested); `Engine.SetTitle` has no caller.

## Known gaps
- No UI yet: stages B (picker + vault windows), C (sticky colours + stickies browser) and D (taskbar jump list) are not done.
- A sheet link in a note with no file (`session` null) or a formula in a sheet with no file resolves against the `Vault` setting.
- A second vault conversion requested while another runs is skipped until that vault is unlocked again.
- `SessionLayout.Close` leaves the primary's `sessionScope` alone; the host must change it after closing the primary's scope, or the next capture records its window twice.
- `SheetBook.PathOf` is a linear scan of loaded documents on each cross-file formula lookup (tens of documents; not measured).
- [[vault-list-and-switching]] "Two things track a vault … `ThoriumSettings.vault.path` is the one you are in" stops being true in stage B; superseded in plan, not yet changed.

Related: [[multi-vault-plan]], [[vault-list-and-switching]], [[start-at-login]], [[desktop-stickies]], [[encryption]], [[session-restore]]
