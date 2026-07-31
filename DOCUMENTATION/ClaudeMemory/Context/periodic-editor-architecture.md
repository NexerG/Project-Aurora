# Periodic editor — architecture & status

Remaking `Periodic` (`AuroraPeriodic`) into an Obsidian-style note editor on the engine.
Full plan: `C:\Users\gmgyt\.claude\plans\time-to-do-some-mighty-gizmo.md`;
layout-engine/scale revision (L1/L2/L3, B1/B2): `C:\Users\gmgyt\.claude\plans\lets-say-the-idea-synthetic-wreath.md`.

## Confirmed decisions
- **Note format:** engine **XML** (not markdown, not JSON), via `[A_XSDType]`/`[A_XSDElementProperty]`.
- **Editing UX:** live-preview WYSIWYG; edits hit a **working copy**; **Ctrl+S** commits + writes XML.
- **Architecture:** plain-data **document model = source of truth**; control tree is a **view**.
- **Scope now:** offline desktop only. Online multi-editor / browser are later (model/view split chosen to allow a CRDT layer later).
- **Layout cache is the single geometry system** (user decision): `TextMeasurer` + `DocumentLayoutCache`
  measure blocks from font metrics into a per-block line cache; **all** geometry
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
- Today each char = one `GlyphControl` = full `VulkanControl` entity + a `ControlData` row in the
  pooled SSBO + slot in `UIModule`'s 50,000-cap descriptor array. 300k glyphs = 6× over the cap,
  hundreds of MB, O(300k) Measure/Arrange reflows. (Stale as of `7b5e032`: glyphs no longer own a
  *separate* storage buffer each — `ControlData` lives in the shared pooled SSBO, so the pool row is
  the GPU cost.)
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
- **L1 — done (2026-07-31), nothing consumes it yet.**
  - `TextMeasurer`: word-boundary wrap, uniform line boxes, per-run segments. Advance =
    `glyph.advanceWidth * px`, em-normalized in `AuroraFont`. **Nothing calls it yet** — L1 is
    deliberately additive so the cache can be built beside what already renders.
  - A line is **not** one run. `TextLine` owns a `List<LineSegment>` (`runIndex`, `charStart`,
    `charCount`, `width`) because a bold word mid-paragraph puts three runs on one visual line —
    the design doc's single-`runIndex` line tuple could not express that.
  - **Line height is CSS/Obsidian, not ink.** Box = `fontSize * DocumentLayout.lineHeight` (1.5),
    font ink box centred, leftover split as half-leading, baseline at `halfLeading + ascent`.
    Per-glyph ink made a line grow when someone typed a "g". Mixed styles take the tallest box.
  - `IGlyphMetrics` = `Get(fontName, char)` + `GetLineMetrics(fontName)`. Production derives the
    font box as max ink across the glyph set; `hhea` ascender/descender would be better but
    `GenerateGlyphAtlas` reads and discards them, so they are not in the `.agd`.
  - `MeasureBlock(ContentBlock, ...)` is the only control-touching overload; the plain-data one
    beneath runs headless. Blocks/runs are `VulkanControl`s whose ctor hits the asset registry,
    entity registry and data pool — anything taking one cannot be tested without booting.
  - **Stale claim removed:** `TextInputControl` no longer advances by ink bbox width — `b9c6d60`
    fixed that. It does still wrap **per character**; that is what the measurer replaces.
  - Verified with fabricated glyphs (no font file, no registry, no GPU): wrap points, mid-word
    split for an over-wide word, trailing space not wrapping, multi-run segmentation, equal
    heights for `"AAA"` vs `"ggg"`, empty block matching a full line, multiplier arithmetic.
  - `DocumentLayoutCache` holds `List<BlockLayout>` + `blockTops` (prefix sum, **N+1 entries** — the
    last is the scroll extent). API: `Rebuild(doc, width)`, `InvalidateBlock(i)` (re-measure one +
    slide the tail by the height delta), `SetContentWidth` (rewrap = full rebuild), `Extent`,
    `HitTest(x,y) -> DocumentPosition`, `CharToPoint(pos) -> CaretGeometry`.
  - `DocumentPosition` (block/run/charOffset) is the caret address P3's cursor will be a pair of.
  - **Per-char advances are not stored** — one float per char is MBs at 100 pages vs a ~100 KB budget.
    Hit-test/caret re-derive the one line they need via `TextMeasurer.MeasureAdvance` (made `public`
    for exactly this: two copies of the pen formula would drift).
  - **Two caret-slot rules**, both non-obvious: a click snaps to the *nearest* slot (past a char's
    midpoint = the slot after it), else you could never click the end of a word; and the slot at the
    end of a **wrapped** line is claimed by the line *below*, so the caret sits before the wrapped
    word instead of off the right edge above. No affinity tracking — not needed until selection
    rendering makes the two visibly distinct.
  - `DocumentLayout.blockSpacing` (new, default 8, in `DocumentStyles.xml`) replaced the hardcoded
    `Spacing = 8f` on `DocumentEditorControl`'s StackPanel — cache and view stack blocks by the same
    number, same rationale as `FontSizeFor`. Divergence here compounds with scroll depth.
  - Structural edits (block added/removed) = full `Rebuild`, no splice. Fine at a few hundred blocks.
  - **The cache is NOT headless-testable** (unlike the measurer): it holds a `RichTextDocument`, whose
    blocks *are* controls, and reaches through them for run text + heading level. Consequence of the
    P0 "model is the control tree" decision, not a fixable oversight. Compile-verified only;
    behaviour lands on the in-app profiling platform.
  - **Remaining before it means anything:** nothing calls it — L2 is what makes the view consume it.
- **L2 — done, view side (2026-07-31). NOT GUI-verified.**
  `TextRunControl` + `DocumentCanvasControl` + a cache-driven materialization diff in
  `DocumentEditorControl`. `TextBlockControl`/`TextInputControl` are no longer used to render a note.
  - **The view materializes per `LineSegment`, not per block** (user decision). A segment cannot wrap
    by construction, so `TextRunControl` has **no wrapping code at all** — x, y, width and baseline
    all arrive from the cache. `TextInputControl`'s per-character wrapping Measure/Arrange and the
    `firstLineOffset`/`lastLineEndX` handshake are simply not on this path.
  - Sharing wrap *code* between cache and view was considered and **rejected**: the view wraps per-run
    against `finalRect.width`, the cache per-**block** against `contentWidth`, so one shared function
    with two different arguments still desynchronises the caret from the glyph it points at. Single
    authority, not shared implementation.
  - `DocumentCanvasControl` height = `cache.Extent`, **not** the sum of its children — measuring
    children would only ever see the materialized viewport and size the scrollbar to it.
  - Split of responsibility: **`Measure`** owns content width → `cache.SetContentWidth` (now returns
    `bool`, because a rewrap changes what line/segment indices *mean* and every keyed control must be
    dropped) and sets the canvas extent; **`Arrange`** owns materialization, since scroll only fires
    `InvalidateArrange`.
  - Buffer = **± one viewport**. This is load-bearing, not padding: `Destroy()` only enqueues, so a
    released strip still draws until `ProcessDestroys` runs next tick — the buffer guarantees that
    frame is spent a full viewport outside the clip rect.
  - **Measured, 400-block / 22,392 px synthetic note, full scroll sweep (32 steps):** strips 57–59 and
    view glyphs ~4,700–4,950 flat end to end; `"Controls"` flat at ~56.5k across the sweep (**no leak**
    through the create/destroy churn); GC sawtooth 47–63 MB, no upward trend; layout settles after
    **2** `Arrange` calls (materializing inside `Arrange` calls `InvalidateLayout`, and it converges —
    it does not oscillate).
  - **What this did and did not buy.** It removed the *second* full glyph set: `BuildBlock` used to
    build a whole parallel copy beside the model's, so a note cost 2×. Now it is model + viewport. It
    did **not** make the total viewport-sized — the model still builds one `GlyphControl` per
    character at load (user's explicit decision to keep, 2026-07-31). On the 400-block note the total
    was ~56.7k controls, i.e. **already past `UIModule`'s 50,000-cap descriptor array**, from the
    model alone. Nothing crashed, so the cap does not appear hard, but that is the ceiling being
    leaned on. See the load-time note under **Deferred**.
  - **Visible change:** wrapping moved from **per-character** to **word-boundary**.
  - Not done: `GlyphControl` pooling. Strips are destroyed and rebuilt at the buffer edge rather than
    recycled. The sweep shows no leak and flat memory, so this stays a profiling question.
- **Deferred — load-time glyph explosion (user, 2026-07-31).** Loading a note builds one
  `GlyphControl` per character before the view is consulted: `DocumentXml.ParseElement`
  `Activator.CreateInstance`s each `<Run>` as `TextRun : TextInputControl : TextControl`, and setting
  the `Text` attribute hits the `text` setter → `SyncGlyphs()`. Accepted as-is for now; the user will
  give the word and intends **control frustum culling** (engine-wide off-screen cull).
  - **Culling alone will not fix this.** It stops off-screen controls being *drawn*; the entities,
    pool rows and descriptor slots all still exist. Raise this before implementing culling as the
    answer.
  - Root cause is the P0 model-as-controls decision, which contradicts this file's own Confirmed
    decisions line (*"plain-data document model = source of truth"*). P0's rationale — blocks are
    controls so the engine UI layout lays the document out for free — was **voided by L1**: the cache
    does layout now, and L2 confirmed the view never touches those model controls either.
  - Two exits were offered, neither taken: (a) make `Block`/`TextRun` POCOs like
    `DocumentLayout`/`HeadingStyle` already are — also restores cache headless-testability and deletes
    `DocumentXml.AttachChild`'s "most-specific accepting list" hack, which exists *only* because
    blocks/runs inherit `Entity.children`; (b) keep the inheritance, stop the `text` setter building
    glyphs.
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

## Document settings & the style cascade (2026-07-31)
- `DocumentLayout` is a **class** on `RichTextDocument` (`layout`), persisted as a nested
  `<DocumentLayout>` element. A class, not a struct, because it owns a `List<HeadingStyle>` —
  value-copying it would hand a working copy the original's styles to mutate. `Clone()` deep-copies.
- Holds `lineHeight`, `paragraphFontSize`, `List<HeadingStyle>`; the declared home for content width
  on resize and the paged/pageless mode when those land.
- **Heading levels are data.** `HeadingStyle` = (`level`, `fontSize`); however many entries exist is
  how many levels exist. The old hardcoded 34/28/23/20/18/16 switch on `DocumentEditorControl` is gone.
- **Two tiers, via the VFS.** `Data/XML/Documents/DocumentStyles.xml` is the editor-wide scheme; an
  app's copy resolves ahead of the engine's, so an app restyles every note without touching engine
  data. A note overrides per-document by embedding its own `<DocumentLayout>`.
- **Empty list = inherit.** Declaring even one style replaces the whole set (no level-by-level merge
  against defaults the author cannot see). Out-of-range levels clamp to the nearest defined one.
- **Scalars do not cascade yet** — `lineHeight`/`paragraphFontSize` fall back to code defaults that
  match the shipped file; inheriting them needs "was this attribute present" tracking the reflection
  parser lacks.
- `DocumentEditorControl` and the measurer both call `DocumentLayout.FontSizeFor(block)`, so cached
  geometry and drawn controls cannot disagree on heading size. `TextRun.fontSize` is unused until
  per-run sizing lands.
- **Live-path risk, not GUI-verified:** opening a note now lazily loads `DocumentStyles.xml` through
  the VFS. Headings at old sizes = cascade resolved; headings at body size = the file did not resolve.
  The engine mount is `Engine.isDebug`-gated (same as `Bootstrap.xml`), so a release build needs the
  file in the app's own Data folder.

## Gotchas
- The binary `Serializer` is **not** the note format — notes are XML via `DocumentXml`. See
  `../Patterns/document-xml-persistence.md`.
- `AuroraTesting` is **empty / not in the solution** — no test home yet. **Decision (user):** manual
  GUI verification for now. P1 was verified with a throwaway harness (deleted). Don't add a
  test-framework NuGet, and don't propose a test project before editor v1 — see the L1 testing
  bullet below.
- Lists/quotes/wikilinks deferred (Simplicity-First) — declared as the editor grows; code/tables
  are now scheduled (B1/B2).
- ~~`TODO: deferred Vulkan resource cleanup` in `TextControl` is a hard L2 prerequisite.~~
  **STALE — resolved, verified 2026-07-31.** The markers no longer exist. `TextControl.DiscardGlyph`
  clears `parent` then `Destroy()`s (clearing first is deliberate: `Destroy()` would otherwise remove
  the glyph from `children` and shift the list under the loop editing it). `Entity.Destroy` enqueues
  the subtree; `EntityRegistry.ProcessDestroys` (once per tick, before `OnTick`) `Unregister`s from
  every group incl. `"Controls"`, fires `OnDestroy`, and `Pool.Free`s the row, compacted at the next
  `DataManager.FrameEdge`. **And there is no per-control Vulkan buffer left to leak at all** — `7b5e032`
  folded `ControlData` into the pooled SSBO, so the pool row *is* the GPU memory. Landed across
  `af87d99`/`6edf8bb`/`7b5e032`, not as document work.
- **Char input no longer reaches the document.** The drain is `Periodic/Editor/Decorations.Write` —
  an `[A_XSDActionDependency("Write", category:"Input")]` bound to the `AnySymbol` keybind — and it
  casts `UICollisionHandling.activeControl as TextControl`. `activeControl` is set to `hovering` (the
  glyph), then `GlyphControl.OnContextAdded` **reassigns it to `parent`**, which used to be a
  `TextInputControl` (a `TextControl`) and after L2 is a `TextRunControl` (not one), so the cast
  fails. Nothing regressed that worked — the view is read-only — but P3 has to solve routing, and the
  drain also sits in the **app** while the Engine/Periodic boundary puts char routing in the engine.
  Note **`ICharacterInput` does not exist** despite CLAUDE.md describing it; P3 is designing that
  interface, not using it. (CLAUDE.md's `Keys.AnySymbol` claim *is* accurate.)
- **`fontName` is dead in the view but live in the cache** (found 2026-07-31, partly fixed).
  `TextRunControl.ResolveFont` now mirrors `FontAssetGlyphMetrics.Resolve`, so the **document** path
  is correct. The gap below still applies to `TextControl`/`TextInputControl` everywhere else.
  `TextControl._fontAsset` is pinned to `d["default"]` in the ctor and **nothing reads**
  `TextInputControl.fontName` — so every run draws in the default font. `TextMeasurer` *does* measure
  per-`fontName` through `IGlyphMetrics`. Harmless while everything is one font; the moment a second
  font is used, measured geometry and drawn glyphs disagree and the caret drifts. Whoever adds font
  selection must resolve the asset from `fontName` in the view at the same time.
- **Testing is sequenced after editor v1, and the harness is the UI** (user decision, 2026-07-31).
  Once the text editor's first version exists, a test/profiling platform gets built **on the engine's
  own UI** — GC/allocation, execution time, general functional checks — dogfooding the UI system while
  doubling as the profiler. This **supersedes** the earlier plan for a ~40-line reflection console
  runner in `AuroraTesting`. Until then, L1/L2 verification is eyeball + throwaway harnesses.
  Still **no NuGet packages** for testing.
  - Consequence to accept knowingly: L2's success criteria (*"scrolls smoothly, `Controls` count stays
    viewport-sized, memory flat"*) are measurement questions that land **before** the tool that
    measures them — so L2 is judged by eye, then re-audited on the platform once it exists.
  - Enabler that still holds: keep `TextMeasurer`/the cache's dependency narrow — a glyph-metrics
    lookup (char→`Glyph`), never `FontAsset`/`GlyphControl`/GPU — so the layout layer stays drivable
    from fabricated metrics whatever the eventual harness is.
