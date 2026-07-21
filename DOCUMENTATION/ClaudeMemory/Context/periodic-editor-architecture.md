# Periodic editor — architecture & status

Remaking `Periodic` (`AuroraPeriodic`) into an Obsidian-style note editor on the engine.
Full plan: `C:\Users\gmgyt\.claude\plans\time-to-do-some-mighty-gizmo.md`;
layout-engine/scale revision (L1/L2/L3, B1/B2): `C:\Users\gmgyt\.claude\plans\lets-say-the-idea-synthetic-wreath.md`.

## Confirmed decisions
- **Note format:** engine **XML** (not markdown, not JSON), via `[A_XSDType]`/`[A_XSDElementProperty]`.
- **Editing UX:** live-preview WYSIWYG; edits hit a **working copy**; **Ctrl+S** commits + writes XML.
- **Architecture:** plain-data **document model = source of truth**; control tree is a **view**.
- **Scope now:** offline desktop only. Online multi-editor / browser are later (model/view split chosen to allow a CRDT layer later).
- **Layout cache is the single geometry system** (user decision): a `DocumentLayoutEngine`
  measures blocks from font metrics (no controls) into a per-block line cache; **all** geometry
  queries — mouse clicks, caret placement, arrows/PageDown/Ctrl-End, selection drag, find-next,
  `ScrollIntoView`, pagination — resolve on the cache. Control tree only answers the app-shell
  question "did the click land on the editor at all". Glyph/run/block controls are **not**
  hit-targets; `TextInputControl.HitTestCursor` / `ResolveOnClick` caret placement are superseded.
  See `DOCUMENTATION/Engine/Core/Entities/Controls/Text/Document Layout Engine.md`.
- **View is virtualized; model is always fully loaded.** Controls materialize only for the
  visible block range ± 1 viewport, recycled via a pool. Read-only runs get a lightweight
  `TextRunControl` (planned) — `TextInputControl` stops being the run renderer (removes the
  model/view style-field duplication).
- **Pages vs pageless = a layout-engine mode**, never a model concern. Paged mode paginates
  cached *lines* (not blocks) so paragraphs split across page breaks.
- **Layout engine lands before P3 editing** (user decision): caret/hit-test math is written once
  against the cache, not against controls and then redone.

## Scale rationale
- 100 pages ≈ 300k chars; model (strings + runs) < 1 MB — data is never the memory problem.
- Today each char = one `GlyphControl` = full `VulkanControl` entity + `ControlData` GPU struct
  + own storage buffer + slot in `UIModule`'s 50,000-cap descriptor array. 300k glyphs = 6× over
  the cap, hundreds of MB, O(300k) Measure/Arrange reflows → the view must be virtualized.
- Layout cache ≈ 100 KB per 100 pages (per block: height + lines
  `(runIndex, charStart, charCount, width, baseline)`; prefix-summed block tops → any Y + scroll
  extent). Visible glyphs ≈ 3–6k, constant for any document length.
- Per-block invalidation: a keystroke re-measures one block + shifts following offsets.
- Rejected: chunked disk lazy-loading (model tiny; breaks search/links/save);
  glyph batching instead of virtualization (still measures/uploads 300k unseen glyphs — batching
  stays a later optional optimization for visible runs); control-tree hit-testing inside the
  document (covers only the materialized viewport; two geometry systems).

## Engine vs Periodic boundary
- **Engine** (`AuroraEngine/Core/UISystem/Controls/Text/...`): document model, edit session,
  cursor/selection, caret control, editor view control, char/special-key routing.
- **Periodic** (app): vault mount, file-tree browse, app-shell `UI.xml`, open/save actions, sample notes.

## Phases (see TODO/plan)
- **P0 — done.** Document types under `Controls/Text/Document/`: `RichTextDocument` (plain model
  holding `List<Block>`), and the tree is now all `VulkanControl`s — `Block : TextBlockControl`
  (a `PanelControl` derivative) → `ContentBlock` → `ParagraphBlock`/`HeadingBlock`, and
  `TextRun : TextInputControl` (no `Inline` base; run reuses the control's style/text). All four
  `[A_XSDType]` types are category **`"UI"`** (moved off the orphan `"TextEditor"` category), so they
  generate into `UITypeSchema.xsd` and a note is authored like `UI.xml`. Caveat: no schema actually
  compiles yet — see [[xsd-generator-cross-category]] (systemic cross-category ref bug, deferred).
- **P1 — done.** `DocumentXml` load/save + `RichTextDocument : IXMLParser`. Round-trip verified.
- **P2 — done (pending GUI verify), INTERIM.** `DocumentEditorControl` (`[A_XSDType("DocumentEditor")]`,
  Scrollable > StackPanel of block controls, reusing `TextBlockControl` + `TextInputControl` as run
  renderer). Wired into `Periodic` `UI.xml` as `<DocumentEditor Source="SampleNote.xml"/>`; sample
  note at `Periodic/Data/XML/Documents/SampleNote.xml`. Note: `ScrollableControl` clashes with
  `System.Windows.Forms.ScrollableControl` (WinForms on) — alias the engine type when subclassing.
  The StackPanel-of-everything presentation and `TextInputControl`-as-run-renderer are replaced in L2.
- **L1** — layout engine, pageless: `TextMeasurer` (pen advance = `glyph.advanceWidth * px`,
  glyph x-offset = `leftSideOffset * px`, quad = `glyphWidth * px`; all three em-normalized in
  `AuroraFont`; **word-boundary** wrap) + per-block line cache + prefix-summed block tops.
  The correct advance formula already exists in `ShortTextControl` / `TextEntity`; the current flow
  controls (`TextInputControl` / `TextBlockControl`) advance by ink **bbox width** and wrap **per
  character** — a defect to reconcile ONTO the measurer in L2, **not a reference to match**. Verify:
  measured line breaks match the `advanceWidth + leftSideOffset` placement on `SampleNote.xml`
  (eyeball for now — see testing note).
- **L2** — virtualized view: `DocumentEditorControl` presents only the visible block range ± 1
  viewport from the cache; `TextRunControl` (lightweight read-only run); glyph GPU cleanup +
  `GlyphControl` pooling (hook `UIModule` deferred-deletion). Verify: generated ~100-page note
  scrolls smoothly; `"Controls"` count stays viewport-sized; memory flat.
- **P3** — editing, rebased onto the cache: `DocumentEditSession` (working copy) + `DocumentCursor`
  + `CaretControl` (position via cache char→point); wire char input drain + special keys; Ctrl+S
  writes XML. Verify: type/delete across runs, round-trip file.
- **P4** — selection (cache-resolved drag incl. auto-scroll) + Ctrl+B/I run split/merge (live)
  + heading/list block ops.
- **B1** — `CodeBlock` (Language attr, monospace, no wrap, view-time syntax coloring — never
  persisted): model + layout + view.
- **B2** — `TableBlock` → `TableRow` → `TableCell` (cell holds `List<Block>`, nested blocks;
  MVP fixed/star columns, no merges): model + layout + view.
- **L3** — paged mode: paginator assigns cached lines to fixed-height pages (blocks split across
  breaks); view draws page-background panels + gaps.
- **P5** — vault browser (`FileObject` → tree view) + 2-pane `UI.xml` shell.
- **Later/optional** — per-run glyph batching (one control per visible run + instanced glyph
  buffer) only if profiling shows visible-glyph control overhead matters; `TextRun` style
  extensions land with the features that need them (`FontSize`, `Underline`, highlight color);
  list/quote/divider blocks.

## Gotchas
- The binary `Serializer` is **not** the note format — notes are XML via `DocumentXml`. See
  `../Patterns/document-xml-persistence.md`.
- `AuroraTesting` is **empty / not in the solution** — no test home yet. **Decision (user):** manual
  GUI verification for now; a headless test project is on the TODO (`Work in Progress List.md`,
  ESSENTIALS). P1 was verified with a throwaway harness (deleted). Don't add a test-framework NuGet
  until that TODO is taken up with the user.
- Lists/quotes/wikilinks deferred (Simplicity-First) — declared as the editor grows; code/tables
  are now scheduled (B1/B2).
- The `TODO: deferred Vulkan resource cleanup` markers in `TextControl`
  (`SyncGlyphs`/`RemoveGlyph`) are a **hard L2 prerequisite** — virtualization churns glyph
  controls on every scroll and today removed glyphs leak GPU buffers.
- **L1 testing (user decision):** eyeball for now; when formalised, **no NuGet packages** — a
  ~40-line reflection runner in `AuroraTesting` (console exe, local `[TestCase]` attribute, non-zero
  exit on fail — same attribute-reflection idiom as Bootstrapper/XSDGenerator) + synthetic-metrics
  unit tests (fabricate `Glyph[]` with known advances — no font file, no GPU) plus optional
  real-font tests via `.agd` deserialization (metrics without the atlas texture) and golden-file
  dumps of the line table. Enabler: keep `TextMeasurer`'s dependency narrow — a glyph-metrics
  lookup (char→`Glyph`), never `FontAsset`/`GlyphControl`/GPU — so fakes are trivial.
