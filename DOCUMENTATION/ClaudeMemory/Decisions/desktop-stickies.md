# Decision — desktop stickies are torn-off-tab windows; background mode is one engine-wide toggle

**Date:** 2026-10-08
**Scope:** `ArctisAurora.EngineWork.Rendering` — `Background`, `RenderWindow`, `AGlfwWindow`, `GraphicsSettings.WindowSetting`; `ArctisAurora.Core.UI` — `WindowActions`, `ConfirmWindow`, `TabViewControl`, `TabActions`, `SessionLayout`, `ContextMenus`; `Thorium/Data/XML/Documents/UI/StickyWindow.ui.xml`

## What changed

Stickies:
- A tab can be pinned to the desktop as a small "sticky" window: own compact title bar (pin toggle, close) over a `Workspace` whose pane holds tabs. Several notes in one sticky = tabs.
- `TabViewControl`: XML `StickyDocument` (UI document a pinned tab opens in); `internal PinToSticky(TabItemControl item, RenderWindow? into)` — null `into` opens a new window `sticky-N` from `stickyDocument`, 300x300 design units at the pointer, then the item becomes the active tab of the first TabView in it. Static ctor also registers context-menu source `tab-stickies` -> `TabActions.StickyEntries`. `SplitViewControl` copies `stickyDocument` onto split-off panes like `tearOffDocument`.
- `TabActions`: action `Tab.PinToSticky`; `internal StickyEntries()` ("New sticky" + one entry per open window whose `uiDocument` equals the view's `stickyDocument`, captioned by that sticky's active tab header, excluding the menu's own window); private `CopyOf(item)` (second view of a file tab via `SessionLayout.tabFactory` + `RestoreView(ViewState())`), now shared by `Split` and `Pin`. A file tab is copied (a second view sharing the note's `DocumentEditSession`, N3); a tab with no file moves.
- Action `Window.TogglePin` and `internal WindowActions.Pin(RenderWindow, bool)` — sets `RenderWindow.pinned`, `AGlfwWindow.SetFloating`, paints the control named `pin` `PaletteRole.Accent` when pinned, `MutedInk` when not.
- `RenderWindow`: `bool pinned`; `volatile bool hidden` (`Renderer.Draw` returns early on it, as for a minimized 0-area window). `AGlfwWindow`: `SetFloating(bool)` (GLFW Floating attribute); `static WorkAreaAt(x, y)` -> work area of the monitor whose full bounds hold the point (full bounds, because a tray click is on the taskbar outside the work area), primary's otherwise.
- `SessionLayout`: `SessionWindow.Pinned`; `Record` writes `window.pinned`; `OpenRecorded` applies it via `WindowActions.Pin`. A sticky restores as a sticky because its record's Document is `sticky-window`.
- `ContextMenus.OpenAtScreen(List<ContextMenuEntry>, int x, int y, RenderWindow scaleFrom)` — always its own menu window, sized by `scaleFrom`, clamped into `WorkAreaAt`, flipped above the point when it would cross the bottom, focused. `Open`/`Host` unchanged.
- Data: `StickyWindow.ui.xml` (asset `sticky-window` in `ThoriumAssets.assets.xml`): WindowRoot 300x300 -> WindowFrame -> 22-unit TitleBar (accent bar, spacer, pin Button with Label `Name="pin"` Text `^`, close Button `Window.Close`) -> `Workspace Default="tab-pane" Pane="tab-pane"`. `StickyDocument="sticky-window"` on the EditableTabs in Thorium's `TabPane.ui.xml` and `Workspace.ui.xml`. `Tab.menu.xml` gains `<ContextSubmenu Text="Pin to desktop" Source="tab-stickies"/>`.

Background mode (`ArctisAurora.EngineWork.Rendering.Background`, static, Win32 P/Invoke):
- `GraphicsSettings.WindowSetting.onClose` (XML `OnClose`, nested enum `WindowSetting.CloseAction { Ask, Tray, Quit }`, `[A_XSDType("CloseAction", "Settings")]`), default `Quit`; replaces the bool `CloseToTray`. Thorium ships `OnClose="Ask"` in `Graphics.settings.xml`. Shows in the Settings window as a dropdown (enum member).
- `Init()` — bootstrap action `Background.Init`, between `Engine.SystemSetup` and `Engine.InitWindowing` in the shared `Bootstrap.bootstrap.xml`. No-op returning true when `onClose` is `Quit` or `TestRunner.active` (so Ask also claims single instance and adds the tray). Otherwise host name = entry assembly name; `Claim(name)` = named mutex `Local\ArctisAurora.Background.<name>`; if another copy owns it: log, `SignalShow(name)` (FindWindowW by class `ArctisAurora.Background` + title, PostMessage WM_APP+2), `Environment.Exit(0)`. Else `CreateTray(name)`: RegisterClassExW, a hidden top-level window, Shell_NotifyIconW NIM_ADD with IDI_APPLICATION and tooltip = host name.
- `Remove()` — shutdown action `Background.Remove`, a `Commit` step before `Logging.Flush` in `Shutdown.shutdown.xml`; NIM_DELETE, no-op without a tray. `active` — the tray exists.
- `Close()` — the main window's close: `Tray` -> `Hide()`, `Quit` -> `Shutdown.Request()`, `Ask` -> private `Ask()` opens `ConfirmWindow.Ask` ("Close <host> or minimize it to the tray?", buttons Cancel / Minimize / Close, check "Don't ask again"). Private `Answer(CloseAction, bool remember)`: remember sets `onClose` to the chosen action (Tray or Quit) and calls `SettingsRegistry.Commit()`; then `Hide()` or `Shutdown.Request()`. Cancel does nothing. Private helpers `Settings()` (the `WindowSetting`), `HostName()` (entry assembly name).
- `Hide()` — creates the tray first when it isn't up (`!active && !TestRunner.active`), then `NoteActions.SaveEdited()`, `SessionLayout.Capture()`, `Engine.primary.hidden = true`, hides the OS window. `Show()` — clears `hidden`, shows, focuses. `Quit()` — `Show()` then `Shutdown.Request()`.
- WndProc (dispatched on main by GLFW's PollEvents/WaitEvents, since the window is created on the bootstrap/GLFW thread): tray left-button-up -> `Engine.Post(Show)`; right-button-up -> `Engine.Post(OpenMenu)` = `ContextMenus.OpenAtScreen` at the cursor with "Open <host>" and "Quit"; WM_APP+2 -> Show; WM_ENDSESSION(wParam true) -> `Shutdown.RunPhase(Shutdown.commitPhase)` synchronously.
- `WindowActions.Close(RenderWindow)` on `Engine.primary`: `Background.Close()`. The drawn X, Alt+F4, taskbar Close and taskkill without /F all go through it, so they hide, quit or ask per `OnClose`. Thorium's menu "Exit" (`ExitApplication` -> `Engine.CloseWindow(primary)`) and the tray menu's Quit still quit outright, without a prompt.
- `ConfirmWindow` generalized: overload `Ask(RenderWindow source, string message, string? check, params (string caption, Action<bool> answer)[] answers)` — a right-aligned row of answer buttons in the order given and, when `check` is non-null, a CheckBox + label line between message and buttons (window grows by `checkHeight`, 28 units). Each answer receives the checkbox state. The old `Ask(source, message, onConfirm, onCancel)` stays and delegates (Cancel/Confirm, no check line); `VaultBrowserControl` unchanged. Content is rebuilt per ask (`Fill`) inside a column built once (`_column`, `_check`); `Answer(Action<bool>)` replaces `_message`, `_onConfirm`, `_onCancel`, `Confirm()`, `Cancel()`.
- Tests: `Thorium.Tests.StickyTests` (`Sticky.PinPinRestore`, suite `Sticky.tests.xml`); `ArctisAurora.Tests.BackgroundTests` (`Background.HideShow`, `Background.MenuClamp`, `Background.ClosePrompt`, suite `Background.tests.xml`). `ClosePrompt`: with `OnClose=Ask`, `WindowActions.Close(primary)` opens `ConfirmWindow` with Cancel/Minimize/Close; clicking Minimize hides the primary; an unticked check leaves the setting at `Ask`.

## Why these choices

**A sticky is a torn-off-tab-style window with its own UI document, not a new control type.**
Tear-off, N3 multi-view, session window records and WindowFrame resize already existed. "Several notes in one sticky" is tabs.

**Pin means always-on-top (GLFW Floating).**
Rejected: a desktop layer under every app (needs WndProc subclassing to stop activation raising it); no toggle at all.

**Pin to desktop copies the view and the tab stays where it was.**
The copy is a second view sharing the note's session (N3). Rejected: moving the tab. Non-file tabs (none today in practice) move, same as Split.

**Stickies show in the taskbar and Alt+Tab.**
User choice; rejected `WS_EX_TOOLWINDOW`.

**The tray menu is the engine's own context menu, through the new `OpenAtScreen`.**
User choice; rejected native TrackPopupMenu (native look, ~20 lines). `OpenAtScreen` clamps only itself; general menu clamping (WIP "context menu gaps") stays open.

**The primary is hidden, never destroyed.**
The renderer's device, queues and descriptor set 0 are tied to `Engine.primary`. Hidden windows skip Draw.

**Close-to-tray branches in `WindowActions.Close`, not as a refusing Shutdown `Request` step.**
A refusing step flips `Shutdown.isClosing` and would make the X button change what quitting means. This supersedes the calendar plan's proposal (C4: a `Request` step hides the window and returns false).

**One engine-wide setting: tray, single instance and the close behaviour all hang off `WindowSetting.OnClose` (Ask | Tray | Quit); tray and single instance exist when it is not `Quit`.**
Originally a bool `CloseToTray`; now one enum rather than that bool plus a separate "ask" bool, because Ask needs the tray to exist and so shares the gate, and one enum keeps the gate a single comparison. User choice. Wired as a shared bootstrap step, not in Thorium's Main. Rejected: claiming the mutex in Main before `new Engine()` — Engine's ctor handles `--send`, and `--test` runs launched while the user's app is open would be swallowed. Host name comes from the entry assembly. Skipped under `--test`.

**The close prompt is the existing `ConfirmWindow` generalized to N answers plus an optional check line.**
User choice. Rejected: a separate ClosePromptWindow class duplicating ~120 lines of ConfirmWindow's build/position code.

**The prompt has a Cancel button.**
User choice. `ConfirmWindow` cannot be dismissed any other way, so without Cancel an accidental X would force a choice.

**Only the window close asks.**
Thorium's menu Exit (`ExitApplication`) and the tray menu's Quit still quit outright.

**"Don't ask again" persists through `SettingsRegistry.Commit()`** (fires OnChanged + SaveAll).
Accepted cost: if the Settings window is open with unapplied edits at that moment, they are applied and saved too. Rejected: a per-setting save path.

**`Hide()` creates the tray on demand.**
Switching Quit -> Ask/Tray at runtime would otherwise hide the window with no tray to bring it back. Single instance is still claimed only at boot, so it starts at the next launch.

**No migration for the `CloseToTray` rename.**
The user's write root had no stored `CloseToTray`: the bool landed the same day, XML only, and never reached a user file.

**The message window is a hidden top-level window, not HWND_MESSAGE.**
Message-only windows receive no WM_ENDSESSION broadcast. WM_ENDSESSION runs the Commit phase synchronously because the process is killed after it returns.

**Tray icon removal is a Commit step.**
User choice, over an AppDomain.ProcessExit handler; neither runs on a crash.

**Hide saves edited named notes and captures the session; unnamed notes wait for Quit's prompt.**
The capture is in memory; the Commit phase's `Settings.SaveAll` persists it at quit or WM_ENDSESSION.

Extends [[shutdown-sequence]] (§6 per-window close and §7 OS close now hide the primary when the tray is up) and the calendar plan's C4 (tray and single instance landed; start at login, toasts, reminders still open).

## Known gaps
- NOT GUI-verified: tray icon appearance, left/right click on it, the engine tray menu actually taking focus (Windows may refuse foreground to a background process; `ContextMenus.Tick` would then close it next frame), X hiding the main window, the second launch bringing the window forward, sign-out saving, sticky look/resize/pin by hand, the "Pin to desktop" submenu listing existing stickies and adding a note into one.
- Tray icon is IDI_APPLICATION — Thorium has no ApplicationIcon/.ico.
- No re-add after Explorer restarts (TaskbarCreated not handled).
- A submenu opened from the tray menu would be hosted relative to the hidden primary and may land in its hidden root (the tray menu has no submenus today).
- `--profile` runs launched while the user's Thorium is running are swallowed by single instance (only `--test` is exempt).
- Start at login, toasts, reminders not done (calendar C4 rest).
- Close prompt: test-verified by `Background.ClosePrompt`; NOT GUI-verified: the prompt's look, the check line layout, "Don't ask again" persisting across a restart (that Commit + user file write path is untested), the Settings dropdown.
- Single instance is not claimed when switching away from `Quit` mid-run (next launch).
- If another `ConfirmWindow` prompt is already open, closing the main window does nothing (`ConfirmWindow` refuses a second ask).
- Switching to `Quit` mid-run leaves the tray icon up until exit (harmless; the tray menu still works).
- Stickies belong to the vault's session scope: a vault switch closes them; switching back restores them.
- A hidden window's caret blink still requests idle wake-ups.
- No drag-a-tab-onto-another-window's-strip; adding to an existing sticky is through the submenu.
- Per-sticky colours not done.

Related: [[shutdown-sequence]], [[tab-view-control]], [[context-menus]], [[session-restore]], [[note-model]], [[window-chrome-and-label]]
