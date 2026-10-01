# Plan — pictures in notes (Word-style)

**Status:** AGREED 2026-09-29 (user: "go with recommendations"). Stages 1–2 landed; 3–6 wait for the tables work
(`TableControl`, [[document-tables]]) to be committed, because they edit the same document files.
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
| 3 | Inline pictures: `StyleSpan` picture fields; `TextMeasurer.Flatten` box and break rules; `TextRunControl.Emit` image row; `Text.Paste` on Ctrl+V → `PasteImage` saves `attachments/<note> yyyyMMdd-HHmmss.png` beside the note, one undo step (`SliceInsertEdit`); `.txt` refuses; `LoadDocument` resolves against the note's folder | planned |
| 4 | Persistence: XML `<Run Image Width Height Wrap X Y/>`; Markdown inline `![alt\|W](path)`, floating `<img src width data-wrap data-x data-y>` | planned |
| 5 | Resize handles: click selects the character; 8 handles + frame in `DocumentControl`'s overlay; corners keep aspect, sides stretch, Shift+corner free; one `PictureEdit` per drag | planned |
| 6 | Wrap + free position: `DocumentControl` measures and paginates in one top-down pass; `TextMeasurer.MeasureBlock` takes a per-line exclusion query; `TextLine.left` threaded through Emit, CaretAt, IndexAt, highlights, caret nav, list marker. Square = rect, Tight = per-row alpha extents from load, Top-and-bottom = full width, Behind/In front = draw order only. Drag moves; right-click "Wrap text ▸" | planned |

## Settled forks
- Square/Tight with room on both sides → text on the larger side only (Word's "Largest only").
- Dragging a floating picture changes only its offset; the anchor stays put.
- A float is placed relative to its anchor paragraph and may cross a page break.

## Out of scope
Paste from Word/HTML, text paste, crop, rotate, captions, alt-text editing, freeing textures, GPU mip generation.

## Verification per stage
- 3–4: logic tests — inline measure (advance, ascent, breaks), XML + Markdown round trip, range delete over a picture + undo.
- 5: resize undo test.
- 6: headless `TextMeasurer` interval tests per mode; golden note with one picture per mode, pageless and paged.
- Clipboard paste: GUI only (Win+Shift+S → Ctrl+V), reported as such.
