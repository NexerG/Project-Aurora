# Decision — console commands are the engine's own actions, reached from three sources and a Ctrl+` overlay

**Date:** 2026-09-27
**Scope:** `ArctisAurora.Core.Commands` — `CommandConsole`, `CommandPipe`; `ArctisAurora.Core.UI` — `ConsoleControl`;
`Engine` (constructor, `Init`); `ArctisAurora.Tests` — `CommandTests`, `ConsoleTests`; each host's `InputMap.inputs.xml`

## What changed
- **Commands are actions.** `CommandConsole.Build` collects every parameterless `static void` `[A_XSDActionDependency]`
  in the `Input`, `UI` and `Any` categories, plus a new `Console` category whose methods may take
  `int`/`float`/`bool`/`string`/enum arguments and may return a `string` reply. Names match ignoring case; the first
  of a duplicate name wins.
- `Execute(line, reply)` runs `;`-separated commands in order on the main thread; double quotes group an argument.
  Each answers `ok`, its returned string, or `error: …` (logged at `Warn`); `reply` gets every answer, one per line,
  once the whole line has run.
- Built-ins: `Help` (every command, `Wait` included), `Quit` (posts `Shutdown.Request`), `Wait <ms>` — parks the rest
  of the line until `Engine.totalTime` passes it on a later frame, asks `FrameScheduler.RequestFrameAt` for that
  frame, and a ticking `Pump` entity resumes it.
- `Arm()` from `Engine.Init` after `TestRunner.Arm`: builds the table; under `--test` nothing else starts. Otherwise:
  - the console window — a background thread reads stdin lines and hands them to `Engine.Post`;
  - `--exec "<line>"` — echoed, then parked on `Wait 0`, so it runs on frame 1, after the first layout;
  - `CommandPipe.Serve`, Debug builds only — `\\.\pipe\Aurora.<Host>`, `PipeOptions.CurrentUserOnly`, one connection
    at a time: reads a line, posts it, writes the replies back, gives up after 60 s.
- `--send "<line>"` is checked at the top of the `Engine` constructor: `CommandPipe.Send` connects (2 s), prints the
  replies and `Environment.Exit`s — 0 all answered, 1 an `error:` answered, 2 no host running. No engine boots.
- `ConsoleControl` — a `StackPanelControl` (role `Ground`, 240 design units, stretched across the top): a
  `ScrollableControl` of `LabelControl` rows (200 kept, oldest destroyed) over a `TextBoxControl`. Enter echoes
  `> line`, runs it, adds the answers, clears and refocuses the field; Escape (`onCancel`) closes it. The newest row is
  scrolled into view on the first tick after it was laid out.
- `Console.Toggle` (`Input`): attaches the control as `Engine.primary`'s root's last child and makes its input the
  active control; again detaches it and clears the active control if the input held it. A console left inside a
  destroyed tree is rebuilt. Bound to **Ctrl+`** in Thorium, Carbon and AuroraEditor.

## Why these choices

**Commands are the action registry, not a second one (user, 2026-09-27).**
Every keybind and UI action is a command for free; the `Console` category only adds arguments and replies.
**Rejected:** a separate `[Command]` attribute — two names for one thing.

**Three sources (user, 2026-09-27).**
The console window costs nothing — every host is a console app. `--exec` scripts a launch. The pipe is the only way
into a *running* app that does not go through OS input, which is what the `aurora-verify` traps are made of.

**`--send` lives in the `Engine` constructor (user, 2026-09-27).**
Every host constructs the engine first, so every host gets it without host edits; the process exits there.

**The namespace is `Commands`, not `Console`.**
`ArctisAurora.Core.Console` would shadow `System.Console` inside `ConsoleSink` and `Serializer`.

**An error answers at `Warn`.**
A test that sends an unknown command must not trip the runner's own "error logged" failure.

**The overlay shows its own session only (user, 2026-09-27).**
The live log belongs to the log-viewer plan, which was written for the old UI stack and needs re-checking first.

**Ctrl+`, not plain ` (user, 2026-09-27).**
Plain ` also produces a character, which `Text.Write` would type into whatever note has focus.

**Tests stay a launch mode.**
None of the sources run under `--test`: a run's fixed clock, unwired desktop input and clean settings only hold from
launch.

**The overlay is the root's last child** — the in-window context-menu precedent ([[context-menus]]): `WindowRoot`
arranges every child over its full box, and the last one paints over the tree and is hit-tested first.

## Verified (2026-09-27, Debug; Thorium unless named)

| Case | Result |
|---|---|
| `--test` | `Commands` (4) and `Console` (1) suites pass beside `Layout`; the same 7 pass in Carbon |
| `--send "Help"` to a running Thorium | the command list, exit 0 |
| `--send "UI.DumpTree"` | `ok`, `uitree.xml` rewritten |
| `--send "Nope; Help"` | `error: no command named Nope` then the list, exit 1 |
| `--send "Quit"` | `ok`; both shutdown phases ran; the process exited 0 |
| `--send` with nothing running | `no running Aurora.Thorium to send to`, exit 2 |
| `--exec "UI.DumpTree; Quit"` | echoed on frame 0, both ran on frame 1, exit 0 |
| `printf 'Help\nQuit\n' \| Thorium.exe` | the list, clean shutdown, exit 0 |
| Carbon boot | `InputHandler.LoadInputs` resolves `Console.Toggle` |
| user settings | `%APPDATA%\Thorium\Settings` identical to a copy taken before the live runs |
| `--send "Wait 300; UI.DumpTree"` / `"Wait 1000; …"` to an idle Thorium | `ok` after 565 / 1226 ms, `--send`'s own start included. It never resumed until `RequestFrameAt` stopped taking wake times already reached ([[idle-frames]]) |

## Known gaps
- The pipe serves one connection at a time — while a line waits, another `--send` times out as "no running".
- The overlay's look and the Ctrl+` keypress are **NOT GUI-verified**.
- AuroraEditor's keybind is not boot-verified; the host was not run.
- UI actions that act on a menu's target (`Tab.Close`, …) do nothing useful from the console.

Related: [[engine-testing]], [[context-menus]], [[frame-scheduler]], [[paths-from-exe-folder]]
