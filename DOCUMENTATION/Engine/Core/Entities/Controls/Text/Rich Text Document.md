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
The note document model for the Periodic (Obsidian-style) editor. It is the source of truth for a note and also its on-disk format, serialized as engine XML (not markdown, not JSON).

The model / view split it was designed around **is not what shipped**: blocks and runs are themselves `VulkanControl`s, so the model *is* the control tree and editing mutates it directly. That is the P0 decision — blocks are controls so the engine's UI layout lays a document out for free — and everything downstream follows from it, including why [[#Edit session — `DocumentEditSession`]] is not a working copy and why the whole UI is scheduled to split into data and visualization once Periodic and the profiler are up. The separation the online multi-editor goal wants is therefore still owed, and arrives with that split rather than here.

## Model shape
A document is a flat list of blocks; a block of flowing text holds a list of inline runs.

- `RichTextDocument` — `List<Block> blocks`. `[A_XSDType("Document")]`, category `UI`. The whole tree is now `VulkanControl`s, so a document is a control tree laid out like `UI.xml`.
- `Block` (abstract, no `[A_XSDType]`) inherits [[TextBlockControl]] (a `PanelControl` derivative that flows runs) → `ContentBlock` (`[A_XSDType("Block")]`), the one concrete block, whose runs are its children. A heading is not a class of its own: a block carries a `StylingType` (`Text`, `Heading1`-`Heading6`, `Comment`, `Code`, `Quote`) and the styles file says how big that is, so adding a level is data rather than a new type. All category `UI`.
- `TextRun` (`[A_XSDType("Run")]`) → inherits [[TextInputControl]]: `Text`, `Bold`, `Italic`, `Strikethrough`, `FontName`, `FontSize` all come from the control, so the run adds no fields of its own (there is no `Inline` base anymore). Colour is the ordinary `ColorHex` every control has — a run that sets it repoints the colour onto each of its glyph children, since the run itself draws an invisible mask and only the glyphs reach the screen.
- `Block` carries no `[A_XSDType]` so it is never emitted as an element — it is an `AllowedChildren` target the [[XSDGenerator]] scans for concrete blocks; content blocks use `typeof(TextInputControl)` as their inline `AllowedChildren` (expands to the one `[A_XSDType]` subtype, `Run`).
- `Clone()` on the document / blocks / inlines is a deep copy. It was written for the working copy the editor was going to edit before a save; that is not what the edit session does, so its only callers now are `DocumentLayout.Clone` and whatever wants an independent copy of a note.

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
`root` = [[#Write Element]] (`document`, `document` layout)
create directory of `path`
write `XDocument` (`root`) to `path`

#### Write Element (node, layout)
`element` = xml element named `node` `[A_XSDType]` name
for each `member` of `node` with `[A_XSDElementProperty]`
	if `member` value equals the value on a fresh instance, skip
	if [[#Is Resolved]] (`node`, `member`, `layout`), skip
	set `element` attribute (`member` name) = [[#Format]] (`member` value)
for each complex `member` of `node`
	`child` = [[#Write Element]] (`member` value, `layout`)
	if `child` has attributes or elements, add `child` to `element`
for each `List<>` field on `node`
	for each `child` in field
		add [[#Write Element]] (`child`, `layout`) to `element`
return `element`

#### Format (value)
if `value` is a bool, return "true" or "false"
return `value` as invariant string

#### Is Resolved (node, member, layout)
if `node` is a `TextRun` and `member` is `fontSize`
	`type` = `run` styling type, or the owning block's when it inherits
	return `run` font size equals `layout` font size for `type`
if `node` is a `VulkanControl` and `member` is `controlColorHex`
	return `controlColor` differs from its default and `controlColorHex` equals its hex
return false

A saved value has to be one somebody wrote. Two mechanisms keep computed values out of a note: the ordinary one is the fresh-instance default (see [[xml-save-skips-defaults]]), and the second is `Is Resolved`, for values a *setter* computed, which no default check can catch because the computed value is not the default one.

There are two. `ApplyLayout` writes the styling scheme's size into every run's `fontSize` the moment a note is loaded — a Heading1 run holds 34 where a fresh run holds 16 — so without the check the first save stamps `FontSize` onto every run and pins the note to whatever the scheme said that day. And `controlColor`'s setter resolves into `controlColorHex`, so a run that named a colour holds both, and saving both pins the note to today's meaning of "gray".

The colour case has a trap the font size case does not: the hex may only be skipped when the *enum* is itself being written. `ControlColor`'s default is `red`, so a control that never named a colour has the enum omitted by the default check — dropping the hex as well would lose an authored `ColorHex` entirely on the next load. A run that deliberately authored the size or the hex the scheme already resolves to loses its explicit attribute, which is the acceptable half of the trade: it reloads to the same value either way, and a second save is byte-identical to the first.

`Format` exists for one reason: `xs:boolean` has no `True`. `Convert.ToString` spells a bool the C# way, so an unfixed save writes `Bold="True"` and the note fails the schema it names in its own `schemaLocation`. It still loads — the reader converts case-insensitively — which is exactly why this went unnoticed until a save was reachable from the UI.

## View — `DocumentEditorControl`
The editable view over a document. It is a [[#^scrollable|ScrollableControl]] over a `DocumentControl`, which stacks the document's **own** block controls — it builds nothing of its own, since the blocks and runs in the model are already controls. `ApplyLayout` resolves each run's styling type into a font size through the scheme before they are stacked, and the caret is a child of the `DocumentControl` so it scrolls with the text.

This presentation does not scale past a few pages, since every character is a full control. The replacement was going to be L2's virtualized view over a layout cache; that was **built and reverted** on 2026-08-07, because virtualizing the view removed a second parallel set of glyphs and left the first one — parsing a note builds every glyph before any view is consulted, so the model alone is already past the descriptor array's 50,000 slots on a 400-block note. There is now one layout path for all text and the control tree is the hit-test again.

What replaces it is engine-wide rather than document-local: the UI splits into **data and visualization**, most of the UI becoming data and controls becoming the thing that draws it, **after** Periodic's first version and the test/profiling platform are up. See `DOCUMENTATION/ClaudeMemory/Decisions/ui-data-control-split.md` and [[Document Layout Engine#Status]].

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

## Edit session — `DocumentEditSession`
An open note is `{ document, path }` and a `Save()` that writes the one to the other. It is deliberately **not** a working copy, even though the description above and the original plan both say it is: the L1/L2 rework left the model *being* the control tree, so a second copy would have to be a second control tree — one `GlyphControl` per character, twice — and the note about the glyph ceiling already says that number is being leaned on. A revert is therefore a reload from disk rather than a discarded clone. When per-note undo arrives it will be an edit log over the live tree, not a shadow copy of it.

`DocumentEditorControl.LoadPath` creates the session; `Save()` on the editor forwards to it. `LoadDocument` on its own leaves the editor sessionless and unsavable, which is what a preview of a document that came from nowhere should do.

## Caret movement
The caret is `(run, cursorPosition)` — the offset lives on the run, and `DocumentControl` remembers which run holds it. Movement splits in two by what the move actually asks:

- **Left / right** walk runs in document order. A run boundary *inside a block* is one caret slot and not two, because the end of one run and the start of the next resolve to the same point — the runs share a visual line through the `firstLineOffset` / `lastLineEndX` handshake. So a step across it lands past the duplicate. A block boundary is two slots, because the runs are on different lines.
- **Up / down, line start / end, page up / down** are all one primitive: resolve a point. `CaretAtPoint` scores every *line of every run* — not every run — because a visual line spans runs, so the run holding the line's start is routinely not the run the caret is in. A line closer in y always wins and x only breaks ties within a band, which is what makes line start and line end land on the right run rather than on the current one.

Line start and line end are the **visual** line's, not the paragraph's, which is the only reason the point primitive is needed at all. Page up and down move by one viewport height, which is why they live on the editor rather than on `DocumentControl` — the scroll viewport is the editor's.

Every move ends in `ScrollIntoView` on the caret's rect, and a move into a different run repoints `UICollisionHandling.activeControl`. That last part is not cosmetic: `Text.Write` drains characters into whatever the collision handler last made active, so a caret that arrowed into a new run without repointing it would type into the run that was clicked.

## Selection
A selection is two caret slots — an **anchor** where the press or the shift-extend started, and a **focus** where the caret is now. The focus is not stored separately: `caretRun` and `cursorPosition` already are it, so the only new state is the anchor, and `anchor == focus` is both "nothing is selected" and the plain-caret behaviour that existed before.

Slots are **normalized on write**, which is what makes that equality mean anything. The end of a run and offset 0 of the next run inside one block are the same point on screen — the block hands the next run the x the previous one ended at — so the two would compare as different while sitting on the same pixel, giving a phantom selection at every run boundary. `Normalize` walks a slot forward past any run end that has a following run in the same block, so one point is one pair. Rightward caret movement gets that rule for free; leftward has to do it itself, since normalizing only ever moves forwards.

Ordering the two ends needs reading order, and a run does not know where it sits in the document, so `OrderedRuns` is walked and the ends compared as `(run index, offset)`. This is what lets a drag run backwards.

Extending rather than collapsing is a boolean carried through the moves that already existed — `MoveCaret(move, extend)` and `SetCaret(run, offset, extend)` — and the boolean comes from the `Extend` [[INPUT#Named modifiers|named modifier]], not from a key the engine names. Shift is only what `InputMap.xml` happens to bind it to.

#### Highlight (run, from, to)
for each `line` of `run` layout
	clip [`from`, `to`) against the `line`'s character span, skip if empty
	`left` = clip starts the line ? the line's own left : `run` [[#Caret At]] (clip start) x
	`right` = clip ends the line ? the line's left + its width : `run` [[#Caret At]] (clip end) x
	arrange the next box at (`left`, `line` top) sized (`right` - `left`, `line` height)

One box per **visual line**, not one per selection, since a wrapped range is several rectangles. The x span comes from `CaretAt` — the same function that places the caret — so a highlight cannot drift from the caret drawn inside it. The one place it cannot be used is a wrapped line's final slot, which belongs to the line *below* by the caret-affinity rule, so the line's own width is the right edge there instead.

The boxes are `SelectionControl`s, a `PanelControl` exactly as `CaretControl` is. Two properties of them are load-bearing rather than incidental: they are **reused and arranged to nothing** when unneeded, because creating and destroying them as the mouse moves costs a pool allocation and a full paint-order permute every tick; and they are **inserted at the head of the child list**, because paint order is the tree's DFS order — which is why the caret, added last, draws over the text, and why a highlight added last would cover the letters instead of sitting behind them.

Drag runs on the engine's drag lifecycle: the editor calls `StartDrag()` from its click handler, `ResolveDrag` then arrives every tick until the button comes up, the focus follows the mouse through `CaretAtPoint`, and a mouse past the viewport edge scrolls by the overshoot. Reading `InputHandler.mousePos` rather than the position handed to `ResolveDrag` is the same one-frame-lag workaround the click path carries.

## Editing
There is one deletion primitive — a range between two caret slots — and Backspace, Delete, typing over a selection and Enter's leading collapse all go through it. Backspace and Delete with nothing selected build the range they need by extending the caret one move and deleting the result, so "the character before the caret" is never spelled out a second time: the rules about which side of a run boundary a slot lives on, and about a block boundary being two slots where a run boundary is one, stay in [[#Caret movement]] where they were already written.

The block merge falls out of that rather than being handled. Backspace at the start of a block extends the caret to the previous block's end, which is a range of zero characters spanning two blocks — and a range spanning two blocks collapses into the first by definition. Delete at the end of a block is the same range approached from the other side.

#### Delete Range (from, to)
if `from` run is `to` run
	remove [`from` offset, `to` offset) from the run's text
	place the caret at (`from` run, `from` offset) and return
`from` run text = its text before `from` offset
`to` run text = its text from `to` offset
destroy every run strictly between them in reading order
if the two runs are in different blocks, [[#Merge Into Head]] (`from` block, `to` block)
if `to` run is now empty, destroy it
place the caret at (`from` run, `from` offset)

#### Merge Into Head (head, tail)
move every surviving run of `tail` into `head`, in order
destroy every block from the one after `head` through `tail`
`head` apply layout

An emptied *tail* run is destroyed and an emptied *head* run is not, which looks arbitrary and is not. The caret has to land somewhere and a run carries the style the next typed character takes, so keeping the head run keeps the formatting at the caret the way every editor does; handing the caret to a neighbour would silently change it. The head run surviving is also what guarantees a block never ends up with zero runs, which is the case that would leave `Normalize`, `OrderedRuns` and `RunAt` all answering for nothing. The tail run has no such claim, and leaving it behind grows the saved note by a `<Run />` that reads back as real.

The merged block keeps the **head** block's styling type — deleting from a heading into the paragraph below leaves a heading. `ApplyLayout` re-runs on it afterwards, or the runs that moved across would keep the size the scheme resolved for the block they came from.

#### Split Block
delete the selection, if any
`tail` = new content block with the caret block's styling type
`carried` = clone of the caret's run, holding its text from the caret on
caret run text = its text before the caret
`tail` add `carried`, then every run after the caret's run in its block
if `carried` is empty and other runs moved in, destroy `carried`
insert `tail` after the caret's block, in both the child list and the model's block list
`tail` apply layout
place the caret at (`tail` first run, 0)

Enter's new block takes the old block's styling type rather than resetting to body text: splitting a heading mid-word has to give two headings, and "Enter at the *end* of a heading gives a paragraph" is a second rule keyed on caret position that can be added to `Split Block` later if it is wanted. Unlike every other caret-moving operation, a split does **not** end in `ScrollIntoView` — the new block has no arranged rect until the next layout pass, and a zero rect reads as "above the viewport", so scrolling would throw the note to the top. Pressing Enter on the last visible line therefore leaves the caret just below the viewport until something else scrolls.

Blocks live in two lists at once — `RichTextDocument.blocks`, which is what a save is written from, and the children of the `DocumentControl`, which is what layout and hit-testing walk. They are the same objects, so every structural edit updates both, which is why the control now holds the document. Rebuilding the model's list from the control tree at save time was the alternative and was rejected: it matches the "the model is the control tree" decision more honestly, but leaves the list silently stale all session for any other reader, and the vault browser and undo are both going to be readers. The duplication is the P0 model-as-controls decision showing through once more, and it goes away with the data/visualization split rather than here.

## Status
- P0 (model types) and P1 (XML persistence) complete; round-trip verified (in-code build + reload of code-built and hand-authored XML are byte/structurally equal).
- P3 complete: click→caret, character input, arrow / Home / End / PageUp / PageDown navigation, and Ctrl+S through `DocumentEditSession`. Save verified against the sample note — no run gains a `FontSize`. Navigation itself is compile-verified and pending GUI verification.
- P2 (`DocumentEditorControl`) implemented; built into `Periodic/Data/XML/Documents/UI.xml` via `<DocumentEditor Source="SampleNote.xml"/>`.
- P4 steps 1 and 2 complete: selection renders and is GUI-verified apart from drag auto-scroll, which the sample note is too short to exercise; deletion over a range, Backspace, Delete and Enter are bound and boot-verified but **not** GUI-verified. Step 3, Ctrl+B/I run split/merge, is next — `Bold` and `Italic` are still read by nothing.
- Undo and select-all do not exist, which deletion is the first feature to make matter: a mis-aimed delete is recoverable only by reloading the note.
- P5 complete: `Periodic` is a two-pane shell, a `VaultBrowser` listing a vault folder beside the editor, and `LoadPath` has a real caller at last. The vault is a settings path; the browser, being app rather than engine, lives in `Periodic` and is described in `DOCUMENTATION/ClaudeMemory/Decisions/vault-browser-and-shell.md`. Switching notes saves the one being left, since nothing tracks dirtiness and nothing can undo.
- Lists, quotes, dividers, wiki-links, inline code: not yet — added as the editor grows. Code blocks and tables are scheduled (B1/B2). L2 is dropped, L3 (paged mode) is unaffected — see [[Document Layout Engine#Status]]. Revised phase order: `DOCUMENTATION/ClaudeMemory/Context/periodic-editor-architecture.md`.
