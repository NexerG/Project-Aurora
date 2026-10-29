# Pattern — measuring live heap memory on Linux

Used for [[text-run-storage]]: the Thorium `--profile-scenario` 1M-char note, before and after a change.

## Setup
- .NET SDK: `apt-get install dotnet-sdk-10.0` — builds.dotnet.microsoft.com is blocked; Ubuntu's archive has 10.0.113. Plus the packages listed in the repo's `LINUX.md`.
- Tools: `dotnet tool install -g dotnet-dump` (and `dotnet-gcdump`).
- Build: Thorium `-c DebugPerf -p:Optimize=true -p:DefineConstants=TRACE;PROFILE` for `net10.0`. Plain Release fails the ImageSharp licence check.

## Run
1. Run the scenario under `xvfb-run` with lavapipe.
2. Take the dump by the scenario process's own PID: `pgrep -f 'Thorium --profile-scenario'`, then check `/proc/<pid>/comm` is `Thorium`. A bare `pgrep -x Thorium` once caught an unrelated Thorium running in the same container.
3. `dotnet-dump collect --type Heap -p <pid>`.
4. Analyse with `dumpheap -stat -live`.

## Rules
- **Always `-live`.** Without it unreachable objects and ~20 MB of free space are counted.
- **`dotnet-dump` is the exact tool.** `dotnet-gcdump` warns its lossy buffering may drop objects on .NET 10.
- The scenario steps by ticks, so a dump's pause delays phases but never skips them.
- Take comparable snapshots each run (just after the note opens; with the Settings window open). One run per side was used.
- The scenario's 1M chars are one shared paragraph string, so text costs ~2 KB of string memory there — it is not a test of string storage.
- Object sizes: `MethodTable.BaseSize` and `Unsafe.SizeOf` from the built assembly.

Related: [[text-run-storage]], [[engine-profiling]]
