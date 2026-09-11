# UI orientation — the new stack, one entry per component

**Scope:** `UINext` = `ArctisAurora.Core.UI`. The outgoing `UI` stack (`ArctisAurora.Core.UISystem`) is not
described; an entry names its old twin, found with `grep -rn "class <Twin>\b" AuroraEngine/Core/UISystem`.

**Use:** find the component → its entry says what it does, which region or members carry it, which note
section settles it → `grep -n` the member, `sed` that region. Open a whole file only when the entry and the
region map both fail. Note links name one `§`: `grep -n '^## '` the note and read that section only.
"(old)" marks a note written for the old twin.

**Upkeep:** a component added, removed or with changed entry points updates its entry in the same change
(`aurora-docs`). Names only — no paths into code, no line numbers.

## A frame, main thread

1. `InputHandler.ActivateKeybinds` — keybind actions run here, F10 `UI.DumpTree` among them.
2. `Engine.HandleUI` → `UIEngine.Poll(window)`, per window — hover, press, release, drag, scroll → `Dispatch`.
3. `NextDragGhost.Follow`, `NextContextMenus.Tick`.
4. `Engine.Interpolate` → `UIEngine.ResolveLayout` — measure + arrange each dirty root.
5. `DataManager.FrameEdge`, then `UIEngine.BuildDrawLists` — DFS each window's root (a drag ghost's
   `rangeRoot` instead), `Control.Emit` the visible ones into the window's `DrawList`, culled to the screen.
6. Render thread: `Render.Modules.UIEngineModule` mirrors the draw list and draws —
   `*/Shaders/UIEngine/UIEngine.vert|frag`, four copies (`shader-pipeline` skill).

Why: [[ui-draw-list]] § What changed; [[ui-engine-stack]] § Vocabulary.

## Core

### Control — abstract `<NextVulkanControl>` · `ECS.EngineEntity.Entity` · partial with ControlXml
One tree node. Layout state is its `UIElements` pool row (`arrange` → `ArrangeData`); the GPU quad is plain
fields (`geometry` → `ControlGeometry`, `visual` → `VulkanControl`). A plain `Control` takes one child.

| region | holds |
|---|---|
| `authored layout` | the inherited XML sizing attrs (see XML authoring); `SetSize`, `SetWidth`, `SetHeight`, `IsWidthStar`, `IsHeightStar` |
| `paint` | colour, alpha, corner radii, edge, gradient; `kind`, `sampler`, `SetUVRect` |
| `layout state` | `arrangedRect`, `DesiredSize`, `depth`, `SetGradientSpace`; flags `isMeasureDirty`, `isArrangeDirty`, `hidden`; `InvalidateLayout`, `InvalidateArrange`, `Hide`, `Show` |
| `layout (two-pass)` | `Measure`, `Arrange`, `WriteArranged`, `ArrangeByAlignment`, `RefreshSubtreeCache`, `Emit` |
| `pointer` | `onEnter`…`onScroll` + `RegisterOnX` + virtual `OnPointerX`; `hitTestable`; `ActiveContextTarget`, `takesActiveControl`; `contextMenu`, `stopsContextMenu`; drag: `draggable`, `StartDrag`, `onDrag`, `onDragStop`, `DraggingOverStart`/`DraggingOver`/`DraggingOverEnd`, `FinishDrag`, `DraggedOutOfWindow`/`DraggedIntoWindow`, `ChildDraggedOut` |
| `tree` | `AddChild` (throws on a second child), `RemoveChild`, `FindByName`, `MarkTreeOrderDirty` |

Static, outside regions: `EnumColorToHex`, `HexToRGB`.
Why: [[ui-engine-stack]] § landing 2, § landing 3, § landing 6a, § The active context is a question, § The drag gap.

### ControlXml — Control's XML half
- `Control.ParseXML(document)` — a tree from a registered `UIDocumentAsset` by name (e.g. `next-main`); the
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
| `input` | `Poll(window)` → `SolveHover`, `SolvePress` (dismisses menus), `SolveRelease` (opens `NextContextMenus.OpenOn`), `SolveDrag`, `SolveScroll`, `SolveDragWindow`; `HitTest` (children last-to-first, optional `skip`), `Dispatch` (target up through parents until a handler returns `true`), `SetActiveControl`, `SetDragging`, `EndDrag`, `Forget`, `WindowOf` |
| `dense order` | `NextElementOrder(pool)` — the DFS order the `UIElements` pool sorts to |
| `draw lists` | `BuildDrawLists` → `Collect` → `Control.Emit`, per window |

Outside regions: `Bootstrap` (the `UIEngine.Bootstrap` step) → `BuildShell` parses `next-main` into the primary
window and loads the workspace default — landing-6b scaffolding.

### WindowRoot — `<NextWindow>` · Control
A window's root, transparent; `RenderWindow.uiNext.uiRoot`. Fits the tree to the window: `FitTo`,
`ViewportSize`, `ToDesignSpace`; fields `windowingMode` (`KeepLocal`/`WindowSize`), `autoscaling`,
`scalingAxis`. Unscaled, design space is window pixels.

### ContainerControl — `<NextContainer>` · Control
Many children, both alignments default to `Stretch`. Base of every multi-child control.

### DrawList
Per window, rebuilt each frame: parallel `ControlGeometry[]` / `VulkanControl[]`, `Next()` hands out a slot,
`Clear()`. Filled by `Control.Emit`; mirrored by the render thread. Why: [[ui-draw-list]].

### UIData — value types
- `LayoutRect` (`x`, `y`, `width`, `height`, `Right`, `Bottom`, `Overlaps`); `Thickness`, `CornerRadii`
  (+ their `TypeConverter`s); `QuadUVs`.
- Rows: `ArrangeData` (pool `UIElements`), `ControlGeometry`, `VulkanControl` — the GPU quad, typed by
  `VulkanControlType` (`MTSDFControl`, `PanelControl`, `ImageControl`).
- Enums: `HorizontalAlignment`, `VerticalAlignment`, `DockMode`, `ArrangeFlags` (`Clip`, `Hidden`,
  `MeasureDirty`, `ArrangeDirty`), `ControlColor`.

### PointerEvent
`target`, `point`, `delta`, `button` (`leftButton` 0, `rightButton` 1), `tapCount`; phases in `PointerPhase`.

## Primitives

- **NextPanelControl** `<NextPanel>` · Control — coloured box, at most one child. Old `PanelControl`.
- **NextButtonControl** `<NextButton>` · NextPanelControl — panel with hover/press colours.
  `OnPointerEnter/Exit/Press/Release`. XML `HoverColorHex`, `PressColorHex`. Old `ButtonControl`,
  [[button-states-and-hover-bubbling]] (old).
- **NextCheckBoxControl** `<NextCheckBox>` · NextButtonControl — 18×18 box, a 10×10 mark panel that is not
  hit-tested. `isChecked`, `onChanged(bool)`; a left release toggles. Old `CheckBoxControl`.
- **NextDropdownControl** `<NextDropdown>` · NextButtonControl — caption, `options`, `onPicked`, `selected`; a
  left release opens `options` as a menu under it. `options` is code-only. Old `DropdownControl`.
- **NextKeyCaptureControl** `<NextKeyCapture>` · NextButtonControl — shows a combo (`SetCombo`, static
  `Describe`); a left release hands the next key to `InputHandler.Capture`; `OnDestroy` cancels a live capture,
  or every keybind stays swallowed. Old `KeyCaptureControl`.
- **NextHintControl** (no XML) · NextPanelControl — translucent wash; the tab view's drop preview. Old `HintControl`.
- **NextIconControl** `<NextIcon>` · Control — one cell of an icon set's MTSDF atlas, by set and name
  (private `Rebind`). XML `Set`, `Icon`. Old `IconControl`.
- **NextCaretControl** (no XML) · Control — blinking insertion bar. `OnTick`, `Focus`, `Blur`. Old
  `CaretControl`, [[caret-blink-and-focus]] (old).

## Text

- **TextRunControl** abstract `<NextTextRun>` · Control — a paragraph as one control, a GPU quad per visible
  glyph. `spans` of `StyleSpan` (`count`, `style`, `colorHex`), `SetSpans`, `style`, `lineHeight`. Regions
  `layout` (`Measure`, `Arrange`, `Emit`) and `caret geometry` (`IndexAt`, `CaretAt`, `TextOrigin`, `Length`).
  `OnPointerPress` → `IGlyphPressTarget`. XML `Text`, `FontSize`, `FontName`. Why: [[ui-engine-stack]] § landing 4.
- **NextLabelControl** `<NextLabel>` · TextRunControl — read-only text. Old `LabelControl`.
- **NextTextBoxControl** `<NextTextBox>` · ContainerControl, `IContext` — single-line field with caret and
  selection. `Focus`, `SelectAll`, `WriteChar`, `Backspace`, `Delete`, `MoveCaret`, `Commit`, `Cancel`,
  `OnContextAdded`/`OnContextRemoved`; nested `NextFieldLine` carries the run. XML `Text`, `FontSize`,
  `TextColorHex`, `SelectionColorHex`, `CaretColorHex`. Old `TextBoxControl`, [[note-naming-and-text-field]] (old).
- **NextEditableLabelControl** `<NextEditableLabel>` · ContainerControl — label that swaps to a text field on
  double-click. `BeginEdit`. XML `Text`, `FontSize`, `TextColorHex`, `FieldColorHex`. Old
  `EditableLabelControl`, [[inline-rename]] (old).

## Layout containers

- **NextStackPanelControl** `<NextStackPanel>` · ContainerControl — children in a row or column; star children
  split what is left by weight. XML `Orientation`, `Spacing`. Old `StackPanelControl`,
  [[stack-panel-arrange-clamp]] (old).
- **NextDockingControl** `<NextDock>` · ContainerControl — children docked by their `DockMode`. XML
  `LastChildFill`. Old `DockingControl`.
- **NextGridListControl** `<NextGridList>` · ContainerControl — band grid; a child claims its cell with
  `Grid.Row`/`Grid.Column`. Child elements `<NextRowDefinition Height SizeMode GapAfter>`,
  `<NextColumnDefinition Width SizeMode GapAfter>`; `SizeMode` is `Fixed`/`Auto`/`Star`. Old `GridListControl`.
- **NextScrollableControl** `<NextScrollable>` (0–1 child) · ContainerControl — scrolls its child; one thumb
  per axis, appended last so hit-test reaches them first. Regions `properties`, `state`, `layout`,
  `scrolling`: `OnScrollInput`, `OnPointerScroll`, `SetScrollOffset`/`GetScrollOffset`, `ScrollIntoView`. XML
  `ScrollDirection`, `ScrollSensitivity`, `Overscroll`, `ThumbColorHex`, `ThumbHoverColorHex`,
  `ThumbPressColorHex`. Old `ScrollableControl`, [[scrollbar-thumb]], [[scroll-overscroll]] (old).
- **NextScrollThumbControl** (no XML) · NextButtonControl — the thumb; its drag becomes a scroll offset.
  `OnPointerPress`, `OnDrag`. Old `ScrollThumbControl`.
- **NextSplitViewControl** `<NextSplitView>` · NextStackPanelControl — panes with grips between; static
  `Split(tabView, SplitEdge)` and `Collapse(split, leaving)`. Old `SplitViewControl`,
  [[splitter-and-pane-sizing]] (old).
- **NextSplitterControl** `<NextSplitter>` · NextButtonControl — grip: resizes a sized pane, or trades weight
  between two star panes (`DragStars`). `OnDrag`, `OnPointerEnter/Exit/Press`. Old `SplitterControl`.

## Tabs

- **NextTabViewControl** `<NextTabView>` · ContainerControl — a strip of tab buttons over its
  `NextTabItemControl` pages. Regions `properties`, `drop`, `strip`, `layout`. `SetActive`, `CloseTab`,
  `CloseOthers`, `CloseToTheRight`, `SplitOff(item, edge)`; subclass hooks `NewOfSameKind`, `BuildCaption`;
  drop target via `DraggingOverStart/Over/End` (hint preview) and `FinishDrag`; nested `CloseButtonControl`.
  Each strip button names `tabContextMenu` and stops the menu walk. XML `TabHeight`, `TabWidth`, `TabColorHex`,
  `ActiveTabColorHex`, `TabHoverColorHex`, `TabInkColorHex`, `GripColorHex`, `GripHoverColorHex`,
  `GripPressColorHex`, `TabContextMenu`. Old `TabViewControl`, [[tab-view-control]] (old),
  [[next-context-menus]] § Menu bar, tab and view menus.
- **NextTabItemControl** `<NextTabItem>` · NextPanelControl — one page; XML `Header` is its caption. Old `TabItemControl`.
- **NextTabStripButtonControl** (no XML) · NextButtonControl — a tab in the strip; a press moved past a
  threshold becomes a drag and shows `NextDragGhost`, which `OnDragStop` hides. `OnPointerPress/Move/Release`,
  `OnDragStop`. Old `TabStripButtonControl`.
- **NextDragGhost** static — the dragged control, drawn again in a floating window centred on the pointer.
  `Show(control)`, `Hide`, `Follow`; sets `UIEngineModule.rangeRoot` and `rangeRect`; opacity from
  `Control.draggingOpacity` or the `DragGhost` UI setting. Old `DragGhost`,
  [[render-thread-reads-pool-row]].
- **NextEditableTabsControl** `<NextEditableTabs>` · NextTabViewControl — captions rename in place on
  double-click (`BuildCaption`, `NewOfSameKind`). Old `EditableTabsControl`, [[tab-rename-and-double-click]] (old).

## Window chrome and shell

- **NextWindowFrameControl** `<NextWindowFrame>` · ContainerControl — resize grips on an undecorated window's
  edges, with cursor shapes. `Measure`, `Arrange`; private `EnsureGrips`, `BeginResize`, `ApplyResize`. Old
  `WindowFrameControl`, [[window-frame-resize]] (old).
- **NextTitleBarControl** `<NextTitleBar>` · NextStackPanelControl — a left press hands the window to the OS
  caption-drag loop (`os.DragByCaption`); `Pump` keeps layout and draw lists running inside it. Old
  `TitleBarControl`, [[window-chrome-and-label]] (old).
- **NextWorkspaceControl** `<NextWorkspace>` (≤ 1 child) · NextPanelControl — holds the pane layout.
  `LoadDefault`, `LoadPane`, static `In(root)`. XML `Default`, `Pane`. Old `WorkspaceControl`,
  [[session-restore]] (old).
- **NextMenuButtonControl** `<NextMenuButton ContextMenu="…">` · NextButtonControl — menu bar entry: a left
  press drops only the menu it names under it; `takesActiveControl => false`. Caption is an authored
  `<NextLabel>` child. Old `MenuButtonControl`, [[next-context-menus]] § Menu bar, tab and view menus.

## Files

- **NextFileBrowserControl** abstract, no XML · NextScrollableControl — rows of files under `RootPath`.
  `Rebuild` → `PopulateRows` → `AddRow`; `BeginRename`. Host hooks: abstract `RootPath`, `PopulateRows`,
  `Activate(file)`; virtual `Accepts`, `DisplayName`, `Rename`. XML `RowHeight`, `Indent`, `RowSpacing`,
  `RowInset`, `GutterWidth`, `RowFontSize`, `RowColorHex`, `RowHoverColorHex`, `RowPressColorHex`,
  `FolderColorHex`, `FileColorHex`, `RowFieldColorHex`. Old `FileBrowserControl`, [[file-browser-tree]] (old).
- **NextFileTreeControl** abstract · NextFileBrowserControl — folders expand in place: `PopulateRows`,
  `Expand`. Old `FileTreeControl`.
- **NextFileRowControl** `<NextFileRow>` · NextButtonControl — one file or folder row. Old `FileRowControl`.

## Context menus

- **NextContextMenus** static — menus by name. `Get` parses the `ContextMenuAsset` on first use (`Register`
  pre-seeds); `Collect(control)` walks up the parents gathering each `contextMenu`, a line between groups,
  until a `stopsContextMenu`. `OpenOn`/`Open`/`Close`; row input `Entered`, `Clicked`; `DismissUnlessInside`;
  `Tick`. Hosted as the root's last child when it fits, its own window when not. Regions `menus`,
  `open and close`, `input`. Why: [[next-context-menus]].
- **NextContextMenuControl** (no XML) · NextStackPanelControl — one menu panel at a `depth`; a nested
  `Row` : NextButtonControl per entry (enter → `Entered` opens a submenu, release → `Clicked`). Old
  `ContextMenuControl`.
- **ContextMenuEntries** — the menu document: root `<NextContextMenu>` (`ContextMenu`); entries
  (`ContextMenuEntry`) `<NextContextButton Text Action>` (`ContextMenuButton`), `<NextContextLine>`
  (`ContextMenuLine`), `<NextContextSubmenu Text>` (`ContextMenuSubmenu`).
- **`Registry.Assets.ContextMenuAsset`** — resolves a menu's path only. Data
  `*/Data/XML/Documents/Menus/*.menu.xml`, registered in the host's `*.assets.xml`; the engine's `view` and
  `tab` in `EngineAssets.assets.xml`.
- Menu actions read `NextContextMenus.target`: `WindowActions`, `TabActions`, `ViewActions`,
  `UIActions.Invoking` (old `Core.UISystem.Actions`, new stack first).

## XML authoring

- Documents: `*/Data/XML/Documents/UI/*.ui.xml`, elements `Next*`, root `<NextWindow>`; registered as
  `UIDocumentAsset`s in the host's `*.assets.xml`; built by `Control.ParseXML(name)`.
- Every element inherits, from `Control`:
  - size — `Width`, `Height`, `MinWidth`, `MinHeight`, `WidthStar`, `HeightStar`, `Margin`, `Padding`
  - place — `HorizontalAlignment`, `VerticalAlignment`, `HorizontalPos`, `VerticalPos`, `DockMode`,
    `Grid.Column`, `Grid.Row`, `ClipToBounds`
  - paint — `ColorHex`, `Alpha`, `ControlColor`, `CornerRadius`, `EdgeColorHex`, `EdgeThickness`, `Gradient`
  - events, taking action names — `onEnter`, `onExit`, `onMove`, `onPress`, `onRelease`, `onTap`, `onScroll`
  - behaviour — `ContextMenu`, `StopsContextMenu`, `Draggable`
- A control's own attributes are in its entry above. `*/Data/XML/Schemas/UITypeSchema.xsd` is generated and
  42 KB — grep it for one element, never read it.

## Host controls

- `Thorium.Editor.CustomControls.NextVaultBrowserControl` — new stack, `NextFileTreeControl`: the vault as a
  tree, renames on disk.
- `…VaultBrowserControl` — old stack, `FileTreeControl`: note tree; open, create, rename, delete.
- `Carbon.Editor.CustomControls.FrameStripControl` — old stack: frame bars per thread, peak per bar.
- `…SessionListControl` — old stack, `ScrollableControl`: capture session folders.
- `…SpanChartControl` — old stack: flame chart / timeline with zoom and pan; nested `ChartScrollThumbControl`.
- `…ZoneTableControl` — old stack, `ScrollableControl`: zone statistics per thread.

## Looking at it

- **F10** → `UITreeDump` (`UI.DumpTree`) → `uitree.xml` beside the exe: every window's tree, arranged and
  desired sizes, `Hidden`. Grep it, don't read it.
- Screenshots: `aurora-verify`'s `capture.ps1`, cropped to the rect the dump gave.
