# Decision — systems are steps of one frame graph, placed into stages by the columns they touch

**Date:** 2026-09-23
**Scope:** `ArctisAurora.Core.Threading` — `FrameScheduler`, `FrameStep`, `FrameGraphDefinition`, `FrameStepDefinition`, `DedicatedDefinition`, `ThreadingSettings`, `ThreadsSetting`, `FrameCapSetting`, `ThreadedSystem` (`Step`, `StartScheduled`, `StopScheduled`, `WaitOut`, `Dedicated`, `Period`), `MainSystem`; `ArctisAurora.Core.Data` — `DataPool` (`AssertAccess`), `DataManager` (`FrameEdge()`, `ResolveComponent`), `PoolDefinition`; `ArctisAurora.Core.Diagnostics` — `Profiling.Frame.Begin(owner, frameIndex)`; `Engine`; data `AuroraEngine/Data/XML/Documents/Frame.frame.xml`, `Pools.pools.xml`

Step 1 of [../Context/frame-scheduler-plan.md](../Context/frame-scheduler-plan.md). Supersedes the free-running thread per system of [[cross-system-change-notification]] and pool ownership of [[ecs-rework-data-pools]].

## What changed
- **`Frame.frame.xml`** (`<FrameGraph>`, XSD category `Systems`, read by `FrameScheduler.Load` after `BuildLanes`):
  - `<Dedicated System="Render"/>` — its own thread, its own `Loop`, never at a barrier.
  - `<Step System Pinned Reads Writes/>` — runs in the graph. `Reads`/`Writes` list `Pool` (every column) or `Pool.Component` (one column; the component type as `Pools.pools.xml` names it, resolved by `DataManager.ResolveComponent`).
  - Today: Main (pinned) writes `UIElements UIQuads Entities Gradients Effects`; Animation writes `Paints Animations Signals Keyframes`; Physics touches nothing — one stage.
- **Stages are computed** (`Wire`, `Place`): a reader waits for every writer of its column (a column a step both reads and writes counts as a write); writers of one column run in list order; a step lands one stage after the latest step it waits for; stages keep list order.
- **Load errors:** a loop throws naming the chain, e.g. `Main waits for Animation (Paints.GpuPaint) → Animation waits for Main (UIElements.ArrangeData)`. Main as Dedicated throws. A system listed twice throws. A system listed nowhere logs a Warn.
- **Pools have no owner.** `System=` is gone from `Pools.pools.xml` and `PoolDefinition`; `DataPool.OwnerName`/`OwnerSystemId`/`SetOwnerSystemId` and `DataManager.ResolveOwners` are deleted.
- **`DataPool.AssertAccess(need, write, op)`** (DEBUG) replaces `AssertOwner`. `need` = column bits, 0 = any column.
  - Write, one column: `GetSpan`, `GetRef`, `CopyFrom`, `UpdateRange`. Write, every column: `Allocate`, `Free`, `Rewind`, `Append`, `FrameEdge`. Write, any column: `MarkContentDirty`, `MarkRangeDirty`, `MarkOrderDirty`.
  - Read (new): `Backing`, `CopyTo`, `CopyRange`, `OwnerAt`. `CopyTo` no longer goes through `GetSpan`.
  - Outside a step nothing is checked, except that a write from a Dedicated thread throws.
- **`DataManager.FrameEdge()`** runs every pool the running step writes in full (`FrameStep.WritesAll`).
- **`FrameScheduler.Run`** is the main thread's loop. Per stage: due unpinned steps go into a fixed array handed out through one 64-bit word (`count << 32 | next`) claimed by compare-exchange; main runs its pinned steps, then claims the rest until `remaining` is 0 (`Scheduler.Barrier` zone). Counters sit 64 B apart. No allocation per frame.
- **Workers** (`Work`) spin 50 µs after their last step, then park on a `SemaphoreSlim`; main releases `min(steps, parked)` per stage.
- **`ThreadedSystem`:** `Step()` = drain → `Tick` → publish → epoch, setting and restoring `Current`; `StartScheduled`/`StopScheduled` run `OnStart`/`OnStop` for graph systems; `Start()` + `Loop` only for Dedicated; `Adopt` and `Send` deleted. `TargetPeriodMs` on a graph system = at most once per period, owed time carried, at most once per frame (Physics 32 ms).
- **Frame rate uncapped by default.** Main's and Animation's 120 Hz overrides are gone.
- **Settings** `<Threading><Threads Count/><FrameCap MaxFps/></Threading>`: `Count` 0 = logical cores − 1 − Dedicated (22 workers on 24), 1 = the graph on the main thread alone (Dedicated keep their threads), N = N − 1 workers, read at start. `MaxFps` 0 = uncapped; paces the graph and every Dedicated loop, read every frame. `ThreadedSystem.WaitOut` = `Sleep(1)` until 2 ms remain, then spin.
- **Profiling:** `Frame.Begin(owner, frameIndex)`. Main's lane is `Main`, workers' `Worker N`; `<F I>` is `FrameScheduler.Frame` for both. A worker opens a frame on its first step of that frame and closes it when the next frame's first step arrives. Zones `Step.<System>` per step.
- **`EngineWork.JobSystem` deleted** — busy-spinning, `Action` per job, no callers.
- **Lanes unchanged in step 1.** `Post`/`OnPost` still carry Animation's requests and values; a system's step runs on one thread at a time, so the SPSC lanes stay single-producer.

## Why these choices

**One graph instead of a thread per system (user, 2026-09-23).** A thread per system caps parallelism at the system count, so a 12-core machine sat half idle, and the goal is uncapped frame rates (~300 fps, Tarkov-class). Rejected: free-running threads plus a worker pool beside them.

**Access belongs to steps, not pools (user).** A pool owned by one thread cannot be written by two steps of different systems in sequence. Per-step reads and writes let readers see finished data and writers go one after another.

**Per column, not per pool (user).** "That's the whole point" — physics writing positions and animation writing colours of one pool must not serialise.

**Stages are computed, not hand-written (user).** The read/write lists already say everything a hand-written stage would.

**A loop is an error, not broken by list order (user).** "There should not be loops where A wants B and B wants A." Two systems that really need each other's data read one side through a mailbox (last frame's copy) — step 2.

**Steps of one class never overlap (user, and the safe pick).** A class's private fields are invisible to the checker; a mistake here costs speed, never data. Unreachable in step 1, where a system is exactly one step.

**Render stays Dedicated, and Dedicated keep their cores in one-thread mode (user).** Vulkan's blocking waits would park a worker. `Threads=1` makes only the graph linear.

**Threading is optional (user).** `Threads=1` exists so a system can be profiled on one core; a system that crawls single-threaded is optimised before threads are considered.

**Stages plus one claim word, no work stealing.** Tens of steps a frame. The count lives in the same word as the index because a separate count let a worker, holding a stale index, claim a slot of the next stage while main was still rewriting it.

**Spin 50 µs, then park.** Waking a parked thread costs tens of µs, and a frame has several stages; at 300 fps that is budget-sized.

## Measured (2026-09-23, Debug, zones on, 24 logical cores, RTX, uncapped)
`--profile-scenario=animation`, per tick, against [[animation-core]] § Measured at scale. That table was taken at 6726940; this one is at ca9965b plus this change, and the UI path changed in between, so the drops are not credited to the scheduler — the claim is no regression.

| | 1k | 5k | 20k |
|---|---|---|---|
| `Anim.Step`, state hold | 0.33 | 1.05 | 3.78 ms (was 0.47 / 1.9 / 6.8) |
| `MainTick`, state hold | 0.28 | 1.21 | 1.54 ms (was 1.1 / 2.9 / 4.2) |
| `MainTick`, margin hold | 1.75 | 9.25 | 38.7 ms (was 3.4 / 14 / 53) |
| `ResolveLayout`, margin hold | 1.44 | 8.03 | 36.7 ms (was 2.3 / 11.6 / 49) |
| teardown frame | — | 107 | 1,732 ms (was 138 / 2,266) |

- Scheduler overhead (frame − `Step.Main` − barrier wait, on main): mean 4 µs, p99 14 µs.
- Barrier wait p50 0, p99 2.8 ms — the 20k frames where single-threaded `Anim.Step` outlasts Main.
- A worker claimed Animation every frame; main never ran it.
- Idle Thorium, uncapped: ~2.1 cores busy, Main ~2,100 frames/s. `Threads=1` + `MaxFps=120`: 0.37 cores, 119.8 frames/s.
- Physics: 34 ms p50 between steps; 17.9/s over the whole scenario, because frames over 32 ms run it once per frame.

## Known gaps
- **Physics runs at most once per frame** — below ~31 fps it slows with the frame. Substeps are physics' own design.
- **A native window drag pauses animation and physics too** — the modal loop blocks main inside its step; render keeps drawing.
- **The theme crossfade steps at the frame rate**, where it had its own 120 Hz.
- **An uncapped graph can outrun render** and compute frames nobody sees — the one-frame-ahead limit comes with the render copy stage.
- **Thorium idles at ~2 cores** uncapped; `MaxFps` caps it.
- **Pacing requests no timer resolution.** Accurate here (119.8 of 120); unverified elsewhere.
- **Carbon's frame strip stacks 24 lane labels on one another.** Worker lanes load, gaps and all, and the timeline draws them.
- **A burst capture dropped 327 Main frames** at uncapped rates.
- **Verified:** builds clean; Thorium and Carbon boot; GUI — Thorium draws and the sidebar hover highlights; Thorium and Carbon shut down through the close button; `Threads=1`; `MaxFps`; a loop refuses to start; an undeclared write throws on frame 0. **NOT verified:** the dirty-note prompt's cancel/confirm; dt after a title-bar drag.

Related: [[cross-system-change-notification]], [[ecs-rework-data-pools]], [[animation-core]], [[engine-profiling]], [[engine-logging]], [[settings-registry]]
