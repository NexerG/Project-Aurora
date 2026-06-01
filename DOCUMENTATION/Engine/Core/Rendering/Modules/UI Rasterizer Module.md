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
SourceFile: ParticleSimulator/Core/Rendering/Modules/UIModule.cs
VerifiedAgainst: 2026-05-30
---
## Description

The concrete [[Renderer Module]] that draws the UI control tree. It renders every [[Vulkan Control]] as an **instanced quad** into an offscreen image, which the compositor then blends over the game output. One draw call covers all controls; per-control data lives in GPU buffers indexed by instance.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `RendererStage` | override | `UI`. |
| `PrepareObjects()` | override | Builds the `MCUI` mesh, subscribes to the `Controls` entity group's `onChanged`, prepares the camera. |
| `UpdateModule(frame)` | override | Rebuilds the per-control instance buffer + descriptor pool/sets for the dirty frame; queues stale resources for deferred deletion. |
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
```

Descriptor counts are sized generously (50 000) with `VariableDescriptorCountBit | PartiallyBoundBit` on the last binding of each set, so the control count can grow without recreating layouts. See the descriptor discussion in [[VULKAN]].

## Methods

### Dirtying
The module is marked dirty (all frames) whenever the `Controls` group changes (add/remove) via `OnControlsChanged`, and whenever layout changes — `UILayout.ResolveLayout()` → the renderer's `UpdateModules()`. A dirty frame triggers a full instance-buffer + descriptor rebuild in `UpdateModule`.

### Drawing
`WriteCommandBuffer` begins the render pass, binds the pipeline, sets the dynamic viewport/scissor from the window size, then `MCUI.EnqueueDrawCommands` binds both descriptor sets and issues one `CmdDrawIndexed` with `instanceCount = controlCount`.

## Helpers

```C#
private void CreateSampler();                 // anisotropic repeat sampler for control textures
private void CreateCircleSDF(...);            // procedural SDF helpers (mask experiments)
```

## Related
- [[Renderer Module]] — the base class
- [[Vulkan Control]] — the entities this module renders
- [[VULKAN]] — renderer system + descriptor strategy
