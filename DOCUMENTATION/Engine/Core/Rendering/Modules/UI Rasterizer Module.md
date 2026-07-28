---
date: 2026-05-30
tags:
  - d_Module
  - d_Rendering
  - d_UI
cssclasses:
  - Aurora.css
Status: Current
Linker:
  - "[[Renderer Module]]"
System:
  - "[[VULKAN]]"
Class:
  - "[[UI Rasterizer Module]]"
Parent Class:
  - "[[Renderer Module]]"
Interfaces:
Used by:
  - "[[VULKAN]]"
Type:
  - Public
Attributes:
Namespace: ArctisAurora.EngineWork.Rendering.Modules
SourceFile: AuroraEngine/Core/Rendering/Modules/UIModule.cs
VerifiedAgainst: 2026-07-21
---
## Description

The concrete [[Renderer Module]] that draws the UI control tree. It renders every [[Vulkan Control]] as an **instanced quad** into an offscreen image, which the compositor then blends over the game output. One draw call covers all controls; per-control data lives in GPU buffers indexed by instance.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `RendererStage` | override | `UI`. |
| `PrepareObjects()` | override | Builds the `MCUI` mesh, subscribes to the `Controls` entity group's `onChanged`, prepares the camera. |
| `UpdateModule(frame)` | override | Refreshes the pooled transform mirror, then either appends the newly added controls' descriptors (normal case) or rebuilds the frame's descriptor pool/sets (only when the pool's capacity changed); queues stale resources for deferred deletion. |
| `CreatePipeline()` | override | `UIRasterizer/UI.vert+frag`, alpha blending, **dynamic viewport/scissor**. |
| `CreateRenderPass()` | override | Single color attachment, final layout `ShaderReadOnlyOptimal` so the compositor can sample it. |
| `WriteCommandBuffers(frame)` | override | Binds the pipeline, sets viewport/scissor, issues the instanced indexed draw. |

## Fields & Properties

```C#
// set 0: camera UBO (0), transforms SSBO (1), per-control data SSBO array (2, variable)
// set 1: mask/texture sampler array (0, variable)
internal override int variableSetCount => 2;

internal static MCUI meshComponent;                 // the quad mesh + instancing
internal override IReadOnlyList<Entity> renderEntities { get; set; }  // the Controls group

internal List<DeferredResources>[] deferredDeletions; // buffers/pools freed N frames later
private static ConcurrentQueue<DeferredResources> _retiredControlBuffers; // handed over by destroyed controls
```

Descriptor counts are sized generously (50 000) with `VariableDescriptorCountBit | PartiallyBoundBit` on the last binding of each set, so the control count can grow without recreating layouts. See the descriptor discussion in [[VULKAN]].

The descriptor pool and both sets are allocated once per swapchain image, sized to the `UIControls` [[Data Pool]] capacity; partial binding lets slots stay unwritten until a control fills them. Per-image state (`_frameBuiltCapacity`, `_frameWrittenControls`) tracks how far each set has been written so adds only append the new tail.

## Methods

### Dirtying
The module is marked dirty (all frames) whenever the `Controls` group changes (add/remove) via `OnControlsChanged`, and on swapchain recreate. A dirty frame no longer rebuilds everything: it re-bakes the pooled transforms into the persistent mirror buffer and appends just the `[written, live)` control descriptors. A full descriptor rebuild happens only when the pool grows (capacity change) or the live count shrank. This is what stops the per-frame descriptor-pool churn while a character key is held.

### Pooled transforms
`MCUI.MakeInstanced` mirrors the `UIControls` pool's dense `GpuTransform` column into a persistent transforms SSBO sized to the pool capacity — patched in place (`AVulkanBufferHandler.UpdateBufferRange`) over the dense range the module's `PoolCursor` reported, recreated only on pool growth. The matrices themselves are baked at the write by `VulkanControl.CommitTransform`, so the column is read-only to the render thread. Per-control descriptor data is fetched by pool dense index (`ControlPool.OwnerAt`) so it lines up with the transform mirror; the pool is no longer append-only (inserts resequence it, destroys compact it), so `PoolCursor.OrderChanged` forces a full descriptor rebuild on any frame where dense indices moved.

### Destroyed controls
A control's per-control SSBO cannot be destroyed in `VulkanControl.OnDestroy` — that runs on the main thread while descriptor sets and submitted command buffers for frames still in flight point at it. The control pushes the buffer to `RetireControlBuffer` instead, and the next `UpdateModule` moves it onto that image's `deferredDeletions`, which frees it when that image next comes around — a full swapchain cycle later, by which point every other image has had its fence waited on and its descriptors rebuilt. It has to be pushed by the control rather than discovered from the pool's `Destroyed` list, because by the time the pool reports the slot dead, compaction has already dropped the row and `OwnerAt` can no longer name the owner.

### Drawing
`WriteCommandBuffer` begins the render pass, binds the pipeline, sets the dynamic viewport/scissor from the window size, then `MCUI.EnqueueDrawCommands` binds both descriptor sets and issues one `CmdDrawIndexed` with `instanceCount` = the live control count. `WriteCommandBuffers` allocates the command-buffer array once but records only the current image; each image records itself on its first dirty pass.

## Helpers

```C#
private void CreateSampler();                 // anisotropic repeat sampler for control textures
private void CreateCircleSDF(...);            // procedural SDF helpers (mask experiments)
```

## Related
- [[Renderer Module]] â€” the base class
- [[Vulkan Control]] â€” the entities this module renders
- [[VULKAN]] â€” renderer system + descriptor strategy
