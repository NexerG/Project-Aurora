# Mistake — a one-off mid-run perf spike that vanishes under QuickJitForLoops=0 is a Tier1-OSR recompile

**What happened (2026-10-16):** `Perf.SaveLargeNote.InPlace` showed a one-off 13–20 ms max in about half the runs after the save moved off the main thread.

**What it was not:**
- Not GC: a 512 MB `DOTNET_GCgen0size` run still spiked.
- A longer warm-up (150 saves) moved the spike rather than removing it.

**What it was:** spiking runs recompiled `NoteSegments.Patch` and `Match` a second time mid-run (`Tier1-OSR`, 3–6 KB of code each), synchronously on the main thread. A clean run had no second pass.

**How to tell:**
- The spike is gone with `DOTNET_TC_QuickJitForLoops=0` (0/4 runs) and with `DOTNET_TieredCompilation=0` (0/6).
- `DOTNET_JitDisasmSummary=1` plus `DOTNET_JitStdOutFile` lists every jitted method with its tier and names the one recompiled mid-run.

**The fix used:** `[MethodImpl(MethodImplOptions.AggressiveOptimization)]` on the two hot loop methods: 0/6 runs spiked, max 1.9–4.0 ms. The real app would otherwise have hitched ~16 ms once per session.

Related: [[note-live-sync]], [[engine-testing]], [[profiling-unoptimized-jit]], [[benchmark-back-to-back-file-saves]]
