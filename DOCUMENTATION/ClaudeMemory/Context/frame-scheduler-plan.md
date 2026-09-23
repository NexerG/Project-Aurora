# Frame scheduler — every system a step of one graph, every core used

**Status:** AGREED 2026-09-23 (design and step order). Step 1 landed 2026-09-23 — [[frame-scheduler]]. Steps 2–4 are open.
**Rule:** each step gets its own plan, approved before code (CLAUDE.md §2). This file is the brief for writing that plan, not a licence to skip it. Forks listed under a step are the user's to decide.
**Goal:** uncapped frame rates, capped only by setting — a Tarkov-class game at ~300 fps (3.3 ms) on the user's PC. Frames must not allocate, and the main-thread-only chain has to fit the budget by itself. A system slow on one core is optimised before threads are considered (`Threads=1` exists for that).

## Cold boot — where things stand after step 1
- **Read first:** [[frame-scheduler]] (what landed, measurements, gaps), then the vault page `Engine/Systems/THREADING.md`.
- **Code** (`ArctisAurora.Core.Threading`, paths via `NAMESPACES.md`): `FrameScheduler` (`Load`, `Wire`, `Place`, `Loop`, `Start`, `Run`, `RunStage`, `Claim`, `Work`, `Stop`), `FrameStep` (`reads`/`writes` column masks per pool id, `waits`, `Due`, `Run`, `Current`), `FrameGraph.cs` (XSD definitions), `ThreadingSettings`, `ThreadedSystem` (`Step`, `StartScheduled`, `WaitOut`, `Dedicated`, `Period`, lanes still present), `MainSystem`. `DataPool.AssertAccess` in `ArctisAurora.Core.Data`. `Engine.MainTick` is still one block.
- **Data:** `AuroraEngine/Data/XML/Documents/Frame.frame.xml` — Render dedicated; steps Main (pinned, writes `UIElements UIQuads Entities Gradients Effects`), Animation (writes `Paints Animations Signals Keyframes`), Physics. One stage.
- **Still on lanes:** `Animations.Send` → `ThreadedSystem.Post(animationSystem, requestKind, AnimationRequest)` (ops `Tween Spring Retarget Stop SetSignal FadeSlots Keyframes Direction`); `AnimationSystem.OnPost` applies requests; `AnimationSystem.Tick` posts `AnimationValue` per stepped track and `FadeSeeded` acks to Main; `MainSystem.OnPost` → `Animations.OnValue` / `Animations.OnFadeSeeded`. Values apply at Main's `Drain`, before input.

## Settled (user, 2026-09-23)
- A frame is ordered stages; steps in a stage run together; any free thread takes the next step; Dedicated systems keep a thread and core of their own, also under `Threads=1`.
- Each step lists the columns it reads and writes; the loader places steps into stages. No pool has an owner.
- Clashes are per column. A reader waits for every writer; writers of one column go in list order.
- A loop is an error. Data two systems need from each other goes through a mailbox.
- Steps of one class never overlap.
- Uncapped by default; `MaxFps` caps.
- No command lanes at the end. Bring them back only if a real need appears.
- Animation values are read directly by a later Main step instead of pushed (reverses the 2026-09-19 push choice — its reason, Main polling another thread's pool, is gone once a barrier orders them).

## Steps
- [x] 1. Scheduler, frame graph, per-column access check, settings, worker profiling lanes; each system one step; lanes unchanged (2026-09-23) — [[frame-scheduler]]
- [ ] 2. Mailbox pools, sub-steps, frame-edge steps, lanes deleted — below
- [ ] 3. `Jobs.For` — one step's rows split across workers; Animation first — below
- [ ] 4. SoA + SIMD for animation tracks — only if step 3's capture asks — below

## Step 2 — mailbox pools, sub-steps, no lanes

**Outcome:** Main and Animation run as several steps each; Animation's requests go through a mailbox pool; Main reads animation values straight from a pool after `Animation.Step`; every command lane is gone; the 682-start and ~1,024-value ceilings are gone.

**Mailbox pools**
- `Pools.pools.xml` `<Pool Mailbox="true">`; `PoolDefinition` gains the attribute; handle-less rows only (`Append`, `Rewind`).
- Writers append to this frame's box; readers see last frame's; `FrameScheduler.Run` swaps every mailbox after the last stage and empties the new write side. No size limit — both sides grow like any pool, never shrink.
- `FrameScheduler.Wire`: a read of a mailbox pool makes no wait. Writers of a mailbox still go in list order.
- First mailbox: `AnimationRequests` (column `AnimationRequest`), written by any Main step that calls `Animations.*`, read by `Animation.Step`.

**Sub-steps**
- A step names an action: `<Step Action="Main.Input" Pinned="true" Reads=… Writes=…/>`, the method tagged `[A_XSDActionDependency("Main.Input", "Frame")]` on a `ThreadedSystem` subclass; the system is the declaring class. Resolved the way `Bootstrapper.Load` resolves `Bootstrap` actions.
- The same-class rule goes live in `Wire`: after the data waits, two steps of one class that do not already wait on each other (directly or through others) get a wait, later-listed on earlier-listed. The reachability check stops it from inventing a loop.
- "Listed twice" becomes "the same Action twice".
- Proposed split of `Engine.MainTick` (confirm against the code at the time):

| Step | Pinned | Body today |
|---|---|---|
| `Main.Input` | yes | `MainSystem`'s dt, `PollEvents`, `ReapClosedWindows`, `DrainPosted`, `ActivateKeybinds`, `HandleUI` per window, `DragGhost.Follow`, `ContextMenus.Tick` |
| `Animation.Step` | no | request drain (from the mailbox), track step, value output, slot fades |
| `Physics.Step` | no | empty |
| `Main.Apply` | yes | `Animations.OnValue` over this frame's values, `OnFadeSeeded` |
| `Main.Logic` | yes | `EntityRegistry.ProcessStarts` / `ProcessDestroys` / `ProcessEnableChanges`, `OnTick` loop |
| `Main.Layout` | yes | `UIEngine.ResolveLayout` |
| frame edges | per pool | `DataManager.FrameEdge` today |
| `Main.DrawLists` | yes | `UIEngine.BuildDrawLists` |

- Order change to accept: input now runs before this frame's values land, and `Main.Apply` still runs before `ResolveLayout`, so an animated `Width` arranges the frame it lands. A request made in `Main.Input` reaches `Animation.Step` next frame — one frame, as today.

**Frame-edge steps**
- `<Edge Pool="UIElements" Pinned="true"/>` — writes every column of that pool; its list position orders it. Edges are exempt from the same-class rule (no hidden state), so edges of different pools run side by side.
- `UIElements`' edge is pinned: `SortAction="UI.ElementOrder"` walks the control tree. Keep today's order — every Main pool's edge before `Main.DrawLists` ("dense indices have settled" before the draw range is told) — and check `UIQuads` especially, since `BuildDrawLists` rewinds it.
- `DataManager.FrameEdge()` (per running step) goes away, and so do the calls in `Engine.MainTick` and at the end of `AnimationSystem.Tick`.

**Lanes deleted**
- `ArctisAurora.Core.Data.Commands`: `CommandLane`, `CommandArena`, `SystemCommand`, `CommandApplier`.
- `ThreadedSystem`: `_inbox`, `_outbox`, `BuildLanes`, `Post`, `OnPost`, `Drain`, `Publish`; `Step()` becomes the method call with `Current` set; `Engine.Init`'s `BuildLanes` call.
- `MainSystem.OnPost`, `AnimationSystem.OnPost`, `Animations.Send` (→ append to the mailbox), `AnimationSystem.requestKind/valueKind/fadeSeededKind`.
- `IPoolColumn.WriteBytes`/`FillBytes`/`CopyWithin` (only the applier used them). Keep `ElementSize` — `DataPool` sizes slots with it. Check `DataPool.DenseOf` and `ThreadedSystem.SystemId` for remaining users (`LogSpool` stamps `SystemId`).
- `LogLane`'s comment names `CommandLane` as its twin — rewrite it. The logger's lanes stay.

**Forks for the user**
1. **Mailbox storage:** a `Mailbox` flag on `DataPool` holding a second set of column arrays (write side via `Append`/`GetSpan`, read side via `Backing`/`CopyRange` plus a `ReadCount`) — recommended — or a separate type outside `DataPool`, which the graph could then not name.
2. **Values out of Animation:** an append-only `AnimationValues` pool (`AnimationValue` rows, rewound by `Animation.Step`, read by `Main.Apply` the same frame) — recommended, it is the post without the lane — or `Main.Apply` scanning the `Animations` dirty range with a per-row "changed" flag.
3. **`FadeSeeded` acks:** a row kind in `AnimationValues`, a small second pool, or a mailbox read next frame.
4. **Epoch and `LastTickMs` per system:** bump each graph system's epoch once at frame end and sum its steps' time into `LastTickMs` (recommended — `Renderer` feeds `mainTickMs`/`physicsTickMs` to `GpuEngineStats`, `RenderSystem.OnStart` waits for Main's first epoch, `LogSpool` stamps it) — or per step.
5. **`System` attribute:** retire it for `Action`, or keep `System` as "the whole `Tick`" alongside `Action`.

**Left out:** `Jobs.For` (step 3); render copy stage; physics substeps.

**Verification**
1. Build → one line, succeeded.
2. Startup log → the stages it prints match the table above, with no loop.
3. Thorium, GUI capture → hover/press bindings ease, `HoverClip` reverses on exit, theme crossfade runs.
4. `--profile-scenario=animation` → no `Anim.RequestDropped` at any size; values applied per frame equal tracks stepped (no ~1,024 cap); per-stage costs within noise of step 1's table in [[frame-scheduler]].
5. `Threads=1` → same visuals.
6. Scratch override of `Frame.frame.xml` → a loop still refuses to start, an undeclared write still throws.
7. `grep` → nothing names `CommandLane`, `Post`, `OnPost`, `BuildLanes`.
8. Docs → [[frame-scheduler]] (step 2 section), [[animation-core]] (push reversed), [[cross-system-change-notification]], WIP (the 682/1,024 items close), vault `THREADING.md` and `ANIMATION.md`.

## Step 3 — `Jobs.For`, one step's rows across workers

**Outcome:** a step can split a loop over rows across every idle thread, OpenMP `schedule(dynamic, chunk)` style; `Anim.Step` at 20k drops well under 1 ms on 24 cores.

- New `Core.Threading.Jobs`: `interface IJobFor { void Execute(int start, int end); }` and `Jobs.For(int count, int chunk, IJobFor kernel)`. The kernel is an object the system keeps — no allocation per call.
- Mechanism: a second claim word for chunks (`chunkCount << 32 | next`), the same trick as the stage word; `Jobs.For` publishes it and wakes parked workers; the caller claims chunks too and returns when every chunk is done. `FrameScheduler.Work` checks chunk claims before stage claims.
- A worker running a chunk sets `FrameStep.Current` to the caller's step, so `AssertAccess` still applies.
- DEBUG asserts: no nested `For`; no structural write (`Append`, `Allocate`, `Free`, `Rewind`, `FrameEdge`) inside a chunk — a `[ThreadStatic]` in-chunk flag checked by `AssertAccess`.
- `Threads=1`: `For` runs the whole range inline, so results are identical by construction.
- Cache rules: chunk boundaries rounded to whole 64-byte lines of the column written; per-chunk dirty min/max into a padded per-chunk array, merged after the join; no shared counter inside the loop; profiling counters summed per chunk.
- Animation: `AnimationSystem` keeps a step kernel over `Animations` rows; a chunk writes only its rows (`value`, `velocity`, `elapsed`, `sleeping`, `driver`) and marks rows to emit; a serial pass afterwards appends the `AnimationValues` rows (appends are not thread-safe) and retires finished tracks.
- **Forks:** the chunk size — fixed per call site, or derived from the element size by a helper; whether Main steps may call `For` (their bodies are tree-walking OO, so probably not yet).
- **Verification:** build; scenario → `Anim.Step` at 20k well under 1 ms, per-track values identical between `Threads=1` and auto (a scratch checksum over `Animations` after N frames, reverted); scheduler overhead still microseconds; Thorium visuals unchanged.

## Step 4 — SoA + SIMD for animation tracks (conditional)
- Only if step 3's capture still shows `Anim.Step` as a cost worth it.
- `AnimationTrack` is one wide struct, and the step branches per driver. Split hot fields into columns, keep a dense list per driver so the tween loop is branch-free, then evaluate curves with `Vector128`/`Vector256` over floats.
- Measure before and after on the same scenario; keep only if it wins.

## Later, not ordered
- **Render copy stage:** render reads only a copy made at the frame's end, and the graph never runs more than one frame ahead of render (today an uncapped graph can compute frames nobody sees).
- **Networking:** a Dedicated socket thread plus a graph step that drains it through a mailbox.
- **Per-step waits instead of stage barriers:** same read/write data, each step waits only for its own predecessors. Only if captures show time lost at barriers.
- **Physics substeps**, so physics keeps its rate below ~31 fps.
- **Timer resolution for the cap** (`timeBeginPeriod` on Windows) if `WaitOut` overshoots on another machine.
- **Carbon's frame strip** for many lanes — 24 labels stack on one another today.

## How to verify here (learned in step 1)
- Build: `dotnet build AuroraEngine/ArctisAurora.sln 2>&1 | grep -E "error|Build succeeded|Build FAILED"`.
- Run Thorium from its output folder — `Thorium/bin/Debug/net10.0-windows10.0.22621.0/Thorium.exe` with that folder as the working directory; the startup log prints the stages and worker count. `aurora-verify` has capture and input.
- Close through the window's own X (client ~1375,15 on a 1399-wide window) to run the real shutdown; `CloseMainWindow` does not close Thorium.
- Settings to try a mode: `%APPDATA%\Thorium\Settings\UserSettings.settings.xml`, add `<Threading><Threads Count="1"/><FrameCap MaxFps="120"/></Threading>`. Back it up first and restore it byte-for-byte after.
- Graph override without touching the repo: in Debug the app's `Data` is mounted before the engine's, so `Thorium/Data/XML/Documents/Frame.frame.xml` overrides the engine copy. Delete it after.
- Scenario captures without pruning the user's: `%APPDATA%\Thorium\ProfileScenario\UserSettings.settings.xml` gets `<Profiling><ProfilingCapture Directory="<scratch>\captures" Keep="50"/></Profiling>`; back up, run `Thorium.exe --profile-scenario=animation`, restore byte-for-byte. The log's `animation scenario — N buttons` lines give each size's starting frame index (`Main:<epoch>` = `<F I>`).
- Parsing captures: a scratch console with `XmlReader` over every `*.frames.xml` — `<N I V>` names, `<F I T D>` frames, nested `<Z N B E A>` zones in ticks at the file's `Frequency`, `<C N V>` counters inside a zone. The step-1 parser was scratch and is gone.
- Carbon: point `<Carbon><CaptureRoot Path="…"/></Carbon>` in `%APPDATA%\Carbon\Settings\UserSettings.settings.xml` at the captures (back up, restore).

Related: [[frame-scheduler]], [[animation-core]], [[cross-system-change-notification]], [[engine-profiling]], [[ecs-rework-data-pools]]
