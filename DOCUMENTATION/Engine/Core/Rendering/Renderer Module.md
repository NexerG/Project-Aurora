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
VerifiedAgainst: 2026-05-30
---
## Description

The abstract base for a **render module** â€” a self-contained mini-renderer with its own queue, render pass, pipeline, descriptors, and an **offscreen output image**. Modules render into their own target; the compositor then samples and blends every module's output into the swapchain. This is how the [[VULKAN]] renderer keeps game/UI/post passes independent and composes them at the end.

A concrete module (e.g. [[UI Rasterizer Module]]) supplies its feature set, descriptor layout, render pass, pipeline, and draw commands by overriding the abstract members below.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `rendererType` / `RendererStage` | abstract prop | Module identity (`Game` / `UI` / `PostProcessing`). |
| `features` / `features12` | abstract prop | Physical-device + Vulkan 1.2 features this module needs; the renderer ORs them together when creating the logical device. |
| `descriptorTypes` / `shaderStages` / `descriptorBindingFlags` / `descriptorMaxCounts` | abstract prop | Declarative descriptor-set layout description. |
| `PrepareObjects()` | abstract | Allocate command pool/queue, build mesh component, hook entity groups. |
| `CreateRenderPass()` / `CreatePipeline()` | abstract | Build the module's render pass + graphics pipeline. |
| `CreateOutputImages()` | virtual | Allocate the offscreen color targets (R8G8B8A8, `SampledBit`). |
| `CreateModuleFrameBuffers()` | abstract | Framebuffers over the output images. |
| `UpdateModule(frame)` | abstract | Per-frame rebuild (descriptors, instance buffers) when dirty. |
| `WriteCommandBuffers(frame)` | abstract | Record the draw commands. |
| `DestroySizeDependentResources()` | virtual | Tear down output images + framebuffers on window resize (see [[VULKAN]] â†’ swapchain recreation). |

## Fields & Properties

```C#
internal Pipeline pipeline;
internal PipelineLayout pipelineLayout;
internal RenderPass renderPass;
internal Framebuffer[] frameBuffers;

public CommandPool moduleCommandPool;
internal CommandBuffer[] commandBuffers;
public bool[] isDirty = { true, true, true };   // per swapchain image

// offscreen render target (sampled by the compositor)
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
The renderer calls these in order during bootstrap: `PrepareObjects` â†’ `CreateRenderPass` â†’ `CreateOutputImages` â†’ `CreateModuleFrameBuffers` â†’ `CreatePipeline`. Each frame, if the module's `isDirty[image]` is set, the renderer calls `UpdateModule`, which re-records via `WriteCommandBuffers`.

### Descriptors
`CreateDescriptorSetLayout` (virtual) builds the layouts from the declarative `descriptorTypes` / `shaderStages` / `descriptorBindingFlags` arrays; `AllocateDescriptorSets` handles the variable-count last binding (bindless arrays). Concrete modules fill them in `UpdateDescriptorSets`.

### Resize
`DestroySizeDependentResources` (null-safe) drops framebuffers + output images so the renderer can recreate them at the new size; pipelines use dynamic viewport/scissor so they are **not** rebuilt.

## Helpers

```C#
internal static ShaderModule CreateShaderModule(ref Vk vk, ref Device logicalDevice, byte[] code);
internal static byte[] ReadFile(string fileName);
```

## Related
- [[VULKAN]] â€” the system that owns and drives modules
- [[UI Rasterizer Module]] â€” the concrete UI module
