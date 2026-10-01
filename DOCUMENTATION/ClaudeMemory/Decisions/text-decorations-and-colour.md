# Decision — decorations are quads the run emits, highlight leaves a gap for the selection, and colour is picked in an engine control

**Date:** 2026-09-29
**Status:** LANDED. Test-verified (`TextInput.UnderlineToggles`, `HighlightApplies`, `StyleSurvivesEnterAndBackspace`,
`ColorPickerPicks`, `MarkdownColourRoundTrip`) and golden-verified (`DecorationsDraw.Decorations`,
`ColorPickerPicks.ColorPicker`, both viewed before approval). **NOT GUI-verified:** the format-bar dropdowns and the
picker inside a menu have not been clicked in a running app.
**Scope:** `ArctisAurora.Core.UI` — `StyleSpan`, `Run`, `StyleDelta` (`Of`), `CaretStyle`, `TextRunControl`
(`WriteHighlight`, `WriteRect`, `selectedFrom/To`, `kind`), `DocumentControl` (`KeepDeletedStyle`, `DisarmStyle`,
`ArrangeSelection`), `BlockControl.SplitAt`, `ColorPickerControl`, `ContextMenuContent`, `DocumentToolbarControl`,
`Gradients.LoadGradients`, `MarkdownFormat`; `AuroraEngine/Data/XML/Documents/Engine.gradients.xml`

## What changed
- `StyleSpan` gains `underline` and `highlightHex`; `strikethrough` is now drawn. `Run` writes `Underline` and
  `HighlightHex`. `StyleDelta`/`CaretStyle` carry both; `StyleDelta.Of(span)` is a delta giving another span the same look
- `TextRunControl.Emit` writes, per line segment: the highlight rectangle **before** the glyphs (at the control's own
  z, behind them), then an underline and/or strike rectangle after them. Rectangles are `PanelControl` rows with
  `noTexture`; geometry is a fraction of the font size (`underlineDrop`, `strikeRise`, `decorationWeight`)
- `DocumentControl.ArrangeSelection` writes each selected block's `selectedFrom/To`; `WriteHighlight` leaves that
  range out, using `CaretAt` for the gap edges
- Format bar: an underline icon button (Ctrl+U, `Text.Underline`) and a highlight icon button whose dropdown is
  None + six presets + the picker. The colour dropdown gains the picker below its eight entries
- `ColorPickerControl` (`<ColorPicker>`): an S/V field (hue quad + `picker-white` + `picker-black` washes), a
  `picker-hue` strip, filled handles, a swatch and a hex `TextBoxControl`. `onPicked` fires on drag release and on a
  committed hex. `ContextMenuContent` hosts it in a menu; a press inside a menu never closes it
- `Gradients.LoadGradients` loads `Engine.gradients.xml` first, then the host's `Gradients.gradients.xml`
- **A chosen style survives Enter and Backspace:** `SplitAt` gives an emptied head or tail the style at the split
  (was the block default). `DeleteSelection` arms the first deleted character's style when it differs from the
  caret's (`KeepDeletedStyle`; an explicit arm wins). The arm is dropped by navigation — `TextInputActions.Move`,
  `PressAt`, multi-tap, `SelectAll`, the editor's gutter press, `Undo`/`Redo` — and after a paste or drop
- `TextRunControl` declares `kind = MTSDFControl` (user, 2026-09-29): it never draws a panel, but the default
  `PanelControl` kind made `GroundBelow` hand its **ink** to its children as their ground, so markers picked the
  light ink. Closes the WIP "bullet dot near-white" bug
- Markdown (Obsidian): colour → `<span style="color:#hex">`, default highlight → `==…==`, other highlights →
  `<mark style="background:#hex">`, underline → `<u>`. Reads those plus `background-color:` and `#RRGGBBAA`
  (alpha dropped); other HTML stays text. `Compose` markers are open strings closed by `Closing`; the re-read
  check compares a style key including colours

## Why these choices

**Decorations are rows the run emits, not child controls.** Glyphs already are; a control per decoration would
be per segment per line and churn on every rewrap.

**The highlight leaves a gap rather than the selection moving on top.** Selection boxes sit at the head of the
document's children so they draw behind the text; a run's highlight, emitted later, would cover them. Drawing
the selection over the text instead would change how every selection looks.

**The picker's square is three quads and two fixed gradients.** Only the base quad's colour changes with the
hue, so nothing texture-based is rebuilt while dragging. A texture regenerated per hue would churn the never-freed
table texture slots (`note-images`).

**Engine gradients in their own file.** Document lookup is app first, engine fallback — one file per name — so an
engine `Gradients.gradients.xml` would be shadowed by any host that ships one.

**Handles are filled, not rings.** `PanelControl` defaults to the `Clear` role, and `PaintRow` zeroes a Clear
quad's alpha, edge included — a transparent-centre ring is not expressible without a new paint rule.

**The arm dies on navigation, not on every `SetCaret`.** Edits move the caret too (`DeleteBetween`,
`SplitBlockAt`, `InsertText`), and Backspace builds its range with `MoveCaret` — clearing in either would kill the
arm exactly where it has to survive.

## Known gaps
- Picker has no alpha channel; `#RRGGBBAA` from Obsidian loses its alpha
- A highlight over a gradient run still uses its own flat colour; a highlight on a table cell's clipped overflow is not tested
- The engine's own `Data/Icons/default` atlas stamp is stale (hosts bake their own); a host without one would re-bake it

Related: [[armed-style-at-the-caret]], [[text-clipboard]], [[list-markers]], [[ui-gradients]], [[note-file-formats]], [[ui-palettes]]
