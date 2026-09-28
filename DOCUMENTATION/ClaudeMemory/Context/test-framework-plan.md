# Test framework — agreed plan, all five slices landed

**Agreed:** 2026-09-25, amended 2026-09-27. **Slices 1–4 landed 2026-09-27, slice 5 2026-09-28 — plan complete.**
**What it reads:** [../Decisions/engine-testing.md](../Decisions/engine-testing.md) (what landed and why),
[../Decisions/engine-profiling.md](../Decisions/engine-profiling.md) (captures, `ProfileScenario`),
[../Decisions/carbon-frame-viewer.md](../Decisions/carbon-frame-viewer.md).
**Checklist form:** the test-framework item in `DOCUMENTATION/Work in Progress List.md`.

This file exists so the work can be picked up cold. Each slice still gets its own file-level plan, approved before it
starts (CLAUDE.md §2).

## Goal

Launch a host with `--test` and get back what passed and what failed — logic, performance and visual — instead of
driving the app from outside with synthetic OS input and screen capture.

## Standing decisions

Settled. Do not re-litigate without asking.

| Decision | Why |
|---|---|
| **Tests run in the existing hosts** — `Thorium.exe --test`; engine tests in any host, a host's tests only in it (user, 2026-09-25) | the host's own data is what is under test; no fifth app, `Data` or shader copy |
| **No unit-test framework** — coroutines over real frames (user, 2026-09-25) | how engines test logic; see [[engine-testing]] |
| **Suites are XML**, one `<Suite>.tests.xml` per suite (user, 2026-09-25) | order, grouping and budgets as data |
| **`Timeout` is milliseconds** of test-clock time (user, 2026-09-27) | — |
| **Carbon only shows results; it never launches a run** (user, 2026-09-27) | runs are started by hand; Carbon reads them when Carbon itself is opened |
| **Swapchain images get `TransferSrc` only while `--test` runs** (user, 2026-09-25) | goldens need readback; a normal run keeps today's usage flags |
| **Golden readback comes from the swapchain, after compositing, before present** | the UI module's own target is pre-compositor — not what is seen |

## Slices

| # | Slice | Status |
|---|---|---|
| 1 | Runner, XML suites, `Check`, fixed clock, OS input unwired, Boot, `results.xml`, exit code | **landed 2026-09-27** |
| 2 | Carbon Tests view — lists runs from a host's `Tests` folder; per-test pass/fail, failure text and source line; the results reader sits beside the writer (the `FrameCaptureReader` precedent). No run button | **landed 2026-09-27** — [[carbon-frame-viewer]] §17 |
| 3 | Input helpers — `MoveTo`, `Click(control)`, `Drag`, `Key(key, mods)`, `Type(text)` through the `InputHandler` handlers `WireInput` would have bound; the runner owns `isInWindow` | **landed 2026-09-27** — [[engine-testing]] § Slice 3 |
| 4 | Perf — `Measure(warmup, frames)` under `Profiling.CaptureUntilFlush`; p50/p95/max/alloc per zone via `FrameCaptureReader`; budgets in the suite XML and/or an approved baseline XML; a Debug build reports `Skipped — unoptimized JIT`; Carbon opens a failure against its baseline; decide whether `--profile-scenario` becomes `--test=Perf.*` | **landed 2026-09-27** as `StartMeasure`/`EndMeasure`, suite-XML budgets only — [[engine-testing]] § Slice 4 |
| 5 | Visual — swapchain readback under `--test`; golden PNGs compared with ImageSharp (already referenced); `actual.png` + `diff.png`; `--test-approve`; a new golden reports `New`, not `Pass`, until looked at; Carbon shows golden/actual/diff side by side | **landed 2026-09-28** — scale 1 under `--test`, exact compare, crop to a control, CLI-only approve — [[engine-testing]] § Slice 5 |

## Slice 3–4 shape, agreed (user, 2026-09-27 — recommended on every fork)

- **Helpers queue a scripted gesture and return its tick count**: `yield return t.Click(button);`. The runner
  feeds one step per tick from `Session.OnTick` (`Main.Logic`), so it lands in the next tick's `Main.Input`.
- `Click` = move → down → up → settle, and fails if `UIEngine.HitTest` at the point is not the control or inside
  it. `Key` = mods down → key → key up → mods up. `Type` = per char key down + `ProcessCharInput`, then key up.
- **Keys and mouse buttons go straight to `keyTracker.EnqueueEvent` with engine `Keys`**; move, char and scroll
  through their handlers. Rejected: a reverse `MapKey` table (~120 entries) to call `ProcessKeyboard`.
- `Type` presses the real key for letters, digits and space, **`Keys.AnySymbol` itself** for anything else.
- New `WindowRoot.ToWindowSpace` (inverse of `ToDesignSpace`); `SeedIsInWindow` sets `false` under `--test`.
- `Type` test lives in Thorium — `AnySymbol → Text.Write` is bound only in Thorium's `InputMap`.
- **Perf: `StartMeasure()` / `EndMeasure()`** — the test drives its own per-tick work between them; warm-up is
  running it before `StartMeasure`. Capture goes to `<run>\<Suite.Name>\` via a `directory` on
  `FrameSpool.BeginSession` (no `Prune`), ended by a non-terminal `Profiling.EndCapture`.
- **Budgets in the suite XML only** (`<Budget Zone Thread P50 P95 Max AllocKB/>`); no baseline capture yet.
- **`Skipped`** for Debug and for Release without `PROFILE`; not counted in the exit code. Measuring build:
  `dotnet build -c Release -p:DefineConstants="TRACE;PROFILE"` — no `Profile` configuration.
- **`--profile-scenario` stays separate** — exploratory, no pass/fail; `--test=` has no wildcard.

## Facts established while designing
- `ProfileScenario` is the template: a ticking `Entity` driving the editor from inside `Main.Logic`.
- Swapchain images are created `ColorAttachment | TransferDst` in two places (`Swapchain`, `Renderer`); the live one
  is `Renderer.CreateSwapchain` — `Swapchain` serves only the legacy `RendererTypes`.
- `Engine.WireInput` binds six callbacks — `ProcessMouseMove`, `ProcessMouseClick`, `ProcessKeyboard`,
  `ProcessCharInput`, `RenderWindow.MouseCrossedBorder`, `ProcessScrollWheel` — slice 3's injection points.
  `AGlfwWindow.SeedIsInWindow` reads OS hover at window creation, outside them.
- UI effects animate off `engine.totalTime`, which the render thread reads when it draws, so a golden needs the test
  clock held until its readback lands, and the readback pinned to the published draw list.
- The primary window's size depends on the monitor's content scale, so a golden needs a fixed window size per test.

## Left out, deliberately
- xUnit/NUnit and mocks.
- A hidden or offscreen window — runs are visible; offscreen rendering is roadmap Phase C.
- Recording and replaying input.
- Golden sets per GPU.
- Host-shell tests (vault, tabs, session) — they need a test-vault fixture.
- Stripping tests from a shipping build.
- A watchdog for a test that hangs inside one tick.
