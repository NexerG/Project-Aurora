# Decision — pictures are mipmapped table textures cached per file; a note anchors them to a character

**Date:** 2026-09-29 (stages 3–7 2026-10-01)
**Status:** LANDED — stages 1–7 (textures, `<Image>`, clipboard read, inline pictures, paste, persistence, resize
handles, wrap modes, moving, rotating); plan in [../Context/note-images-plan.md](../Context/note-images-plan.md)
**Scope:** `ArctisAurora.EngineWork.Rendering.Helpers` — `AVulkanBufferHandler` (`CreateTextureBuffer`,
`ClampToDevice`, `UploadTexture`, `CreateImage`, `CreateImageView`); `Rendering.Renderer.RecordAcquires`;
`Render.Modules.UIEngineModule.UpdateModule`; `Core.Registry.Assets` — `TextureAsset` (`ForFile`, `LoadImage`,
`Table`), `SamplerAsset.maxLod`; `Core.UI` — `ImageControl`, `StyleSpan`, `TextMeasurer`, `TextRunControl`,
`BlockControl`, `Run`, `DocumentControl.PasteImage`, `DocumentEditorControl.PasteImage`, `IClipboardTarget`,
`TextInputActions.Paste`, `DocumentXml`, `RichTextDocument`, `MarkdownFormat`; `Core.Filing.ClipboardImage`;
`ControlSampler.sampler.xml`

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
  `WriteTextureTable` iterates a stable snapshot while Main adds. `UIEngineModule.UpdateModule` reads
  `TableVersion` **before** writing the table, so an add in between is caught next frame.
- `TextureAsset.ForFile(path)`: one mipmapped `R8G8B8A8Unorm` texture per full path (case-insensitive), main thread only.
  Returns null and warns when the file fails to load or all 256 slots are taken — nothing throws.
- `ImageControl` `<Image Source>`: `kind = ImageControl`, white `colorHex`, so the shader's `color *= texel`
  shows the picture as is. Measures at native size capped to the width; `Width`/`Height` set one side, aspect the other.
- `ClipboardImage.TryGet`: registered `"PNG"` → `CF_DIBV5` → `CF_DIB` → first image file in `CF_HDROP`. A DIB
  gets a `BITMAPFILEHEADER` prefixed and goes through ImageSharp's BMP decoder; a 32-bit DIB with all-zero alpha
  is made opaque.
- **Inline pictures (stage 3).** `StyleSpan` gains `imageSource` (absolute in memory), `imageWidth`, `imageHeight`
  (0 = native), `IsPicture`, `AsText()`. `BlockControl.PictureChar` is U+FFFC.
  - `TextMeasurer.Run` gains `picture`/`imageWidth`/`imageHeight`; `MeasureAdvance` returns the width, so
    `IndexAt`/`CaretAt` are unchanged. `Flatten`: ascent = height, descent = the span font's, `breakAfter`.
    `MeasureBlock`: a picture never hangs, and overflowing breaks at `i - 1`.
  - `TextRunControl.BuildRuns(wrapWidth)` resolves `ForFile` into `_runTextures`; `PictureSize`: authored size at
    aspect × zoom, else native × zoom capped to the wrap width. `Emit` writes one `ImageControl` quad
    (`WriteImage`), no highlight or decorations.
  - `BlockControl`: `SameStyle` false for any picture; `InsertText` on a picture span → `TextSpanBeside`
    (neighbour text span, or a new zero-count `AsText()` span); `StyleAt` returns `AsText()`; `DropEmptySpans`
    turns a lone empty picture span into text.
  - Paste: `Text.Paste` → text when `ClipboardText.Get` is non-empty, else `ClipboardImage.TryGet` →
    `IClipboardTarget.PasteImage`. `DocumentEditorControl.PasteImage` refuses `.txt` (`Info`), saves
    `attachments/<note> yyyyMMdd-HHmmss.png` beside the note, then `DocumentControl.PasteImage` under one
    `BeginStep("Paste picture")` — an `InsertRangeEdit`, no new record. `FragmentFromText` strips U+FFFC.
- **Persistence (stage 4).** `Run` gains `Image`, `Width`, `Height`; `AppendRun` turns an image run into U+FFFC,
  `Runs()` writes it with no `Text`. `DocumentXml.Load`/`Save` and `RichTextDocument.Load`/`Save` (`.md`) call
  `DocumentXml.ResolvePictures` / `RelativePictures` on the tree — relative to the note's folder, `/` separators.
  `MarkdownFormat`: reads `![alt|W](path)` / `![alt|WxH](path)` / `<path>`, writes `![|W](path)` (rounded px,
  `%20`/`%28`/`%29`/`%25` escaped), inside open markers; escape mode now escapes `[`. Plain text drops pictures.

- **Resize handles (stage 5).** A picture is "selected" when the selection is exactly its one character — derived,
  no separate state. `TextRunControl.PictureAt`/`PictureBox`; `OnPointerPress` on a picture →
  `IGlyphPressTarget.PicturePressed` → `DocumentControl.PicturePressed` selects `[i, i+1]` (Shift extends as text;
  a press on the selected picture starts the text drag, and a release without moving keeps it selected —
  `textDragPicture`). `DocumentControl` region `pictures`: `ArrangePictureFrame` (after `ArrangeSelection`) places 4
  frame lines and 8 `PictureHandle`s (public nested `PanelControl`, `left/right/top/bottom`, resize cursors),
  collected after the caret. `BeginPictureResize` / `ResizePicture` (from the press, not per-tick deltas) /
  `EndPictureResize` → one `PictureEdit` (address, stored size before/after) under `BeginStep("Resize picture")`;
  undo/redo `DocumentControl.SetPictureSize` reselects. `BlockControl.SetPictureSize(offset, w, h)`.
  - Rules: left/top grow away from the fixed inline edge; corner = scale by the axis that moved more; min 8 px;
    width capped to the column; stored unzoomed and rounded to whole px; an aspect-following picture
    (`imageHeight` 0) dragged by a corner keeps height 0 (`![|W]`), a side or Shift+corner writes both.

- **Wrap modes (stage 6a).** `PictureWrap` (`Inline Square Tight TopAndBottom Behind InFront`, `[A_XSDType]`);
  `StyleSpan.wrap`/`imageX`/`imageY`/`IsFloating`; `Run` `Wrap X Y`. A floating picture's char has zero advance and
  no line-box effect (`TextMeasurer.Run.floating`). `TextLine.left` is applied in `Emit`, `IndexAt`, `CaretAt`,
  `PictureBox`, `HighlightBlock`, `CaretAtPoint`'s distance.
  - Layout: `TextMeasurer.MeasureBlock(..., ILineSlots slots)` → `MeasureAround` (one line at a time,
    `Place(y, h, out left, out right)` → top; a line taller than its guess asks again, ≤ 3 tries); `slots == null`
    keeps the old loop untouched. `DocumentControl.Paginate`: `RegisterFloats(block, blockTop)` (rect = column-left +
    X·zoom, block top + Y·zoom, `PictureSizeAt`), `WrapsAround` → `TextRunControl.LayoutAround(new FloatSlots(…))`,
    else `LayoutAround(null)` if `laidAround`, then plain `Paginate`. `FloatSlots.Place`: page push, cut every
    overlapping wrapping float (Square = rect ± 8 px·zoom, TopAndBottom = everything, Tight = opaque outline),
    widest gap wins; under 48 px·zoom → move to the nearest float bottom. No early "settled" stop while a wrapping
    float reaches below or the block is `laidAround`; document height includes float bottoms.
  - Views: `FloatingPicture : ImageControl` per float, synced after `Paginate` (`SyncFloatViews`); behind ones in
    `behindViews` inserted after the highlights (before the blocks — hit-test and paint order), the rest in
    `frontViews` before the picture frame. Press → `PicturePressed`. `PictureRect` = view rect for floats, run
    box inline; resize on a float moves X/Y from the left/top handles (Y ≥ 0).
  - Edit: `DocumentControl.SetPictureWrap` (inline → float takes its inline box as X/Y; → inline clears them;
    refused in table cells), `DocumentEditorControl.SetPictureWrap` under `BeginStep("Wrap picture")`, actions
    `Picture.WrapInline/Square/TopAndBottom/Behind/InFront`, "Wrap text ▸" in `Note.menu.xml`. `PictureEdit` and
    `SetPicture(at, StyleSpan)` / `BlockControl.SetPicture` carry the whole picture span.
  - `IGlyphPressTarget.PicturePressed(run, index, button)`: a non-left press only selects the picture.
  - Markdown: a floating picture writes `<img src width height data-wrap data-x data-y>` (lower-case wrap, rounded px),
    read back by `htmlPicture`/`ReadHtmlPicture`; inline stays `![|W](path)`.

- **Tight and moving (stage 6b).** `TextureAsset.OpaqueRows()` — per pixel row the opaque (alpha ≥ 128) left/right as
  width fractions, computed by `ForFile` from the decoded image **before** upload (`OpaqueRowsOf`), because
  `CreateTextureBuffer`'s `using var _image` frees the pixels after upload; null for non-`ForFile` textures.
  `FloatPicture.rows`; `FloatSlots.Outline` unions the rows overlapping the line ± 8 px; rows all clear → no cut.
  `Picture.WrapTight` + menu entry.
  - Move: `FloatingPicture` (now public) — AllResize cursor; a left press on the selected float →
    `BeginPictureMove` + `StartDrag`; `MovePicture` sets X/Y from the press (X clamped to the paper incl. margins,
    Y free while dragging); `EndPictureMove` → `AnchorFor(pictureTop)` (last top-level `BlockControl` with top ≤
    picture top, else the first) → same anchor: one `PictureEdit`; other: under `BeginStep("Move picture")` restore
    the press state unrecorded, `DeleteSelection` the char, `Insert` it at the target's offset 0 with
    Y = top − target top (≥ 0), select it.
  - **Pagination is strictly top-down over `floats`**: a pass drops the records of blocks at or after `from` and
    re-registers them as it reaches them; an early "settled" stop re-registers the remaining blocks at their kept
    tops. Before this, a record from a later block leaked into an earlier one on a from-0 pass — caught by
    `LayoutEngine.VerifyLayout` while dragging a picture above its anchor.

- **Rotating (stage 7).** Engine: `Control.rotation` (`Quaternion`, about the arranged centre; the setter keeps
  `ArrangeFlags.Rotated` and calls `InvalidateArrange`). `Control.Emit` builds Scale · `CreateFromQuaternion` ·
  Translation. With `Rotated`, `LayoutEngine.ArrangeRow` and `Control.WriteArranged` take the clip from
  `LayoutRect.Turned(rotation)` (bounding box of the turned rect) and so do the subtree bounds (and
  `UIEngine.VerifySubtreeCache`); `UIEngine.HitsNode` brings the point back with `LayoutRect.Unturned` (conjugate)
  and then asks `Control.HitsShape(point)` (virtual, default true). `UIEngine.vert`: `fragLocal = inPosition.xy * size`
  — identical unrotated, keeps the SDF in the quad's frame when the matrix turns.
  - `Palettes.clear` (`inlineBit | gradientBit`, unused before): `UIEngine.frag` `isClear` draws the edge band alone
    and nothing when there is no edge. `PaletteRole.Clear` is unchanged (row alpha 0 — the edge vanishes too).
  - Data: `StyleSpan.imageRotation` (clockwise degrees, whole, 0–359), `StyleSpan.collision` (`PictureCollision Box
    Shape`, `[A_XSDType]`), `StyleSpan.Rotation` → quaternion. `Run` `Rotation` `Collision`. Markdown: a floating
    picture adds `data-collision`/`data-rotate`; a turned inline picture is `<img src width height data-rotate>`
    (no `data-wrap`), since `![|W]` has no place for it.
  - Inline: `BuildRuns` hands `TextMeasurer.Run` the turned bounding box as `imageWidth/Height` (advance, ascent,
    breaks — `TextMeasurer` unchanged); `_runPictures` keeps the drawn size and quaternion; `WriteImage` draws the
    unturned rect centred in the box; `PictureSizeAt` is the drawn size; new `PictureFrame(index, out rect, out
    rotation)`; `PictureAt` un-turns the point.
  - Floats: `FloatPicture.rotation/collision/bounds`; `bounds` replaces `rect` in `WrapsAround`, `FloatsBelow`,
    `Place`'s overlap and `below`, and the document height. Square + Box (or upright) cuts `bounds ± gap`; Square +
    Shape and turned Tight go through `FloatSlots.Outline` → `TurnedSpan` — the x extent, inside the line's band, of
    the turned rect (Shape) or each turned opaque row strip (Tight): corners inside the band plus edge crossings at
    its top and bottom. Upright Tight keeps the old row-range loop exactly.
  - UI: `DocumentControl.PictureFrame` (was `PictureRect`) returns rect + quaternion; `ArrangePictureFrame` places
    frame lines and handles at their turned positions with the same rotation; `PictureHandle.Side`; cursor is the
    resize shape nearest the turned direction (tan 22.5° bins). `PictureRotator : PanelControl` — `Palettes.clear`
    fill, 1.5 px edge, radius half-diagonal + 16, `HitsShape` = band ±6 px, Crosshair, collected after the frame and
    before the handles (handles win). `Begin/Rotate/EndPictureRotate`: the from-to quaternion between press and
    pointer vectors composed onto the stored turn; degrees only on store, `2·atan2(z, w)`, whole, Shift → 15°.
  - Resize on a turned picture: the drag is brought into the picture's axes with the conjugate; a float keeps the
    opposite handle fixed — shift = R(fixedOld − fixedNew) − Δsize/2, which reduces to the old left/top rules upright.
  - Anchor: `MinPictureY(size, rotation)` = ceil of (h − turned h)/2 — Y may go that far negative so the turned box's
    top stays at or below the paragraph's top. Move drop and rotate end share `PlaceFloat` (was `EndPictureMove`'s
    body): `AnchorFor(top + lift)`, re-anchor when it rises above. Resize and inline → float clamp Y to it too.
  - Wrap changes keep the turn. `SetPictureCollision` under `BeginStep("Picture collision")`; actions
    `Picture.CollideBox/CollideShape`, "Collision ▸" in `Note.menu.xml`.
  - Tests: `TestContext.Drag(control, from, to, steps, params Keys[] held)` presses at a point and holds keys.

## Why these choices

**Mips are built on the CPU, not blitted.**
`vkCmdBlitImage` needs a graphics-capable queue. Uploads run on the transfer queue and are released to graphics
([[texture-queue-ownership]]), so blitting would mean recording on the Render thread after the acquire. CPU levels
reuse the existing submit unchanged. The cost is a Main-thread hitch per load: tens of milliseconds for a 4K image.

**Pictures upload `Unorm`, not `Srgb`.** The swapchain is `R8G8B8A8Unorm` with sRGB-nonlinear colour space and the
UI shader writes sRGB-encoded values unconverted — hex colours, fonts and icons ([[atlas-is-unorm-not-srgb]]) all
live in that space. An `Srgb` view linearizes on sample and nothing re-encodes: (200,80,40) drew as (147,20,5)
until 2026-10-01. Hardware bilinear filtering now blends in sRGB space, same as every other UI colour; the CPU
mip chain is still averaged in linear light.

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

**Paste is text-first (user, 2026-10-01: "copying text pastes text, copying images pastes an image, a table pastes as
a table or else its text").** Windows holds one copy in several formats, so "what was copied" is read off which
formats it offers: text and table copies (Word, Excel, browsers) always offer `CF_UNICODETEXT`; screenshots, copied
images and image files offer none. Excel also offers a bitmap, which image-first would have pasted. No table paste
exists, so a table lands as its text.

**A pasted picture gets no authored size.** It shows native, capped to the column, until stage 5's handles author
one. Avoids resolving the column width at paste time; `.md` writes no `|W` for it.

**Paths are absolute in memory, relative on disk, converted on the XML tree.** One place per direction covers
`.xml` and `.md` alike; spans never need the note's folder. `DocumentXml.Load`/`Save` do the `.xml` case because
they own that tree.

**Wrap, X and Y are deferred to stage 6** (user, 2026-10-01) — nothing reads them before then.

**Floats lay out top-down inside pagination, and only the paragraphs they reach pay for it.** A wrapped line's
width depends on its document y, so measuring and paginating can no longer be separate for those paragraphs. Every
other paragraph keeps measure-then-paginate, and a note without floats runs the old code (the old `MeasureBlock`
loop is not shared with `MeasureAround`). Y ≥ 0 relative to the anchor is what makes top-down sufficient — the
reason the "anchor stays put" fork was reopened for 6b's drag (re-anchor on drop).

**One gap per line, the widest.** Generalises the settled "larger side only" to several floats without text on both
sides of a picture.

**Floats are child controls, not run quads.** Their rect is document-level, they need hits above the text (or
under it, for Behind), and `ImageControl` already draws and sizes a picture.

**Rotation is stored in degrees, computed as quaternions** (user, 2026-10-01). Degrees are what a person reads in
the file; everything inside the engine — matrix, hit-test, bounds, wrap outline, the drag — goes through a Z-axis
`Quaternion`, so the same type carries over when controls ever turn in 3D. The only `atan2` is the store.

**Rotation lives on `Control`, not on the picture views.** Frame lines and handles turn with the picture too, and
clip, subtree bounds and hit-test all have to agree on a turned control's footprint, so one field and one flag
cover every case. Unrotated controls pay one flag test in arrange and hit-test.

**Inline pictures reserve their turned bounding box** (user: fork 1b). The line never overlaps a turned picture,
and `TextMeasurer` needed no change — the box is just the run's size. Rejected: floats only.

**A collision option on the picture, Square only** (user: fork 2). Box (Word's behaviour) by default; Shape follows
the turned rect. Tight is already shape-exact and Top-and-bottom is full width, so neither needs it.

**A turned box never rises above its anchor's top** (user: fork 3a). The top-down layout needs every wrap
region at or below its anchor's top; Y ≥ lift keeps that, and a rotate that breaks it re-anchors like a move drop.

**Resize handles turn with the picture** (user: fork 4a); rejected hiding them while turned.

**The ring is an edge-only circle through a new paint word, not segments** (user, 2026-10-01). The fragment shader
multiplies the edge by the fill's alpha and inline paints are opaque, so a "transparent fill with an edge" could not
be drawn. Rejected: 64 tangent segments (no shader change) and Word's single knob (not what was asked).
`0xC0000000` was the one free tag pattern; reusing `PaletteRole.Clear` would have shown the edges of every
`PanelControl`/container, all of which default to it.

**Picture selection is derived from the text selection** (stage 5). No second "selected object" to keep in sync
with undo, deletes or Shift+arrows; Delete, Ctrl+C and drag-to-move all work on it as a one-character range.

## Known gaps
- Clipboard read is not tested; a headless run has no clipboard content. Real Ctrl+V is **NOT GUI-verified**.
- A `Source` (or note picture) that fails to load leaves no placeholder; `ForFile` re-warns on every remeasure of
  that block, because failures are not cached.
- Undoing a paste leaves the PNG in `attachments/`; an unnamed note's attachment keeps its placeholder name after
  naming.
- The caret still draws beside a selected picture; double-click on a picture word-selects.
- Handles in a scrolled table cell are not clipped to the cell's viewport.
- No autoscroll while dragging a float near the editor's edge; a float cannot be dragged into another note.
- `ForFile` pays one extra pixel pass per picture at load for Tight's outline (user, 2026-10-01: option a).
- A Behind picture is clickable only where no paragraph covers it; floats in table cells are not drawn (refused by the menu, but a file could hold one);
  tables do not wrap around floats; list markers do not move with `line.left`.
- A trailing space may hang past a narrowed line's slot (the existing hang rule), up to one space into the gap.
- Noticed, not touched: a right press inside a text selection starts a text drag (`PressAt` ignores the button)
  that only a left release ends.
- A picture span carries the caret's text style; Markdown drops those fields.
- Obsidian `![[x.png]]` embeds are not read.
- A source that offers both a picture and plain text for a picture copy pastes the text.
- Rotate drag is test-verified (`TestContext.Drag` from the ring's band), **NOT GUI-verified**; the Collision menu is
  not driven by a test.
- The column cap limits the drawn width, so a column-wide inline picture turned 45° reserves ~1.4× the column and
  overhangs it.
- An effect's rotation replaces a control's stored rotation instead of adding to it (`UIEngine.vert` effect path).
- `Control.Emit`'s off-clip early-out still tests the unturned rect; a turned corner at the viewport edge can
  appear a frame late. The ring is not clipped to a table cell's viewport (same as the handles).
- An inline picture turning grows its box, so the line reflows under the cursor mid-drag; the angle is measured
  about the centre taken at the press.
- A rotated Tight float iterates every opaque row per line query (no row culling).

Related: [[texture-queue-ownership]], [[glyphs-as-pool-data]], [[ui-engine-stack]], [[document-tables]],
[[text-clipboard]]
