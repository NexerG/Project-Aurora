# Periodic editor — architecture & status

Remaking `Periodic` (`AuroraPeriodic`) into an Obsidian-style note editor on the engine.
Full plan: `C:\Users\gmgyt\.claude\plans\time-to-do-some-mighty-gizmo.md`;
layout-engine/scale revision (L1/L2/L3, B1/B2): `C:\Users\gmgyt\.claude\plans\lets-say-the-idea-synthetic-wreath.md`.
**LANDED 2026-08-07 (`542e7d7`):** one layout path for all text, document is a plain control tree,
**L2's virtualization and `DocumentLayoutCache` are gone**. See [[text-layout-one-measurer]] — it is
the current description of this area. L1/L2/P3 below are kept as history and are marked where they
no longer describe the code.

## Confirmed decisions
- **Note format:** engine **XML** (not markdown, not JSON), via `[A_XSDType]`/`[A_XSDElementProperty]`.
- **Editing UX:** live-preview WYSIWYG; edits hit a **working copy**; **Ctrl+S** commits + writes XML.
- **Architecture:** plain-data **document model = source of truth**; control tree is a **view**.
- **Scope now:** offline desktop only. Online multi-editor / browser are later (model/view split chosen to allow a CRDT layer later).
- ~~**Layout cache is the single geometry system**~~ — **SUPERSEDED 2026-08-07.** `TextMeasurer` is
  still the only thing that decides line breaks, but there is no cache: each text control holds its
  own `BlockLayout` and answers geometry for itself (`OffsetAt`/`CaretAt`). The control tree *is*
  the hit-test again. See [[text-layout-one-measurer]].
- ~~**View is virtualized; model is always fully loaded.**~~ — **SUPERSEDED 2026-08-07.** No
  virtualization: every character is a `GlyphControl`, always. `TextRunControl` is deleted and
  `TextRun : TextInputControl` is the run renderer again.
  - **The replacement is engine-wide, not document-local (user, 2026-08-17):** the UI splits into
    **data and visualization** — the parent/child tree becomes pool data, a control stays one object
    per element presenting its row, all on the existing `UIControls` pool. Sequenced after Periodic
    v1 and the profiler; nothing here changes before then. See [[ui-data-control-split]].
  - **It does not fix the glyph ceiling.** One control per element means the count is unchanged.
    The ceiling and the split share a cause and are separate problems; the escape hatch below is
    still the only thing that touches the count.
- **Pages vs pageless = a layout-engine mode**, never a model concern. Paged mode paginates
  *lines* (not blocks) so paragraphs split across page breaks. Those lines now come from each
  block's own `BlockLayout` rather than from a cache; `DocumentControl` is where page panels go.
- **Layout engine lands before P3 editing** (user decision): caret/hit-test math is written once
  against measured lines, not against glyph rects and then redone. Held — the math survived the
  cache's deletion by moving onto `TextControl` intact.

## Scale rationale
**Stale as of 2026-08-07** — the cache and the virtualized view it argues for are gone. The
*numbers* still hold and are why the ceiling is worth watching; see [[text-layout-one-measurer]]
for the decision to accept them and the escape hatch if they bite.
- 100 pages ≈ 300k chars; model (strings + runs) < 1 MB — data is never the memory problem.
- Today each char = one `GlyphControl` = full `VulkanControl` entity + a `ControlData` row in the
  pooled SSBO. (**Descriptor cost is GONE as of 2026-07-31** — the sampler array used to be indexed by
  `gl_InstanceIndex`, so every glyph burned one of 50,000 slots writing the same atlas view; it is now
  a 256-entry table indexed by `ControlData.textureIndex` and every glyph in a font shares one slot.
  See [[glyphs-as-pool-data]], which also settles that glyphs **stay controls**, because per-letter
  colour/rotation/animation are required.) The pool row remains: 300k glyphs =
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
- **Held as of P5** — `VaultBrowserControl` and `PeriodicSettings` are Periodic's; the engine gained
  nothing for the browser except the `FileObject` recursion fix.

## Phases (see TODO/plan)
- **P0 — done.** Document types under `Controls/Text/Document/`: `RichTextDocument` (plain model
  holding `List<Block>`), and the tree is now all `VulkanControl`s — `Block : TextBlockControl`
  (a `PanelControl` derivative) → `ContentBlock` (the one concrete block, `[A_XSDType("Block")]`), and
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
- **L1 — done (2026-07-31). `TextMeasurer` survives and is now the only wrapper; the
  `DocumentLayoutCache` half is deleted (2026-08-07).** `MeasureBlock` now takes a `float lineHeight`
  and a `firstLineOffset` instead of a `DocumentLayout`; `CaretGeometry` moved here, `DocumentPosition`
  is gone. Everything below about the cache is history.
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
  - `DocumentLayout.blockSpacing` (new, default 8, in `XML/Settings/DocumentSettings.xml`) replaced the hardcoded
    `Spacing = 8f` on `DocumentEditorControl`'s StackPanel — cache and view stack blocks by the same
    number, same rationale as `FontSizeFor`. Divergence here compounds with scroll depth.
  - Structural edits (block added/removed) = full `Rebuild`, no splice. Fine at a few hundred blocks.
  - **The cache is NOT headless-testable** (unlike the measurer): it holds a `RichTextDocument`, whose
    blocks *are* controls, and reaches through them for run text + heading level. Consequence of the
    P0 "model is the control tree" decision, not a fixable oversight.
  - **Geometry is now verified in-app** (2026-07-31, at P3): `CharToPoint → HitTest → CharToPoint`
    over every caret slot returns the identical point — 608/608 on the sample note, 49,984/49,984 on
    the 400-block stress note. Supersedes the earlier "compile-verified only" note. The cheap way to
    re-run it is a temporary loop in `SyncVisible`; it needs no input and no eyeballing.
  - **Remaining before it means anything:** nothing calls it — L2 is what makes the view consume it.
- **L2 — REVERTED 2026-08-07.** Everything in this entry is history: `TextRunControl`,
  `DocumentCanvasControl`, `SegmentKey`, `SyncVisible`/`Materialize`/`Release` and `IDocumentPlaced`
  are deleted. Its measurements (57–59 strips flat, no leak, converges in 2 `Arrange` calls) were
  real and are kept only as evidence about the old design.
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
- **Deferred — load-time glyph explosion (user, 2026-07-31; answered 2026-08-17).** Loading a note
  builds one `GlyphControl` per character before the view is consulted: `DocumentXml.ParseElement`
  `Activator.CreateInstance`s each `<Run>` as `TextRun : TextInputControl : TextControl`, and setting
  the `Text` attribute hits the `text` setter → `SyncGlyphs()`.
  - **Still open, and the UI split does not close it** — that split keeps one control per element,
    so the count survives it. See [[ui-data-control-split]] "What this does and does not buy".
  - **Culling will not fix it either**, which is why it stopped being the intended answer. It stops
    off-screen controls being *drawn*; the entities and pool rows all still exist.
  - What is left is the escape hatch above (a run with no glyph children until it is visible) or a
    per-kind visualizer, which was considered and rejected on per-letter capability.
  - Root cause is the P0 model-as-controls decision, which contradicts this file's own Confirmed
    decisions line (*"plain-data document model = source of truth"*). P0's rationale — blocks are
    controls so the engine UI layout lays the document out for free — was **voided by L1**: the cache
    does layout now, and L2 confirmed the view never touches those model controls either.
  - Two exits were offered, neither taken: (a) make `Block`/`TextRun` POCOs like
    `DocumentLayout`/`HeadingStyle` already are — also restores cache headless-testability and deletes
    `DocumentXml.AttachChild`'s "most-specific accepting list" hack, which exists *only* because
    blocks/runs inherit `Entity.children`; (b) keep the inheritance, stop the `text` setter building
    glyphs.
- **P3 — DONE (2026-08-17).** `DocumentEditSession` + Ctrl+S + arrows/Home/End/PageUp/PageDown all
  landed, and the whole char/special-key path moved into the engine as `TextInputActions`. Two
  things the plan said are now decided the other way and are recorded in
  [[engine-side-text-input]]: the session is **not** a working copy (the model is the control tree,
  so a clone is a second control tree), and line start/end are the **visual** line's, resolved by
  scoring every line of every run rather than per run. `Periodic/Editor/Decorations.cs` no longer
  contains any input code. `ICharacterInput` **still does not exist**.
- **P3 — click→caret and char input both working (2026-08-07).** Rebuilt on control-local geometry:
  `TextControl.OffsetAt`/`CaretAt`, caret placed by `DocumentControl`, `cursorPosition` on
  `TextControl`. Typing works — the `Decorations.Write` blocker below is resolved. Remaining:
  `DocumentEditSession` (working copy), Ctrl+S, special keys. See [[text-layout-one-measurer]].
  The 2026-07-31 entry below is history.
- **P3 — click→caret done (2026-07-31); editing not started.**
  - `CaretControl` + `IDocumentPlaced` (the canvas positions anything implementing it from cache
    coordinates — strips, caret, and L3 page panels later). Caret is a child of the **canvas**, not
    the editor, so it scrolls with the document for free.
  - **Editability boundary (user rule, 2026-07-31):** turning a left-click into a caret lives in
    `DocumentEditorControl.ResolveOnClick` and **nowhere else**. Document names, toolbars and the
    file tree are separate controls with no such behaviour, so nothing is editable by accident.
    Renaming those is a right-click concern → `ContextMenuControl` (**exists** as a stub with an
    `Open()` that only logs; `UICollisionHandling.defaultContextMenu` field already declared).
  - **How the click gets there:** `UICollisionHandling.FindDeepestValid` returns the **deepest** hit,
    which inside the document is a `GlyphControl`. It bubbles (`BubbleAll` in its ctor) — so
    `TextRunControl` and `DocumentCanvasControl` now call `BubbleAll()` too, or the click dies at the
    strip. The glyph is *not* the hit-test: it only says "the document was clicked", and the caret
    position comes from `cache.HitTest`, which works for text that was never materialized.
  - **Coordinate spaces coincide** *unless the window scales*. `WriteArrangedTransform` writes
    `finalRect` straight into `transform.position/scale`, and `InputHandler.mousePos` is raw GLFW
    window pixels. Those are the same units in every mode except `WindowControl.WindowingMode
    .ScaleUp`, so raw mouse coordinates must go through **`WindowControl.ToDesignSpace`** before
    they are compared to a rect — `Engine.HandleUI` and `DocumentEditorControl.ResolveOnClick` both
    do. Screen→document = subtract `canvas.arrangedRect` origin (already scroll-shifted). Use
    `InputHandler.mousePos`, **not** `ResolveOnClick`'s `oldPos`: Engine sets
    `uiCollisionHandler.lastMousePos` *after* click resolution, so `oldPos` lags by a frame.
  - A rewrap keeps the cursor valid — block/run/charOffset are independent of where lines break —
    so the caret is re-resolved via `CharToPoint`, not reset.
  - **Verified: caret round-trip on real font metrics.** `CharToPoint → HitTest → CharToPoint` over
    every caret slot: **608/608 exact** on the sample note, **49,984/49,984 exact** on the 400-block
    stress note. This is the first real check of L1's geometry, which until now was compile-verified
    only — it exercises midpoint snapping, the wrap-boundary rule and run boundaries.
  - **Remaining:** `DocumentEditSession` (working copy), char input drain + special keys, Ctrl+S.
    See the char-routing gotcha below — the existing drain does not reach the document.
- **P4 — steps 1 and 2 done (2026-08-17); step 3 (Ctrl+B/I) open.**
  - Step 1, selection: anchor + the caret as focus, one `SelectionControl` box per visual line,
    drag / shift+click / shift+arrows. **GUI-verified by the user** except auto-scroll, which has
    nothing to scroll on the sample note. See [[document-selection]].
  - Step 2, editing over a range: `Text.Backspace`, `Text.Delete`, `Text.NewBlock` bound in
    `InputMap.xml`, and typing over a selection replaces it. One primitive, `DeleteRange(from, to)`
    — Backspace/Delete with no selection make a one-slot one by extending the caret through the
    existing `MoveLeft`/`MoveRight`, so run/block boundary rules are not written twice. Cross-block
    delete collapses into the head block; Enter splits a block and the new one keeps the styling
    type. `DocumentControl` now holds the `RichTextDocument`, because the block list is duplicated
    between it and the control tree. **Not GUI-verified.** See [[document-structural-editing]].
  - Step 3, Ctrl+B/I run split/merge, and heading/list block ops: not started.
- **B1** — `CodeBlock` (Language attr, monospace, no wrap, view-time syntax coloring — never
  persisted): model + layout + view.
- **B2** — `TableBlock` → `TableRow` → `TableCell` (cell holds `List<Block>`, nested blocks;
  MVP fixed/star columns, no merges): model + layout + view.
- **L3** — paged mode: paginator assigns cached lines to fixed-height pages (blocks split across
  breaks); view draws page-background panels + gaps.
- **P5 — done (2026-08-17).** `VaultBrowserControl` in Periodic + a 2-pane `UI.xml`. The vault is a
  `<Vault Path="Notes"/>` setting on a `Periodic` category; notes live in `Periodic/Data/Notes`, not
  in the engine-config `Data/XML/Documents`. Rows are a flat indented list, not a collapsible tree.
  Controls have no names, so the browser finds the editor with a `Find<T>` walk from
  `EntityRegistry.uiTree`. Opening happens from `Periodic.Main`, **not** `OnStart` — the engine
  cannot create entities inside its `OnStart`/`OnTick` foreach loops. See [[vault-browser-and-shell]].
- **Later/optional** — per-run glyph batching (one control per visible run + instanced glyph
  buffer) only if profiling shows visible-glyph control overhead matters; `TextRun` style
  extensions land with the features that need them (`FontSize`, `Underline`, highlight color);
  list/quote/divider blocks.

## Document settings & the style cascade (2026-07-31)
- `DocumentLayout` is a **class** on `RichTextDocument` (`layout`), persisted as a nested
  `<DocumentLayout>` element. A class, not a struct, because it owns a `List<TextStyle>` —
  value-copying it would hand a working copy the original's styles to mutate. `Clone()` deep-copies.
- Holds `lineHeight`, `List<TextStyle>`; the declared home for content width on resize and the
  paged/pageless mode when those land.
- **Styling types are data, headings are not a class.** `TextStyleType` = `Inherit, Text,
  Heading1-6, Comment, Code, Quote`; `TextStyle` = (`type`, `fontSize`). A block names a
  `StylingType` and the scheme says what it looks like — see [[text-styling-types]].
- **Three tiers, via the settings registry.** `Data/XML/Settings/DocumentSettings.xml` is the
  editor-wide scheme, cascading engine → app → the host's write root **per attribute**; a note then
  overrides per-document by embedding its own `<DocumentLayout>`. See [[settings-registry]].
- **Empty list = inherit.** Declaring even one style replaces the whole set (no entry-by-entry merge
  against defaults the author cannot see). A heading past the last one listed takes the last one.
- `FontSizeFor(TextStyleType)` is the single resolver, so the measurer and the drawn controls cannot
  disagree on heading size. `ContentBlock.ApplyLayout` resolves per run — a run's own `stylingType`
  wins over its block's, `Inherit` means it has none — and writes the result into `TextRun.fontSize`.
- **Live-path risk, not GUI-verified:** the scheme now arrives at the `Settings.LoadAll` bootstrap
  step, not lazily on first note open. Headings at scheme sizes = cascade resolved; headings at 18
  = nothing resolved and `fallbackFontSize` won. The engine mount is `Engine.isDebug`-gated (same as
  `Bootstrap.xml`), so a release build needs the file in the app's own Data folder.

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
- **Glyphs are repointed, not replaced, on a text write** (2026-07-31, engine-wide — not document-only).
  `GlyphControl.SetCharacter(char, FontAsset, int px)` is the extracted ctor body; the ctor is now
  `BubbleAll()` + a call to it. `TextControl.SyncGlyphs` calls it on a positional mismatch instead of
  destroying and constructing a control.
  - **Why.** `SyncGlyphs` diffs by position (`children[i].character == target[i]`), so inserting one
    character mismatches *every* position after it. Each replacement was a `pool.Allocate` (possibly a
    pool grow → full descriptor rebuild), three registry dictionary lookups (`ControlStyle.Default()`,
    the `"default"` texture, the `"ControlSampler"` sampler), an `EntityRegistry.AddToGroup`, a matrix
    bake, an O(n) `parent.children.Remove`, a deferred free compacted at `DataManager.FrameEdge`, and
    one `MarkOrderDirty` → full DFS permute of the UI pool. Typing one character at the head of a
    500-character paragraph cost all of that ×500, **per keystroke**.
  - **After.** Everything a `GlyphControl` holds derives from `(character, font, size)`, so a rewrite is
    field writes plus a UV update. Controls are now only ever **appended to or trimmed from the tail** —
    the interior is adjusted in place, keeping its pool row, its tree position and its paint order.
  - **Trap that bit during the change:** `ascent`/`descent` are only assigned inside `if (range != 0)`.
    On a fresh control the miss is harmless (fields default to 0); on a *reused* one it silently keeps
    the previous character's baseline. `SetCharacter` clears both before the test. Any field added to
    `GlyphControl` later must be unconditionally assigned there for the same reason.
  - `SetCharacter` ends in `InvalidateLayout()` — the `preferredWidth`/`preferredHeight` setters
    invalidate on their own, but only when the number changes, and two characters routinely share a
    cell size while differing in advance, bearing and UVs.
  - **Not covered:** the diff is still positional, so an insert still *visits* every later character
    (cheaply). Prefix/suffix trimming would make it O(edit) — considered and deliberately not taken.
    `TextRunControl.SetSegment` still destroys-then-rebuilds, but `Materialize` guards it with
    `if (!isNew) return;`, so it only ever runs on a control with no children — its destroy loop is
    dead in practice, and it becomes live only when strip pooling lands.
  - **Also not covered — `ShortTextControl` is separately broken.** It *hides* `TextControl.text` and
    `.fontSize` (CS0108 ×2), so `SyncGlyphs` never runs for it at all, and its own setter appends glyphs
    without discarding the old ones — setting its text twice leaves both strings drawing. Pre-existing,
    left alone deliberately.
- **Char input — RESOLVED 2026-08-07, moved into the engine 2026-08-17.** The cast succeeds because a
  glyph's parent is a `TextRun : TextInputControl : TextControl` again. `isEditing` is set by
  `DocumentEditorControl.ResolveOnClick` or by `DocumentControl.SetCaret`, not by the run, which
  keeps the editability boundary. The drain is now `TextInputActions.Write` in the engine, not
  `Periodic.Decorations.Write`. Still true: **`ICharacterInput` does not exist**. Original note:
- **Char input does not reach the document, and two CLAUDE.md claims about it are wrong**
  (found 2026-07-31). The drain is `Periodic/Editor/Decorations.Write` — an
  `[A_XSDActionDependency("Write", category:"Input")]` bound to the `AnySymbol` keybind. It casts
  `UICollisionHandling.activeControl as TextControl`, checks `isEditing`, then `WriteChar`s the
  queue. Two problems for P3:
  - `activeControl` is set to `hovering` (the glyph), then `GlyphControl.OnContextAdded` **reassigns
    it to `parent`** — which used to be a `TextInputControl` (a `TextControl`) and is now a
    `TextRunControl` (not one). So the cast fails and typing into a note is a silent no-op.
  - The drain lives in the **app**, but the Engine/Periodic boundary puts char routing in the engine.
  - **`ICharacterInput` does not exist** — CLAUDE.md describes it as the interface to implement for
    raw char input. There is no such type anywhere in the solution. Whatever P3 does here is
    designing it, not using it. (CLAUDE.md's `Keys.AnySymbol` claim *is* accurate.)
- **`fontName` — RESOLVED 2026-08-07.** It lives on `TextControl` and resolves `_fontAsset`, so
  every text control measures and draws with the same font. Original note:
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
