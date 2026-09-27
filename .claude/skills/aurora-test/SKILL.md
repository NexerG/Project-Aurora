---
name: aurora-test
description: Run, write and read Aurora's in-engine tests, and drive a running app through its console instead of synthetic OS input. Use when a change needs checking, when writing a test or a suite, when a --test run fails and you need to know why, and whenever you would otherwise reach for SetCursorPos, keybd_event, SendKeys or capture.ps1 to poke the app.
---

# Testing Aurora from inside the engine

Two tools replace driving the app from outside:

| Need | Tool |
|---|---|
| a pass/fail answer about behaviour | a test, run with `--test` |
| state from a running app, or one action in it | a console command, sent with `--send` |

Both run inside the engine, so none of `aurora-verify`'s synthetic-input traps apply. A passing test earns
**test-verified** on `aurora-verify`'s ladder — a logic check, never a visual one.

## Running tests

```bash
Thorium/bin/Debug/net10.0-windows10.0.22621.0/Thorium.exe --test > <scratchpad>/run.txt 2>&1; echo "exit=$?"
```

- Any working directory works. `--test=<Suite>` runs one suite. The app exits by itself; the exit code is the number
  of failed entries.
- `grep -aE '\[Test\]' run.txt` — one `PASS`/`FAIL` line per test, then `N passed, M failed — <results.xml>`.
  Don't read the whole log.
- `results.xml` lands in `%APPDATA%\<Host>\Tests\<yyyyMMdd-HHmmss>\`: `<Test Suite Name Result Ticks>`, with
  `<Failure Message File Line>` children.
- **Boot** is always first: the host's own UI runs 60 ticks, and Boot fails on any `Error`/`Fatal` logged since
  launch. It fails today on pre-existing errors (WIP list). A change caused a Boot failure only if the error *kinds*
  changed — group them and compare with a run from before the change:
  `grep -aE ' (ERROR|FATAL) ' run.txt | sed -E 's/^[0-9:.]+ //' | sort | uniq -c`
- The host's own history is `%APPDATA%\<Host>\Logs\engine.log`. Use `grep -a`: the file holds binary bytes and plain
  grep stops matching partway through.
- Engine suites run in every host; a host's own suites only in that host.

A run changes the app, on purpose: the clock is fixed at 1/60 s a tick, no window gets OS mouse or keyboard
callbacks, idle waiting is off, and settings come from an emptied `%APPDATA%\<Host>\TestSettings` — the user's
settings, layout and vault list are never touched.

## Writing a test

Engine tests live in `AuroraEngine/Tests/` (`ArctisAurora.Tests`); a host's own in `<Host>/Tests/`.

```csharp
[A_XSDActionDependency("Layout.StarChildCrossSize", "Test")]
private static IEnumerator<int> StarChildCrossSize(TestContext t)
{
    StackPanelControl bar = new StackPanelControl { orientation = StackPanelControl.Orientation.Horizontal, preferredHeight = 32f };
    bar.AddChild(new LabelControl { text = "Untitled", widthStar = 1f });
    t.Show(bar);
    yield return 2;
    t.Check(bar.arrangedRect.height == 32f, "the bar is arranged 32 px tall");
}
```

- `t.Show(control)` makes it the primary window's whole tree and destroys the previous one.
- `yield return n` waits n ticks; two after a `Show` covers layout. Rects are design units.
- `t.Check(bool, "…")` records its file:line on failure and carries on. Word the message as the expectation.
- A test also fails on an exception (throw site reported), a timeout, or any `Error`/`Fatal` logged while it ran.
  Console `error:` answers and the runner's own lines log at `Warn`, so they never count.
- List it in a suite — one file per suite, the file name is the suite name:

```xml
<!-- AuroraEngine/Data/XML/Documents/Tests/Layout.tests.xml -->
<TestSuite xmlns="http://arctisaurora/AuroraTestTypes">
	<Test Action="Layout.StarChildCrossSize" Timeout="5000"/>
</TestSuite>
```

`Timeout` is milliseconds of test-clock time, default 10000. A suite line naming no test fails, and so does
`--test=<Suite>` naming no suite. New file → `_Build/GenerateNamespaces.cmd`.

Traps:
- **Build fixtures in code.** A `*.ui.xml` loads by name through the asset registry; there is no fixture loader.
- **Idle behaviour cannot be tested.** Idle waiting is off under `--test`, so wake and idle bugs only show against a
  normally launched app.
- **A test that hangs inside one tick hangs the process** — the timeout counts ticks, not wall time.
- **Changing the runner itself?** Write throwaway tests that fail each way — false check, exception, timeout, logged
  error, unknown name — run them, then delete them. A runner that passes everything is the failure to rule out.

## Poking a running app

Debug builds serve `\\.\pipe\Aurora.<Host>`. Send it a line from any shell:

```bash
Thorium/bin/Debug/net10.0-windows10.0.22621.0/Thorium.exe --send "UI.DumpTree"
```

- It prints the answers. Exit 0 all ok, 1 an `error:` came back, 2 no host answered.
- Commands are every parameterless `Input`/`UI`/`Any` action (`UI.DumpTree`, `Profiling.Capture`, `Settings.Open`,
  …) plus `Help`, `Quit` and `Wait <ms>`; `;` chains them. `Help` lists them all.
- `--exec "UI.DumpTree; Quit"` on the launch line runs after the first layout; the app then exits by itself.
- To check something live: start the app in the background, poll `--send Help` until it stops returning 2 (each try
  waits 2 s for the pipe), and end it with `--send Quit` — never kill it (`never-kill-thorium-mid-import`).
- A normal launch loads the user's vault and session and saves them back on `Quit`. Copy
  `%APPDATA%\<Host>\Settings` aside first and compare afterwards.
- **The pipe serves one line at a time.** While a line is waiting (`Wait`, or a command that hangs — the server gives
  up after 60 s), every other `--send` returns 2 as if nothing were running.
- The Ctrl+` overlay is the same console inside the window, for the user.
