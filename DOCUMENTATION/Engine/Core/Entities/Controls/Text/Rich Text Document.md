---
Status: Current
tags:
  - Engine
  - d_UI
  - d_Data
  - d_XML
  - d_Filing
Class:
  - "[[RichTextDocument]]"
Type:
  - Public
---
## Description
The note document model for the Periodic (Obsidian-style) editor. It is the source of truth for a note and also its on-disk format, serialized as engine XML (not markdown, not JSON). The control tree is a view synced from this model; editing mutates a working copy of it rather than the controls directly.

This is the data half of a model / view / edit-session split: `RichTextDocument` (data) → a document view control (P2) → `DocumentEditSession` (P3 working copy + cursor). The split is deliberate so the later online multi-editor goal can layer a sync model onto the same data without touching the view.

## Model shape
A document is a flat list of blocks; a block of flowing text holds a list of inline runs.

- `RichTextDocument` — `List<Block> blocks`. `[A_XSDType("Document")]`, category `UI`. The whole tree is now `VulkanControl`s, so a document is a control tree laid out like `UI.xml`.
- `Block` (abstract, no `[A_XSDType]`) inherits [[TextBlockControl]] (a `PanelControl` derivative that flows runs) → `ContentBlock` (abstract, holds `List<TextRun> inlines`) → `ParagraphBlock` (`[A_XSDType("Paragraph")]`), `HeadingBlock` (`[A_XSDType("Heading")]`, `Level` 1-6). All category `UI`.
- `TextRun` (`[A_XSDType("Run")]`) → inherits [[TextInputControl]]: `Text`, `Bold`, `Italic`, `Strikethrough`, `RunColorHex`, `FontName`, `FontSize` all come from the control, so the run adds no fields of its own (there is no `Inline` base anymore).
- `Block` carries no `[A_XSDType]` so it is never emitted as an element — it is an `AllowedChildren` target the [[XSDGenerator]] scans for concrete blocks; content blocks use `typeof(TextInputControl)` as their inline `AllowedChildren` (expands to the one `[A_XSDType]` subtype, `Run`).
- `Clone()` on the document / blocks / inlines is a deep copy — used to make the isolated working copy the editor edits before a save.

Planned (not yet in code): `TextRun` gains `FontSize` (0 = inherit block default), `Underline` and a highlight color — mixed fonts/sizes word-by-word are just adjacent runs, with `StyleEquals` merging same-styled neighbours on edit. New blocks arrive via the same free round-trip: `CodeBlock` (Language attribute, monospace, no wrap; syntax coloring is computed at view time and never persisted) and `TableBlock` → `TableRow` → `TableCell` where a cell holds `List<Block>` (nested blocks; MVP fixed/star columns, no merges).

## Persistence — `DocumentXml`
Load and save are attribute-driven reflection (the same pattern as [[Vulkan Control]] `ParseXML`), so new blocks / inlines / run styles round-trip automatically once they carry the attributes. NOTE: the binary [[Serializer]] is unrelated — notes are XML, never routed through `Serializer`.

`RichTextDocument` implements [[XSDGenerator|IXMLParser]]`<RichTextDocument>`; the string argument is a file path (resolved by the vault), not a `Paths.Doc` name.

#### Load (path)
parse `XDocument` from `path`
return [[#Parse Element]] (`root`) as `RichTextDocument`

#### Parse Element (element)
`type` = [[AnyXMLType]]`.FindType`(`element` local name)
`node` = create instance of `type`
[[#Apply Attributes]] (`element`, `node`)
for each `child element` in `element`
	[[#Attach Child]] (`node`, [[#Parse Element]] (`child element`))
return `node`

#### Apply Attributes (element, node)
for each `member` of `node` with `[A_XSDElementProperty]`
	`attribute` = `element` attribute matching `member` name (case-insensitive)
	if `attribute` exists
		set `member` = convert `attribute` value to member type (`TypeDescriptor`)

#### Attach Child (parent, child)
`list` = `parent` `List<>` field whose element type is assignable from `child` type
add `child` to `list`

#### Save (document, path)
`root` = [[#Write Element]] (`document`)
create directory of `path`
write `XDocument` (`root`) to `path`

#### Write Element (node)
`element` = xml element named `node` `[A_XSDType]` name
for each `member` of `node` with `[A_XSDElementProperty]`
	set `element` attribute (`member` name) = `member` value (invariant string)
for each `List<>` field on `node`
	for each `child` in field
		add [[#Write Element]] (`child`) to `element`
return `element`

## View — `DocumentEditorControl`
The read-only view over a document (the "controls as a view" half). It is a [[#^scrollable|ScrollableControl]] whose single child is a vertical [[StackPanel]] of one control per block; each `ContentBlock` becomes a [[TextBlockControl]] that flows one [[INPUT|TextInputControl]] per run (the run renderer). Headings scale font size by level; paragraphs use the body size.

This P2 presentation is **interim**: building every block (and reusing the editable `TextInputControl` as run renderer) does not scale past a few pages, since every character is a full control with its own GPU buffer. It is replaced in L2 by the virtualized view of the [[Document Layout Engine]] — geometry for the whole document lives in a layout cache, controls materialize only for the visible viewport, and read-only runs get a lightweight `TextRunControl`. All hit-testing (including mouse) moves to the cache; controls stop being hit-targets.

- `[A_XSDType("DocumentEditor", "UI")]` so it can be placed in `UI.xml`; a `Source` attribute names an engine-XML note to load (resolved via `Paths.Doc` when relative, used as-is when rooted).
- `LoadDocument(RichTextDocument)` — entry point used by the vault later; `LoadPath(name)` — load by file.
- The control tree lives in the engine's `Controls` render group. **Rebuild-on-edit (P3+) must remove stale run/glyph controls from that group** — the deferred-cleanup TODO already noted in [[INPUT|TextControl]]; build-once (P2) is unaffected.

#### Load Document (document)
`stack` = vertical [[StackPanel]] (invisible mask, stretch)
for each `block` in `document`
	`stack` add [[#Build Block]] (`block`)
set scrollable content = `stack`

#### Build Block (block)
if `block` is not a `ContentBlock` return null
`size` = heading ? size-for-level : paragraph size
`text block` = [[TextBlockControl]] (stretch)
for each `run` in `block`
	`run control` = [[INPUT|TextInputControl]] (size, run style flags)
	`run control` text = `run` text   // builds glyphs at the set size
	`text block` add `run control`
return `text block`

## Status
- P0 (model types) and P1 (XML persistence) complete; round-trip verified (in-code build + reload of code-built and hand-authored XML are byte/structurally equal).
- P2 (read-only `DocumentEditorControl`) implemented; built into `Periodic/Data/XML/Documents/UI.xml` via `<DocumentEditor Source="SampleNote.xml"/>`. Pending manual GUI verification.
- Lists, quotes, dividers, wiki-links, inline code: not yet — added as the editor grows. Code blocks and tables are scheduled (B1/B2), after the [[Document Layout Engine]] phases (L1/L2) and editing (P3/P4). Revised phase order: `DOCUMENTATION/ClaudeMemory/Context/periodic-editor-architecture.md`.
