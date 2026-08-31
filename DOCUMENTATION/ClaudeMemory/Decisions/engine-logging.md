# Decision — logging is per-thread SPSC lanes drained by one background thread

**Date:** 2026-08-22
**Status:** LANDED. Solution builds clean; Thorium boots, writes
`%AppData%/Thorium/Logs/engine.log`, rotates, and dumps the recorder on a crash — all verified by
running it. **`Logging.Flush` is not GUI-verified** (the shutdown sequence needs a real window close;
same gap as [[shutdown-sequence]]). Swapchain-rebuild lines are not verified either — they need a
resize.
**Scope:** new `ArctisAurora.Core.Diagnostics` (+ `.Sinks`), `ThreadedSystem`, `Bootstrapper`,
`Shutdown`, `Renderer`, `SettingsRegistry`, `Bootstrap.xml`, `Shutdown.xml`, and the 51
`Console.WriteLine` call sites across 24 files.

## What was there before

Nothing. 51 raw `Console.WriteLine` calls, already conventionally prefixed `[Bootstrap]`,
`[Settings]`, `[Renderer]`, `[XSD]` — so the category was half-designed and became the channel name.

## Decisions

### 1. Interpolated string handlers, one type per level

`Log.Warn($"…")` takes a `[InterpolatedStringHandler] ref struct` whose constructor is
`(int literalLength, int formattedCount, LogChannel channel, out bool enabled)`. When `enabled` comes
back false the compiler emits **none** of the `AppendFormatted` calls, so the holes are never
evaluated — a disabled level is one field load and a predicted branch.

Seven handler types rather than one, because the level has to reach the constructor and
`[InterpolatedStringHandlerArgument("")]` only carries the receiver. Each is ~20 lines over a shared
`LogWriter`. Rejected: one generic `LogHandler<TLevel>` with a static-abstract level tag. It works
and the JIT would constant-fold it, but it buys nothing over seven trivial structs and reads worse.

Each handler has **two** constructors — one taking `LogChannel`, one taking `LogGate` — which is what
lets `Log.Warn(…)` and `Log.Every(1000).Warn(…)` share one type.

### 2. Formatting is UTF-8, once, on the calling thread

`LogWriter` appends through `IUtf8SpanFormattable.TryFormat` into a 1 KB `[ThreadStatic]` scratch
buffer. No intermediate `string`, and the bytes serve both the file and the console.

The `value is IUtf8SpanFormattable` test does **not** box for value types: struct generic
instantiations are specialized, so the test folds and the interface call devirtualizes. This is what
`Utf8.TryWrite`'s own handler does.

Overflow truncates with a trailing `...`. A logger that throws is worse than no logger, so nothing in
the write path can.

### 3. One lane per thread, mirroring CommandLane — not a shared MPSC queue

`LogLane` is a ring of 24-byte-ish `LogRecord` plus a byte arena, four cursors, one volatile store
each way. Structurally the same as `CommandLane` + `CommandArena`, for the same reason recorded
there: per-thread, every ring has one writer and one reader, so an enqueue is a store and a cursor
bump. A shared inbox would have three threads CAS on one cache line.

It is a **copy, not a reference**. `CommandApplier`, `DataPool` and `DataManager` all log, so a
diagnostics namespace depending on `Core.Data.Commands` would close the loop. The duplication is
~80 lines and deliberate.

Threads that are not a `ThreadedSystem` (GLFW callbacks, importer tasks, the validation callback)
share one lane behind a `Lock`. In practice that is 1 shared + 3 system lanes, forever.

Text never straddles the arena's end — a run that would wrap skips the tail and restarts at zero, so
the consumer always reads one contiguous span.

### 4. The drain is a plain thread, not a ThreadedSystem

It owns no pools. Making it a `ThreadedSystem` would hand it a `SystemId` and six command lanes it
would never read, and `BuildLanes` would size every other system's arrays around it. A background
`Thread` at `BelowNormal`, woken by an `AutoResetEvent` or a 250 ms poll.

It k-way merges the lanes by timestamp, so three free-running threads interleave in the order they
actually logged rather than in lane order.

### 5. Records carry object references, not interned ids

`LogRecord` holds `LogChannel` and the `[CallerFilePath]` `string` directly rather than `ushort` ids
into a site table. The caller paths are literals, so storing them is a reference copy and the whole
interning table disappears. Only the rate gate needs a `(file, line)` dictionary, and that is the
opt-in path.

`LogGate` is a `readonly struct`, not a `ref struct` — it holds a reference and a bool and needs no
ref-struct semantics.

### 6. Timestamps are raw counter reads

`Stopwatch.GetTimestamp()` per record, converted to wall time once at format time against a boot
anchor. `DateTime.Now` does a timezone lookup per call. Same trick `ThreadedSystem.ElapsedMs` already
uses.

Every record also carries `ThreadedSystem.Current`'s `SystemId` and `Epoch` — free reads that turn a
three-thread interleave into something you can reconstruct an order from. Output is
`main:1423`, or `t7` for a thread that is not one of ours.

### 7. Rate gates key on the call site, which is free

`[CallerFilePath]` + `[CallerLineNumber]` are compile-time constants, so `(file, line)` is a stable
key nobody has to invent. `Log.Every(1000).Warn(…)` and `Log.Once().Warn(…)`. Without this, one bad
state in the render loop writes 144 lines a second and the log is useless. This is the single most
engine-specific thing in the design.

### 8. Only Hot is compiled out

`[Conditional("DEBUG")]` on `LogChannel.Hot` and `LogGate.Hot` — the call **and** its interpolation
literals are gone in Release, verified by byte-scanning the Release assembly for the literal.
`Debug.Assert` is precedent for `[Conditional]` on a method taking a `ref` handler.

`Trace` and `Debug` stay runtime-gated so a shipped build can be told to log verbosely for a bug
report. Compiling them out would make a user-reported bug unreproducible.

### 9. The channel floor is the minimum across all sinks

Not the console's. The flight recorder captures below what anything prints, so those lines still have
to be formatted — the sinks filter afterwards. `LogSpool.Configure` computes
`min(console, file, recorder)` and pushes it to every channel.

### 10. Self-starting, configured later, replayed retroactively

`XSDGenerator.GenerateXSD()` runs before `Engine.Init()` in `Thorium.Main` and logs, so the logger
cannot be a bootstrap step. `LogChannel.For` starts the spool.

`Logging.Configure` is step 2 of `Bootstrap.xml`, right after `Settings.LoadAll`. Before it: the
console prints at a fixed `Info` default (so a boot that wedges still says where), and every line is
also held in a bounded pending list. At `Configure` the held lines are replayed into the file and the
recorder at their real levels. Nothing from early bootstrap is lost.

`Configure` runs on the main thread but the sinks belong to the spool thread, so it posts a snapshot
and waits up to 2 s for the ack — the steps after it are already writing to a live file.

### 11. Three spill triggers, not one

Watermark (half the buffer), `FlushMs` elapsed, and **any record ≥ Error**. The error trigger is the
point: the line you most want on disk is the one immediately before the crash. The time trigger is
what saves a hard kill — verified, a `taskkill /F` still left all 61 lines on disk.

### 12. The recorder dumps once, and only when it has something the file lacks

First implementation dumped **twice** — `ThreadedSystem`'s catch and `OnUnhandled` both called it —
and with default levels the tail duplicated what the file sink had already written. A crash tripled
the log (201 lines for a 60-line session).

Now: an `Interlocked` once-flag, plus a guard that skips the dump entirely when
`recorderMin >= fileMin`, because then every line in the tail is already on disk. The recorder earns
its keep in the configuration it was designed for — file at `Warn`, recorder at `Trace`.

### 13. The crash catch rethrows

`ThreadedSystem.Loop` wraps everything in `try/catch` → `Log.Exception(Fatal, …)` +
`DumpRecorder()` → **`throw`**. Swallowing it would leave a dead render thread silently spinning,
which is worse than the crash. Current behaviour is preserved exactly; only the log line is new.

Verified with a temporary throw on the physics thread: the `Fatal` line, the full stack and the
recorder tail all reach the file before the process dies.

### 14. Console writes raw UTF-8, coloured only when the terminal admits it

`Console.OpenStandardOutput()`, written by the spool thread and nobody else, so no game thread ever
takes the console lock. VT processing is enabled via `SetConsoleMode`; if that fails — which is what
happens when stdout is redirected to a file — colour is dropped rather than emitting escape garbage.
Verified: a redirected run has no escape bytes.

`Console.OutputEncoding = Encoding.UTF8` at start, because the engine's own messages are full of em
dashes.

The ANSI sequences are **built** from `0x1B` at runtime rather than written as literals, so no bare
escape byte sits in the source file.

## Left standing

- **No per-channel level overrides.** `SettingCategory` resolves each child element by name against
  the category's `Setting` fields, so a repeated `<Channel Name= Level=/>` would overwrite one
  instance. The non-category `ISettingsGroup` path *does* handle child lists, so this fits as a
  sibling `<LogChannels>` group — asked, not built.
- **The log file has no BOM.** It is UTF-8 and reads correctly in `cat`, Notepad and PowerShell 7;
  Windows PowerShell 5.1's `Get-Content` misreads em dashes as ANSI. Not worth a BOM unprompted.
- **`ThreadedSystem.Send` backpressure is still silent.** Four `false` returns are four dropped
  writes. It has **zero callers** today, so nothing was wired.
- `LogLane` reports records dropped to a full ring as a synthetic line, so the loss is never silent.

Related: [[shutdown-sequence]], [[ecs-rework-data-pools]], [[settings-categories]],
[[mapped-streaming-buffers]]
