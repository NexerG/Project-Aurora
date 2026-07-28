# Decision — dynamic rendering replaces render passes / framebuffers

**Date:** 2026-07-28
**Scope:** `ArctisAurora.EngineWork.Rendering` — `Renderer`, `RenderingModule`, `UIModule`, `CompositorModule`

## What changed
- `VkRenderPass` and `VkFramebuffer` are gone from the active renderer. Vulkan 1.3 **core**
  dynamic rendering, not the `VK_KHR_dynamic_rendering` extension — `ApplicationInfo.ApiVersion`
  was already `Vk.Version13`, so no extension string was added.
- Removed from `RenderingModule`: `renderPass` field, `frameBuffers` field,
  `CreateRenderPass()` abstract, `CreateModuleFrameBuffers()` abstract.
- Added to `RenderingModule`: `outputFormat` const, `ImageBarrier(...)` static helper.
- `Renderer` gained `_features13` / `features13` and `VerifyRequiredFeatures()`.

## Why these choices

**`features13` is renderer-level, not a per-module abstract property.**
`features` and `features12` are aggregated from the modules because modules genuinely differ in
what they need. Dynamic rendering is not like that — it replaces render passes for *every* module
at once, so there is no module that could sensibly opt out. Adding a third abstract property that
every module would fill in identically would be surface area for nothing. If a future module needs
a different 1.3 feature (sync2, maintenance4), add the aggregation *then*.

**`outputFormat` is one const on the base rather than a literal per use site.**
Dynamic rendering makes the pipeline declare its attachment format up front, and it must match the
image at `CmdBeginRendering`. That's now three places that have to agree. Previously `UIModule`'s
render pass declared `surfaceFormat.Format` while `CreateOutputImages` allocated
`R8G8B8A8Unorm` — they matched only because `GetSwapchainSurfaceFormat` prefers that exact format
and falls back to `_formats[0]` otherwise. The const removes that latent mismatch.

**Barriers are an explicit helper, not an abstraction over render targets.**
Only two modules exist and they need *different* transitions (`ShaderReadOnlyOptimal` vs
`PresentSrcKhr`). A `RenderTarget` type owning formats + views + layout tracking would be
speculative. `ImageBarrier` exists purely because the same 8-argument call shape appears 4x.

## The part that bites
A render pass did the layout transitions implicitly (initial/final layouts + subpass
dependencies). `CmdBeginRendering` does **none** of it. Every transition is now a manual
`vkCmdPipelineBarrier`:

| Module | Before `CmdBeginRendering` | After `CmdEndRendering` |
|---|---|---|
| `UIModule` | `Undefined` -> `ColorAttachmentOptimal` (TopOfPipe -> ColorAttachmentOutput) | `ColorAttachmentOptimal` -> `ShaderReadOnlyOptimal` (ColorAttachmentOutput -> FragmentShader) |
| `CompositorModule` | `Undefined` -> `ColorAttachmentOptimal` (ColorAttachmentOutput -> ColorAttachmentOutput) | `ColorAttachmentOptimal` -> `PresentSrcKhr` (ColorAttachmentOutput -> BottomOfPipe) |

Cross-submit ordering was **not** changed — the timeline semaphore chain in `Renderer.Draw`
already provides it. The barriers only do layout transitions and the write->read visibility.

## Hardware floor
`PhysicalDeviceVulkan13Features::dynamicRendering`. Practically: NVIDIA Maxwell (2014), AMD
Polaris on Windows / GCN 1.0 on RADV (2012), Intel Skylake (2015).
`Renderer.VerifyRequiredFeatures()` checks it before `vkCreateDevice` so the failure is legible.

## Verified
Builds clean; `Periodic.exe` runs the full loop with validation layers on and produces **zero**
layout/barrier/rendering validation errors. Untested: window resize (`RecreateSwapchain`) and
multi-monitor, which were not exercised.

## Pre-existing, NOT caused by this change
`vkCreateShaderModule` spirv-val error — `ControlDataBuffer` member 0 array stride 44 not
16-aligned. Confirmed identical on the pre-change build. Comes from commit `907c5f2`
"Fold ControlData into the pooled SSBO". See [[ecs-rework-data-pools]].
