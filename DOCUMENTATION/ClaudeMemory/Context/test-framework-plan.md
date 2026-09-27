# Test framework — agreed plan, slice 1 landed

**Agreed:** 2026-09-25, amended 2026-09-27. **Slice 1 landed 2026-09-27**; slices 2–5 not started.
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
| 2 | Carbon Tests view — lists runs from a host's `Tests` folder; per-test pass/fail, failure text and source line; the results reader sits beside the writer (the `FrameCaptureReader` precedent). No run button | not started |
| 3 | Input helpers — `MoveTo`, `Click(control)`, `Drag`, `Key(key, mods)`, `Type(text)` through the `InputHandler` handlers `WireInput` would have bound; the runner owns `isInWindow` | not started |
| 4 | Perf — `Measure(warmup, frames)` under `Profiling.CaptureUntilFlush`; p50/p95/max/alloc per zone via `FrameCaptureReader`; budgets in the suite XML and/or an approved baseline XML; a Debug build reports `Skipped — unoptimized JIT`; Carbon opens a failure against its baseline; decide whether `--profile-scenario` becomes `--test=Perf.*` | not started |
| 5 | Visual — swapchain readback under `--test`; golden PNGs compared with ImageSharp (already referenced); `actual.png` + `diff.png`; `--test-approve`; a new golden reports `New`, not `Pass`, until looked at; Carbon shows golden/actual/diff side by side | not started |

## Facts established while designing
- `ProfileScenario` is the template: a ticking `Entity` driving the editor from inside `Main.Logic`.
- Swapchain images are created `ColorAttachment | TransferDst` in two places (`Swapchain`, `Renderer`); slice 5 has to
  find which is live.
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
