# Decision — Thorium starts at login, optionally into the tray; stickies are vault or free

**Date:** 2026-10-21
**Scope:** `Thorium.Startup`, `Thorium.ThoriumSettings` (`StartupSetting`), `ArctisAurora.EngineWork.Rendering.Background`, `ArctisAurora.EngineWork.Rendering.RenderWindow`, `ArctisAurora.Core.UI` — `SessionLayout`, `TabViewControl`, `TabActions`, `SettingsWindow`, `ToggleSwitchControl`

## What changed
- `ThoriumSettings.startup` = `StartupSetting` (`[A_XSDType("Startup","Settings")]`): `atLogin` (XML AtLogin, false), `opens` (XML Opens, nested enum `StartupView { LastVault, VaultBrowser }`, LastVault), `stickies` (true), `inTray` (false); `onChanged = "Startup.Apply"`.
- `Startup` (internal static): action `Startup.Apply` (category Settings) -> `Background.SetLoginCommand(atLogin ? "--login" : null)`.
- `Startup.Read(args)` parses `--login` / `--tray`, sets `Background.startHidden = () => tray || (login && inTray)`, adds tray entry "New sticky".
- `Startup.Place(locked)` after restore:
  - normal launch: nothing.
  - locked vault: shows the hidden primary (unlock prompt).
  - no tray: hides nothing.
  - else: hides vault windows (primary + windows with no `sessionScope`) when `tray || inTray`; hides free stickies when Stickies is off; on `--login` with Opens=VaultBrowser and the vault shown, posts `VaultsWindow.Open(Engine.primary)`.
- `Startup.NewSticky()` writes `Sticky yyyy-MM-dd HHmmss` + `ThoriumSettings.newNotes.Extension` (empty note; `.md` before 2026-10-23) to `StickiesFolder` (`%APPDATA%\Arktis\Thorium\Stickies`), opens it with `TabViewControl.OpenSticky` centred on the primary monitor (`Engine.CenterOnPrimary`), scope `freeScope = "stickies"`.
- `Thorium.Main`: `Startup.Read(args)` when not standalone; `SessionLayout.freeScope = Startup.freeScope`; after Restore (or the locked-vault path) always `SessionLayout.RestoreFree()` then `Startup.Place(locked)`.
- `Background`: `startHidden` (`Func<bool>?`, set by the host before Init, evaluated after settings load), `startsHidden` = `startHidden() && canTray && !TestRunner.active`, `trayEntries` (between "Open <host>" and "Quit"), `HideWindow(RenderWindow)`, `SetLoginCommand(string?)`.
- `SetLoginCommand` writes/deletes value `<host name>` = `"<Environment.ProcessPath>" <arguments>` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` via `Microsoft.Win32.Registry` (no NuGet); Windows only, no-op elsewhere.
- `Background.Show()` shows every window with `hidden` set, then focuses primary. `Background.Init`: a second copy launched hidden exits without signalling the running one.
- `AGlfwWindow.CreateWindow()` (primary): GLFW `Visible` hint = `!Background.startsHidden`; `Engine.InitWindowing`: `primary.hidden = Background.startsHidden`. New `Engine.CenterOnPrimary(w, h)` wraps the internal `AGlfwWindow.CenterOnPrimary`.
- `RenderWindow.sessionScope` (string?, null = recorded under `SessionLayout.scope`).
- `SessionLayout`: `freeScope` (empty = none); `RestoreFree()` reopens windows recorded under `freeScope` and tags them; `Record` (via `Capture`) writes windows whose `sessionScope == freeScope` to the free partition (always, also while `paused`) and the rest to `scope` (skipped while paused); `ClearWorkspaces` (from `ChangeScope` and `Reset`) keeps any non-primary window with a `sessionScope`; private `OpenRecorded` returns the window.
- `TabViewControl.OpenSticky(item, document, x, y, sessionScope)`; `PinToSticky(item, null)` calls it and passes the source window's `sessionScope`. Sticky size now scales from `Engine.primary`.
- RETRACTED 2026-10-22 (stage B: stickies are global): `TabActions.StickyEntries` lists only stickies whose `sessionScope` equals the tab's window's. It no longer filters by scope.
- RETRACTED 2026-10-23 ([[switch-controls]]): `ToggleSwitchControl` was rebuilt as an on/off pill (`OnText`, `OffText`, `On`) and `SettingsWindow.Editor` gives enums of 2-4 names a `SwitchControl`, so the Opens row is a switch, not this toggle. Original follows. `ToggleSwitchControl` (`<ToggleSwitch LeftText RightText Right>`) and `SettingsWindow.Editor`: an enum with exactly two names gets a toggle instead of a `DropdownControl`; helper `Words(string)` splits PascalCase. No existing settings enum has two values, so no other row changed.
- RETRACTED 2026-10-23: gradient `toggle-track` is removed from `Engine.gradients.axml`; goldens `Startup.ToggleSwitch.Left`/`Right` are deleted. Data: gradient `toggle-track` in `Engine.gradients.axml`; suite `Startup.tests.axml` / `StartupTests`.

## Why these choices

**RETRACTED 2026-10-22 (user, [[multi-vault-workspace]] stage B): stickies are one global system, a vault file shows in one only as a link; `Startup.Place` no longer posts `VaultsWindow.Open` on login; `StartupSetting.opens` now applies to every launch. Original reasoning follows.** **The note's file decides whether a sticky is free or belongs to a vault.**
User chose this over a per-sticky "keep on desktop" switch (B): a desktop sticky could then show a note of a closed or locked vault, and its links would resolve against whatever vault is open. Also rejected: every sticky global (C), which has the same problems for every sticky and stops a vault's notes being contained in it. Cost of the chosen rule: one sticky cannot mix both kinds, and "Pin to desktop" lists only same-kind stickies.

**Startup options are four fields, not one enum.**
Opens is the two-value toggle; Stickies and InTray are independent checkboxes, so InTray + Stickies leaves only free stickies on the desktop. The user asked for a two-option toggle with captions each side and InTray as a checkbox.

**The setting lives under Thorium, not the engine.**
Three of the options are Thorium ideas (vaults, stickies); the engine provides only the registry write and the hidden start.

**The Run value is `"<exe>" --login` and the choice is read at launch.**
Changing Opens / Stickies / InTray never rewrites the registry; only AtLogin does. HKCU, so no admin.

**`--tray` acts as InTray ticked** (the Stickies setting still applies). A second copy launched hidden exits silently instead of raising the running one.

**A locked vault on a hidden launch shows the main window with the unlock prompt.**
Recommended over deferring the prompt until the window is shown, which needs more code.

**The main window is created invisible; other restored windows are hidden right after they open.**
The GLFW `Visible` hint means login shows no flash on the main window.

**A normal launch restores free stickies too; Stickies only governs login / `--tray` launches.**
Free stickies the setting hides are still opened (hidden), so a quit does not wipe their record; the tray's Open shows them.

**`Background.startHidden` is a `Func`, not a bool.**
InTray is only readable after settings load inside bootstrap, which is after `Main` has called `engine.Init`.

**RETRACTED 2026-10-23 ([[switch-controls]]): neither control uses a gradient any more; colours are palette roles, see that note. Original reasoning follows.** **Toggle colours follow the palette (Accent gradient).**
User choice over the fixed colours from the reference image. Track paints role Chrome with gradient `toggle-track` (Angle 30, Accent alpha 0.7 -> Accent); knob fixed #FFFFFF; chosen caption `Ink`, other `MutedInk`; knob slides by `Animations.Tween(knob, "horizontalPosition", ...)` 0.18 s CubicInOut.

## Known gaps
- Test-verified: full suite 373 passed, 1 failed (Boot, pre-existing baseline), 1 skipped; no `[Vulkan]` lines. `Startup.FreeStickyKeepsScope`, `Startup.TrayHidesVault`, `Startup.LoginWithoutStickies`, `Startup.ToggleSwitch`. Golden-verified: toggle in both states.
- Launch check (Debug, `--tray`, user's vault): every Thorium window hidden, no GLFW/Vulkan errors from focusing the hidden primary, `--send Quit` captured `stickies` and the vault scope.
- NOT checked: the registry write (AtLogin toggled in Settings), an actual sign-in launch, the tray menu's "New sticky" and tray click showing hidden windows by hand, Opens=VaultBrowser opening the vault browser, the toggle's look inside the real Settings window.
- The Run value points at whichever exe last applied AtLogin; Debug and shipped builds share the settings folder. Not re-synced at boot.
- Restored non-primary windows are hidden after they open, so InTray launches may flash them for a frame.
- ~~A tab torn off or split out of a free sticky lands in the vault's session (`TearOff` does not carry `sessionScope`).~~ Fixed for tear-offs 2026-10-22: the torn window copies `sessionScope` from its source window, see [[multi-vault-workspace]].
- Tray "New sticky" has no name prompt; the note is named by timestamp.
- No Linux/macOS login items; `SetLoginCommand` is a no-op there.
- In the test fixture (black ground) a plain `Ink` label renders dimmer than `MutedInk`, so the toggle's chosen caption looks dimmer there; not investigated.
- Pinned stickies scale from the primary window, not the source window (differs only across mixed-DPI monitors).

**2026-10-22:** the free/vault sticky scopes are extended by [[multi-vault-workspace]] (several vaults open, closing a vault leaves its stickies); `Startup.Opens` is planned to be reused for every launch in stage B.

Related: [[desktop-stickies]], [[session-restore]], [[tray-availability-gate]], [[multi-vault-workspace]]
