# Decision — pictures are mipmapped table textures cached per file; a note anchors them to a character

**Date:** 2026-09-29
**Status:** PARTIAL — stages 1–2 landed (textures, `<Image>`, clipboard read); 3–6 planned in
[../Context/note-images-plan.md](../Context/note-images-plan.md)
**Scope:** `ArctisAurora.EngineWork.Rendering.Helpers` — `AVulkanBufferHandler` (`CreateTextureBuffer`,
`ClampToDevice`, `UploadTexture`, `CreateImage`, `CreateImageView`); `Rendering.Renderer.RecordAcquires`;
`Core.Registry.Assets` — `TextureAsset` (`ForFile`, `LoadImage`, `Table`), `SamplerAsset.maxLod`;
`Core.UI.ImageControl`; `Core.Filing.ClipboardImage`; `ControlSampler.sampler.xml`

## What changed
- `CreateTextureBuffer(…, mipmaps)` builds the full mip chain on the CPU: each level `Resize` from the one above,
  `KnownResamplers.Box`, `Compand = true`. All levels go in one staging buffer, one `BufferImageCopy` per level,
  same transfer submit.
- Images over `maxImageDimension2D` shrink to it (aspect kept, `Warn`) before upload — any texture, not only pictures.
- Every texture barrier, the acquire in `RecordAcquires` and `CreateImageView` use `Vk.RemainingMipLevels`, so the
  level count never travels through `QueueAcquire`. Single-level images are unaffected.
- `SamplerAsset` gains `MaxLod` (default 0). `ControlSampler`: `MipmapMode="Linear" MaxLod="1000"`. Atlases
  have one level, so glyphs and icons sample as before (the `PanelAndLabel` golden still matches).
- `TextureAsset.Table` is a copy-on-write array published with `Volatile.Write`, so the Render thread's
  `WriteTextureTable` iterates a stable snapshot while Main adds.
- `TextureAsset.ForFile(path)`: one mipmapped sRGB texture per full path (case-insensitive), main thread only.
  Returns null and warns when the file fails to load or all 256 slots are taken — nothing throws.
- `ImageControl` `<Image Source>`: `kind = ImageControl`, white `colorHex`, so the shader's `color *= texel`
  shows the picture as is. Measures at native size capped to the width; `Width`/`Height` set one side, aspect the other.
- `ClipboardImage.TryGet`: registered `"PNG"` → `CF_DIBV5` → `CF_DIB` → first image file in `CF_HDROP`. A DIB
  gets a `BITMAPFILEHEADER` prefixed and goes through ImageSharp's BMP decoder; a 32-bit DIB with all-zero alpha
  is made opaque.

## Why these choices

**Mips are built on the CPU, not blitted.**
`vkCmdBlitImage` needs a graphics-capable queue. Uploads run on the transfer queue and are released to graphics
([[texture-queue-ownership]]), so blitting would mean recording on the Render thread after the acquire. CPU levels
reuse the existing submit unchanged. The cost is a Main-thread hitch per load: tens of milliseconds for a 4K image.

**`Compand` averages in linear light.** Box-filtering sRGB bytes directly darkens every minified edge. The 25% golden
shows 1 px black/white stripes averaging to an even light grey.

**No texture is ever freed; 256 is the cap.** Accepted by the user for now. Freeing needs a slot free list and GPU
destruction delayed until the frames using the texture finish. That is the "engine image upgrade".

**A picture in a note is a U+FFFC character with a picture span** (Word's model: every picture is anchored).
Caret, selection, delete, undo fragments and paste positions all work on it unchanged. Wrap modes other than
inline give it no advance and place the picture off its anchor paragraph.

**Rejected: a picture as its own block.** Obsidian-simple, but the user wants Word behaviour: inline, wrap modes,
free position, resize handles.

**`.txt` notes refuse a pasted picture.** Plain text cannot hold one.

## Known gaps
- **The texture-table version is read after `WriteTextureTable`** in `UIEngineModule`, so a texture added between
  the two is never written into that frame's set (until the next add). The fix is to read the version first,
  a two-line change in `UIEngineModule`. Awaiting approval.
- Clipboard read is not tested; a headless run has no clipboard content to read.
- A `Source` that fails to load leaves a zero-size control and a `Warn`, with no placeholder.

Related: [[texture-queue-ownership]], [[glyphs-as-pool-data]], [[ui-engine-stack]], [[document-tables]]
