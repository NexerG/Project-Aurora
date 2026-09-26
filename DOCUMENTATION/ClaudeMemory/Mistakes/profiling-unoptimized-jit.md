# Mistake — every Debug timing was taken on an unoptimized JIT

**What I did wrong (2026-09-19 → 2026-09-24):** every `--profile-scenario` table in [[animation-core]] § Measured
at scale and [[frame-scheduler]] § Step 2 / Step 3 Measured was run from the plain Debug build. Step 3's known gap
"`Anim.Step` at 20k is 1.4–1.8 ms, not under 1 ms" and step 4's premise (SIMD to get under 1 ms) both rested on it.
Optimized, the same step is 0.24–0.51 ms — about 5× less.

**Why it happens:** the Debug configuration compiles with `DebuggableAttribute(DisableOptimizations)`, so the JIT
never optimizes — no inlining, no enregistering, `Vector128` ops as calls. Hosts only run from Debug (`Paths`
resolves `Data` by the working directory only when `Engine.isDebug`), so there is no ready Release build to
measure instead.

**Second trap:** `dotnet build -p:Optimize=true` on an up-to-date tree changes nothing — the incremental build
does not treat a property as an input, and the run gives identical numbers. Only `--no-incremental` recompiles.

**The rule:**
- **Preferred since 2026-09-25 — Release with the profiler:** `dotnet build AuroraEngine/ArctisAurora.sln -c Release
  "-p:DefineConstants=TRACE%3BPROFILE"`, run `Thorium/bin/Release/<tfm>/Thorium.exe` with that folder as the working
  directory. No DEBUG asserts. Output is `bin/Release`, so the Debug bin is untouched. Only `Engine.isDebug` reads a
  compile symbol, so replacing `DefineConstants` drops nothing else. Thorium copies its `Data` to output since
  2026-09-25; the engine's own font bakes are stale and only Thorium's shadow them.
- Fallback: `dotnet build AuroraEngine/ArctisAurora.sln -p:Optimize=true --no-incremental`. `DEBUG` stays
  defined, so `[Conditional("DEBUG")]` work (`VerifySubtreeCache`, `AssertAccess`) still costs — name it when it
  shows up in a zone.
- Afterwards `dotnet build AuroraEngine/ArctisAurora.sln --no-incremental`, or bin keeps the optimized binaries.
- Label every measured table with the build it came from.
- Early rungs of one run are still tiering up: 1k ran slower than 5k at `Threads=1`. Compare within a rung.

Related: [[frame-scheduler]], [[animation-core]], [[engine-profiling]]
