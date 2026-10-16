# Decision — an open .xml note and its file stay in step both ways, by segments

**Date:** 2026-10-15
**Status:** LANDED, test-verified. Not GUI-verified (see Known gaps).
**Amended 2026-10-16:** saving an open `.xml` note no longer does file I/O on the main thread: the note's bytes are kept in memory, the journal is held open per session with a checksummed record, and the commit runs on a background writer thread (`FileWriteQueue`). The XML writer indents with one tab per level. See "Off the main thread" and "Measured" below.
**Scope:** `ArctisAurora.Core.Filing.Xml` (`XMLWriter`), `ArctisAurora.Core.Filing` (`FileJournal`, `FileWriteQueue`), `ArctisAurora.Core.Filing.Serialization` (`Paths.JOURNAL`), `ArctisAurora.Core.UI` (`NoteSegments`, `SegmentPatch`, `NoteSegment`, `NoteSegmentKind`, `NoteFileSync`, `NoteNode`, `RichTextDocument`, `DocumentEdits`, `DocumentControl`, `DocumentEditSession`, `NoteSessions`, `DocumentEditorControl`, `NoteNameWindow`, `NoteActions`, `TabViewControl`, `TableControl`), `ArctisAurora.EngineWork` (`Shutdown`), `Shutdown.shutdown.xml`

Groundwork for later network replication. Reverses part of [[note-naming-and-text-field]] (close-time naming).

## What changed
- In-app edits autosave: first edit saves on the next tick, then at most once per 250 ms, plus a final save after the last edit. One `NoteFileSync` per session, created and disposed by `NoteSessions.Open`/`Close` (dispose flushes). Ctrl+S still works.
- Writes only the changed top-level segments, in place. `NoteSegments.Write(document, path, map)`; first write (no map) is whole. `NoteSegments.Build(document, path, out map)`, `Map(bytes, document)`, `Rebase(file, outside, document, path)`.
- Outside writes reach the open note through a `FileSystemWatcher` in `NoteFileSync`; applied as one undo step ("Reloaded from disk") through `RichTextDocument.ReplaceNodes(first, removed, added)` raising `NoteChangeKind.Nodes`, recorded as `NodesEdit` + `NoteHeader` (`DocumentEdits.cs`). `DocumentControl.ShowNodes` reconciles views.
- Crash safety: `FileJournal` is an instance per note (`FileJournal.Open(path)`, held open, `FileShare.None`) with `Commit(writes, length)`; static `Commit(path, writes, length)` (open + commit + dispose) and `Replay(path)` remain. Files under `Paths.JOURNAL` (`%LOCALAPPDATA%\Arktis\Journal`). `RichTextDocument.Load` replays a leftover record first. Record format and flush count: see "Journal record" below.
- `XMLWriter.Element(StringBuilder, XElement, depth)` / `StartTag(StringBuilder, XElement, declareNamespace)`: CRLF, tab indent (`XMLWriter.Indent` is `"\t"`, was `"  "`); `DocumentXml.Save` writes through `XmlWriter.Create(path, new XmlWriterSettings { Indent = true, IndentChars = XMLWriter.Indent, NewLineChars = XMLWriter.Newline })` instead of `XDocument.Save(path)`. `DocumentXml`: `WriteLayout`, `Groups`, `WriteGroup`, `WriteEntries`, `BindSchema` split out; `WriteScalars` internal.
- Dirty tracking: `NoteNode.dirty` (internal), set by `RichTextDocument.MarkDirty(kind, first, count)` from `Raise`; `TableHolding(index)` finds the table of a flat block. `TableControl.EndResize` marks its table.
- `DocumentEditSession.sync` and `Repath(newPath)` (re-points the watcher).
- Close-time naming removed: `DocumentEditorControl.SaveNamed`, `needsNaming`, `onNamed`; `NoteActions.SettleUnnamed` (action `Notes.SettleUnnamed`), `FirstUnnamed`, `discarded`, `ForgetDiscarded`. `NoteNameWindow.Ask(source, suggestion, onConfirm, onCancel)` lost `onDiscard` and the "Don't save" button. `TabViewControl.CloseTab` and `NoteActions.SettleWindow` save edited notes without prompting.
- `DocumentEditorControl.Save()` with no session invokes `onSave` and returns (Tex preview Ctrl+S, formerly in `SaveNamed`); `TextInputActions.Save` calls it.
- `Shutdown.shutdown.xml`: the `Notes.SettleUnnamed` step is gone; `<Phase Name="Request">` stays, empty. `Shutdown.Resume` has no callers but stays.
- Tests: engine suite `Notes` (`DirtyTracking`, `PartialWrite`, `JournalReplay`, `Autosave`, `DiskReload`, `LocalWins`, `NoNamingPrompt`) in `NoteTests.cs` / `Notes.tests.xml`; Thorium `NoteModel.SegmentMap`; `TextInputTests.Unstamped(document)`; `SheetTests` MathLinks reads the body after the frontmatter.
- 2026-10-16, `NoteSegments`: new `Prepare(RichTextDocument document, string path, List<NoteSegment>? map, byte[]? file) → SegmentPatch` builds the writes from the in-memory file bytes, reuses `file` when its length holds, builds the whole file with no map or no file, and clears the dirty flags. New `public readonly record struct SegmentPatch(List<(long offset, byte[] bytes)> writes, long length, List<NoteSegment> map, byte[] file)`. `Write(document, path, map)` kept as `File.ReadAllBytes` + `Prepare` + `FileJournal.Commit` + DEBUG `Verify`. `Patch`/`Match` take `byte[] file` instead of a `FileStream` and are `[MethodImpl(MethodImplOptions.AggressiveOptimization)]`. `WriteWhole` → private `Whole` (returns a `SegmentPatch`). `ReadAt` removed.
- 2026-10-16, `FileJournal`: static class → `public sealed class FileJournal : IDisposable`. Instance `Commit(writes, length)`: write the record, `Flush(true)`, apply to the target, `Flush(true)`, zero the magic with `Flush(false)`. `Replay(path)` returns false while that path's journal is held in-process, deletes a zeroed record silently, warns on a torn record or one that fails its checksum. `Dispose` deletes the journal file. Internal `Record(path, writes, length)` is the test hook (records, leaves it unapplied). Record: `int magic, byte[32] SHA-256(body), int bodyLength, body(path string, long length, int count, (long offset, int size, bytes)*)`; the trailing commit marker is gone.
- 2026-10-16, new static `FileWriteQueue` (`Core.Filing`): one `IsBackground` thread named "file writes", started lazily, FIFO. `Enqueue(FileJournal journal, SegmentPatch patch, Action<Exception?> done)` (`done` runs on the writer thread), `Drain()` waits until nothing is pending, `[A_XSDActionDependency("Files.Drain", "Shutdown")] DrainAll()`.
- 2026-10-16, `NoteFileSync`: new fields `byte[]? file`, `FileJournal? journal`, `int inFlight`, `ConcurrentQueue<Exception?> landed`, `bool flushing`. `Write()` → `Write(!flushing)`; private `Write(bool wait)`: `Prepare` on main, `FileWriteQueue.Enqueue`, and with `wait` set `Drain()` + `Landed(false)` rethrowing the first failure (ExceptionDispatchInfo). New private `Done(Exception?)` (writer thread: `Stamp` on success, enqueue the outcome, `Engine.Post(() => Landed(true))`), `Landed(bool warn)` (main: settles counts; on failure sets map/file null, `session.MarkUnsaved()`, warns once; on success resets `warned` and, when `stampModified` is off, sets `document.modified` from the file time; with nothing in flight runs `Settled()` once), `DropJournal()` (drain if in flight, dispose the journal; called from `Watch()` and `Dispose()`), `Remap(byte[])`. `Settled` and `Reload` skip while writes are in flight. `TrySave` resets `warned` only for non-segmented saves. `Flush` sets `flushing` around `TrySave`.
- 2026-10-16, `DocumentEditSession.MarkUnsaved()` (internal): sets isDirty without restarting autosave. `VaultBrowserControl.RenameNote` (Thorium): `FileWriteQueue.Drain()` before `File.Move`.
- 2026-10-16, data: `Shutdown.shutdown.xml` Commit phase gains `<Step Action="Files.Drain"/>` right after `Notes.SaveEdited`; `Perf.tests.xml` gains `Perf.SaveLargeNote.InPlace` and `.Shift` (budgets under "Measured"). Tests: `PerfTests.SaveLargeNoteInPlace`/`SaveLargeNoteShift` with helpers `SaveSegments`, `ToggleChars`, `LargeDocument`, constants `saveBlock = 500`, `shiftChars = 2000`, `saveWarmupTicks = 150`; `NoteTests.ReadsBack` calls `FileWriteQueue.Drain()` first; `Notes.JournalReplay` gains "a record that fails its checksum is dropped unapplied" and renames "cut off before its commit marker" → "cut off mid-record".

## Why these choices

**No note-format redesign.**
Notes already save from the model (`NoteNode`/`NoteBlock`/`NoteTable`), not from controls; the real need was replication and consistency.

**Partial writes by top-level segment.**
Segments are the top-level elements of `<Document>`: header, `<DocumentLayout>`, each `<Block>`/`<Table>`, each `<Footnote>`/`<Float>` group. An edit that fits its segment's slack (trailing spaces) is overwritten in place. One that outgrows it shifts the file's tail once, from the first size-changing hole; the user accepted that shifting the tail is physically unavoidable and must just be rare. A grown segment gets slack `max(64 B, 25%)`; leftover slack beyond `max(256 B, 50%)` is trimmed by shifting left. In DEBUG every synchronous `NoteSegments.Write` reads the file back, re-maps it and throws on any mismatch; the background autosave path has no such check. With no map (first write) the whole file is written.

**Crash safety from a journal, not temp-file + atomic replace.**
Atomic replace rewrites the whole file, which defeats partial writes. `FileJournal` records each batch, applies it, and clears the record; `RichTextDocument.Load` replays a leftover one. Since 2026-10-16 the journal file is held open per note and the record carries a checksum (see "Journal record"). Binary, because it is a short-lived crash record and not a data/config file, so the XML-for-data rule does not apply.

**Our own XML writer.**
`XMLWriter` is the start of a custom XML suite; the user eventually wants custom parsing, writing and XSD verification there. Note-specific segment logic lives in `NoteSegments` (`ArctisAurora.Core.UI`).

**Dirty flag set from `RichTextDocument.Raise`, not from runs notifying their blocks.**
`Raise` is the single exit every model edit passes (the user's choice). Header and layout are compared at write time instead of flagged. The column drag marks its table in `TableControl.EndResize`.

**Autosave cadence and a stale-map guard.**
First edit next tick, then at most every 250 ms, plus a final save. If the file's length or write time differs from what was last written, fall back to a whole write.

**Reload into a clean note matches lead and tail by node XML.**
Widened to whole footnote/float groups, applied via `ReplaceNodes`, one undo step. A write that does not parse as a note is ignored.

**On conflict the app wins (the user's call).**
Unsaved local edits are written over an outside write, via `NoteSegments.Rebase`.

**A failed autosave write stays dirty and retries (the user's choice).**
Folder deleted or file locked: caught, one Warn logged, dirty flag kept. Ctrl+S is unchanged. From 2026-10-16 a failed background write comes back through `NoteFileSync.Landed`: it drops `map` and `file`, calls `DocumentEditSession.MarkUnsaved()` and warns once; no test covers this.

**Tests ignore the Created/Modified stamps when comparing undo results.**
Autosave re-stamps Modified (the user's choice). Undo does not roll back Modified, by design.

**The `Notes` tests are in the engine suite.**
They read internal flags and there is no `InternalsVisibleTo`.

**Naming happens at creation; the close-time prompt is gone.**
The vault already prompts for a name when it creates a note (F5/New), so the close-time prompt (shutdown step `Notes.SettleUnnamed`, `CloseTab`'s prompt, `SaveNamed`) was removed; it was dead anyway since autosave clears `isDirty`. `NoteNameWindow`'s Discard button only served that path, so it went too. A note with no Name keeps showing its file name. This reverses part of [[note-naming-and-text-field]].

**The Shutdown Request phase stays as an empty `<Phase>`.**
If the phase element were missing, `RunPhase` logs an error and returns false and the app could never quit. `Shutdown.Resume` is kept for future Request steps.

**A test that edits a note clears the tree before deleting its file.**
Autosave or exit-time `Notes.SaveEdited` would recreate the deleted temp fixture. `.claude/skills/aurora-test/writing.md` rule rewritten; the old reason (the exit-time naming prompt hanging `--test`) no longer exists — see [[test-run-hangs-on-unnamed-note]].

## Off the main thread (2026-10-16)

**Saving an open note no longer stalls the main thread on file I/O.**
Two steps: (a) fewer file opens per save, (b) the commit on a background thread. The tab-indent XML writer change is separate and left whole-save cost unchanged (ranges overlapped).

**The cost was file opens, and probably Windows Defender, not our code.**
A CPU trace put ~80% of `Patch` in `FileStream`'s constructor. A probe outside the repo: opening a file right after it was written and closed costs ~10 ms (p95 13.6), a second open 0.1 ms; three forced flushes cost only ~2.5 ms. Real-time protection was confirmed on. Each save used to open or create files about three times (read the note, create the journal, write the note) and delete the journal; the journal delete waits on the journal's own scan inside every save. Back-to-back saves also hit the previous save's scan, so the bench overstated real autosave cost (see [[benchmark-back-to-back-file-saves]]). The Defender attribution is from the probe plus the trace, not proven.

**(a) In-memory file bytes and a held-open journal.**
`NoteFileSync.file` removes the read open; holding the journal open removes create and delete.

**Rejected: holding the note itself open for writing across saves.**
An outside writer that opens with `FileShare.Read` (`File.WriteAllText`, our `DiskReload`/`LocalWins` tests) would get a sharing violation, which breaks outside-write live sync.

**Journal record: a SHA-256 checksum instead of a commit marker (chosen by the user).**
The agreed reuse-the-record design would have replayed torn mixes: a same-layout record overwritten during a power loss can keep the old magic and old commit marker, and replay would apply a mix of new and old bytes. Chosen: SHA-256 of the body in the record, verified by `Replay`, one journal `Flush(true)` per commit, commit marker dropped. Clearing is an unforced zeroing of the magic, safe because replay is idempotent (absolute offsets + final length). That is 2 forced disk flushes per save, down from 3. Rejected: (2) keep the marker and zero + force the magic before each record (4 flushes, ~0.8 ms each); (3) truncate the record before each commit, which relies on NTFS persisting the truncation before the data, not guaranteed. Hashing costs microseconds for in-place records and ~2 ms for whole-file records.

**(b) A dedicated writer thread, main builds the patch (chosen by the user).**
Main builds the `SegmentPatch` (match, serialize the dirty elements, header, holes, new map and file bytes) and hands the writer only byte arrays main never touches again, so no model or byte-copy state crosses threads. A plain thread with no pools and not a `ThreadedSystem`, consistent with LogSpool/FrameSpool; a single thread guarantees per-file order. Rejected: `Task.Run`/thread pool, which nothing in the engine uses and whose ordering would need extra work.

**Explicit saves wait (chosen by the user).**
Ctrl+S, tab/window close, `Dispose`, `Notes.SaveEdited`, `Overwrite` and rename all drain, so "saved" still means on disk and Ctrl+S failures still surface through `TrySave`. Only the autosave timer path (`Flush`) returns without waiting. Rejected: everything async, which would need a drain in every save-then-read test and caller. `Files.Drain` at shutdown exists because process exit would kill the background thread mid-autosave. The vault rename drains before `File.Move` (the only place an open note gets moved), because a queued write would recreate the old path (Apply uses `OpenOrCreate`).

**`AggressiveOptimization` on `NoteSegments.Patch` and `Match` (chosen by the user).**
After (b), `Perf.SaveLargeNote.InPlace` showed a one-off 13–20 ms max in about half the runs. Not GC: a 512 MB `DOTNET_GCgen0size` run still spiked. Gone with `DOTNET_TC_QuickJitForLoops=0` (0/4) and `DOTNET_TieredCompilation=0` (0/6); a longer warm-up (150 saves) moved it rather than removing it. `DOTNET_JitDisasmSummary` showed spiking runs recompile `Patch` and `Match` a second time mid-run (`Tier1-OSR`, 3–6 KB of code each), synchronously on main; a clean run had no second pass. Marking both `AggressiveOptimization` removed it: 0/6 runs spiked, max 1.9–4.0 ms. The real app would otherwise have hitched ~16 ms once per session. The save tests keep their own 150-save warm-up (`saveWarmupTicks`). See [[perf-spike-is-osr]].

## Measured (2026-10-16)

Release+PROFILE, `Note.Save` zone on Main, 1M-char note (1000 blocks × 1000 chars, ~1.05 MB file), saves back-to-back unless noted.

| Step | Path | p50 (ms) | p95 (ms) | Alloc |
|---|---|---|---|---|
| Before (3 runs) | whole save, `DocumentXml.Save` (HEAD) | 130–134 | — | 25.6 MB |
| Before | in-place `NoteSegments.Write` | 37–48 | 50–52 | 345 KB |
| Before | tail shift | 40–49 | — | — |
| Before | first (whole) `NoteSegments.Write` | 140 | — | 32 MB (the journal holds a second copy) |
| Before, saves 250 ms apart | in-place, same code | 9.4–10.3 | — | — |
| After (a) | in-place | 2.40–2.53 | 3.05–3.56 | — |
| After (a) | shift | 5.8–8.1 | 26–32 | — |
| After (a), spaced | in-place | 2.75–4.61 | — | — |
| After (b), main thread only | in-place | 0.36–0.42 | 0.79–0.89 | — |
| After (b), main thread only | shift | 0.70–0.81 | 1.15–1.47 | — |

- Writer-thread I/O roughly equals the spaced synchronous number (~3–6 ms per save), off the main thread.
- The `Note.Save` zone wraps only `Prepare` plus bookkeeping; the commit is outside the zone.
- Budgets by the rule (P95 = 2× the worst p95, `Max` 8, `AllocKB` = 2× the worst frame), from 6 Release+PROFILE runs. Budgets are per machine.

| Test | Worst p95 (ms) | P95 | Worst frame KB | AllocKB | Max |
|---|---|---|---|---|---|
| `Perf.SaveLargeNote.InPlace` | 1.473 | 3 | 336.0 | 672 | 8 |
| `Perf.SaveLargeNote.Shift` | 1.361 | 3 | 2398.5 | 4797 | 8 |

## Known gaps
- Test-verified only: full suite 324 passed; the only failure is the existing Boot baseline error, no new errors, no `[Vulkan]` lines. `Notes` suite 7/7.
- **Not GUI-verified:** nobody has edited an open note from another program by hand while Thorium was running. The secondary-window close path (`WindowActions` → `NoteActions.SettleWindow`) is not tested directly; it is the same `SaveEditedIn` the tested `CloseTab` uses.
- Whole writes carry zero slack: `NoteSegments.Build` writes none, so the first edit to each block in a freshly written or `XDocument`-saved file shifts the tail; only the second lands in place. Option under consideration: `max(64 B, 25%)` slack at a whole write (bigger file).
- Segment writes for every file type are wanted, but `.md`/`.txt`/`.tex` still write whole through `document.Save`, not the journal. Needs its own analysis of `MarkdownFormat` / `PlainTextFormat` / `TexSourceFormat`.
- An open note's file renamed or deleted by another program is not followed (the watcher filters on the old name).
- Reload reads and parses on the main thread, so a large note blocks a frame.
- No direct test that `Overwrite`/`Rebase` reuses matching segments; `LocalWins` checks the outcome, which the stale-map whole write could also produce.
- Sheets and planners have no live sync.
- `Notes.DirtyTracking` leaves a stray temp file (autosave rewrites it after the test deleted it).
- 2026-10-16 verification: test-verified. Debug: `Notes` 7/7, Thorium `NoteModel` passes, full suite 324 passed (only the existing Boot baseline fails), no `[Vulkan]` lines. Release+PROFILE `_Build/PerfTests.cmd`: `Perf.SaveLargeNote.InPlace` and `.Shift` pass their budgets. **Not GUI-verified:** nobody has edited a note in Thorium by hand with background autosave on, renamed a note mid-autosave, or pulled power mid-commit.
- No test that a failed background autosave re-marks the session dirty, and no test for the in-flight rules in `Settled`/`Reload`.
- The DEBUG `Verify` (read the file back and re-map it) runs only in the synchronous `NoteSegments.Write`; the background autosave path has no DEBUG verification.
- `FileWriteQueue` lives in `Core.Filing` but takes `Core.UI.SegmentPatch`, so the dependency runs Filing → UI.
- The first full write of a session is still serialized on main (~60–140 ms for a 1 MB note, numbers drifted between sessions on this machine). Whole-file timings drifted ~2× between sessions (HEAD-identical `Whole` went 118–125 → 62.8–63.1 ms p50); treat cross-session whole-save numbers as unreliable.
- `.md`/`.txt`/`.tex` notes still save whole and synchronously on main.
- If an outside write lands between our apply and our stamp on the writer thread, it is taken as ours (the same window existed before).
- A note deleted while an autosave is queued gets recreated by Apply's `OpenOrCreate` (existing gap, now also possible from the background thread).
- Pre-existing Perf failures in the same `--test=Perf` run, not investigated: `Perf.RelayoutLabels` max 9.2 ms, `Perf.TypeLargeNote` max 11.6 ms, `Perf.RewrapLargeNote` max 16.3 ms, `Perf.Controls.Table.Relayout` alloc 176.7 KB > 65.

Related: [[note-naming-and-text-field]], [[shutdown-sequence]], [[engine-testing]], [[benchmark-back-to-back-file-saves]], [[perf-spike-is-osr]], [[note-file-formats]], [[note-model]], [[test-run-hangs-on-unnamed-note]], [[document-xml-persistence]]
