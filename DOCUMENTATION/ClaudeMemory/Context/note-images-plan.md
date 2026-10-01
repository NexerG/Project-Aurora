# Plan — pictures in notes (Word-style)

**Status:** AGREED 2026-09-29 (user: "go with recommendations"). Stages 1–7 landed (3–7 on 2026-10-01); nothing further planned.
Decisions: [[note-images]].

## Model
- A picture is one U+FFFC character in a block's text. Its `StyleSpan` (count 1, never merged) holds source, size,
  wrap mode and offset.
- Inline: advance = width, ascent = height (sits on the baseline, grows the line); a line may break on either side,
  and the character never hangs past the margin like trailing spaces.
- Other wrap modes: zero advance. The picture is placed off the anchor paragraph and cuts an exclusion.

## Stages
| # | Stage | Status |
|---|---|---|
| 1 | CPU mip chain, device clamp, `MaxLod`, copy-on-write table, `TextureAsset.ForFile` | landed |
| 2 | `ImageControl` `<Image Source>`, `ClipboardImage.TryGet` | landed |
| 3 | Inline pictures: `StyleSpan` picture fields; `TextMeasurer.Flatten` box and break rules; `TextRunControl.Emit` image row; `Text.Paste` on Ctrl+V → `PasteImage` saves `attachments/<note> yyyyMMdd-HHmmss.png` beside the note, one undo step (`InsertRangeEdit`); `.txt` refuses; paths resolved against the note's folder in `DocumentXml`/`RichTextDocument` | landed |
| 4 | Persistence: XML `<Run Image Width Height/>`; Markdown inline `![alt\|W](path)`. `Wrap X Y` and floating `<img src width data-wrap data-x data-y>` moved to stage 6 | landed |
| 5 | Resize handles: click selects the character; 8 handles + frame in `DocumentControl`'s overlay; corners keep aspect, sides stretch, Shift+corner free; one `PictureEdit` per drag. Markdown already writes `\|WxH` once a height is authored. Selection is derived (exactly one picture char); width capped to the column; corner on an aspect-following picture writes width only | landed |
| 6 | Wrap + free position: `DocumentControl` measures and paginates in one top-down pass; `TextMeasurer.MeasureBlock` takes a per-line exclusion query; `TextLine.left` threaded through Emit, CaretAt, IndexAt, highlights, caret nav, list marker. Square = rect, Tight = per-row alpha extents from load, Top-and-bottom = full width, Behind/In front = draw order only. Drag moves; right-click "Wrap text ▸". Adds `StyleSpan`/`Run` `Wrap X Y` and Markdown's floating `<img>` | planned |

| 6a | (split 2026-10-01) Square, Top-and-bottom, Behind, In front; `ILineSlots` layout; `TextLine.left`; `FloatingPicture`; "Wrap text ▸" menu; XML `Wrap X Y`, Markdown `<img data-wrap data-x data-y>`; right-press selects a picture | landed |
| 6b | Tight (`TextureAsset.OpaqueRows`, taken at load by `ForFile` — pixels are freed after upload); drag-move a float; re-anchor on drop to the block at/above its new top (Y ≥ 0), one undo step | landed |
| 7 | Rotate (agreed 2026-10-01: 1b, 2 collision option, 3a, 4a; degrees stored, quaternion math): `Control.rotation` (emit, clip, bounds, hit-test, `HitsShape`); edge-only ring `PictureRotator` (`Palettes.clear`); inline reserves the turned box; floats wrap `bounds`, Square + `Collision="Shape"` and Tight follow the turned outline; handles turn with it; Y ≥ lift, re-anchor on rotate end; `Run Rotation Collision`, `<img data-rotate data-collision>` | landed |

## Settled forks
- Square/Tight with room on both sides → text on the larger side only (Word's "Largest only").
- ~~Dragging a floating picture changes only its offset; the anchor stays put.~~ **Reopened 2026-10-01 (user: 1a):**
  a drop re-anchors to the block at or above the picture's new top, Y ≥ 0 — layout is top-down, so a float above
  its anchor could not wrap the text above it.
- Pictures in table cells stay inline; wrap gap is a fixed 8 px; a line slot under 48 px (× zoom) moves below the float.
- A float is placed relative to its anchor paragraph and may cross a page break.

## Out of scope
Paste from Word/HTML, text paste, crop, captions, alt-text editing, freeing textures, GPU mip generation.

## Verification per stage
- 3–4: logic tests — inline measure (advance, ascent, breaks), XML + Markdown round trip, range delete over a picture + undo.
- 5: resize undo test.
- 6: headless `TextMeasurer` interval tests per mode; golden note with one picture per mode, pageless and paged.
- 7: `Layout.RotatedHitTest`; `TextInput.PictureRotateRoundTrip/Inline/Layout/Draws/PictureRotate` (ring drag, Shift
  snap, re-anchor, undo); goldens `PictureRotateInline.Selected`, `PictureRotateLayout.Tight`, `PictureRotateDraws.Box/Shape`.
- Clipboard paste: GUI only (Win+Shift+S → Ctrl+V), reported as such.
