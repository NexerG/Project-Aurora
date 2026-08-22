# Text layout — one measurer, document as a plain control tree

Landed 2026-08-07 in `542e7d7`. Supersedes L2 (virtualized view) and the layout cache.
Plan: `C:\Users\gmgyt\.claude\plans\text-controls-on-the-measurer.md`.

## What changed

Two text systems became one. `TextControl` wrapped per character in its own `Measure` and again in
its own `Arrange`; the document measured word-boundary lines into `DocumentLayoutCache` and
materialized only visible line segments as `TextRunControl` strips.

Now `TextMeasurer` is the only thing that decides where a line breaks, and the document is an
ordinary control tree the engine lays out like any other UI:

```
DocumentEditorControl (ScrollableControl)
  DocumentControl              stacks blocks by blockSpacing, places the caret
    Block : TextBlockControl   flows its runs (firstLineOffset/lastLineEndX handshake)
      TextRun : TextInputControl   one BlockLayout, glyph children
        GlyphControl
```

- Storage is unchanged: `text` setter -> `SyncGlyphs()` -> `List<GlyphControl>` children.
- `TextControl.Measure` produces a `BlockLayout`; `Arrange` places glyphs on it. Wrapping is decided
  once per layout pass instead of being re-derived in both.
- Settings are **copied down**, not looked up: `Block.ApplyLayout` writes `lineHeight` and
  `DocumentLayout.FontSizeFor(block)` onto each run, so a run sizes itself from its own fields.

## Why virtualization was dropped (user decision)

Every character in a note is a `GlyphControl`, always. This is not a regression: the model already
built one per character at load (`DocumentXml` sets `Text`, which hits the setter), and the 400-block
stress note measured ~56.7k controls **from the model alone** — already past `UIModule`'s 50,000 cap.
L2 only ever removed the *second* copy. This commits to that state rather than working around it.

Escape hatch if the ceiling bites, which does not change the shape above: `TextMeasurer` works from
the string and font metrics alone, so a run can hold `text` + a measured `BlockLayout` with **no**
glyph children and call `SyncGlyphs()` when it becomes visible.

The cache's original justification — "control-tree hit-testing only covers the materialized
viewport" — was conditional on virtualization. Remove virtualization and it stops applying.

## Load-bearing details

- **Runs must live in `children`.** Rendering, paint order, hit-testing and destroy all walk
  `children`. `DocumentXml.AttachChild` used to pick the "most-specific accepting list", so a `<Run>`
  landed in `ContentBlock.inlines` and never in the tree — which is why P2 built a parallel tree and
  L2 materialized separate strips. `inlines` is deleted; `RunAt(i)` indexes the runs among children.
  Holding them in both lists would mean keeping two lists in step through every insert/split/merge.
- **`WriteElement` had to learn two filters.** A run's children are `GlyphControl`s, which carry no
  `[A_XSDType]` and made `Save` throw; and blocks/runs inherit every `VulkanControl` scalar, of which
  `Margin`/`Padding` are `Thickness` — no `ToString`, so they serialized as the type name and the
  converter refused them on the next read. A saved note was unloadable. Filters: skip children with
  no `[A_XSDType]`, and skip members declared on `VulkanControl` or above. The second was replaced
  2026-08-07 by skipping any scalar equal to its default — see [[xml-save-skips-defaults]].
- **`RepointGlyphs` exists because measurement moved.** `Measure` sizes lines from `fontSize` while
  the quads still come from the glyphs, so a glyph built at a stale size now *disagrees with the
  layout* rather than merely looking wrong. XML attribute order is undefined, so `Text` is routinely
  applied before `FontSize` on the same parse. Both setters repoint in place.
- **`fontName` moved to `TextControl`** and resolves `_fontAsset`. Layout measures by name through
  `IGlyphMetrics` while glyphs draw from the asset; a control naming one font and drawing another
  puts every glyph at an x the measurement never predicted.
- **`cursorPosition` moved to `TextControl`.** It is the same number `OffsetAt` resolves and
  `CaretAt` draws. `DocumentControl` reads it at `Arrange` rather than copying it, so typing moves
  the caret with no extra wiring — two cursors kept in step by hand is the classic desync bug.
- **Two caret-slot rules**, carried over from the cache unchanged: a click past a character's
  midpoint takes the slot *after* it (else you can never click the end of a word), and the slot at
  the end of a **wrapped** line is claimed by the line *below* (else the caret sits off the right
  edge instead of before the wrapped word).
- **`DocumentControl` is not a `StackPanelControl`.** The caret cannot be a stacked item, and it
  cannot hang off the editor either — `ScrollableControl` takes one child and its `Arrange` is
  guarded by `children.Count == 1`, so a second child silently stops the stack being arranged at all.
  Placing the caret *after* the blocks means the run's `arrangedRect` is that frame's, and that rect
  is already scroll-shifted, so the caret follows the text without reading the scroll offset.
- **Editability boundary held.** `TextRun` overrides `ResolveOnClick` to bubble instead of inheriting
  `TextInputControl`'s begin-edit-and-stop; `DocumentEditorControl` is what sets `cursorPosition`,
  calls `BeginEdit()` and places the caret. A plain `<TextInput>` elsewhere keeps its own behaviour,
  so document names and the file tree are not editable by accident. `Block` and `DocumentControl`
  call `BubbleAll()` so the click survives the trip up from the glyph.

## Rejected

- Keeping `inlines` as a computed accessor over `children` — three call sites *index* it, which a
  lazy view cannot do without allocating per call or caching a second list, i.e. the sync problem
  being avoided.
- Sharing wrap *code* between two authorities (the L2-era rejection) is moot: there is one authority.
- Teaching `TextBlockControl` to skip a caret child instead of adding `DocumentControl` — puts a
  document special case in a general layout container.

## Verified

- `CaretAt -> OffsetAt -> CaretAt` over every slot in every run: **607/607 exact** on the sample
  note. (The cache scored 608 because it addressed document positions, where the boundary between
  two runs on a line is one slot; per-run addressing counts each run's own end.)
- Save -> reload round-trip: 7 blocks out, 7 blocks back, no glyph elements, run order and `Bold`
  preserved. Not byte-identical to the source — defaults were written explicitly (`Bold="False"`,
  `FontSize="16"`, a `<DocumentLayout>` the original omitted). Fixed for scalars 2026-08-07 by
  [[xml-save-skips-defaults]]; the spurious `<DocumentLayout>` remains.
- Rendered output is pixel-identical to the pre-rework L2 render: same heading sizes, same inline
  bold run, same wrap point, same block spacing.
- Typing: click into a paragraph, type, characters land at the caret and it advances.

## Text sits inside an authored box by `HorizontalPos`/`VerticalPos` (2026-08-22)

`Arrange` penned every glyph from the top-left of the control's own rect, so a `Label` given a
`Width`/`Height` bigger than its text drew it in the corner. The title bar's `Periodic` was the
visible case — a 21px line flush against the top-left of a 120x32 box, while `-`, `[]` and `X` looked
right only because they are content-sized inside a `Button`, and `VulkanControl.Arrange` already
centres a single child by `horizontalPosition`/`verticalPosition`.

`TextControl.Arrange` now measures the slack between the inner rect and the `BlockLayout` and offsets
the pen and the baseline by it, weighted by those same two fields. No new property: `HorizontalPos`
and `VerticalPos` are declared on `VulkanControl`, default to `0.5`, and already mean "where in the
slot", so the title centres with no change to `UI.xml` or `TabWindow.xml`.

- **Gated on `preferredWidth`/`preferredHeight`, not on the arranged rect.** Every caret-bearing text
  control is content-sized and gets handed a *wider* rect by its owner — `TextBoxControl` arranges
  its `FieldLine` at `max(inner.width, DesiredSize.X)`, `TextBlockControl` its runs at `inner.width`
  — and both then place the caret at `textRect.x + CaretAt(...).x`. Offsetting against the arranged
  rect would desync the caret from the glyphs everywhere. Slack from an authored box is zero for all
  of them.
- **Negative slack clamps to zero.** `ConfirmWindow` and `NoteNameWindow` set `preferredHeight = 20`
  on 15px prompt labels, whose line box is 22.5 — the text overflows the box, and half-centring the
  overflow would move two dialogs for no reason.
- **`FileBrowserControl`'s gutter label is pinned to `horizontalPosition = 0f`.** It is the only
  other text control in the tree carrying an authored width (`preferredWidth = 12`), and the default
  0.5 would have nudged every expander arrow into the middle of its column.
- Centring is of the **line box**, not the ink: a string with no descenders reads a pixel or so high,
  the same as CSS. `Periodic` measures ink at x 35..85, y 10..21 in its 120x32 box — centre (60,
  15.5) against (59.5, 15.5).

## Still open

- ~~`runColorHex` reaches no glyph.~~ Fixed 2026-08-07. `runColorHex` is deleted; a run carries the
  ordinary `ColorHex`, and `TextControl` overrides the now-`virtual` `VulkanControl.controlColorHex`
  to call `RepointGlyphs()` — the same shape as `fontSize`/`fontName`, needed because XML attribute
  order is undefined and `Text` may be applied first. New glyphs take the run's colour in
  `SyncGlyphs`. Made possible by [[xml-save-skips-defaults]]: `ColorHex` is declared on
  `VulkanControl`, so the old chrome filter would have dropped it from every saved note.
- `Decorations.Write` still lives in the app, not the engine, contrary to the Engine/Periodic
  boundary. Unchanged by this work.
