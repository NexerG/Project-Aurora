# Decision — per-frame buffers are mapped and per-image, staging is for upload-once data

**Date:** 2026-08-22
**Status:** LANDED and GPU-verified — run by the user 2026-08-22, UI renders correctly. Engine and
Periodic build clean, 0 errors, warning count unchanged at 450.
**Scope:** `ArctisAurora.EngineWork.Rendering.Helpers` (`AVulkanBufferHandler`),
`ArctisAurora.EngineWork.Rendering` (`Renderer`, `AuroraCamera`),
`ArctisAurora.EngineWork.Rendering.MeshSubComponents` (`MCUI`),
`ArctisAurora.EngineWork.Rendering.Modules` (`UIModule`).

## What was already true, and wasn't documented

Every per-frame buffer write went through `BeginSingleTimeCommands`/`EndSingleTimeCommands`, and
`EndSingleTimeCommands` ended with `vkQueueWaitIdle` **and `vkDeviceWaitIdle`**. Per frame per window
that was 3 staging allocations, 3 queue submits, 5 queue waits and 2 full device stalls — inside
`Renderer.Draw`, after the timeline wait and after `AcquireNextImage`. `MAX_FRAMES_IN_FLIGHT = 2`
was decorative; the device drained twice per frame.

Worse, the copies bought nothing. `CreateBuffer<T>` passed `defaultStagingMemoryFlags`
(`HOST_VISIBLE | HOST_CACHED`) for the **destination** as well as the staging source, so every
transfer was host memory → transfer queue → host memory. Nothing in the engine lived in device-local
memory at all. `HOST_CACHED` is also the readback flag; for write-only upload data it asks for the
wrong type, and the absent `vkFlushMappedMemoryRanges` was only safe because the first matching type
on the test hardware happens to also be `HOST_COHERENT`.

**Still open:** `Renderer.CreateLogicalDevice` hardcodes `QueueCreateInfoCount = 2`, one graphics and
one transfer `DeviceQueueCreateInfo`. `QueueAllocator` resolves each flag to the family with the
fewest extra bits, so a GPU with a single universal family — most Adreno and Mali — resolves both to
index 0, and duplicate family indices in `pQueueCreateInfos` are invalid. Blocks Android at
`vkCreateDevice`. Logged in the WIP list as an oversight.

## Decisions

### 1. The axis is write frequency, not buffer type

The first proposal was one prefab for "3D model stuff" (vertices, indices) and another for "SSBOs
and storage buffers" (user, 2026-08-22). Rejected because `MCUI.gradientBuffer` is a storage buffer
that is written **once** at bootstrap from XML and never touched again — by type it groups with the
SSBOs, by access pattern it is identical to vertex data. Splitting on buffer type would park the
gradient table in BAR space forever and give up device-local read speed for nothing, and the same
trap waits on the other side the first time a mesh wants a storage buffer.

| Prefab | Flags | Used by |
|---|---|---|
| `staticDeviceMemoryFlags` | `DEVICE_LOCAL` | vertices, indices, gradient table, textures |
| `streamingMemoryFlags` | `DEVICE_LOCAL \| HOST_VISIBLE \| HOST_COHERENT` | transforms, control data, camera UBO, engine stats |
| `streamingMemoryFlagsFallback` | `HOST_VISIBLE \| HOST_COHERENT` | the above, where the preferred type does not exist |
| `defaultStagingMemoryFlags` | `HOST_VISIBLE \| HOST_COHERENT` | staging sources |

Usage-flag prefabs stay keyed on buffer type, where they already were; `storageBufferFlags` joins
`vertexBufferFlags`/`indexBufferFlags`/`raytracingBufferFlags` to complete the set.

### 2. Preferred/required memory type, so no code ever asks whether ReBAR is on

`FindMemoryType` gained a `(typeFilter, preferred, required)` overload that scans for the preferred
flag set and delegates to the single-flag version on miss. `CreateBuffer` gained the matching
overload; the old single-flag entry point forwards with `preferred == required`, so no existing call
site changed.

This is what makes one code path cover all three targets. On Android and Apple Silicon the preferred
set matches immediately because unified memory reports every type as device-local and host-visible.
On a discrete PC with Resizable BAR it matches the mappable VRAM type. Without ReBAR it matches the
256 MB legacy BAR window, or falls back to system DRAM when that is exhausted. **No capability
check, no branch, no `#if` anywhere in engine code** — the driver's type list is the only input.

Test hardware (RTX 3080, `vulkaninfo`): heap 0 is 9.81 GiB device-local, heap 1 is 30.8 GiB system.
Type 5 is `DEVICE_LOCAL | HOST_VISIBLE | HOST_COHERENT` on heap 0, and `nvidia-smi` reports
`BAR1 Total = 16384 MiB`, so ReBAR is enabled and the whole VRAM heap is mappable. Note that every
host-visible type reports `IMAGE_TILING_OPTIMAL: None` — **no optimally-tiled image can live in
host-visible memory**, which is why textures must stage on every platform regardless of BAR size.

### 3. One set of buffers per swapchain image

Standing rule (user, 2026-08-22): *if there is a question how many buffers to use, the answer is per
swapchain image.* Sized from `window.imageCount`, not `MAX_SWAPCHAIN_IMAGES` — `frameResources` and
`_cameraBuffer` already use `imageCount`, the mirrors are recreated on pool growth anyway so a fixed
size buys nothing, and 8 images' worth of both columns is 6.5 MB against 2.5 MB for 3, which matters
on mobile.

`MCUI.transformsBuffers`/`controlDataBuffers` are now arrays with parallel `DeviceMemory[]` and
`nint[]` mapped pointers. `MakeInstanced` writes the dirty range straight through
`_transformsMapped[imageIndex]`; the growth path destroys, recreates and re-maps all N and seeds each
with the whole column. `WriteStaticDescriptors` binds `[currentFrame]` instead of the single handle.

### 4. The device stall *was* the synchronization, which is why it could not just be deleted

`UIModule.meshComponent` is `static` — one `MCUI`, one transforms buffer, one control-data buffer,
bound into every image's descriptor set. The per-image bookkeeping (descriptor sets, command
buffers, `PoolCursor`s, `frameResources`) was all correctly N-buffered; the memory underneath it was
not. With `MAX_FRAMES_IN_FLIGHT = 2` the timeline wait at the top of `Draw` waits for frame N-2, so
frame N-1 can still be reading that buffer when frame N copies into it. Nothing tore because
`vkDeviceWaitIdle` drained every queue first.

So decision 3 is load-bearing, not an optimisation: per-image buffers are what makes removing the
stall safe. Doing step 6 without step 3 would have produced tearing under sustained control drag.

### 5. `EndSingleTimeCommands` keeps `vkQueueWaitIdle`, loses `vkDeviceWaitIdle`

After the conversion its only remaining callers are `CopyBufferToImage` and `TransitionImageLayout`,
both reached only from `CreateTextureBuffer`, whose only live caller is `TextureAsset`. That is
load-time work against a freshly created image that nothing in flight can reference, so waiting the
transfer queue is sufficient and draining the device is not.

### 6. What still stages, and why it is not a fallback path

Vertex, index and texture uploads keep the staging + single-time-command path, and their destinations
are now genuinely `DEVICE_LOCAL`. This is not legacy: optimally-tiled images cannot be map-written at
all (decision 2), and for bulk write-once data the transfer queue's DMA engine beats write-combined
CPU writes while leaving BAR space for things that need it. The dividing line is upload-once-read-many
into device-local (stage) against rewritten-every-frame (map).

## What came out

Per frame, per window, with one UI module:

| Removed | Count |
|---|---|
| `vkDeviceWaitIdle` | 2 |
| `vkQueueSubmit` | 3 |
| `vkQueueWaitIdle` | 5 |
| `vkAllocateMemory` + `vkFreeMemory` | 3 each |

The device stalls are the substance. The VRAM placement is second-order and was **not** the reason
for the change: the camera UBO is 128 bytes read once per draw, and both SSBOs together are under a
megabyte against 5 MB of L2 on the test card, so most reads hit cache wherever the backing store
lives. Writing to mapped VRAM also goes over PCIe write-combined, which is slower than a local DRAM
memcpy — the right trade for data read thousands of times per frame, roughly neutral for the camera.
Uniformity was judged worth more than that margin.

Not measured. `GpuEngineStats` already publishes per-system tick times to the shaders and is the
instrument for a before/after, but no capture was taken.

## Traps this introduces

- **Never read back through a mapped pointer.** The target may be write-combined, where a single
  read costs orders of magnitude. `Unsafe.Write` of a whole struct and `WriteMappedRange`'s forward
  `CopyTo` are both safe; `ptr->field = x`, `+=`, or reading a value to compare it are not.
- **`MCUI` is static but the mirrors are sized from one window's `imageCount`.** Two windows with
  different swapchain image counts would index past the array. Not reachable today — every window
  shares a device and surface format so `MinImageCount` is identical — but it is a real edge, and it
  is the same shared-static tension that caused decision 4.
## Known limits

`FindMemoryType` falls back on **type matching, not allocation failure**. On a discrete GPU without
ReBAR the preferred `DEVICE_LOCAL | HOST_VISIBLE | HOST_COHERENT` type still exists, backed by a
256 MB window — the match succeeds, then `vkAllocateMemory` fails past that size and `CreateBuffer`
throws rather than dropping to system memory. Unreachable at today's ~2.5 MB of streaming buffers;
it becomes real the moment this pattern covers anything large.
