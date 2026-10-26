# Decision — several vaults open at once; stage A (data foundation) and stage B (vault windows, redesigned) landed

**Date:** 2026-10-22
**Scope:** `ArctisAurora.Core.Security` — `VaultLock`; `ArctisAurora.Core.UI` — `SheetBook`, `SheetCalc`, `SheetFormula`, `SheetLinks`, `TextRunControl`, `DocumentControl`, `SessionLayout`, `TabViewControl`, `WorkspaceBarControl`, `WorkspaceControl`, `WorkspaceActions`, `UnlockPrompts`; `ArctisAurora.EngineWork.Rendering` — `RenderWindow`, `AGlfwWindow`; `ArctisAurora.EngineWork` — `Engine`; `Thorium.Editor.CustomControls` — `VaultBrowserControl`

**Status:** PARTIAL — stages A and B landed (B redesigned, see "Stage B" below); C to re-plan, D open, see [[multi-vault-plan]]. Stage A's session parts (`SessionLayout.Open`/`Close`, `closedWindows`, the `Primary` = anchored rule, `RootPath` = vault of its own window) were REMOVED/SUPERSEDED by stage B and are marked below.

## Stage B — landed 2026-10-22, redesigned by the user

Stickies are now one global system separate from vaults, and an editor window holds one or more vaults (each with its own browser in the sidebar); the same vault may be open in several windows. One global session layout (partition `workspace`) replaces per-vault session partitions; each editor window's record lists its vaults. A vault is open while some editor window lists it; when none does, its tabs in non-sticky windows close, sticky tabs of its files become "vault closed" placeholders, and an encrypted vault's key is dropped. Reopening the vault revives the placeholders. The old in-place vault switch (`VaultsWindow.Switch`/`Enter`) is gone.

### What changed (stage B)
- `RenderWindow`: `Action<RenderWindow>? onClose` (host-owned close, called by `WindowActions.Close` in place of the default), `List<string> roots` (folders the window's browser lists, recorded with its session).
- `WindowActions.Close(window)` calls `window.onClose` when set. `Window.MinimizeToTray` hides the acting window (`Background.Hide(Acting() ?? Engine.primary)`).
- `Background`: `Close`, `Ask`, `Answer`, `Hide` take the window (default primary). New `Func<bool>? hidePrimary` (host: the main window starts hidden with no tray involved), `bool primaryStartsHidden` (= `startsHidden || hidePrimary() && !TestRunner.active`, read by `AGlfwWindow.CreateWindow`'s Visible hint and `Engine.InitWindowing`'s `primary.hidden`), `Action? open` (the tray's Open entry, tray left click and the second-copy show signal; private `Open()` dispatches), `ShowWindow(RenderWindow)`.
- `SessionLayout`: new `SessionRoot` (`[A_XSDType("SessionRoot","Settings")]`, `Path`, `ISessionWindowChild`); `SessionWindow.roots`. `RecordOf` writes `window.roots`; `OpenRecorded` and `Rebuild` (primary record) set `window.roots` before `Fill`. `RecordOf`'s `primary` is back to `window == Engine.primary`. REMOVED: `SessionLayout.Open(key, into)`, `SessionLayout.Close(key, kept)`, private `closedWindows` and its seeding in `Record`. `scope`/`ChangeScope` stay.
- `TabViewControl.PinToSticky`: a new sticky gets `SessionLayout.freeScope` (the source window's `sessionScope` only when `freeScope` is empty, so other hosts are unchanged). `TabActions.StickyEntries` no longer filters stickies by the tab window's `sessionScope`.
- New `Thorium.Editor.VaultWindows` (static): `workspaceScope = "workspace"`; `Editors()`, `IsOpen(vault)`, `NameOf`, `KnownVaultOf(path)` (deepest known vault, null outside all), `Closed(path)` (false standalone; true when the file's known vault is locked-not-open or listed by no window), `OpenHere(window, path)`, `OpenInNewWindow(path)` (reuses the hidden emptied primary, else a new anchored "main" window at the settings window size, cascaded), `OpenSomewhere(path)`, `CloseMenuVault` (action `Vault.Close`), `CloseVault(window, vault)`, `CloseWindow(window)`, `Revive(vault)`, `Wire(window)` (anchor, onClose, tray button, browsers, title), `Retitle` (vault names joined with ", ", or "Thorium"), `AddBrowser`/`RemoveBrowser`/`RemoveBrowsers`/`RebuildBrowsers`, `Migrate()`, `Restore(bool migrated)`, `StartEmpty()`, `AnyLocked()`, `UnlockAtLaunch()`, `TrayOpen()`; private `WithKey`, `Empty`, `Settle`, `ToClosed`.
- New `ClosedVaultControl` (`PanelControl`, `IFileEditor`): placeholder tab "<vault> is closed/locked" with an "Open vault" button (`VaultWindows.OpenSomewhere`); `Reopen()` swaps the real editor in (same pattern as `LockedFileControl.Open`).
- `VaultBrowserControl`: public `vault`, `RootPath => vault`; removed `browserName`, `VaultOfWindow`, `OpenFirstNote`, `FirstNote`. `CurrentVault()` (internal): menu target row/header, else the focused tab's file's vault if the window lists it, else the window's first root, else the `Vault` setting. `VaultOf(home)` uses `VaultWindows.KnownVaultOf` with the setting as fallback. `VaultWindow(from)` = from if it has browsers, else the first shown editor with browsers, else primary. New `Browsers(window)`; `Browser()` = the Home window's browser for `CurrentVault()`. `UnlockVault()` → private `UnlockVault(vault)` (rebuilds browsers and revives). `AskVaultKey` gains `Action? cancelled`. `BuildTab` returns a closed placeholder when `VaultWindows.Closed(path)`.
- `VaultsWindow`: rows are a row button plus a new-window button; private `Choose(path, newWindow)` replaces `Switch`/`Enter`/`Current`; `_source` = the window the picker opened over (row click opens there if it is an editor window, else in the primary); Vaults.Add opens the picked folder in that window.
- `Thorium.Main`: `SessionLayout.scope = VaultWindows.workspaceScope`; `migrated = VaultWindows.Migrate()`; `Background.open = VaultWindows.TrayOpen`; Opens=VaultBrowser (and primary started hidden) → `StartEmpty()` (empty hidden primary + picker), else `Restore(migrated)`; then `RestoreFree`, `Startup.Place(VaultWindows.AnyLocked())`, `UnlockAtLaunch()` when locked. `Startup.Read` sets `Background.hidePrimary = () => opens == VaultBrowser`; `Startup.Place` no longer posts `VaultsWindow.Open` on login. `StartupSetting.opens` now applies to every launch; the `Vault` setting is read but no longer written.
- Data: `UI.ui.axml` `<VaultBrowser Name="Browser">` replaced by `<StackPanel Name="Browsers" Orientation="Vertical"/>` inside a `<Scrollable>` (since 2026-10-23, see Sidebar layout), each vault a group (header label with the `vault` context menu + `VaultBrowserControl`). `Vault.menu.axml` gains "Close vault". New suite `VaultWindows.tests.axml`; `VaultScope.tests.axml` drops `CloseKeepsStickies`.
- Tests: new `VaultWindowsTests` (`VaultWindows.TwoInOneWindow`, `KeyFollowsWindows`, `RootsRecorded`, `Migrate`); `VaultScope.CloseKeepsStickies` removed; `StickyTests.PinPinRestore` now asserts the sticky is global (another scope keeps it, tagged `freeScope`, still pinned, with its note).

### Why (stage B)

**Stickies are a separate global system, not per vault (user, 2026-10-22).**
Supersedes start-at-login's "the note's file decides whether a sticky is free or belongs to a vault" and the vault-scoped sticky parts of [[desktop-stickies]]. Cost accepted: a sticky can show a vault file only as a link that goes to a placeholder when that vault is closed/locked. A sticky holds its own files (Stickies folder) and links to vault files (user chose "Both"); stickies never hold a vault key. The earlier plan (keep a vault's key while a sticky shows its encrypted note; `SessionLayout.Close` with a keep-predicate) was rejected by the user as too complicated.

**A window holds one or several vaults, and one vault may be in several windows (user).**
Rejected: raising the existing window instead of opening a second — not wanted.

**One global layout (partition `workspace`), each window record listing its vaults (user chose option a).**
Rejected (b): also remember each vault's own tabs — would have kept stage A's per-scope `Open`/`Close` and `closedWindows`; those are removed as unused (fork F3).

**Sidebar: one `VaultBrowserControl` per vault under a header (F1).**
Rejected: several roots inside `FileBrowserControl` — an engine change `FileTreeControl` inherits.

**Sidebar layout (2026-10-23): vaults stack by content in one scroller, each under a bold header band with a rule between.**
`UI.ui.axml` wraps `Browsers` in `<Scrollable HeightStar="1" Role="Surface">`; `AddBrowser` gives each browser `fitContent = true` and no `heightStar`, so the next vault starts right under the last row of the one above (it used to split the column evenly, putting the second header mid-column). Header: `Chrome` ground, a 3×12 `Accent` mark (the title-bar mark's idiom) then an `Ink` bold label (rows are regular `Ink`/`MutedInk` on `Surface`); bold alone was not distinct enough (user). Rejected: name in the accent colour; an `Accent` band (competes with the active tab).
Empty sidebar space: the `<Scrollable>` carries `ContextMenu="vault"`, and `CurrentVault()` answers a control carrying the vault menu with the lowest browser under it, so New note etc. there act on the bottom vault. Each browser sets `stopsContextMenu` so a click inside a vault doesn't collect the vault menu twice. `VaultWindows.Divide` puts a 1px top `Line` edge on every header but the first, re-run by `AddBrowser`/`RemoveBrowser`. The rule sits on the header, not the group, because the group is `alpha = 0` and alpha multiplies the edge too. Rejected: overriding `MeasureCore` in `VaultBrowserControl` from `rows`' desired height — duplicates the scrollable's unmeasurable-offer handling. Header icon left out (user's call).

**Opens=VaultBrowser shows only the picker over a hidden empty primary, hidden from creation via `Background.hidePrimary` (F2).**
Rejected: restore the layout and post the picker over it (no engine change, but not what "opens into the picker" means). Consequence: such a launch does not restore the layout, so the next quit records only what was opened.

**Picker: row click = open here; a new-window button beside the row, not inside it (F4).**
A button inside a row button is a known hit-test fight (see [[vault-list-and-switching]] §2). Rejected: a row context menu.

**Locked vaults at launch are prompted one after another; a cancelled prompt leaves the browser's unlock row and placeholder tabs (F5).** Rejected: no prompt.

**An editor window whose last vault is closed stays open and empty (F6).** Rejected: closing it like a window close.

**Closing an editor window.**
If another shown editor window exists, its notes are saved and it closes (the primary is emptied, `LoadEmpty`, and hidden instead — the renderer's device is tied to it — and the next `OpenInNewWindow` reuses it); the last shown editor window gets the existing Close / Tray / Cancel prompt, with Tray hiding that window. A per-window `onClose` hook (rejected: a global host callback in `WindowActions`). `Background.Close`/`Hide` take the window (rejected: always hiding the primary, which would leave the last vault window on screen). Tray Open is a host hook `Background.open` (rejected: `Show` skipping unscoped windows, which changes other hosts).

**Migration (once, when the "workspace" partition is missing).**
The `Vault` setting's old partition becomes "workspace" with `roots=[that vault]` on its main records; sticky records from every vault partition move to the free stickies partition; old partitions are left in the file. During restore the recorded roots count as open, so tear-offs restored before their editor window don't get placeholders.

### Verification (stage B)
- Test-verified: midpoint full suite 375 passed, 2 failed (Boot — the pre-existing default-sampler error; `Sticky.PinPinRestore`, which asserted vault-scoped stickies and was then updated), 1 skipped; new VaultWindows suite 4/4 pass; no `[Vulkan]` lines. End-of-change full suite 380 passed, 1 failed (Boot, baseline), 1 skipped; no `[Vulkan]` lines. Debug launch check (user settings backed up and restored): first launch migrated the Notes vault's session to `workspace`, and quit wrote one `main` primary record listing that vault.
- NOT GUI-verified: nothing clicked — the picker's two buttons, the per-vault headers and Close vault menu, the title text, the tray Open paths, the Close / Tray / Cancel prompt with several windows, the picker-first launch and the locked-vault prompts at launch are correct by inspection only.

### Known gaps (stage B)
- Closing the primary while another editor window is open (empty + hide) is not covered by a test (the fixture draws in the primary).
- A picker-first launch does not restore the layout; the next quit overwrites it with what was opened.
- After restore, a primary recorded with no vaults while other windows list some is hidden after it opens, so it may flash.
- `Background.Quit` still calls `Show()`, which shows a hidden emptied primary for a moment before shutdown.
- The new-window button uses the "maximize" icon as a stand-in glyph.
- `SheetBook.Clear()` on settling an encrypted vault clears every vault's loaded sheets (reload lazily).
- Old per-vault session partitions stay in UserSettings (unused).
- Stage C (sticky palettes, stickies browser) needs re-planning on the global-sticky model; stage D untouched.

## Stage A (below) — session parts retired by stage B

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
- RETRACTED by stage B (removed): `SessionLayout`: new `Open(string key, RenderWindow? into = null)` — opens a scope's recorded windows tagged with `key`; the record marked `Primary` fills `into` when given, otherwise opens as an anchored window; after a `Close`, only the windows that `Close` closed are reopened. New `Close(string key, Func<RenderWindow, bool> kept)` — records and closes the scope's windows except the kept ones; the primary is recorded but never closed. New private static `closedWindows` and private `RecordOf(window)` (extracted from `Record`).
- RETRACTED by stage B (`closedWindows` removed; `Primary` back to `window == Engine.primary`): `SessionLayout.Record` writes every window under its own `sessionScope` partition (null → `scope`, as before; `freeScope` as before; any other key → its own partition); a scope with no open windows is left as recorded unless it is in `closedWindows`. The `Primary` flag in a record now means "the primary or an anchored window".
- `SessionLayout.scope` and `ChangeScope` stay, as the partition for windows with no `sessionScope`.
- SUPERSEDED by stage B (`RootPath => vault`, one browser per vault; `VaultOfWindow` removed): Thorium `VaultBrowserControl`: `RootPath` = the vault of its own window (`sessionScope`, falling back to the `Vault` setting). New private `VaultOfWindow(RenderWindow?)`, `VaultOf(string? home)` (deepest known vault containing the file, else the setting), `VaultWindow(RenderWindow?)`, `Home()`. `Browser()`, `Open`, `FocusedTabs(window)`, `ShownTabs(window)`, `ShownKind(window)`, the workspace filter and the vault-lock prompts follow the invoking window instead of `Engine.primary`. `FindSheet(string? home, string file)`, `VaultSheets(string? home)`, `VaultNotes(string? home)`.
- `WorkspaceActions.Workspace()` and `UnlockPrompts.Prompt` use `UIActions.Invoking()` instead of `Engine.primary`.
- Tests: `ArctisAurora.Tests.VaultTests.TwoOpen` (`Vault.TwoOpen`, in `Vault.tests.axml`); new `Thorium.Tests.VaultScopeTests` (`VaultScope.CloseKeepsStickies`, `VaultScope.SheetInHomeVault`) with new suite `VaultScope.tests.axml`. Sheet and encryption tests updated to the new callback signatures.

## The whole feature (user's first design, 2026-10-22 — superseded by stage B above)
Stage A was the base for this; the stages are in [[multi-vault-plan]]. Retracted by the redesign: one editor window per vault, closing a vault leaving its stickies via a predicate, the key staying while a sticky shows an encrypted note, the picker hiding once a vault opens.
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

**(Stage A, `Open`/`Close`/`closedWindows`/predicate reasoning below retracted by stage B.) `SessionLayout.scope`/`ChangeScope` are kept beside the new per-scope `Open`/`Close`.**
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
- [[vault-list-and-switching]] "Two things track a vault … `ThoriumSettings.vault.path` is the one you are in" is superseded by stage B (the setting is read-only input); amended there.

- Vault identity (a vault id in `.aurora/Vault.manifest.axml`, `KnownVault.id`) and links between vaults are settled in [[link-graph]].

Related: [[multi-vault-plan]], [[vault-list-and-switching]], [[start-at-login]], [[desktop-stickies]], [[encryption]], [[session-restore]], [[link-graph]]
