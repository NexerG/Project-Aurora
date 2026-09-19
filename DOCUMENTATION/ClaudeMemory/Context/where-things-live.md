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
| `Filing` | `ArctisAurora.Core.Filing` (+ `.Serialization`) — fonts, glyphs, icon sets; serializer, importers, paths |
| `Registry` | `ArctisAurora.Core.Registry`; `Assets` = `...Registry.Assets` |
| `Render` | `ArctisAurora.EngineWork.Rendering` (+ `.Modules`, `.Helpers`, `.MeshSubComponents`) |
| `Threading` | `ArctisAurora.Core.Threading` |
| `UI` | `ArctisAurora.Core.UI` — the UI stack, flat: controls, layout, input, documents, actions, gradients, the text measurer. `Control` is CPU, `VulkanControl` is one GPU quad |

**A `UI` type by name → [ui-orientation.md](ui-orientation.md)**: one entry per component — what it does, its
XML element, entry points, regions. The rows below answer "which types own this concept".

## Look, colour and chrome

| Concept | Code | Data | Note |
|---|---|---|---|
| the theme, palette, automatic colours, dark/light text on a panel, which palette the app uses, fading between themes | `Animation.Animations.FadeSlots`, `AnimationSystem` (slot fades), `UI.PaletteDefinition.themeFade`, `UI.SettingsWindow` palette `onPicked`; `UI.Palettes` (`baked`, `LoadPalettes`, `Get`, `Names`, `Default`, `Surface`, `Ink`, `Step`, `Inline`), `UI.PaletteDefinition`, `UI.PaletteRole`, `UI.PaletteSetting` (`UISettings.palette`); `UI.Control` — `role`, `paletteName`, `PaintOr`, `CopyPaint`, `InheritPaint`, `RepaintChildren`; `TextBoxControl.PaintText`, `EditableLabelControl.PaintText` | `*/Data/XML/Documents/Palettes/*.palette.xml`; `*/Data/XML/Settings/UI.settings.xml` (`<UI><Palette Name>`); `*.ui.xml` attrs `Palette`, `Role` | [[ui-palettes]] |
| an authored control colour | `UI.Control` — `colorHex`, `edgeColorHex`, `EnumColorToHex`, `HexToRGB` | `*/Data/XML/Documents/UI/*.ui.xml` attrs `ColorHex`, `ControlColor`, `EdgeColorHex` | [[control-edge-and-outline]], [[ui-palettes]] |
| styling shape, rounded corners, how round rows/tabs/menus/buttons are, accent bar thickness, window corner rounding | `UI.PaletteDefinition` (`rowRadius`, `tabRadius`, `controlRadius`, `popupRadius`, `rowAccentWidth`, `tabAccentWidth`, `windowCorners`); `UI.CornerRole`, `UI.AccentRole`, `UI.WindowCorners`; `UI.Control` — `cornerRole`, `accentRole`, `ApplyShape`, `cornerRadius`; `Rendering.AGlfwWindow.RoundCorners` | `*.palette.xml` attrs `RowRadius`, `TabRadius`, `ControlRadius`, `PopupRadius`, `RowAccentWidth`, `TabAccentWidth`, `WindowCorners`; `*.ui.xml` attr `CornerRadius` | [[ui-palette-shape]] |
| a border, an edge, accent bars — the active tab's top bar, the highlighted sidebar row, the edge colour | `UI.Control` — `edgeThickness` (`Thickness`, per side), `edgeColorHex`, `edgeRole`, `accentRole`; `UI.Palettes.EdgeAccent`; `UI.TabViewControl` (`ApplyTabColors`, `activeChanged`); `UI.FileBrowserControl.SetCurrent`; `Thorium.Editor.CustomControls.VaultBrowserControl` (`FollowFocusedTab`); `Registry.Context.changed` | `*.palette.xml` attr `EdgeAccent`; `*.ui.xml` attrs `EdgeThickness`, `EdgeColorHex`, `EdgeRole`; `Thorium/Data/XML/Documents/UI/UI.ui.xml` title-bar mark and bottom rule | [[control-edge-and-outline]], [[ui-palettes]], [[ui-palette-shape]] |
| a quad's colour on the GPU — paint words, the paint table | `UI.VulkanControl` (`paint`, `alpha`, `edgePaint`); `UI.Palettes.Paints` (`GpuPaint`, pool `Paints`); `UI.Gradients.Pool` (pool `Gradients`); `Rendering.Modules.UIEngineModule.TableMirror`; `Shaders/UIEngine/UIEngine.vert` `resolvePaint` | `AuroraEngine/Data/XML/Documents/Pools.pools.xml` | [[ui-palettes]] |
| gradients; a gradient that follows the palette, role stops, shade, an edge/border gradient | `UI.Gradients` (`IndexOf`, `Word`), `UI.GpuGradientStop`; `UI.Palettes.RoleOffsets`, `gradientBit`; `UI.Control.gradient`, `edgeGradient`, `gradientId`, `Emit`; `Shaders/UIEngine/UIEngine.frag` `stopColor`, `isGradient` | `Thorium/Data/XML/Documents/Gradients.gradients.xml` | [[ui-gradients]] |
| effects, per-letter animation, a wave or bounce on text, fading/sliding text in, GPU tweens | `UI.Effects` (`LoadEffects`, `IndexOf`, `Stagger`), `UI.EffectDefinition`, `UI.GpuEffect`; `UI.VulkanControl` (`effect`, `effectStart`); `UI.Control` (`effect`, `RestartEffect`); `UI.TextRunControl` (`StyleSpan.effect`, `WriteGlyph`); `UI.BlockControl` `Run.effect`; `Rendering.Modules.UIEngineModule` (set 1 binding 5); `Shaders/UIEngine/UIEngine.vert` `ease`, `effectProgress` | `*/Data/XML/Documents/Effects/*.effects.xml` (engine `UI.effects.xml`, Thorium `Thorium.effects.xml`); `*.ui.xml` / notes attr `Effect` | [[ui-effects]] |
| corner rounding, edge + outline strokes | `UI.Control`, `VulkanControl`; `Shaders/UIEngine/UIEngine.frag` | — | [[control-edge-and-outline]] |
| clipping a control to its parent | see "clipping on the **new** stack" below | — | [[ui-clipping]] (old stack) |
| title bar, minimise/maximise/close | `UI.TitleBarControl`, `WindowFrameControl`; `UI.WindowActions`; `UI.LabelControl` | host `UI.ui.xml` | [[window-chrome-and-label]], [[window-frame-resize]] |
| icons | `Filing.IconSet`; `UI.IconControl`; `Assets.IconSetAsset`; `Filing.SvgPath` | `*/Data/Icons/*/*.import.xml` | — |
| fonts, glyph atlas | `Filing.AuroraFont`, `Filing.Glyph`, `Filing.Bezier`; `Assets.FontAsset`; `ArctisAurora.Core.Generators.MTSDFGen` | `*/Data/Fonts/*/*.import.xml`; `AuroraEngine/Data/XML/Imports/EngineFonts.imports.xml` | [[atlas-is-unorm-not-srgb]] |
| which face a run draws in — regular, bold, italic, bold-italic | `UI.FontStyle`, `UI.AtlasMetaData` (`Effective`, `StyleBlock`, `CellIndex`, `charIndex`); `Filing.AssetImporter` face probing | `AuroraEngine/Data/XML/Imports/EngineFonts.imports.xml` attrs `Bold`, `Italic`, `BoldItalic` | [[bold-italic-face]] |
| button hover / press appearance, how fast hover fades, signals | `UI.ButtonControl` (`state`, `PaintState`, `Signal`); `UI.VulkanControl.state`; `UI.Palettes.IsSurfaceRest`; `UI.PaletteDefinition` (`stateFrequency`, `stateDamping`); `Animation.Signals`; `Shaders/UIEngine/UIEngine.vert` `resolveStatePaint`; `UI.UIEngine` | `*/Data/XML/Documents/Palettes/*.palette.xml` attrs `StateFrequency`, `StateDamping` | [[ui-palettes]] § Continuous state, [[animation-core]], [[button-states-and-hover-bubbling]] (old stack) |

## Layout and hit-testing

| Concept | Code | Data | Note |
|---|---|---|---|
| measure / arrange pass | `UI.UIEngine.ResolveLayout`; `UI.Control` (`Measure`, `Arrange`); `UI.StackPanelControl` | — | [[stack-panel-arrange-clamp]] |
| mouse hit-test, hover, drag lifecycle | `UI.UIEngine` (`Poll`, `HitTest`, `CheckDrag`); `UI.Control.StartDrag`; `UI.DragGhost` | — | [[n-click-dispatch]], [[document-selection]] |
| double / triple click | `UI.UIEngine` (`SolveRelease`'s `tapCount`, `PointerPhase.Tap`); `Core.InputHandler.tapWindow`; `Core.InputSettings.DoubleClickSetting` | `*/Data/XML/Settings/` | [[n-click-dispatch]], [[tab-rename-and-double-click]] |
| padding, margin, border widths | `UI.Thickness`, `UI.ThicknessConverter` | — | [[thickness-type-converter]] |
| parenting, child order, control names | `ArctisAurora.Core.ECS.EngineEntity.Entity`; `UI.Control`, `ContainerControl` | — | [[entity-reparenting-and-names]] |
| window scaling mode, ortho box | `UI.WindowRoot`; `Render.AuroraCamera` | host `UI.ui.xml` | [[window-scaling-modes]] |

## Containers and navigation

| Concept | Code | Data | Note |
|---|---|---|---|
| tabs, tearing off, tab menus | `UI.TabViewControl`, `TabItemControl`, `EditableTabsControl`, `TabStripButtonControl` | `TabWindow.ui.xml`, `TabPane.ui.xml` | [[tab-view-control]], [[tab-rename-and-double-click]] |
| splits, splitters, docking | `UI.SplitViewControl`, `DockingControl`, `SplitterControl` | host `Workspace.ui.xml` | [[splitter-and-pane-sizing]] |
| scrolling, thumb, overscroll | `UI.ScrollableControl`, `ScrollThumbControl` | — | [[scrollbar-thumb]], [[scroll-overscroll]] |
| file tree, note browser rows | `UI.FileBrowserControl`, `FileTreeControl`, `FileRowControl`; `ArctisAurora.Core.Filing.FileObject` | — | [[file-browser-tree]] |
| context menus | see "context menus on the **new** stack" below | `*/Data/XML/Documents/Menus/*.menu.xml` | [[context-menus]]; [[context-menu-hosting]], [[context-menu-invoker]] (old stack) |
| session and layout restore | `UI.SessionLayout`; `UI.WorkspaceControl` | `Workspace.ui.xml`; `Shutdown.shutdown.xml` | [[session-restore]] |
| rename a row or tab in place | `UI.EditableLabelControl`, `TextBoxControl` | — | [[inline-rename]] |
| dropdowns, checkboxes, menu buttons | see "menu bar buttons, dropdowns…" below | — | — |

## Text and documents

| Concept | Code | Data | Note |
|---|---|---|---|
| document model — blocks and runs | `UI.RichTextDocument`, `BlockControl` (+ `Run`); `UI.TextStyleType`, `TextStyle`, `DocumentLayout`, `DocumentSettings` (in `RichTextDocument.cs`) | `*/Data/Notes/*.xml` | [[text-styling-types]], [[document-structural-editing]] |
| line breaking, text measurement, atlas cell geometry | `UI.TextMeasurer` (`atlasInkMargin`, `CellScale`), `FontAssetGlyphMetrics` | — | [[text-layout-one-measurer]] |
| caret — blink, position, follow | `UI.CaretControl`, `DocumentEditorControl` | — | [[caret-blink-and-focus]], [[document-caret-scrolling]] |
| selection | `UI.DocumentControl`, `CaretSlot` | — | [[document-selection]] |
| bold / italic / headings, format bar | see "bold / italic / headings, format bar on the **new** stack" below | `AuroraEngine/Data/XML/Settings/DocumentSettings.settings.xml` | [[document-format-bar]], [[armed-style-at-the-caret]], [[text-styling-types]], [[bold-italic-face]] |
| undo / redo | `Editing.UndoStack`, `EditStep`, `IEditRecord`; see the **new**-stack row below | — | [[document-undo]] |
| single-line text fields | `UI.TextBoxControl` | — | [[note-naming-and-text-field]] |
| text and the caret on the **new** stack — a paragraph as one control, a GPU quad per visible glyph | `UI.TextRunControl` (`spans`, `Emit`, `IndexAt`, `CaretAt`, `TextOrigin`), `StyleSpan`, `IGlyphPressTarget`, `CaretControl`; `Shaders/UIEngine/UIEngine.frag` MTSDF branch | — | [[ui-engine-stack]], [[ui-draw-list]] |
| what the new stack draws this frame, and what it culls — off screen, or too small to draw its contents; the draw list, the quad pool | `UI.UIEngine` (`Quads`, `BuildDrawLists`, `Collect`, `detailCullSize`), `Control.Emit`, `TextRunControl.WriteGlyph`, `LayoutRect.Overlaps`; `Data.DataPool` (`Rewind`, `Append`); `Render.Modules.UIEngineModule` (`PublishQuadRange`, `MirrorDrawList`) | `AuroraEngine/Data/XML/Documents/Pools.pools.xml` (`UIQuads`) | [[ui-draw-list]], [[ui-quads-pool]] |
| the UI flickering, a frame drawing blank or short, one window showing another's quads | `UI.UIEngine.BuildDrawLists`; `Render.Modules.UIEngineModule` (`PublishQuadRange`, `_quadRange`, `MirrorDrawList`) | — | [[ui-draw-list-publish]], [[ui-quads-pool]] |
| clipping on the **new** stack | `UI.Control` (`arrange.clip`), `UIEngine.Collect`; `Shaders/UIEngine/UIEngine.frag` (`inClip`) | — | [[ui-engine-clip-as-coverage]] |
| per-character bold / colour / size on a run | `UI.StyleSpan`; `UI.TextMeasurer.Run` (`charStart`/`charCount`) | `*/Data/Notes/*.xml` `<Run>` attrs | [[ui-engine-stack]] |
| note load / save | `UI.DocumentXml`; `Filing.Serializer`, `XmlReflection` | `*/Data/Notes/*.xml` | [../Patterns/document-xml-persistence.md](../Patterns/document-xml-persistence.md), [[xml-save-skips-defaults]] |
| a note on the **new** stack — blocks, caret, selection, editing | `UI.DocumentControl`, `BlockControl` (+ `Run`), `DocumentEditorControl`, `CaretSlot` | host `*.ui.xml` `<DocumentEditor Source>`; `*/Data/Notes/*.xml` | [[ui-engine-stack]], [[document-selection]] (old) |
| bold / italic / headings, format bar on the **new** stack | `UI.DocumentToolbarControl`, `StyleDelta`, `CaretStyle`, `StyleRangeEdit`; `UI.TextInputActions` (`Toggle`, `Editor`) | host `*.ui.xml` `<DocumentToolbar>`; `AuroraEngine/Data/XML/Settings/DocumentSettings.settings.xml` | [[ui-engine-stack]], [[document-format-bar]] (old), [[armed-style-at-the-caret]] (old) |
| undo / redo on the **new** stack | `Editing.UndoStack`; `UI.TextEdit`, `SplitEdit`, `DeleteRangeEdit`, `StyleRangeEdit`, `BlockSnapshot`, `DocumentFragment`, `DocumentAddress` | — | [[ui-engine-stack]], [[document-undo]] (old) |
| note load / save on the **new** stack | `UI.DocumentXml`, `RichTextDocument`, `DocumentEditSession`; `UI.NoteActions` | `*/Data/Notes/*.xml` — `<Document>`/`<Block>`/`<Run>`, written by hand; `UITypeSchema.xsd` no longer declares them | [[ui-engine-stack]], [../Patterns/document-xml-persistence.md](../Patterns/document-xml-persistence.md) |
| markdown notes, plain-text notes, which file types the vault opens, the default for a new note | `UI.RichTextDocument` (`Load`, `Save`, `extensions`); `UI.MarkdownFormat`, `UI.PlainTextFormat`; `UI.DocumentXml` (`Parse`, `ToXml`); `Thorium.Editor.CustomControls.VaultBrowserControl` (`Accepts`, `FreePath`, `WriteName`) | vault `*.md`, `*.txt`, `*.xml` | [[note-file-formats]] |
| bullet points, checkboxes / task lists, list nesting, Tab / Shift+Tab | `UI.BlockControl` (`listKind`, `listLevel`, `isChecked`, `SyncMarker`), `UI.ListKind`; `UI.DocumentControl` region `lists` (`TypeListPrefix`, `ClearListAtCaret`, `ShiftListLevel`, `SetBlockList`); `UI.DocumentEditorControl` (`SetChecked`, `ShiftListLevel`); `UI.BlockStateEdit`; `UI.TextInputActions` (`Indent`, `Outdent`); `UI.DocumentLayout.listIndent` | `<Block List Level Checked>`; `Thorium/Data/XML/Documents/Inputs/InputMap.inputs.xml` (`Text.Indent`, `Text.Outdent`) | [[note-file-formats]] |

## Input

| Concept | Code | Data | Note |
|---|---|---|---|
| keybinds, gestures, key repeat | `Core.InputHandler`, `InputSettings`; `InputBindings`, `KeybindOverride` (in `InputBindingSettings.cs`) | `*/Data/XML/Documents/Inputs/InputMap.inputs.xml` | [[engine-side-text-input]] |
| modifier roles (Ctrl/Shift by name) | `Core` — `InputModifier`, `NamedModifier`, `GestureMatcher` | `InputMap.inputs.xml` | [[named-input-modifiers]] |
| what a keystroke does in the editor | `UI.TextInputActions` | `InputMap.inputs.xml` | [[engine-side-text-input]] |
| contexts — when a binding is live | `Registry.Context`, `ContextDefinition` | `Thorium/Data/XML/Documents/Contexts/Thorium.contexts.xml` | [[declared-contexts]] |

## Data, assets and settings

| Concept | Code | Data | Note |
|---|---|---|---|
| asset registry and lookup | `ArctisAurora.EngineWork.Registry.AssetRegistries`; `Assets.*` | `AuroraEngine/Data/XML/Documents/Registry.registry.xml`; `*/Data/XML/Assets/*.assets.xml` | [[asset-manifest-and-import]] |
| importing source assets | `Filing.AssetImporter`, `MeshImporter`; `ImportSet`, `FontImport`, `IconImport` (in `ImportManifest.cs`) | `*.import.xml`, `*.imports.xml` | [[asset-manifest-and-import]], [[asset-pipeline-bake]] |
| UI documents as assets | `Assets.UIDocumentAsset`; `UI.Control.ParseXML` | `*/Data/XML/Documents/UI/*.ui.xml` | [[ui-document-registry]] |
| settings and their cascade | `Registry.SettingsRegistry`, `Setting`, `SettingScope`, `SettingCategory`, `UserSettingsFile` | `*/Data/XML/Settings/*.settings.xml` | [[settings-registry]], [[settings-categories]] |
| paths, virtual file system | `Filing.Paths`, `VirtualFileSystem` | — | [[asset-manifest-and-import]] |
| XSD schema generation | `Registry.XSDGenerator`; `AnyXMLType` | `*/Data/XML/Schemas/` | [[xsd-generator-cross-category]] |
| data pools (the ECS rework) | `Data.DataManager`, `DataPool`, `PoolColumn`, `TransformData`; `Data.Commands.*` | `AuroraEngine/Data/XML/Documents/Pools.pools.xml` | [[ecs-rework-data-pools]], [[cross-system-change-notification]] |

## Engine core and lifecycle

| Concept | Code | Data | Note |
|---|---|---|---|
| startup order | `Core.Bootstrapper` | `AuroraEngine/Data/XML/Documents/Bootstrap.bootstrap.xml` | — |
| quitting, save prompts | `Core.Shutdown`; `UI.NoteActions`, `WindowActions` | `AuroraEngine/Data/XML/Documents/Shutdown.shutdown.xml` | [[shutdown-sequence]] |
| tick order, threads | `Core.Engine`; `Threading.MainSystem`, `RenderSystem`, `PhysicsSystem`, `ThreadedSystem`; `Animation.AnimationSystem` | — | [[ecs-rework-data-pools]], [[animation-core]] |
| animation, tweens, easing, springs, animating a property, messaging another thread | `Animation.Animations` (`Tween`, `Spring`, `Retarget`, `Stop`), `AnimationSystem`, `Curve`/`EaseKind`, `Spring`, `A_Animatable`, `AnimatableProperty`; `Threading.ThreadedSystem.Post`/`OnPost`; `Data.Commands.CommandOp.Post` | `AuroraEngine/Data/XML/Documents/Pools.pools.xml` (`Animations`) | [[animation-core]] |
| keyframes, clips, animation XML, hover/press animation on a control, reversing an animation, state bindings (rest/hover/press values), context-menu row hover, menu underline, menu slide-out, folder expand/collapse animation | `UI.ContextMenuControl.Row` (`menu-row`), `UI.ContextMenuControl.reveal` (`menu-open`), `UI.FileTreeControl` (`Toggle`, `Collapse`), `Animations.StopAll`, `Animation.AnimationLibrary` (`LoadAnimations`, `Clip`, `Binding`, `Sample`, `LocalTime`, `ParseValue`), `ClipDefinition`/`ClipTrackDefinition`/`KeyDefinition`, `BindingDefinition`/`BindingTrackDefinition`, `Keyframe`, `ClipLoop`, `Animations.Play`/`Direct`, `StateBinding`, `AnimationSystem` (keyframe driver); `UI.Control` — `clip`, `hoverClip`, `pressClip`, `stateBinding` | `*/Data/XML/Documents/Animations/*.anim.xml` (engine `UI.anim.xml`, Thorium `Thorium.anim.xml`); `Pools.pools.xml` (`Keyframes`); `*.ui.xml` attrs `Clip`, `HoverClip`, `PressClip`, `StateBinding` | [[animation-core]] |
| entity create / destroy | `Registry.EntityRegistry`; `Core.Engine.Interpolate` | `EntityRegistry.entities.xml` | [[entity-lifecycle-queues]] |
| which columns an entity has; an entity's position/scale | `ECS.EngineEntity.Entity` (`PoolName`, `AllocatePooledData`, `AllocateIn`, `FreePooledData`), `TransformEntity` | `AuroraEngine/Data/XML/Documents/Pools.pools.xml` | [[entity-transform-split]] |
| logging | `Diag.LogChannel`, `LogLevel`, `LoggingSettings`; `Diag.Sinks.*` | `Bootstrap.bootstrap.xml`, `Shutdown.shutdown.xml` | [[engine-logging]] |
| profiling — timing a tick phase, counting calls | `Diag.Profiling` (`Zone.Start`/`End`/`Increment`, `Report`); zones in `Core.Engine.MainTick`, `Threading.RenderSystem`; draw and swapchain-rebuild zones in `Render.Renderer` (`Draw`, `RecreateSwapchain`, `CreateSwapchain`); layout zones in `UI.UIEngine.ResolveLayout`, `UI.DocumentControl`, `UI.TextRunControl`, `UI.DocumentEditorControl` | — | [[engine-profiling]] |
| frame capture — every span of every frame, to a file | `Diag.Profiling.Frame`, `Diag.FrameSpool`, `ProfilingSettings`; frame edges in `Threading.ThreadedSystem.Loop`; `Diag.Profiling.CaptureUntilFlush`; `Diag.Profiling.Flush` waits for every thread's last batch at shutdown | `Bootstrap.bootstrap.xml`, `Shutdown.shutdown.xml` | [[engine-profiling]] |
| profiling a launch — `--profile[=N]`, or `Mode="Boot"` | `Diag.Profiling.ArmBoot` (called from `Core.Engine.Init`), `Diag.CaptureMode`; phase frame + step zones in `Core.Bootstrapper.RunPhase` | `*/Data/XML/Settings/` (`<Profiling><ProfilingCapture Mode="Boot"/>`) | [[engine-profiling]] §13 |
| profiling data pools — items, capacity, memory per frame; `--profile-pools` | `Diag.Profiling.Frame.Pool` (called from `Data.DataManager.FrameEdge`), `Data.DataPool.ReservedBytes`, `Diag.ProfilingCaptureSetting.pools`; `Diag.FrameSpool.WriteFrame` (`<P>`) | `*/Data/XML/Settings/` (`<Profiling><ProfilingCapture Pools="true"/>`) | [[engine-profiling]] §14 |
| profiling a scenario — typing and resizing the window on a 1M-char note, one capture; `--profile-scenario` | `Diag.ProfileScenario` (`Arm` called from `Core.Engine.Init`; `Open`, `OnTick`) | — | [[engine-profiling]] §15 |
| profiling animation, animation stress test, animation at scale; `--profile-scenario=animation` | `Diag.ProfileScenario` (`RunAnimation`, `BuildGrid`, `Ramp`); zones/counters `Anim.*` in `Animation.AnimationSystem`, `Threading.MainSystem.OnPost`, `Animation.Animations` | engine `Animations/UI.anim.xml` (`profile-state`, `profile-margin`) | [[engine-profiling]] §17, [[animation-core]] § Measured at scale |
| reading a frame file back | `Diag.FrameCaptureReader`; `CaptureSession`, `CapturedThread`, `CapturedFrame`, `CapturedSpan`, `CapturedPool` | `Profiling/<session>/<thread>.frames.xml` | [[carbon-frame-viewer]] |
| the new-stack tree as laid out — every control's rect, to a file (F10) | `UI.UITreeDump` (`UI.DumpTree`) | `Thorium/Data/XML/Documents/Inputs/InputMap.inputs.xml`; writes `uitree.xml` beside the exe | `aurora-verify` skill |

## Rendering

| Concept | Code | Data | Note |
|---|---|---|---|
| device, swapchain, frame loop | `Render.Renderer`, `Swapchain`, `RenderWindow`, `VulkanRenderer` | — | [[render-window-owns-the-swapchain]], [[swapchain-extent-is-the-truth]], [[dynamic-rendering]] |
| OS windows, placement; the active window | `Render.AGlfwWindow`; `UI.UIEngine.activeWindow` (`ActiveWindow`, set mid-drag only — OS focus feeds nothing since 2026-09-15) | — | [[active-glfw-window-context]] (old stack) |
| the **new** UI draw path | `Render.Modules.UIEngineModule` (`window.ui`, `uiRoot`, `firstInstance`, `WriteTextureTable`); `UI.UIEngine`, `Control` (`rows`), `WindowRoot` | `AuroraEngine/Data/XML/Documents/Pools.pools.xml` (`UIElements`, `UIQuads`) | [[ui-engine-stack]] |
| the **new** measure/arrange pass, dirty roots, per-window ranges | `UI.UIEngine` (`ResolveLayout`, `RefreshWindowRanges`, `ElementOrder`, `NextControlOrder`); `UI.Control` (`Measure`, `Arrange`, `InvalidateLayout`) | `Pools.pools.xml` (`SortAction`) | [[ui-engine-stack]] |
| the **new** window root, design-space fitting | `UI.WindowRoot` (`FitTo`, `ViewportSize`, `ToDesignSpace`) | — | [[ui-engine-stack]] |
| the **new** hit-test, hover, press, bubbling | `UI.UIEngine` (`Poll`, `HitTest`, `Dispatch`, `Forget`); `UI.PointerEvent`, `PointerPhase`; `UI.Control` (`OnPointerX`, `RegisterOnX`, `hitTestable`, `canBeActiveContext`, `takesActiveControl`) | — | [[ui-engine-stack]] |
| the **new** wheel | `UI.UIEngine` (`SolveScroll`, `ActiveTarget`); `UI.Control` (`OnPointerScroll`, `RegisterOnScroll`) | — | [[ui-engine-stack]] |
| dragging on the **new** stack | `UI.Control` — `draggable` (gates the press), `StartDrag`, `DraggingOverStart`/`DraggingOver`/`DraggingOverEnd`, `FinishDrag`, `ChildDraggedOut`; `UI.UIEngine` — `SetDragging`, `dragging` (`Dragging` context), `CheckDrag`, `EndDrag`, `HitTest`'s `skip` | `*.ui.xml` attr `Draggable` | [[ui-engine-stack]] § the drag gap |
| delivering a drag to its **claimant** on the new stack | `UI.Control` (`onDrag`, `onDragStop`, `OnDrag`, `OnDragStop`, `RegisterOnDrag`); `UI.UIEngine` (`SolveDrag` and its stale-release guard, `WindowOf`, `Poll`'s `ownsDrag`). Consumers: `UI.WindowFrameControl`, `SplitterControl`, `ScrollThumbControl` | — | [[ui-engine-stack]] |
| context menus on the **new** stack, right click, submenus | `UI.ContextMenus` (`Collect`, `Open`, `Register`, `Get`, `target`, `Tick`); `UI.ContextMenuControl` (+ `Row`); `UI.ContextMenu`, `ContextMenuButton`, `ContextMenuLine`, `ContextMenuSubmenu`; `UI.Control` (`contextMenu`, `stopsContextMenu`, `ParseMenu`); `Registry.Assets.ContextMenuAsset` | `Thorium/Data/XML/Documents/Menus/*.menu.xml`, listed as `ContextMenuAsset` in `ThoriumAssets.assets.xml`; the engine's `view` and `tab` in `AuroraEngine/Data/XML/Documents/Menus/`, listed in `EngineAssets.assets.xml`; dictionary `contextMenus` in `Registry.registry.xml`; `*.ui.xml` attrs `ContextMenu`, `StopsContextMenu` | [[context-menus]] |
| menu bar buttons, dropdowns, checkboxes, key capture on the **new** stack | `UI.MenuButtonControl`, `DropdownControl`, `CheckBoxControl`, `KeyCaptureControl` | `*.ui.xml` `<MenuButton ContextMenu="…">`, `<Dropdown>`, `<CheckBox>`, `<KeyCapture>` | [[context-menus]] |
| tab and view menu actions — close, split — on the **new** stack | `UI.TabActions`, `ViewActions`, `UIActions.Invoking` (new stack first, via `UI.ContextMenus.target`); `UI.TabViewControl.tabContextMenu` | `Tab.menu.xml`, `View.menu.xml`; `*.ui.xml` attr `TabContextMenu` | [[context-menus]] |
| the drag preview on the new stack | `UI.DragGhost` (`Show`, `Hide`, `Follow`); `Render.Modules.UIEngineModule` (`rangeRoot`, `rangeRect`); `UI.Control.draggingOpacity` | `*.ui.xml` attr `DraggingOpacity`; `<UI><DragGhost Opacity>` setting (`UI.UISettings`) | [[ui-engine-stack]], [[render-thread-reads-pool-row]] |
| splits and grips on the **new** stack | `UI.SplitViewControl` (a transparent `StackPanelControl`); `UI.SplitterControl` (`PreviousPane`/`NextPane`, `DragStars`) — `Split`/`Collapse` are not ported, they land with the tabs | `*.ui.xml` `<SplitView>`, `<Splitter>` | [[ui-engine-stack]] |
| scrolling and thumbs on the **new** stack | `UI.ScrollableControl` (`scrollDirection`, `MaxScrollOffset`, `ThumbTravel`, `ArrangeThumbs`, `EnsureThumbs`, `OnPointerScroll`, `ScrollIntoView`); `UI.ScrollThumbControl` — one thumb per axis, both appended so the last-to-first hit-test reaches them | `*.ui.xml` `<Scrollable>` attrs `ScrollDirection`, `ScrollSensitivity`, `Overscroll`, `Thumb*ColorHex` | [[ui-engine-stack]] |
| building a **new**-stack tree from XML | `UI.Control.ParseXML` (in `ControlXml.cs`); the `[A_XSDElementProperty]` set on `UI.Control`; `UI.ContainerControl` | any `Next*` `*.ui.xml`, e.g. `Thorium/Data/XML/Documents/UI/UI.ui.xml`, registered in `ThoriumAssets.assets.xml` | [[ui-engine-stack]] |
| corner radii, padding and margin on the **new** stack | `UI.CornerRadii` + `CornerRadiiConverter`, `UI.Thickness` + `ThicknessConverter`, `UI.ControlColor` | `*.ui.xml` attrs `CornerRadius`, `Padding`, `Margin`, `ControlColor` | [[ui-engine-stack]] |
| images, icons, masks and gradients on the **new** stack — one sampler slot read three ways | `UI.Control` (`kind`, `sampler`, `SetUVRect`, `gradient`), `VulkanControl.noTexture`, `VulkanControlType`; `Render.Modules.UIEngineModule` (`CreateGradientTable`, set 1 binding 3); `Shaders/UIEngine/UIEngine.frag` | `Thorium/Data/XML/Documents/Gradients.gradients.xml` | [[ui-engine-stack]], [[ui-gradients]] |
| compositing several modules into one window | `Core.Rendering.Modules.CompositorModule`; `compositorOrder` on `Render.Modules.RenderingModule` | — | [[ui-engine-stack]] |
| new UI shaders | `*/Shaders/UIEngine/UIEngine.vert`, `UIEngine.frag` — four copies | — | `shader-pipeline` skill |
| buffers, GPU memory | `Render.Helpers.AVulkanBufferHandler` | — | [[mapped-streaming-buffers]], [[engine-resource-manager]] |
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
| app entry, wiring the views | `Carbon.Carbon`, `Carbon.CarbonSettings` | `Carbon/Data/XML/Settings/` | [[carbon-frame-viewer]] |
| the capture session list, Load XML | `Carbon.Editor.CustomControls.SessionListControl`; `Carbon.Editor.CarbonActions` | `Carbon/Data/XML/Documents/UI/UI.ui.xml` | [[carbon-frame-viewer]] |
| frames over time, click to select one | `Carbon.Editor.CustomControls.FrameStripControl` | `UI.ui.xml` | [[carbon-frame-viewer]] |
| flame chart and the aligned timeline, the timeline's scroll bar | `Carbon.Editor.CustomControls.SpanChartControl` (`Mode="Frame"` / `"Timeline"`), `ChartScrollThumbControl` | `UI.ui.xml` | [[carbon-frame-viewer]] |
| zone totals, calls, min/max, counters | `Carbon.Editor.CustomControls.ZoneTableControl` | `UI.ui.xml` | [[carbon-frame-viewer]] |
| data pools in a capture — the table's pools block, the clicked frame's readout | `ZoneTableControl.Pools`; `Carbon.Editor.Comparison.PoolLine` (`Show`) | `UI.ui.xml` (`Pools`) | [[carbon-frame-viewer]] §16 |
| comparing two captures, pinning a baseline | `ZoneTableControl.SetBaseline`; `Carbon.Editor.Comparison`; `Carbon.Editor.CarbonActions` (`Carbon.PinBaseline`, `Carbon.ClearBaseline`) | `UI.ui.xml` | [[carbon-frame-viewer]] §14, §15 |
| second frame strip, slide / swap / scale against the first, which capture the charts show | `Carbon.Editor.Comparison`; `FrameStripControl.SetOffset` / `SetReferenceScale` / `Mark`; `CarbonActions` (`Carbon.SlideLeft`, `Carbon.SlideRight`, `Carbon.SwapStrips`); `ArctisAurora.Core.UI.SliderControl` | `UI.ui.xml` (`CompareStrip`, `Scale`) | [[carbon-frame-viewer]] §15 |

## Facts that cost time to rediscover

- **Thorium is palette-driven; Carbon and the engine's `Settings.ui.xml` are not.** A control with no
  `ColorHex` paints from its `Role` against the nearest `Palette` above it, else the app palette named by
  `<UI><Palette Name>`; an authored `ColorHex` wins. A composite's `*ColorHex` attribute left out means
  "palette". Thorium ships 12 `thorium-*` palettes, picked live in Settings › UI; Carbon still authors every colour.
  See [[ui-palettes]] § Known gaps.
  `ControlColor` names only the 16 enum values in `EnumColorToHex`; everything else is `ColorHex`.
- **Data XML files carry their kind in the filename** — `Bootstrap.bootstrap.xml`, not `Bootstrap.xml`.
  Notes written before that landed still use the short name. See [[xml-type-suffix]].
- **Shaders exist in four copies** — `AuroraEngine/`, `Thorium/`, `AuroraEditor/`, `Carbon/` — and the
  `.spv` must stay byte-identical across them. Edit the `AuroraEngine` copy; the `shader-pipeline`
  skill owns the procedure. Carbon carries only the four the UI path loads (`UIEngine/UIEngine.*` and
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
