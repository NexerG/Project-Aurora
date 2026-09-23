# Decision — profiling is zones that time and increments that count, compiled out by flag

**Date:** 2026-09-02
**Status:** LANDED, CPU only. Solution builds clean with no new warnings; behaviour verified by a
scratch console harness against the real types (below). **NOT verified inside a running Thorium** — no
boot, no GUI.
**Scope:** `ArctisAurora.Core.Diagnostics.Profiling`, `FrameSpool`, `ProfilingSettings`;
`ThreadedSystem.Loop`; `Engine.MainTick`; `RenderSystem.Tick`; `Bootstrap.bootstrap.xml`;
`Shutdown.shutdown.xml`.

## What was there before

Nothing named. Two things already measure and are **not** superseded:
`ThreadedSystem.LastTickMs` (per-thread wall time, feeds `deltaTime`) and `GpuEngineStats` (the same
three numbers plus frame index, published to every shader). Those are telemetry. This is profiling —
inside a tick, not around it.

## Decisions

### 1. Two flags, OR'd, and no project file changes

`[Conditional("DEBUG"), Conditional("PROFILE")]` on every entry point. Multiple `ConditionalAttribute`
apply as **OR**, so the profiler is live in Debug with nothing added to any `.csproj`, and `PROFILE`
stays the opt-in for anyone who wants it in another configuration. A build with neither symbol emits
neither the call **nor its arguments** — the zone-name literals are not in the assembly.

**Rejected: a `Profile` solution configuration** (Release + PROFILE). Correct for measuring real
numbers, but it costs a `.sln` edit plus every `.csproj`, and there is no ship configuration yet that
would need protecting from `DEBUG`.

**Rejected: a `static readonly bool` master switch** that the JIT folds. Genuinely free, but
`static readonly` has to settle in a static constructor — before `Settings.LoadAll`, which is
bootstrap step 1 — so its only honest source is an environment variable, and a folded constant can
never be un-folded for a UI toggle. `Profiling.enabled` is a plain `bool` instead: one predicted
branch, and a build carrying `PROFILE` is a build that intends to profile.

### 2. Zones time, increments only count

A zone is `Start`/`End` and reads the clock twice. An increment is a dictionary hit and no clock.
Roughly 50ns against 10-20ns, which is the whole point: **an increment can sit in a loop a zone
cannot.** "Layout took 3.2ms, and Measure ran 412 times inside it" is unaffordable if every sub-call
needs its own timer, and that shape of answer is what the glyph-count problem needs.

An increment attributes to the **innermost** open zone on the calling thread, which is what forces a
per-thread stack of open names rather than a single flag. With nothing open it attributes to `(root)`
rather than being dropped.

Counters key on `(zone, name)`, so `Measure` called from both `Layout` and `HitTest` reports two
numbers. Flat, not a tree — a counter never becomes a timed child on its own. Promoting one means
changing `Increment` to `Start`/`End` at that site (user, 2026-09-02).

### 3. Re-entering a zone deepens it, it does not restart it (user, 2026-09-02)

`Start` bumps `calls` always, but stamps `openStart` only as the zone's own depth goes 0→1; `End`
accumulates only as it returns to 0. One `int` per zone per thread, cheaper than a stack of open
stamps.

**Consequence to read carefully:** for a recursive zone `totalTicks` is the wall time of the
*outermost* span only, so `totalTicks / calls` is **not** mean-per-call, and min/max are over
outermost spans. Correct as inclusive time, meaningless as an average. Non-recursive zones are
unaffected. Verified: 4-deep recursion around one 30ms burn reports `x4` with a single 34ms span.

### 4. `End` takes the name, so a mismatch is caught where it happens

**Rejected: `End()` popping the stack**, which is shorter and structurally cannot mismatch — but then
a skipped `End` is invisible until the numbers look wrong. The named form lets a
`[Conditional("DEBUG")]` check compare against the stack top and warn, naming both zones. Verified:
`Zone.End("Wrong")` against an open `"Opened"` warns and names both.

**Rejected outright: `Zone.Increment(this)`** — a zone's own self-increment, from the user's first
sketch, which they flagged as suspect in the same breath. The ambiguity is fatal: with the loop
*outside* the zone, `Start` already counts every iteration and the self-increment doubles it; with
the loop *inside*, it is the only way to count. Same call, opposite meanings, and the API cannot tell
which. The entry count comes free from `Start` either way, so an inner loop takes a named counter.

### 5. No shared state, so no lock, no snapshot, no drain thread

`[ThreadStatic]` tables. `Report()` prints the **calling thread's own** tables and clears them, so
nothing ever reads another thread's dictionary. Main reports from `MainTick`, render from
`RenderSystem.Tick`, and the log shows a line per thread — which is what you want anyway, since main
and render are different budgets.

This is the one thing deliberately *unlike* [[engine-logging]]: no `LogLane` equivalent, no k-way
merge. A reporter iterating another thread's live `Dictionary` is not a torn number, it is a
corrupted resize, so the choice was a per-table lock, a published snapshot, or not crossing threads
at all. Not crossing is free and smaller.

**Amended 2026-09-02 by §8-§11.** A capture *does* cross threads, but as a **hand-off**, not shared
access: the recording thread fills a `CaptureBatch`, submits it, and never touches it again; the
spool writes it and returns it to that thread's free stack. Nothing is read concurrently, so the
lock is one acquire per batch (~64 frames) rather than one per zone edge. The rule the invariant
protects — *no thread reads another thread's live tables* — is intact.

**Rejected: `ConcurrentDictionary`** — an interlocked op per increment, on the path whose whole
justification is that it is cheaper than reading a clock.

### 6. Values are reached by ref, not read-modify-write

`CollectionsMarshal.GetValueRefOrAddDefault` / `GetValueRefOrNullRef` update the struct in the
dictionary's own storage. A `TryGetValue` → mutate → assign round trip would copy a 40-byte struct
twice per zone edge.

### 7. Reporting is a log line per zone, not a panel

`Log.Info` on a `"Profiling"` channel, gated by a self-owned 1000ms period so the merge work is
skipped rather than formatted and dropped. One line per zone keeps every line under `LogWriter`'s
1KB scratch, which a single combined line would overrun once there are counters.

Clearing at each report also bounds the leak in §4: a zone left open by a mismatched `End` is wiped
at the next report rather than being stuck out of the numbers forever. A zone genuinely open *across*
a report skips that report instead (`stackDepth != 0`), so a live stack is never cleared.

The UI panel is the roadmap's job — see [[ui-data-control-split]] for why the profiler is sequenced
before the UI rework and built on the UI it will measure.

**Amended 2026-09-02:** the report is now gated by `ProfilingReport.Enabled`, **default false**
(user, 2026-09-02 — asked for after reading the output). The gate wraps only the `Log.Info` calls;
the period still rolls and still clears, so §4's leak bound is unaffected. Note the consequence: it
gates the whole log call, so switching the report off takes it out of the file and the flight
recorder too, not just the console. A console-only gate needs per-channel sink levels in
`LogSpool`/`LogChannel`, which was not built.

## 8. A capture is a stream of spans, not per-frame totals (user, 2026-09-02)

`Profiling.Frame.Begin`/`End` bracket a tick; while a capture is live, every `Zone.Start` appends a
`SpanRecord` (name, begin, end, depth) and pushes its index, and `Zone.End` patches the end. Opens
happen in pre-order, so **the array is already in document order** and the writer emits nesting from
the `depth` field alone. XML is a tree and a zone stack is a tree, so the file *is* the flame chart —
the reader does no reconstruction.

**Rejected: per-frame aggregates** (each frame's zone totals, flat). ~40% smaller and enough for
"MainTick over time" graphs, but flat totals cannot be re-nested: no "AtlasBake ran inside Measure",
no parked tail, no sub-frame alignment against the render thread. The aggregate is derivable from the
stream at load time; the reverse is not.

**The stream is not cheaper than what was there.** An earlier claim that it would be assumed it
*replaced* the aggregate tables. It does not — the 1s report still needs them — so while capturing, a
zone edge costs the dictionary hit **plus** an array append, and an increment costs two dictionary
hits. Folding the aggregate from the span records at frame end would remove that, at ~25 lines and a
duplicated outermost-only rule; not worth it for ~3ns on ~30 edges a frame.

**Consequence:** the stream records *every* span instance, so a recursive zone nests visibly in the
file while §3's aggregate keeps its outermost-only rule. The two deliberately disagree.

## 9. Formatting happens on the spool thread, and batching alone would not have helped

The ask was "write frame data every few frames so it doesn't hog the process". Batching the writes is
only half: if the timed thread still built the XML text, the cost would have moved rather than gone.
So the recording thread only ever appends structs, and `FrameSpool` — a plain background thread, the
same shape as [[engine-logging]]'s drain — does every `XmlWriter` call and every file append.

That is also what lets a `SpanRecord` hold the **`string` reference** rather than an id: the hot path
never hashes a name, and the spool interns them (value equality, so a name built at runtime is still
correct) when it writes the `<Names>` table.

**Three batches per thread, and a starved pool drops frames rather than waiting.** Dropped frames are
counted and surface as `Dropped=` on the next batch that gets written. Two holes follow from the
no-cross-thread rule and are accepted: drops still pending when a capture ends are never reported,
and ~~a partial batch held by a thread at shutdown is lost (up to `FramesPerBatch - 1` frames)~~ —
**closed 2026-09-17**, `Profiling.Flush` now waits for it; see §16.

## 10. One file per thread, aligned by absolute timestamp

`Stopwatch.GetTimestamp` is process-wide, so `<F T="…">` alone lets a reader stack main and render on
one timeline. No correlation id, no shared frame counter, and the per-thread invariant survives.
`<F I>` is `ThreadedSystem.Epoch`, which is the frame number the rest of the engine already uses.

A session is a folder of `<thread>.frames.xml`, named for the moment it started (`-2`, `-3` … if two
captures land in the same second). Continuous mode past `MaxFileMB` **ends the session and starts a
new folder** rather than renaming files, so every folder is a self-contained capture; `Keep` prunes
the oldest. `Batch Seq` is assigned by the writer, so it is dense from zero **per file**, not
per-lane.

**A file killed mid-capture has no closing tag.** Deliberate: a reader is expected to tolerate
truncation, since the last partial batch of a crashed run is worthless anyway. A clean exit closes it
through `Profiling.Flush`, the `Commit` step before `Logging.Flush`.

## 11. Frame edges live in `ThreadedSystem.Loop`, not at the call sites (user, 2026-09-02)

**REVISED 2026-09-23:** only Dedicated threads keep `Loop`. `FrameScheduler.Run` brackets the main thread's frame (lane `Main`); a worker opens a `Worker N` frame on its first step of a frame; both use `FrameScheduler.Frame` as `<F I>` — [[frame-scheduler]].

`Frame.Begin` runs before `Drain()` and `Frame.End` after `Publish()`, and `Report()` moved there
from `Engine.MainTick` / `RenderSystem.Tick`. So `<F D>` is the real tick wall time — the same number
as `LastTickMs` — and the gap between it and the root zone's end is time the thread spent parked,
drawn for free. Physics gets instrumented at no extra cost.

**Rejected: leaving `ThreadedSystem` alone** and bracketing beside the old `Report()` calls. Smaller
diff, but then `D` is just the root zone repeated and the parked/unattributed gaps — the reason to
look at a frame file at all — disappear.

## 12. Allocation is a second axis on the zone, and "collected" is not there (user, 2026-09-03)

**Date:** 2026-09-03
**Scope:** `Profiling`, `FrameSpool`, `FrameCaptureReader`; `Carbon`'s `ZoneTableControl` and
`SpanChartControl`.

`GC.GetAllocatedBytesForCurrentThread()` is a thread-local read that allocates nothing, so it is
sampled at exactly the gates the clock already uses: at a zone edge under `outermost || capturing`,
and unconditionally at a frame edge. A zone carries `totalBytes` beside `totalTicks`, a span carries
its own bytes beside `B`/`E`, and a frame carries the thread's whole tick.

**Both levels, not one** (user, 2026-09-03). Frame-level alone says which *thread* makes garbage and
then you bisect by hand; the per-zone number is what makes it actionable. It cost one extra
thread-local read per zone edge, next to a `Stopwatch.GetTimestamp()` that costs ~4× more.

**Allocation obeys the same two rules time does**, deliberately: the aggregate follows §3's
outermost-only rule, the stream records every instance per §8, and both are **inclusive** — a
parent's bytes contain its children's. Verified exactly: `Outer` 300,048 = 100,000 + 200,000 + two
24-byte array headers, with `Inner` 200,024 nested inside it.

### Bytes collected are out (user, 2026-09-03)

Asked for as "allocated and collected", cut to allocated on the same day. The reason it is not a
matching pair: **the runtime does not expose bytes freed.** What was offered and not taken —

- **Collection counts** — `GC.CollectionCount(0/1/2)` deltas per frame. Exact, ~6 cheap FCalls.
- **Bytes reclaimed, derived** — `(heapBegin − heapEnd) + processAllocatedDuringFrame` from
  `GC.GetTotalMemory(false)` and `GC.GetTotalAllocatedBytes(false)`, written only on a frame where a
  collection actually ran. Approximate, and **process-wide**, so all three threads would report the
  same collections from their own frame windows — it does not sum across threads the way `A` does.
- **`GC.GetTotalPauseDuration()`** — one call, and the number that explains a frame spike.

**Rejected outright: `GC.GetGCMemoryInfo()`**, which is the only API giving real per-collection
detail. It **allocates** — a profiler that allocates to measure allocation corrupts its own
measurement.

### Consequences to hold on to

- **The frame window is the *end* of `Frame.Begin`→top of `Frame.End`** (user, 2026-09-03). Both
  origins are stamped after `Open` has picked the lane and batch, which is why `Begin`'s body is a
  separate method — it has three exits and all three must land on the same origin. This excludes the
  profiler at both ends: the `CaptureLane` and its three batches at one, `Report()`'s string
  interpolation at the other.
- **Frame 0 used to carry 285.6 KB on a thread that does nothing.** `PhysicsSystem.Tick` is empty and
  Carbon still totalled 292,464 B for the lane — `CaptureLane`'s ctor, measured exactly:
  `SpanRecord[2048]` 81,944 + `CounterRecord[512]` 12,312 + `FrameRecord[64]` 3,096 + object, ×3
  batches. 240 KB of it is span storage a zone-less thread never writes.
- **Growth during the tick is still counted, deliberately.** The first `Zone.Start` grows the report
  dictionaries (~304 B) and `AddSpan` doubles an overflowing span array (~160 KB, once per pooled
  batch). Excluding these needs a monotonic `overheadBytes` on `Tables`, an `openOverhead` in
  `ZoneData`, a field on `SpanRecord` (40→48 B, +20% on those 80 KB arrays) and extra
  `GetAllocatedBytesForCurrentThread()` reads on the zone hot path — rejected as too much machinery
  for one-off amounts. Steady state is exact regardless — 100 frames of a 100,000-byte allocation
  measured 10,002,400 twice running.
- **Per-thread means per-thread.** Work a zone hands to a pool thread does not appear in its bytes.
- `<F A>` is always written; `<Z A>` only when non-zero **and** the span closed, so a zone that
  allocates nothing stays a self-closing `<Z N B E />`. A capture file written before this reads
  back as zero rather than failing.

## 13. A boot capture has to be armed before the phase that would configure it (user, 2026-09-03)

**Date:** 2026-09-03
**Scope:** `Profiling` — `ArmBoot`, `Frame.Begin`, `Configure`; `ProfilingSettings` — `CaptureMode`;
`Bootstrapper.RunPhase`; `Engine.Init`.

Two ways to ask for one, because they cover different windows and only one can cover the boot itself.

### The setting cannot capture the phase that reads it

`Settings.LoadAll` is bootstrap step 1 and `Profiling.Configure` is step 3, so a session armed from
`ProfilingCapture.Mode` begins at the *next* frame — and the bootstrap phase is over before the first
frame exists. `CaptureMode.Boot` therefore buys what `Continuous` buys minus the never-ending part:
the first `BurstFrames` frames of every thread, from launch, persisted so a repeat run needs no
argument. Measured: three files, 60 frames each, and no `Bootstrap.frames.xml` in the folder.

`--profile` / `--profile=N` is read straight off `Environment.GetCommandLineArgs()` by
`Profiling.ArmBoot`, called from `Engine.Init` **before** `Bootstrapper.RunPhase` — the only point
early enough. It lives in the engine rather than each host's `Main`, so all three applications get it
without a line of their own. The flag beats the setting: `_bootArmed` short-circuits `Configure`'s own
arm, because re-arming mid-phase would close the boot frame's file and open a second session folder.

**Still out of reach either way: `XSDGenerator.GenerateXSD()`**, which every host runs in `Main`
before `Engine.Init`. Catching it needs the host to arm first, and no host does.

### The phase is one frame and a step is a zone

`Bootstrapper.RunPhase` brackets its step loop in `Frame.Begin(phaseName)` / `Frame.End()`, and each
`method.Invoke` in `Zone.Start(stepName)` / `End(stepName)`. Opens are flat and in order, so the file
is the boot's flame chart with nothing to reconstruct.

**Rejected: one frame per step.** `<F D>` would then be the same number `RunPhase` already logs, and
it throws away the only thing the frame view adds — every step on one timeline against one total.

### A thread that gains a system identity moves lane

The boot frame runs on the main thread before the frame loop starts, so `ThreadedSystem.Current` is
null and `Frame.Begin` took its `t{managedThreadId}` fallback. `Tables.lane` is built once and cached,
so **the main thread's real frames would then have landed in `t1.frames.xml`** and no `Main.frames.xml`
would have existed at all. `Frame.Begin` now compares the lane's name against the owner every frame
and, when they differ, hands the batch off as `last` and drops the lane. That is what closes the boot
frame's own file, and the check is general rather than a boot special case.

`Begin` grew `string? owner = null` for it — `ThreadedSystem.Loop` passes nothing, `RunPhase` passes
the phase name, which is what names `Bootstrap.frames.xml`. The `$"t{…}"` interpolation stays behind
two `??`, so an engine thread never allocates for it.

### Consequences to hold on to

- **`--profile=N` is N frames per thread, and the boot frame is one of main's.** Measured 1 + 119 on
  main against 120 each on render and physics. Left as is — the budget is honestly per-thread.
- **The boot lane is built with the default `FramesPerBatch`**, since `FrameSpool.Configure` has not
  run when it is created: three 64-frame batches for one frame, and the ~292KB lands in that frame's
  own `<F A>`, which is §12's warm-up rule doing exactly what it says.
- **`<F I>` is 1 on the boot frame**, not 0 — there is no `Epoch` to read, so it takes the
  `frameIndex + 1` fallback.
- **The bootstrap steps land in the main thread's report tables too**, so the first `Report()` period
  carries them mixed with a second of real ticks. Cleared at the next period, and only visible with
  `ProfilingReport.Enabled`.
- **A step that throws leaves its zone open** — `Zone.End` sits after `method.Invoke` with no
  `finally`. A boot step throwing is fatal anyway, and §7's clearing bounds it.

**Verified in a running Thorium, 2026-09-03** — the first profiler change that was. `--profile=120`
wrote `Bootstrap` / `Main` / `Physics` / `Render` frame files, all four closed cleanly by the burst
ending rather than by the process dying:

| Case | Result |
|---|---|
| header | `Thread="Bootstrap"`, `Mode="Boot"`, `Requested="120"` |
| the phase | one `<F I="1" D="7884058" A="255416888">` — 788.4ms, 243.6MB |
| steps | 25 `<Z>`, one per `Bootstrap.bootstrap.xml` step, in XML order |
| against the log | `phase 'Bootstrap' — 790ms, 25 steps`, the phase timer being the wider bracket |
| the lane split | `Bootstrap.frames.xml` 1 frame, `Main.frames.xml` 119, both closed |
| slowest step | `Renderer.Initialize` 253.6ms; `AssetRegistries.PreloadAssets` 154.9ms |
| largest allocator | `Context.LoadContexts` 91.5MB; `InputHandler.LoadInputs` 63.2MB |
| the setting alone | `Mode="Boot" BurstFrames="60"`, no flag → 3 files × 60 frames, no `Bootstrap` |

## 14. Data pools are a third record on the frame, pushed by the pool owner (user, 2026-09-14)

**Date:** 2026-09-14
**Scope:** `Profiling.Frame.Pool`, `Profiling.Configure`; `FrameSpool` — `PoolRecord`, `CaptureBatch.AddPool`,
`WriteFrame`; `ProfilingCaptureSetting.pools`; `FrameCaptureReader` — `CapturedPool`; `DataPool.ReservedBytes`;
`DataManager.FrameEdge`.

### What changed
- `DataManager.FrameEdge` calls `Profiling.Frame.Pool(name, Count, Capacity, ReservedBytes)` right after each
  `DataPool.FrameEdge()`. Recorded only while the calling thread is capturing and `FrameSpool.pools` is set.
- `<F>` gains one `<P N C K M/>` per pool, after its frame-level `<C>`: name id, live items, capacity, reserved
  bytes. `M`, not `B`, because `B` is a span's begin.
- `DataPool.ReservedBytes` = capacity × `_slotBytes`, fixed in the ctor: every column's `ElementSize` plus
  `_slots`, `_backMap`, `_versions`, `_publishedSlotVersion` (4 × int) and `_owners` (a pointer).
- Off by default. `<ProfilingCapture Pools="true"/>`, or `--profile-pools` on the command line, which forces it
  on over the setting (fork 1b).
- `CapturedFrame.firstPool`/`poolCount` slice `CapturedThread.pools`; truncation rolls pools back with the rest.
  An older file reads with no pools.

### Why these choices

**A record of its own, not counters.** A counter is summed per `(zone, name)` and Carbon sums it across frames;
a pool size is a gauge, and "UIElements 98 × 1315 frames" is nonsense. Naming counters `UIElements.Count`
would also have meant a `Set` beside `Increment` and a reader that tells the two apart by string.

**The owner pushes; Diagnostics does not read pools.** `Core.Data` already references `Core.Diagnostics` for
logging, and a pool may only be read on its owning thread. A profiler walking `DataManager.Pools` would invert
the layering and cross threads, which §5 exists to prevent. Pushed from the loop, it lands on the thread that
owns the pool, in that thread's frame, with no lock.

**Sampled after `FrameEdge`, not before.** Before it, freed rows are still alive until compaction; after it,
`Count` is what the pool carries into the next frame.

**Reserved bytes, and every capacity-sized array.** Every array is capacity-sized, so bytes are exactly linear
in capacity and used bytes are `M × C / K` with nothing lost. Columns alone would hide 24 B a slot, which is
40% of `Entities`. Not counted: array headers, the small side tables (`_freeIds`, `_pendingFree`, `_dirtyLog`),
the objects `_owners` points at, and GPU mirrors of a pool.

**One flag, not one for items and one for bytes.** They are one record at one cost; two switches would be
configurability with nothing to choose between.

**The switch sits in `Configure`, not `ArmBoot`.** `ArmBoot` reads the command line early because the boot
frame needs it; no pool exists until `DataManager.ParseXML`, and nothing calls `FrameEdge` before `MainTick`.
Read in `Configure`, it survives `FrameSpool.Configure` overwriting the flag from the setting.

**`CaptureBatch.pools` is sized from the flag.** `framesPerBatch × 4` with it on, zero with it off, growing on
overflow. A fixed 256-record array would add ~18 KB to frame 0's `<F A>` on every thread in every capture
(§12). With the flag on, every thread still pays it, pool-less `Render` and `Physics` included — the lane does
not know which thread will record pools. The boot lane is built before `Configure`, so it gets none — correct,
the boot frame has no pools.

**Every frame, not on change.** Counts change rarely, but a delta stream needs carry-forward in the reader and
a full write at every batch and session edge. Three pools cost ~120 bytes of XML a frame.

### Consequences to hold on to
- **Every pool is Main's today, so pools only ever appear in `Main.frames.xml`.** A pool owned by another system
  would be sampled on Main — but `DataManager.FrameEdge`'s flat loop already asserts on that (see `DataPool.FrameEdge`).
- **Measured slot sizes:** `UIControls` 260 B, `UIElements` 164 B, `Entities` 60 B, each carrying 24 B of
  bookkeeping.
- **Not in the one-second report.** Capture only.
- **The setting itself is not run-verified.** The switch path is; `Pools` is in the regenerated
  `SettingsTypeSchema.xsd`, and settings parse through the same path as `BurstFrames`.

### Verified

`Carbon.exe --profile=3000 --profile-pools`, clicking three session rows during the burst:

| Case | Result |
|---|---|
| records | 2999 Main frames, 8997 `<P>`; none in `Render`/`Physics` |
| names | `UIControls`, `UIElements`, `Entities` declared in `<Names>` and resolved |
| counts move | `UIElements` 98 (1315 frames) → 809 (623) → 1130 (666) → 1219 (395) |
| growth | capacity 1024 → 2048 at 1130 items, `M` 167,936 → 335,872 |
| steady state allocates nothing | 4 of 2999 `FrameEdge` spans carry `A`, all on a load frame |
| switch off | `--profile=60` → 59 Main frames, no `<P>` |
| an older capture | `Stage0-1000k-rearrange` loads in Carbon with no pools and no error |

## 15. A scenario drives the editor from inside the tick, under one capture (user, 2026-09-14)

**Date:** 2026-09-14
**Scope:** `ProfileScenario` — `Arm`, `Open`, `OnTick`; `Engine.Init`.

Typing and a window resize on a 1,000,000-character note, recorded into the same capture session.

### What changed
- `--profile-scenario` → `ProfileScenario.Arm()`, called from `Engine.Init` right after `Profiling.ArmBoot`. Moves
  the settings write root to a `ProfileScenario` folder beside the host's own, then posts the entity's creation
  to the first main tick.
- `ProfileScenario : Entity` counts main ticks in `OnTick`:

| Tick | Does |
|---|---|
| 2 | `Open`: 1,000 `BlockControl`s of 1,000 chars (one `Run` each) into a `RichTextDocument`; a bare `WindowRoot` holding one stretched `DocumentEditorControl` replaces `Engine.primary.ui.uiRoot`; caret at the end of block 0, `FocusCaret` |
| 30 | `Profiling.CaptureUntilFlush()` (was `Capture(240)` until 2026-09-17, §16) |
| 31–150 | one char into `InputHandler.charInputReadQueue`, then `TextInputActions.Write()`; zone `Scenario.Type` |
| 151–270 | `glfwSetWindowSize` on the primary window, 8 px narrower a tick for 60 ticks, then back; zone `Scenario.Resize` |
| 271 | `SettingsWindow.Open(Engine.primary)`, then `ShowCategory(keybindsCategory)` (added 2026-09-17) |
| 272–301 | settle — the settings window's GPU side is built on the render thread |
| 302–421 | `glfwSetWindowSize` on the settings window, 8 px wider a tick for 60 ticks, then back; zone `Scenario.ResizeSettings` |
| 422–1321 | hold — nothing scripted; room for an external real-pointer drag of the settings window's edge |
| 1322 | logs the final length and window size, `Engine.Post(Shutdown.Request)` |

- Captures and the log land where the host's normally do — both resolve against the write root's parent.

### Why these choices

**In the engine, not in Thorium (user, fork c).** `AGlfwWindow._glfw`, `RenderWindow.os` and the window handle
are internal, and the editor is the engine's. In Thorium it would have needed a public resize on `RenderWindow`.
It runs in any host with a primary window.

**An entity, not a self-reposting `Engine.Post`.** `DrainPosted` loops `TryDequeue` until the queue is empty, so an
action that re-posts itself never leaves the drain. An entity's `OnTick` runs once a tick in `Interpolate`, before
`ResolveLayout`, so an edit and the remeasure it causes land in the same frame.

**Its own tree, not a Thorium tab.** Only `LoadPath` replaces an editor's `DocumentEditSession`, so a generated
note loaded into an open tab would dirty the previous note and push into its undo stack. The bare editor has no
session, so the `Notes.*` shutdown steps find nothing.

**The write root moves because `Session.Capture` would wipe the vault's layout.** It skips a window with no
`WorkspaceControl` and assigns what it found wholesale — measured: `captured 0 window(s)` at shutdown.
**Beside the host's settings, not `%TEMP%` (user, 2026-09-14).** `FrameSpool.CaptureRoot` and the log resolve
against the write root's *parent*, so a `%TEMP%\AuroraProfileScenario` root put captures in `%TEMP%\Profiling`,
which Carbon's default `CaptureRoot` never lists. A host with no write root (the Editor) is not redirected.

**Typing goes through `Text.Write`, not `TypeChar`.** It runs the same `BeginStep` / `DeleteSelection` / `TypeChar`
/ `MarkDirty` a key press does. The char is queued after `ActivateKeybinds` and drained immediately, so the real
bind never sees it.

**Resizing is `glfwSetWindowSize`, which `WindowFrameControl` made on an edge drag until 2026-09-17** (now one
in-process Win32 `SetWindowPos` through `AGlfwWindow.SetBounds`). Not `SetWindowPos` from another process —
`aurora-verify`'s 65535 trap. The size callback fires inside the call, `WindowRoot.FitTo`
marks the root dirty, and that tick's `ResolveLayout` rewraps every block because `_wrapWidth` changed.

**The swap waits for tick 2 (user, 2026-09-14).** Swapped in `OnStart` (tick 1), the host's shell was destroyed
before its first layout. Its root was still in `UIEngine._dirtyRoots`, `ResolveLayout` laid the dead tree out, and
its lazily built children — `WindowFrameControl`'s 4 grips and 6 `ScrollThumbControl`s — were created under
destroyed parents: live, never destroyed, unreachable from `ElementOrder`. The result was `'UIElements'
resequence order count 1006 != live count 1016 — skipping` on every frame, so every captured `FrameEdge` skipped
its resequence. Found by walking `UIElements` owners against `ElementOrder`. **Rejected:** skipping destroyed
roots in `ResolveLayout` — it needs an `isDestroyed` accessor on `Entity` and is engine work outside this change.

**Load stays out of the capture.** The first full measure of the million characters lands on tick 2; the capture
opens 28 ticks later.

**1,000 paragraphs of 1,000 chars (user, fork a).** Typing rewraps one block, resizing rewraps all. **Rejected:** one
1M-char block (every keystroke rewraps everything) and 10,000 × 100 (measures control count more than wrapping).

**Constants, not settings, and no scenario format.** One scenario; nothing asked for knobs.

### Consequences to hold on to
- **Each run counts toward `ProfilingCapture.Keep`** and prunes the host's oldest capture, the same as F9.
- **No undo records.** The editor has no session, so no `TextEdit` is pushed; real typing pays for one.
- **Typing lands at the top of the note**, so `RequestScrollToCaret` never scrolls.
- **Every paragraph is identical**, so every block costs the same to wrap.
- **A maximized window is untested** — `glfwSetWindowSize` on one is not an ordinary resize.
- **The capture header says `Mode="Burst"`**, not the scenario's name.
- **Destroying a root before its first layout leaks its lazy children.** Engine gap, open in the WIP list.

### Verified

Three runs of the first shape (swap on tick 1, write root in `%TEMP%`), `Thorium.exe --profile-scenario`:

| Case | Result |
|---|---|
| exit | by itself, exit 0, both shutdown phases ran |
| document | `1000000 chars in 1000 blocks` → `1000120 chars` |
| window | 1280x720 at start and at end |
| capture | `Main.frames.xml` 240 frames: `I` 30–149 carry `Scenario.Type`, 150–269 `Scenario.Resize` |
| the remeasure | `ResolveLayout` avg 2.4 / 4.0 / 3.5 ms in typing frames against 86.5 / 89.0 / 85.9 ms in resize frames, max 121–152 ms |
| user settings | `%APPDATA%\Thorium\Settings` hash unchanged |

The final shape (swap on tick 2, write root beside the host's), one run:

| Case | Result |
|---|---|
| the leak | 0 `resequence order count` warnings, against 272 per run before |
| where it lands | session in `%APPDATA%\Thorium\Profiling`, scenario settings in `%APPDATA%\Thorium\ProfileScenario` |
| capture | 240 frames, `I` 30–149 `Scenario.Type`, 150–269 `Scenario.Resize` |
| the remeasure | `ResolveLayout` avg 2.85 ms typing, 87.59 ms resizing, max 144 ms |
| document, window, exit | 1,000,000 → 1,000,120 chars, 1280x720 restored, exit 0 |
| user settings | hash unchanged; the oldest capture was pruned (`Keep` 5) |

## 16. Shutdown waits for every thread's last batch, and the scenario captures to the end (user, 2026-09-17)

**Date:** 2026-09-17
**Scope:** `Profiling` — `Flush`, `CaptureUntilFlush`, `Frame.Release`, `Frame.Hand`, `Frame.Open`;
`ProfileScenario.OnTick`; `Renderer` — `Draw`, `RecreateSwapchain`, `CreateSwapchain`.

Asked for to find out why a window resize on an integrated GPU draws stretched frames.

### What changed
- **Zones in `Renderer.Draw`:** `Draw.Wait` (timeline `WaitSemaphores`), `Draw.Acquire`, `Draw.Update` (global
  buffers + module/compositor re-record), `Draw.Submit` (the one submit), `Draw.Present`. They nest under
  `RenderSystem.Tick`'s `Draw`.
- **Zones in `Renderer.RecreateSwapchain`:** `Swapchain.Recreate` over the method (closed before the minimized
  `return` too), children `Swapchain.WaitIdle`, `.DestroyOutputs`, `.Destroy`, `.Create`, `.ResizePerImage`
  (only when the image count changed), `.CreateOutputs`, `.Descriptors`. Both call sites (after acquire, after
  present) are covered by being inside the method. `.Destroy` splits into `.DestroyViews` and `.DestroyKHR`.
- **Zones in `Renderer.CreateSwapchain`**, nesting under `Swapchain.Create` on a rebuild and standing alone on a
  window's first creation: `Swapchain.QuerySupport` (`GetSupportDetails`), `.GetExtension`
  (`TryGetDeviceExtension`), `.CreateKHR`, `.GetImages` (both calls), `.ImageViews`.
- **Zones in `AVulkanHelper`:** `Swapchain.QueryCapabilities` (in `GetSupportDetails` and inline in
  `CreateSwapchain`), `.QueryFormats` (`GetSurfaceFormats`), `.QueryPresentModes` (`GetPresentModes`).
- **`Profiling.CaptureUntilFlush()`** — the `Continuous` branch of `Configure` as a call: `BeginSession("Burst",
  0)`, `_sessionFrames = -1`. `ProfileScenario` tick 30 calls it instead of `Capture(240)`.
- **`_heldBatches`** — `Interlocked` count of batches taken from a lane and not yet handed; incremented in
  `Frame.Open` after a successful `Take`, decremented in `Frame.Hand`.
- **`Frame.Release()`** — hands the calling thread's batch mid-frame and clears `capturing`, so the zones still
  open in that frame and `Frame.End` skip the batch.
- **`Profiling.Flush`** ends the session, `Frame.Release()`s the caller (main, inside the `Commit` step), then
  `SpinWait.SpinUntil(_heldBatches == 0, flushWaitMs)` before `FrameSpool.Flush`. `flushWaitMs` = 2000; a
  timeout logs `Warn` with the count left.

### Why these choices

**The scenario's fixed burst never reached the resize on the render thread.** `remaining` is per thread, so
`Capture(240)` gave render 240 of *its* frames — under 0.5 s of typing at ~5,000 fps (RTX) or ~490 fps (iGPU).
**Rejected:** `Capture(100_000)` — a magic number that has to outlast the fastest render thread, and it ends
through `Flush` the same way.

**An unbounded capture exposed §9's hole, so the hole got closed at `Flush` (user).** A thread hands a partial
batch only at its next `Frame.Open` after the session changes. `Flush` stopped the spool inside main's last
tick, so main's tail (192 of 241 frames kept) and — on the iGPU, whose resize render ticks take ~120 ms — the
render thread's whole resize half (0 rebuild ticks) never reached a file. **Rejected:** a scenario-local
`EndCapture` plus a delayed `Shutdown.Request` — scenario-only, and `Continuous` captures would still lose
their tail at every exit.

**The wait is a batch count, not a join.** Every thread keeps ticking through the `Commit` phase, so each
hands its batch at its next frame edge — within one tick. Joining the systems would have meant moving
`Profiling.Flush` out of `Shutdown.shutdown.xml` to after `Engine.Stop`, and after `Logging.Flush`. The
counter is the first shared state on the recording path (§5), but it moves once per batch, not per zone.

**Main hands its own batch rather than waiting for a frame edge it will never reach.** `Flush` runs inside
main's tick and main's loop exits after it.

### Consequences to hold on to
- **The shutdown tick is not recorded** on main — `Release` drops the frame in progress.
- **A thread that stops opening frames while holding a batch** (or `Profiling.enabled` flipped off) stalls
  shutdown for `flushWaitMs`, then its frames are lost with a `Warn`.
- **A thread that takes a batch just after the wait saw zero loses that frame.** It began after the capture
  ended.
- **`Flush` blocks main for up to one render tick** — 102 ms on the iGPU mid-rebuild, 28 ms on the RTX.
- **The iGPU's rebuild is two driver calls.** `vkCreateSwapchainKHR` 92 ms and `vkDestroySwapchainKHR` 20 ms on
  the AMD Radeon, against 0.7 / 0.6 ms on the RTX. `CreateSwapchain` passes `OldSwapchain = default` and
  `RecreateSwapchain` destroyed before creating, so no driver resource was reused. **Tested 2026-09-17:**
  handing the old swapchain over cut them to 13.6 / 0.11 ms — see [[render-window-owns-the-swapchain]] §8.
- **`VK_EXT_swapchain_maintenance1` / `VK_EXT_surface_maintenance1` (and the `KHR_` forms)** would allow an
  oversized swapchain presented one-to-one — no rebuild while dragging. The RTX driver exposes them; the AMD
  Radeon driver (26.6.1) does not.

### Verified

Builds: Debug, Release, Release with `DEBUG` — 0 errors. One `--profile-scenario` run per GPU, validation
off, the iGPU selected with `VK_LOADER_DRIVERS_SELECT=*amd*`:

| Case | Result |
|---|---|
| main | 241 frames (`I` 30–270), `Dropped` 0 — 192 before the wait |
| render | iGPU 921 frames with 12 resize rebuild ticks (0 before); RTX 10,875 |
| flush | 102 ms iGPU, 28 ms RTX; no `not handed` warning; every file closes |
| iGPU rebuild tick | `RenderTick` 117.9 ms: `Swapchain.Recreate` 116.2 — `Create` 93.0, `Destroy` 21.1, `CreateOutputs` 1.1, `WaitIdle` 0.16, `DestroyOutputs` 0.15; `Draw.Present` 1.6 |
| RTX rebuild tick | 6.3 ms: `Recreate` 5.3 — `Create` 3.3, `DestroyOutputs` 0.72, `Destroy` 0.63, `CreateOutputs` 0.39; `Draw.Present` 0.86 |
| iGPU typing tick | `Draw` 2.05 ms, of which `Draw.Wait` 1.85 |
| iGPU resize | every render tick is a rebuild — 0 plain ticks in the resize half |

Second pass with the `CreateSwapchain` and `.Destroy` children, same setup, one run per GPU:

| Case | Result |
|---|---|
| iGPU rebuild tick (12) | `Recreate` 113.0 ms — `Create` 92.3 (`CreateKHR` 92.2, `QuerySupport` 0.08, `GetExtension` 0.07, `GetImages` 0.02, `ImageViews` 0.004), `Destroy` 19.8 (`DestroyKHR` 19.8, `DestroyViews` 0.002) |
| RTX rebuild tick (219) | `Recreate` 5.1 ms — `Create` 3.2 (`QuerySupport` 2.4, `CreateKHR` 0.74, `GetExtension` 0.02), `Destroy` 0.61 (`DestroyKHR` 0.61) |
| children | add up to their parent within 0.03 ms on both |

Third pass — the query split, then each change of [[render-window-owns-the-swapchain]] §7–§8 on its own run:

| Case | iGPU (rebuild ticks) | RTX (rebuild ticks) |
|---|---|---|
| split | `QuerySupport` 0.09 ms (`QueryCapabilities` 0.04, `QueryFormats` 0.04, `QueryPresentModes` 0.001) | `QuerySupport` 2.52 ms (`QueryFormats` 2.52, `QueryCapabilities` 0.002, `QueryPresentModes` 0.001) |
| + format/mode cache | `QuerySupport` 0.04; `Recreate` 121.0 | `QuerySupport` 0.003; `Recreate` 5.43 → 2.88, `RenderTick` 4.1 |
| + old swapchain | `RenderTick` 123.1 → 17.2, `CreateKHR` 97.6 → 13.6, `DestroyKHR` 21.4 → 0.11; rebuilds in the resize 11 → 84 | `RenderTick` 4.1 → 3.8, `Recreate` 2.48, `DestroyKHR` 0.66 → 0.07, `CreateKHR` 0.94 → 1.07 |
| validation on | no new message; rebuilds 86 | no new message; rebuilds 219 |

## 17. The animation system gets a stress mode on the same scenario (user, 2026-09-19)

**Date:** 2026-09-19
**Scope:** `ProfileScenario` (`Arm`, `RunAnimation`, `BuildGrid`, `Ramp`, `StopHandles`, `Hold`); zones in `AnimationSystem.Tick`/`OnPost`, `MainSystem.OnPost`, `Animations.OnValue`/`Send`; engine `Animations/UI.anim.xml`.

### What changed
- `--profile-scenario=animation` — a mode on `ProfileScenario`, not a second class (user). Plain `--profile-scenario` is unchanged. `Arm` passes the mode to a private ctor; the mode runs as an `IEnumerator<int>` advanced once per `OnTick`.
- Zones: `Anim.Step` (track loop), `Anim.Fades` (`StepFades`) on Animation; `Anim.OnValue` around each applied value on Main. Counters: `Anim.Request` (Animation `OnPost`), `Anim.Stepped`, `Anim.ValuePosted`/`Anim.ValueRefused` (per track per tick), `Anim.ValueApplied` (past the stale check), `Anim.RequestDropped` (`Send` refused). Track total is the `Animations` pool's item count under `--profile-pools`, not a counter.
- Clips `profile-state` (`state` 0 → 2) and `profile-margin` (`Margin` left 0 → 4), 1 s `PingPong` `CubicInOut`, in the engine's `UI.anim.xml` (user: data over 30 s tweens).
- Timeline: settle 30 ticks → `CaptureUntilFlush` → one fade → per N in `{100, 1000, 5000, 20000}`:

| Stage | Zone | Does |
|---|---|---|
| fade (once) | `Scenario.FadeStart`, `Scenario.Fade` | `FadeSlots(0, 0, int.MaxValue)` — every paint slot fades to itself; 240-tick hold |
| build | `Scenario.Build` | `BuildGrid(N)`: vertical stack of horizontal rows of 100 `ButtonControl`s, 16 px; destroys the previous grid |
| a. burst | `Scenario.Burst`, `Scenario.BurstHold` | N `Tween`s on `state` in one tick; 240-tick hold |
| b. clip, no layout | `Scenario.StateRamp`, `Scenario.StateHold` | `Play("profile-state")` ramped; hold; stop |
| c. clip, layout | `Scenario.MarginRamp`, `Scenario.MarginHold` | `Play("profile-margin")` ramped; hold; stop |
| d. fan-out | `Scenario.SpringRamp`, `Scenario.SpringHold` | N springs on `state` following one `Signals.Create()`, flipped 0 ↔ 2 every 60 ticks |
| e. stop | `Scenario.StopAll` | `StopAll` on the first 1,000 buttons, 250 a tick |
| teardown | `Scenario.Teardown` | `BuildGrid(0)`; 30-tick hold |

- Ramps start ≤ 1,000 a tick and resume at the first refusal. Stops go through kept handles, 250 a tick, with no zone. After the last N: `Shutdown.Request`.

### Why these choices

**Steady stages ramp because the request lane holds 682, not 1,024.** A burst past that drops, and so does a steady stage started the same way. That leaves only ~682 tracks running, not N. Only stage a bursts, because measuring the drop is the point.

**`StopAll` is sampled, not run on every control.** It scans every binding, so running it on all N at 200k is ~4×10¹⁰ comparisons. A fixed sample of 1,000 still shows the cost per call growing with N.

**Stops go 250 a tick with no refusal check.** `Stop` releases on Main even when its request is dropped. The track keeps posting stale values that eat into the value lane. A quarter of the lane leaves room for Animation to tick once per four Main ticks.

**200k was in the ladder and came out (user).** Main ran at ~10 Hz there, so one run would take well over 5 minutes. It got through build, burst and the state clip, and part of the margin clip, before it was closed.

### Consequences to hold on to
- Ladder sizes are total controls in the tree. Every stage except the burst runs all N tracks, but at most ~1,024 values reach Main per tick. The measurement is both the thread cost and that cap.
- Zones only mark the stage on Main. The animation thread's frames are placed in a stage by timestamp.
- The capture is ~250 MB at `MaxFileMB` 64 and rolls across five session folders (`-2` … `-5`).

### Verified (2026-09-19)
- Builds clean. **Run to completion through 20k**, then 200k was stopped through `CloseMainWindow` → shutdown flushed. Read with a scratch C# script over the XML, not Carbon. Every new zone and counter is present. Results are in [[animation-core]] § Measured at scale.
- **NOT GUI-verified** — nobody watched the grid.

## Left standing

- **GPU is out entirely** (user, 2026-09-02). Frame times can be read back from a `VkQueryPool`, and
  when they are they land through a `Submit(name, startTicks, endTicks)` on the same tables — the end
  time arrives frames after the CPU-side span closed, which is also why the API is `Start`/`End` and
  not a `using` scope. Nothing is written for it.
- **`using (Zone.Scope())` was rejected** for now: `[Conditional]` does not apply to value-returning
  methods, so it would be JIT-eliminated rather than compiled out, and it cannot express a GPU zone.
  It is leak-proof by construction, which `Start`/`End` is not — the debug check in §4 is the
  substitute. ~15 lines to add if a branchy call site ever needs it.
- **Recursion is verified in a harness, not in the engine.** No shipped zone re-enters — the six in
  `MainTick` and the two in `RenderSystem.Tick` are all flat.
- **The first report period is skipped**, since the first `Report()` only anchors `periodStart`.
- ~~**GC/allocation counters are not here.**~~ **Allocation landed 2026-09-03** — see §12. Bytes
  *collected* are still out, deliberately.
- **A typo in a name silently creates a second entry** and strands the first. This is the real cost of
  string keys over a resolved handle; the §4 check makes it a first-frame failure.
- **No `.xsd` for the frame format**, and no `xsi:schemaLocation` in the header. **Still none after
  the reader landed** (user, 2026-09-02): `FrameCaptureReader` is hand-written and is the only
  consumer, so a schema would validate only files the engine itself wrote. See
  [[carbon-frame-viewer]] §"Left standing".
- ~~**No keybind entry** in any application's `InputMap.inputs.xml`.~~ **Bound 2026-09-02** —
  Thorium's `InputMap.inputs.xml` maps `F9` to `Profiling.Capture`, and it has now been pressed: two
  real 300-frame captures exist and read back correctly. **This is the profiler's first run in a
  real application.**
- **§11 is wrong about where the frame edges are.** `ThreadedSystem.Loop` carries `Frame.Begin`/`End`
  and `Report` — it does **not** carry a `FrameEdge` zone, which is one of `Engine.MainTick`'s. So
  "every system has them, physics included" is false for the *zone*: `Physics.frames.xml` holds 300
  frames and zero spans. The frame *edges* are shared, as claimed; the named zone is not. Found by
  reading a real capture in Carbon, 2026-09-02. Not yet corrected in the prose above or in
  `PROFILING.md`.
- **`Profiling.Flush` stops the spool for good** — it is a `Commit` step and nothing captures after
  shutdown. A `Capture` after a `Flush` in the same process would find a dead worker.
- **Nothing a host does before `Engine.Init` can be captured**, `XSDGenerator.GenerateXSD()` most of
  all. The earliest arm is `Profiling.ArmBoot` inside `Init`; see §13.
- ~~**The visualizer is not here.**~~ **Landed 2026-09-02 as `Carbon`** — a fourth application on the
  engine's own UI, with `FrameCaptureReader` beside `FrameSpool`. See [[carbon-frame-viewer]].

## Verified

Scratch console harness (outside the repo, referencing the real `AuroraEngine`):

| Case | Result |
|---|---|
| three 20ms spans | `Plain 61.48ms x3 (min 20.002 max 21.473)` |
| 4-deep recursion, one 30ms burn | `Recursive 34.40ms x4 (min 34.395 max 34.395) — Level 4` |
| nesting + attribution | `Outer 10.03ms x1 — A 3`, `Inner 10.01ms x1 — B 1` |
| `enabled = false` | no `Disabled` zone, no counter |
| mismatched `End` | `Zone.End("Wrong") closes "Opened"` |

One defect found and fixed by that run: a zone with calls but no completed span printed its
`long.MaxValue` min sentinel as `922337203685477.625ms`.

**Capture, second harness (2026-09-02), same arrangement.** Six frames of a fixed nesting
(`Tick > Layout > Text`, `Tick > Draw`, `Measure` ×3 in Layout, `Glyph` ×5 in Text), read back through
`XDocument`:

| Case | Result |
|---|---|
| header | `Thread`, `Frequency` = `Stopwatch.Frequency`, `Mode="Burst"`, `Requested="6"` |
| name table | 6 ids, every `N=` in the body resolves |
| frames | 6 `<F>`, each with `I`, `T`, `D` |
| nesting | one root `<Z>`; `Layout` and `Draw` its children; `Text` inside `Layout` |
| counter attribution | `Measure 3` on `Layout`, `Glyph 5` on `Text` — not merged, not on the root |
| span containment | `Text`'s `B`/`E` lie inside `Layout`'s |
| frame vs root span | `D` ≥ the root span's `E`, so the parked tail is present |
| span duration | the 2ms sleep measured 9.68ms — Windows timer granularity, not a profiler error |
| starved pool | 3200 ticks, 576 frames written, **2617 dropped and reported**, `Seq` dense from 0 |

Two things that run found: a lane-global `Batch Seq` (so a file started at an arbitrary number —
moved to the writer, now dense per file), and two captures in the same second writing the same
folder (now uniquified). The starved run also confirms the §9 hole — 7 of 3200 ticks are neither
written nor counted, being drops still pending when the capture ended.

**Allocation, third harness (2026-09-03), same arrangement.** Six captured frames of
`Outer(100KB) > Inner(200KB)` plus `Empty` and 50KB loose in the frame, read back through
`FrameCaptureReader`; then 2×100 uncaptured frames read out of the report tables by reflection:

| Case | Result |
|---|---|
| span, leaf | `Inner` exactly 200,024 — the array plus its 24-byte header |
| span, inclusive | `Outer` exactly 300,048, containing `Inner` |
| frame | exactly 350,072 = loose + outer + inner + 3 headers |
| a zone that allocates nothing | `<Z N="2" B="44444" E="44447" />` — no `A` written at all |
| `zone.totalBytes` over a period | exactly 10,002,400 for `Outer` ×100 |
| period frame bytes, first run | 10,002,704 — 304 over, the table growth in frame 0 |
| period frame bytes, second run | exactly 10,002,400, so the overage is one-time |

Related: [[engine-logging]], [[ui-data-control-split]], [[gpu-global-frame-data]],
[[ecs-rework-data-pools]], [[carbon-frame-viewer]]
