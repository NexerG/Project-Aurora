# Styling types replace heading block classes

Landed 2026-08-08. Supersedes `HeadingBlock`/`ParagraphBlock` and `HeadingStyle`.

## What changed

A heading was a class (`HeadingBlock : ContentBlock` with `Level`). It is now a value: `ContentBlock`
is the only concrete block and carries `stylingType`, and the styles file says what that looks like.

| Before | After |
|--------|-------|
| `ParagraphBlock` / `HeadingBlock` | `ContentBlock`, `[A_XSDType("Block")]` |
| `<Heading Level="2">` / `<Paragraph>` | `<Block StylingType="Heading2">` / `<Block>` |
| `HeadingStyle (level, fontSize)` | `TextStyle (type, fontSize)` |
| `DocumentLayout.paragraphFontSize` | the `Text` entry in `textStyles` |
| `FontSizeFor(Block)` | `FontSizeFor(TextStyleType)` |

`TextStyleType`: `Inherit, Text, Heading1-6, Comment, Code, Quote`. Members with no entry in the
styles file are legal — see the resolution order below.

## Where the setting lives, and why in two places

`stylingType` is declared **twice**: on `TextControl` (default `Inherit`) and on `ContentBlock`
(default `Text`). `TextBlockControl` is a *sibling* of `TextControl` under
`AbstractContainerControl`, not a subclass, so there is no single place below
`AbstractContainerControl` that reaches both — and putting document styling on
`AbstractContainerControl` would hand it to every `StackPanel` in the engine.

`ContentBlock.ApplyLayout` resolves: a run's own type wins, `Inherit` falls back to the block's. Same
copy-down shape `lineHeight` and `fontSize` already used.

**Rejected: styling type on runs only** (the literal reading of "a heading is just a glorified text
run"). Two runs on one line could then disagree about being a heading; "make this line an H2" would
rewrite every run in it; and block spacing, document outline/folding, and "Enter at the end of an H1
gives you body text" would all have to interrogate the runs to find out what the line is. Heading is
a line-level role. The separate *class* was the thing worth deleting, not the block-level identity.

## Resolution order

`FontSizeFor(type)`:
1. `Inherit` is read as `Text`.
2. The note's own `textStyles` if non-empty, else the editor's (`DocumentSettings` via the settings
   registry — was `DocumentStyles.xml` via the VFS, see [[settings-registry]]).
3. Exact `type` match.
4. If `type` is a heading and no exact match exists, **the last heading entry in the list** — user's
   rule, and a change from the old "nearest defined level".
5. `fallbackFontSize` (18).

Step 4 is heading-only on purpose: an unlisted `Comment` falling back to body text is right, an
unlisted `Heading9` collapsing to body text is not.

## Load-bearing details

- `Block` stays abstract with no `[A_XSDType]`, so it remains the `allowedChildren` target the
  generator scans; `CodeBlock`/`TableBlock` arrive as siblings of `ContentBlock`, not subclasses.
- `TextControl.stylingType` has no setter logic. It is resolved into `fontSize` by `ApplyLayout`, and
  `fontSize`'s setter is what repoints the glyphs — a styling type that never reaches `ApplyLayout`
  changes nothing on screen.
- `Text` is the block default and `Inherit` the run default, so with [[xml-save-skips-defaults]]
  neither is ever written. A note is `<Block>` for body and `<Block StylingType="Heading1">` for a
  heading, with nothing on the runs.
- `TextMeasurer.MeasureBlock(ContentBlock, ...)` was ported to per-run resolution but **has no
  callers** — dead since the L2 revert, when each control started measuring itself. Left in place
  rather than deleted (pre-existing dead code).

## Verified

36/36 on the scratchpad harness (see [[xml-save-skips-defaults]] for how it boots).

- `Text` -> 18, `Heading1` -> 34, `Heading2` -> 28, `Inherit` -> 18 against the shipped scheme.
- Scheme stopping at `Heading3`: `Heading3` exact -> 20, `Heading6` clamps -> 20, `Comment` falls
  back to 18 rather than clamping.
- Block `Heading1` with two runs: the `Inherit` run gets 34, the `Text` run gets 18, both get
  `lineHeight` 1.5.
- Round-trip: `StylingType="Heading1"` kept, `StylingType="Text"` omitted as the default, block
  styling types survive reload.

## Open

- `TextStyle` carries only `fontSize`. Font, weight, colour and per-type block spacing are the
  obvious next fields and need no format change — add attributes and they cascade.
- `ControlColor` and `ColorHex` both serialize when a run uses the enum form (`ControlColor="gray"`
  writes `ColorHex="#808080"` too, since the enum setter writes the hex). Same duplicate-state shape
  `runColorHex` had, one level up on `VulkanControl`. Harmless while they agree; load order decides
  if a hand-edit makes them disagree.
