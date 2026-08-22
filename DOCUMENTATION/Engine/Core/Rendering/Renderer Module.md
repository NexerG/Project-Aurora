---
date: 2026-05-30
tags:
  - d_Rendering
  - d_Module
cssclasses:
  - Aurora.css
Status: Current
Linker:
  - "[[VULKAN]]"
System:
  - "[[VULKAN]]"
Class:
  - "[[Renderer Module]]"
Parent Class:
Interfaces:
Used by:
  - "[[VULKAN]]"
Type:
  - Public
  - Abstract
Attributes:
Namespace: ArctisAurora.EngineWork.Rendering.Modules
SourceFile: AuroraEngine/Core/Rendering/Modules/RenderingModule.cs
VerifiedAgainst: 2026-07-28
---
## Description

The abstract base for a **render module** â€” a self-contained mini-renderer with its own queue, pipeline, descriptors, and an **offscreen output image**. Modules render into their own target; the compositor then samples and blends every module's output into the swapchain. This is how the [[VULKAN]] renderer keeps game/UI/post passes independent and composes them at the end.

A concrete module (e.g. [[UI Rasterizer Module]]) supplies its feature set, descriptor layout, pipeline, and draw commands by overriding the abstract members below.

Modules use **dynamic rendering** (Vulkan 1.3 core), so there is no `VkRenderPass` and no `VkFramebuffer` anywhere in the module surface. A module names its attachment format at pipeline creation via `PipelineRenderingCreateInfo`, hands the image view straight to `CmdBeginRendering` when recording, and issues its own layout transitions with `ImageBarrier` on both sides of the rendering instance.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `rendererType` / `RendererStage` | abstract prop | Module identity (`Game` / `UI` / `PostProcessing`). |
| `features` / `features12` | abstract prop | Physical-device + Vulkan 1.2 features this module needs; the renderer ORs them together when creating the logical device. |
| `descriptorTypes` / `shaderStages` / `descriptorBindingFlags` / `descriptorMaxCounts` | abstract prop | Declarative descriptor-set layout description. |
| `PrepareObjects()` | abstract | Allocate command pool/queue, build mesh component, hook entity groups. |
| `CreatePipeline()` | abstract | Build the module's graphics pipeline, declaring its attachment formats in `PipelineRenderingCreateInfo`. |
| `outputFormat` | const | The offscreen colour format. Shared by the images and the pipeline's format declaration so the two cannot drift apart. |
| `CreateOutputImages()` | virtual | Allocate the offscreen colour targets (`outputFormat`, `SampledBit`). |
| `ImageBarrier(...)` | static | The layout transition a render pass used to imply. Called either side of `CmdBeginRendering`/`CmdEndRendering`. |
| `UpdateModule(frame)` | abstract | Per-frame rebuild (descriptors, instance buffers) when dirty. |
| `UpdateFrameData(imageIndex)` | virtual | The module's own per-frame buffers, written every frame whether or not it is dirty. The renderer writes the global set (see [[VULKAN]] â†’ Global Frame Data) and leaves these here. |
| `WriteCommandBuffers(frame)` | abstract | Record the draw commands. |
| `DestroySizeDependentResources()` | virtual | Tear down output images on window resize (see [[VULKAN]] â†’ swapchain recreation). |

## Fields & Properties

```C#
internal Pipeline pipeline;
internal PipelineLayout pipelineLayout;

public CommandPool moduleCommandPool;
internal CommandBuffer[] commandBuffers;
public bool[] isDirty = { true, true, true };   // per swapchain image

// offscreen render target (sampled by the compositor)
internal const Format outputFormat = Format.R8G8B8A8Unorm;
public Image[] outputImages;
public ImageView[] outputImageViews;
public DeviceMemory[] imageDeviceMemory;
public int compositorOrder = 0;                 // blend order in the compositor

internal AuroraCamera camera;
internal FrameResources[] frameResources;       // descriptor pool + sets, one per frame
```

## Methods
%% Grouped by responsibility; access shown inline. %%

### Lifecycle (driven by the renderer)
The renderer calls these in order during bootstrap: `PrepareObjects` â†’ `CreateOutputImages` â†’ `CreatePipeline`. Each frame, if the module's `isDirty[image]` is set or `HasPendingWork(image)` reports work the module found by polling, the renderer calls `UpdateModule`, which re-records via `WriteCommandBuffers`. `UpdateFrameData` runs every frame regardless, after the renderer has published its own global buffers for that image.

### Descriptors
`CreateDescriptorSetLayout` (virtual) builds the layouts from the declarative `descriptorTypes` / `shaderStages` / `descriptorBindingFlags` arrays; `AllocateDescriptorSets` handles the variable-count last binding (bindless arrays). Concrete modules fill them in `UpdateDescriptorSets`.

### Resize
`DestroySizeDependentResources` (null-safe) drops the output images so the renderer can recreate them at the new size; pipelines use dynamic viewport/scissor so they are **not** rebuilt. Under dynamic rendering there are no framebuffers to rebuild either, so a resize only touches images and the compositor's descriptors.

### Layout transitions
A render pass used to do these for free: its attachment initial/final layouts moved the image in and out, and its subpass dependencies supplied the barrier around it. `CmdBeginRendering` does none of that, so each module issues the transitions itself via `ImageBarrier` â€” one before `CmdBeginRendering` to reach `ColorAttachmentOptimal`, one after `CmdEndRendering` to reach whatever the consumer needs (`ShaderReadOnlyOptimal` for a module the compositor samples, `PresentSrcKhr` for the compositor itself).

## Helpers

```C#
internal static void ImageBarrier(CommandBuffer commandBuffer, Image image,
    ImageLayout oldLayout, ImageLayout newLayout,
    PipelineStageFlags srcStage, PipelineStageFlags dstStage,
    AccessFlags srcAccess, AccessFlags dstAccess);
internal static ShaderModule CreateShaderModule(ref Vk vk, ref Device logicalDevice, byte[] code);
internal static byte[] ReadFile(string fileName);
```

## Related
- [[VULKAN]] â€” the system that owns and drives modules
- [[UI Rasterizer Module]] â€” the concrete UI module
