# Decision — markdown as you type, code blocks, rules and alignment are block state on the one paragraph control

**Date:** 2026-10-01
**Status:** LANDED. Test-verified: `TextInput.MarkdownPrefixes`, `MarkdownInline`, `CodeBlockEditing`, `RuleEditing`,
`MarkdownBlocksRoundTrip`, `MarkdownSkipsPlainText`, `AlignLines`, `AlignAroundPicture`; golden-verified:
`MarkdownBlocksDraw.Blocks` (viewed before approval). **NOT GUI-verified.**
**Scope:** `ArctisAurora.Core.UI` — `DocumentControl` (regions `markdown`, `lists`, `styling`, `undo primitives`),
`BlockControl`, `TextRunControl`, `TextMeasurer` (`TextLine.room`), `RichTextDocument` (`TextStyleType.Rule`,
`TextAlignment`, `TextStyle.fontName`, `DocumentLayout.FontNameFor`), `DocumentEdits` (`BlockSnapshot`,
`DeleteRangeEdit`), `DocumentXml`, `MarkdownFormat`/`PlainTextFormat`, `DocumentEditorControl`,
`DocumentToolbarControl`, `TextInputActions`; data `DocumentSettings.settings.xml`, `EngineFonts.imports.xml`,
`EngineAssets.assets.xml`, Thorium `InputMap.inputs.xml`, icons `align-left/center/right.svg`

## What changed
- **Markdown as you type.** `TypeMarkdownPrefix` (was `TypeListPrefix`) adds `#`×1–6 + space → Heading1–6 and
  `> ` → Quote, as a `TextEdit` + `StyleRangeEdit` in the keystroke's step. `TypeInlineMarkdown` closes `**x**`,
  `*x*`, `~~x~~`, `` `x` `` on the closing character: both markers removed, the content styled, one `BlockStateEdit`;
  the closed style is armed off so what follows is plain. `TypeMarkdownLine` (on Enter): a whole line of
  ```` ```lang ```` makes the block Code with `language`; `---`/`***`/`___` makes a rule. All off in Code, on a rule,
  in table cells (prefixes and lines only) and when `DocumentControl.plainText` (a `.txt` note).
- **Code blocks.** Each line stays its own Code `BlockControl` (as `.md` already read them). `ApplyLayout` sets
  `fontName` from `DocumentLayout.FontNameFor` — the scheme's `<TextStyle Type="Code" FontName="consola">` — and insets
  10 px both sides; `Emit` draws a palette `SubField` ground per line across the full block width (`Field` is the
  page's own colour in thorium-light, so it drew nothing visible); `Paginate` drops
  `blockSpacing` between consecutive Code blocks, so a run reads as one box. A Code *span* in other text takes the
  same font through the new virtual `TextRunControl.FontFor`. `language` rides `SplitAt`, snapshots,
  `Language` on `<Block>` and the fence's info string. Enter on an empty last line of a run turns it back to Text
  (`EndCodeBlock`). Consolas is baked from the installed system font (`consola.ttf`, four faces).
- **Rules.** `TextStyleType.Rule`: a text-less block that measures as one empty line and `Emit`s a 1 px palette
  `Line` stroke across the column instead of glyphs. Typing, pasting or Enter on it starts a Text paragraph after
  it (`LeaveRule`: `SplitBlockAt` + `SetBlockList`). `DeleteBetween` with a rule as the head gives the head the
  tail's kind (`TakeKind`), so Backspace after a rule deletes the rule; `DeleteRangeEdit.Undo` puts the head's kind
  back from the fragment (`RestoreKind`). The styling menu's "Horizontal line" calls `InsertRule`: an empty block
  becomes the rule, otherwise one goes after the caret's block.
- **Alignment.** `TextAlignment { Left, Center, Right }` on `BlockControl.alignment`, `Align` on `<Block>`.
  `TextRunControl.Align` runs after `MeasureBlock` and `LayoutAround` and adds `(room − visible width) × 0/½/1` to
  each `TextLine.left`, where visible width leaves trailing spaces hanging and `room` is the new per-line width
  `MeasureAround` records (0 = the wrap width). Ctrl+L/E/R (`Text.AlignLeft/Center/Right`) and three toolbar
  buttons; `SetBlockAlignment` covers the selection's blocks as one `BlockStateEdit`.

- **Fences (2026-10-01).** `Read`:
  - opens on `fenceLine`: three or more `` ` `` or `~`, and a backtick fence's info string may not contain a backtick;
  - closes only on the same character, at least as many and nothing else (`ClosesFence`);
  - an unclosed fence runs to the end of the file.

  `Write` opens a run with a placeholder line and `CloseFence` rewrites it one longer than the longest
  all-backtick line inside the run, so a `` ``` `` line inside code survives. A paragraph starting `~~~` is
  escaped. Test: `TextInput.MarkdownFences`
- **Tab in code (2026-10-01)** types a real `\t`, not spaces (user). `DocumentControl.ShiftCodeIndent`, called
  first from the editor's `ShiftListLevel`:
  - no selection: a tab at the caret;
  - a selection: one tab more or fewer at the head of every Code block in it;
  - a `TextEdit` per line, so undo is one step.

  A table cell still steps cells. Test: `TextInput.CodeBlockTab`
- **Tab stops (2026-10-01, user: "tab stops").** A tab advances to the next multiple of
  `TabStopSpaces` × the run's space advance, counted from the line's start (`TextMeasurer.TabAdvance`; a pen
  already on a stop goes a full stop on). The advance depends on position, so:
  - the measurer flags tabs (`Tab` in `BlockLayout.flags` since [[rewrap-advance-cache]]) and `AdvanceAt` rewrites their advance as `MeasureBlock`, the wrap
    re-sum and `FillLine` reach them — `AppendLine` sums the rewritten values;
  - every walker in `TextRunControl` passes its pen-in-line to `MeasureAdvance(char, run, pen)`: `IndexAt`,
    `CaretAt` (`x - line.left`), `Align`'s visible width (now a forward walk), and `Emit`, which skips the glyph
    for a tab.

  Test: `TextInput.TabStops`

## Why these choices

**Alignment rides `TextLine.left`, which every geometry site already adds.**
The WIP note said alignment needed a block-level line-width pass that had not existed since the L2 revert. That
was true of the old stack, where each run measured itself. On the new stack a paragraph is one `TextRunControl`
and `MeasureBlock` hands back every line with its width, and picture wrapping had already made `left` a term in
`Emit`, `IndexAt`, `CaretAt`, `PictureBox` and the selection highlight. So alignment is a post-pass, not a new
layout. Justify is the exception — it spreads slack over the gaps, which all five sites walk advance by advance —
and was left out (user, 2026-10-01).

**Alignment is `.xml` only.** Markdown has no paragraph alignment. `<p align>` was rejected: CommonMark does not
parse Markdown inside an HTML block, so a centred paragraph's bold would come back as literal `**`. `.md` and `.txt`
notes refuse it (`CanAlign`), the rule tables already follow.

**A rule is a text-less block, not a separate control.** A non-block control in the document (as tables are)
would have the caret skip it, but selection and deletion across a non-block is what tables still refuse. As a block
it is addressed, snapshotted, undone, paginated and deleted by the code that already exists, at the price of two
special cases (`LeaveRule`, the rule-head merge).

**Code font is data on the style scheme.** `TextStyle.fontName` beside `fontSize`, resolved like it, so a note or app
can name another monospace font. Resolution happens at `BuildRuns` (`FontFor`), never written onto spans — a span's
`fontName` is authored and saved, so filling it would pin `consola` into every note.

**Inline undo hands back the markers.** One step per keystroke, so Ctrl+Z after `**b**` gives `**b*` — the same
shape the list prefix already had.

## Known gaps
- ~~Syntax colouring and no-wrap code (B1's other half) not built~~ — landed 2026-10-02 as view-time colouring and a
  `Wrap` setting on the code block; see [[code-block-colouring]].
- Tab stops are a fixed `TextMeasurer.TabStopSpaces` (4) space widths of the tab's own run, measured from the
  line's start, not from the block's or a list's indent. They are not configurable.
- ~~Text typed straight after an inline `` `code` `` stays code~~ — `StyleDelta.code` (2026-10-02): `true` sets a
  span's `stylingType` to `Code`, `false` turns a Code span back to `Inherit` with an unauthored size; the closing
  backtick arms `code: false`. Test: `TextInput.MarkdownInline`
- ~~A text drop onto a rule is not redirected~~ — `DropSelection` and `InsertAt` go through `OffRule(address)`, the
  address form `LeaveRule` now calls (2026-10-02). Test: `TextInput.DropOntoRule`
- `.md` writes a rule as `---`; under a paragraph line other Markdown readers take that as a setext heading 2.
  Kept on purpose (user, 2026-10-02): Thorium's own reader takes it as a rule.
- **Justify (2026-10-02).** `TextAlignment.Justify`; `TextRunControl.Justify` gives every line but the block's last
  `spaceExtra` = (room − visible width) / spaces before `justifyEnd` (the last visible character), and grows
  `line.width` by the stretch. A line holding a tab is left alone. `Stretch`/`Stretched` add it in every walker —
  draw, `WriteHighlight`, `IndexAt`, `CaretAt`, `PictureBox`, `MathBox` — because `LineSegment` is a readonly
  struct and its `width` stays the measured one. `BlockLayout.NextLine` clears both fields. Ctrl+J
  (`Text.AlignJustify`), toolbar `align-justify` icon. `.md` does not store alignment. Tests: `TextInput.JustifyLines`
  (+ golden)
- **Justify under optimal breaks and before a display formula (2026-10-06, L7a).** When the block's `OptimalBreaks`
  is set (`DocumentLayout.optimalBreaks`, LaTeX previews only), `TextRunControl.Justify` lets `spaceExtra` go
  negative — a space shrinks, bounded by a third of the line's inner space width. A line whose next line opens with
  a display math span is skipped (`BeforeDisplay`), like a last line; this applies to notes too (user, fork L7a-F3).
  Test: `Tex.JustifyBeforeDisplay`. See [[latex-editor]]
- **Soft hyphen U+00AD (2026-10-06, L7b).** In any note a soft hyphen takes no room (`TextMeasurer.MeasureAdvance` returns 0) and is a
  break opportunity; a line that breaks at it shows a "-" (`TextLine.hyphen`, counted in `line.width`), and `DocumentControl.CopySelection`
  drops it. Tests: `Tex.SoftHyphen`. See [[latex-editor]]
- **First-line indent, space above, spacer runs (2026-10-06, L7c).** A block carries `BlockControl.firstIndent` (px; `FirstLineIndent` = `firstIndent * textZoom`,
  moves line 0 only) and `spaceBefore` (float?, null = the layout's block spacing), written as `<Block Indent SpaceBefore>`. Both are carried through Enter
  (the tail keeps both), Backspace/merge (`TakeKind`), undo snapshots and plain-text/fragment paste, like `alignment`; no UI sets them. `<Run Space="px"/>`
  is a spacer: a fixed-width no-break space whose characters advance `Space` and draw nothing (`StyleSpan.spaceWidth`); text typed beside one goes into a
  text span (`BlockControl.Typable`) and a spacer copies as spaces. Tests: `Tex.BlockSpacing`. See [[latex-editor]], [[document-pages]]

Related: [[note-file-formats]], [[list-markers]], [[document-structural-editing]], [[document-undo]], [[note-images]], [[document-format-bar]]
