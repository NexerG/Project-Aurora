# Decision — GPU-side sizes come from `Renderer.swapchainExtent`, never `Engine.window.windowSize`

**Date:** 2026-08-18
**Status:** LANDED. Reproduced and fixed under an automated resize probe — pre-fix died after 200
one-pixel `SetWindowPos` steps, post-fix survived 2400 with zero validation output.
**Scope:** `ArctisAurora.EngineWork.Rendering` (`Renderer`), `ArctisAurora.EngineWork.Rendering.Modules`
(`RenderingModule`, `UIModule`, `CompositorModule`).

## The crash

Dragging a window edge killed the render thread with

```
System.Exception: failed to submit single time commands
  at AVulkanBufferHandler.EndSingleTimeCommands
```

which is a red herring — the submit fails because the device is already lost. The cause is one
validation error earlier:

```
VUID-VkRenderingInfo-pNext-06079
vkCmdBeginRendering(): pColorAttachments[0].imageView width (1281) is less than
renderArea.offset.x (0) + renderArea.extent.width (1282)
```

## Decisions

### 1. The window size and the swapchain images are two different clocks

`CompositorModule.WriteCommandBuffer` attached `swapchainImageViews[index]` — sized whenever the
swapchain was last built — and set `RenderArea` from `Engine.window.windowSize`, which is live. The
two are written by different threads at different times:

- `AGlfwWindow.WindwoResizeCallback` writes `windowSize` from the **main** thread, during `PollEvents`.
- `Renderer.Draw` → `WriteCommandBuffer` reads it from the **render** thread, which since the
  threading rework runs free and never waits on main (`ThreadedSystem` / `RenderSystem`).

So a resize landing between the last `RecreateSwapchain` and the next command-buffer record produces
a render area one pixel wider than the image it renders into. One pixel is enough: the GPU faults, the
device is lost, and the next unrelated `QueueSubmit` — the single-time upload in `MCUI.MakeInstanced`
— is what actually throws.

`Renderer.swapchainExtent` is now the single source. `CreateSwapchain` stores what the swapchain was
actually built at, and everything sized to it reads that back: both modules' `RenderArea`, viewport
and scissor, `RenderingModule.CreateOutputImages`, and the camera projection in `Draw`. The two clocks
cannot drift because there is now only one.

### 2. The extent comes from the surface, not from the window

`ImageExtent = Engine.window.windowSize` was also a spec violation in its own right, and the probe
caught it as a second VUID:

```
VUID-VkSwapchainCreateInfoKHR-pNext-07781   (imageExtent must equal currentExtent)
```

On Win32 the surface always reports a real `currentExtent`, so the driver was quietly building images
at a size we never asked for — a second, independent way for the attachment and the render area to
disagree. `ChooseSwapchainExtent` honours `currentExtent` when the surface reports one and falls back
to the window size clamped to `minImageExtent`/`maxImageExtent` when it reports `0xFFFFFFFF`.

### 3. UI layout stays on the live window size

`WindowControl.FitTo(Engine.window.windowSize)` and `EntityRegistry.uiTree` were left alone. Layout is
CPU-side and wants the window it is being laid out for; a frame laid out at 1282 rendered into a 1281
target loses one column for one frame and is corrected by the recreate that follows. Only GPU-side
extents were moved.

## Known-adjacent, not touched

- **`EndSingleTimeCommands` never checks its waits.** The guard reads
  `if (rRueue != Success && rRueue != Success && rRueue != Success)` — the same variable three times,
  `&&` where `||` was meant. `rQueueWait` and `rDeviceWait` are computed and discarded, so a failing
  `QueueWaitIdle`/`DeviceWaitIdle` passes silently. Left as-is: correcting it makes the method throw
  in more places, which is a separate call.
- **`Renderer.swapchainImageCount` is hardcoded to `3`.** `CreateSwapchain` asks for
  `MinImageCount + 1` and reads the real count into a local, so `swapchainImages` can be longer than
  every array sized off the constant (`isDirty`, `renderFinishedSemaphores`, module command buffers).
  Nothing observed it on this GPU; it is an index-out-of-range waiting for a driver that hands back
  four images.

Related: [[dynamic-rendering]], [[cross-system-change-notification]], [[window-frame-resize]]
