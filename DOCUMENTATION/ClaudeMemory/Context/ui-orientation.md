# UI orientation — the new stack, one entry per component

**Scope:** `UI` = `ArctisAurora.Core.UI`. The old stack's controls were deleted 2026-09-15 (landing 6d);
an entry still names its old twin, which survives only in git history and the "(old)" notes.
`Core.UISystem` is gone too (2026-09-15): gradients, `TextMeasurer` and actions live here; fonts and icons in `Core.Filing`.

**Use:** find the component → its entry says what it does, which region or members carry it, which note
section settles it → `grep -n` the member, `sed` that region. Open a whole file only when the entry and the
region map both fail. Note links name one `§`: `grep -n '^## '` the note and read that section only.
"(old)" marks a note written for the old twin.

**Upkeep:** a component added, removed or with changed entry points updates its entry in the same change
(`aurora-docs`). Names only — no paths into code, no line numbers.

## A frame, main thread

Steps of `Frame.frame.xml`, in order — [[frame-scheduler]] § Step 2.

1. `Main.Input` (`Engine.Input`): `InputHandler.ActivateKeybinds` — keybind actions run here, F10 `UI.DumpTree`
   among them; `Engine.HandleUI` → `UIEngine.Poll(window)`, per window — hover, press, release, drag, scroll →
   `Dispatch`; `DragGhost.Follow`, `ContextMenus.Tick`.
2. `Main.Logic` — `Animations.DrainDone` (last frame's finished tracks: release, `onDone`), then `Engine.Interpolate`
   — entity lifecycle and `OnTick`. Then `Animation.Step` on a worker: writes pool-stored animated properties
   (`Width`, `Margin`, …) straight into `ArrangeData`, appending `LayoutDirty` rows.
3. `Main.Layout` → `UIEngine.ResolveLayout` — `DrainLayoutDirty` invalidates the animated controls, then
   measure + arrange each dirty root.
4. Pool edges, then `Main.DrawLists` → `UIEngine.BuildDrawLists` — rewind `UIQuads`, DFS each window's root (a drag
   ghost's `rangeRoot` instead), `Control.Emit(z)` the visible ones into the pool, publish the window's range.
5. Render thread: `Render.Modules.UIEngineModule` mirrors its `UIQuads` range and draws —
   `*/Shaders/UIEngine/UIEngine.vert|frag`, four copies (`shader-pipeline` skill).

Why: [[ui-draw-list]] § What changed, [[ui-quads-pool]]; [[ui-engine-stack]] § Vocabulary.

## Core

### Control — abstract `<Control>` · `ECS.EngineEntity.Entity` · partial with ControlXml
One tree node. Layout state is its `UIElements` pool row (`arrange` → `ArrangeData`, `node` → `LayoutNode`); paint
is on the same row (`visual` → `VulkanControl`), resolved as it is drawn and finished per drawn row by `PaintRow`;
`ControlGeometry` is built in `Emit`. A plain
`Control` takes one child.

| region | holds |
|---|---|
| `authored layout` | the inherited XML sizing attrs (see XML authoring); `SetSize`, `SetWidth`, `SetHeight`, `IsWidthStar`, `IsHeightStar` |
| `paint` | colour, alpha, corner radii, edge, gradient (`gradientId`, word rebuilt in `InheritPaint`); `kind`, `sampler`, `SetUVRect`; palette — `role`, `paletteName`, `ownPalette`, `palette`, `groundBelow`, `colorAuthored`; `PaintOr`, `CopyPaint`; virtual `SetPaint`, `ApplyRole`, `PaintRow` (the drawn row — Clear gate, button state); `RolePaint`, `InheritPaint` (called by `UIEngine.Collect` each frame); shape — `edgeRole`, C#-only `cornerRole`/`accentRole`, `ApplyShape` (from the setters and `InheritPaint`); animation — `effect`/`RestartEffect`, `clip` (played in `OnStart`), `hoverClip`, `pressClip`, `stateBinding`, `RunClip`, `StopClip`, `Interacted` (hooks in the base `OnPointerEnter/Exit/Press/Release`, cleanup in `OnDestroy`) |
| `layout state` | `arrangedRect`, `DesiredSize`, `ClipRect`; `rotation` (`Quaternion` about the centre — sets `ArrangeFlags.Rotated`; emit, clip, subtree bounds and hit-test turn with it, [[note-images]]); flags `isMeasureDirty`, `isArrangeDirty`, `hidden`; `InvalidateLayout`, `InvalidateArrange`, `Hide`, `Show` |
| `layout (two-pass)` | `Measure`, `Arrange` (non-virtual entries), `MeasureCore`, `ArrangeCore` (what a control with its own layout overrides), `WriteArranged` (clip inheritance), `ArrangeByAlignment`, `RefreshSubtreeCache`, `Emit` |
| `pointer` | `onEnter`…`onScroll` + `RegisterOnX` + virtual `OnPointerX`; `hitTestable`; `HitsShape` (unturned point, default true); `ActiveContextTarget`, `takesActiveControl`; `contextMenu`, `stopsContextMenu`; drag: `draggable`, `StartDrag`, `onDrag`, `onDragStop`, `DraggingOverStart`/`DraggingOver`/`DraggingOverEnd`, `FinishDrag`, `DraggedOutOfWindow`/`DraggedIntoWindow`, `ChildDraggedOut` |
| `tree` | `AddChild` (throws on a second child), `RemoveChild`, `OnChildDetached` (a child destroyed while attached — marks order dirty, invalidates layout), `FindByName`, `MarkTreeOrderDirty` |

Static, outside regions: `EnumColorToHex`, `HexToRGB`.
Why: [[ui-engine-stack]] § landing 2, § landing 3, § landing 6a, § The active context is a question, § The drag gap;
palette: [[ui-palettes]].

### ControlXml — Control's XML half
- `Control.ParseXML(document)` — a tree from a registered `UIDocumentAsset` by name (e.g. `main`); the
  root element names its own type.
- `Control.ParseMenu(path)` — a `*.menu.xml` into a `ContextMenu`.
- `RecursiveParse` / `ResolveAttributes` — elements → `[A_XSDType]` classes, attributes →
  `[A_XSDElementProperty]` members; event attributes resolve against cached `[A_XSDActionDependency]`
  methods (`TaggedActions`).

Why: [[ui-engine-stack]] § XML event attributes.

### UIEngine — static, main thread

| region | holds |
|---|---|
| `layout` | `RegisterDirtyRoot`, `ResolveLayout`, `VerifySubtreeCache` |
| `input` | `Poll(window)` → `SolveHover`, `SolvePress` (dismisses menus), `SolveRelease` (opens `ContextMenus.OpenOn`), `SolveDrag`, `SolveScroll`, `SolveDragWindow`; `HitTest` (children last-to-first, optional `skip`), `Dispatch` (target up through parents until a handler returns `true`), `SetActiveControl`, `SetDragging`, `EndDrag`, `Forget`, `WindowOf` |
| `dense order` | `ElementOrder(pool)` — the DFS order the `UIElements` pool sorts to |
| `draw lists` | `Quads`; `BuildDrawLists` → `Collect(control, z)` → `Control.Emit(z)`, per window; `detailCullSize` — under it on either axis, `Collect` emits the control and skips its children |

No bootstrap step: each host sets `Engine.primary.ui.uiRoot` from `Control.ParseXML` itself.

### WindowRoot — `<WindowRoot>` · Control
A window's root, transparent; `RenderWindow.ui.uiRoot`. Fits the tree to the window: `FitTo`,
`ViewportSize`, `ToDesignSpace`, `ToWindowSpace` (its inverse); fields `windowingMode` (`KeepLocal`/`WindowSize`), `autoscaling`,
`scalingAxis`, `scale` (window pixels per design unit = display scale × UI zoom, set by `UIScaling`). At
scale 1, design space is window pixels. [[ui-scaling]]

### ContainerControl — `<Container>` · Control
Many children, both alignments default to `Stretch`. Base of every multi-child control.

### UIQuads — pool, not a class
`UIEngine.Quads`, declared in `Pools.pools.xml`; columns `ControlGeometry`, `VulkanControl`. Handle-less: rewound
once per frame (`DataPool.Rewind`), filled by `Control.Emit` / `TextRunControl.WriteGlyph` through
`DataPool.Append` + `GetSpan<T>()[row]`. Every window shares it; each window's `(first, count)` goes to
`UIEngineModule.PublishQuadRange` after its walk, and the render thread reads only that range and `Backing<T>()`.
Why: [[ui-quads-pool]], [[ui-draw-list]], [[ui-draw-list-publish]].

### Palettes — static, not a control
Loads `Palettes/*.palette.xml` (`LoadPalettes`, bootstrap), `Get(name)`, `Default` (named by `UISettings.palette`); owns the paint table
(`Paints` pool, `GpuPaint`). Paint words: `Inline`, `IsInline`, `ColorOf`. Derived words: `Surface`, `Ink`, `Step`;
`Contrast`; `RoleOffsets` for gradient role stops; `Code(palette, SyntaxToken)` for code colours. Types beside it: `PaletteDefinition` `<Palette>`, `PaletteRole`. The GPU copy is
`UIEngineModule.TableMirror<GpuPaint>`, set 1 binding 4, read by both stages.
Why: [[ui-palettes]].

### UIData — value types
- `LayoutRect` (`x`, `y`, `width`, `height`, `Right`, `Bottom`, `Overlaps`); `Thickness`, `CornerRadii`
  (+ their `TypeConverter`s); `QuadUVs`.
- Rows: `ArrangeData` (pool `UIElements`), `ControlGeometry`, `VulkanControl` — the GPU quad, typed by
  `VulkanControlType` (`MTSDFControl`, `PanelControl`, `ImageControl`); its colours are paint words
  (`paint`, `edgePaint`) plus `alpha`.
- Enums: `HorizontalAlignment`, `VerticalAlignment`, `DockMode`, `ArrangeFlags` (`Clip`, `Hidden`,
  `MeasureDirty`, `ArrangeDirty`), `ControlColor`.

### PointerEvent
`target`, `point`, `delta`, `button` (`leftButton` 0, `rightButton` 1), `tapCount`; phases in `PointerPhase`.

## Primitives

- **PanelControl** `<Panel>` · Control — coloured box, at most one child. Old `PanelControl`.
- **ButtonControl** `<Button>` · PanelControl — panel with hover/press colours; `state` (spring, in place on
  `visual.state`) is painted by the `PaintRow` override. `OnPointerEnter/Exit/Press/Release`. XML `HoverColorHex`, `PressColorHex`. Old `ButtonControl`,
  [[button-states-and-hover-bubbling]] (old).
- **CheckBoxControl** `<CheckBox>` · ButtonControl — 18×18 box, a 10×10 mark panel that is not
  hit-tested. `isChecked`, `onChanged(bool)`; a left release toggles; `SetScale(float)` sizes box and mark
  (document zoom). Old `CheckBoxControl`.
- **SliderControl** `<Slider>` · ContainerControl — `value` 0–1, a track and a thumb panel, neither
  hit-tested. Press jumps, drag follows (`Pick`); `onChanged(float)` fires on the gesture only, not on a
  `value` set. XML `Value`, `TrackHeight`, `ThumbWidth`, `TrackColorHex`, `ThumbColorHex`. No old counterpart.
- **DropdownControl** `<Dropdown>` · ButtonControl — caption, `options`, `onPicked`, `selected`; a
  left release opens `options` as a menu under it through `ContextMenus.OpenFrom` (so a dropdown inside a popup leaves the popup open), no narrower than itself, captions centered. `options` is code-only. Old `DropdownControl`.
- **ExpanderControl** `<Expander>` · ContainerControl — two `Line` rules and a round grip split in half; the first
  authored child is the content, slid open between the halves. `reveal` (animatable `ArrangeData.reveal`, Measure),
  `expanded` (XML `Expanded`, jumps), `Toggle` (tweens `reveal`). Nested `Viewport` (clips, holds the content against
  the lower rule) — `Hide()`n while settled closed, so the content is not measured, arranged, drawn or hit; `Toggle`
  shows it before opening, the close tween's `onDone` hides it. `Half` (24×12 clip) over `Circle` (24×24 button, acts
  on press, takes no active control) holding one `arrow`; `ArrangeCore` turns both arrows by `reveal × 180°`. No old
  counterpart. [[note-properties]], [[engine-testing]] § What the control targets found
- **KeyCaptureControl** `<KeyCapture>` · ButtonControl — shows a combo (`SetCombo`, static
  `Describe`); a left release hands the next key to `InputHandler.Capture`; `OnDestroy` cancels a live capture,
  or every keybind stays swallowed. Old `KeyCaptureControl`.
- **HintControl** (no XML) · PanelControl — translucent wash; the tab view's drop preview. Old `HintControl`.
- **IconControl** `<Icon>` · Control — one cell of an icon set's MTSDF atlas, by set and name
  (private `Rebind`). XML `Set`, `Icon`. Old `IconControl`.
- **ImageControl** `<Image>` · Control — a picture file through `TextureAsset.ForFile` (mipmapped, cached per
  path); `kind = ImageControl`, white paint. `MeasureCore` is native size capped to the width, or `Width`/`Height`
  with the aspect filling the other. XML `Source` (absolute, or relative to the data mounts). No old counterpart.
  [[note-images]]
- **CaretControl** (no XML) · Control — blinking insertion bar. `OnTick`; `Focus` opts in to ticking,
  `Blur` out ([[entity-tick-group]]). Old
  `CaretControl`, [[caret-blink-and-focus]] (old).

## Text

- **TextRunControl** abstract `<TextRun>` · Control — a paragraph as one control, a GPU quad per visible
  glyph. `spans` of `StyleSpan` (`count`, `style`, `colorHex`, `fontName`, `fontSize`, `gradient`,
  `strikethrough`, `underline`, `highlightHex`, `stylingType`, `fontSizeAuthored`, `IsBold`/`IsItalic`; picture:
  `imageSource`, `imageWidth`, `imageHeight`, `imageRotation`, `collision`, `Rotation`, `IsPicture`, `AsText`), `SetSpans`, `style`, `lineHeight`. `OptimalBreaks` (protected virtual, default false) makes `MeasureBlock` break Knuth-Plass; `Justify` then lets spaces shrink, and skips a line before a display math span (`BeforeDisplay`). A line ending at a soft hyphen (U+00AD, `TextLine.hyphen`) draws a "-" after its last character and the soft hyphen draws nothing elsewhere; `Align` and `Justify` count the hyphen in the line's visible width. `FirstLineIndent` (protected virtual, 0) moves line 0; a spacer span (`StyleSpan.spaceWidth`, unzoomed px, cleared by `AsText`) advances its width per character and the draw loop skips it. A picture
  span draws one image quad (`PictureSize`, `WriteImage` — turned, centred in its turned box); `PictureAt`, `PictureBox` (the line box), `PictureFrame` (drawn rect + turn); `MathAt`, `MathBox` (a formula's drawn box); a sheet link is an object span too (`sheetRef`, `IsSheet`): `BuildRuns` lays a `SheetBox` through `SheetLinks.Layout` and `WriteSheet` draws it in `Emit` [[sheets]]; a press on a picture or formula calls
  `IGlyphPressTarget.PicturePressed(run, index, button)` instead of `GlyphPressed`; `LayoutAround(slots)` re-lays
  lines around floats (`laidAround`), `PictureSizeAt`; every line geometry use adds `TextLine.left` [[note-images]].
  `kind` is `MTSDFControl`, so its children get the ground under it, not its ink. `Emit` writes a segment's
  highlight (`WriteHighlight`, gapped over `selectedFrom/To`) before its glyphs and underline/strike (`WriteRect`)
  after. [[text-decorations-and-colour]].
  Regions `layout` (`MeasureCore`, `ArrangeCore`, `Emit`) and `caret geometry` (`IndexAt`, `CaretAt`, `TextOrigin`,
  `Length`, `Lines`). `OnPointerPress` → `IGlyphPressTarget`. Virtual `FontFor(span)` and `Alignment`; `Align` shifts
  each line's `left` across its `TextLine.room` after `MeasureBlock`/`LayoutAround` [[markdown-blocks-and-alignment]].
  Both pass `_layout` as `MeasureBlock`'s `reuse`, so `Lines` is the same list and the same `TextLine`s after a
  remeasure [[large-note-measure-cost]], and a rewrap re-breaks `_layout`'s kept advances [[rewrap-advance-cache]].
  XML `Text`, `FontSize`, `FontName`. **`MeasureCore`
  returns the last `desired` while the run is clean and its wrap width unchanged** — anything `BuildRuns` reads
  must invalidate layout, which is why `colorHex` does. Why: [[ui-engine-stack]] § landing 4, § landing 6c.
- **LabelControl** `<Label>` · TextRunControl — read-only text, one line: overrides `Wraps` false, so
  overflow is cut at the box unless `ClipToBounds="false"`. Old `LabelControl`.
- **TextBoxControl** `<TextBox>` · ContainerControl, `IContext`, `IClipboardTarget` — single-line field with caret and
  selection. `Focus`, `SelectAll`, `WriteChar`, `Backspace(word)`, `Delete(word)`, `MoveCaret`, `Commit`, `Cancel`, `onEdited` (every text change),
  `Undo`/`Redo` (`history` of nested `FieldEdit`, cleared on focus/commit/cancel), `Copy`/`Cut`/`Paste`,
  `OnContextAdded`/`OnContextRemoved`; nested `FieldLine` carries the run and lets presses through to the box.
  `OnPointerTap`: double click selects the word (`SelectWordAt`, via `TextInputActions.WordEdge`), triple the field.
  `scrollX` slides the line left so the caret stays inside (`FollowCaret`, reset when not editing).
  XML `Text`, `FontSize`,
  `TextColorHex`, `SelectionColorHex`, `CaretColorHex`. Old `TextBoxControl`, [[note-naming-and-text-field]] (old).
- **EditableLabelControl** `<EditableLabel>` · ContainerControl — label that swaps to a text field on
  double-click. `BeginEdit`. XML `Text`, `FontSize`, `TextColorHex`, `FieldColorHex`. Old
  `EditableLabelControl`, [[inline-rename]] (old).

## Documents

- **BlockControl** (no XML) · TextRunControl — one block of a note: the paragraph's string with its runs as
  spans. `stylingType`, `listKind`/`listLevel`/`listMarker`/`isChecked`, resolved `shownMarker`/`listNumber`
  (`ShowMarker`, set by `DocumentControl.RenumberLists`), `ApplyLayout(DocumentLayout)` (copies `optimalBreaks`; list indent as
  `padding.left`, marker sync and size), `MeasureCore` (wraps inside the indent), `ArrangeCore` (shape `IconControl`
  centred in the indent, number `LabelControl` right-aligned, or `CheckBoxControl`), `AppendRun`, `Runs()` (both carry `Run.sheet`, the `Sheet` attribute of a sheet link). Also
  declares `ListMarker` and `ListMarkers` (`Format`, `ShapeIcon`, `IsNumbered`) [[list-markers]]. Region `text and spans`:
  `InsertText`, `RemoveText`, `SplitAt`, `AppendBlock`, `Snapshot`/`SliceSnapshot`/`Restore`/`From`,
  `InsertSlice`/`AppendSlice`, `StyleAt`, `StyleRange`, `SplitSpanAt`, `MergeSpans`. `firstIndent` (px; the `FirstLineIndent` override = `firstIndent * textZoom`) and `spaceBefore` (float?, null = the layout's block spacing) are copied in `SplitAt`, `SliceSnapshot`, `Restore`, `TakeKind` and carried by `BlockSnapshot`; `Typable` (private) keeps text typed beside a spacer in a text span, never the spacer. A boundary belongs to the
  span **after** it — except a picture span, which never grows: `TextSpanBeside`. `PictureChar` is U+FFFC;
  `SetPicture`. `alignment`, `language` (code fence), `codeWrap` (a Code block wraps only with it; `Wraps`), `TakeKind`;
  the checkbox finds its editor through `Editor()`, any depth; `ApplyLayout` also sets `fontName` from the style
  scheme and insets a Code block both sides; `Emit` draws a Code block's `SubField` ground per line, or a `Rule`'s
  `Line` stroke instead of text; `FontFor` gives a Code span the Code font [[markdown-blocks-and-alignment]].
  Replaces `Block`/`ContentBlock` + `TextRun`.
- **Run** `<Run>` — a run as the file writes it; exists at load and save only. `Text`, `Bold`, `Italic`,
  `Strikethrough`, `Underline`, `ColorHex`, `HighlightHex`, `ControlColor`, `Gradient`, `FontName`, `FontSize`, `FontSizeAuthored`,
  `StylingType`, `Image`, `Width`, `Height`.
- **DocumentControl** (no XML) · ContainerControl, `IGlyphPressTarget` — the content area. Regions `caret`
  (`SetCaret`, `CollapseSelection`, `GlyphPressed`/`OnPointerPress` → `PressAt`, `OnPointerTap`), `caret navigation` (`CaretPoint`,
  `CaretAtPoint`, `CaretOffText`, `AdjacentBlock`), `selection` (`SelectWord`, `SelectAll`,
  `OrderedSelection`, `Select`, `InSelection`, `SelectedFragment`, `CopySelection`), `text drag` (`BeginTextDrag`,
  `TextDragSource`, `ShowDropAt`/`HideDrop` — `dropCaret` is also the drag token; [[text-drag-and-drop]]), `pictures`
  (`PicturePressed`, `SelectedPicture`, `PictureFrame` (rect + quaternion), `ArrangePictureFrame` — frame and handles turned
  with the picture, `Begin/Resize/EndPictureResize`, `Begin/Rotate/EndPictureRotate`, `SetPicture`, `SetPictureWrap`,
  `SetPictureCollision`, `MinPictureY`, nested public `PictureHandle` (`Side`) and `PictureRotator` (edge-only ring,
  band `HitsShape`); frame, ring, then handles collected after the caret), `formulas` (`SelectedMath`, `StoredMath`, `SetMath`,
  `PlaceMath`/`RecordPlaced`/`RemovePlaced`, `RecordMath`, `MathAnchor`; a double-click in `OnPointerTap` opens the editor — [[math-in-notes]]), `sheet links` (`PasteLink`, `RefreshSheetLinks`, `RenameSheetLinks`; copy writes values — [[sheets]]) and
  `floating pictures` (`RegisterFloats`, `WrapsAround`, `FloatSlots : ILineSlots` with `Outline`/`TurnedSpan`, `SyncFloatViews`,
  `ArrangeFloats`, `Begin/Move/EndPictureMove`, `PlaceFloat`, `AnchorFor`, nested public `FloatingPicture` — press selects, press on
  the selected one drags; behind views before the blocks in `children`, front ones after — [[note-images]]), highlights inserted at the **head** of `children`, after the page panels, so they paint behind the text),
  `pages` (`page`, `zoom`, `Paginate` — blocks laid on paper and line tops rewritten, paragraphs a float reaches laid around it, `ArrangePages` — page panels at the very head, `Mm`; L7d/L7e: `NumberPages` writes running heads, the page number and the footnote rule into each sheet's private nested `PageMargins : ContainerControl`, `RunningHeads`, `PageAt`, footnote and float blocks skipped by the flow and placed from `insertControls` — `MeasureFootnotes`, `PlaceFootnotes`, `IsInsert`; L7f: `MeasureFloats`, `ReserveOrphanNotes`, `Paginate` rewinds for top floats; internal nested `PageSpace`, internal `PageFloats` — [[document-pages]]),
  `editing` (`DeleteSelection(restoreSelection)`, `PasteText`, `PasteImage`, `InsertAt`, `DropSelection`, `Insert`, `FragmentFromText`, `ForDestination` — [[text-clipboard]]; `SplitBlock`, `TypeChar`, `Blocks` — flat, table cells included; `TableViewport`,
  `OneContainer`), `markdown` (`TypeMarkdownLine` — ```` ```lang ````/`---` + Enter, `EndCodeBlock`, `LeaveRule`,
  `InsertRule`, `TypeInlineMarkdown`/`InlineOpener`/`MarkCode`; off when `plainText`; `HighlightCode`/`Tokenize` — runs of code lines
  coloured through `SyntaxTokenizer` into `TextRunControl.syntax`, from `MeasureCore`; `CodeWidth` — [[code-block-colouring]]), `tables`
  (`InsertTable`, `InsertTableRow`/`InsertTableColumn`/`DeleteTableRow`/`DeleteTableColumn`/`DeleteTable` through `ChangeTable`,
  `RecordTableResize`, `PutTable`, `AtListItemStart` — [[document-tables]]), `lists` (`TypeMarkdownPrefix` — also `#`/`> `,
  `SetListMarker`, `ListsChanged`, `RenumberLists` — run from `MeasureCore` when `listsDirty`,
  `ClearListAtCaret`, `ShiftListLevel`, `SetBlockList`), `styling` (`StyleSource`, `DisarmStyle`, `KeepDeletedStyle`,
  `CaretBlockStyling`, `ApplyStyle`, `ArmStyle`, `ApplyStyleTo`/`ApplyStyleBetween`, `SetBlockStyling`,
  `CaretBlockAlignment`, `SetBlockAlignment`, `SnapshotBlocks`, `RestoreBlocks`), `addressing` (`AddressOf`, `Resolve`, `CaretTo`) and `undo primitives`
  (`InsertText`, `RemoveText`, `DeleteBetween` — a rule head takes the tail's kind, `InsertFragment`, `RestoreKind`, `InsertBetween`, `JoinBlockWithNext`, `SetTable`). Also declares
  `CaretSlot`, `StyleDelta`, `CaretStyle` and `PageBands`. `header` — one control at the top margin of page 1,
  `Paginate` starts below it. `MeasureCore` skips the blocks while the paper is unchanged, and passes
  `Paginate(paper, from, to)` the changed block range so it resumes and stops early;
  `CollectChildren` draws only the pages and blocks the clip touches. Old `DocumentControl`. [[document-pages]], [[note-properties]], [[ui-draw-list]]
- **TableControl** (no XML; `<Table>` in a note) · GridListControl — a note's table: Fixed columns from
  `widths` × zoom, Auto rows, each cell a vertical `StackPanelControl` of `BlockControl`s. `AddRow`, `Cells`,
  `AppendBlocks` (feeds `DocumentControl.Blocks`), `StepCell`, `SetZoom`, `ApplyLayout`; region `pages`
  (`FirstRowHeight`, `Paginate` — page pushes written into `gapAfter`, zeroed again in `MeasureCore`); borders
  are `PanelControl`s appended to `children` past the cell assignments, and so are the column grips (nested
  private `ColumnGrip`, one per column on its right edge; region `column resize`: `BeginResize`/`Resize`/`EndResize`
  → `DocumentControl.RecordTableResize`). `RowCount`, `CellBlocks(row, column)` (finds the cell covering that grid position); `showBorders` (default true; borders honour spans and the flag); `AddRow(cells, spans)`; `alignment` (`TextAlignment`; `ArrangeCore` narrows and moves the grid for centred/right), `cellRules`/`leftRules`/`rightRules` (exact rules, drawn by private `ArrangeRules`, which joins touching segments), `cellPadding` + `ApplyInsets` (from `ApplyLayout`, `SetZoom`, `ReadTable`); column spans via `GridListControl.ColumnSpan`/`SetColumnSpan`. Lives inside the horizontal
  `ScrollableControl` that `Hosted()` builds, for both `LoadDocument` and `DocumentControl.PutTable`. [[document-tables]]
- **DocumentEditorControl** `<DocumentEditor>` · ScrollableControl, `IContext`, `IClipboardTarget` — one open note. Subscribes to `SheetBook.changed` (`BookChanged` → `RefreshSheetLinks`; `OnDestroy` unsubscribes); `PasteLink` (step "Paste link"), `RenameSheetLinks`.
  `Source`/`LoadPath`/`LoadDocument` (builds the properties header for `.md`/`.xml`), `Save` (refreshes it),
  `needsNaming`, `FocusCaret`; regions `styling` (forwards under a `BeginStep`; also `SetAlignment` — `.xml` only, `CanAlign`, `InsertRule`, `InsertFormula`/`EditFormula` (open a `FormulaPopup`, whose static `open` and `PasteLink()` insert a `\sheet{ref}` cell reference into its source box — [[sheets]]), `SetChecked`, `ShiftListLevel`,
  `Page`/`SetPage`, and the non-undoable `SetPalette`/`ApplyPalette`, `SetLayout`, `SetFrontmatterValue`, `SetReadOnly`;
  every edit path checks `Writable`), `ViewState`/`RestoreView` (`SessionTab`; scroll applied at the first Arrange through
  `pendingView`/`ScrollToView`), `selection` (`SelectLine`, `BeginSelectionDrag`, `OnDrag` + autoscroll), `caret movement`
  (`MoveCaret`, `MoveWord`, `MoveToEnd`), `editing` (`Backspace(word)`, `Delete(word)`, `SplitBlock`, `TypeChar`),
  `clipboard` (`Copy`, `Cut`, `Paste`, `PasteImage` — saves `attachments/`, `.txt` refuses), `text drop` (`DraggingOver*`, `FinishDrag`), `history`
  (`BeginStep`/`Undo`/`Redo`/`MarkDirty`), `focus`. `ArrangeCore` scrolls to the caret and **must never exit with
  the arrange flag set**. XML adds `CaretColorHex`, `SelectionColorHex` to the scrollable's. Old
  `DocumentEditorControl`.
- **DocumentToolbarControl** `<DocumentToolbar>` · StackPanelControl — the format bar for whichever
  note holds the caret; resolves it per press through `TextInputActions.Editor()` and takes no active
  control. `OnTick` (opted in at construction) reflects bold/italic/underline/highlight/styling/alignment/colour/size/page format;
  three alignment `IconButton`s after the styling dropdown, whose "Horizontal line" entry calls `InsertRule`;
  `OpenColors`/`OpenHighlights` drop presets plus a `Picker` (`ColorPickerControl` in a `ContextMenuContent`); `OpenPage` is the page menu; nested `ToolButton` (acts on press) and
  `PxBox` (the one part that does take the focus; captures the range on its press). XML `HoverColorHex`,
  `PressColorHex`, `IdleInkColorHex`, `ActiveInkColorHex`, `SeparatorColorHex`, `FieldColorHex`. Old
  `DocumentToolbarControl`, [[document-format-bar]], [[armed-style-at-the-caret]] (old).
- **TexEditorControl** — the LaTeX split view: source editor plus read-only preview, debounced `Recompile` ([[latex-editor]]). L7d: after layout `ResolvePages()` reads each label's page through `DocumentEditorControl.PageAt` and re-shows the tree (`Show(tree)`) when a `\pageref` number changed, at most twice (`pageReloads`); state `compiledAt`, `pageTree`, `pageRefs` (from `TexLowering.PageRefs`). Costs a one-frame "??" per recompile that has `\pageref`.
- **RichTextDocument** `<Document>` — the model: `blocks`, `name`, `layout`, `palette`, `created`, `modified`,
  `frontmatter`; `extensions`, `Load` (fills `created` from disk), `Save` (switch on the extension), `Stamp`.
  **DocumentEditSession** — the open file: `path`, `undo`, `isDirty`, `MarkDirty`, `Repath`, `Save` (stamps
  `modified` when dirty).
- **NotePropertiesControl** (no XML) · StackPanelControl — the header's rows: dates, palette dropdown, layout
  fields, List markers, Read only, a Markdown note's other frontmatter keys (`UserValue`: checkbox for true/false,
  field + Open for a link via static `openLink`; × `Remove`), and an "Add property" row (`AddProperty`). `Refresh`
  (posted after add/remove); swallows presses and taps; takes no active control. [[note-properties]]
- **Frontmatter** (static) — `Split`, `Entries` (`Entry`: key, value, editable, list, line, count), `Get`, `Set`
  (a block list writes back as one, `ListLines`).
- **DocumentXml** — `Load`/`Parse(XElement)` build blocks from a `<Document>` tree, `ToXml`/`Save` write one;
  the block level is written by hand. Since 6d no XSD type declares `"Document"`/`"Block"`/`"Run"`, so a
  note's `schemaLocation` validates nothing. [../Patterns/document-xml-persistence.md](../Patterns/document-xml-persistence.md)
- **NoteFormats.cs** — `MarkdownFormat` and `PlainTextFormat`: `Read(text, name) → XElement`,
  `Write(XElement) → string`. Regions `read`, `write`. [[note-file-formats]]
- **DocumentEdits.cs** — `DocumentAddress` (`(block, offset)`), `BlockSnapshot`,
  `DocumentFragment`, and the records `TextEdit`, `SplitEdit`, `DeleteRangeEdit`,
  `StyleRangeEdit`, `BlockStateEdit`, `PictureEdit`. Undo currency is snapshots and fragments, never control references — undo rebuilds
  blocks. Why: [[ui-engine-stack]] § landing 6c.
- **IFileEditor** interface — what a tab holds, to tab close, `TabViewControl.FileEditorOf`/`FindOpenDocuments`,
  session capture/restore and vault rename: `path`, `isDirty` (explicit — `Entity` has one), `Save`, `Repath`,
  `ViewState`, `RestoreView`. Implemented by `DocumentEditorControl`, `SheetEditorControl` and `PlannerEditorControl`. [[sheets]], [[planner]]

## Sheets

- **SheetEditorControl** (no XML) · StackPanelControl (vertical, Stretch both axes), `IClipboardTarget`, `IFileEditor` — one open sheet: public `scroller` (`ScrollableControl`; nested private `Scroller` holds the `SheetControl`) above a private page `strip` (`SheetPageStripControl`, hidden for a CSV); `Surface`
  ground, the scroller scrolls Both. `pageIndex`, `page`, private `Build(int)`. `LoadPath` (through `SheetBook.Get` — tabs of one file share the document), `Load`, `document`,
  `path`, `unsaved`, `undo` (the document's). Subscribes to `SheetBook.changed` (private `BookChanged` redraws; own
  file's edits set `unsaved`), unsubscribes in `OnDestroy`. Regions `file` (`Save`, `Repath`, `Undo`, `Redo`), `selection` (`activeRow/Column`, `anchorRow/Column`, `Select`, `Move`, `Enter`, `Tab`,
  `SelectAll`, `RequestScrollToActive`), `editing` (`editing`, `editRow/Column`, `BeginEdit(keep)`, `TypeOver`, private
  `FinishEdit`/`CancelEdit`, `Clear`, private `Write` — the one place a `SheetCellEdit` is built), `clipboard` (TSV of
  values; `Copy` keeps static `copiedText`/`copiedFrom` for `PasteLink`),
  `view` (`ViewState`/`RestoreView` in `SessionTab`'s caret/anchor/`scrollX`/`topDelta`, page index in `topBlock`; the scroller's `ArrangeCore` applies a pending
  view or scrolls the active cell clear of the headers), `formatting` (`ToggleBold`, `SetFill`, `SetNumberFormat`, `IsSelected`, internal `ResizeBand`), `pages` (`ShowPage`, `AddPage`, `DeletePage`, `RenamePage`, `ExportPage`, private `FreePageName`, `Record`), `layers` (`editLayer`, `EditLayer`, `ToggleLayer`, `AddLayer`, `DeleteLayer`, private `FreeLayerName`). Fixed sheets and insert: `Select` clamps to the page on a fixed doc, private `BookChanged` re-clamps anchor/active when an undo shrinks a fixed page, `Grow(rows, columns)` (one `SheetSizeEdit`, label "Grow page"), `Paste` past the edge of a fixed page pushes a `SheetSizeEdit` and the `SheetCellEdit` in one undo scope, `Insert(bool column)` (count = selection span, inserted before it, one `SheetInsertEdit`). [[sheets]]
- **SheetPageStripControl** (no XML) · StackPanelControl — the page strip under the grid, Chrome 26 px: private `PageTab` : ButtonControl per page (`EditableLabelControl` caption; press shows the page, double-tap renames, `contextMenu = "sheet-page"`), a permanent "+" button, a star filler, a "Layers" button opening `SheetLayersControl` as `ContextMenuContent` above itself. `Sync()` rebuilds tabs only when pages or names changed. Its static ctor `ContextMenus.Register`s the code-built menu `sheet-page` (Rename, Delete, Export as CSV). [[sheets]]
- **SheetLayersControl** (no XML) · StackPanelControl — the layers panel: `panelWidth`, `Sync()`; a row per layer, top first, visibility toggle (`bullet-disc`/`bullet-circle` icons) and name, the edited layer lit; "Add layer", "Delete layer". [[sheets]]
- **SheetGrowPopup** (internal, no XML) — the grow popup of a fixed sheet, opened by a right press on a "+" strip: built like `FormulaPopup` (`ContextMenus.Open` + `ContextMenuContent`), two number boxes "Add horizontal" (columns) and "Add vertical" (rows), both starting at 0; the bottom strip focuses "Add vertical", the right strip "Add horizontal". Enter = one `SheetEditorControl.Grow`, Esc/outside click cancels, static `Tab()` swaps the boxes (`Sheet.Tab`/`Sheet.TabBack` try it first); non-numbers count as 0. [[sheets]]
- **SheetControl** (no XML) · ContainerControl — the grid canvas for one `SheetPage`. Measures to the used extent plus
  spare; `ArrangeCore` reads the scroller's inner rect and lays out the visible window only: `ArrangeGrid` (pooled lines
  and `LabelControl`s in a private `Parts` container), `ArrangeSelection` (wash, 4 outline bars, the `field`
  `TextBoxControl` over the edited cell), `ArrangeHeaders` (pinned to the viewport). `CellRect`, `CellAt`,
  `headerWidth`/`headerHeight`, `CellsChanged`. Pointer: press selects (Shift extends) and starts a drag, drag extends,
  double tap edits. Formatting: `fills` Parts drawn first, bold through the label's run style, text through `Display(format.number)`; header-edge resize (`EdgeAt`, `OnDrag` live, `OnDragStop` records through `ResizeBand`, `ShowCursor`); spare pooled parts arrange to `Hidden`; right-click uses the `sheet` context menu. Measuring above is the unfixed type; a fixed sheet (`SheetDocument.fixedSize`) measures to the page's `rows` x `columns` plus a 20 px strip (`growWidth`), arrange loops stop at the page edge, grid lines end at the grid edge, and two fixed parts `growRows`/`growColumns` ("+" labels, Chrome / MutedInk, cut to the viewport) are placed by `ArrangeGrow`; `GrowAt` hit-tests them. The strips sit `growGap` (4 px) off the grid and each other (`MeasureCore` adds `growWidth + growGap * 2`), with `cornerRole = CornerRole.Control` and gradient `sheet-grow` (radial Field → Accent at 0.6 alpha, `Engine.gradients.xml`). `ArrangeCore` ends with `Parts.Settle` on `fills`, `grid` and `headers`, re-arranging each layer once its pooled children are placed so its cached `subtreeBounds` are current. Left press on a strip adds 1, Shift+left adds `SheetSettings.grow.step`, right press sets `growMenu` and `stopsContextMenu` and `OnPointerRelease` posts (`Engine.Post`) the `SheetGrowPopup` next tick; `growPresses` makes `OnPointerTap` ignore a double tap that involved a strip press. [[sheets]]
- **SheetFormula.cs** — `SheetValue` (`kind`, `FromRaw`, `Display`), `SheetFormula` (`Parse`, `Evaluate`,
  `References`, `ShiftCells`, `RenamePrefix`, `RenamePage`, `Prefix`, error-code consts, grid bounds; private nodes `Constant`, `Reference`, `Negate`, `Binary`, `Compare` — `= <> < > <= >=` giving 1/0 — and `Call` — SUM, MIN, MAX, AVERAGE, ROUND, ROUNDUP, ROUNDDOWN, IF, with `Aggregate` and `Rounded`). **SheetCalc.cs** — `SheetCellId`, `SheetCalc`
  (`Value`, `Changed`, `Add`/`Remove`, `RecalcAll`; internal `Read`, `PageNamed` — null for a file-part reference from a CSV). **SheetBook.cs** — static: one
  `SheetDocument` per path (`Get`, `Register`/`Unregister`, `OwnerOf`, `Resolve` via `findSheet`), the shared `calc`,
  `changed` event, vault hooks `Created`/`Deleted`/`Renamed`/`Clear` (CSV paths too), `FileName` (reference file part), `PageRenamed`, `Shifted`, `Restructured`, Thorium-set `vaultSheets`/`vaultNotes`. [[sheets]]
- **SheetLinks.cs** — `SheetLinks` (static: `Parse`, `Reference`, `IsLink`, `Layout` → `SheetBox`, `Plain`, `Renamed`, `PageRenamed`, `Shifted`; region `math`: `HasMathLinks`, `ExpandMath`, `RenameMath` — `\sheet{…}` inside a formula, expanded to the cell's value before `MathParser` sees it), `SheetBox`,
  `SheetBoxCell`: sheet links shown in notes, drawn by `TextRunControl`; no control of their own. `SheetEditorControl.CopiedReference`,
  `TextInputActions.PasteLink` (`Text.PasteLink`: `FormulaPopup.PasteLink()` → note editor → plain paste) feed Paste link into a note or its open formula popup. [[sheets]]
- **SheetDocument.cs** — `SheetDocument` (`pages`, `undo`, `extension`, `IsSheet`, `Blank(name, fixedSize)`, `fixedSize`, addressing statics), `SheetPage`
  (bands, `Shown`, `Used`, `defaultSize`, `rows`/`columns`, `Extend`, `Shift`), `SheetLayer` (`Get`/`Set`), `SheetCell`. **SheetXml** reads/writes `.sheet.xml`, keeping
  unknown elements. `SheetFormat` (per-page `formats`). **SheetEdits.cs** — `SheetCellEdit`, `SheetFormatEdit`, `SheetBandEdit`, `SheetPageEdit`, `SheetPageRenameEdit`, `SheetLayerEdit`, `SheetLayerShowEdit`, `SheetSizeEdit`, `SheetInsertEdit`; also `SheetSettings`/`SheetGrowSetting` (`grow.step`, Settings category "Sheets"). `SheetDocument` also holds `csvDelimiter`/`csvBom`/`isCsv`; `Load`/`Save` route by extension to `SheetCsv` or `SheetXml`. **SheetCsv.cs** — static `SheetCsv`: `extension`, `IsCsv`, `Delimiter`, `Read`, `Write`, `Load`, `Save`, `Export`. **SheetActions** — `Sheet.*` keybind actions (region `formatting`: `Sheet.Bold`, `Sheet.Fill*`, `Sheet.Format*`; `Sheet.InsertRowsAbove`, `Sheet.InsertColumnsLeft`). [[sheets]]

## Planner

- **PlannerEditorControl** (no XML) · StackPanelControl, `IFileEditor` — one open planner (`*.planner.xml`): a toolbar (Gantt | Board | Calendar on the left, Hour/Day/Week/Month on the right, Gantt only; `<` `Today` `>` and Day/Week/Month span buttons, Calendar only) over a `scroller` holding the `chart` (`GanttChartControl`), the `board` (`PlannerBoardControl`) or the `calendar` (`CalendarControl`). `PlannerView { Gantt, Board, Calendar }`; `document`, `path`, `unsaved`, `view`, `zoom` (default Day), `LoadPath`, `Load`, `ShowView`, `SetZoom` (keeps the time at the viewport's left edge), `Save`, `Repath`, `ViewState`/`RestoreView` (view in `SessionTab.topBlock`, zoom in `caretBlock`, scroll in `scrollX`/`topDelta`). Private nested `PlannerScroller`. P2 editing: `undo`, `selected`, `clock` (`Func<DateTime>`, default `DateTime.Now`) / `Now`, `Select`, `AddTicket`, `EditSelected`, `MoveSelected(category)`, `MoveTicket(ticket, category, index)` (P3: target column without the ticket, insert at the clamped index, one "Move ticket" step changing `order` and `categoryId` of every ticket whose value differs; the source column is not renumbered), `DeleteSelected`, `Apply(ticket?, draft)` (edit → one "Edit ticket" step; null ticket → "Add ticket"), internal `CommitTime(ticket, before, label)`, `Undo`, `Redo`, private `Record(label, edit)` (as `SheetEditorControl.Record`), private `DocumentChanged` (sets `unsaved`, drops a deleted selection, redraws), `OnDestroy` unsubscribes; toolbar "+ Ticket". P4 categories: toolbar "+ Category"; `pickedCategory` + private `pickedFrom`/`pickedAt` (the category last pressed on and where), `PickCategory(category, from, point)`, `AddCategory()`, `EditPickedCategory()`, `ApplyCategory(category?, name, colorHex)` (null → "Add category", appended; else "Edit category" when changed), `DeleteCategory(category)` ("Delete category"; never the last one); `DocumentChanged` calls `board?.RebuildSoon()`. PC calendar: `calendar`, `span` (default Week), private `anchorDay`, `anchor` (today when unset), `ShowSpan(CalendarSpan)`, `Step(int direction)` (±1 day / 7 days / 1 month), `GoToday()`, `NewAt(TimeRange, Control from, Vector2 point)`, private `ShowIf`, private `pendingTop` + `MorningTop()` (calendar opens at 07:00; applied in `PlannerScroller.ArrangeCore`); `EditSelected` anchors the popup under `calendar.BlockRect`; `DocumentChanged` also calls `calendar?.Changed()`; `ViewState`/`RestoreView` store the span in `SessionTab.anchorBlock` and the anchor day (`DateOnly.DayNumber`, 0 = today) in `topOffset`. C2: `Apply` and `CommitTime` record the private `WithFollowers(ticket, before, after)` — the edit plus every downstream follower shifted by the change of the ticket's END, transitively, visited-set guarded, one `PlannerTicketEdit`. [[planner]]
- **GanttChartControl** (no XML) · ContainerControl, built like `SheetControl` — `PlannerZoom { Hour, Day, Week, Month }` (day widths 768 / 32 / 10 / 3 px); `nameWidth` 200, `bandHeight` 22, `headerHeight` 44, `rowHeight` 28; `origin`/`end` (every ticket and today, padded 4 units, 6 at Hour); `Offset`, `X`, `TimeAt`, `RowTop`, `RowOf`, `BarRect`, internal `RowsChanged`. Rows: each category (shaded) then its tickets. Only rows/units in the viewport get pooled parts (private nested `ChartParts`); name column pinned left, two-band date header pinned top (upper Day/Month/Year, lower the zoom unit; weeks start Monday), today line in Accent (uses `editor.Now`). P2 pointer region: `contextMenu = "planner"`; static ctor registers menu source `"planner-categories"`; selection outline (4 Ink parts, `outlineWidth` 2); public `TicketAt(point)`, private `GripAt` (edge zone → resize, middle → move), `OnPointerPress` (left: select + `StartDrag`; right: select the row's ticket, return false so the menu opens), `OnDrag` (snaps the delta: hour on Hour zoom for a timed ticket, day otherwise), `OnDragStop` (one "Move ticket" / "Resize ticket" step), `OnPointerMove`/`OnPointerExit` (HResize cursor on edges), `OnPointerTap` (double → edit). P4: public `CategoryAt(point)`, private `RowAt(point)` (`TicketAt` uses it); every press calls `editor.PickCategory(CategoryAt(point), this, point)`; a double click on a category row opens the category popup; `stopsContextMenu = true`. C2: attachments drawn as blocks in the kind colour at alpha 0.4 before / after the bar in its row (`attachmentParts`, bars layer); link elbows (`linkParts`, grid layer, MutedInk, `elbowWidth` 8) when either row is in view; span uses `OuterStart` / `OuterEnd`. [[planner]]
- **PlannerBoardControl** (no XML) · horizontal StackPanelControl — `columnWidth` 260, private `Rebuild()`, internal `RebuildSoon()` (one `Engine.Post`ed `Rebuild` per tick, skipped if the board was destroyed; flag `rebuildPending`), public static `Dates(TimeRange)`. A column per category (swatch + "Name  count" heading); a card is a colour strip across the top, name, dates, "by <creator>". The strip is on top because a side strip with no preferred height fills the cross axis of a horizontal stack. P3 drag: public nested `TicketCard : StackPanelControl` (`board`, `ticket`; arms on a left press, claims the drag past `dragThreshold` 4 px with `StartDrag` + `DragGhost.Show`, `OnDragStop` → `DragGhost.Hide`, as `TabStripButtonControl`); private `columns` (category, column panel, cards) built by `Rebuild`; public `CardsIn(category)`; private static `Fill(card, document, ticket)`. Region `drop`: private `DropAt(dragged, point, out column, out index)` (column whose x-range holds the point, else the nearest; index = the column's other cards whose centre is above the point; only a `TicketCard` of this board), `DraggingOverStart`/`DraggingOver` → `Mark`, `DraggingOverEnd` → `Unmark`, `FinishDrag` → `editor.MoveTicket(...)` directly (P3 posted it; P4 removed that — the rebuild itself is posted, see `RebuildSoon` and [[rebuild-inside-pointer-dispatch]]). Marker: target column `edgeRole = Accent`, `edgeThickness` 1.5 px; a `dropGap` 28 px top margin on the card the drop lands before, or bottom margin on the last card when it lands last; `markedColumn`/`markedCard`/`markedIndex`, reset by `Unmark` and `Rebuild`. P4: columns have `contextMenu = "planner-category"`, `stopsContextMenu = true`, a press picks their category, a double click opens its popup; `TicketCard` press picks its category and returns handled only for a left press (right-click reaches the column's menu), new `OnPointerTap` swallows taps so a double click on a card does not open the column's category; cards show a row of initials chips (`chipHeight` 18, SubField, bold) when the ticket has assignees. [[planner]]
- **CalendarControl** (no XML) · ContainerControl — the planner's Calendar view. `CalendarSpan { Day, Week, Month }`; consts `gutterWidth` 56, `headerHeight` 24, `laneHeight` 22, `hourHeight` 48 (public), private `blockInset` 2, `labelHeight` 18, `cellHeader` 20, `chipHeight` 18, `nowWidth` 2, `dotSize` 8, `outlineWidth` 2, `grabHeight` 6, `attachmentAlpha` 0.4; `public static readonly TimeSpan snap` 15 min. Public `first` (`DateOnly`: Day = anchor, Week = the anchor's Monday, Month = the Monday on/before the 1st, 6×7 cells), `TopHeight` (pinned header + all-day strip, ≥1 lane), `BodyTop`, `PointAt(DateTime)`, `TimeAt(Vector2)` (unsnapped), `DayAt(Vector2)`, `BlockRect(ticket)` (first shown block, nullable), `TicketAt(point)`, static `Pack(IReadOnlyList<(DateTime start, DateTime end)>) → (int column, int columns)[]`; internal `Changed()`. Overrides `OnTick` (invalidates arrange when the minute of `editor.Now` changes, `FrameScheduler.RequestFrameAt` for the next minute, `SetTicking(true)` in the ctor), `OnPointerPress`/`OnDrag`/`OnDragStop`/`OnPointerMove`/`OnPointerExit`/`OnPointerTap`. Private nested `CalendarParts` (layer; not `ChartParts`, name-collision trap), `CalendarPool<T>` (pooled parts, hides what an arrange did not take), `Piece`, `PieceKind { Timed, AllDay, Chip }`, `Grip { Move, Start, End }`; pieces built in Measure (all-day lanes packed, then per-day timed pieces), rects in Arrange. `contextMenu = "planner"`, `stopsContextMenu = true`. Day/Week: hour gutter pinned left, day-name header + all-day strip pinned top (all-day tickets and timed ones ≥ 24 h), a timed ticket crossing midnight is one piece per day, blocks under the pinned header are not hit, now line + dot in Danger across today. Month: weekday row, day number (Accent today, MutedInk outside the month), chips "HH:mm Name", "+N more". Selection outline in Ink. Drag: Day/Week timed — middle moves, top/bottom edge resizes (VResize cursor), 15 min snap, min one snap; strip and Month chips move by whole days; release → `editor.CommitTime`. Double click: block → edit popup; empty grid slot → new-ticket popup (1 h from the slot floored to 15 min); strip or month cell → new all-day ticket. Width is the viewport (no horizontal scroll). [[planner]]
- **PlannerDocument.cs** — `PlannerDocument` (`extension` ".planner.xml", `defaultColor`, `name`, `categories`, `tickets`, `extra`, `undo`, `changed`/`Changed()`, `NextOrder(category)`, `IsPlanner`, `Load`, `Save`, `Blank`, `CategoryOf`, `ColorOf`, `TicketsIn`), `PlannerCategory`, `PlannerTicket` (`Clone`, `CopyFrom`), `TimeRange` (record struct; exclusive end; `Days`). C2: `PlannerTicket.attachments` (`List<PlannerAttachment>`), `follows` (`Guid?`), `OuterStart`/`OuterEnd` (time incl. attachments), `Attached(bool before)` (`Clone`/`CopyFrom` copy both); `PlannerDocument.Find(Guid)`, `Followers(ticket)` (direct), `Upstream(ticket, other)` (other follows ticket, directly or not; cycle-guarded). **PlannerXml.cs** — `Load`, `Save`, `Parse`, `ToXml`; unknown elements kept; Ticket `Follows="guid"` (after `Order`), child `<Attachment Kind Side="Before|After" Minutes/>` (after Assignee; one without a kind or positive minutes is kept unknown). `ArctisAurora.Core.Users.User` (`current` = "Grexen", `Named`, `initials` — first letters of the first two words, else the first two letters of a one-word name, upper-cased; empty → "?") is the ticket creator and assignee type. [[planner]]
- **PlannerEdits.cs** — `PlannerTicketEdit(document, ticket, before, after)` (copies before/after over the live ticket, raises `Changed`), `PlannerTicketAddEdit(document, ticket, index, add)` (insert at index / remove). P3: `PlannerTicketEdit` holds a list of (ticket, before, after); new constructor `PlannerTicketEdit(document, List<(ticket, before, after)>)`, the single-ticket constructor delegates to it; Undo/Redo copy every entry, then raise `Changed` once. P4: `PlannerCategoryEdit(document, category, (name, colorHex) before, (name, colorHex) after)`; `PlannerCategoryAddEdit(document, category, index, add)` (insert at index / remove; raises `Changed`). [[planner]]
- **PlannerTicketPopup** (internal, built like `SheetGrowPopup`) — Name, Category (`DropdownControl`, SubField), Start, End, Assignees (comma-separated → `User.Named`), Colour (`ColorPickerControl`), "Created by", an Apply button. Enter in any box or Apply = one undo step; Esc / outside click cancels. Dates `yyyy-MM-dd` (all-day, end shown as the last day) or `yyyy-MM-dd HH:mm` (timed). No Tab between boxes. C2: `Open(editor, from, point, ticket, TimeRange? time = null)` (a new ticket uses `time`, else today all day); rows "Before" / "After" (comma text "Travel 30m, Prep 1h"; `1h30` = 90 min; bare number = minutes; no length = the kind's default; unknown kind dropped; internal `FormatAttachments`, `ParseAttachments`) and "Follows" (`DropdownControl` "(none)" + every ticket except this one and those downstream of it). [[planner]]
- **PlannerCategoryPopup** (internal, like `PlannerTicketPopup`) — Name box, `ColorPickerControl`, a fixed-height button row (Delete — only when editing and more than one category — a spacer, Apply). Enter / Apply apply; an empty name keeps the old one (or "New category"); Esc / outside click cancels. Opened by "+ Category", a double click on a category row (Gantt) or column (board), or the "Edit category" / "Add category" menu lines. [[planner]]
- **PlannerActions** — `Planner.Undo`, `Planner.Redo`, `Planner.Delete`, `Planner.Edit`, `Planner.Save`, `Planner.EditCategory`, `Planner.AddCategory` ("Input" actions; no-op unless a planner holds the active control); second binds in `InputMap.inputs.xml`. Menus: `Planner.menu.xml` gains "Edit category" / "Add category"; `PlannerCategory.menu.xml` (same two lines) is registered as `planner-category` in `ThoriumAssets.assets.xml`. [[planner]]
- **PlannerAttachments.cs** (no XML) — `[A_XSDType("AttachmentKind","UI")] PlannerAttachmentKind` (`name`, `colorHex` default #8E8E93, `minutes` default 30), `[A_XSDType("AttachmentKinds","UI")] PlannerAttachmentKindMap` (schema root), `readonly record struct PlannerAttachment(string kind, bool before, TimeSpan duration)`, static `PlannerAttachmentKinds` (`all`, `Find(name)` case-insensitive, bootstrap step `PlannerAttachmentKinds.Load` — engine `Engine.attachments.xml` then optional host `Attachments.attachments.xml`, a later same-name kind replaces an earlier one). Kinds are engine data like gradients/palettes. [[planner]]

## Layout containers

- **StackPanelControl** `<StackPanel>` · ContainerControl — children in a row or column; star children
  split what is left by weight; a `hidden` child gets no slot and no spacing. XML `Orientation`, `Spacing` — properties
  mirrored into the row's `LayoutNode` (`axis`, `spacing`), invalidating layout. Old `StackPanelControl`,
  [[stack-panel-arrange-clamp]] (old).
- **DockingControl** `<Dock>` · ContainerControl — children docked by their `DockMode`. XML
  `LastChildFill`. Old `DockingControl`.
- **GridListControl** `<GridList>` · ContainerControl — band grid; a child claims its cell with
  `Grid.Row`/`Grid.Column`. Child elements `<RowDefinition Height SizeMode GapAfter>`,
  `<ColumnDefinition Width SizeMode GapAfter>`; `SizeMode` is `Fixed`/`Auto`/`Star`. Measure resolves
  Fixed → Auto columns → Star columns → Auto rows (each child at its spanned column width) → Star rows; a cell's
  rect stops before its last band's gap. Allocation-free: loops, and band offsets in two kept arrays
  (`BuildOffsets`). Old `GridListControl`.
- **ScrollableControl** `<Scrollable>` (0–1 child) · ContainerControl — scrolls its child; one thumb
  per axis, appended last so hit-test reaches them first; no gutter — thumbs overlay the content's
  edge. Regions `properties`, `state`, `layout`,
  `scrolling`: `OnScrollInput`, `OnPointerScroll` (wheel X drives X, wheel Y drives Y, never crossed; an
  unmoved axis bubbles), `SetScrollOffset`/`GetScrollOffset`, `ScrollIntoView`. XML
  `ScrollDirection`, `ScrollSensitivity`, `Overscroll`, `ThumbColorHex`, `ThumbHoverColorHex`,
  `ThumbPressColorHex`. Old `ScrollableControl`, [[scrollbar-thumb]], [[scroll-overscroll]] (old).
- **ScrollThumbControl** (no XML) · ButtonControl — the thumb; its drag becomes a scroll offset.
  `OnPointerPress`, `OnDrag`. Old `ScrollThumbControl`.
- **SplitViewControl** `<SplitView>` · StackPanelControl — panes with grips between; static
  `Split(tabView, SplitEdge)` and `Collapse(split, leaving)`. Old `SplitViewControl`,
  [[splitter-and-pane-sizing]] (old).
- **SplitterControl** `<Splitter>` · ButtonControl — grip: resizes a sized pane, or trades weight
  between two star panes (`DragStars`). `OnDrag`, `OnPointerEnter/Exit/Press`. Old `SplitterControl`.

## Tabs

- **TabViewControl** `<TabView>` · ContainerControl — a strip of tab buttons over its
  `TabItemControl` pages. Regions `properties`, `drop`, `strip`, `layout`. `SetActive`, `CloseTab`,
  `CloseOthers`, `CloseToTheRight`, `SplitOff(item, edge)`; subclass hooks `NewOfSameKind`, `BuildCaption`;
  drop target via `DraggingOverStart/Over/End` (hint preview) and `FinishDrag`; nested `CloseButtonControl`.
  Each strip button names `tabContextMenu` and stops the menu walk. XML `TabHeight`, `TabWidth`, `TabColorHex`,
  `ActiveTabColorHex`, `TabHoverColorHex`, `TabInkColorHex`, `GripColorHex`, `GripHoverColorHex`,
  `GripPressColorHex`, `TabContextMenu`. Old `TabViewControl`, [[tab-view-control]] (old),
  [[context-menus]] § Menu bar, tab and view menus.
- **TabItemControl** `<TabItem>` · PanelControl — one page; XML `Header` is its caption. Old `TabItemControl`.
- **TabStripButtonControl** (no XML) · ButtonControl — a tab in the strip; a press moved past a
  threshold becomes a drag and shows `DragGhost`, which `OnDragStop` hides. `OnPointerPress/Move/Release`,
  `OnDragStop`. Holds two children, caption then close button (`AddChild` bypasses the one-child rule), and lays
  them out itself (region `layout`): ✕ pinned right at full height, caption in the rest, placed by its own
  position fractions and margin — `BuildTab` gives it the bottom inset as a margin. A squeezed tab keeps its caption
  start and ✕ visible. Old `TabStripButtonControl`.
- **DragGhost** static — the dragged control, drawn again in a floating window centred on the pointer.
  `Show(control)`, `Hide`, `Follow`; sets `UIEngineModule.rangeRoot` and `rangeRect`; opacity from
  `Control.draggingOpacity` or the `DragGhost` UI setting. Old `DragGhost`,
  [[render-thread-reads-pool-row]].
- **EditableTabsControl** `<EditableTabs>` · TabViewControl — captions rename in place on
  double-click (`BuildCaption`, `NewOfSameKind`). Old `EditableTabsControl`, [[tab-rename-and-double-click]] (old).

## Window chrome and shell

- **WindowFrameControl** `<WindowFrame>` · ContainerControl — resize grips on an undecorated window's
  edges, with cursor shapes. `MeasureCore`, `ArrangeCore`; private `EnsureGrips`, `BeginResize`, `ApplyResize`. Old
  `WindowFrameControl`, [[window-frame-resize]] (old).
- **TitleBarControl** `<TitleBar>` · StackPanelControl — a left press hands the window to the OS
  caption-drag loop (`os.DragByCaption`); `Pump` keeps layout and draw lists running inside it. Old
  `TitleBarControl`, [[window-chrome-and-label]] (old).
- **WorkspaceControl** `<Workspace>` (≤ 1 child) · PanelControl — holds the pane layout.
  `LoadDefault`, `LoadPane`, static `In(root)`. XML `Default`, `Pane`. Old `WorkspaceControl`,
  [[session-restore]] (old).
- **MenuButtonControl** `<MenuButton ContextMenu="…">` · ButtonControl — menu bar entry: a left
  press drops only the menu it names under it; `takesActiveControl => false`. Caption is an authored
  `<Label>` child. Old `MenuButtonControl`, [[context-menus]] § Menu bar, tab and view menus.

## Files

- **FileBrowserControl** abstract, no XML · ScrollableControl — rows of files under `RootPath`.
  `Rebuild` → `PopulateRows` → `AddRow`; `BeginRename`. Host hooks: abstract `RootPath`, `PopulateRows`,
  `Activate(file)`; virtual `Accepts`, `DisplayName`, `Rename`. XML `RowHeight`, `Indent`, `RowSpacing`,
  `RowInset`, `GutterWidth`, `RowFontSize`, `RowColorHex`, `RowHoverColorHex`, `RowPressColorHex`,
  `FolderColorHex`, `FileColorHex`, `RowFieldColorHex`. Old `FileBrowserControl`, [[file-browser-tree]] (old).
- **FileTreeControl** abstract · FileBrowserControl — folders expand in place: `PopulateRows`,
  `Expand`, `Toggle`. Opened rows tween `Height` 0 → `rowHeight` (`Track`); `Collapse` tweens them to 0
  and rebuilds on the last one's `onDone`. `listed` = rows with depth; `turning` puts an `expander-*`
  effect on the toggled folder's `gutter`. Old `FileTreeControl`.
- **FileRowControl** `<FileRow>` · ButtonControl — one file or folder row. Old `FileRowControl`.

## Context menus

- **ContextMenus** static — menus by name. `Get` parses the `ContextMenuAsset` on first use (`Register`
  pre-seeds); `Collect(control)` walks up the parents gathering each `contextMenu`, a line between groups,
  until a `stopsContextMenu`. `OpenOn`/`Open` (`onClosed` — run once when the top-level menu closes, whoever closes it)/`Close`; `OpenFrom(entries, on, point, width, centered)` (opens inside an open panel at depth d+1 with opener null; closes deeper panels); `RegisterSource(name, build)` (a submenu with a `source` builds its entries each time it opens; unknown source warns once and does not open); row input `Entered`, `Clicked` (a button row closes from `NestedFrom(panel)` — the shallowest open `OpenFrom` panel, else the whole menu); `DismissUnlessInside` (a press inside panel d closes an `OpenFrom` panel at d+1);
  `Tick`. Hosted as the root's last child when it fits, its own window when not. Regions `menus`,
  `open and close`, `input`. Why: [[context-menus]].
- **ContextMenuControl** (no XML) · StackPanelControl — one menu panel at a `depth`; a nested
  `Row` : ButtonControl per entry (enter → `Entered` opens a submenu, release → `Clicked`; binding
  `menu-row`). `reveal` (0–1, `ArrangeData.reveal`, clip `menu-open`) slides it down: `ArrangeCore` shifts it up and `ClipSubtree`s
  it at its anchor. Old `ContextMenuControl`.
- **ContextMenuEntries** — the menu document: root `<ContextMenu>` (`ContextMenu`); entries
  (`ContextMenuEntry`) `<ContextButton Text Action>` (`ContextMenuButton`), `<ContextLine>`
  (`ContextMenuLine`), `<ContextSubmenu Text Source>` (`ContextMenuSubmenu`, `source`); code-only `ContextMenuContent` hosts a
  control as itself among the rows (the colour pickers).
- **ColorPickerControl** `<ColorPicker Hex>` · ContainerControl — S/V field (hue quad + `picker-white`/`picker-black`
  washes), `picker-hue` strip, filled handles, swatch, hex `TextBoxControl`; lays its parts out by hand. `hex`,
  `onPicked` (drag release, committed hex), `OnPointerPress`→`StartDrag`/`OnDrag`/`OnDragStop`; static
  `TryParseHex`, `ToHex`, `HsvToRgb`, `RgbToHsv`. Gradients in `Engine.gradients.xml`. [[text-decorations-and-colour]]
- **`Registry.Assets.ContextMenuAsset`** — resolves a menu's path only. Data
  `*/Data/XML/Documents/Menus/*.menu.xml`, registered in the host's `*.assets.xml`; the engine's `view` and
  `tab` in `EngineAssets.assets.xml`.
- Menu actions read `ContextMenus.target`: `WindowActions`, `TabActions`, `ViewActions`,
  `UIActions.Invoking`.

## XML authoring

- Documents: `*/Data/XML/Documents/UI/*.ui.xml`, elements `Next*`, root `<WindowRoot>`; registered as
  `UIDocumentAsset`s in the host's `*.assets.xml`; built by `Control.ParseXML(name)`.
- Every element inherits, from `Control`:
  - size — `Width`, `Height`, `MinWidth`, `MinHeight`, `WidthStar`, `HeightStar`, `Margin`, `Padding`
  - place — `HorizontalAlignment`, `VerticalAlignment`, `HorizontalPos`, `VerticalPos`, `DockMode`,
    `Grid.Column`, `Grid.Row`, `ClipToBounds`
  - paint — `ColorHex`, `Alpha`, `ControlColor`, `CornerRadius`, `EdgeColorHex`, `EdgeThickness`, `Gradient`,
    `Role`, `Palette`
  - events, taking action names — `onEnter`, `onExit`, `onMove`, `onPress`, `onRelease`, `onTap`, `onScroll`
  - behaviour — `ContextMenu`, `StopsContextMenu`, `Draggable`
- A control's own attributes are in its entry above. `*/Data/XML/Schemas/UITypeSchema.xsd` is generated and
  42 KB — grep it for one element, never read it.

## Host controls

- `Thorium.Editor.CustomControls.VaultBrowserControl` — new stack, `FileTreeControl`: the vault as a
  tree, renames on disk.
- `Carbon.Editor.CustomControls.FrameStripControl` `<FrameStrip>` · ContainerControl — frame bars per
  thread, peak per bar; `OnPointerPress` maps the point to a bar, `onFrameSelected`.
- `…SessionListControl` `<SessionList>` · ScrollableControl — capture session folders; `Load`,
  `onSessionLoaded`.
- `…TestRunListControl` `<TestRunList>` · ScrollableControl — test run folders under `TestRoot`; `Load`,
  `onRunLoaded`.
- `…TestResultsControl` `<TestResults>` · ScrollableControl — one run's tests and failures; `ShowRun`,
  `onOpenCapture` (a failed measured test's button); a `Fail`/`New` shot's golden/actual/diff as `ImageControl`
  panels (`ShotWidth`, V flipped with `SetUVRect(0,1,1,0)`), textures cached per path.
- `…SpanChartControl` `<SpanChart Mode>` · ContainerControl — flame chart / timeline; `OnPointerScroll`
  zooms, `OnPointerPress` + `OnDrag` pan; nested `ChartScrollThumbControl`.
- `…ZoneTableControl` `<ZoneTable>` · ScrollableControl — zone statistics per thread; `SetSession`,
  `SetBaseline` (per-frame diff); `Pools` closes each thread with its data pools. XML `DeltaWidth`,
  `SlowerColorHex`, `FasterColorHex`.

## Looking at it

- **F10** → `UITreeDump` (`UI.DumpTree`) → `uitree.xml` beside the exe: every window's tree, arranged and
  desired sizes, `Hidden`. Grep it, don't read it. Against a running host with no keypress:
  `<Host>.exe --send UI.DumpTree`.
- **ConsoleControl** (no XML) · StackPanelControl — the Ctrl+` command console: a `ScrollableControl` of
  `LabelControl` rows over a `TextBoxControl`. `Toggle` (action `Console.Toggle`) hosts it as the primary root's
  last child and focuses the input; `Submit` runs a line through `Commands.CommandConsole`; `AddRow` keeps 200.
  [[dev-console]]
- Screenshots: `aurora-verify`'s `capture.ps1`, cropped to the rect the dump gave.
