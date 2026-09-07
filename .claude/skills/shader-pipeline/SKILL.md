---
name: shader-pipeline
description: Compile and mirror Aurora's GLSL shaders across the four project trees. Use whenever a shader under Shaders/ is edited or added, a .spv needs rebuilding, or a change touches ControlData layout or descriptor set numbering — the SPIR-V ships in four copies that must stay byte-identical.
---

# Shader pipeline

`AuroraEngine/Shaders/` is the source of truth. `Thorium/Shaders/` and `AuroraEditor/Shaders/` are full mirrors.
`Carbon/Shaders/` mirrors only the UI path — `Modules/Compositor/compositor.*`, `UIEngine/UIEngine.*` and
`UIRasterizer/UI.*`, source and `.spv` — so the files it lacks are by design, not drift. Never edit a mirror directly.

The loop: **edit the engine copy → compile → mirror source *and* `.spv` to every tree → confirm byte-identical.**

CLAUDE.md says to mirror the `.spv`. Mirror the GLSL source too — mirroring only the binary has already let three
sources fall cosmetically out of step while every `.spv` stayed identical, so nothing looked wrong.

## Compile

`glslc` is on PATH (`$VULKAN_SDK/Bin/glslc`, currently `C:\Program Files (x86)\Vulkan`).

One shader — the usual case:

```bash
cd "C:/Projects-Repositories/Aurora/Project-Aurora/AuroraEngine/Shaders" && glslc --target-env=vulkan1.3 UIRasterizer/UI.vert -o UIRasterizer/UI.vert.spv
```

All nineteen:

```bash
cd "C:/Projects-Repositories/Aurora/Project-Aurora/AuroraEngine/Shaders" && for s in Modules/Compositor/compositor.vert Modules/Compositor/compositor.frag PathtracingShaders/closesthit.rchit PathtracingShaders/miss.rmiss PathtracingShaders/raygen.rgen PathtracingShaders/shadows.rmiss RadianceCascades2D/Radiance.Drawing.comp RadianceCascades2D/Radiance.LayerCompute.comp RadianceCascades2D/Radiance.Phosphorus.comp RadianceCascades2D/Radiance.Probes.comp Shadowmap.vert Shadowmap.frag UIEngine/UIEngine.vert UIEngine/UIEngine.frag UIRasterizer/UI.vert UIRasterizer/UI.frag vulkan.vert vulkan.frag; do glslc --target-env=vulkan1.3 "$s" -o "$s.spv" || echo "FAILED $s"; done; glslc --target-env=vulkan1.3 RadianceCascades2D/Radiance.compute.comp -o RadianceCascades2D/Radiance.comp.spv || echo "FAILED Radiance.compute.comp"
```

`--target-env=vulkan1.3` is not optional. It is what emits SPIR-V 1.6, which is what every shipped `.spv` here
is. Drop it and you get an older SPIR-V that fails to load at pipeline creation, well away from the edit.

## Mirror

```bash
cd "C:/Projects-Repositories/Aurora/Project-Aurora/AuroraEngine/Shaders" && find . -type f ! -name '*.png' | while read f; do cp "$f" "../../Thorium/Shaders/$f"; cp "$f" "../../AuroraEditor/Shaders/$f"; if [ -f "../../Carbon/Shaders/$f" ]; then cp "$f" "../../Carbon/Shaders/$f"; fi; done
```

## Verify — prints only mismatches

```bash
cd "C:/Projects-Repositories/Aurora/Project-Aurora" && for f in $(cd AuroraEngine/Shaders && find . -type f ! -name '*.png' | sort); do s=ok; cmp -s "AuroraEngine/Shaders/$f" "Thorium/Shaders/$f" || s="DIFF-Thorium"; cmp -s "AuroraEngine/Shaders/$f" "AuroraEditor/Shaders/$f" || s="$s DIFF-Editor"; if [ -f "Carbon/Shaders/$f" ]; then cmp -s "AuroraEngine/Shaders/$f" "Carbon/Shaders/$f" || s="$s DIFF-Carbon"; fi; [ "$s" = ok ] || echo "$s  $f"; done; echo "--- comparison done ---"
```

Silence between the command and `--- comparison done ---` is a pass.

## Traps

- **`Radiance.compute.comp` compiles to `Radiance.comp.spv`.** The only source whose output is not its own
  name plus `.spv`. A naive loop produces `Radiance.compute.comp.spv`, which nothing loads, and leaves the
  real `.spv` stale — a shader that silently keeps running old code.
- **`Default.vert/.frag` and `Light.vert/.frag` have no `.spv` and nothing loads them.** Dead sources. Do not
  compile them, do not delete them (CLAUDE.md §4 — mention, don't clean up).
- **The Carbon leg of the mirror copies only what Carbon already carries.** The `[ -f … ]` guard is what keeps
  the other 30 files out of its tree, and it cannot tell "not wanted" from "not there yet" — a shader Carbon
  starts needing has to be created there once by hand, or it is silently skipped on every mirror after.
- **Set 0 belongs to the renderer**, holding `GpuEngineStats`. A module's own sets start at 1. Renumbering a
  set means the shader, the pipeline layout, and the `firstSet` argument in that module's `EnqueueDrawCommands`
  all move in the same commit. The compositor is the exception — its own set is still 0. See
  `ClaudeMemory/Decisions/gpu-global-frame-data.md`.
- **`UI.vert`'s instance block is `scalar` layout on purpose** — it gives a 136-byte stride matching the
  `Pack=1` C# `ControlData` exactly. Switching it to `std430` shifts every control past the first.
- **A `.spv` change is invisible to `dotnet build`.** Nothing compiles or validates shaders during the build,
  so the only proof is the byte-identical check above plus a run.

## Cost, before reaching for a shader change

A shader edit is four trees, a recompile and a mirror — which is why the caret blink was done on the CPU tick
rather than from the frame time already in set 0 (user, 2026-08-23). If a change *can* be made control-side,
weigh that first.
