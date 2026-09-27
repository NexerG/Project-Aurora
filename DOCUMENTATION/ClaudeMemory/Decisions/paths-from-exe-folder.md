# Decision — data and shader paths resolve against the exe's folder, not the working directory

**Date:** 2026-09-27
**Scope:** `ArctisAurora.Core.Filing.Serialization` — `Paths.GetPath`; `ArctisAurora.EngineWork.Rendering` —
`GraphicsPipeline.ReadFile`, `RenderingModule.ReadFile`, `Pathtracing.ReadFile`, `RadianceCascades2D.LoadShader`

## What changed
- `Paths.GetPath` joins `../../../<path>` onto `AppContext.BaseDirectory` in Debug instead of the working directory.
  Release is unchanged — it already used `AppContext.BaseDirectory/<path>`.
- The four shader readers join a relative path onto `AppContext.BaseDirectory` before reading. The 16
  `"../../../Shaders/…"` call sites are unchanged.
- An app starts from any working directory — its bin folder, the repo root, `dotnet run`.

## Why these choices

**Fixed where the files are read, not by moving the working directory (user, 2026-09-27).**
**Rejected:** `Directory.SetCurrentDirectory(AppContext.BaseDirectory)` at startup — one line, but process-wide state
that would also move any relative path passed on the command line.

## Consequences to hold on to
- Setting the working directory no longer redirects `Paths`. A throwaway harness that did so
  ([[xsd-generator-cross-category]]'s 2026-07-30 verification) now resolves against its own exe's folder.
- The rule that put `Shaders/` in every app still holds: `../../../Shaders` is relative to each app's own folder.

## Verified (2026-09-27, Debug)

| Launch | Result |
|---|---|
| `Thorium.exe --test`, working directory = repo root | same results as from the bin folder |
| `dotnet run --project Thorium/Thorium.csproj -- --test` | same |
| `Carbon.exe --test`, working directory = repo root | boots, tests pass |

Related: [[carbon-frame-viewer]], [[xsd-generator-cross-category]], [[dev-console]]
