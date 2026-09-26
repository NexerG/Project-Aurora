# Decision — systems are steps of one frame graph, placed into stages by the columns they touch

**Date:** 2026-09-23 (step 1), 2026-09-23 (step 2 — § Step 2 below)
**Scope:** `ArctisAurora.Core.Threading` — `FrameScheduler` (`FindActions`, `Build`, `Wire`, `Reaches`), `FrameStep`, `FrameGraphDefinition`, `FrameStepDefinition`, `DedicatedDefinition`, `ThreadingSettings`, `ThreadsSetting`, `FrameCapSetting`, `ThreadedSystem` (`RunStep`, `EndFrame`, `StartScheduled`, `StopScheduled`, `WaitOut`, `Dedicated`, `Period`), `MainSystem`, `PhysicsSystem`; `ArctisAurora.Core.Data` — `DataPool` (`AssertAccess`, `ElementBytes`), `IPoolColumn.ElementBytes`, `DataManager` (`ResolveComponent`), `PoolDefinition`; `ArctisAurora.Core.Animation` — `AnimationSystem`, `Animations` (`Write`, `ApplyValues`), `A_Animatable`, `AnimatableProperty`, `AnimationValue`; `ArctisAurora.Core.Diagnostics` — `Profiling.Frame.Begin(owner, frameIndex)`; `Engine` (`Input`, `Interpolate`); data `AuroraEngine/Data/XML/Documents/Frame.frame.xml`, `Pools.pools.xml`

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
**Unoptimized Debug JIT, ~5× slow** — [[profiling-unoptimized-jit]]; compare only within this note's Debug tables. `--profile-scenario=animation`, per tick, against [[animation-core]] § Measured at scale. That table was taken at 6726940; this one is at ca9965b plus this change, and the UI path changed in between, so the drops are not credited to the scheduler — the claim is no regression.

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

## Step 2 — sub-steps, frame edges, lanes deleted, animation writes in place (2026-09-23)

### What changed
- **A step names an action, not a system.** `<Step Action="Main.Input" …/>` resolves to an instance method tagged `[A_XSDActionDependency(name, "Frame")]` on a `ThreadedSystem` subclass, bound to that system (`FrameScheduler.FindActions`); the declaring system is the step's `System`. `System=` survives only on `<Dedicated>`, which names a whole thread-owning system.
- **Frame edges are steps:** `<Step Edge="Pool"/>` writes every column of that pool and runs `DataPool.FrameEdge`; `System` is null. A `Step` has exactly one of `Action`/`Edge` (load error otherwise). Edges are `Step` attributes, not an `<Edge>` element, because the generated schema is an `xs:sequence` — a separate element could not sit between steps.
- **Load errors:** unknown action or pool, the same action or edge listed twice, an action of a Dedicated system, a Frame action declared twice. A system with no step and not Dedicated logs a Warn.
- **Same-system rule live in `Wire`:** after the data waits, two steps of one system that do not already reach each other (`Reaches`, transitive) get a wait, later-listed on earlier-listed; the `Loop` message labels it `same system`.
- **`Engine.MainTick` split** into Main actions on `MainSystem`: `Main.Input` (dt, `Engine.Input` — poll, reap, posted work, keybinds, `HandleUI`, drag ghost, context menus), `Main.Logic` (`Engine.Interpolate` — lifecycle + `OnTick`), `Main.Apply` (`Animations.ApplyValues`), `Main.Layout` (`UIEngine.ResolveLayout`, zone `ResolveLayout` kept), `Main.DrawLists` (`UIEngine.BuildDrawLists`). Zones `MainTick`, `Interpolate`, `FrameEdge`, `RefreshWindowRanges` are gone — `Step.<Action>` per step. Physics: `Physics.Step` (`PhysicsSystem.Simulate`, empty). Animation: `Animation.Step` (`AnimationSystem.Advance`).
- **Graph today** (`Frame.frame.xml`), 7 stages: `Main.Input` → `Main.Logic` → `Animation.Step` ‖ `Physics.Step` → `Edge.UIElements` → `Main.Layout` → edges of `UIQuads Entities Gradients Effects Paints` → `Main.DrawLists` (`Main.Apply` deleted 2026-09-26, [[animation-core]] § Drained pools). Every Main step declares step 1's Main set plus `Animations Paints`; only Input and Logic add `Signals`; Logic adds `AnimationDone`, Layout `LayoutDirty`. Physics declares `Entities.TransformData` so it runs after entity logic in the same frame.
- **Pinned:** Input (GLFW), Logic (entity `OnTick` is arbitrary code — the profiling scenario calls `glfwSetWindowSize` from it), Apply (`onDone` callbacks and setters). Layout and DrawLists reach no GLFW today; pinned because every Main step already waits for the one before, so a worker would only add a handoff. All edges unpinned, `UIElements` included — its sort walks the control tree, which nothing else touches in that stage.
- **Command lanes deleted:** `ArctisAurora.Core.Data.Commands` (`CommandLane`, `CommandArena`, `SystemCommand`, `CommandApplier`); `ThreadedSystem` `_inbox`/`_outbox`/`BuildLanes`/`Post`/`OnPost`/`Drain`/`Publish`; `IPoolColumn.WriteBytes`/`FillBytes`/`CopyWithin` (replaced by `ElementBytes`); `DataManager.FrameEdge()`; `FrameStep.WritesAll`. `SystemId` stays — the log stamps it.
- **`ThreadedSystem`:** `Tick` is `virtual`, run only by a Dedicated `Loop` via a private `Step`. Graph steps run through `RunStep(Action)` (sets `Current`, sums step time); `FrameScheduler.Run` calls `EndFrame()` on every scheduled system after the last stage — epoch +1 and `LastTickMs` = that frame's summed step time, only if a step ran. `RenderSystem.OnStart` still waits for Main's epoch to leave 0 (after the first full frame).
- **Pool stats** (`Profiling.Frame.Pool`) are reported for every pool at frame end on Main's lane, not per edge — pools without an edge still report.
- **Animation requests are direct writes** by the calling Main step — see [[animation-core]] § Step 2 of the frame scheduler.

### Why these choices

**No mailbox (user accepted, overriding the earlier "flag on `DataPool`" answer).** With `Animation.Step` between `Main.Logic` and `Main.Apply`, the graph orders Main's writes into `Animations`/`Signals`/`Paints` exactly as it orders Animation's writes into `UIElements`. A mailbox's one-frame delay broke stop-then-set once animation writes rows in place: a `Stop` in `Main.Input` would reach Animation next frame, so this frame's step overwrote a value Main had just set, then stopped and left its own. Cost: a step after `Animation.Step` cannot write `Signals` — it is a loop (load error). Nothing does today. A mailbox comes back only if one ever must.

**Edges only where they do work (user).** An edge frees/compacts (`UIElements`, `Entities`), sorts (`UIElements`), or publishes a generation a consumer polls (`UIQuads`, `Gradients`, `Effects`, `Paints` — the renderer's mirrors). `Animations`, `Signals`, `Keyframes`, `AnimationValues` do none of these. An edge for every pool would add three stages, and the `Signals` edge (a writer `Animation.Step` must read after) would push Physics into a stage apart from Animation.

**Epoch once per frame, time summed (user, option A).** Every reader — `RenderSystem`'s start gate, the log stamp, `GpuEngineStats` — means "the system's frame"; per-step epochs would change the shader-visible stats layout.

**`System=` deleted from `Step` (user).** One way to name a step, the same `Action` mechanism `Bootstrap.xml` and `Shutdown.xml` use, category `Frame`.

**Main.Logic before Physics (user).** Entity ticks that move or push entities must reach physics in the same frame, not the next physics tick.

### Measured (2026-09-23, Debug, zones on, 24 logical cores, RTX, uncapped)
**Unoptimized Debug JIT** — [[profiling-unoptimized-jit]]. `--profile-scenario=animation`, per frame (mean), against step 1's table above.

| | 1k | 5k | 20k |
|---|---|---|---|
| `Anim.Step`, state hold | 0.49 | 0.91 | 3.55 ms |
| `Anim.Step`, margin hold (in-place writes) | 0.24 | 1.22 | 4.97 ms |
| Main steps + edges, state hold | 0.80 | 2.19 | 5.33 ms |
| Main steps + edges, margin hold | 2.06 | 10.47 | 38.68 ms |
| `ResolveLayout`, margin hold | 1.55 | 7.92 | 31.67 ms |
| `Main.Apply`, state / margin hold | 0.51 / 0.23 | 0.90 / 1.21 | 3.74 / 5.22 ms |
| teardown frame | — | — | 1,719 ms |

- **Values applied = tracks stepped on every frame at every size** (20,000 at 20k); step 1 applied at most ~1,024 a tick and dropped the rest, and its `MainTick` zone excluded the drain. So the state-hold Main row is not a regression of the same work: Main now applies every value (~0.19 µs each, setter path; ~0.26 µs, in-place path's `InvalidateLayout`).
- **Scheduler overhead** (frame − Main-run steps − barrier wait): mean 19.5 µs, p50 17.6, p99 39.2 — up from 4 / 14 µs. 7 stages instead of 1, per-frame `EndFrame` + pool stats, and a `SemaphoreSlim` release for each unpinned stage while workers are parked. Not tuned.
- **Barrier wait** p50 0.003 ms, p99 0.106 ms (was p99 2.8 ms) — Animation no longer overlaps the whole of Main.
- `Threads=1` + `MaxFps=120`, idle: 0.44 cores (step 1: 0.37).

### Known gaps
- **Scheduler overhead ~20 µs a frame** — see Measured; the unpinned lone stages (Animation ‖ Physics, the edges) wake parked workers every frame.
- **`Main.Apply` per value costs ~0.2–0.26 µs** (Debug JIT); at 20k that is 4–5 ms on Main. The per-value `Profiling.Zone.Increment` is one per frame since 2026-09-24.
- **22 capture batches lost at shutdown** — `Profiling.Flush` warns that parked workers never hand their last batch (seen before step 2 too).
- **Decision notes older than this one name `MainTick`** — read it as the Main steps above.
- **Verified:** builds clean; Thorium boots and prints the 7 stages; GUI — file-tree expand/collapse (in-place `Height`, `Collapsed` via `onDone`), a collapse interrupted by a re-expand ends at full heights, hover highlight, context menu slides open, palette switch crossfades (sidebar sampled 0→5→37→159→223→255 over ~300 ms, no early jump), Settings and Thorium close through their X; `Threads=1` + `MaxFps=120` same visuals; Carbon boots, opens a capture, closes; a loop, a duplicate action and an undeclared `Signals` write each fail as designed. **NOT verified:** the dirty-note prompt's cancel/confirm; dt after a title-bar drag; AuroraEditor not run (its generated schemas are stale until it is).

## Step 3 — `Jobs.For`, Animation's rows across workers (2026-09-24)

### What changed
- New `Threading.Jobs`: `IJobFor.Execute(start, end)`, `Jobs.For(count, rowBytes, kernel)`, `Jobs.Chunk(rowBytes)` (~16 KB of rows, whole 64-byte lines), `Jobs.InChunk`.
- Threads used = `min(chunk count, workers + 1)`; one chunk, `Threads=1`, or a second concurrent `For` runs every chunk inline on the caller.
- Chunk claim word (`chunks << 32 | next`) + `remaining` + `joined` in their own padded `Jobs` counters; `FrameScheduler.Work` tries `Jobs.Join`/`Help` before stage claims and does not park while chunks are claimable; `FrameScheduler.Wake`/`WorkerCount` added.
- A worker running a chunk borrows the caller's step (`FrameStep.SetCurrent`), so `AssertAccess` applies; helper time shows as zone `Jobs.Chunk` on its lane.
- DEBUG: `For` inside a chunk throws; `DataPool.AssertStructural` (`Allocate`, `Append`, `Free`, `Rewind`, `FrameEdge`) throws inside a chunk.
- `AnimationSystem` is its own `IJobFor`: chunks step drivers, write their own `Animations` rows and in-place targets, record a per-track outcome byte and per-chunk min/max/count (16 ints apart). `Anim.Emit` (serial) appends `AnimationValues` in track order in one block, retires finished tracks, marks the dirty range.
- `Profiling.Zone.Increment(name, amount)`; `Anim.Stepped` counted once a frame from the chunk sums.
- **One live track per property (pulled from [[animation-in-place-plan]] A8).** `Animations.Bind` stops a live track on the same `(target, C# property name)` (`byProperty`, cleared in `Release`); `AnimatableProperty.name` makes the XML and C# names one key. A replaced track's `onDone` does not run. `Animations.IsLive(handle)` is public; `ButtonControl.EnsureSpring` recreates its state spring once a tween or clip on `state` replaced it.

### Why these choices
**Chunk size from the row size, thread count from the chunk count (user, 2026-09-24).** A job with work for 6 threads takes 6; 1k tracks run on ~5 helpers, 20k on ~14.
**Retire and append stay serial.** Appends are not thread-safe; retiring in the same pass keeps value order and `done` identical to a serial step.
**Replace, not fight (user, 2026-09-24).** Two chunks writing one field make the winner vary by thread timing; before step 3 two tracks already fought each frame. Rejected: a DEBUG assert only; replacing tweens and clips but not springs (springs would still race); for the button, anything but recreating the spring (a kept handle to a replaced spring silenced hover for good).
**A second `For` while one runs goes inline, not queued.** One chunk slot keeps the claim to a single word; only Animation calls `For` today.

### Measured (2026-09-24, Debug, zones on, 16 logical cores, 14 workers, uncapped)
**Unoptimized Debug JIT** — [[profiling-unoptimized-jit]]; the optimized figures are § Step 4.0. `--profile-scenario=animation`, hold phases, mean ms.

| | 1k | 5k | 20k |
|---|---|---|---|
| `Anim.Step`, state hold (step 2: 0.49 / 0.91 / 3.55 on 24 cores) | 0.14 | 0.45 | 1.48 |
| `Anim.Step`, margin hold (step 2: 0.24 / 1.22 / 4.97) | 0.17 | 0.57 | 1.83 |
| of which `Anim.Emit` (serial), state / margin | 0.08 / 0.07 | 0.30 / 0.30 | 1.10 / 1.11 |
| helper lanes per frame, state hold | 4.9 | 9.0 | 13.8 |

- The parallel pass at 20k is 0.4–0.7 ms; the serial `AnimationValues` append is most of what is left. It goes away with `Main.Apply` ([[animation-in-place-plan]]).
- Appending one block instead of `Append` + `GetSpan` per row halved `Anim.Emit` (2.4 → 1.1 ms at 20k).
- **Determinism:** with `dt` fixed at 1/120 (scratch, reverted), a checksum over every `Animations` row every 200 frames matched on all 24 samples between `Threads=1` (0 workers) and auto (14 workers).

### Known gaps
- ~~`Anim.Step` at 20k is 1.4–1.8 ms, not under 1 ms~~ — **retracted 2026-09-24:** unoptimized Debug JIT; optimized it is 0.24–0.51 ms with 14 workers (§ Step 4.0).
- `MarkRangeDirty`/`MarkContentDirty` are not refused inside a chunk; they are not thread-safe.
- A second `For` in the same stage runs inline, unparallelised.
- `Control.RunClip` keeps held hover/press clip handles like the button did: a clip replaced by another track on the same property is never replayed (`Direct` on dead handles does nothing). No authored UI has both today (only `HoverClip="underline"`).
- The replace path itself is not exercised by the scenario or checked by hand.
- Not verified: scheduler overhead since step 3; the DEBUG asserts triggered on purpose; Thorium by hand (the scenario ran start to finish and exited normally).

## Step 4.0 — where the kernel's time goes, optimized (2026-09-24)
`-p:Optimize=true --no-incremental` (DEBUG asserts on), 16 logical cores, 20k, mean ms over each hold. 4.1–4.3 (column split, per-driver loops, SIMD) not started; the user has not chosen whether to go on.

| 20k | `Threads=1` step / emit | auto step / emit | kernel, auto |
|---|---|---|---|
| burst (tween) | 0.30 / 0.15 | 0.24 / 0.15 | 0.09 |
| state clip | 0.55 / 0.20 | 0.37 / 0.23 | 0.14 |
| margin clip (in-place writes) | 0.80 / 0.23 | 0.51 / 0.29 | 0.22 |
| spring | 0.50 / 0.19 | 0.38 / 0.24 | 0.14 |

- **Under 1 ms already, on one thread too.** The Debug tables above overstate everything ~5×.
- **Little left to vectorise:** the parallel kernel is 0.09–0.22 ms wall; a perfect 2× saves ~0.1 ms. Emit is the larger half and leaves with `Main.Apply`.
- `WriteTarget` (in-place writes) is ~0.14 ms of the `Threads=1` margin kernel (scratch run with it skipped, reverted); per-driver math is 15–20 ns a track.
- Per-track math is already 4-wide: `Vector4` maps to `Vector128`. Across-track SIMD would only reach the scalar parts — curve evaluation, spring exp/sin/cos.
- At 200k see [[animation-core]] § Measured at 200k.

Related: [[cross-system-change-notification]], [[ecs-rework-data-pools]], [[animation-core]], [[engine-profiling]], [[engine-logging]], [[settings-registry]], [[animation-in-place-plan]]
