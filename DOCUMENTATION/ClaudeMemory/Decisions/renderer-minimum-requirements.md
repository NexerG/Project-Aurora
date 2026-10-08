# Decision — the renderer asks for Vulkan 1.3 and nothing it can run without

**Date:** 2026-10-08
**Scope:** `ArctisAurora.EngineWork.Rendering` (`Renderer.ChoosePhysicalDevice`, `Renderer.Unsupported`,
`Renderer.FitToDevice`, `Renderer.MissingFeature`), `ArctisAurora.Core.Registry.Assets` (`SamplerAsset`,
`TextureAsset.MaxTextures`)

## The floor
Vulkan 1.3 instance + device. Nothing below it is attempted — see Rejected.

| Required | Requested by | Used for |
|---|---|---|
| `VK_KHR_swapchain` | `Renderer.extensions` | presenting |
| `dynamicRendering` (1.3) | `Renderer.PreInitialize` | `CmdBeginRendering` in `UIEngineModule`, `CompositorModule` — [[dynamic-rendering]] |
| `timelineSemaphore` (1.2) | `Renderer.PreInitialize` | frame pacing in `Renderer.Draw`, `ScreenReadback` |
| `scalarBlockLayout` (1.2) | `UIEngineModule.features12` | `scalar` buffers in `UIEngine.vert`/`.frag`; the 96-byte row stride depends on it |
| `runtimeDescriptorArray`, `descriptorBindingVariableDescriptorCount`, `descriptorBindingPartiallyBound`, `shaderSampledImageArrayNonUniformIndexing` (1.2) | `UIEngineModule.features12` | the `samplers[]` texture table, the compositor's variable binding |

Hardware, roughly: NVIDIA Maxwell, AMD Polaris (GCN 1.0 on RADV), Intel Skylake.

## Optional — degrades instead of failing
- `samplerAnisotropy` — `FitToDevice` clears it when the chosen GPU lacks it; `SamplerAsset.CreateVulkanSampler`
  enables anisotropy only when the XML asks **and** `Renderer.renderer.features.SamplerAnisotropy` is set.
- Texture table size — `TextureAsset.MaxTextures` (256) is clamped by `FitToDevice` to the min of
  `maxPerStageDescriptorSamplers`, `maxPerStageDescriptorSampledImages`, `maxDescriptorSetSamplers`,
  `maxDescriptorSetSampledImages`. A full table already degrades (`Log.Warn`, image not drawn), so a clamp is
  a smaller table, not a failure. `MaxTextures` is a static property, no longer a const, for this reason.

## What was removed
- Device extensions `VK_EXT_descriptor_indexing`, `VK_EXT_scalar_block_layout`. Core since 1.2 and already
  enabled through `PhysicalDeviceVulkan12Features`; listing them was only a way for a driver to refuse.
- `VerifyRequiredFeatures` (checked `dynamicRendering` only). Replaced by `Unsupported`.

## Device choice
`ChoosePhysicalDevice` skips any device `Unsupported` names a reason for (API < 1.3, a missing extension, or
the first requested feature field the device lacks, by field name via `MissingFeature`). First usable device
wins; the `<Device>` setting picks among usable ones only. No usable device throws with every device's
reason. Feature aggregation (`PreInitialize`) runs before `Initialize`, so the check sees the full request.
`samplerAnisotropy` is treated as supported in the check because it is optional.

`CreateLogicalDevice` still has its own extension check; it is now redundant with `Unsupported`.

## Rejected
- **1.2 + `VK_KHR_dynamic_rendering`.** Branches two `CmdBeginRendering` sites and pipeline creation. GPUs
  stuck on 1.2 on Windows (Kepler, pre-Polaris GCN) are believed to lack the extension too, so the gain is
  mostly old Android, older Mesa and MoltenVK. Not worth it today.
- **1.0/1.1.** Render passes back (reverses [[dynamic-rendering]]), binary semaphores + fences instead of the
  timeline, std430 repack of the `VulkanControl` row and every `.spv` in four copies, fixed sampler array.
  The only route to Kepler / GCN1-2 / Haswell-era Intel. A rewrite, not a removal.

## Not done
- Dead renderer types (`VulkanRenderer`, `RendererTypes/*`, `MeshSubComponents/*`) — they never create a
  device, so they don't affect the floor; removal reaches into `Entity`.
- No present-support check per device in `Unsupported`.

## Verified
Builds clean (all four projects). `_Build/test.sh`: 257 pass, 3 fail — `Boot` (default sampler error),
`Sheet.FixedSize` golden, `Perf.TypeLargeNote` (layout errors); `Sheet` fails identically with the change
stashed. No `[Vulkan]` lines. The unsupported-anisotropy and clamped-table paths are unexercised on this GPU.
