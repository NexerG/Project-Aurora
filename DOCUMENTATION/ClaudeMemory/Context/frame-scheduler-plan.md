# Frame scheduler — every system a step of one graph, every core used

**Status:** AGREED 2026-09-23 (design and step order). Steps 1 and 2 landed 2026-09-23, step 3 on 2026-09-24 — [[frame-scheduler]]. Step 4: 4.0 measured, 4.1–4.3 undecided.
**Rule:** each step gets its own plan, approved before code (CLAUDE.md §2). This file is the brief for writing that plan, not a licence to skip it. Forks listed under a step are the user's to decide.
**Goal:** uncapped frame rates, capped only by setting — a Tarkov-class game at ~300 fps (3.3 ms) on the user's PC. Frames must not allocate, and the main-thread-only chain has to fit the budget by itself. A system slow on one core is optimised before threads are considered (`Threads=1` exists for that).

## Cold boot — where things stand after step 2
- **Read first:** [[frame-scheduler]] (step 1 and § Step 2 — what landed, measurements, gaps), [[animation-core]] § Step 2 of the frame scheduler, then the vault page `Engine/Systems/THREADING.md`.
- **Code** (`ArctisAurora.Core.Threading`, paths via `NAMESPACES.md`): `FrameScheduler` (`Load`, `FindActions`, `Build`, `Access`, `Wire`, `Reaches`, `Place`, `Start`, `Run`, `RunStage`, `Claim`, `Work`, `Stop`), `FrameStep` (`Name`, `System` — null for an edge, `reads`/`writes` column masks per pool id, `waits`, `Due`, `Run`, `Current`), `FrameGraph.cs` (XSD definitions), `ThreadingSettings`, `ThreadedSystem` (`RunStep`, `EndFrame`, `StartScheduled`, `WaitOut`, `Dedicated`, `Period`; `Tick` only for Dedicated), `MainSystem` (actions `Input Logic Apply Layout DrawLists`), `PhysicsSystem.Simulate`, `AnimationSystem.Advance`. `DataPool.AssertAccess`, `DataPool.ElementBytes` in `ArctisAurora.Core.Data`.
- **Data:** `AuroraEngine/Data/XML/Documents/Frame.frame.xml` — Render dedicated; 7 stages: `Main.Input` → `Main.Logic` → `Animation.Step` ‖ `Physics.Step` → `Edge.UIElements` → `Main.Layout` → edges of `UIQuads Entities Gradients Effects Paints` → `Main.DrawLists`.
- **No lanes.** Main writes animation requests into `Animations`/`Signals`/`Paints` directly; `Animation.Step` writes pool-stored targets in place and appends `LayoutDirty` (drained by `Main.Layout`) and `AnimationDone` (drained by the next `Main.Logic`). `Main.Apply` deleted 2026-09-26 — [[animation-in-place-plan]].

## Settled (user, 2026-09-23)
- A frame is ordered stages; steps in a stage run together; any free thread takes the next step; Dedicated systems keep a thread and core of their own, also under `Threads=1`.
- Each step lists the columns it reads and writes; the loader places steps into stages. No pool has an owner.
- Clashes are per column. A reader waits for every writer; writers of one column go in list order.
- A loop is an error. Data two systems need from each other goes through a mailbox — none exists: step 2 found the graph's ordering of direct writes enough (a mailbox's one-frame delay broke stop-then-set). Build one only when a loop really needs breaking.
- Steps of one system never overlap.
- Uncapped by default; `MaxFps` caps.
- No command lanes. Bring them back only if a real need appears.
- Animation values are read by a later Main step, not pushed.
- Entity logic runs before physics, so an `OnTick` that moves or pushes an entity reaches physics the same frame.
- Step 4 runs regardless of what step 3's capture shows.

## Steps
- [x] 1. Scheduler, frame graph, per-column access check, settings, worker profiling lanes; each system one step; lanes unchanged (2026-09-23) — [[frame-scheduler]]
- [x] 2. Sub-steps, frame-edge steps, lanes deleted, animation writes in place, no mailbox (2026-09-23) — [[frame-scheduler]] § Step 2
- [x] 3. `Jobs.For` — one step's rows split across workers; Animation first (2026-09-24) — [[frame-scheduler]] § Step 3
- [ ] 4. SoA + SIMD for animation tracks — below; 4.0 measured (2026-09-24), 4.1–4.3 undecided

## Step 2 — landed 2026-09-23
See [[frame-scheduler]] § Step 2. Departures from the brief that stood here, each agreed before code: no mailbox pools (direct writes, above); `<Step Edge="Pool"/>` instead of an `<Edge>` element (the generated schema is an `xs:sequence`); edges only on the six pools whose edge does work, pool stats reported at frame end; `Main.Logic` before `Physics.Step`; animation values written in place for pool-stored properties, `FadeSeeded` gone because Main seeds fades itself.

## Step 3 — `Jobs.For`, one step's rows across workers

**Outcome:** a step can split a loop over rows across idle threads, OpenMP `schedule(dynamic, chunk)` style; `Anim.Step` at 20k (3.5–5 ms after step 2) drops well under 1 ms on 24 cores.

- New `Core.Threading.Jobs`: `interface IJobFor { void Execute(int start, int end); }` and `Jobs.For(int count, int chunk, IJobFor kernel)`. The kernel is an object the system keeps — no allocation per call.
- Mechanism: a second claim word for chunks (`chunkCount << 32 | next`), the same trick as the stage word; `Jobs.For` publishes it and wakes parked workers; the caller claims chunks too and returns when every chunk is done. `FrameScheduler.Work` checks chunk claims before stage claims.
- **A job does not have to take every thread (user, 2026-09-23).** Not every job has work for every thread, and chunks sized for cache locality may do best on ~4. `For` takes a thread cap (or derives one from count and chunk); the threads it does not take keep claiming other steps.
- A worker running a chunk sets `FrameStep.Current` to the caller's step, so `AssertAccess` still applies.
- DEBUG asserts: no nested `For`; no structural write (`Append`, `Allocate`, `Free`, `Rewind`, `FrameEdge`) inside a chunk — a `[ThreadStatic]` in-chunk flag checked by `AssertAccess`.
- `Threads=1`: `For` runs the whole range inline, so results are identical by construction.
- Cache rules: chunk boundaries rounded to whole 64-byte lines of the column written; per-chunk dirty min/max into a padded per-chunk array, merged after the join; no shared counter inside the loop; profiling counters summed per chunk.
- Animation: `AnimationSystem` keeps a step kernel over `Animations` rows; a chunk writes only its rows (`value`, `velocity`, `elapsed`, `sleeping`, `driver`) and its tracks' in-place targets (distinct rows — two tracks on one field of one row is the one hazard to rule out), and marks rows to emit; a serial pass afterwards appends the `AnimationValues` rows (appends are not thread-safe) and retires finished tracks.
- **Settled (user, 2026-09-24):** chunk size derived from the element size by a helper; thread count derived from the chunk count — a job with work for 6 threads takes 6; Main steps do not call `For`; `Main.Apply` is not parallelised but deleted, as a separate rework → [[animation-in-place-plan]].
- **Verification:** build; scenario → `Anim.Step` at 20k well under 1 ms, per-track values identical between `Threads=1` and auto (a scratch checksum over `Animations` after N frames, reverted); scheduler overhead still microseconds; Thorium visuals unchanged.

## Step 4 — SoA + SIMD for animation tracks
- Runs regardless of step 3's capture (user, 2026-09-23).
- `AnimationTrack` is one wide struct (now also carrying the in-place target), and the step branches per driver. Split hot fields into columns, keep a dense list per driver so the tween loop is branch-free, then evaluate curves with `Vector128`/`Vector256` over floats.
- Measure before and after on the same scenario.
- **Plan agreed 2026-09-24** (user: per-chunk index lists, tolerance checksum): 4.0 measure → 4.1 split `AnimationTrack` into columns (`TrackHead`, `TrackMotion`, `TrackGoal`, `TrackTime`, `TrackClip`, `TrackTarget`) → 4.2 per-chunk `stackalloc` index lists per driver → 4.3 SIMD across tracks where 4.0 says it pays (springs via `Vector128/256.Exp/Sin/Cos`, tweens grouped by `EaseKind`).
- **4.0 result (2026-09-24): stopped at the gate.** Optimized, `Anim.Step` at 20k is already 0.24–0.51 ms; the parallel kernel is 0.09–0.22 ms wall, so 4.1–4.3 would save ~0.1 ms. The user has not chosen between stopping, 4.1+4.2 only, or all of it → [[frame-scheduler]] § Step 4.0.

## Later, not ordered
- **Generated entity steps (user, 2026-09-23).** A source generator reads the game's entity types and logic, follows each tick method and what it calls, and records which pools and columns it reads and writes. It emits frame steps with those reads and writes, and the scheduler places them exactly like authored steps — by the columns they touch, not by list position — so entity logic that does not clash runs in parallel with the rest of the logic instead of queuing in `Main.Logic`. Whatever the generator cannot prove stays in `Main.Logic`: GLFW or OS calls (pinned), reflection, virtual calls into code it cannot see. The DEBUG access check validates the generated reads and writes at runtime, like authored ones. **Open:** what counts as one step (an entity type, a component, a method); whether stages are rebuilt at runtime as entity types appear and disappear; how a main-thread-only call is detected. **Flag:** needs `Microsoft.CodeAnalysis.CSharp` (a new NuGet dependency) in a separate analyzer project.
- **Independent work runs as soon as its own inputs are ready (user, 2026-09-23).** Two workloads that do not depend on each other run in parallel, whatever stage they would land in: `Edge.UIElements` only has to follow the UI steps that write it (`Main.Layout`), so it can run beside everything else and finish off, instead of waiting at a stage barrier. Same read/write data, each step waits only for its own predecessors — the stage barrier becomes per-step waits.
- **Render copy stage:** render reads only a copy made at the frame's end, and the graph never runs more than one frame ahead of render (today an uncapped graph can compute frames nobody sees).
- **Networking:** a Dedicated socket thread plus a graph step that drains it through a mailbox.
- **Physics substeps**, so physics keeps its rate below ~31 fps.
- **Timer resolution for the cap** (`timeBeginPeriod` on Windows) if `WaitOut` overshoots on another machine.
- **Carbon's frame strip** for many lanes — 24 labels stack on one another today.
- **Scheduler overhead** — ~20 µs a frame after step 2 (7 stages; a `SemaphoreSlim` release per unpinned stage while workers are parked). Per-step waits above would also cut it.

## How to verify here (learned in steps 1–2)
- **Timings: `dotnet build … -p:Optimize=true --no-incremental`**, then rebuild plain Debug with `--no-incremental` afterwards — Debug JIT is unoptimized and ~5× slow, and an incremental build ignores the property change → [[profiling-unoptimized-jit]].
- Build: `dotnet build AuroraEngine/ArctisAurora.sln 2>&1 | grep -E "error|Build succeeded|Build FAILED"`.
- Run Thorium from its output folder — `Thorium/bin/Debug/net10.0-windows10.0.22621.0/Thorium.exe` with that folder as the working directory; the startup log prints the stages and worker count. `aurora-verify` has capture and input.
- Close through the window's own X (client ~1375,15 on a 1399-wide window) to run the real shutdown; `CloseMainWindow` does not close Thorium.
- Settings to try a mode: `%APPDATA%\Thorium\Settings\UserSettings.settings.xml`, add `<Threading><Threads Count="1"/><FrameCap MaxFps="120"/></Threading>`. Back it up first and restore it byte-for-byte after.
- Graph override without touching the repo: in Debug the app's `Data` is mounted before the engine's, so `Thorium/Data/XML/Documents/Frame.frame.xml` overrides the engine copy. Delete it after.
- Scenario captures without pruning the user's: `%APPDATA%\Thorium\ProfileScenario\UserSettings.settings.xml` gets `<Profiling><ProfilingCapture Directory="<scratch>\captures" Keep="50"/></Profiling>`; back up, run `Thorium.exe --profile-scenario=animation`, restore byte-for-byte. The log's `animation scenario — N buttons` lines give each size's starting frame index (`Main:<epoch>` = `<F I>`).
- Parsing captures: a script over every `*.frames.xml` except `Render` — `<N I V>` names, `<F I T D>` frames, nested `<Z N B E A>` zones in ticks at the file's `Frequency`, `<C N V>` counters inside a zone. Key frames by `<F I>` across lanes (`Anim.Step` runs on a worker, or on main inside `Scheduler.Barrier`), and a frame's phase by the `Scenario.*` zone on Main. The step-2 parser was scratch Python and is gone.
- Carbon: point `<Carbon><CaptureRoot Path="…"/></Carbon>` in `%APPDATA%\Carbon\Settings\UserSettings.settings.xml` at the captures (back up, restore).
- **A launched Thorium inherits the launching shell's pipes.** `Start-Process` with redirected output inside a command piped to `grep` keeps the pipe open until Thorium exits — the command hangs. Redirect `< /dev/null` and do not pipe a command that leaves Thorium running.
- **Settings and menus open their own OS windows**, and `MainWindowHandle` can switch to them. Target a window by handle (EnumWindows over the process) for both input and capture; `capture.ps1` only takes the main window, and making it topmost covers the popup.
- A 0.3 s crossfade is too short to catch from a separate capture process: click and `CopyFromScreen` at fixed delays inside one script, and read a pixel per sample.

Related: [[frame-scheduler]], [[animation-core]], [[cross-system-change-notification]], [[engine-profiling]], [[ecs-rework-data-pools]]
