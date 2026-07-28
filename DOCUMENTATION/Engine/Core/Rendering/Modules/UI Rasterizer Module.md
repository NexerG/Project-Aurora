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
| `CreatePipeline()` | override | Declares a single colour attachment of `outputFormat` in `PipelineRenderingCreateInfo`; no render pass handle. |
| `WriteCommandBuffers(frame)` | override | Binds the pipeline, sets viewport/scissor, issues the instanced indexed draw. |

## Fields & Properties

```C#
// set 0: camera UBO (0), transforms SSBO (1), per-control data SSBO array (2, variable)
// set 1: mask/texture sampler array (0, variable)
internal override int variableSetCount => 2;

internal static MCUI meshComponent;                 // the quad mesh + instancing
internal override IReadOnlyList<Entity> renderEntities { get; set; }  // the Controls group

internal List<DeferredResources>[] deferredDeletions; // descriptor pools freed N frames later
```

Descriptor counts are sized generously (50 000) with `VariableDescriptorCountBit | PartiallyBoundBit` on the last binding of each set, so the control count can grow without recreating layouts. See the descriptor discussion in [[VULKAN]].

The descriptor pool and both sets are allocated once per swapchain image, sized to the `UIControls` [[Data Pool]] capacity; partial binding lets slots stay unwritten until a control fills them. Per-image state (`_frameBuiltCapacity`, `_frameWrittenControls`) tracks how far each set has been written so adds only append the new tail.

## Methods

### Dirtying
The module is marked dirty (all frames) whenever the `Controls` group changes (add/remove) via `OnControlsChanged`, and on swapchain recreate. A dirty frame no longer rebuilds everything: it re-bakes the pooled transforms into the persistent mirror buffer and appends just the `[written, live)` control descriptors. A full descriptor rebuild happens only when the pool grows (capacity change) or the live count shrank. This is what stops the per-frame descriptor-pool churn while a character key is held.

### Pooled transforms
`MCUI.MakeInstanced` mirrors the `UIControls` pool's dense `GpuTransform` column into a persistent transforms SSBO sized to the pool capacity — patched in place (`AVulkanBufferHandler.UpdateBufferRange`) over the dense range the module's `PoolCursor` reported, recreated only on pool growth. The matrices themselves are baked at the write by `VulkanControl.CommitTransform`, so the column is read-only to the render thread. Per-control descriptor data is fetched by pool dense index (`ControlPool.OwnerAt`) so it lines up with the transform mirror; the pool is no longer append-only (inserts resequence it, destroys compact it), so `PoolCursor.OrderChanged` forces a full descriptor rebuild on any frame where dense indices moved.

### Pooled control data
`ControlData` (quad UVs + style) is a pool column too, mirrored to its own persistent SSBO bound once at set0/b2. It used to be one small Vulkan buffer per control, bound as a descriptor array — so every control owned a GPU resource that had to be created on construction, re-bound whenever the set was rebuilt, and deferred-deleted on destroy. As a column it rides compaction and resequence like everything else, and a destroyed control now frees nothing on the GPU at all. `VulkanControl.controlData` is a `ref` into the column; writers call `UpdateControlData()` to widen the dirty range, the same contract as `CommitTransform()`.

The binding relies on `GL_EXT_scalar_block_layout`: the C# struct is `Pack = 1` at 44 bytes, and scalar layout gives the GLSL struct the same 44-byte array stride. Under `std430` it would round to 48 and every control past the first would read shifted data.

### Drawing
`WriteCommandBuffer` barriers the output image from `Undefined` to `ColorAttachmentOptimal`, opens a rendering instance with `CmdBeginRendering` (the output image view, `Clear`/`Store`, the dark clear colour), binds the pipeline, sets the dynamic viewport/scissor from the window size, then `MCUI.EnqueueDrawCommands` binds both descriptor sets and issues one `CmdDrawIndexed` with `instanceCount` = the live control count. After `CmdEndRendering` a second barrier moves the image to `ShaderReadOnlyOptimal` for the compositor to sample. `WriteCommandBuffers` allocates the command-buffer array once but records only the current image; each image records itself on its first dirty pass.

## Helpers

```C#
private void CreateSampler();                 // anisotropic repeat sampler for control textures
private void CreateCircleSDF(...);            // procedural SDF helpers (mask experiments)
```

## Related
- [[Renderer Module]] â€” the base class
- [[Vulkan Control]] â€” the entities this module renders
- [[VULKAN]] â€” renderer system + descriptor strategy
