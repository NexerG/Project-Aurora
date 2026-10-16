# Mistake — saving the same file back-to-back in a perf loop measures the on-close scan, not the save

**What went wrong (2026-10-16):** the `Note.Save` zone read 37–48 ms p50 for an in-place `NoteSegments.Write` on a 1M-char note, with saves back-to-back. That looked like our cost; it was mostly file opens.

**Why it happens:**
- A CPU trace put ~80% of `NoteSegments.Patch` in `FileStream`'s constructor.
- Probe outside the repo: opening a file right after it was written and closed costs ~10 ms (p95 13.6); a second open costs 0.1 ms. Three forced flushes cost only ~2.5 ms. Windows Defender real-time protection was confirmed on.
- A save that opens or creates files (read the note, create the journal, write the note, delete the journal) pays that scan on each open, and the next back-to-back save hits the previous save's scan.
- With saves 250 ms apart, the same code measured p50 9.4–10.3 ms.
- The attribution to Defender is from the probe plus the trace; it was not proven.

**The rule:**
- Space the saves (250 ms, the autosave cadence), or compare against a spaced variant, before reading a file-save number.
- Count file opens per save, not just bytes written; the fix that worked was fewer opens (bytes kept in memory, journal held open), then moving the commit off the main thread. See [[note-live-sync]] § Off the main thread.

Related: [[note-live-sync]], [[engine-testing]], [[perf-spike-is-osr]]
