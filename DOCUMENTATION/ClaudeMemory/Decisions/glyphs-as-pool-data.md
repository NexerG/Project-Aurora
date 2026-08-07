# Decision — glyphs stay controls; the sampler array becomes a texture table

**Date:** 2026-07-31
**Status:** IMPLEMENTED (2026-07-31). Runs clean; not yet eyeballed — see *Verified*.
**Scope:** `ArctisAurora.EngineWork.Rendering.Modules.UIModule`, the `UIRasterizer` shaders,
`VulkanControl.ControlData`, `ArctisAurora.Core.Registry.Assets.TextureAsset`.

## The standing decision first: a glyph is a control, and stays one

**User decision, 2026-07-31.** Periodic is Obsidian/Google-Docs/Word class, not a notepad. Individual
letters must be able to carry their own colour, their own rotation, and their own animation. Per-letter
animation is explicitly wanted.

So a `GlyphControl` keeps:
- its **full `GpuTransform` mat4** — letters may rotate and animate independently of their run;
- its **own tint in `ControlData`** — per-letter colour and gradient text are on the roadmap
  (*text upgrade → simple color, gradient*).

**Do not propose stripping these again.** An earlier pass in this same session designed a 24-byte
glyph row that dropped the matrix, the tint and the per-glyph metrics, moved glyphs into their own
`UIGlyphs` pool, and deleted `GlyphControl`. It was rejected, correctly: it optimised for static
uniform text and would have made per-letter animation impossible. The cost of a glyph being a real
entity — a pool row, a mat4, a tree slot — **is the price of the feature, not debt.**

Row count is not the problem it looks like: the document virtualizes to viewport ± 1, so a 100-page
note never has 50k live rows. Virtualization handles row count. What follows handles descriptors.

## The actual problem — the sampler array is indexed by instance

`UIModule` binds a **50,000-entry** `CombinedImageSampler` array (set 1, binding 0), and the fragment
shader reads `samplers[fragInstanceID]` where `fragInstanceID` is `gl_InstanceIndex` — the pool's
dense index. So the array is keyed by **which control** rather than by **which texture**, and
`WriteSamplerDescriptors` writes one entry per control from `control.maskAsset.textureImageView` +
`control.maskSampler`.

Every glyph of a font writes the *identical* pair. Thousands of descriptors spent saying one thing
thousands of times, and the cap is "how many controls exist" — which one large note can exhaust for
the whole application.

## The change

Add a texture index to the per-control data and index the array by it:

```
ControlData { QuadUVs uvs; ControlStyle style; uint textureIndex; }   // 44 B -> 48 B
```

frag: `texture(samplers[fragTextureIndex], fragUV)` instead of `samplers[fragInstanceID]`.

The descriptor array is then a **texture table**: one slot per distinct `(imageView, sampler)` pair.
Every glyph in a font shares a slot; every panel masked by `invisible` shares another. The cap drops
from ~50,000 (control count) to a few hundred (texture count).

`TextureAsset` is the natural owner of the index — assigned when the asset registers, so the table is
written when a *texture* appears, never when a control does.

## Why this is the right cut

- **Control churn stops touching descriptors at all.** Today adding controls appends descriptor
  writes, and a compaction or resequence (`cursor.OrderChanged`) forces a **full descriptor-set
  rebuild**. With a texture table, descriptors change only when a new texture registers — typing a
  character would never write a descriptor again.
- **It breaks the instance-index triple-coupling.** `gl_InstanceIndex` currently indexes transforms,
  control data *and* samplers. Only the first two are genuinely per-instance.
- **It fixes a live validation warning for free.** `ControlData` is 44 bytes and spirv-val complains
  the array stride is not 16-aligned (recorded in [[dynamic-rendering]] as pre-existing, from
  `7b5e032`). Adding the `uint` makes it 48, which is 16-aligned.
- **It is already on the roadmap** — Phase D's *descriptor sets → texture set → massive texture
  buffer*. Text just forced it early.
- **It does not depend on the font-loading strategy.** Eager or lazy, indices are assigned as textures
  register — so the open "do I load every font whether or not it is used" question can stay open.

## What this deliberately does NOT do

Rejected with the 24-byte-row design above, recorded so it is not re-proposed:

- **A separate `UIGlyphs` pool.** Glyphs stay in `UIControls`, one draw, existing DFS order.
- **A span allocator, or a rebuild-the-column walk.** Neither is needed if glyphs stay entities.
- **The draw-ordering decision** (depth key vs interleaved batches vs text-on-top). That question only
  existed because glyphs were going to be drawn from a second pool. Moot.
- **Dropping per-glyph CPU metrics.** `IGlyphMetrics`/`TextMeasurer` re-derive them for the *cache*'s
  purposes, which is why unmaterialized text can be measured — but a live animated glyph keeps its
  own.

## Still open, independent of this change

- **Clipping does not exist.** Every `Arrange` computes `ClipRect`, and its only consumer
  `VulkanControl.HitTest` has **zero callers** — `UICollisionHandling` hit-tests the transform quad
  instead. The scissor is whole-window; neither shader clips. `ScrollableControl` carries a comment
  deferring to "your existing clip system", which was never built. Scrolled document text is not
  actually cut at the viewport edge.
- **`UI.frag` MSDF-decodes every control**, including plain panels sampling the `invisible` mask
  through `median(msdf.rgb)`. Once a texture index exists, a per-control flag (or a second pipeline)
  distinguishing "MSDF glyph" from "plain texture" is the natural follow-on.

## What landed

- **`TextureAsset`** — `MaxTextures = 256`, a static append-only `Table`, a `TableVersion` counter, and
  `textureIndex` assigned by `RegisterInTable()`. Called from the three places that actually create an
  image view (`LoadAsset`'s file branch, `LoadDefault`, `LoadInvisible`), so a `TextureAsset` sitting in
  the registry without a view never takes a slot.
- **`ControlData`** — gained `uint textureIndex`; 44 B → **48 B**.
- **`VulkanControl.maskAsset`** — field → property, writing `controlData.textureIndex` and dirtying the
  row on assign. Every existing `maskAsset = x` kept working unchanged, including
  `GlyphControl.SetCharacter`'s.
- **`VulkanControl.maskSampler`** — **deleted**. Its only reader was the per-control descriptor write,
  and its ctor assignment was a registry dictionary lookup *per control* for a value that was identical
  engine-wide.
- **`UIModule`** — `WriteSamplerDescriptors(frame, from, to)` → `WriteTextureTable(frame)`;
  `_frameWrittenControls` deleted, `_frameTableVersion` added; set 1 sized by `MaxTextures` instead of
  pool capacity.
- **Shaders** — `ControlData` gained `uint textureIndex`; `fragInstanceID` became `fragTextureIndex`
  (same location, same `flat uint`, so no varying was added), fed from
  `CD.controls[gl_InstanceIndex].textureIndex`. Applied to all **three** copies — `AuroraEngine/`,
  `AuroraEditor/` and `Periodic/` each carry their own, and **Periodic's frag is the MTSDF one**, which
  reads `samplers[...]` twice (`texture` and `textureSize`). Recompiled with
  `glslc --target-env=vulkan1.3`.

**The rebuild trigger shrank.** It was `capacity changed || cursor.OrderChanged || live <
writtenControls || live > builtCapacity`. It is now **capacity changed** alone: set 0's three bindings
point at whole-pool mirrors, so compaction and resequence move rows *inside* buffers that stay put, and
set 1 no longer cares about dense index at all. `cursor.TryConsumeStructural()` is still called — an
unconsumed generation looks pending forever — its result just no longer forces a rebuild.

## Verified

- Builds clean, 0 errors, no new warnings.
- `Periodic.exe` runs the full bootstrap and starts all three threads with **validation layers on**
  (`Renderer.isDebugEnabled = true`) and produces **zero** validation output.
- The pre-existing `vkCreateShaderModule` spirv-val error — *`ControlDataBuffer` member 0 array stride
  44 not 16-aligned*, recorded in [[dynamic-rendering]] — **no longer appears**. 48 is 16-aligned.
- **NOT verified: that text still looks right.** Nothing here was eyeballed; a wrong index would render
  the wrong image rather than crash. Needs a GUI pass.

## Known pre-existing hole this walks past

`TextureAsset.LoadAsset`'s early return when the name is already registered does `asset = d[name]` —
assignment to a *parameter*, which does nothing — and returns with `this` never getting an image view.
Before, that control sampled a null view; now it also reports `textureIndex = 0` and samples whatever
registered first. Slightly less bad, still wrong. Not touched.

Related: [[ecs-rework-data-pools]], [[dynamic-rendering]], [[periodic-editor-architecture]]
