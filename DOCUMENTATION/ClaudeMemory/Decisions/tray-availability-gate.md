# Decision — minimize-to-tray is gated on whether a tray can exist, and the options grey out when it cannot

**Date:** 2026-10-16
**Scope:** `ArctisAurora.EngineWork.Rendering` — `Background` (`canTray`, `Init`, `Close`, `Ask`); `ArctisAurora.Core.UI` — `ConfirmWindow` (`Ask`, `Fill`), `WindowActions` (`MinimizeToTray`), `ButtonControl.enabled`; `Thorium.Thorium` — `Main`; `Thorium/Data/XML/Documents/UI/UI.ui.xml` (`TrayButton`); `ArctisAurora.Tests.BackgroundTests` — `ClosePrompt`

## What changed
- `Background.canTray` (new, `public static bool`): `OperatingSystem.IsWindows() && <Tray Enabled>`.
- `Background.Init` returns early on `!canTray` (was `!Tray()`). `Background.Close` keeps its `!Tray()` early quit; `CloseAction.Tray` now does `if (canTray) Hide(); else Ask();`. `Background.Ask` passes a null Minimize answer when `!canTray`.
- `ConfirmWindow.Ask(source, message, check, params (string caption, Action<bool>? answer)[])` — the answer is nullable; a null answer builds that button with `ButtonControl.enabled = false` (the existing dim-to-0.4-alpha, swallowed hover and press). `ConfirmWindow.Fill` takes the same tuple type. Existing callers unchanged.
- `WindowActions.MinimizeToTray` (action `Window.MinimizeToTray`) is a no-op unless `Background.canTray`.
- `Thorium.Thorium.Main`, after `Control.ParseXML("main")`, disables the button named `TrayButton` when `!Background.canTray`. In `UI.ui.xml` the title-bar tray button is now `Name="TrayButton"`.
- `BackgroundTests.ClosePrompt`, when `!Background.canTray`: checks Minimize is present and not `enabled`, clicks it and checks the prompt stays open and the window stays visible, then Cancel closes the prompt. The Windows path is unchanged.

## Why these choices

**"Can tray" is Windows AND `<Tray Enabled>`, not `Background.active`.**
`Init` skips the icon under `--test`, so `active` would make the Windows `ClosePrompt` test lose its Minimize answer. Same reasoning as the existing rejection in [[desktop-stickies]].

**`<Tray Enabled>` off keeps today's behaviour: close quits, no prompt. The greyed path applies only when Tray is on but the OS cannot tray.**
Otherwise Carbon (tray off by default) would start prompting on Windows.

**Greyed, not hidden, and not quitting (user's call).**
The first draft hid the title-bar button and made close-without-tray quit outright. Rejected: the user wants the option visible but inert.

**A null answer means disabled in `ConfirmWindow.Ask`, rather than a third tuple field.**
A third field would break all 3 callers.

**`ClosePrompt` asserts the gate on Linux rather than being skipped.**
Skipping would test nothing; running the Windows path on Linux would shut the app down mid-suite.

## Known gaps
- The settings window's OnClose dropdown still lists `Tray` on Linux (generic enum editor); choosing it prompts with Minimize greyed.
- The greyed title-bar button has no test of its own.
- Verified on Linux only (see [[linux-test-port]]: `ClosePrompt` passes via its no-tray branch). Windows: NOT run; the Windows behaviour rests on `canTray` being true there whenever `<Tray Enabled>` is.

Related: [[desktop-stickies]], [[linux-test-port]], [[engine-testing]]
