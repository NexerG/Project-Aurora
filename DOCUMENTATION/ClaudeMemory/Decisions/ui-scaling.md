# Decision — the UI scales by display scale × zoom; documents zoom separately by re-layout

**Date:** 2026-09-27
**Scope:** `ArctisAurora.Core.UI` — `WindowRoot.scale`, `UIScaling`, `ZoomSetting`, `DocumentZoomSetting`, `DocumentControl.zoom`, `BlockControl.SetZoom`, `TextRunControl.textZoom`, `DocumentEditorControl.Rezoom`; `ArctisAurora.EngineWork.Rendering.AGlfwWindow` (`InitGlfw`, `contentScale`, `PrimaryContentScale`, `OnContentScale`); `ArctisAurora.EngineWork.Engine.InitWindowing`; `AuroraEngine/Data/XML/Documents/Bootstrap.bootstrap.xml`; `*/Data/XML/Documents/Inputs/InputMap.inputs.xml`

## What changed
- **UI scale** = `AGlfwWindow.contentScale` (GLFW window content scale = the OS display scale of that monitor) × `<UI><Zoom Percent>` / 100 (float, clamped 50–300). `UIScaling.For(window)`.
- `WindowRoot.scale`: the non-autoscaling `ViewportSize` returns `window pixels / scale`. `ToDesignSpace`, the projection (`AuroraCamera`), `DragGhost.PreviewSize` and context-menu placement already went through that ratio, so they follow with no change.
- `UIEngineModule.uiRoot` setter assigns `scale` before `FitTo`. `UIScaling.Apply(window)` re-sets it and re-fits; `UI.Rescale` (the Zoom setting's `OnChanged`) applies it to every window.
- Windows opened from a design size convert with `UIScaling.ToPixels(source, design)`: `SettingsWindow`, `MenuScreen`, `ConfirmWindow`, `NoteNameWindow`, `ContextMenus.HostInWindow`, `TabViewControl.TearOff`. `ConfirmWindow`/`NoteNameWindow` are built once and now resize + `Apply` on every `Ask`, so a zoom change after the first open reaches them. `FitTo(new Extent2D(design…))` calls became `FitTo(window.os.windowSize)`.
- First window: `Engine.InitWindowing` sizes it at settings size × `PrimaryContentScale()` × zoom. A restored session layout still uses its recorded pixels.
- Input actions `UI.ZoomIn`/`UI.ZoomOut` (±10) and `UI.ZoomReset`, bound Ctrl+= / Ctrl+− / Ctrl+0 in Thorium, Carbon and AuroraEditor.
- Ctrl+wheel: `UIEngine.SolveScroll` calls `ZoomIn`/`ZoomOut` by the wheel's Y sign when `InputModifier.Zoom` is held, and the scroll is not dispatched. One step per frame. The role is bound to Left/RightControl by `<NamedModifier Modifier="Zoom">` in Thorium's input map.
- **GLFW starts in its own bootstrap step**, `Glfw.Init` (`AGlfwWindow.InitGlfw`), right after `Logging.Configure`. It returns false on failure, which halts boot. The constructor's `GetApi` and `CreateWindow`'s `Init`/`Terminate` are gone.
- `glfwGetWindowContentScale` / `glfwSetWindowContentScaleCallback` are called through function pointers from `_glfw.Context.GetProcAddress`. The callback is an `[UnmanagedCallersOnly]` static that finds the window by handle in `Engine.windows` and posts `UIScaling.Apply`.
- **Document zoom** = `<UI><DocumentZoom Percent>` (float, clamped 25–400), `OnChanged` `Document.Rezoom`, which walks every window's tree for `DocumentEditorControl`s; hidden tabs are still children, so they are reached.
  - `DocumentControl.zoom` multiplies paper, margins (`Mm()`), gap and block spacing, and pushes itself into every block each measure (`BlockControl.SetZoom`), so blocks made by Enter or undo pick it up.
  - `TextRunControl.textZoom` multiplies every run's font size in `BuildRuns` (rounded to int); stored and shown sizes are untouched.
  - `BlockControl.SetZoom` also scales the list indent and the marker (`SizeMarker`): the bullet dot directly, a task checkbox through `CheckBoxControl.SetScale` (box 18, mark 10, corners 3/2, all × scale).

## Why these choices

**Display scale comes from GLFW's content scale, not from resolution.**
It is the user's own OS setting, per monitor. A 1440p/4K panel left at 100% gets no change, and zoom covers that.

**Document zoom re-lays the text at the new size instead of scaling a finished layout.**
User call: layout-time scaling is what the user asked for. Text is MTSDF, so a draw-time transform would not actually have pixelated; the real costs of that route were a subtree transform through emit, clip and hit-test. The cost of this route: integer font sizes, so type grows a little unevenly and line breaks can differ between zoom levels.

**Both zooms are float percentages in the Settings window, with no toolbar control and no presets.**
User call. The Settings window renders any float setting as a field, so neither needed UI code.

**The two missing GLFW functions are called through Silk's own loaded library.**
Silk.NET.GLFW 2.21 binds neither. The newest release (2.23.0) doesn't either, and Silk's `main` has only the getter, never the callback. `Context.GetProcAddress` resolves from the same module Silk loaded, so there is one GLFW instance, not a second copy found by `DllImport`.

**GLFW init is its own step, not a side effect of opening the first window.**
User call. It lets `InitWindowing` ask for the monitor's scale before the window exists.

**Hairlines snap to device pixels (2026-10-09).** `UIScaling.Snap` / `Hairline` — see [[ui-palettes]] § Contrast floors. Only the sheet grid uses them so far.

## Known gaps
- A display scale other than 100% is **not verified**; the test machine is one 1920×1080 at 100%. Moving a window between monitors of different scales has never been exercised.
- The first window uses the **primary** monitor's scale even when `GraphicsSettings` names another monitor; the callback fixes the contents once it lands, not the window size.
- A menu or dialog window reads its own content scale where GLFW creates it (hidden, default position) and is sized from its source's. On mixed-DPI setups the two can differ until it moves and the callback fires.
- At 200% document zoom the bullet dot drew near-white against the page. Its colour path was not touched by zoom and was not investigated.
- The F10 tree dump is in design units, so it only equals client pixels at scale 1.
- AuroraEditor's input map still uses the old `Button`/`State` shape beside the new entries; AuroraEditor was not booted.

Related: [[document-pages]], [[window-scaling-modes]], [[settings-registry]], [[ui-engine-stack]]
