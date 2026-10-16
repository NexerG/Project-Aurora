# Decision — the in-engine test suite runs on Linux through per-site Win32 guards, not a platform layer

**Date:** 2026-10-16
**Scope:** `ArctisAurora.EngineWork.Rendering` — `AGlfwWindow`, `TouchpadInput`, `DisplayNames`; `ArctisAurora.EngineWork` — `Engine`; `ArctisAurora.Core.Filing` — `ClipboardImage`, `FileDialog`; `ArctisAurora.Core.Filing.Serialization` — `Paths`, `MeshImporter`; `Registry.Assets.FontAsset`; `ArctisAurora.Tests.BackgroundTests`; `_Build/test.sh`

## What changed
- Thorium's `--test` suite builds and runs on Linux (Ubuntu 24.04, cloud container). Every Win32 P/Invoke the boot and test paths reach is behind `if (!OperatingSystem.IsWindows()) return …`.
- `AGlfwWindow`: `RoundCorners`, `AllowSnapping`, `Raise`, `DragByCaption` no-op off Windows; `SetBounds` goes through `glfw.SetWindowPos` + `SetWindowSize`; `GetPlacement` returns the GLFW pos/size (with the maximized flag).
- `TouchpadInput.Register` no-op. `DisplayNames.At` returns null (falls back to GLFW's monitor name; also keeps its WinRT `DisplayMonitor` type unloaded). `ClipboardImage.TryGet` returns false. `FileDialog.Pick` returns without opening (the callback never fires).
- `Engine()` logs `Environment.ProcessId` instead of kernel32 `GetCurrentThreadId` off Windows (Linux main-thread tid == pid).
- `Paths`: the `Data\\…` constants now use `/` (`GetPath("Data/XML")` etc.); `FontAsset` atlas paths join with `/`.
- `MeshImporter`: new static constructor; off Windows it calls `NativeLibrary.SetDllImportResolver(typeof(AssimpContext).Assembly, …)` mapping `"libdl.so"` to `NativeLibrary.Load("libdl.so.2")`.
- `_Build/test.sh`, `uname -s` = Linux: builds `Thorium/Thorium.csproj` alone with `-p:EnableWindowsTargeting=true`, runs `Thorium` (no .exe) under `xvfb-run -a -s "-screen 0 1920x1080x24"` with `VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json`. On both platforms the run's ERROR/FATAL lines go through `tr -d '\r'` before the baseline compare (the log writes CRLF; only the baseline side was stripped before, so every baseline error read as new on Linux).
- `AssetImporter.HashFiles` drops `\r` bytes from each source before hashing, so an icon or font stamp is the same on a CRLF (Windows) and an LF (Linux) checkout.
- Minimize-to-tray gating, found on the way: [[tray-availability-gate]].

## Why these choices

**Each Win32 site gets an `OperatingSystem.IsWindows()` guard rather than a platform-abstraction layer.**
It is a JIT-time constant: zero cost on Windows, and the Windows paths stay byte-identical. The goal was tests running on Linux, not a Linux product, so an abstraction would be speculative.

**The AssimpNet `libdl` fix is a resolver in `MeshImporter`'s static constructor.**
Rejected: upgrading to AssimpNet 5.0.0-beta1 — its package was inspected and its loader still P/Invokes the unversioned `"libdl.so"`, so it fixes nothing. Rejected: a container symlink `libdl.so` to `libdl.so.2` — every Linux machine would need it. The resolver lives in `MeshImporter` because `MeshImporter.ImportFBX` is the only `new AssimpContext()` in the engine. It loads on glibc 2.34 and later.

**Data paths use `/`.**
`Path.GetFullPath` normalizes `/` to `\` on Windows, so the resolved values there are unchanged; on Linux `\` is a filename character, not a separator.

**Import stamps hash with `\r` stripped, for every source (icons and fonts).**
`* text=auto` checks SVGs out LF on Linux and CRLF on Windows, and a raw-bytes hash made each platform rewrite the other's stamp. Rejected: `*.svg text eol=crlf` in `.gitattributes` — existing Linux clones would need a re-checkout. Rejected: stripping for icons only — the user chose one rule for all of `HashFiles`. Cost: every existing font and icon stamp changes once, so the next Windows run re-bakes every font atlas once (the committed atlas PNGs may change then).

**Container provisioning is environment, not repo.**
`apt install dotnet-sdk-10.0 mesa-vulkan-drivers libvulkan1 vulkan-tools libglfw3 xvfb vulkan-validationlayers libminizip1t64`.
- builds.dotnet.microsoft.com is blocked by the cloud proxy; Ubuntu's archive carries dotnet-sdk-10.0 (10.0.112).
- Validation layers are required because Debug enables `VK_LAYER_KHRONOS_validation` and instance creation fails without it (ErrorLayerNotPresent).
- `libminizip1t64` is a dependency of AssimpNet 4.1.0's bundled `libassimp.so`.
- Lavapipe (Mesa 25.2.8, Vulkan 1.4) meets the renderer's 1.3 floor ([[renderer-minimum-requirements]]).

## Verification
Test-verified on Linux: `bash _Build/test.sh` gave 275 passed, 52 failed, 1 skipped, 0 new; no `[Vulkan]` lines; no new error kinds. The 52 failures are `Boot` (the pre-existing baseline sampler error `failed to load the default sampler asset`, same as Windows) plus 51 golden-image mismatches (lavapipe rasterization vs goldens approved on a Windows GPU). `Background.HideShow`, `MenuClamp` and `ClosePrompt` pass. The final run, after the `HashFiles` change, gave 274 passed, 53 failed — the extra one `Notes.LocalWins` (see Known gaps). Windows: NOT run — builds clean on Linux only; Windows behaviour rests on every Windows branch being unchanged.

## Known gaps
- 51 goldens fail on lavapipe; there are no Linux goldens or tolerance.
- On Linux: the title bar does not drag the window (`DragByCaption` no-op; `TitleBarControl` still posts its synthetic button-up, so GLFW's real release arrives as a second up — believed harmless, unverified); Raise-without-focus is a no-op; maximized placement saves the maximized rect, not the restore rect; image paste and file/folder pickers do nothing; no tray, no single-instance claim; console output is uncoloured (`ConsoleSink`'s try/catch fallback).
- `Notes.LocalWins` failed once in a full Linux run (`XmlException`, a torn note file) and passed 3/3 alone. Suspected: the test's outside write races the session's save, and Linux file locks are advisory where Windows `FileShare` blocks a mid-write read. Unconfirmed.
- The full solution build fails on Linux: `_Build.csproj` post-build runs `GenerateNamespaces.cmd`; `test.sh` sidesteps it by building Thorium only.
- Carbon and AuroraEditor have not run on Linux. There is no SessionStart hook to provision the container.

Related: [[tray-availability-gate]], [[engine-testing]], [[renderer-minimum-requirements]], [[desktop-stickies]]
