---
date: 2026-09-02
tags:
  - d_System
cssclasses:
  - Aurora.css
Status: Current
Linker:
  - "[[Arctis Aurora]]"
System:
  - "[[PROFILING]]"
Dependencies:
  - "[[LOGGING]]"
Implementors:
  - "[[PROFILING]]"
Namespace: ArctisAurora.Core.Diagnostics
SourceFiles: AuroraEngine/Core/Diagnostics/Profiling.cs, AuroraEngine/Core/Diagnostics/FrameSpool.cs, AuroraEngine/Core/Diagnostics/ProfilingSettings.cs, AuroraEngine/Core/Bootstrapper.cs
VerifiedAgainst: 2026-09-03
---
## Overview

Profiling answers "where did this tick go", inside a tick rather than around it. Two things already measure a tick from the outside and are not replaced by this: `ThreadedSystem.LastTickMs` is each thread's wall time and feeds `deltaTime`, and `GpuEngineStats` publishes those same three numbers to every shader. This is the layer beneath them.

There are two primitives. A **zone** is a timed span, opened and closed by name. An **increment** is a bare counter that attributes itself to whichever zone is currently open, and never reads the clock. That split is the design: a zone costs two `Stopwatch.GetTimestamp()` reads, an increment costs a dictionary hit, so an increment can sit inside a loop that a zone would drown.

Every entry point is compiled out unless `DEBUG` or `PROFILE` is defined, so a build with neither carries neither the calls nor the zone-name strings.

There are two ways to read what it collects. The **report** is a log line per zone, once a second, summarising the period — off by default. A **capture** is the other end of the scale: every span of every frame, in order and nested, written to an XML file for something else to draw.

## Architecture

```mermaid
graph TD
  Call["Profiling.Zone.Start(&quot;UI.Layout&quot;)"] --> Flag{DEBUG or PROFILE?}
  Flag -->|neither| Gone[no IL emitted at all]
  Flag -->|either| Enabled{Profiling.enabled?}
  Enabled -->|no| Ret[return]
  Enabled -->|yes| Stack[push name on this thread's zone stack]
  Stack --> Zones["zones[name]: calls++, stamp start if depth 0 to 1"]
  Inc["Zone.Increment(&quot;Measure&quot;)"] --> Counters["counters[(innermost zone, name)]++"]
  Zones --> Report["Report(): print this thread's tables, clear"]
  Counters --> Report
  Report --> Log[LogChannel &quot;Profiling&quot;]
  Stack --> Capture{capturing?}
  Capture -->|yes| Span["append SpanRecord, push its index"]
  Span --> Batch["Frame.End(): batch full or burst over?"]
  Batch -->|hand over| Spool[FrameSpool thread: XML, one file per thread]
  Batch -->|no batch free| Drop["drop the frame, count it"]
```

### Using it
A zone wraps the span you want timed, and increments inside it count whatever ran within.

```csharp
Profiling.Zone.Start("UI.Layout");
    Profiling.Zone.Increment("Measure");
    Profiling.Zone.Increment("Arrange");
Profiling.Zone.End("UI.Layout");
```

Zones nest, and an increment always attributes to the innermost one that is open on the calling thread. An increment with no zone open attributes to `(root)` rather than being dropped. Counters are keyed by zone as well as by name, so `Measure` counted under `Layout` and under `HitTest` reports as two separate numbers.

`Report()` prints the calling thread's tables and clears them, at most once a second. It runs at the end of every `ThreadedSystem` tick, so each thread reports its own line and no thread ever reads another's tables. The printing is gated by `ProfilingReport.Enabled` and is **off by default**; the period still rolls and still clears when it is off, so nothing accumulates forever. The gate covers the whole log call, so switching the report off takes it out of the log file and the flight recorder as well as the console.

### What is instrumented today
`MainTick` carries `MainTick`, `PollEvents`, `ActivateKeybinds`, `HandleUI` (with a `Window` counter), `Interpolate` and `FrameEdge`. `RenderSystem.Tick` carries `RenderTick` and `Draw`. The frame edges themselves are in `ThreadedSystem.Loop`, so every system has them, physics included.

Outside the tick loop, `Bootstrapper.RunPhase` carries a zone named for every bootstrap step and brackets the whole phase in a frame of its own.

### Re-entering a zone
A zone entered again while already open does not start a second timer. It deepens the one already running.

```
Zone.Start(name):
	push name on the thread's stack
	zone = zones[name]
	zone.calls += 1
	if zone.open == 0
		zone.openStart = Stopwatch.GetTimestamp()
	zone.open += 1

Zone.End(name):
	check name against the top of the stack, warn if it differs (DEBUG only)
	pop the stack
	zone = zones[name]
	zone.open -= 1
	if zone.open == 0
		elapsed = Stopwatch.GetTimestamp() - zone.openStart
		zone.totalTicks += elapsed
		fold elapsed into zone.minTicks and zone.maxTicks
```

The consequence is worth holding on to: for a recursive zone the accumulated time is the wall time of the **outermost** span only, so dividing it by the call count is not a mean per call, and min/max are over outermost spans. It is correct as inclusive time and misleading as an average. Zones that never re-enter are unaffected.

### Why the flags are two
`[Conditional("DEBUG"), Conditional("PROFILE")]` applies as an OR, so the profiler is live in a normal Debug build with nothing added to any project file, and `PROFILE` remains available to switch it on in any other configuration. Because `[Conditional]` removes the whole call expression, the string arguments are removed with it and the zone names never reach the assembly.

`Profiling.enabled` is a plain field rather than something the JIT can fold to a constant, deliberately: a folded constant has to settle in a static constructor, which is before settings are loaded, and it could never be flipped by a UI toggle afterwards. A build that carries `PROFILE` is a build that intends to profile, so it pays one predicted branch.

### Why nothing is shared between threads
The tables are `[ThreadStatic]`, and `Report` only ever touches the calling thread's own. Reading another thread's live `Dictionary` while it adds a key is not a torn number, it is a corrupted resize, so the alternatives were a lock on every zone edge, a published snapshot per thread, or not crossing threads at all. Not crossing is both free and smaller, and a line per thread is what you want to read anyway, since main and render are different budgets.

This is the one place the design deliberately diverges from [[LOGGING]], which does merge every thread's lane into one time-ordered stream.

A capture does cross threads, but by handing a buffer over rather than sharing one. The recording thread fills a batch, submits it, and never touches it again; the spool writes it and pushes it back onto that thread's free stack. The rule that matters — no thread reads another thread's live tables — still holds, and the cost is one lock acquire per batch instead of one per zone edge.

### Capturing frames
A capture records every span of every frame rather than a period's totals.

```csharp
Profiling.Capture();          // the next BurstFrames frames, then it closes the files
Profiling.Capture(1200);      // or a length of your own
```

`Profiling.Capture` is tagged as an `Input` action, so it can be bound to a key in an application's `InputMap.inputs.xml`; Thorium binds it to `F9`. Setting `ProfilingCapture.Mode` to `Continuous` starts one at launch instead and never ends it, rolling into a new session folder every `MaxFileMB` and keeping the last `Keep` of them.

While a capture is live, `Zone.Start` appends a record and `Zone.End` patches its end time. Opens happen in the order the zones nest, so the array is already in document order and needs no sorting. Nothing on the timed thread formats anything: `FrameSpool`, a background thread, does every `XmlWriter` call and every file write, which is the point — batching the writes without moving the formatting would only have relocated the cost.

Each thread holds three batches. If the spool falls behind and none is free, **frames are dropped and counted, never waited on** — a profiler that stalls the thing it measures is worse than one with a gap in it. The count surfaces as `Dropped` on the next batch written. Two gaps follow from never touching another thread's tables, and are accepted: drops still pending when a capture ends are not reported, and a partial batch held by a thread at shutdown is lost.

### Profiling a launch
Two ways to start a capture at launch rather than at a keypress, and they do not cover the same window.

```
Thorium.exe --profile          # BurstFrames frames of every thread, from boot
Thorium.exe --profile=1200     # or a length of your own
```

`Profiling.ArmBoot` reads that off the process command line and opens the session from `Engine.Init`, before the bootstrap phase runs — which is why **the bootstrap phase itself is captured**, as one frame holding a zone per step, written as `Bootstrap.frames.xml` beside the thread files. It lives in the engine rather than in each application's `Main`, so Thorium, Carbon and the Editor all take the flag without knowing about it.

Setting `ProfilingCapture.Mode` to `Boot` does the same for a run with no argument, and is the one to reach for when every launch should be profiled. It cannot capture the bootstrap phase: the setting is read by `Profiling.Configure`, which is itself a step of the phase in question, so the capture only opens in time for the first real frame. The flag wins when both are set.

Neither reaches `XSDGenerator.GenerateXSD()`, which every application runs in `Main` before the engine is initialised at all.

One number to read carefully: `--profile=N` is N frames **per thread**, and on the main thread the bootstrap frame is one of them. A 120-frame capture writes 1 boot frame and 119 main frames, against a full 120 each from render and physics.

### The frame file
One file per thread, `Profiling/<yyyyMMdd-HHmmss>/<thread>.frames.xml`.

```xml
<FrameCapture Thread="Main" Frequency="10000000" Started="…" Mode="Burst" Requested="300">
  <Names><N I="0" V="MainTick"/><N I="1" V="HandleUI"/><N I="2" V="Measure"/></Names>
  <Batch Seq="0" Dropped="0">
    <F I="10412" T="8823410992341" D="91004" A="350072">
      <Z N="0" B="0" E="83120" A="300048">
        <Z N="1" B="4903" E="12142" A="200024"><C N="2" V="412"/></Z>
      </Z>
    </F>
  </Batch>
</FrameCapture>
```

`F` is a frame — `I` its index (the system's epoch), `T` the `Stopwatch` stamp it started on, `D` how long it took, `A` how many bytes it allocated. `Z` is a span and `C` a counter, `B` and `E` are ticks measured from the frame's own `T`, `A` is the span's own bytes, and `N` is an index into `Names`. Everything is a raw tick or byte count with `Frequency` at the top, because formatting a millisecond figure costs more than the span being measured; the reader divides.

Three consequences worth holding on to. The nesting of the elements is the nesting of the zones, so the file **is** the flame chart and nothing has to be reconstructed. `T` is process-wide, so two threads' files stack on one timeline without a correlation id — the render thread's wait sits directly under whatever main was doing. And the gap between `D` and the root span's `E` is time the thread spent parked, which draws itself.

A file whose process died has no closing `</FrameCapture>`. That is deliberate — the last partial batch of a crashed run is worthless, so a reader is expected to tolerate the truncation. A clean exit closes it through `Profiling.Flush`, the shutdown step before `Logging.Flush`.

One shape in the file catches readers out: a `Z` with no children and no counters is written self-closing, as `<Z N="3" B="84947" E="94958" />`, so nothing can wait for a closing tag that will not come. `A` is omitted from a span that allocated nothing, and from one the frame edge cut off before it closed; a file written before allocation was recorded reads back as zero rather than failing.

### Allocation
Every span and every frame also carries how many bytes the thread allocated inside it, sampled with `GC.GetAllocatedBytesForCurrentThread()` — a thread-local read that allocates nothing itself, taken at the same gates as the clock.

Bytes behave exactly like time. They are **inclusive**, so a parent's number contains its children's; the aggregate follows the same outermost-only rule as a recursive zone's duration, and the capture stream records every instance. Per-thread means per-thread: work a zone hands off to a pool thread is not in its bytes.

Two things are worth knowing before reading a number. The first frame of a thread carries the profiler's own table growth — a few hundred bytes for the report, a few hundred KB more if a capture is starting, since that is where the batches are built. And the measured window is `Frame.Begin` to `Frame.End`, which is why turning the one-second report on does not inflate the figures: the report's own string building happens after the frame has closed.

**Bytes collected are not recorded.** The runtime does not expose bytes freed, and the one API that gives real per-collection detail, `GC.GetGCMemoryInfo()`, allocates — a profiler that allocates to measure allocation corrupts its own measurement. Collection counts and a derived reclaimed figure are described in `ClaudeMemory/Decisions/engine-profiling.md` §12 and were deliberately not built.

### Reading it back
`FrameCaptureReader` is the other half of `FrameSpool`, in the same namespace, and is `WriteFrame` run backwards — element nesting is the span depth, so the flat arrays come back the way the recording thread built them. `Enumerate(root)` lists session folders without parsing anything, `LoadSession(dir)` reads every thread file in one, and `LoadFile(path)` reads one. Truncation is expected rather than exceptional: what was read is kept, `truncated` is set, and the frame the writer died inside is dropped because nothing references its spans.

The viewer that draws these files is **Carbon**, a fourth application on the engine's own UI beside Thorium and the Editor. It shows the session list, a bar per frame per thread, one frame's flame chart, every thread aligned on the absolute clock, and the zone totals rolled up over the capture. See `ClaudeMemory/Decisions/carbon-frame-viewer.md`.

### Reading the output
```
Main 1002ms — allocated 12.41MB
Main 1002ms — MainTick 8.31ms x120 (min 7.902 max 9.421) 11.90MB
Main 1002ms — HandleUI 5.10ms x120 (min 4.880 max 6.114) 3.21MB — Window 120
Render 1001ms — Draw 6.02ms x144 (min 3.910 max 9.008) 84.2KB
```
The first line is the whole thread over the period, including whatever ran outside any zone. Then per zone: the period the line covers, the zone's accumulated time, its entry count, the shortest and longest completed span, and its bytes. Counters for that zone follow the dash.

### Not here yet
GPU timings are out. When frame times are read back from a `VkQueryPool` they will land on the same tables through a submit that carries its own start and end stamps, which is part of why the API is `Start`/`End` rather than a `using` scope — a GPU span closes frames after the code that opened it has returned. Bytes collected, collection counts and GC pause time are out too, for the reasons above.

The report is a log line and the capture is a file — neither is a panel. The reader and its viewer landed as **Carbon** (above); the UI-hosted test and profiling platform is still the roadmap item this was built to feed. There is still no `.xsd` for the frame format: the reader is hand-written and is the only consumer, so a schema would validate nothing but files the engine itself wrote.
