# Decision — the new stack's clip multiplies coverage instead of discarding

**Date:** 2026-09-12
**Status:** LANDED and GPU-verified — `VUID-VkShaderModuleCreateInfo-pCode-08740` is gone from the
boot log and the UI renders unchanged.
**Scope:** `Shaders/UIEngine/UIEngine.frag` in all four trees;
`ArctisAurora.EngineWork.Rendering.Modules` — `UIEngineModule` (pipeline blend and depth state);
`ArctisAurora.EngineWork.Rendering` — `Renderer.PreInitialize`

Concerns the **new** stack only. The old stack still clips with a `discard` in
`Shaders/UIRasterizer/UI.frag`; [ui-clipping](ui-clipping.md) is unchanged and still accurate.

## What changed

`UIEngine.frag` opened with a clip test that ran `discard`. It now computes `inClip` as 0 or 1 and
folds it into the alpha the shader already writes, so no invocation leaves:

    outColor = vec4(color, opacity * alpha * inClip);

## Why these choices

**`discard` at Vulkan 1.3 compiles to `OpDemoteToHelperInvocation`, which needs a feature nothing enabled.**
The module declared the `DemoteToHelperInvocation` capability while
`VkPhysicalDeviceVulkan13Features::shaderDemoteToHelperInvocation` was never requested.
`Renderer.PreInitialize` copies each module's `features` and `features12` and hardcodes only
`features13.DynamicRendering` — `RenderingModule` has no `features13` member at all, so no module can
ask for a 1.3 feature.

**The op is picked by the target environment, and glslc will not emit another one.** Compiled the real
shader against each:

| `--target-env` | op emitted | SPIR-V |
|---|---|---|
| vulkan1.0 | `OpKill` | 1.0 |
| vulkan1.1 | `OpKill` | 1.3 |
| vulkan1.2 | `OpKill` | 1.5 |
| vulkan1.3 | `OpDemoteToHelperInvocation` | 1.6 |

`#extension GL_EXT_terminate_invocation : require` changes nothing — still demote. glslc never emits
`OpTerminateInvocation` for a plain `discard`.

**Demote was the semantically correct op, which is why lowering the target env was not an option.**
Fragments run in 2×2 quads and `fwidth` differences a value against its quad neighbour. The clip is a
screen-space rectangle edge, so it cuts *through* quads, and the shader calls `fwidth` after it — in
the rounded-box branch and again for edge thickness. `OpKill` ends the invocation and leaves the
survivors' derivatives undefined along every clip boundary. Demote keeps it running as a helper whose
output is discarded, so the quad stays whole.

**Folding into alpha removes the question rather than answering it.** No invocation leaves, so the
derivatives are correct by construction and the capability disappears — the module now declares only
`Shader`, `ImageQuery` and `RuntimeDescriptorArray`.

**It is exactly equivalent here because the pipeline has no depth.** `UIEngineModule` builds its
pipeline with `DepthTestEnable = false` and `DepthWriteEnable = false`, and blending on with
`SrcColorBlendFactor = SrcAlpha`. A discarded fragment and an alpha-0 fragment are observationally
identical. With depth writes enabled they would not be, and this fold would be wrong.

## What this cost

- A clipped fragment runs the whole shader instead of leaving at the first branch. Only the
  partially-clipped fringe pays it — a fully clipped control never reaches the GPU, because `Collect`
  culls it out of the draw list against `LayoutRect.Overlaps`.
- The `.spv` grew 12868 → 12908 bytes.

## Rejected

| Alternative | Why not |
|---|---|
| Enable `shaderDemoteToHelperInvocation` in `Renderer.PreInitialize` | One line, no shader edit, and it was the recommendation. The user chose the fold, which removes the capability rather than licensing it |
| Add a `features13` override to `RenderingModule` mirroring `features12` | Real plumbing for one flag that no module needs once the fold lands |
| Lower `--target-env` to get `OpKill` back | Trades a validation error for genuinely undefined derivatives at every clip edge |
| Per-control `vkCmdSetScissor` | One draw call per control; destroys the instancing the draw list exists to feed. Same reason the old stack rejected it |

## Known gaps

- Only `UIEngine.frag` changed. `UIRasterizer/UI.frag` still discards, and the old stack still runs.
- `RenderingModule` still has no `features13` member. Any future 1.3 feature needs that hook, or a
  hardcoded line beside `features13.DynamicRendering`.
- `SetupPipelines` took 164 ms on the first run after the change against 13 ms before. Assumed a
  shader-cache miss, not measured.

Related: [[ui-clipping]], [[ui-draw-list]], [[ui-engine-stack]], [[mapped-streaming-buffers]]
