---
name: aurora-ship
description: Ship an Aurora app — publish it in Release and pack its Data and shaders into one encrypted, signed `.arx` with `_Build arx <App>.build.axml`, then prove the shipped exe boots and passes its tests from the archive. Use whenever asked to ship, package, publish, release or make a build of Thorium (or any app), on anything about `.arx`, `AppBuilder`, `<App>.build.axml`, `Ship/`, or the build keys under `%APPDATA%\Arktis\BuildKeys`, and when adding a build to Carbon or AuroraEditor.
---

# Shipping an app

`_Build arx <App>.build.axml [rid]` (`ArctisAurora.Core.Filing.Arx.AppBuilder.Run`) packs `<App>.arx` once —
engine Data, app Data and the app's `.spv` — then, for each `<Target Rid Framework>` (or only the one named by
`rid`), runs a self-contained `dotnet publish -f <Framework> -r <Rid>` in Release into `<Output>/<Rid>/`,
deletes the `Data/` and `Shaders/` folders publish copied there, and puts the archive and plain copies of
whatever `<Loose>` matches under `Data/` beside the exe. Each target carries only its own OS's natives. A
Release exe that finds an `*.arx` beside itself mounts it before boot and reads everything through it. Design
and format: `ClaudeMemory/Decisions/arx-archives.md`.

The loop: **build the solution → `_Build arx` → check the output folder → run the shipped exe's tests.**

Only Thorium has a build file today: `Thorium/Thorium.build.axml`, targets `win-x64` and `linux-x64`, output
`Ship/Thorium/<rid>/` (gitignored). macOS and mobile are not ported — see the WIP list.

## Ship

```bash
cd "$(git rev-parse --show-toplevel)" && if b=$(dotnet build AuroraEngine/ArctisAurora.sln -v q -nologo 2>&1); then _Build/bin/Debug/net10.0-windows10.0.22621.0/_Build.exe arx Thorium/Thorium.build.axml 2>&1 | grep -av ' warning '; echo "exit ${PIPESTATUS[0]}"; else echo "$b" | grep -E ' error ' | sort -u | head -20; echo "BUILD FAILED"; fi
```

`_Build` itself runs from its Debug build; only the app is published in Release. Each target's publish compiles
the whole app in Release, so give the call a long timeout or run it in the background; append a rid
(`… Thorium.build.axml win-x64`) to build one target. Publish prints every compiler warning despite `-v quiet`
(~700 lines); the `grep -v` drops them.

A pass is `exit 0` and one line per target, `[Build] <App> <rid>: N entries packed into …\<rid>\<App>.arx, M loose files beside it.`
A publish failure logs `dotnet publish … failed with exit code …` and exits 1 — rerun the publish by hand
without `-v quiet` to see why.

## Check the output folder

```bash
cd "$(git rev-parse --show-toplevel)/Ship/Thorium" && sha256sum */*.arx && for t in */; do (cd "$t" && ls -d runtimes 2>/dev/null; find Data -type f | grep -v '^Data/XML/Documents/UI/'); done; echo "--- loose check done ---"
```

One identical hash per target, then silence before `--- loose check done ---`, is a pass. A `runtimes` line
means a publish ran without a RID and carries every OS's natives. Anything else under `Data/` is read *instead
of* the archive (the loose folder mounts first) and ships in clear. The `grep -v` pattern is Thorium's `<Loose>`
rule — swap it for the app's own.

## Run the shipped build's tests

```bash
cd "$(git rev-parse --show-toplevel)" && log="${TEMP:-/tmp}/aurora-ship.txt"; Ship/Thorium/win-x64/Thorium.exe --test > "$log" 2>&1; code=$?; grep -aE '\[Test\] (FAIL|NEW) ' "$log" | sed -E 's/^.*\[Test\] //; s/  @[^ ]+$//' | cut -c1-250; comm -13 <(tr -d '\r' < _Build/test-baseline.txt | sort -u) <(grep -aE ' (ERROR|FATAL) ' "$log" | tr -d '\r' | sed -E 's/^[0-9:.]+ +(ERROR|FATAL) +[^ ]+ /\1 /' | sort -u) | grep . | sed 's/^/new error: /'; grep -a '\[Vulkan\]' "$log" | head -5; s=$(grep -aE '\[Test\] [0-9]+ passed' "$log" | sed -E 's/^.*\[Test\] //'); echo "${s:-no results line - the run died, log: $log}"; echo "exit $code"
```

The same filter as `_Build/test.sh`, pointed at the shipped exe. It should match a Debug `test.sh` run: the
same failures, no `new error:` lines. A `[Vulkan]` line stops the work (CLAUDE.md §10). `Arx.XmlRoundTrip`
passes trivially here — a shipped app carries no source trees to compare against.

On Linux (the cloud container `aurora-test` provisions), build `_Build` and ship the Linux target there, then
run the same filter with the launch line swapped:
```bash
export EnableWindowsTargeting=true; dotnet build _Build/_Build.csproj -v q -nologo && dotnet _Build/bin/Debug/net10.0-windows10.0.22621.0/_Build.dll arx Thorium/Thorium.build.axml linux-x64 2>&1 | grep -av ' warning '
VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 1920x1080x24" Ship/Thorium/linux-x64/Thorium --test > "$log" 2>&1
```
It should match a Linux `test.sh` run. A Linux folder copied off Windows loses the launcher's execute bit —
`chmod +x Thorium` first.

## Adding an app

1. `<App>/<App>.build.axml`, copied from `Thorium.build.axml`. Paths are relative to the build file.
   - `App` — names the `.arx` and the key file. `Project` — the csproj. `Output` — keep it under `../Ship/`.
   - `<Target Rid Framework>` — one per platform. `Framework` must be one of the csproj's `<TargetFrameworks>`:
     `net10.0-windows10.0.22621.0` for `win-*`, `net10.0` otherwise (the csproj needs both to ship Linux).
   - `<Data Source>` — app first, `../AuroraEngine/Data` after: **the first source holding a relative path wins.**
   - `<Shaders Source>` — the app's own `Shaders/` tree (Carbon's carries only the UI path's four).
   - `<Loose Include>` / `<Exclude Include>` — globs relative to the Data root, `**` across folders.
2. The app's csproj gets the two lines Thorium's has, or the exe carries no keys and refuses the archive:
   ```xml
   <ItemGroup>
     <AssemblyMetadata Include="ArxKey" Value="$(ArxKey)" />
     <AssemblyMetadata Include="ArxPublicKey" Value="$(ArxPublicKey)" />
   </ItemGroup>
   ```
3. Ship, check, run the tests — as above with the app's paths.

`_Build` needs no project reference to the app; the builder publishes by path.

## Traps

- **The key file is the project's, forever.** `%APPDATA%\Arktis\BuildKeys\<App>.keys.axml` is created on the
  first build (`WARN … created build keys …`) and reused after. Never delete, move or regenerate it: archives
  built with another key set cannot be read by apps already installed, and cannot be patched. It lives
  outside the repo and needs a backup off the machine. A `created build keys` warning on an app that has
  shipped before means the file went missing — stop and say so.
- **Never commit `Ship/` or a key file**, and never paste a key's contents anywhere.
- **XML loses its comments and processing instructions** in the archive. Anything that reads a comment at run
  time breaks only in the shipped build.
- **`XML/Schemas/**` is always excluded** — a packed app skips XSD generation (`Paths.packed`).
- **A packed path cannot be written.** Font/icon import (off in Release anyway) and `--test-approve` goldens do
  not work from `Ship/`; approve goldens from a Debug run.
- **`FATAL [Arx] cannot mount the app's archives`, exit 1** — the signature, table or key did not check out: a
  tampered or truncated `.arx`, or an exe published with a different key set than the archive beside it.
  Rebuild both together.
- **Building `_Build` regenerates `NAMESPACES.md`** (its post-build step). A diff there after a ship is that,
  not a stray edit.
