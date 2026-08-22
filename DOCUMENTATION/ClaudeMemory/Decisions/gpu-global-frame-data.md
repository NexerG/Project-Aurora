# Decision — the renderer owns a global frame-data set at set 0; modules keep their own

**Date:** 2026-08-22
**Scope:** `ArctisAurora.EngineWork.Rendering` — `Renderer`, `RenderingModule`, `UIModule`, `MCUI`,
`Shaders/UIRasterizer/UI.vert`, `UI.frag`

## What changed

- New `GpuEngineStats` struct (in `Renderer.cs`, alongside where `UBO` sits in
  `AVulkanBufferHandler.cs`): `mainTickMs`, `physicsTickMs`, `renderTickMs`, `totalTime`,
  `wrappedTime`, `frameIndex`. Six 4-byte scalars, so std140 and `LayoutKind.Sequential` agree at
  offsets 0/4/8/12/16/20 — verified against `spirv-dis`.
- `Renderer` gained a **global descriptor set**: `globalSetLayout`, `globalSets[]`, one
  `engineStatsBuffers[i]` per swapchain image, host-visible and **mapped once** for the life of the
  process. Built in the existing `Renderer.PrepareDescriptors` bootstrap step — no new
  `Bootstrap.xml` step.
- `Renderer.UpdateGlobalBuffers(window, imageIndex)` runs inside `Draw`, after the timeline wait and
  `AcquireNextImage`, before any module work.
- `RenderingModule.UpdateFrameData(int imageIndex)` — new virtual, empty default. `Draw` no longer
  reaches into `modules[i].camera`; `UIModule` overrides it and updates its own camera matrix.
- **Set numbering shifted.** Global is set 0. `UIModule`'s two sets became 1 and 2, in the pipeline
  layout, in `MCUI.EnqueueDrawCommands`'s `firstSet` arguments, and in both UI shaders.

## Why these choices

**One buffer per swapchain image, not a ring keyed on the frame counter.**
Module command buffers are recorded **only when dirty** (`UIModule.WriteCommandBuffers`), not every
frame, so whatever set a record binds is the set that image keeps using for many frames. A ring
indexed by frame would need a re-record or a descriptor rewrite every frame, which is the whole
design being thrown away. Per **image** satisfies both halves: the descriptor for image *i* always
points at buffer *i* so it never needs rewriting, and `AcquireNextImage` will not hand back an image
that is still in flight, so the slot being written is not being read.

**Mapped host-visible memory, not the staging path.**
`AVulkanBufferHandler.EndSingleTimeCommands` does `QueueWaitIdle` **and** `DeviceWaitIdle`. The
camera already pays one of those per frame per window; routing a per-frame stats write through
`UpdateBuffer` would have added a second full device stall per frame to publish 24 bytes.

**A fixed ceiling of 8 images, not a window's `imageCount`.**
The resource is device-wide and outlives every window. Sizing it to the primary window's count would
mean rebuilding it from `ResizePerImageResources` when a present-mode change alters that count —
which destroys sets that *other* windows' recorded command buffers still reference. A ceiling is
never rebuilt. Exceeding it throws `IndexOutOfRangeException` at the write, which is loud.

**`ShaderStageFlags.All` on the binding.** Stage flags are baked into the layout, and changing them
means recreating the layout, every pipeline layout built from it, and every pipeline. One flag value
now is cheaper than that later.

**Global at set 0 rather than appended last** (user, 2026-08-22). Conventional — set 0 changes least
often — and the renumbering cost is at its floor while only two shader files use set numbers.

## Known gaps

- **The compositor does not bind the global set.** Its own set stays 0, so set 0 means the global
  data in a module pipeline and the module-output sampler array in the compositor pipeline. Legal
  (set numbers are local to a pipeline layout) but inconsistent. Wiring it is ~4 lines plus a
  renumber of `compositor.frag`.
- **Multi-window shares the slots.** Two windows drawing the same image index write the same buffer,
  so the per-image guarantee above only holds within one window. The content is identical global
  data, so the worst case is a torn read between two structs a few hundred µs apart.
- `frameIndex` is `RenderWindow.frameCounter`, which `CreateSyncObjects` starts at
  `MAX_FRAMES_IN_FLIGHT` (2), not 0. It is also that window's counter, not a renderer-wide one.
- `totalTime` as a float reaches 1 ms per ulp at ~4.6 hours of uptime. `wrappedTime`
  (`totalTime % 1024`) exists for anything driving animation; `totalTime` is for display.
- Nothing is destroyed at shutdown — consistent with the instance, device and command pools, none of
  which the renderer tears down either.
- **Not GPU-verified.** Builds clean, the SPIR-V decorations and member offsets were checked, but no
  shader reads the block yet and the numbers have not been observed arriving on the device. User is
  checking in Nsight.

Related: [[render-window-owns-the-swapchain]], [[dynamic-rendering]], [[engine-resource-manager]],
[[swapchain-extent-is-the-truth]]
