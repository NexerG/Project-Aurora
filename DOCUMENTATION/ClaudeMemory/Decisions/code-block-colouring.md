# Decision — code blocks are coloured at view time and do not wrap unless the block says so

**Date:** 2026-10-02
**Scope:** `ArctisAurora.Core.UI` — `SyntaxTokenizer`, `SyntaxToken`, `SyntaxState`, `DocumentControl` (`HighlightCode`, `Tokenize`, `CodeWidth`, `contentWidth`), `TextRunControl.syntax`, `BlockControl` (`codeWrap`, `Wraps`), `BlockSnapshot.codeWrap`, `Palettes.Code`, `PaletteDefinition`, `DocumentXml`; `Thorium/Data/XML/Documents/Palettes/thorium-*.palette.xml`

## What changed
- `SyntaxTokenizer.Tokenize(line, language, state, tokens)` writes one `SyntaxToken` (`Plain`, `Keyword`, `String`,
  `Number`, `Comment`) per character and returns what the line leaves open (`SyntaxState`: block comment, XML
  comment, XML tag, Python `"""`/`'''`).
- Languages, by fence name: `cs`/`csharp`/`c#`, `glsl` (+ `vert`/`frag`/`comp`/`geom`/`tesc`/`tese`), `py`/`python`,
  `xml`/`xsd`/`html`/`svg`/`xaml`; anything else (none included) gets the C-like rules with a common keyword set.
  C-like: `//`, `/* */`, `"…"`/`'…'` with backslash escapes, C# `@"…"`, numbers (`0x1F`, `1.5f`), `#directive` as a
  keyword. Python: `#` comments, triple quotes. XML: tag names and brackets as keywords, attribute values as strings,
  entities as numbers, `<!-- -->`.
- `DocumentControl.HighlightCode` runs from `MeasureCore`'s dirty branch: each run of consecutive note-level Code
  blocks with one language is re-tokenized whole when any line is measure-dirty or has a stale array, carrying the
  state line to line. Non-code blocks get `syntax = null`.
- `TextRunControl.Emit` draws a character with `Palettes.Code(palette, token)` when the run has no authored colour
  or gradient and the token is not `Plain`. Nothing is saved.
- Palette: optional `Keyword`, `String`, `Number`, `Comment` on `<Palette>` (`keyword`, `stringLiteral`, `number`,
  `comment`). Four slots appended to every palette block (`codeBase`, `blockSize` + 4), so theme fades carry them.
  Unset → Accent for the first three, muted ink on `SubField` for Comment. The 12 `thorium-*` palettes set all four;
  the engine `default` uses the fallbacks.
- Wrap: `BlockControl.codeWrap` (`Wrap="true"` on a Code `<Block>`, written only for Code); `Wraps` is false for a
  Code block without it. Copied wherever `language` is (split, snapshot, restore, `TakeKind`, paste kinds).
  `.md`/`.txt` never carry it, so their code never wraps.
- An unwrapped line wider than the column is arranged at its own width (`CodeWidth`; its `SubField` ground goes with
  it), and `contentWidth` — the widest such line plus both margins, at least the paper — is the document's desired
  width, so the editor's own horizontal scroll reaches it; the page is centred on `contentWidth`.
- Tests: `Syntax.CSharpLine`, `Syntax.CommentSpansLines`, `Syntax.Languages` (engine suite `Syntax`);
  `TextInput.CodeColoursAndOverflow` (+ goldens `Coloured`, `Commented`); `TextInput.MarkdownBlocksDraw` re-approved
  for the coloured `for`/`0`/`10`.

## Why these choices

**Colour at view time, never in the file.** The tokens are a function of text + language; storing them would be a
second copy that every edit has to keep in sync, and Markdown has nowhere to put them.

**A run of lines is re-read whole.** A `/*` typed on line 1 recolours line 40; tracking per-line end states to stop
early was not worth it for code blocks of tens of lines. Typing outside code costs one walk of `children`.

**Optional palette fields with fallbacks** (user fork a). Every palette keeps loading unchanged; the colours are
filled per palette as a judgement call, not derived.

**No-wrap by default, the note widens** (user, 2026-10-02: "make it a setting on the code block (xml only), but
normally it does not wrap for txt and .md"; then "note scrolls sideways" over a per-group scroller or clipping).
Rejected: a sideways scroller per code group — as structural as tables (flattening, inserts into it, merge guards);
clipping at the text edge — the caret goes out of sight.

## Known gaps
- An unwrapped line runs past the paper over the page gap; the ground of a long line is ragged against its neighbours.
- No UI toggles `Wrap`; it is XML only, by request.
- Code in table cells is neither coloured nor widened (cells only get code from XML).
- Keyword lists are hand-picked, not exhaustive; contextual C# keywords (`value`, `get`, `set`) colour everywhere.
- Strings: C# verbatim `""` escapes and raw/interpolated strings split early; an apostrophe in an unnamed language opens a string.
- The engine `default` palette shows keywords, strings and numbers in one colour (Accent).
- **NOT GUI-verified** — the scroll to a long line's caret, live recolouring while typing, palette switching.

Related: [[markdown-blocks-and-alignment]], [[ui-palettes]], [[document-pages]], [[text-layout-one-measurer]]
