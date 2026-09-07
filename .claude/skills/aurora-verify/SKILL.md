---
name: aurora-verify
description: Verify a change to Aurora and work out which verification claim is honest. Use before reporting a change as done or working, before calling something an engine defect, and whenever driving the app with synthetic input or screen capture — the capture and input paths here have both produced false results before.
---

# Verifying a change

There is no automated test suite. The user tests manually, and the test/profiling platform is scheduled after
the text editor's first version. So the only honest claim is the one matching what was actually done, and the
words below are the ones the WIP list already uses.

## The ladder — claim the rung you reached, not the one above

| Claim | Means | How |
|---|---|---|
| **builds clean** / **compile-verified** | it compiles | `dotnet build` |
| **boot-verified** | the app started and got through bootstrap | launch it, watch for the throw |
| **GUI-verified** | someone looked at the window and saw the behaviour | capture, or the user looked |
| **NOT GUI-verified** | the honest default | say it out loud |

`NOT GUI-verified` is not a failure state and is written all over the WIP list. Claiming a rung you did not
reach is the failure state.

Bootstrap gives one check for free: an action name in `AuroraEngine/Data/XML/Documents/Bootstrap.bootstrap.xml`
or a host's `Data/XML/Documents/Inputs/InputMap.inputs.xml` that resolves to nothing throws at
`InputHandler.LoadInputs`, so a clean boot proves every declared action exists.

## Build

```bash
cd "C:/Projects-Repositories/Aurora/Project-Aurora" && dotnet build AuroraEngine/ArctisAurora.sln
```

Nothing in the build compiles or validates shaders — a `.spv` change is invisible to it. See `shader-pipeline`.

## Launching

`Paths.GetPath` resolves `..\..\..` against the **process working directory**, so the exe only starts
correctly from its own output folder. `dotnet run` from the repo root sends it looking in `C:\Data\` and it
fails in a way that looks like missing assets.

Three hosts boot the engine, each with its own `Data/` beside its exe, so "the app" means naming one:
`Thorium/bin/Debug/net10.0-windows10.0.22621.0/Thorium.exe`, and the same path shape for `Carbon.exe` and
`AuroraEditor.exe`. `ArctisAurora.exe` sits in those folders too — it is the engine assembly, not the host.

## Capture

Three things that do not work, so they are not worth retrying:

- `CopyFromScreen` loses to whatever owns the foreground — usually the Claude window.
- `SetForegroundWindow` from a background shell is blocked by Windows' foreground lock.
- `PrintWindow` comes back blank, because the surface is Vulkan-rendered.

What works: `SetWindowPos(hwnd, HWND_TOPMOST, …, 0x43)` → capture → `SetWindowPos(hwnd, HWND_NOTOPMOST, …)`.

**A structural dump is not a visual check.** A control-tree dump of parents, arranged rects and glyph counts
verifies geometry and wiring and says nothing about what is drawn. This exact substitution was reported as
"verified" on 2026-08-07 while the window was mostly white — a `StackPanelControl` had inherited the default
`maskAsset` and was painting a solid quad under white text. If capture fails, say the visual check did not
happen. Do not reuse the word for a proxy.

Corollary: **a new container in a draw path needs its `maskAsset` checked.** The default paints; opting out is
explicit and per-control (`WindowControl`, `TextControl`, `TextBlockControl`, `DocumentEditorControl`,
`DocumentControl` all set `"invisible"`).

## Synthetic input

`SetCursorPos` + `mouse_event` for the pointer, `[System.Windows.Forms.SendKeys]` for characters.

Three traps, each of which has already produced a wrong conclusion:

- **`Engine.HandleUI` returns immediately while `UICollisionHandling.isInWindow` is false.** Input that leaves
  the pointer outside the window makes the *next* drag silently do nothing, which reads as a broken feature.
  Move the cursor inside and let a tick pass before driving anything.
- **`SetWindowPos` from another process is not a resize.** It produces a `WM_SIZE` whose 16-bit `HIWORD` is
  `0xFFFF`, so GLFW reports `900x65535`, Vulkan is honestly asked for a 900x65535 swapchain, and honestly
  returns `ErrorOutOfDeviceMemory`. `MoveWindow`, `ShowWindow(SW_MAXIMIZE)` and a real pointer drag all resize
  it correctly.
- **`Text.Write` is bound `<Continuous/>`.** `SendKeys` steals focus, the key's release lands on another
  window, and the bind keeps firing — pouring characters into whatever note has focus. Drive text with
  explicit key-up, and restore any note the probe wrote to.

## Before calling something an engine defect

- **"The unmodified build does it too" proves only that your change is not the cause.** It is not evidence the
  engine is at fault — the harness is unmodified in both runs too.
- **Reproduce through a second, independent input path** before reporting it. `MoveWindow` vs `SetWindowPos`,
  or the app's own chrome.
- **Probe the values before naming a cause.** Two rounds of reasoning about `RecreateSwapchain` produced
  nothing; one print of the requested extent produced the answer immediately.
- CLAUDE.md fences off the Vulkan internals. Going in needs evidence that holds.

## If a claim turns out to be wrong

Retract in **every** place it was made — the WIP list and each decision note that repeated it, not just the
newest file. A false premise the user has already answered a question against is the expensive failure here.
