# Decision — tests are coroutines run inside the real host, listed in XML suites

**Date:** 2026-09-27
**Scope:** `ArctisAurora.Core.Testing` — `TestRunner`, `TestEntry`, `TestContext`; `ArctisAurora.Tests` — `LayoutTests`;
`Engine` (`fixedStep`, `Init`, `WireInput`), `MainSystem.Input`, `AnimationSystem.Advance`, `LogSpool` (`errorCount`, `Write`);
slice 3: `TestContext` input helpers, `UI.WindowRoot.ToWindowSpace`, `AGlfwWindow.SeedIsInWindow`, `ArctisAurora.Tests.InputTests`,
`Thorium.Tests.TextInputTests`; slice 4: `TestBudget`, `TestContext.StartMeasure`/`EndMeasure`, `TestRunner.CheckBudgets`,
`ArctisAurora.Tests.PerfTests`; slice 5: `ScreenReadback`, `TestContext.Golden`, `TestRunner.CompareShot`/`Compare`,
`Engine.clockHeld`, `RenderWindow.readback*`, `Renderer.CreateSwapchain`/`Draw`, `UI.UIScaling.For`, `Engine.InitWindowing`,
`ArctisAurora.Tests.VisualTests`

**LANDED** — slices 1–5 of [test-framework-plan](../Context/test-framework-plan.md). The Carbon view landed as
[[carbon-frame-viewer]] §17, with `TestResultsReader` beside the writer here.

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

### Slice 3 — input helpers (2026-09-27)
- `TestContext.MoveTo(control)`, `MoveTo(window, point)`, `Click(control, button)`, `Click(control, point, button = MouseLeft)`, `Drag(control, to, steps = 8)`,
  `Key(key, params mods)`, `Type(text)` each queue steps and return `steps + 1`; the test yields that.
- `Session.Step` calls `_context.RunStep()` once a tick, after the timeout check and before the wait countdown.
- `Point(control)`: centre of `arrangedRect`; records a failure (caller's file:line) when `UIEngine.HitTest` there is
  not the control or a descendant; sets `isInWindow` on that window only; `ProcessMouseMove(handle, ToWindowSpace(p))`.
- Keys and buttons: `keyTracker.EnqueueEvent(key, RawAction, Engine.totalTime)`. Chars: `ProcessCharInput`.
- `Type` key per char: `A`–`Z` (either case), `Num0`–`Num9`, `Space`, else `AnySymbol` itself.
- `SeedIsInWindow` sets `false` under `TestRunner.active`.
- `Click(control, point, button)` (2026-10-05): clicks a design-space point with any mouse button; records a failure when the point does not hit `control` or a descendant (same check as the point `Drag` overload). Added for `Sheet.GrowPopup`, because the sheet's grow strips are not hit-testable controls.
- `ContextMenus.Tick` returns early under `TestRunner.active`, so a popup test does not depend on OS focus (2026-10-05).
- `WindowRoot.ToWindowSpace(designPoint, window)` — inverse of `ToDesignSpace`.
- Tests: `Input.ClickFiresRelease`, `Input.KeyWithModifierFiresKeybind` (Ctrl+` → `Console.Toggle`, bound in all three
  hosts' `InputMap`), `Input.DragMovesSplitter`; Thorium's `TextInput.TypeWritesText`.

### Slice 4 — perf (2026-09-27)
- `<Test>` gains `<Budget Zone Thread="Main" P50 P95 Max AllocKB/>` (`TestBudget`, `TestEntry.budgets`); an absent
  limit is `-1`, unchecked. `Load` parses them into the queue entry.
- `t.StartMeasure()` → `Profiling.CaptureInto(<run>\<action>)`; `yield return t.EndMeasure()` → `Profiling.EndCapture`,
  and `Session.Step` holds the test (timeout still counting) until `Profiling.TryFinishCapture()`. Warm-up is the test
  running its work before `StartMeasure`. Spool side: [[engine-profiling]] §18.
- `StartMeasure` under `Engine.isDebug` → skip reason `unoptimized JIT`; without `Profiling.compiledIn` →
  `profiler not compiled in`. A test with no failures and a skip reason records `Result="Skipped" Reason="…"`; not in the
  exit code. The summary line is `N passed, M failed, K skipped`.
- `CheckBudgets`: `FrameCaptureReader.LoadSession`; per frame, the sum of the zone's span durations and bytes, over the
  frames it ran in; p50/p95 are nearest-rank, `AllocKB` is the worst frame. Logs one stats line per budget. Fails on
  each limit exceeded (`Main/Step.Main.Layout p95 1.019 ms > 0.01`), a missing thread, or a zone that never ran.
- Also fails: budgets with nothing measured (unless skipped); a test that ends with its capture still open
  (`EndCapture` is called, the failure says `EndMeasure never finished`).
- `results.xml`: `Capture="<action>"` on a measured test, relative to the run folder. The run folder is now chosen once
  (`RunDirectory`) at the first test, since captures land in it before `results.xml` is written.
- `Perf.RelayoutLabels`: 1000 labels, the column's width flips 400/401 every tick, 30 warm-up + 120 measured ticks;
  budget `Step.Main.Layout` P95 3 ms, Max 25 ms, AllocKB 1 — set from the first Release+`PROFILE` run.
- Measuring build: `dotnet build <Host>.csproj -c Release -p:DefineConstants="TRACE%3BPROFILE"` (`%3B` is the `;`).

### Perf targets (2026-10-02)
- `_Build/PerfTests.cmd [Host]` (default Thorium): solution build Release+`PROFILE`, then `<Host>.exe --test=Perf`
  from `bin/Release`; exit code passes through.
- New `ArctisAurora.Tests.PerfTests` targets, all 30 warm-up + 120 measured ticks. Fixtures copied from
  `ProfileScenario` (`OpenNote`, `ShowGrid`), which stays untouched and separate.
- **Roof: `Max="8"` on every budget** (user, 2026-10-02) — 8 ms per frame per zone, no headroom for spikes; a spike
  is a failure. `P95` is 2× the worst p95 of 3 Release+`PROFILE` runs, rounded up to 0.5, capped at 8; `AllocKB` is
  2× the worst frame. Budgets are per zone; there is no whole-frame budget.

| Test | Work per tick | p95 / worst max (ms), worst-frame KB — Release+PROFILE, 3 runs |
|---|---|---|
| `Perf.TypeLargeNote` | 1000×1000-char note, one char via `charInputReadQueue` + `TextInputActions.Write` | Layout 0.89–1.51 / 42.6, 187; Logic 0.18–0.46 / 6.2; MeasureBlocks 0.26–0.44 / 20.6, 163 |
| `Perf.RewrapLargeNote` | `SetPage` with a `Custom` page, 210 → 150 → 210 mm, 1 mm a tick | Layout 28.5–33.1 / 42.6, 8,722; `Text.MeasureBlock` 27.4–32.3. **2026-10-02** after [[rewrap-advance-cache]]: Layout 4.1–4.7 / 13.5 (one run of six 8.6), `Text.MeasureBlock` p50 2.9–3.2; budgets not re-derived |
| `Perf.ResizeLargeNote` | editor `preferredWidth` 800 → 320 → 800, 8 px a tick — no rewrap: text width is the paper's (`DocumentControl.Paginate`), Paged and Pageless alike | Layout 0.33–0.55 / 0.97 |
| `Perf.AnimationBurst` | 5000 buttons, one 2 s `Tween` on `state` each at the first measured tick | `Step.Animation.Step` 0.62–1.20 / 1.8 |
| `Perf.AnimationLayoutClip` | 5000 buttons playing `profile-margin` | Anim 0.47–1.01 / 1.3; Layout 0.85–1.11 / 1.5 |
| `Perf.Controls.<Name>.Static` / `.Relayout` | 1000 of one control in rows of 25 (`widthStar` 1, 16 tall, grid 1000 wide); Static changes nothing, Relayout flips the grid width 1000/1001 | budgets on `Step.Main.Input`, `Step.Main.Layout`, `Step.Main.DrawLists`, Render `Draw` |

- Controls: Panel, Button, CheckBox, Slider, Dropdown, Expander, KeyCapture, Icon, Label, TextBox, EditableLabel,
  StackPanel (nested), GridList (2 star cells), Scrollable, SplitView, TabView (2 tabs); `Table` is 100 3×3 tables in
  a note, Relayout flips the page 210/209 mm. Image is out — it needs a picture file.
- Fails by design today: `RewrapLargeNote` (max > 8; p95 > 8 until 2026-10-02), `TypeLargeNote` and `RelayoutLabels` max (14–43 ms frames — tier-up: both pass with `DOTNET_TieredCompilation=0`), and
  any test whose frame lands on a quad-mirror reallocation (Render `Draw` 9–25 ms, 1.6 KB — see WIP).

- `Step.Animation.Step` is budgeted on `Main`: at 5000 rows the scheduler runs it on Main (120/120 frames). If it
  moves to a worker the budget fails as "never ran" — `CheckBudgets` reads one thread per budget.
- Animation tests end with `Animations.StopAll` on every button; note tests end with `t.Show(new StackPanelControl())`.
- Debug `--test=Perf`: all `SKIP — unoptimized JIT`. Release+`PROFILE` suite ~60 s wall.

### What the control targets found (2026-10-02)
- Layout costs ~0.2 µs per control whatever the control; a heavy control is one built from many controls.
  Counts per instance (`--profile-pools`, `UIElements`/`UIQuads`): Panel 1/1, Expander 13/8.3 → 11/6.8, TabView 16/5 →
  12/8, GridList 3/3, Scrollable 4/10.
- `GridListControl` allocated ~14 objects per measure + arrange (LINQ, `Select().ToArray()`, offset arrays) — 820 KB
  a frame at 1000 grids. Now loops and two kept offset arrays: 0 KB. `TableControl.Cells()` still allocates a list
  per arrange (32 KB/frame on the table note).
- `Step.Main.DrawLists` and `Draw.Update` scale with quads every frame, changed or not — the list is rebuilt and
  `UIEngineModule.MirrorDrawList` copies every row (176 B) each frame. Rebuild-on-change is the open item in
  [[ui-draw-list]].
- Render `Draw` spikes (9–25 ms) are `Draw.Update` reallocating the quad mirror (grow, or shrink after
  `DataPool.ShrinkAfterSeconds`) and rebuilding descriptor sets mid-frame.
- Expander, TabView changes: see `Context/ui-orientation.md` entries. TabView's quads rose because a squeezed tab now
  shows its caption start and ✕ (user, 1a) instead of drawing empty.

### Slice 5 — goldens (2026-09-28)
- `yield return t.Golden(shot, region = null)` sets `Engine.clockHeld` and `ScreenReadback.Request(Engine.primary)`
  (`readbackEpoch = mainSystem.Epoch + 1`). `Session.Step` holds the test (timeout still counting) until
  `ScreenReadback.TryTake`, then `CompareShot` and releases the clock.
- `clockHeld`: `Main.Input` and `AnimationSystem.Advance` step 0 while the fixed clock is on.
- Render side, `Renderer.Draw`: `ScreenReadback.Record` — once main's epoch reaches the request — allocates a
  host-visible (cached preferred, coherent required) buffer and a one-time CB from `compositeCommandPool`:
  `PresentSrc→TransferSrc` (src `AllCommands`/`MemoryWrite`), `CmdCopyImageToBuffer`, `→PresentSrc`, a
  transfer→host buffer barrier. The CB rides the compositor's batch as its second CB, so `renderFinished` and the
  timeline `+4` cover it. After present, `Complete` waits the timeline for `+4`, copies out, frees buffer and CB.
- Swapchain `ImageUsage` gains `TransferSrc` only under `TestRunner.active` (armed before bootstrap).
- `TryTake` reads `R8G8B8A8*` and `B8G8R8A8*` (swapped), forces alpha 255; any other format fails the shot.
- Display scale is **1 under `--test`**: `UIScaling.For` and `InitWindowing` skip the content scale.
- `region` crops to `arrangedRect` via `WindowRoot.ToWindowSpace` against the image size; empty/off-screen fails.
- Golden: `<suite file's folder>\Goldens\<Action>.<Shot>.png`. Exact compare. Mismatch → `Fail`
  (`N px differ, max channel delta d` or `size WxH, golden WxH`), `<run>\<Action>\<Shot>.actual.png` +
  `.diff.png` (golden greyed, differing pixels red). No golden → shot and test `New`, actual only.
- `--test-approve` (with `--test`): a missing or mismatched golden is written, shot `Approved`, test `Pass`.
- `Result="New"` is not in the exit code; summary `N passed, M failed, K skipped, J new`. `results.xml`:
  `<Shot Name Result Golden(abs) Actual Diff(rel)/>`. `TestResultsReader`: `TestShot`, `TestRunInfo.newCount`.
- `TextureAsset.LoadFile` is public (Carbon uploads shot PNGs). Carbon side: [[carbon-frame-viewer]] §17.
- Test: `Visual.PanelAndLabel` — 200×80 `#2E5C8A` panel, radius 8, white label; golden committed.

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

**A helper returns its own tick count (user, 2026-09-27).**
`yield return t.Click(b)` keeps `IEnumerator<int>` as the only test shape and resumes exactly when the gesture has
been handled. Steps run in `Main.Logic`, so each lands in the next tick's `Main.Input`, where an OS callback would
have. Press and release in one tick would set `justPressed` and `justReleased` in the same `Update`.

**Keys enter at the tracker, not `ProcessKeyboard` (user, 2026-09-27).**
The handler is `MapKey` + `EnqueueEvent`; calling it would need a reverse `MapKey` (~120 entries) so tests could
speak GLFW keys. Move, char and scroll still go through their handlers, which carry real logic (window lookup).

**`Click` fails when something else is hit.**
A click that lands on an overlay would otherwise pass any test whose check was the absence of a reaction.

**`StartMeasure`/`EndMeasure`, not `Measure(warmup, frames)` (user, 2026-09-27).**
A suspended test cannot drive per-tick work, and the work is the point; the pair lets the test keep yielding.

**Budgets in the suite XML only; no baseline capture (user, 2026-09-27).**
Perf is per machine, so a baseline belongs in AppData, not the repo — and `--test-approve` is slice 5's. Carbon opens a
failing capture alone and can still pin any other capture by hand.

**`Skipped` is its own result.** A Debug or unprofiled build is not evidence either way; a pass would claim a
measurement that never happened, a fail would make every Debug run red.

**`AllocKB` is the worst frame.** A ceiling; `AllocKB="0"`-style budgets mean "allocates nothing on any frame".

**`--profile-scenario` stays separate (user, 2026-09-27).** Exploratory captures with no pass/fail.

**The readback is a second CB in the compositor's batch, not a third submit (2026-09-28).**
The timeline advances two values per frame (`+3`, `+4`), and present waits on the compositor's binary semaphore, so
a separate submit would need its own semaphore for present and a renumbered timeline. In the same batch the signals
already cover it. The compositor CB is recorded only when dirty, so the copy cannot live inside it.

**The clock is held, not the draw pinned.** Main keeps ticking during a readback (the render thread draws once per
epoch); with dt = 0 nothing time-driven moves, so any draw after the request is the same frame. UI effects run on
`totalTime - start`, so goldens do not depend on test order.

**Scale 1 under `--test` (user, 2026-09-28).** Otherwise the window size and every golden depend on the monitor.
**Rejected:** one golden per scale.

**Exact compare, no tolerance knob (user, 2026-09-28).** Goldens are per machine already (per-GPU sets left out).

**Approve from the CLI only (user, 2026-09-28).** Carbon stays show-only. **Rejected:** an Approve button.

**Crop to a control (user, 2026-09-28).** Small goldens, independent of window size. `null` still takes the window.

**`New` is not `Pass`.** A golden nobody has looked at verifies nothing; it is not a failure either, or every new
visual test would make runs red until approved.

**The settings root is emptied every run.**
The `ProfileScenario` precedent moves the root so `Session.Capture` cannot wipe the user's layout; the wipe is on top,
because `Session.Capture` and `Settings.SaveAll` write into it at shutdown and the next run would start from them.

## Verified (2026-09-27, Debug, Thorium)

| Case | Result |
|---|---|
| `Thorium.exe --test` | exits by itself in ~5 s; Boot + 2 layout tests; `results.xml` written; exit 1 |
| Boot | FAIL — 55 errors since launch, all pre-existing: 48× `[Layout] skipped layout left … desired stale` (in every Thorium session since commit `bc8d73b`, none before), 6× Vulkan `vkCmdPipelineBarrier` validation, 1× default sampler asset — both in every session for weeks |
| Boot, rerun 2026-09-27 on the queue-ownership tree | FAIL — 49: the same 48 `desired stale` (rows 98 and 167 on ticks 0–23, 216 and 219 once), the sampler; **0** `[Vulkan]` lines. Commands, Console and Layout suites all PASS |
| `Layout.StackPanelArranges` | PASS |
| `Layout.StarChildCrossSize` | PASS — the WIP "`StackPanelControl.Measure` pollutes `maxCross` from star children" does not reproduce on the new stack; `LayoutEngine.MeasureStack` pass 1 skips star children |
| throwaway suite, deleted after | false check → message + `file:line`; exception → type, message, throw site; never-ending with `Timeout="500"` → `timed out after 500 ms`; `Log.Error` → `1 error(s) logged`; unknown action → `no test named …`; fixed clock PASS; exit 6 |
| `--test=Nope` | `no suite named Nope`, exit 2 |
| user settings | `%APPDATA%\Thorium\Settings` hash unchanged across every run |
| slice 3, `Thorium.exe --test` | 11 PASS + Boot FAIL (same 48 `desired stale` + sampler); `ClickFiresRelease` 7 ticks, `KeyWithModifierFiresKeybind` 8, `DragMovesSplitter` 17 (pane 200 → 250, arranged 250), `TypeWritesText` 18 (`"Hi 5!"`) |
| slice 3, `Carbon.exe --test` | all 10 engine tests PASS; Boot FAIL on the sampler only; `TextInput` not run (Thorium's) |
| slice 3 throwaway suite, deleted after | click under a covering panel → `hits StackPanelControl, not the control` + no release; `` ` `` without Ctrl → console stays shut; `MoveTo` alone → no release; `Type` into an unfocused box → text check fails |
| slice 4, Debug Thorium / Carbon `--test` | `SKIP Perf/Perf.RelayoutLabels — unoptimized JIT`; exit 1 (Boot only); `Result="Skipped" Reason=…` in `results.xml` |
| slice 4, plain Release `--test=Perf` | `SKIP — profiler not compiled in`. Boot: **1 error, the sampler only** — the 48 `desired stale` are a Debug-only check |
| slice 4, Release+`PROFILE` `--test=Perf`, 3 runs | PASS; `Step.Main.Layout` over exactly 120 frames: p50 0.30/0.55/0.55 ms, p95 0.82/0.95/1.02, **max 5.96/10.13/10.48**, alloc 0.0 KB; 16 thread files in `<run>\Perf.RelayoutLabels\`, all closed; `%APPDATA%\Thorium\Profiling` untouched |
| slice 4 throwaway suite, deleted after | P95 `0.01` → `p95 1.019 ms > 0.01`; `Zone="No.Such.Zone"` → `never ran in the capture`; exit 2 |
| slice 5, Debug `Thorium --test=Visual` | no golden → `NEW`, `actual.png` 200×80 in the fixture's blue (R/B order right); `--test-approve` → golden written, `PASS`; rerun `PASS`; **0** `[Vulkan]` lines |
| slice 5 throwaway, deleted after | second shot 30 ticks later → `PASS` against the same golden, clock moved after; `#2E5C8B` → 15739 px differ, max delta 1, diff red over the fill, label and corners grey; whole-window shot 1280×720 at scale 1 |
| slice 5, full Debug `--test` | Thorium 12 PASS + Perf SKIP + Boot FAIL (sampler only); Carbon 11 PASS, same Boot; `Visual.PanelAndLabel` passes in Carbon against Thorium's golden (121 ticks — readback latency) |
| slice 4 throwaway Carbon test, deleted after | `t.Click` on the newest run row → `t.Click` on Open capture → `Sessions.Loaded` = that capture (16 threads), `CaptureView` shown, `TestView` hidden — also covers slice 2's click → `ShowRun` path |

## Known gaps
- A test that hangs inside one tick hangs the process — the timeout counts ticks.
- Fixtures are built in code; loading a `*.ui.xml` by name goes through the asset registry.
- AuroraEditor has not run `--test`.
- No scroll helper; double-click is two `Click`s inside the 250 ms tap window, and so is any two clicks on one
  control in back-to-back tests.
- Pointer position, `isInWindow` and a half-played gesture carry into the next test — nothing resets them.
- One run's `max` swings 6 → 10 ms on the same build; `Max` budgets need that headroom. No averaging, no reruns.
- A test that ends mid-capture calls `EndCapture` but does not wait, so its last batches can land in the next
  measured test's folder.
- A readback costs ticks against `Timeout` while held — 4 in Thorium, 121 in Carbon (main runs unpaced).
- The first readback barrier chains to the compositor's `→PresentSrc` transition only through `AllCommands`/
  `MemoryWrite`. Every slice 5 run had sync validation on (the `<Validation>` defaults, which the emptied
  TestSettings keeps) and logged no `[Vulkan]` line — one GPU, one driver.
- A test that ends with a shot pending fails and releases the clock; a late `Complete` can hand its frame to the
  next test's `Golden`.
- `--test-approve` in Release writes into the `bin` copy of `Data`; approve from Debug.
- Only the primary window is read back.
- A normal launch keeps the old swapchain flags by code (`TestRunner.active` false) — not checked in a run.

Related: [[engine-profiling]], [[frame-scheduler]], [[engine-logging]], [[shutdown-sequence]], [[carbon-frame-viewer]]
