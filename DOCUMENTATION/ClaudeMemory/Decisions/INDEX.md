# Decisions — index

One line per note, so "which decision applies here?" is one read instead of a grep and three opens.
Grouped by area; each row states what the note *settles*, so a row that does not match the task means
the note does not need opening. Symbols resolve through `NAMESPACES.md`.

Status is landed unless marked. `PLANNED` = designed, not built — do not describe it as existing code.

Standing constraints (ECS storage, Vulkan internals, physics, XSD-not-JSON) are in
[README.md](README.md), not here. Concept → code lookup is
[../Context/where-things-live.md](../Context/where-things-live.md).

## Look, colour and chrome

| Note | Settles | Key symbols |
|---|---|---|
| [[control-edge-and-outline]] | two strokes, because there are two distance fields to stroke | `VulkanControl`, `UI.vert`/`UI.frag` |
| [[ui-clipping]] | the clip rect rides in the control's pool row; the fragment shader discards against it | `VulkanControl`, `WindowControl`, `UI.frag` |
| [[ui-gradients]] | gradients are a shared table indexed per control, procedural, no texture; slot 0 reserved | `Gradients`, `VulkanControl`, `MCUI`, `UIModule` |
| [[window-chrome-and-label]] | the title bar is ordinary controls; text on a button needed a non-input label | `WindowActions`, `LabelControl` |
| [[window-frame-resize]] | the resize border is a control's padding, not an engine special case | `WindowFrameControl` |
| [[atlas-is-unorm-not-srgb]] | a distance field is not a colour, so font atlases upload as UNORM | `TextureAsset`, `FontAsset`, `AVulkanBufferHandler` |

## Layout, controls and interaction

| Note | Settles | Key symbols |
|---|---|---|
| [[button-states-and-hover-bubbling]] | enter/exit were dead events; a button's visual state is what needed them | `UICollisionHandling`, `VulkanControl`, `ButtonControl` |
| [[n-click-dispatch]] | the release dispatches a tap count and the receiver filters it | `UICollisionHandling`, `VulkanControl`, `TextRun` |
| [[stack-panel-arrange-clamp]] | a stack clamps children in Arrange and keeps offering MaxValue in Measure | `StackPanelControl.Arrange` |
| [[thickness-type-converter]] | `Thickness` parses XML by mirroring its own constructors, not CSS | `VulkanControl.Thickness`, `ThicknessConverter` |
| [[entity-reparenting-and-names]] | reparenting is detach + the new parent's `AddChild`; a name lives on `Entity` | `Entity`, `VulkanControl` |
| [[window-scaling-modes]] | a window root has a windowing mode, and the ortho box is what changes | `WindowControl`, `AuroraCamera`, `EntityRegistry.uiTree` |
| [[ui-data-control-split]] | **SUPERSEDED in approach** — the split was right, the in-place migration was not; see [[ui-engine-stack]] | — |
| [[ui-engine-stack]] | **PARTIAL** — the UI is rebuilt in a new namespace beside the old one; `Control` is CPU, `VulkanControl` is one GPU quad. Landings 1–5 and 6a built: it draws, measures, arranges, takes a per-window slice, dispatches the pointer and the wheel, renders a styled paragraph as one control with a row per glyph, samples images, icons, masks and gradients, and builds a tree from XML. the drag gesture runs press to release against whatever it is over, but tells its claimant nothing; no context menu — built at 6a and removed until the controls that use it land | `UIEngine`, `Control`, `ControlXml`, `ContainerControl`, `WindowRoot`, `TextRunControl`, `NextCaretControl`, `ArrangeData`, `ControlGeometry`, `VulkanControl`, `CornerRadii`, `PointerEvent`, `UIEngineModule` |

## Containers and navigation

| Note | Settles | Key symbols |
|---|---|---|
| [[tab-view-control]] | hiding is a collapsed clip; a closed tab is destroyed, not kept | `TabViewControl`, `TabItemControl`, `VulkanControl` |
| [[tab-rename-and-double-click]] | a double click is dispatched from the release; the renaming strip is its own control | `TabViewControl`, `EditableTabsControl`, `UICollisionHandling` |
| [[splitter-and-pane-sizing]] | a splitter writes one pane's size and the star pane absorbs the rest — or, between two star panes, trades weight across the pair | `SplitterControl`, `StackPanelControl` |
| [[scrollbar-thumb]] | the thumb goes at the head of `children` (hit order is paint order reversed); content gets a gutter | `ScrollableControl`, `ScrollThumbControl` |
| [[scroll-overscroll]] | overscroll is extra range on `MaxScrollOffset`, not a second offset | `ScrollableControl`, `DocumentEditorControl` |
| [[file-browser-tree]] | the file browser is an engine control over a lazy `FileObject` tree | `FileObject`, `FileBrowserControl`, `FileTreeControl` |
| [[context-menu-hosting]] | a context menu hosts itself; where it hosts is a subclass | `ContextMenus`, `ContextMenuControl`, `WindowedContextMenuControl` |
| [[context-menu-invoker]] | a menu entry acts on the control its menu was opened on; a bad name fails the boot | `ContextMenus`, `WindowActions`, `ViewActions` |
| [[inline-rename]] | a row's name is a label that becomes a field; a rename resyncs by walking the tree | `EditableLabelControl`, `TextBoxControl`, `FileRowControl` |
| [[session-restore]] | the session is a settings group, and a declared `Workspace` is where it lands | `SessionLayout`, `WorkspaceControl`, `RenderWindow.uiDocument` |
| [[active-glfw-window-context]] | the active window is a GLFW focus latch; a drag raises what it hovers | `AGlfwWindow`, `RenderWindow`, `UICollisionHandling` |

## Text and documents

| Note | Settles | Key symbols |
|---|---|---|
| [[text-layout-one-measurer]] | one measurer decides every line break; the document is a plain control tree | `TextMeasurer`, `DocumentControl` |
| [[text-styling-types]] | a heading is a value on `ContentBlock`, not a subclass | `ContentBlock`, `stylingType` |
| [[document-format-bar]] | one range-styling primitive, and a bar that never takes the caret | `DocumentToolbarControl`, `StyleRangeEdit`, `ContentBlock`, `TextRun` |
| [[bold-italic-face]] | bold-italic is a fourth baked face; a family missing it falls back to regular | `FontStyle`, `Glyph`, `AtlasMetaData`, `AssetImporter` |
| [[armed-style-at-the-caret]] | a style chosen with nothing selected is armed, not discarded | `DocumentControl`, `DocumentEditorControl`, `TextInputActions` |
| [[document-selection]] | selection is two caret slots; the engine's drag lifecycle was finished to carry it | `CaretSlot`, `SelectionControl`, `VulkanControl.StartDrag` |
| [[caret-blink-and-focus]] | the caret blinks itself, and a run tells the document when focus left | `CaretControl`, `DocumentControl`, `TextRun` |
| [[document-caret-scrolling]] | the caret scroll is requested, and Arrange performs it | `DocumentEditorControl` |
| [[document-structural-editing]] | deletion is one range operation and Enter is its inverse | `DocumentControl`, `DocumentEditorControl`, `TextRun` |
| [[document-undo]] | undo is inverse data records; redo replays the forward operation | `UndoStack`, `EditStep`, `IEditRecord`, `…Document.Edits` |
| [[note-naming-and-text-field]] | a note carries its own name; the engine grew a text field to ask for one | `NoteNameWindow`, `TextBoxControl`, `RichTextDocument` |

## Input

| Note | Settles | Key symbols |
|---|---|---|
| [[engine-side-text-input]] | text input belongs to the engine; keybind timings are global settings | `InputSettings`, `KeyStateTracker`, `TextInputActions` |
| [[named-input-modifiers]] | a modifier is a named role, so engine code never names the key that fills it | `InputModifier`, `NamedModifier`, `GestureMatcher` |
| [[declared-contexts]] | contexts are declarable in XML and one can derive from another | `Context`, `ContextDefinition`, `UICollisionHandling` |

## Data, assets and settings

| Note | Settles | Key symbols |
|---|---|---|
| [[asset-manifest-and-import]] | assets are declared in manifests, cooked by importers, resolved through the VFS | `AssetRegistries`, `AssetImporter`, `Paths`, `VirtualFileSystem` |
| [[ui-document-registry]] | a UI document is a registry asset; assigning a window's root destroys the old one | `UIDocumentAsset`, `VulkanControl.ParseXML`, `UIModule` |
| [[settings-registry]] | settings are reflected groups, cascaded per attribute, saved as a diff | `SettingsRegistry`, `ISettingsGroup`, `UserSettingsFile` |
| [[settings-categories]] | a setting is its own type, so it is its own element with typed attributes | `Setting`, `SettingScope`, `SettingCategory` |
| [[ecs-rework-data-pools]] | **PARTIAL** — OO components become pooled columns; handles, compaction, command lanes | `DataPool`, `DataManager`, `PoolColumn`, `TransformData` |
| [[cross-system-change-notification]] | no bus, no subscribers — consumers poll a per-pool version | `DataPool` version, `ThreadedSystem` |
| [[asset-pipeline-bake]] | **FUTURE** — the pak is a pre-concatenated cook cache; GPU offsets assigned, never baked | `AssetRegistries`, `AVulkanMesh` |
| [[world-streaming-prefetch]] | **FUTURE** — streaming predicts by reachability, not visibility; runtime grid over baked PVS | — |

## Serialization and XML

| Note | Settles | Key symbols |
|---|---|---|
| [[xml-type-suffix]] | a data XML file names its kind: `[name].[type].xml` | all `*/Data/XML/**` and their loaders |
| [[xml-save-skips-defaults]] | a value equal to its default is not written, and that is intended | `XmlReflection.ScalarMembers` |
| [[xsd-generator-cross-category]] | cross-category type refs need `xs:import`; a dangling ref voids the whole schema | `XSDGenerator` |

## Engine core and lifecycle

| Note | Settles | Key symbols |
|---|---|---|
| [[shutdown-sequence]] | shutdown is the bootstrap sequence run backwards, in two phases; only `Request` may refuse | `Shutdown`, `Bootstrapper`, `NoteActions` |
| [[entity-lifecycle-queues]] | lifecycle drains through queues popped between frames, not `foreach` over live lists | `Engine.Interpolate`, `EntityRegistry` |
| [[entity-transform-split]] | an entity's columns are what its pool declares; the transform moved down to `TransformEntity` and an entity frees rows in every pool it holds | `Entity`, `TransformEntity`, `EntityComponent`, `EntityRegistry` |
| [[entity-tick-group]] | **FUTURE** — ticking should iterate a `"Tickable"` group, not every entity behind a flag | `Engine.Interpolate`, `EntityRegistry` |
| [[engine-logging]] | per-thread SPSC lanes drained by one background thread; a log call is a memory write | `LogChannel`, `LogLane`, `Diagnostics.Sinks.*` |
| [[engine-profiling]] | zones time and increments count, both compiled out by flag; per-thread tables, no shared state; a capture streams every span of every frame to XML off-thread, and `--profile` arms one early enough to hold the bootstrap phase | `Profiling`, `FrameSpool`, `ThreadedSystem.Loop`, `Bootstrapper.RunPhase` |
| [[carbon-frame-viewer]] | the frame reader sits beside the writer; Carbon is a fourth app drawing it, and one control is both the flame chart and the aligned timeline | `FrameCaptureReader`, `SpanChartControl`, `FrameStripControl` |
| [[winforms-to-console]] | the engine assembly is a plain console app; WinForms is off | `AuroraEngine.csproj`, `Program`, `Engine` |

## Rendering and Vulkan

| Note | Settles | Key symbols |
|---|---|---|
| [[render-window-owns-the-swapchain]] | a window owns its swapchain, sync and modules; the renderer keeps only the device | `RenderWindow`, `Renderer`, `AGlfwWindow` |
| [[swapchain-extent-is-the-truth]] | GPU-side sizes come from `Renderer.swapchainExtent`, never `Engine.window.windowSize` | `Renderer`, `RenderingModule`, `UIModule` |
| [[dynamic-rendering]] | dynamic rendering replaces render passes and framebuffers | `Renderer`, `RenderingModule`, `CompositorModule` |
| [[gpu-global-frame-data]] | the renderer owns a global frame-data set at set 0; modules keep their own from set 1 | `Renderer`, `UIModule`, `MCUI`, `UI.vert` |
| [[mapped-streaming-buffers]] | per-frame buffers are mapped and per-image; staging is for upload-once data; never read back | `AVulkanBufferHandler`, `Renderer`, `MCUI` |
| [[glyphs-as-pool-data]] | glyphs stay controls; the sampler array becomes a texture table indexed per control | `UIModule`, `ControlData`, `TextureAsset` |
| [[engine-resource-manager]] | **FUTURE** — one manager owns asset-backed memory; GPU memory is arenas, not malloc/free | `AVulkanBufferHandler`, `RenderingModule` |

## Thorium

| Note | Settles | Key symbols |
|---|---|---|
| [[vault-browser-and-shell]] | the vault is a settings path; the browser finds the editor by walking the tree | `ThoriumSettings`, `VaultBrowserControl`, `FileObject` |
| [[vault-list-and-switching]] | the vault list is a settings group; the screen switches by writing the setting | `VaultsWindow`, `MenuScreen`, `FolderPicker` |

## Keeping this current

A new `Decisions/` note adds a row here in the same commit — the `aurora-docs` skill carries the step.
Rows do not restate the note; if a row needs a second sentence, the note's title is the thing to fix.
