# Decision — each queue role gets a (family, index) slot; the device creates what the slots need

**Date:** 2026-09-27
**Scope:** `ArctisAurora.Core.Rendering.Helpers` (`QueueAllocator`), `ArctisAurora.EngineWork.Rendering`
(`Renderer.CreateLogicalDevice`, `Renderer.graphicsQueueLock`, `Renderer.Draw`)

## What changed
- `CreateLogicalDevice` builds one `DeviceQueueCreateInfo` per **distinct** family among graphics, transfer and
  present, with `QueueCount = QueueAllocator.QueueCountFor(family)`. Was two infos, count 1, unconditionally.
- `QueueAllocator._queueIndex` — queue index per role. Graphics 0. Transfer 1 when it resolves to graphics' family
  and that family has `QueueCount > 1`, else 0. Present uses index 0 of `presentFamilyIndex` (unchanged).
- `QueueAllocator.QueueCountFor(family)` — one past the highest index handed out in that family, minimum 1.
- `QueueAllocator.TransferSharesQueue` — transfer index 0 on the graphics or present family, i.e. the same `VkQueue`.
- `AllocateQueue` reads the slot index. The old `cap.defaultIndex++` mutated a struct copy and did nothing; removed.
- `Renderer.graphicsQueueLock` — `transferCommandLock` when `TransferSharesQueue`, else its own object. `Draw` takes
  it around its `QueueSubmit` and `QueuePresent`.

## Why these choices

**Duplicate family indices in `pQueueCreateInfos` are invalid, and the present family was never created.**
On a single-universal-family GPU (most Adreno and Mali) graphics and transfer both resolve to family 0 and
`vkCreateDevice` failed. Independently, `AllocatePresentQueue` called `GetDeviceQueue` on `presentFamilyIndex`
without the device ever creating a queue there — invalid wherever present ≠ graphics family.

**A second queue for transfer only — no compute, no per-window graphics queues.**
Only two threads submit: Render (graphics + present) and the loaders (transfer, under `transferCommandLock`).
A separate transfer queue removes the one real contention, on the GPUs where transfer shares graphics' family.
Async compute has no module to use it and Render draws every window on one thread, so more queues would be
speculative. The slot map is where a new role goes when a consumer exists.

**Ownership is per family, so a same-family second queue needs no release/acquire.**
[[texture-queue-ownership]] keys the hand-over on family inequality; that stays correct. Cross-queue ordering is
the host `QueueWaitIdle` before the acquire/first use, same as the cross-family case.

**One shared lock object rather than a conditional lock.**
`graphicsQueueLock` aliases `transferCommandLock` only when the `VkQueue` is shared; otherwise it is private and
uncontended. `Draw` always locks it — no branch, and the shared case cannot be forgotten at a new submit site.
Cost of the shared case: `Draw` waits out an in-progress upload's `QueueWaitIdle`.

## Verified
- Build clean. Thorium, Carbon, AuroraEditor `--test`-booted with sync validation in three configurations, zero
  `[Vulkan]` lines, error set identical to baseline in each:
  - as-is (dedicated transfer family on this machine);
  - transfer forced to graphics' family (temporary, reverted) — family 0 created with 2 queues, distinct handles;
  - that plus the second queue denied (temporary, reverted) — 1 queue, `TransferSharesQueue` true, same handle.
- **NOT GUI-verified.** Not run on real mobile hardware.

## Known gaps
- `vkDeviceWaitIdle` (`Renderer` swapchain rebuild, `RenderSystem` window reap) needs every queue externally
  synchronised and takes neither lock — an upload in flight on another thread races it. Pre-existing.
- A GPU whose graphics family does not advertise `TRANSFER_BIT` and has no transfer family gets transfer family −1
  (graphics implies transfer, but reporting it is optional). Pre-existing; `AllocateQueue` throws.
- `SwapchainCreateInfoKHR` passes two family indices with `Exclusive` sharing — ignored, harmless, untouched.

Related: [[texture-queue-ownership]], [[render-window-owns-the-swapchain]]
