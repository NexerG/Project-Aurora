# Where things live — concept → code

`NAMESPACES.md` answers "where is namespace X". This file answers the question that actually starts a
task: **"where is the thing the user just named"**. Read it first, before grepping. If the concept is
in the table, the grep is a symbol lookup instead of a search.

Code is `namespace` + class, resolved through `NAMESPACES.md` (paths rot, see
[../Patterns/finding-code.md](../Patterns/finding-code.md)). Data XML has no such index, so those are
paths — relative to the host project, where `*/` means each of `AuroraEngine/`, `Thorium/`, `AuroraEditor/`.

Namespace shorthands used in the tables:

| Short | Full |
|---|---|
| `Core` | `ArctisAurora.EngineWork` — `Engine`, `Bootstrapper`, `Shutdown`, `InputHandler` |
| `Data` | `ArctisAurora.Core.Data` (+ `.Commands`) |
| `Diag` | `ArctisAurora.Core.Diagnostics` (+ `.Sinks`) |
| `Editing` | `ArctisAurora.Core.Editing` |
| `Filing` | `ArctisAurora.Core.Filing.Serialization` |
| `Registry` | `ArctisAurora.Core.Registry`; `Assets` = `...Registry.Assets` |
| `Render` | `ArctisAurora.EngineWork.Rendering` (+ `.Modules`, `.Helpers`, `.MeshSubComponents`) |
| `Threading` | `ArctisAurora.Core.Threading` |
| `UI` | `ArctisAurora.Core.UISystem`; `.Controls`, `.Containers`, `.Interactable`, `.Text`, `.Doc` (`Text.Document`), `.Edits` (`Text.Document.Edits`), `.Editing` (`Text.Editing`), `.Actions` — **the outgoing stack** |
| `UINext` | `ArctisAurora.Core.UI` — the replacement stack, built beside the old one. `Control` is CPU, `VulkanControl` is one GPU quad |

## Look, colour and chrome

| Concept | Code | Data | Note |
|---|---|---|---|
| the theme, any control colour | `UI.Controls.VulkanControl` — `controlColorHex`, `controlColor`, `edgeColorHex`, `outlineColorHex`, `EnumColorToHex` | `*/Data/XML/Documents/UI/*.ui.xml` attrs `ColorHex`, `ControlColor`, `EdgeColorHex`, `OutlineColorHex` | [[control-edge-and-outline]] |
| gradients | `UI.Gradients`; `VulkanControl.Gradient` | `Thorium/Data/XML/Documents/Gradients.gradients.xml` | [[ui-gradients]] |
| corner rounding, edge + outline strokes | `UI.Controls.VulkanControl` (`ControlData`); `UI.frag` | — | [[control-edge-and-outline]] |
| clipping a control to its parent | `UI.Controls.VulkanControl`, `WindowControl`; `UI.vert` + `UI.frag` | — | [[ui-clipping]] |
| title bar, minimise/maximise/close | `UI.Controls.TitleBarControl`, `WindowFrameControl`; `UI.Actions.WindowActions`; `UI.Text.LabelControl` | host `UI.ui.xml` | [[window-chrome-and-label]], [[window-frame-resize]] |
| icons | `UI.IconSet`; `UI.Controls.IconControl`; `Assets.IconSetAsset`; `Filing.SvgPath` | `*/Data/Icons/*/*.import.xml` | — |
| fonts, glyph atlas | `UI.AuroraFont`, `UI.Glyph`; `Assets.FontAsset`; `ArctisAurora.Core.Generators.MTSDFGen` | `*/Data/Fonts/*/*.import.xml`; `AuroraEngine/Data/XML/Imports/EngineFonts.imports.xml` | [[atlas-is-unorm-not-srgb]] |
| which face a run draws in — regular, bold, italic, bold-italic | `UI.FontStyle`, `UI.AtlasMetaData` (`Effective`, `StyleBlock`, `CellIndex`); `Filing.AssetImporter` face probing | `AuroraEngine/Data/XML/Imports/EngineFonts.imports.xml` attrs `Bold`, `Italic`, `BoldItalic` | [[bold-italic-face]] |
| button hover / press appearance | `UI.Interactable.ButtonControl`; `UI.UICollisionHandling` | — | [[button-states-and-hover-bubbling]] |

## Layout and hit-testing

| Concept | Code | Data | Note |
|---|---|---|---|
| measure / arrange pass | `UI.UILayout`; `UI.Controls.VulkanControl`; `UI.Containers.StackPanelControl` | — | [[stack-panel-arrange-clamp]] |
| mouse hit-test, hover, drag lifecycle | `UI.UICollisionHandling`; `VulkanControl.StartDrag`; `UI.DragGhost` | — | [[n-click-dispatch]], [[document-selection]] |
| double / triple click | `UI.UICollisionHandling`; `Core.InputHandler.tapWindow`; `Core.InputSettings.DoubleClickSetting` | `*/Data/XML/Settings/` | [[n-click-dispatch]], [[tab-rename-and-double-click]] |
| padding, margin, border widths | `VulkanControl.Thickness`, `VulkanControl.ThicknessConverter` | — | [[thickness-type-converter]] |
| parenting, child order, control names | `ArctisAurora.Core.ECS.EngineEntity.Entity`; `UI.Controls.VulkanControl` | — | [[entity-reparenting-and-names]] |
| window scaling mode, ortho box | `UI.Controls.WindowControl`; `Render.AuroraCamera` | host `UI.ui.xml` | [[window-scaling-modes]] |

## Containers and navigation

| Concept | Code | Data | Note |
|---|---|---|---|
| tabs, tearing off, tab menus | `UI.Containers.TabViewControl`, `TabItemControl`, `EditableTabsControl`, `TabStripButtonControl` | `TabWindow.ui.xml`, `TabPane.ui.xml` | [[tab-view-control]], [[tab-rename-and-double-click]] |
| splits, splitters, docking | `UI.Containers.SplitViewControl`, `DockingControl`; `UI.Interactable.SplitterControl` | host `UI.ui.xml` | [[splitter-and-pane-sizing]] |
| scrolling, thumb, overscroll | `UI.Containers.ScrollableControl`; `UI.Interactable.ScrollThumbControl` | — | [[scrollbar-thumb]], [[scroll-overscroll]] |
| file tree, note browser rows | `UI.Containers.FileBrowserControl`, `FileTreeControl`, `FileRowControl`; `ArctisAurora.Core.Filing.FileObject` | — | [[file-browser-tree]] |
| context menus | `UI.ContextMenus`; `UI.Controls.ContextMenuControl`, `ContextMenuItemControl`, `WindowedContextMenuControl` | `*/Data/XML/Documents/ContextMenus.menus.xml` | [[context-menu-hosting]], [[context-menu-invoker]] |
| session and layout restore | `UI.SessionLayout`; `UI.Containers.WorkspaceControl` | `Workspace.ui.xml`; `Shutdown.shutdown.xml` | [[session-restore]] |
| rename a row or tab in place | `UI.Text.EditableLabelControl`; `UI.Editing.TextBoxControl` | — | [[inline-rename]] |
| dropdowns, checkboxes, menu buttons | `UI.Interactable.DropdownControl`, `CheckBoxControl`, `MenuButtonControl`, `KeyCaptureControl` | — | — |

## Text and documents

| Concept | Code | Data | Note |
|---|---|---|---|
| document model — blocks and runs | `UI.Doc.RichTextDocument`, `DocumentControl`; `Block`/`ContentBlock` (in `Blocks.cs`); `TextRun` (in `Inlines.cs`) | `*/Data/Notes/*.xml` | [[text-styling-types]], [[document-structural-editing]] |
| line breaking, text measurement | `UI.Doc.TextMeasurer` | — | [[text-layout-one-measurer]] |
| caret — blink, position, follow | `UI.Doc.CaretControl`, `DocumentEditorControl` | — | [[caret-blink-and-focus]], [[document-caret-scrolling]] |
| selection | `UI.Doc.SelectionControl`, `CaretSlot` | — | [[document-selection]] |
| bold / italic / headings, format bar | `UI.Doc.DocumentToolbarControl`; `UI.Edits.StyleRangeEdit` | `AuroraEngine/Data/XML/Settings/DocumentSettings.settings.xml` | [[document-format-bar]], [[armed-style-at-the-caret]], [[text-styling-types]], [[bold-italic-face]] |
| undo / redo | `Editing.UndoStack`, `EditStep`, `IEditRecord`; `UI.Edits.*` | — | [[document-undo]] |
| single-line text fields | `UI.Editing.TextInputControl`, `TextBoxControl`; `UI.Text.TextControl` | — | [[note-naming-and-text-field]] |
| text and the caret on the **new** stack — a paragraph as one control, a GPU quad per visible glyph | `UINext.TextRunControl` (`spans`, `Emit`, `IndexAt`, `CaretAt`, `TextOrigin`), `StyleSpan`, `IGlyphPressTarget`, `NextCaretControl`; `Shaders/UIEngine/UIEngine.frag` MTSDF branch | — | [[ui-engine-stack]], [[ui-draw-list]] |
| what the new stack draws this frame, and what it culls | `UINext.DrawList`, `Control.Emit`, `UIEngine.BuildDrawLists`, `LayoutRect.Overlaps`; `Render.Modules.UIEngineModule` (`drawList`, `MirrorDrawList`) | — | [[ui-draw-list]] |
| per-character bold / colour / size on a run | `UINext.StyleSpan`; `UI.Doc.TextMeasurer.Run` (`charStart`/`charCount`) | none yet — the new stack parses no XML | [[ui-engine-stack]] |
| note load / save | `UI.Doc.DocumentXml`; `Filing.Serializer`, `XmlReflection` | `*/Data/Notes/*.xml` | [../Patterns/document-xml-persistence.md](../Patterns/document-xml-persistence.md), [[xml-save-skips-defaults]] |

## Input

| Concept | Code | Data | Note |
|---|---|---|---|
| keybinds, gestures, key repeat | `Core.InputHandler`, `InputSettings`; `InputBindings`, `KeybindOverride` (in `InputBindingSettings.cs`) | `*/Data/XML/Documents/Inputs/InputMap.inputs.xml` | [[engine-side-text-input]] |
| modifier roles (Ctrl/Shift by name) | `Core` — `InputModifier`, `NamedModifier`, `GestureMatcher` | `InputMap.inputs.xml` | [[named-input-modifiers]] |
| what a keystroke does in the editor | `UI.Text.TextInputActions` | `InputMap.inputs.xml` | [[engine-side-text-input]] |
| contexts — when a binding is live | `Registry.Context`, `ContextDefinition` | `Thorium/Data/XML/Documents/Contexts/Thorium.contexts.xml` | [[declared-contexts]] |

## Data, assets and settings

| Concept | Code | Data | Note |
|---|---|---|---|
| asset registry and lookup | `ArctisAurora.EngineWork.Registry.AssetRegistries`; `Assets.*` | `AuroraEngine/Data/XML/Documents/Registry.registry.xml`; `*/Data/XML/Assets/*.assets.xml` | [[asset-manifest-and-import]] |
| importing source assets | `Filing.AssetImporter`, `MeshImporter`; `ImportSet`, `FontImport`, `IconImport` (in `ImportManifest.cs`) | `*.import.xml`, `*.imports.xml` | [[asset-manifest-and-import]], [[asset-pipeline-bake]] |
| UI documents as assets | `Assets.UIDocumentAsset`; `VulkanControl.ParseXML` | `*/Data/XML/Documents/UI/*.ui.xml` | [[ui-document-registry]] |
| settings and their cascade | `Registry.SettingsRegistry`, `Setting`, `SettingScope`, `SettingCategory`, `UserSettingsFile` | `*/Data/XML/Settings/*.settings.xml` | [[settings-registry]], [[settings-categories]] |
| paths, virtual file system | `Filing.Paths`, `VirtualFileSystem` | — | [[asset-manifest-and-import]] |
| XSD schema generation | `Registry.XSDGenerator`; `AnyXMLType` | `*/Data/XML/Schemas/` | [[xsd-generator-cross-category]] |
| data pools (the ECS rework) | `Data.DataManager`, `DataPool`, `PoolColumn`, `TransformData`; `Data.Commands.*` | `AuroraEngine/Data/XML/Documents/Pools.pools.xml` | [[ecs-rework-data-pools]], [[cross-system-change-notification]] |

## Engine core and lifecycle

| Concept | Code | Data | Note |
|---|---|---|---|
| startup order | `Core.Bootstrapper` | `AuroraEngine/Data/XML/Documents/Bootstrap.bootstrap.xml` | — |
| quitting, save prompts | `Core.Shutdown`; `UI.Actions.NoteActions`, `WindowActions` | `AuroraEngine/Data/XML/Documents/Shutdown.shutdown.xml` | [[shutdown-sequence]] |
| tick order, threads | `Core.Engine`; `Threading.MainSystem`, `RenderSystem`, `PhysicsSystem`, `ThreadedSystem` | — | [[ecs-rework-data-pools]] |
| entity create / destroy | `Registry.EntityRegistry`; `Core.Engine.Interpolate` | `EntityRegistry.entities.xml` | [[entity-lifecycle-queues]] |
| which columns an entity has; an entity's position/scale | `ECS.EngineEntity.Entity` (`PoolName`, `AllocatePooledData`, `AllocateIn`, `FreePooledData`), `TransformEntity` | `AuroraEngine/Data/XML/Documents/Pools.pools.xml` | [[entity-transform-split]] |
| logging | `Diag.LogChannel`, `LogLevel`, `LoggingSettings`; `Diag.Sinks.*` | `Bootstrap.bootstrap.xml`, `Shutdown.shutdown.xml` | [[engine-logging]] |
| profiling — timing a tick phase, counting calls | `Diag.Profiling` (`Zone.Start`/`End`/`Increment`, `Report`); zones in `Core.Engine.MainTick`, `Threading.RenderSystem` | — | [[engine-profiling]] |
| frame capture — every span of every frame, to a file | `Diag.Profiling.Frame`, `Diag.FrameSpool`, `ProfilingSettings`; frame edges in `Threading.ThreadedSystem.Loop` | `Bootstrap.bootstrap.xml`, `Shutdown.shutdown.xml` | [[engine-profiling]] |
| profiling a launch — `--profile[=N]`, or `Mode="Boot"` | `Diag.Profiling.ArmBoot` (called from `Core.Engine.Init`), `Diag.CaptureMode`; phase frame + step zones in `Core.Bootstrapper.RunPhase` | `*/Data/XML/Settings/` (`<Profiling><ProfilingCapture Mode="Boot"/>`) | [[engine-profiling]] §13 |
| reading a frame file back | `Diag.FrameCaptureReader`; `CaptureSession`, `CapturedThread`, `CapturedFrame`, `CapturedSpan` | `Profiling/<session>/<thread>.frames.xml` | [[carbon-frame-viewer]] |

## Rendering

| Concept | Code | Data | Note |
|---|---|---|---|
| device, swapchain, frame loop | `Render.Renderer`, `Swapchain`, `RenderWindow`, `VulkanRenderer` | — | [[render-window-owns-the-swapchain]], [[swapchain-extent-is-the-truth]], [[dynamic-rendering]] |
| OS windows, focus, placement | `Render.AGlfwWindow` | — | [[active-glfw-window-context]] |
| the UI draw path | `Render.Modules.UIModule`; `Render.MeshSubComponents.MCUI`; `Render.UI.UIRenderer` | — | [[glyphs-as-pool-data]], [[gpu-global-frame-data]] |
| the **new** UI draw path | `Render.Modules.UIEngineModule` (`window.uiNext`, `uiRoot`, `firstInstance`, `WriteTextureTable`); `UINext.UIEngine`, `Control` (`rows`), `WindowRoot` | `AuroraEngine/Data/XML/Documents/Pools.pools.xml` (`UIElements`, `VulkanControls`) | [[ui-engine-stack]] |
| the **new** measure/arrange pass, dirty roots, per-window ranges | `UINext.UIEngine` (`ResolveLayout`, `RefreshWindowRanges`, `NextElementOrder`, `NextControlOrder`); `UINext.Control` (`Measure`, `Arrange`, `InvalidateLayout`) | `Pools.pools.xml` (`SortAction`) | [[ui-engine-stack]] |
| the **new** window root, design-space fitting | `UINext.WindowRoot` (`FitTo`, `ViewportSize`, `ToDesignSpace`) | — | [[ui-engine-stack]] |
| the **new** hit-test, hover, press, bubbling | `UINext.UIEngine` (`Poll`, `HitTest`, `Dispatch`, `Forget`); `UINext.PointerEvent`, `PointerPhase`; `UINext.Control` (`OnPointerX`, `RegisterOnX`, `hitTestable`, `canBeActiveContext`, `takesActiveControl`) | — | [[ui-engine-stack]] |
| the **new** wheel | `UINext.UIEngine` (`SolveScroll`, `ActiveTarget`); `UINext.Control` (`OnPointerScroll`, `RegisterOnScroll`) | — | [[ui-engine-stack]] |
| dragging on the **new** stack | `UINext.Control` — `draggable` (gates the press), `StartDrag`, `DraggingOverStart`/`DraggingOver`/`DraggingOverEnd`, `FinishDrag`, `ChildDraggedOut`; `UINext.UIEngine` — `SetDragging`, `dragging` (`NextDragging` context), `CheckDrag`, `EndDrag`, `HitTest`'s `skip` | `*.ui.xml` attr `Draggable` | [[ui-engine-stack]] § the drag gap |
| delivering a drag to its **claimant** on the new stack | `UINext.Control` (`onDrag`, `onDragStop`, `OnDrag`, `OnDragStop`, `RegisterOnDrag`); `UINext.UIEngine` (`SolveDrag` and its stale-release guard, `WindowOf`, `Poll`'s `ownsDrag`). Consumers: `UINext.NextWindowFrameControl`, `NextSplitterControl`, `NextScrollThumbControl` | — | [[ui-engine-stack]] |
| the drag preview and context menus on the new stack | **nothing yet** — they return at 6b with the controls that use them; the old stack's are `UI.ContextMenus`, `UI.DragGhost` | — | [[ui-engine-stack]] |
| splits and grips on the **new** stack | `UINext.NextSplitViewControl` (a transparent `NextStackPanelControl`); `UINext.NextSplitterControl` (`PreviousPane`/`NextPane`, `DragStars`) — `Split`/`Collapse` are not ported, they land with the tabs | `*.ui.xml` `<NextSplitView>`, `<NextSplitter>` | [[ui-engine-stack]] |
| scrolling and thumbs on the **new** stack | `UINext.NextScrollableControl` (`scrollDirection`, `MaxScrollOffset`, `ThumbTravel`, `ArrangeThumbs`, `EnsureThumbs`, `OnPointerScroll`, `ScrollIntoView`); `UINext.NextScrollThumbControl` — one thumb per axis, both appended so the last-to-first hit-test reaches them | `*.ui.xml` `<NextScrollable>` attrs `ScrollDirection`, `ScrollSensitivity`, `Overscroll`, `Thumb*ColorHex` | [[ui-engine-stack]] |
| building a **new**-stack tree from XML | `UINext.Control.ParseXML` (in `ControlXml.cs`); the `[A_XSDElementProperty]` set on `UINext.Control`; `UINext.ContainerControl` | `Thorium/Data/XML/Documents/UI/NextProbe.ui.xml`, registered in `ThoriumAssets.assets.xml` — both deleted at 6b | [[ui-engine-stack]] |
| corner radii, padding and margin on the **new** stack | `UINext.CornerRadii` + `CornerRadiiConverter`, `UINext.Thickness` + `ThicknessConverter`, `UINext.ControlColor` | `*.ui.xml` attrs `CornerRadius`, `Padding`, `Margin`, `ControlColor` | [[ui-engine-stack]] |
| images, icons, masks and gradients on the **new** stack — one sampler slot read three ways | `UINext.Control` (`kind`, `sampler`, `SetUVRect`, `gradient`), `VulkanControl.noTexture`, `VulkanControlType`; `Render.Modules.UIEngineModule` (`CreateGradientTable`, set 1 binding 3); `Shaders/UIEngine/UIEngine.frag` | `Thorium/Data/XML/Documents/Gradients.gradients.xml` | [[ui-engine-stack]], [[ui-gradients]] |
| compositing several modules into one window | `Core.Rendering.Modules.CompositorModule`; `compositorOrder` on `Render.Modules.RenderingModule` | — | [[ui-engine-stack]] |
| new UI shaders | `*/Shaders/UIEngine/UIEngine.vert`, `UIEngine.frag` — four copies | — | `shader-pipeline` skill |
| buffers, GPU memory | `Render.Helpers.AVulkanBufferHandler` | — | [[mapped-streaming-buffers]], [[engine-resource-manager]] |
| UI shaders | `*/Shaders/UIRasterizer/UI.vert`, `UI.frag` — three copies | — | `shader-pipeline` skill |
| graphics settings | `Render.GraphicsSettings`, `DisplayNames` | `Thorium/Data/XML/Settings/Graphics.settings.xml` | [[settings-categories]] |

## Thorium (the host app)

| Concept | Code | Data | Note |
|---|---|---|---|
| app entry, vault path setting | `Thorium.Thorium`, `Thorium.ThoriumSettings` | `Thorium/Data/XML/Settings/` | [[vault-browser-and-shell]] |
| vault list and switching | `Thorium.Editor.VaultsWindow`; `UI.MenuScreen`; `ArctisAurora.Core.Filing.FolderPicker` | `Vaults.ui.xml`, `UI.ui.xml` | [[vault-list-and-switching]] |
| the note browser | `Thorium.Editor.CustomControls.VaultBrowserControl` | `UI.ui.xml` | [[vault-browser-and-shell]] |
| host-side control subclasses | `Thorium.Editor.Decorations` | — | — |

## Carbon (the frame viewer)

| Concept | Code | Data | Note |
|---|---|---|---|
| app entry, wiring the four views | `Carbon.Carbon`, `Carbon.CarbonSettings` | `Carbon/Data/XML/Settings/` | [[carbon-frame-viewer]] |
| the capture session list, Load XML | `Carbon.Editor.CustomControls.SessionListControl`; `Carbon.Editor.CarbonActions` | `Carbon/Data/XML/Documents/UI/UI.ui.xml` | [[carbon-frame-viewer]] |
| frames over time, click to select one | `Carbon.Editor.CustomControls.FrameStripControl` | `UI.ui.xml` | [[carbon-frame-viewer]] |
| flame chart and the aligned timeline, the timeline's scroll bar | `Carbon.Editor.CustomControls.SpanChartControl` (`Mode="Frame"` / `"Timeline"`), `ChartScrollThumbControl` | `UI.ui.xml` | [[carbon-frame-viewer]] |
| zone totals, calls, min/max, counters | `Carbon.Editor.CustomControls.ZoneTableControl` | `UI.ui.xml` | [[carbon-frame-viewer]] |

## Facts that cost time to rediscover

- **There is no theme file, no stylesheet, no palette.** A colour is a per-control attribute in the
  host's `*.ui.xml`, or a hardcoded default in the control's C#. "Change the app's theme" means
  editing those attributes across the `*.ui.xml` set plus the defaults in `VulkanControl`.
  `ControlColor` names only the 16 enum values in `EnumColorToHex`; everything else is `ColorHex`.
- **Data XML files carry their kind in the filename** — `Bootstrap.bootstrap.xml`, not `Bootstrap.xml`.
  Notes written before that landed still use the short name. See [[xml-type-suffix]].
- **Shaders exist in four copies** — `AuroraEngine/`, `Thorium/`, `AuroraEditor/`, `Carbon/` — and the
  `.spv` must stay byte-identical across them. Edit the `AuroraEngine` copy; the `shader-pipeline`
  skill owns the procedure. Carbon carries only the four the UI path loads (`UIRasterizer/UI.*` and
  `Modules/Compositor/compositor.*`), because it never constructs another renderer type.
- **An app is launched from its own `bin/Debug/<tfm>/`, not by `dotnet run`.** `Paths.GetPath`
  resolves `../../../Data` against the *working directory*, so the wrong one kills boot at
  `XSDGenerator`. Same rule puts `Shaders/` in every app.
- **`Thorium` was `Periodic`; `AuroraEngine` was `ParticleSimulator`.** Older notes, commits and plan
  files use the old names. See [project-map.md](project-map.md).
- **Set 0 belongs to the renderer**, not to a module — a module's own descriptor sets start at 1.

## When the concept is not in the table

UI XML attribute names are `[A_XSDElementProperty]` on a C# member, so the attribute is greppable
back to its owner:

```
grep -rn '\[A_XSDElementProperty("ColorHex"' AuroraEngine
```

Same for `[A_XSDType("…")]` for an element name and `[A_XSDActionDependency("…")]` for an action
string in a bootstrap, shutdown, menu or input file. That is one grep, not a search.

## Keeping this current

Updated when a system lands, alongside the `Decisions/` note — the `aurora-docs` skill carries the
step. A row is wrong when a class is renamed or moved between namespaces; a row is missing when a new
`Decisions/` note names types no row mentions.

Related: [[project-map]], [Decisions/INDEX.md](../Decisions/INDEX.md),
[Patterns/finding-code.md](../Patterns/finding-code.md)
