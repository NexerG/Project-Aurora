# Decision — textures move from the transfer family to graphics by release/acquire

**Date:** 2026-09-27
**Scope:** `ArctisAurora.EngineWork.Rendering.Helpers` (`AVulkanBufferHandler.UploadTexture`),
`ArctisAurora.EngineWork.Rendering` (`Renderer.QueueAcquire`, `Renderer.RecordAcquires`, `Renderer.Draw`,
`CreateSyncObjects`/`DestroySyncObjects`, `RenderWindow.acquireCommandBuffers`)

## What changed
- `UploadTexture` records `Undefined → TransferDstOptimal`, the copy, and a **release** barrier
  (`TransferDstOptimal → ShaderReadOnlyOptimal`, src transfer family, dst graphics family, dst `BottomOfPipe`/0)
  in **one** command buffer, under `Renderer.transferCommandLock`. Was three submits and three idle waits, unlocked.
- After the submit's `QueueWaitIdle` it calls `Renderer.QueueAcquire(image)`.
- `Renderer.RecordAcquires` swaps the pending list out under `acquireLock` and records one `CmdPipelineBarrier`
  of matching **acquire** barriers (src `TopOfPipe`/0, dst `FragmentShader`/`ShaderRead`) into
  `window.acquireCommandBuffers[window.currentFrame]`.
- `Draw` calls it after the module update loop and before building `modulesSubmit`; when it recorded, that
  buffer is first in the module batch.
- `acquireCommandBuffers` — `MAX_FRAMES_IN_FLIGHT` per window, from `compositeCommandPool`, allocated in
  `CreateSyncObjects(window)`, freed in `DestroySyncObjects(window)`.
- Same family for transfer and graphics → no ownership transfer: the last barrier is an ordinary one to
  `FragmentShader`/`ShaderRead`, nothing is queued.
- Removed `AVulkanBufferHandler.TransitionImageLayout` and `CopyBufferToImage`.

### Buffers (same day, follow-up)
- `AVulkanBufferHandler.CopyBuffer` (behind `CreateBuffer<T>` and `UpdateBuffer<T>`, both overloads) appends one
  `BufferMemoryBarrier` over `WholeSize` in the copy's command buffer: a **release** (transfer → graphics family,
  `TransferWrite` → 0, dst `BottomOfPipe`) or, same family, an ordinary barrier to `Renderer.bufferReadAccess` at
  `Renderer.bufferReadStages`. After the idle wait, `Renderer.QueueAcquire(Buffer)`.
- `Renderer` holds `pendingBufferAcquires`/`recordingBufferAcquires` beside the image lists, under the same
  `acquireLock`. `RecordAcquires` swaps both and records buffer and image acquires in **one** `CmdPipelineBarrier`,
  dst stage `FragmentShader | bufferReadStages`.
- `bufferReadAccess` = `VertexAttributeRead | IndexRead | UniformRead | ShaderRead`; `bufferReadStages` =
  `VertexInput | VertexShader | FragmentShader`. Fixed, not derived from the buffer's usage flags.
- Live path: the `uidefault` UI quad (`AVulkanMesh.Load` → `LoadCustomMesh` → `CreateBuffer`). `UpdateBuffer` has no
  live caller (`Rasterizer`/`Pathtracing` are not running).

## Why these choices

**The old barrier was invalid, and the ownership was silently wrong.**
`ShaderRead` at `AllCommands` on a `TRANSFER|SPARSE` family is `VUID-vkCmdPipelineBarrier-pImageMemoryBarriers-02820`
— the only live validation error across Thorium, Carbon and AuroraEditor on 2026-09-27. Beneath it, textures are
`Exclusive` and were written on the transfer family and sampled on graphics with no transfer, so their contents
were undefined by spec; desktop drivers forgave it. Validation does not report a missing transfer.

**Release/acquire over `SharingMode.Concurrent`.**
Concurrent removes the transfer but can disable compression (AMD) and hides ownership rather than stating it.
Rejected in favour of the explicit pair.

**The acquire is recorded by the Render thread, not submitted by the uploader.**
`compositeQueue` is externally synchronised and only `Draw` submits to it. Having the uploading thread submit
the acquire would need a cross-thread queue lock.

**Drained after module updates, not at the top of `Draw`.**
`TextureAsset.RegisterInTable` runs after `QueueAcquire`, so any texture a module could have written into the
table this frame already has its acquire queued, and it lands in the same submit, ahead of the draws. Whichever
window draws first drains; later windows submit after it on the same queue.

**The host wait orders release before acquire — no semaphore.**
Uploads are synchronous (`QueueWaitIdle`) and happen at bootstrap. The acquire's submit happens after the host
returned from that wait. A transfer timeline the frame waits on is the shape for async uploads, not needed yet.

**`UpdateBuffer` does not transfer ownership back to the transfer family first.**
The WIP entry assumed it needed graphics release → transfer acquire. It does not: a family may take ownership of
an `Exclusive` resource without a transfer when the old contents are not needed, and `UpdateBuffer` overwrites the
buffer from offset 0. Only valid while an update writes the **whole** buffer — a partial update would need the pair.
The real hazard with `UpdateBuffer` is timing (overwriting a buffer an in-flight frame still reads), not ownership.

**A fixed read mask for buffer acquires, not one derived from usage flags.**
Uploads happen at load time, so a wider dst scope costs nothing measurable; deriving it would need the usage passed
through `CopyBuffer` and into the pending list. Compute and ray-tracing consumers would need their stages added.

**Prologue buffers per frame slot.**
The timeline wait at the top of `Draw` guarantees the frame that last used `currentFrame`'s slot is done, so
`BeginCommandBuffer` (implicit reset, the pool has `ResetCommandBufferBit`) is safe. Rebuild and teardown
already run after `DeviceWaitIdle`.

## Verified
- Build clean; no new warnings in the touched files.
- Thorium, Carbon, AuroraEditor booted from bin (sync validation on) and closed normally: zero `[Vulkan]` log lines.
- Thorium GUI-verified: glyph (MTSDF) and icon atlases render.
- Thorium context menu opened and dismissed 3× — each is a new `RenderWindow`, so the per-window alloc/free ran
  by the code path; no log line proves it.
- Buffers: build clean; three hosts `--test`-booted with sync validation, zero `[Vulkan]` lines, error set identical
  to the pre-change baseline (default sampler, `desired stale`). Validation does not report a missing transfer, so
  this proves no regression, not the hand-over. **NOT GUI-verified.**
- Same-family path (textures and buffers): exercised by temporarily forcing `QueueAllocator.Get(TransferBit)` to the
  graphics family, reverted — zero `[Vulkan]` lines, same baseline errors.

## Known gaps
- `TextureAsset._table` is appended by the loading thread and read by Render unlocked. Only bootstrap writes today.
- `UpdateBuffer` can overwrite a buffer an in-flight frame is reading (no fence against the frame timeline).
- A destroyed buffer or image still in a pending acquire list would be barriered after destruction. No path does it
  today (`LoadCustomMesh` destroys only a fresh mesh's null handles).

Related: [[mapped-streaming-buffers]], [[render-window-owns-the-swapchain]], [[glyphs-as-pool-data]], [[device-queue-slots]]
