# Decision — tests are coroutines run inside the real host, listed in XML suites

**Date:** 2026-09-27
**Scope:** `ArctisAurora.Core.Testing` — `TestRunner`, `TestEntry`, `TestContext`; `ArctisAurora.Tests` — `LayoutTests`;
`Engine` (`fixedStep`, `Init`, `WireInput`), `MainSystem.Input`, `AnimationSystem.Advance`, `LogSpool` (`errorCount`, `Write`)

**PARTIAL** — slice 1 of [test-framework-plan](../Context/test-framework-plan.md). The Carbon view, input helpers,
perf and golden-image checks are open.

## What changed
- `--test` runs every suite, `--test=<Suite>` one. `TestRunner.Arm` is called from `Engine.Init` after
  `ProfileScenario.Arm`.
- A suite is `Data/XML/Documents/Tests/<Suite>.tests.xml`, found across mounts by `VirtualFileSystem.EnumerateAll`;
  the file's name half is the suite name — the `*.inputs.xml` keybind-group rule. `<Test Action="…" Timeout="ms"/>`,
  default 10000 ms. The schema (`TestTypeSchema.xsd`) is generated from `TestEntry` + `TestRunner`, `Shutdown`'s shape.
- A test is `[A_XSDActionDependency(name, "Test")] static IEnumerator<int> X(TestContext t)`. `yield return n` waits n
  ticks. `t.Show(control)` makes it the primary window's whole tree (the `uiRoot` setter destroys the old one).
  `t.Check(bool, what)` records a failure with the caller's file:line and the test carries on.
- `Arm`: `Engine.fixedStep = 1/60`; moves the settings write root to `<parent>\TestSettings`, emptied first; collects
  `"Test"` actions; queues suites; posts a ticking `Session` entity.
- `Session`: sets `ThreadingSettings.idle.wait = false` in memory. **Boot** first — the host's own tree runs 60 ticks,
  failing if `LogSpool.errorCount > 0` since launch — then each queued test one at a time.
- A test fails on: a false `Check`; an exception (type, message, throw-site file:line; the exception itself is logged
  at `Warn`); running past `Timeout` (ticks × step); any `Error`/`Fatal` logged while it ran (`errorCount` delta); an
  `Action` naming no test. `--test=<Suite>` naming no suite is a failure too.
- Output: a `PASS`/`FAIL` log line per test, a summary line, `results.xml` in `<parent>\Tests\<yyyyMMdd-HHmmss>\`
  (`<TestRun Host Build Started>` → `<Test Suite Name Result Ticks>` → `<Failure Message File Line>`),
  `Environment.ExitCode` = failed count, then `Shutdown.Request`.
- `Engine.WireInput` returns early under `--test`: no window gets OS mouse or keyboard callbacks.
- Test clock: `MainSystem.Input` sets `Engine.deltaTime` from `fixedStep`, `AnimationSystem.Advance` its `dt`, when
  `fixedStep > 0`.
- `LogSpool.errorCount` — `Interlocked` count of `Error`/`Fatal` records in `Write`, which the handler path and
  `LogChannel.Exception` both reach.

## Why these choices

**In the real hosts, not a fifth test app (user, 2026-09-25).**
A test app would need its own `Data` and a fifth shader copy, could not run Thorium's tests, and would test a copy of
the data instead of what the host ships — a broken host palette or keybind file would pass there.

**Coroutines over real frames, no unit-test framework (user, 2026-09-25).**
How engines test logic: Unreal Functional Tests, Unity PlayMode `IEnumerator` tests, Rare's actor tests — the scenario
runs in the engine it tests. `ProfileScenario` already proved the shape here. A test that yields nothing is a plain
function check, so there is no second framework.

**Suites are XML (user, 2026-09-25).**
Order and grouping are data, like `Bootstrap.xml`/`Shutdown.xml`; the generated schema restricts `Action` to test
names. Perf budgets join the same file in slice 4. **Rejected:** attribute discovery with a name-prefix filter —
simpler, but order lives in code.

**`Timeout` is milliseconds of test-clock time (user, 2026-09-27).**
Tick-exact because the clock is fixed: 500 ms times out on tick 31.

**OS input is unwired, not filtered.**
Every trap in `aurora-verify` is OS-side (foreground lock, focus theft, extended keys, the user's own mouse, the
cross-process 65535 resize). With the callbacks never bound, nothing from the desktop reaches a run; slice 3 calls the
same handlers directly.

**A fixed 1/60 clock.**
UI, input and animation read only two wall clocks, `MainSystem.Input` and `AnimationSystem.Advance`. Key repeat, the
tap window, caret blink and `UIEngine.vert` effects (`engine.totalTime`) all derive from them. `TimeSpan` truncates
1/60 s to 166666 ticks, so `totalTime` after N ticks is N × 0.0166666 — the same every run, not exactly N/60.

**Idle wait is off for the run.**
Thorium and Carbon ship `<Idle Wait="true"/>`, so main parks on `WaitEvents` whenever nothing is pending and the runner
would stall. `FrameScheduler` re-reads the same `ThreadingSettings` object every frame, so the flip needs no loop
change, and runs go flat out, timedemo-style. **Rejected:** `FrameScheduler.RequestFrameAt(totalTime)` each tick —
`Pending` drops a wake time `totalTime` has reached, so it would need a `+ε`.

**Boot is built in, not a suite.**
It has to run before any test replaces the host's tree, and suites order by file name across mounts.

**A name that resolves to nothing fails.**
Bootstrap logs and skips; here a typo in a suite would read as a pass.

**The settings root is emptied every run.**
The `ProfileScenario` precedent moves the root so `Session.Capture` cannot wipe the user's layout; the wipe is on top,
because `Session.Capture` and `Settings.SaveAll` write into it at shutdown and the next run would start from them.

## Verified (2026-09-27, Debug, Thorium)

| Case | Result |
|---|---|
| `Thorium.exe --test` | exits by itself in ~5 s; Boot + 2 layout tests; `results.xml` written; exit 1 |
| Boot | FAIL — 55 errors since launch, all pre-existing: 48× `[Layout] skipped layout left … desired stale` (in every Thorium session since commit `bc8d73b`, none before), 6× Vulkan `vkCmdPipelineBarrier` validation, 1× default sampler asset — both in every session for weeks |
| `Layout.StackPanelArranges` | PASS |
| `Layout.StarChildCrossSize` | PASS — the WIP "`StackPanelControl.Measure` pollutes `maxCross` from star children" does not reproduce on the new stack; `LayoutEngine.MeasureStack` pass 1 skips star children |
| throwaway suite, deleted after | false check → message + `file:line`; exception → type, message, throw site; never-ending with `Timeout="500"` → `timed out after 500 ms`; `Log.Error` → `1 error(s) logged`; unknown action → `no test named …`; fixed clock PASS; exit 6 |
| `--test=Nope` | `no suite named Nope`, exit 2 |
| user settings | `%APPDATA%\Thorium\Settings` hash unchanged across every run |

## Known gaps
- A test that hangs inside one tick hangs the process — the timeout counts ticks.
- `AGlfwWindow.SeedIsInWindow` still reads OS hover once per window at creation; slice 3 has to own `isInWindow`.
- Fixtures are built in code; loading a `*.ui.xml` by name goes through the asset registry.
- Only Thorium has run `--test`.

Related: [[engine-profiling]], [[frame-scheduler]], [[engine-logging]], [[shutdown-sequence]], [[carbon-frame-viewer]]
