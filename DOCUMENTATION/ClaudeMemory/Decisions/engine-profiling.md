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
and a partial batch held by a thread at shutdown is lost (up to `FramesPerBatch - 1` frames).

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

- **The frame window is `Frame.Begin`→`Frame.End`**, which is what excludes `Report()`'s own string
  interpolation — the report runs *after* `Frame.End`, so switching it on does not inflate `A`.
- **Warm-up lands on the frame, not on a zone.** Frame 0 carries the profiler's own one-time table
  growth: 304 bytes for the report tables, ~292 KB more when a capture's three batches are built in
  `Frame.Begin`. Zones read identically in frame 0 and frame 5. Steady state is exact — 100 frames of
  a 100,000-byte allocation measured 10,002,400 twice running.
- **Per-thread means per-thread.** Work a zone hands to a pool thread does not appear in its bytes.
- `<F A>` is always written; `<Z A>` only when non-zero **and** the span closed, so a zone that
  allocates nothing stays a self-closing `<Z N B E />`. A capture file written before this reads
  back as zero rather than failing.

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
