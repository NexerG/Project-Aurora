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

1. `InputHandler.ActivateKeybinds` — keybind actions run here, F10 `UI.DumpTree` among them.
2. `Engine.HandleUI` → `UIEngine.Poll(window)`, per window — hover, press, release, drag, scroll → `Dispatch`.
3. `DragGhost.Follow`, `ContextMenus.Tick`.
4. `Engine.Interpolate` → `UIEngine.ResolveLayout` — measure + arrange each dirty root.
5. `DataManager.FrameEdge`, then `UIEngine.BuildDrawLists` — rewind `UIQuads`, DFS each window's root (a drag
   ghost's `rangeRoot` instead), `Control.Emit(z)` the visible ones into the pool, publish the window's range.
6. Render thread: `Render.Modules.UIEngineModule` mirrors its `UIQuads` range and draws —
   `*/Shaders/UIEngine/UIEngine.vert|frag`, four copies (`shader-pipeline` skill).

Why: [[ui-draw-list]] § What changed, [[ui-quads-pool]]; [[ui-engine-stack]] § Vocabulary.

## Core

### Control — abstract `<Control>` · `ECS.EngineEntity.Entity` · partial with ControlXml
One tree node. Layout state is its `UIElements` pool row (`arrange` → `ArrangeData`); paint is a plain field
(`visual` → `VulkanControl`); `ControlGeometry` is built in `Emit`. A plain `Control` takes one child.

| region | holds |
|---|---|
| `authored layout` | the inherited XML sizing attrs (see XML authoring); `SetSize`, `SetWidth`, `SetHeight`, `IsWidthStar`, `IsHeightStar` |
| `paint` | colour, alpha, corner radii, edge, gradient (`gradientId`, word rebuilt in `InheritPaint`); `kind`, `sampler`, `SetUVRect`; palette — `role`, `paletteName`, `ownPalette`, `palette`, `groundBelow`, `colorAuthored`; `PaintOr`, `CopyPaint`; virtual `SetPaint`, `ApplyRole`; `RolePaint`, `InheritPaint`, `RepaintChildren`; shape — `edgeRole`, C#-only `cornerRole`/`accentRole`, `ApplyShape` (from the setters and `InheritPaint`); animation — `effect`/`RestartEffect`, `clip` (played in `OnStart`), `hoverClip`, `pressClip`, `stateBinding`, `RunClip`, `StopClip`, `Interacted` (hooks in the base `OnPointerEnter/Exit/Press/Release`, cleanup in `OnDestroy`) |
| `layout state` | `arrangedRect`, `DesiredSize`, `ClipRect`; flags `isMeasureDirty`, `isArrangeDirty`, `hidden`; `InvalidateLayout`, `InvalidateArrange`, `Hide`, `Show` |
| `layout (two-pass)` | `Measure`, `Arrange`, `WriteArranged` (clip and palette inheritance), `ArrangeByAlignment`, `RefreshSubtreeCache`, `Emit` |
| `pointer` | `onEnter`…`onScroll` + `RegisterOnX` + virtual `OnPointerX`; `hitTestable`; `ActiveContextTarget`, `takesActiveControl`; `contextMenu`, `stopsContextMenu`; drag: `draggable`, `StartDrag`, `onDrag`, `onDragStop`, `DraggingOverStart`/`DraggingOver`/`DraggingOverEnd`, `FinishDrag`, `DraggedOutOfWindow`/`DraggedIntoWindow`, `ChildDraggedOut` |
| `tree` | `AddChild` (throws on a second child), `RemoveChild`, `FindByName`, `MarkTreeOrderDirty` |

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
`ViewportSize`, `ToDesignSpace`; fields `windowingMode` (`KeepLocal`/`WindowSize`), `autoscaling`,
`scalingAxis`. Unscaled, design space is window pixels.

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
`Contrast`; `RoleOffsets` for gradient role stops. Types beside it: `PaletteDefinition` `<Palette>`, `PaletteRole`. The GPU copy is
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
- **ButtonControl** `<Button>` · PanelControl — panel with hover/press colours.
  `OnPointerEnter/Exit/Press/Release`. XML `HoverColorHex`, `PressColorHex`. Old `ButtonControl`,
  [[button-states-and-hover-bubbling]] (old).
- **CheckBoxControl** `<CheckBox>` · ButtonControl — 18×18 box, a 10×10 mark panel that is not
  hit-tested. `isChecked`, `onChanged(bool)`; a left release toggles. Old `CheckBoxControl`.
- **SliderControl** `<Slider>` · ContainerControl — `value` 0–1, a track and a thumb panel, neither
  hit-tested. Press jumps, drag follows (`Pick`); `onChanged(float)` fires on the gesture only, not on a
  `value` set. XML `Value`, `TrackHeight`, `ThumbWidth`, `TrackColorHex`, `ThumbColorHex`. No old counterpart.
- **DropdownControl** `<Dropdown>` · ButtonControl — caption, `options`, `onPicked`, `selected`; a
  left release opens `options` as a menu under it, no narrower than itself, captions centered. `options` is code-only. Old `DropdownControl`.
- **KeyCaptureControl** `<KeyCapture>` · ButtonControl — shows a combo (`SetCombo`, static
  `Describe`); a left release hands the next key to `InputHandler.Capture`; `OnDestroy` cancels a live capture,
  or every keybind stays swallowed. Old `KeyCaptureControl`.
- **HintControl** (no XML) · PanelControl — translucent wash; the tab view's drop preview. Old `HintControl`.
- **IconControl** `<Icon>` · Control — one cell of an icon set's MTSDF atlas, by set and name
  (private `Rebind`). XML `Set`, `Icon`. Old `IconControl`.
- **CaretControl** (no XML) · Control — blinking insertion bar. `OnTick`, `Focus`, `Blur`. Old
  `CaretControl`, [[caret-blink-and-focus]] (old).

## Text

- **TextRunControl** abstract `<TextRun>` · Control — a paragraph as one control, a GPU quad per visible
  glyph. `spans` of `StyleSpan` (`count`, `style`, `colorHex`, `fontName`, `fontSize`, `gradient`,
  `strikethrough`, `stylingType`, `fontSizeAuthored`, `IsBold`/`IsItalic`), `SetSpans`, `style`, `lineHeight`.
  Regions `layout` (`Measure`, `Arrange`, `Emit`) and `caret geometry` (`IndexAt`, `CaretAt`, `TextOrigin`,
  `Length`, `Lines`). `OnPointerPress` → `IGlyphPressTarget`. XML `Text`, `FontSize`, `FontName`. **`Measure`
  returns the last `desired` while the run is clean and its wrap width unchanged** — anything `BuildRuns` reads
  must invalidate layout, which is why `colorHex` does. Why: [[ui-engine-stack]] § landing 4, § landing 6c.
- **LabelControl** `<Label>` · TextRunControl — read-only text, one line: overrides `Wraps` false, so
  overflow runs past the box unless `ClipToBounds`. Old `LabelControl`.
- **TextBoxControl** `<TextBox>` · ContainerControl, `IContext` — single-line field with caret and
  selection. `Focus`, `SelectAll`, `WriteChar`, `Backspace`, `Delete`, `MoveCaret`, `Commit`, `Cancel`,
  `OnContextAdded`/`OnContextRemoved`; nested `FieldLine` carries the run. XML `Text`, `FontSize`,
  `TextColorHex`, `SelectionColorHex`, `CaretColorHex`. Old `TextBoxControl`, [[note-naming-and-text-field]] (old).
- **EditableLabelControl** `<EditableLabel>` · ContainerControl — label that swaps to a text field on
  double-click. `BeginEdit`. XML `Text`, `FontSize`, `TextColorHex`, `FieldColorHex`. Old
  `EditableLabelControl`, [[inline-rename]] (old).

## Documents

- **BlockControl** (no XML) · TextRunControl — one block of a note: the paragraph's string with its runs as
  spans. `stylingType`, `listKind`/`listLevel`/`isChecked`, `ApplyLayout(DocumentLayout)` (list indent as
  `padding.left`, marker sync), `Measure` (wraps inside the indent), `Arrange` (places the dot or
  `CheckBoxControl` marker child), `AppendRun`, `Runs()`. Region `text and spans`:
  `InsertText`, `RemoveText`, `SplitAt`, `AppendBlock`, `Snapshot`/`SliceSnapshot`/`Restore`/`From`,
  `InsertSlice`/`AppendSlice`, `StyleAt`, `StyleRange`, `SplitSpanAt`, `MergeSpans`. A boundary belongs to the
  span **after** it. Replaces `Block`/`ContentBlock` + `TextRun`.
- **Run** `<Run>` — a run as the file writes it; exists at load and save only. `Text`, `Bold`, `Italic`,
  `Strikethrough`, `ColorHex`, `ControlColor`, `Gradient`, `FontName`, `FontSize`, `FontSizeAuthored`,
  `StylingType`.
- **DocumentControl** (no XML) · ContainerControl, `IGlyphPressTarget` — the content area. Regions `caret`
  (`SetCaret`, `CollapseSelection`, `GlyphPressed`, `OnPointerTap`), `caret navigation` (`CaretPoint`,
  `CaretAtPoint`, `CaretOffText`, `AdjacentBlock`), `selection` (`SelectWord`, `SelectAll`,
  `OrderedSelection`, highlights inserted at the **head** of `children` so they paint behind the text),
  `editing` (`DeleteSelection`, `SplitBlock`, `TypeChar`, `Blocks`), `lists` (`TypeListPrefix`,
  `ClearListAtCaret`, `ShiftListLevel`, `SetBlockList`), `styling` (`StyleSource`,
  `CaretBlockStyling`, `ApplyStyle`, `ArmStyle`, `ApplyStyleTo`/`ApplyStyleBetween`, `SetBlockStyling`,
  `SnapshotBlocks`, `RestoreBlocks`), `addressing` (`AddressOf`, `Resolve`, `CaretTo`) and `undo primitives`
  (`InsertText`, `RemoveText`, `DeleteBetween`, `InsertFragment`, `JoinBlockWithNext`). Also declares
  `CaretSlot`, `StyleDelta` and `CaretStyle`. Old `DocumentControl`.
- **DocumentEditorControl** `<DocumentEditor>` · ScrollableControl, `IContext` — one open note.
  `Source`/`LoadPath`/`LoadDocument`, `Save`, `needsNaming`, `FocusCaret`; regions `styling` (forwards under a
  `BeginStep`; also `SetChecked`, `ShiftListLevel`), `selection` (`SelectLine`, `BeginSelectionDrag`, `OnDrag` + autoscroll), `caret movement`
  (`MoveCaret`), `editing` (`Backspace`, `Delete`, `SplitBlock`, `TypeChar`), `history`
  (`BeginStep`/`Undo`/`Redo`/`MarkDirty`), `focus`. `Arrange` scrolls to the caret and **must never exit with
  the arrange flag set**. XML adds `CaretColorHex`, `SelectionColorHex` to the scrollable's. Old
  `DocumentEditorControl`.
- **DocumentToolbarControl** `<DocumentToolbar>` · StackPanelControl — the format bar for whichever
  note holds the caret; resolves it per press through `TextInputActions.Editor()` and takes no active
  control. `OnTick` reflects bold/italic/styling/colour/size; nested `ToolButton` (acts on press) and
  `PxBox` (the one part that does take the focus; captures the range on its press). XML `HoverColorHex`,
  `PressColorHex`, `IdleInkColorHex`, `ActiveInkColorHex`, `SeparatorColorHex`, `FieldColorHex`. Old
  `DocumentToolbarControl`, [[document-format-bar]], [[armed-style-at-the-caret]] (old).
- **RichTextDocument** `<Document>` — the model: `blocks`, `name`, `layout`; `extensions`, `Load`, `Save`
  (switch on the extension). **DocumentEditSession** — the open file: `path`, `undo`, `isDirty`, `MarkDirty`,
  `Repath`, `Save`.
- **DocumentXml** — `Load`/`Parse(XElement)` build blocks from a `<Document>` tree, `ToXml`/`Save` write one;
  the block level is written by hand. Since 6d no XSD type declares `"Document"`/`"Block"`/`"Run"`, so a
  note's `schemaLocation` validates nothing. [../Patterns/document-xml-persistence.md](../Patterns/document-xml-persistence.md)
- **NoteFormats.cs** — `MarkdownFormat` and `PlainTextFormat`: `Read(text, name) → XElement`,
  `Write(XElement) → string`. Regions `read`, `write`. [[note-file-formats]]
- **DocumentEdits.cs** — `DocumentAddress` (`(block, offset)`), `BlockSnapshot`,
  `DocumentFragment`, and the records `TextEdit`, `SplitEdit`, `DeleteRangeEdit`,
  `StyleRangeEdit`, `BlockStateEdit`. Undo currency is snapshots and fragments, never control references — undo rebuilds
  blocks. Why: [[ui-engine-stack]] § landing 6c.

## Layout containers

- **StackPanelControl** `<StackPanel>` · ContainerControl — children in a row or column; star children
  split what is left by weight; a `hidden` child gets no slot and no spacing. XML `Orientation`, `Spacing`. Old `StackPanelControl`,
  [[stack-panel-arrange-clamp]] (old).
- **DockingControl** `<Dock>` · ContainerControl — children docked by their `DockMode`. XML
  `LastChildFill`. Old `DockingControl`.
- **GridListControl** `<GridList>` · ContainerControl — band grid; a child claims its cell with
  `Grid.Row`/`Grid.Column`. Child elements `<RowDefinition Height SizeMode GapAfter>`,
  `<ColumnDefinition Width SizeMode GapAfter>`; `SizeMode` is `Fixed`/`Auto`/`Star`. Old `GridListControl`.
- **ScrollableControl** `<Scrollable>` (0–1 child) · ContainerControl — scrolls its child; one thumb
  per axis, appended last so hit-test reaches them first; no gutter — thumbs overlay the content's
  edge. Regions `properties`, `state`, `layout`,
  `scrolling`: `OnScrollInput`, `OnPointerScroll`, `SetScrollOffset`/`GetScrollOffset`, `ScrollIntoView`. XML
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
  `OnDragStop`. Old `TabStripButtonControl`.
- **DragGhost** static — the dragged control, drawn again in a floating window centred on the pointer.
  `Show(control)`, `Hide`, `Follow`; sets `UIEngineModule.rangeRoot` and `rangeRect`; opacity from
  `Control.draggingOpacity` or the `DragGhost` UI setting. Old `DragGhost`,
  [[render-thread-reads-pool-row]].
- **EditableTabsControl** `<EditableTabs>` · TabViewControl — captions rename in place on
  double-click (`BuildCaption`, `NewOfSameKind`). Old `EditableTabsControl`, [[tab-rename-and-double-click]] (old).

## Window chrome and shell

- **WindowFrameControl** `<WindowFrame>` · ContainerControl — resize grips on an undecorated window's
  edges, with cursor shapes. `Measure`, `Arrange`; private `EnsureGrips`, `BeginResize`, `ApplyResize`. Old
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
  until a `stopsContextMenu`. `OpenOn`/`Open`/`Close`; row input `Entered`, `Clicked`; `DismissUnlessInside`;
  `Tick`. Hosted as the root's last child when it fits, its own window when not. Regions `menus`,
  `open and close`, `input`. Why: [[context-menus]].
- **ContextMenuControl** (no XML) · StackPanelControl — one menu panel at a `depth`; a nested
  `Row` : ButtonControl per entry (enter → `Entered` opens a submenu, release → `Clicked`; binding
  `menu-row`). `reveal` (0–1, clip `menu-open`) slides it down: `Arrange` shifts it up and `ClipSubtree`s
  it at its anchor. Old `ContextMenuControl`.
- **ContextMenuEntries** — the menu document: root `<ContextMenu>` (`ContextMenu`); entries
  (`ContextMenuEntry`) `<ContextButton Text Action>` (`ContextMenuButton`), `<ContextLine>`
  (`ContextMenuLine`), `<ContextSubmenu Text>` (`ContextMenuSubmenu`).
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
- `…SpanChartControl` `<SpanChart Mode>` · ContainerControl — flame chart / timeline; `OnPointerScroll`
  zooms, `OnPointerPress` + `OnDrag` pan; nested `ChartScrollThumbControl`.
- `…ZoneTableControl` `<ZoneTable>` · ScrollableControl — zone statistics per thread; `SetSession`,
  `SetBaseline` (per-frame diff); `Pools` closes each thread with its data pools. XML `DeltaWidth`,
  `SlowerColorHex`, `FasterColorHex`.

## Looking at it

- **F10** → `UITreeDump` (`UI.DumpTree`) → `uitree.xml` beside the exe: every window's tree, arranged and
  desired sizes, `Hidden`. Grep it, don't read it.
- Screenshots: `aurora-verify`'s `capture.ps1`, cropped to the rect the dump gave.
