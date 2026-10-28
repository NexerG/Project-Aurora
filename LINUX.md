# Building and testing Aurora on Linux

Linux runs Thorium's in-engine test suite and headless exports (for example a LaTeX → PDF export) under a virtual
display, on Mesa's software Vulkan driver (lavapipe). No GPU is needed. It is a test and tooling platform, not a
shipped one: Windows-only features (tray, Windows Hello, file pickers, touchpad gestures) do nothing here.

Checked on Ubuntu 24.04 (the Claude Code cloud container) on 2026-10-28.

## Install

```bash
sudo apt-get update
```

```bash
sudo apt-get install -y --no-install-recommends dotnet-sdk-10.0 mesa-vulkan-drivers libvulkan1 vulkan-validationlayers libglfw3 xvfb xauth libminizip1t64
```

| Package | Why |
|---|---|
| `dotnet-sdk-10.0` | The projects target `net10.0` (Linux) and `net10.0-windows10.0.22621.0` (Windows). Ubuntu's archive carries it (10.0.113 at the time of writing); use it where builds.dotnet.microsoft.com is blocked. |
| `mesa-vulkan-drivers` | lavapipe, the CPU Vulkan driver (`/usr/share/vulkan/icd.d/lvp_icd.json`). It meets the renderer's Vulkan 1.3 floor. |
| `libvulkan1` | The Vulkan loader. |
| `vulkan-validationlayers` | Debug builds enable `VK_LAYER_KHRONOS_validation`; without it instance creation fails with `ErrorLayerNotPresent`. |
| `libglfw3` | Windowing, through Silk.NET.GLFW. |
| `xvfb`, `xauth` | A virtual X display, so GLFW has a screen to open windows on (`xvfb-run`). |
| `libminizip1t64` | A dependency of AssimpNet 4.1.0's bundled `libassimp.so`; without it boot dies loading meshes. |

Optional:

```bash
sudo apt-get install -y --no-install-recommends glslc vulkan-tools gdb
```

```bash
pip install pypdf pdfplumber
```

- `glslc`: only when a shader changes (see the `shader-pipeline` skill: compile with `--target-env=vulkan1.3`, copy to all four trees).
- `vulkan-tools` (`vulkaninfo`) and `gdb`: diagnosing the driver or a native crash.
- `pypdf` / `pdfplumber`: reading an exported PDF's text and glyph positions to compare it against a reference PDF.

## Build and test

```bash
bash _Build/test.sh
```

```bash
bash _Build/test.sh Tex
```

On Linux `test.sh` builds `Thorium/Thorium.csproj -f net10.0 -p:EnableWindowsTargeting=true` alone and runs
`Thorium/bin/Debug/net10.0/Thorium --test` under `xvfb-run` with `VK_ICD_FILENAMES` pointing at lavapipe. The full
log is `/tmp/aurora-test/run.txt`.

Expect these failures, none of them a regression:

- **Boot**: the pre-existing "failed to load the default sampler asset" error, the same on Windows.
- **About 54 golden images**: the goldens were approved on a Windows GPU, and lavapipe rasterizes edges slightly differently. Read only the non-golden `FAIL` lines.
- **`Sheet.PasteLinks`, `Sheet.NotePasteLinks`**: they fail on Windows too.

## Headless export

A host opened on a file can be driven by console commands:

```bash
VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 1920x1080x24" Thorium/bin/Debug/net10.0/Thorium /path/to/doc.tex --exec "Wait 3000; Focus; Tex.ExportPdf; Wait 3000; Quit"
```

The PDF lands beside the `.tex`.

## When the run crashes in the driver

Lavapipe segfaults on a shader read outside a descriptor array, where a GPU silently returns garbage. If a run dies
with no `[Crash]` line and a native backtrace ends in an `llvmpipe` thread, run it once with GPU-assisted validation.
It names the draw, the binding and the out-of-range index as a `[Vulkan]` line instead of crashing:

```bash
VK_LAYER_ENABLES=VK_VALIDATION_FEATURE_ENABLE_GPU_ASSISTED_EXT VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 1920x1080x24" Thorium/bin/Debug/net10.0/Thorium --test=Tex
```

## Not covered

- Carbon and AuroraEditor have not been run on Linux; only Thorium builds for `net10.0`.
- There is no SessionStart hook; a fresh cloud container needs the install step above once.

Details and the reasoning behind the Linux port: `DOCUMENTATION/ClaudeMemory/Decisions/linux-test-port.md`.
