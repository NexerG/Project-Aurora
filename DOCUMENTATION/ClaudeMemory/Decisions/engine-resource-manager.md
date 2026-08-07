# Decision — one resource manager owns asset-backed memory; GPU memory is arenas, not malloc/free

**Date:** 2026-08-03
**Status:** DESIGN ONLY — nothing here is implemented. No code was written in the session that
produced this note. Treat every "will" below as a direction, not a description.
**Scope:** future `ArctisAurora.Core` resource/memory manager; `AVulkanBufferHandler`;
`ArctisAurora.EngineWork.Rendering.Modules.RenderingModule` deferred deletion.

## The motivating constraint — `maxMemoryAllocationCount`

`AVulkanBufferHandler.CreateBuffer` ends every path with `BindBufferMemory(buffer, memory, 0)` —
offset **zero**, one dedicated `vkAllocateMemory` per buffer. Buffers and allocations are **1:1**.

That matters because the two are limited very differently:

| Object | Limit |
|--------|-------|
| `VkBuffer` | none — it is a *description* of a range, not storage |
| `VkDeviceMemory` | `maxMemoryAllocationCount`, **4096** (spec floor; NVIDIA and AMD report exactly that on desktop, some Intel drivers report far more — design for the floor) |

So the ceiling is on *allocations*, not buffers, and today they are the same number. Any design
that gives each entity its own buffer walls out around **4096 entities**. `vkAllocateMemory` is
also a kernel-level call (tens of µs), so per-object allocation is slow well before it is illegal.

**The fix is `vkBindBufferMemory`'s offset parameter**, which is currently always 0. One large
allocation, many buffers bound at different offsets, respecting `memReqs.alignment`.

## Where the legacy renderer would have died

The legacy `VulkanRenderer` subclasses declare `_indexedCount = 50000` variable-count arrays
(`UIRenderer`, `Rasterizer`, `RadianceCascades2D`, `Pathtracing`). In `Rasterizer`,
set 0 binding 2 is one SSBO **per entity** and binding 3 one sampler **per entity**.

**The 50 000 is not the binding constraint.** Ordered by what breaks first:

1. `maxMemoryAllocationCount` — 4096 entities, per above.
2. Draw calls — `Rasterizer.WriteCommandBuffers` loops entities issuing one `CmdDrawIndexed` each;
   a few thousand is the realistic CPU budget.
3. Spawn cost — `AddEntityToRenderQueue` destroys the pool, rewrites every entity's descriptors and
   re-records all command buffers. O(n) per spawn, O(n²) across a load.
4. Descriptor count — last, and the only one anybody was worried about.

All four are symptoms of **per-entity granularity**. `UIModule` already fixed this for UI: two
buffers total (`transformsBuffer`, `controlDataBuffer`) regardless of control count, entity count
moving buffer *size* not buffer *count*. See [[glyphs-as-pool-data]] for the descriptor half.

**Game world differs in one way:** UI is one mesh (`uidefault`) instanced N times, so it collapses
to a single draw. The world has many meshes. Resolution: sort the pool by meshId — the world's
equivalent of `UI.DFSOrder` — and issue one instanced draw per *distinct mesh*. Draw count becomes
hundreds, not entities. `CmdDrawIndexedIndirect` later, when GPU culling is actually wanted.

## Decisions

### 1. GPU memory is arenas whose contents recycle — not malloc/free

Fixed arenas, permanently allocated, never released. A slot table tracks
`{ assetId, lastUsedFrame, size }`; eviction overwrites the coldest slot and patches the handle.

Rationale over a general free-list allocator: it removes fragmentation as a *category* instead of
fighting it with compaction, and it is how virtual texturing and Nanite-style cluster streaming
work. Allocation is permanent; residency rotates.

Block sizing: 64–256 MB is the normal range — **pick one, do not architect around the number**.
One block pool **per memory type index** (a `DEVICE_LOCAL` block cannot serve a `HOST_VISIBLE`
request; `memReqs.memoryTypeBits` must match). Anything larger than a block gets a dedicated
allocation — never grow a block. Keep buffers and images in separate blocks so
`bufferImageGranularity` never enters the picture.

### 2. The GPU cannot release memory — only the decision is GPU-driven

There is no GPU-side free. `vkFreeMemory` and all allocator bookkeeping are host operations. What
can be GPU-driven is *which* resource to evict, never the eviction itself. Consequence: "is this
resident?" and "was this used?" are answers of **different ages**, and eviction is always deferred.

### 3. Never read the indirect command buffer on the CPU

Reading indirect args CPU-side serialises GPU→CPU and destroys the reason for going indirect.
Instead:

- culling/shading writes a small **feedback buffer** (per-mesh `lastUsedFrame`), separate from the
  draw args;
- copy to a **ring of readback buffers**, one per frame in flight;
- read frame N's feedback at frame N+2, when its fence has already retired — never blocks.

The ring is not about throughput. It exists because there is no instant at which a single buffer
is not being written by the GPU; one buffer means either a torn read or a fence stall. Same
principle as `MAX_FRAMES_IN_FLIGHT` command buffers and `UIModule.deferredDeletions[currentFrame]`.

Indirect args stay write-only from the CPU's side. The feedback buffer is the channel.

### 4. Scope is asset-backed memory only

**Rejected: one manager owning all GPU memory.** Split by lifetime.

| Owner | What | Driven by |
|-------|------|-----------|
| Resource manager | meshes, textures, levels, asset buffers | asset loads, residency feedback |
| Renderer | swapchain images, attachments, descriptor pools | window size, swapchain rebuild |

Renderer transients are window-sized and rebuilt on resize; they share nothing with asset residency
but the word "memory". Routing them through one allocator makes every resize touch the asset path.

### 5. CPU pools and GPU arenas stay separate allocators under one facade

Shared **handle space** and shared **residency policy**; different mechanics. Their alignment
rules, growth behaviour and failure modes have nothing in common — unifying the allocator itself
buys nothing and costs both. `DataPool` growth stays as-is; the GPU arena is its own thing.

### 6. It is a `ThreadedSystem`, not a static with locks

- loads/unloads arrive as commands on its `CommandLane` — main and render threads enqueue, the
  manager owns the mutation. Same single-writer rule `DataPool.AssertOwner` already enforces.
- structural work at `FrameEdge`, alongside `DataManager.FrameEdge()`.
- **deferred GPU deletion centralises here.** `UIModule.deferredDeletions` is the per-module version
  of this today; modules should stop each carrying their own.
- consumers poll with something `PoolCursor`-shaped: what became resident / evicted since last look.

## Build order

1. GPU arena allocator + deferred-deletion queue as a `ThreadedSystem` with a lane. Move
   `UIModule`'s deletion queue into it.
2. `IAssetSource` + upload path — see [[asset-pipeline-bake]].
3. Residency slot table + eviction — only once something actually needs evicting.
4. Feedback buffer + readback ring — only once GPU-driven rendering exists.

1 and 2 are worth doing now. 3 and 4 are speculative until a world exists that does not fit, and
both bolt on without disturbing 1–2 **provided handle indirection is right from the start**.

## Still open

- Nothing shrinks. `ArctisAurora.Core.Data.DataPool` has `Grow()` and no counterpart — capacity is
  monotonic, so a 40k-entity zone leaves buffers 40k-sized forever.
  Reclamation is a `DataPool` feature, not a Vulkan one, and **needs hysteresis**: a naive
  shrink-on-drop thrashes `DeviceWaitIdle` in `MCUI.MakeInstanced` whenever the count oscillates
  around the threshold.
- Whether to adopt VMA rather than hand-rolling the suballocator. It is a new native dependency, so
  it needs flagging under the CLAUDE.md no-new-deps rule before anyone reaches for it.
- `TextureAsset.MaxTextures` is a hardcoded `256` whose failure mode is a throw in `RegisterInTable`.
  Should be clamped to the queried device limit at bootstrap. Note the pool uses
  `DescriptorPoolCreateFlags.None`, so the binding limit is `maxPerStageDescriptorSampledImages`
  (spec floor 16) — `UIModule.features12` enables every `DescriptorBinding*UpdateAfterBind` feature
  but neither the binding nor the pool opts in, so the larger update-after-bind limits are being
  paid for and not used.
- `UIModule.GetVariableDescriptorCount` returns `MaxTextures` unconditionally, so the variable-count
  mechanism is declared and never varied. Making it return `Table.Count` requires texture-table
  growth to force the *rebuild* branch, not `UIModule.UpdateModule`'s `_frameTableVersion` in-place
  rewrite, which would otherwise write past the allocated count. Payoff at 256 is negligible;
  matters if the ceiling rises.

## Reference — what is and is not dynamic in Vulkan descriptors

Three numbers that get conflated:

| Number | Fixed at | Cost |
|--------|----------|------|
| layout `DescriptorCount` | `vkCreateDescriptorSetLayout` — changing it means recreating layout, pipeline layout and every pipeline | **none** — a declaration, not an allocation |
| `DescriptorSetVariableDescriptorCountAllocateInfo` | each `vkAllocateDescriptorSets`, any value ≤ ceiling | only what is asked for |
| `DescriptorPoolSize.DescriptorCount` | per pool | real memory |

Idiom: set the ceiling once, high, and vary the allocation underneath. `RenderingModule.
AllocateDescriptorSets` already routes this through `GetVariableDescriptorCount(set)`, and the
legacy `VulkanRenderer.CreateGlobalDescriptorSets` already passes `_entitiesToRender.Count`.

Related: [[asset-pipeline-bake]], [[world-streaming-prefetch]], [[glyphs-as-pool-data]],
[[ecs-rework-data-pools]]
