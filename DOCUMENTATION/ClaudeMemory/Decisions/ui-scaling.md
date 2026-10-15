# Decision — the UI scales by display scale × zoom; documents zoom separately by re-layout

**Date:** 2026-09-27 (wheel zoom and sheet/planner zoom 2026-10-11)
**Scope:** `ArctisAurora.Core.UI` — `WindowRoot.scale`, `UIScaling`, `ZoomSetting`, `DocumentZoomSetting`, `DocumentControl.zoom`, `BlockControl.SetZoom`, `TextRunControl.textZoom`, `DocumentEditorControl.Rezoom`, `UIEngine.ZoomContent`, `SheetControl.SetZoom`, `PlannerEditorControl.SetContentZoom`, `GanttChartControl`/`CalendarControl`/`PlannerBoardControl.Rezoom`; `ArctisAurora.EngineWork.Rendering.AGlfwWindow` (`InitGlfw`, `contentScale`, `PrimaryContentScale`, `OnContentScale`); `ArctisAurora.EngineWork.Engine.InitWindowing`; `AuroraEngine/Data/XML/Documents/Bootstrap.bootstrap.xml`; `*/Data/XML/Documents/Inputs/InputMap.inputs.xml`

## What changed
- **UI scale** = `AGlfwWindow.contentScale` (GLFW window content scale = the OS display scale of that monitor) × `<UI><Zoom Percent>` / 100 (float, clamped 50–300). `UIScaling.For(window)`.
- `WindowRoot.scale`: the non-autoscaling `ViewportSize` returns `window pixels / scale`. `ToDesignSpace`, the projection (`AuroraCamera`), `DragGhost.PreviewSize` and context-menu placement already went through that ratio, so they follow with no change.
- `UIEngineModule.uiRoot` setter assigns `scale` before `FitTo`. `UIScaling.Apply(window)` re-sets it and re-fits; `UI.Rescale` (the Zoom setting's `OnChanged`) applies it to every window.
- Windows opened from a design size convert with `UIScaling.ToPixels(source, design)`: `SettingsWindow`, `MenuScreen`, `ConfirmWindow`, `NoteNameWindow`, `ContextMenus.HostInWindow`, `TabViewControl.TearOff`. `ConfirmWindow`/`NoteNameWindow` are built once and now resize + `Apply` on every `Ask`, so a zoom change after the first open reaches them. `FitTo(new Extent2D(design…))` calls became `FitTo(window.os.windowSize)`.
- First window: `Engine.InitWindowing` sizes it at settings size × `PrimaryContentScale()` × zoom. A restored session layout still uses its recorded pixels.
- Input actions `UI.ZoomIn`/`UI.ZoomOut` (±10) and `UI.ZoomReset`, bound Ctrl+= / Ctrl+− / Ctrl+0 in Thorium, Carbon and AuroraEditor.
- Wheel zoom (2026-10-10, widened 2026-10-11): `UIEngine.SolveScroll`, when `InputModifier.Zoom` (Left/RightControl) is held, never dispatches the scroll. With `InputModifier.AppZoom` (Left/RightShift) also held it calls `UIScaling.ZoomIn`/`ZoomOut` (Ctrl+Shift+wheel = app zoom). Without it, it calls the private `ZoomContent(larger)`, which walks up from `hovering`: a `DocumentEditorControl` with `zoomOverride` set steps its enclosing `TexEditorControl.SetPreviewZoom(±10, clamped 25–400)`; otherwise the first `DocumentEditorControl` / `SheetEditorControl` / `PlannerEditorControl` ancestor steps `DocumentEditorControl.ZoomIn`/`ZoomOut` (Ctrl+wheel = content zoom, ±10 on the global `DocumentZoom`, 25–400, then `SettingsRegistry.Apply`); off any editor plain Ctrl+wheel still does nothing. `OverDocument()` is gone. Roles are bound in Thorium's input map. Ctrl+= / − / 0 stay app zoom. A Tex preview with its own zoom ignores the setting, so Ctrl+wheel over it steps that override (its caption follows).
- **GLFW starts in its own bootstrap step**, `Glfw.Init` (`AGlfwWindow.InitGlfw`), right after `Logging.Configure`. It returns false on failure, which halts boot. The constructor's `GetApi` and `CreateWindow`'s `Init`/`Terminate` are gone.
- `glfwGetWindowContentScale` / `glfwSetWindowContentScaleCallback` are called through function pointers from `_glfw.Context.GetProcAddress`. The callback is an `[UnmanagedCallersOnly]` static that finds the window by handle in `Engine.windows` and posts `UIScaling.Apply`.
- **Document zoom** = `<UI><DocumentZoom Percent>` (float, clamped 25–400), `OnChanged` `Document.Rezoom`, which walks every window's tree for `DocumentEditorControl`s and, since 2026-10-11, also calls `SheetControl.SetZoom(DocumentZoom)` and `PlannerEditorControl.SetContentZoom(DocumentZoom)` on every one it reaches (`DocumentZoom` is now internal); hidden tabs are still children, so they are reached.
  - `DocumentControl.zoom` multiplies paper, margins (`Mm()`), gap and block spacing, and pushes itself into every block each measure (`BlockControl.SetZoom`), so blocks made by Enter or undo pick it up.
  - `TextRunControl.textZoom` multiplies every run's font size in `BuildRuns` (rounded to int); stored and shown sizes are untouched.
  - `BlockControl.SetZoom` also scales the list indent and the marker (`SizeMarker`): the bullet dot directly, a task checkbox through `CheckBoxControl.SetScale` (box 18, mark 10, corners 3/2, all × scale).
  - Sheets and the planner re-lay at the same zoom (2026-10-11): each view multiplies its own design sizes and font sizes, fonts as `max(1, round(size × zoom))`. `SheetControl` has `zoom` (from `DocumentEditorControl.DocumentZoom` at construction), `SetZoom`, public zoomed `HeaderWidth`/`HeaderHeight` and zoomed wrappers over `SheetPage`'s column/row metrics; the public `headerWidth`/`headerHeight` consts stay as design values. `PlannerEditorControl.contentZoom` / `SetContentZoom` calls `Rezoom()` on `GanttChartControl`, `PlannerBoardControl` and `CalendarControl` (board `Rezoom` = rebuild). `zoom` on `PlannerEditorControl` is still the Hour/Day/Week/Month time scale. `DayWidth` is also × zoom.
  - Stored sizes stay at 100%: sheet column widths and row heights, board column widths and the Gantt name column are stored in design px and drawn × zoom; a resize drag divides the pointer delta by the zoom before storing (a 60 px drag at 200% stores 30).
  - Not scaled: grab/edge hit widths and drag thresholds (pointer-feel distances), hairline/outline widths, alpha values. The planner toolbar and popups are app chrome and stay on app zoom.
  - `TestContext.Scroll` gained `Keys[]? held` (keys held around the wheel turn). Tests: `TextInput.WheelZoom`, `Sheet.WheelZoom`, `Planner.WheelZoom`, `TabTools.PreviewWheelZoom`.

## Why these choices

**Display scale comes from GLFW's content scale, not from resolution.**
It is the user's own OS setting, per monitor. A 1440p/4K panel left at 100% gets no change, and zoom covers that.

**Document zoom re-lays the text at the new size instead of scaling a finished layout.**
User call: layout-time scaling is what the user asked for. Text is MTSDF, so a draw-time transform would not actually have pixelated; the real costs of that route were a subtree transform through emit, clip and hit-test. The cost of this route: integer font sizes, so type grows a little unevenly and line breaks can differ between zoom levels.

**Notes, sheets and the planner share the one `DocumentZoom` setting (2026-10-11).**
User call. Rejected: a per-editor-kind setting (three Settings rows), and a per-tab zoom saved with the session (notes would have had to leave the global setting).

**Sheets and the planner re-lay per view, the way notes do.**
User call, consistent with the re-layout choice above. Rejected: a generic subtree scale applied at draw/clip/hit-test, which would be one mechanism for every editor but is the route already rejected for notes and changes the UI core.

**Ctrl+wheel over a Tex preview with its own zoom steps that override.**
The preview ignores the shared setting, so stepping the setting would change nothing visible; `SetPreviewZoom` moves its caption too.

**Root cause of the reported "Ctrl+wheel does nothing" (2026-10-11).**
The 2026-10-10 change made plain Ctrl+wheel note-only, and the user's sessions that day had a planner tab and a sheet tab open, so nothing happened. The note path itself worked: the log showed `Document.Rezoom` never ran in those sessions, and a synthetic test over a note passed.

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
- Wheel zoom over sheets, planner and Tex preview is test- and golden-verified only; **NOT GUI-verified**: real touchpad pinch and real mouse Ctrl+wheel in a running Thorium.
- Pinch arrives as Ctrl+wheel with small fractional deltas; each frame with any nonzero delta steps a full 10%, so pinch may zoom fast. Not tuned.
- Restored session scroll offsets are in pixels, so reopening at a different zoom lands at a different place.
- The Gantt/Calendar/Board/Sheet toolbars and the ticket/category popups do not scale with content zoom (by design: app chrome).
- Separate bug seen in the 2026-10-11 log, not addressed: after a Ctrl+Shift app zoom with a sheet tab open, `[UIEngine] subtreeBounds … != recomputed` and `[Layout] skipped layout left 12 rows stale` ERRORs (also once at startup with a planner tab: subtreeBounds (0,0,0,0)).

Related: [[document-pages]], [[window-scaling-modes]], [[settings-registry]], [[ui-engine-stack]]
