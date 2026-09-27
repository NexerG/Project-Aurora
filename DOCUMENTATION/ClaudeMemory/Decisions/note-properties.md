# Decision — note properties: parsed frontmatter, per-note palette, dates, shown in an expander at the top of the note

**Date:** 2026-09-27
**Status:** LANDED, partly GUI-verified — see Known gaps. Solution builds clean; scratch harness (`NoteFormats.cs` +
`Frontmatter.cs` compiled alone, 23 fixtures) passes.
**Scope:** `ArctisAurora.Core.UI` — new `Frontmatter`, `ExpanderControl`, `NotePropertiesControl`; `MarkdownFormat`
(`properties`, `IsProperty`, `ReadProperties`, `PropertyHolder`), `RichTextDocument` (`palette`, `created`, `modified`,
`frontmatter`, `Stamp`, `Load`), `DocumentEditSession.Save`, `DocumentXml` (tolerant parse), `DocumentControl.header`,
`DocumentEditorControl` (`SetPalette`, `ApplyPalette`, `SetLayout`, `SetFrontmatterValue`), `TextInputActions.Editor`;
`AuroraEngine/Data/Icons/default/svg/chevron-up.svg`

## What changed
- **Frontmatter** (`.md` only): a YAML `---`…`---`/`...` or TOML `+++`…`+++` block at line 0 is split off by
  `Frontmatter.Split` and kept whole, delimiter lines included, as `RichTextDocument.frontmatter` (`Frontmatter`
  attribute on `<Document>`). No closing line → literal text, as before.
- **Owned keys** (`MarkdownFormat.properties`, case-insensitive) map onto the tree: `Palette`, `Created`, `Modified` →
  `<Document>`; `LineHeight`, `BlockSpacing`, `ListIndent` → `<DocumentLayout>`; `PageMode`, `PageSize`, `Landscape`,
  `PageWidth`, `PageHeight`, `Margin*`, `PageGap` → `<Page>` (`Mode`, `Size`, …, `Gap`). Flat keys, so neither syntax
  needs nesting. `Name` is **not** owned — a Markdown note's name is its file name.
- **Write-back** (`MarkdownFormat.Write`): per owned key, `Frontmatter.Set` — unchanged value → block untouched;
  changed → that key's line(s) replaced, existing key casing kept; new → inserted before the closing line (TOML: before
  the first `[table]`); absent from the tree (default, per [[xml-save-skips-defaults]]) → line removed. No block and a
  value to write → a YAML block is created. Unknown keys, comments and order are never touched.
- **Parser subset:** YAML `key: value` (plain, `'…'`, `"…"`, trailing ` #` comment), `[a, b]` inline lists (editable,
  raw), `- item` block lists / nested maps / `|` `>` scalars (read-only, flattened for display). TOML `key = value`,
  strings, numbers, bools, single-line arrays; multi-line arrays/strings read-only; a `[table]` and everything after it
  is one read-only entry.
- **Dates:** ISO 8601 with offset (`RichTextDocument.Stamp`), `xs:string`. `Load` fills a missing `created` from
  `File.GetCreationTime`; `DocumentEditSession.Save` stamps `modified` only when `isDirty`. Every saved `.md` note
  therefore gains a block (user, fork B). `.xml` notes carry them as `<Document>` attributes.
- **Per-note palette:** `RichTextDocument.palette`. `DocumentEditorControl.ApplyPalette` sets `paletteName`, role
  `Ground`, alpha 1 when the name is in `Palettes.Names`; otherwise transparent on the app palette and a `Warn`
  (`Palettes.Get` throws on an unknown name, so the check comes first).
- **`DocumentXml` parses tolerantly** (user, fork C): every `ApplyAttributes` in `Parse`/`ReadLayout`/`ReadBlock` passes
  `tolerant: true`, so a bad attribute logs and keeps the default instead of failing the load.
- **Header:** `DocumentControl.header` — measured at the text width, arranged at the top margin of page 1; `Paginate`
  starts at `top + headerHeight`. `DocumentEditorControl.LoadDocument` builds `ExpanderControl` →
  `NotePropertiesControl` for `.md` and `.xml` sessions, none for `.txt` or a session-less load.
- **`ExpanderControl`** `<Expander Expanded>`: two 1px `Line` rules and a 24px round grip split into halves. Closed, the
  halves meet on the rule (↑ over ↓); `Toggle` tweens `reveal` 0↔1 (0.2 s, CubicOut) and the lower rule and half slide
  down with the content between them, arrows crossfading to point inward. The first authored child is the content.
- **`NotePropertiesControl`**: rows Created, Modified (read-only), Palette (`DropdownControl`, "App default" + names),
  Line height / Block spacing / List indent (`TextBoxControl`, applied on Enter or blur, bad value put back), then (`.md`)
  every non-owned key — editable text box or read-only label. `Refresh` after every editor `Save`.
- **`TextInputActions.Editor()`** returns null when the walk passes an editing `TextBoxControl`, so a field inside a
  note gets the keystrokes instead of the note.

## Why these choices

**Parsed, not opaque (user, fork F1).** The keys drive the note — palette, layout, page — so `.md` gets the per-note
settings only `.xml` had. The raw block is still kept (user, "keep frontmatter") so tags, aliases and comments round-trip
byte-identical; only owned keys whose value changed are rewritten.

**Custom subset parser, not YamlDotNet/Tomlyn.** No new NuGet dependency, and neither library writes back a block with
comments and key order untouched. Anything outside the subset degrades to read-only, never to an error.

**Owned keys reuse the tree.** Frontmatter values become attributes on the same `<Document>`/`<DocumentLayout>`/`<Page>`
elements an `.xml` note has, so `DocumentXml.Parse` is still the only code building a note, and `WriteScalars`'
skip-defaults decides what the block keeps.

**Dates as strings.** No DateTime entry in `XSDGenerator.MemberMap` / `AnyXMLType.typeMap`; adding one means keeping
both maps in sync for one feature.

**The grip is a full circle behind a half-height clip.** `sdRoundBox` clamps each corner radius to
`min(halfExtent)`, so a 24×12 panel cannot be a semicircle (it drew a rounded square). Each `Half` is a 24×12 clip over
a 24×24 `Circle` button offset up or down — no shader change, and the clip also drops the stroke on the flat side.

**Inside the first page (user, fork G), in an expander at the top (user, fork A)** — the header paginates with the
text rather than sitting outside the paper.

## Known gaps
- **Text boxes in the header do not take a press.** `TextRunControl.OnPointerPress` walks up to the nearest
  `IGlyphPressTarget`, which is now `DocumentControl`; it consumes the press, so the `TextBoxControl` never starts
  editing. Line height / block spacing / list indent and editable frontmatter values are therefore not editable in
  the GUI. Proposed one-line fix in `TextBoxControl.FieldLine` (press returns false) awaiting the user.
- GUI-verified 2026-09-27 in Thorium: frontmatter hidden from the body, rows listed (block list read-only), unknown
  palette warns and falls back, palette pick repaints only that note and survives a restart, Ctrl+S rewrites only the
  palette line and adds `Created`/`Modified`, `.xml` note gets the three attributes, grip is a circle that splits into
  semicircles with flipped arrows. **The animation itself was not captured mid-motion** — only end states.
- Palette, layout and frontmatter edits are not undoable; adding or removing keys from the panel is not supported.
- A duplicated note keeps the source's `Created`; a copied file gets a new one from disk.
- Normalizations on save: an owned key equal to its default is removed; an edited line loses its trailing comment;
  CRLF inside the block becomes LF.
- A header taller than a page overflows page 1. Context menus from the note (the palette dropdown) use the app palette.
- The expander state is not remembered per note.

Related: [[note-file-formats]], [[document-pages]], [[ui-palettes]], [[xml-save-skips-defaults]], [[document-format-bar]]
