# Decision — a workspace is a code-only page inside WorkspaceControl

**Date:** 2026-10-08
**Status:** LANDED (W2, W3, W4a and W4b of `../Context/workspace-plan.md`). W2 test-verified and shot-verified; W3/W4 test-verified and golden-verified. **NOT GUI-verified.**
**Scope:** `ArctisAurora.Core.UI` — `WorkspaceControl`, `WorkspacePageControl`, `WorkspaceKind`, `WorkspaceBarControl`, `WorkspaceActions`, `SessionLayout` (`SessionWorkspace`, `ISessionWindowChild`), `PlannerBook`, `PlannerDocument`, `SheetDocument`, `PlannerEditorControl`, `SheetEditorControl`, `TabActions`, `TabViewControl`; `Thorium.Editor.CustomControls.VaultBrowserControl`; `UI.ui.xml`, `Workspace.menu.xml`, `WorkspaceAdd.menu.xml`, `ThoriumAssets.assets.xml`; W3/W4: `TabToolsControl`, `NoteToolsControl`, `RibbonControl`, `InspectorControl`, `DocumentToolbarControl`, `SplitterControl`, `DocumentControl`, `DocumentEditorControl`, `SheetEditorControl`, `TextInputActions`, `IFileEditor`, `ArctisAurora.Core.Filing` (`FileDialog`, `FilePicker`, `FolderPicker`), Thorium `PaneNew.menu.xml`

## What changed
- `WorkspaceKind` enum `[A_XSDType("WorkspaceKind","UI")]`: General, Docs, Sheets, LaTeX, Manager, Calendar.
- `WorkspacePageControl` (code-only, no XML; `PanelControl`): `title`, `kind`, `content` (the one pane tree), `isShown`, internal `lastFocused`, static `Of(Control)` (nearest page above a control).
- `WorkspaceControl` children are pages. `shown`, `Pages`, `AddPage(title, kind)` (first page shown, later hidden), `Show(page)`, `Rename(page, title)`, `RemovePage(page)` (refuses the last; shows the neighbour if the shown one went), static event `changed(WorkspaceControl)`. `OnChildDetached` clears `shown`. `MeasureCore`/`ArrangeCore` lay out only `shown`.
- `Show(page)`: records the old page's focused `TabViewControl` in `lastFocused`, hides it, shows the new one, sets `ActiveControl` to the new page's last-focused pane's editor, else its first pane.
- XML `FirstRun` (field `firstRun`): space-separated kinds a window starts with when nothing was saved; General alone when left out. `LoadDefault()` puts `Default` into a page of the first kind and an empty pane into each further one. `LoadEmpty()`: every `FirstRun` kind, each one empty pane. `LoadPane()` keeps its signature (shown page, or a new General page); `LoadPane(page)` is new.
- `WorkspaceBarControl` `<WorkspaceBar>` (StackPanel): one nested `WorkspaceButton` (`ButtonControl`; `page`, `caption` `EditableLabelControl`) per page of the Workspace in its own window. Shown tab paints Ground with `AccentRole.Tab`, others Clear with muted ink. Press shows, double-click renames in place, right-click opens `TabContextMenu` (default "workspace"). Rebuilds on `WorkspaceControl.changed` through `Engine.Post`. Static `BeginRename(page)`.
- `WorkspaceActions` (category "UI"): `Workspace.NewGeneral/NewDocs/NewSheets/NewLaTeX/NewManager/NewCalendar` (page with one empty pane, shown), `Workspace.Duplicate` (via `SessionLayout.Duplicate`), `Workspace.Rename`, `Workspace.Close` (closes the page's tabs one at a time through `TabViewControl.CloseTab(item, onClosed)`, then `RemovePage`; nothing when it is the last page). Target: the `WorkspaceButton` the menu opened on, else the shown page of the menu's window (falls back to the primary).
- `SessionLayout`: `SessionWorkspace` (`Title`, `Kind`, `Shown`, `SessionPane` list); interface `ISessionWindowChild` (implemented by `SessionPane` and `SessionWorkspace`; `SessionWindow` `AllowedChildren` is now this); `SessionWindow.workspaces`; `SessionWindow.panes` is read only from records written before workspaces, never written. `Record` writes one `SessionWorkspace` per page (private `PanesOf(page)`). `Fill`: workspaces → one page each, the `Shown` one shown; an old record with panes and no workspaces → one General page; nothing → `LoadDefault`. Private `FillPage`, public static `Duplicate(workspace, page)`. `ChangeScope` fallback `LoadPane()` → `LoadEmpty()`.
- `PlannerBook` (static, in `PlannerDocument.cs`): `Open(path)`, `Close(path, document)`, `Renamed(from, to)`; one `PlannerDocument` per path, count on internal `PlannerDocument.views`. `PlannerDocument.unsaved` and `SheetDocument.unsaved` are new; `PlannerEditorControl.unsaved` and `SheetEditorControl.unsaved` read them. `PlannerEditorControl.LoadPath` opens through `PlannerBook`; private `Release()` (unsubscribe, `PlannerBook.Close`) runs from `Load` and `OnDestroy`.
- `TabActions.Split`: any tab whose editor has a path gets a second view (was notes and `.tex` only). `TabViewControl.CloseTab(item, onClosed)` private → internal.
- `VaultBrowserControl`: field `filtered` (shown kind); region `workspace filter` (`OnWorkspaceChanged` re-filters, sets label `VaultFilter`, `Rebuild`; `Shows(kind, path)`; `FilterName`). `Shows`: Docs = notes that are not sheets/CSV/planners/.tex; Sheets = sheets + CSV; LaTeX = .tex; Manager and Calendar = planners; General = all. Names: All files / Notes / Sheets / LaTeX / Plans. `Accepts` adds `Shows`. `Open` reuses a tab only from a shown workspace (a file open in a hidden one gets a second view in the shown one) and falls back to `ShownTabs()` (first pane of the shown page; replaces `FindByName("Tabs")`, constant `tabsName` removed). A planner opened while the shown kind is Calendar starts in `PlannerView.Calendar`. `FocusedTabs` also requires the view's page to be shown. `RenameNote` calls `PlannerBook.Renamed`.
- Data: `UI.ui.xml` title bar gains a 1 px separator, `<WorkspaceBar Height="26"/>` and a 22×22 `MenuButton ContextMenu="workspace-add"` ("+"); sidebar gains a 24 px header row (`Label Name="VaultFilter"`); `<Workspace ... FirstRun="General Docs Sheets LaTeX Manager Calendar"/>` (every kind, user 2026-10-08). `Workspace.menu.xml` (Rename, Duplicate, Close) and `WorkspaceAdd.menu.xml` (New General/Docs/Sheets/LaTeX/Manager/Calendar workspace, Duplicate current, Rename...) are registered as `workspace` and `workspace-add`.
- Tests: `WorkspaceTests.cs` + `Workspace.tests.xml` (`Workspace.Switch`, `Workspace.Menu`, `Workspace.SessionRoundTrip`, `Workspace.LegacySession`); `Planner.TwoViews` in `PlannerTests.cs`.

### W3 — tab-row tools (built in an earlier session, written up 2026-10-08)
- Each pane's tab row carries the active editor's tools; the 30 px `DocumentToolbar` band is gone from Thorium's `UI.ui.xml` and `TabWindow.ui.xml`.
- NEW `TabToolsControl` (StackPanel of 20 px tool buttons): `Icon`, `Text(text, press, drops)`, `Separator`, static `Light`, static `Drop`; nested `TabToolButton` acts on press and never takes the active control.
- NEW `NoteToolsControl`: B I U, align left, bullets, Paste link.
- `IFileEditor.tools` (default null). Implemented by `DocumentEditorControl.tools`, `SheetEditorControl.tools` (B, Format menu, Fill menu, Paste link, Sum), `TexEditorControl.tools` (Build, Live, error count, Sync, zoom), `PlannerEditorControl.tools`.
- `TabViewControl.SyncTools()` mounts the active editor's tools at the right of the tab row.
- `SheetEditorControl.AutoSum()`; sheet formula bar (24 px: cell name, fx, formula field) in `SheetEditorControl`.
- Pane "+": Thorium `VaultBrowserControl.NewInPane(view, button)` — Docs → note, Sheets → sheet, LaTeX → `.tex`, Manager and Calendar → planner, General → menu `pane-new` (`PaneNew.menu.xml`, registered in `ThoriumAssets.assets.xml`).
- Tests: `TabTools.Mount`, `TabTools.Bullets`, `TabTools.AutoSum`, `TabTools.FormulaBar`, `TabTools.Tex` (`TabToolsTests.cs`, `TabTools.tests.xml`).

### W4a — ribbon and inspector
- NEW `RibbonControl` `<Ribbon>` `[A_XSDType("Ribbon","UI")]`: hidden outside Docs and Sheets. Target = the active editor of the shown workspace's focused pane: internal static `FocusedView(page)`, `Target(page)`, `Covers(view, editor)`. Categories per workspace in `WorkspacePageControl.ribbonCategory`; the inspector toggle flips `WorkspacePageControl.inspectorShown`. XML `ConvertCsv` (Action). Helpers shared with the inspector: `MarginEntries`, `MarginSides`, `MarginField`, `NumberEntries`, `NumberCaption`, `Swatch`, `SizeCaption`, `Mm`, `LightCaption`.
- Docs Format = the existing `DocumentToolbarControl`. Docs Insert = Table, Picture…, Formula, Display formula, Sheet link, Code block, Quote, Divider. Docs Page = Paged/Pageless, Size dropdown, Portrait/Landscape, Margins (Normal 25.4 / Narrow 12.7 / Wide 50.8 mm presets + T/B/L/R fields), Page numbers.
- Sheets Format = Bold, Format dropdown, $ % 0.00, 7 fill swatches. Sheets Insert = fx Formula (focuses the formula bar), Sum, Paste link, Copy reference, Page, Layer. Sheets Data = Import CSV…, Export page as CSV, Convert CSV to sheet.
- NEW `InspectorControl` `<Inspector>` `[A_XSDType("Inspector","UI")]`: Paragraph (Style, Align, List None/Bullets/Numbers/Tasks), Page (Layout, Size "A4, 210 x 297", Orientation, Margins mm fields, Numbers), Cell (header names the cell, Format, Fill, Bold, Reference shown as `Page!Cell` + Copy), Layers (hosts `SheetLayersControl`). Hides itself and the `SplitterControl` right before it.
- `DocumentToolbarControl`: NEW `public Func<DocumentEditorControl?>? target`; reordered to Style | B I U S | colour, highlight | px | align ×4 | bullets + marker chevron, numbered, task, outdent, indent; page button removed (Page is a ribbon category); `OpenPage` removed; NEW internal `StylingEntries`, `MarkerEntries`, `SizeEntries`, `PageEntry`, `ChangePage`, `CaptionFor`.
- `WorkspacePageControl.ribbonCategory` (string, "Format"), `inspectorShown` (bool, true). `SessionWorkspace` NEW attributes `Ribbon`, `Inspector`; restore, capture and `SessionLayout.Duplicate` carry them.
- `TabViewControl.SyncTools` mounts nothing when `RibbonControl.Covers`.
- `TextInputActions`: NEW `Text.Strikethrough`, `Text.Numbers`, `Text.Tasks`, `Text.InsertPicture` (Input actions, no keybinds).
- `SheetEditorControl.On` private → internal; NEW `FocusFormula()`.
- `SplitterControl`: NEW field `grabNext` — between a star pane and a sized pane after it, the drag writes the after-pane's size (inverted delta).
- Thorium `UI.ui.xml`: `<Ribbon Height="30" ConvertCsv="Sheets.ConvertOpenCsv"/>` under the TitleBar; `<Splitter Width="3"/><Inspector Width="240"/>` after the Workspace column.
- Tests: `RibbonTests.cs` (`Ribbon.Shows`, `Ribbon.Docs`, `Ribbon.Sheets`, `Inspector.Docs`, `Inspector.Sheets`, `Ribbon.Session`), suite `Ribbon.tests.xml`; goldens `Ribbon.Docs.Format`, `Ribbon.Sheets.Format`, `Inspector.Docs.Panel`, `Inspector.Sheets.Panel`.

### W4b — entries that needed new logic
- `DocumentControl.ToggleList(ListKind, ListMarker?)` NEW; `ToggleBullets()` now calls it. Numbered = `ListKind.Bullet` + `ListMarker.Decimal`; "already that list" compares kind and numbered-ness. `DocumentEditorControl.ToggleNumbers()`, `ToggleTasks()` (undo steps "Numbered list", "Task list").
- `DocumentEditorControl.InsertPictureFile(source)` NEW: copies the file unchanged into the note's `attachments/` (name collisions get " (2)"), then inserts through `content.PasteImage`; `.txt` and `.tex` refused. `PasteImage` now shares private `AttachmentsFolder(verb)` and `PutPicture(file, label)`.
- `ArctisAurora.Core.Filing`: NEW internal `FileDialog` (the IFileDialog interop; one dialog at a time, STA thread, answer via `Engine.Post`; real `SetFileTypes` signature with `FilterSpec`). `FolderPicker` is now a thin wrapper over it. NEW `FilePicker.Pick(owner, startFolder, filters, onPicked)`.
- `SheetEditorControl.ImportPage(csvPath)` NEW: `SheetCsv.Load(path).pages[0]`, named after the file (a free "Sheet n" name when taken or invalid), one undo step "Import CSV", page shown.
- Thorium `VaultBrowserControl`: `SheetFromCsv(FileObject, bool)` → `SheetFromCsv(string csvPath, bool replace)`; NEW action `Sheets.ConvertOpenCsv` (UI) converts the focused tab's CSV. `OnWorkspaceChanged` returns unless the browser is still in `Engine.primary`'s tree (see [[destroyed-parent-orphans]]).
- Icons: 20 NEW filled SVGs in the default set (strikethrough, list-numbered, list-task, outdent, indent, panel-right, table, picture, sigma, sigma-display, link, code, quote, divider, copy, plus, layers, import, export, convert). Atlas rebaked (44 icons), copied byte-identical to AuroraEngine, Thorium, AuroraEditor and Carbon `Data/Icons/default`.
- Tests: `RibbonEntriesTests.cs` (`Note.NumbersToggle`, `Note.TasksToggle`, `Note.InsertPictureFile`, `Sheet.ImportCsv`, `Ribbon.CsvEntries`).

### Reset UI (2026-10-08)
- `SessionLayout.Reset()` + action `Session.Reset` (Thorium app menu "Reset UI"): every workspace back to `FirstRun`, torn-off windows closed, stickies kept. Details: [[session-restore]] §9.

### Workspace tab drag (2026-10-08)
- `WorkspaceControl.Move(WorkspacePageControl page, int index)` NEW — index is a gap among the bar's tabs (0..count). Same workspace: reorders children (a gap past the old slot is decremented), raises `changed`. From another workspace: refused when the source is on `Engine.primary` and has one page; otherwise RemoveChild from source, AddChild here, insert at the gap, `Show(page)`, then the source's NEW private `Released(page, index)` — clears `shown` if it was the page, closes the source window via `Engine.CloseWindow` when it is now empty and not the primary, else shows the neighbour at the old index and raises `changed`. `Released` exists because `OnChildDetached` only fires on Destroy, not on RemoveChild/SetParent.
- `WorkspaceBarControl`: NEW XML property `TearOffDocument` (`tearOffDocument`); NEW region `drop` — `DraggingOverStart`/`DraggingOver` (place marker), `DraggingOverEnd`, `FinishDrag` (calls `workspace.Move(tab.page, GapAt(point))`), private `ShowMarker`, `HideMarker`, `GapAt` (count of tabs whose centre is left of the point). Marker field `marker`: a `PanelControl`, role Accent, 2x16, not hit-testable, reset in `Rebuild`.
- `WorkspaceBarControl.TearOff(WorkspacePageControl page)` NEW (`internal unsafe`) — mirrors `TabViewControl.TearOff`: opens a 900x640 window named `workspace-N` at the pointer from `tearOffDocument`, no `LoadDefault`, then `Move(page, 0)`; refuses when the page is its window's only workspace.
- `WorkspaceButton`: NEW internal field `bar`; press/move threshold drag (12 px, `StartDrag` + `DragGhost.Show`; does not arm while `caption.isEditing`). `OnDrag` samples `UIEngine.mouseOverWindow != null` each tick. `OnDragStop` hides the ghost, tears off when not accepted and the pointer was outside every window, else shows the page.
- `WindowActions`: NEW action `Window.MinimizeToTray` -> `Background.Hide()` (always the primary). NEW icon `tray.svg` (tray with down arrow) in the default set; atlas rebaked to 45 icons.
- Data: `UI.ui.xml` (`<WorkspaceBar TearOffDocument="tab-window"/>`; tray button `Icon="tray"` before minimize); `TabWindow.ui.xml` (new `<WorkspaceBar TearOffDocument="tab-window"/>` and the `workspace-add` "+" MenuButton after the title label).
- Tests: `WorkspaceTests` NEW `Workspace.Reorder`, `Workspace.MoveAcross`, `Workspace.TearOff` (+ helpers `ShowBar`, `AddEmpty`, `Tabs`, `Marker`, `Torn`, `Find<T>`), registered in `Workspace.tests.xml`.

## Why these choices

**A workspace is a code-only page inside `WorkspaceControl`** (user, 2026-10-08).
Rejected: keeping `WorkspaceControl` as one workspace and adding a `<Workspaces>` set in `UI.ui.xml` — the torn-off window would keep a lone `<Workspace>`, and every caller that finds "the workspace" would have to find the shown one. A page is needed at all because `SplitViewControl.Split`/`Collapse` swap the single child of their host; one stable single-child host per workspace keeps those rules unchanged.

**Hidden workspaces stay alive, hidden like inactive tab pages** (plan fork F1, user).
Rejected: capture and rebuild on switch like a vault switch — closing a note's last view releases its session and loses its undo history on every switch.

**First run is an XML attribute `FirstRun`** (user, 2026-10-08), over Thorium code adding a Sheets page after restore.
The same list fills a vault with no recorded session (`ChangeScope` fallback). Thorium: General + Sheets (fork F3).

**The vault filter has a header label** (user, 2026-10-08) — a new 24 px row, since the browser had no header.

**Old session records are carried forward, not reset.**
A `SessionWindow` with panes and no workspaces loads as one General workspace and is written back as workspaces (never destroy user data).

**`WorkspaceBarControl` rebuilds through `Engine.Post`.**
A rename commits from inside the caption a rebuild would destroy.

**Planners share per path through `PlannerBook`, shaped like `NoteSessions`.**
Sheets already shared document and undo through `SheetBook`, so only `unsaved` moved onto the document. The plan's "close prompt only on the last view" item needed no code: `CloseTab` only saves dirty sheets/planners and prompts only for unnamed notes, already gated on `session.views == 1`.

**Calendar workspace = planners opened in Calendar view** (fork F6, user).

**W3/W4 forks, settled by the user 2026-10-08:**
- **F8 (b): the ribbon entries with no existing logic were built, as a separate W4b.** Over dropping them.
- **F9: tab-row tools hide only where the ribbon covers that editor** (a note in Docs, a sheet in Sheets). Elsewhere they stay: General, tear-off windows, a sheet inside Docs. Rejected: showing both — duplicate controls.
- **F10: ribbon category and inspector toggle are per workspace and saved in the session.** Rejected: in-memory only — the toggle would reset every launch.
- **F11: the sheet page strip keeps its layers popup everywhere; the inspector also hosts the panel.** Rejected: hiding the popup while the inspector shows — couples the strip to the inspector, and General has no inspector.
- **F12: the Margins dropdown is presets plus a T/B/L/R field row.**
- **F13: the unused `DocumentToolbarControl` is reused as Docs Format**, which keeps its px-field focus handling. Rejected: rebuilding it from `TabToolsControl` parts.
- **F14 (a): Import CSV adds a page to the open sheet.** Rejected: a new sheet file beside it — duplicates the vault's "Sheet from CSV".
- **F15 (a): an inserted picture is copied unchanged.** Rejected: re-encode through `PasteImage` as PNG — photos grow several-fold.
- **F16 (a): one internal `FileDialog` behind both pickers.** Rejected: a second copy of ~70 lines of COM interop.

**The ribbon and inspector act on the focused pane's active editor of the shown workspace.**
`FocusedView`: `TabViewControl.focused` when it is on the page, else the page's `lastFocused`, else its first pane. Entries give the note the caret, or the sheet's grid the active control, first — the same way W3's tools do. Ribbon buttons never take the active control.

**The inspector is draggable, and `SplitterControl` sizes the after-pane in that one case** (user: draggable over a fixed 240 px column with a drawn edge).
`SplitterControl` previously always sized the pane before it, which between the star Workspace column and the fixed inspector would have resized the Workspace.

**Icons are drawn, not text captions** (user).
The importer (`Filing.Serialization.SvgPath`) refused strokes at W4b (no stroke-to-outline, `fill="none"` was refused), so the design canvas's stroked icons could not be imported as-is; since 2026-10-09 strokes import, baked from the centreline — see [[svg-import]]. (At W4b it also took only `<path>`, filled evenodd as nonzero and had no arc `A` command; since 2026-10-09 it reads arcs, basic shapes, transforms and evenodd — see [[svg-import]].)

**Inspector Reference shows `Page!Cell`, not the full note link.**
A `StackPanelControl` measures max(content, preferred), so the full link (with the file name) widened the column to 549 px. Copy still copies the cells, which Paste link turns into a link.

**Page size captions use ASCII "x" and ",".** The UI font's Latin charset has no `·` or `×`.

**`$` and `%` on Sheets Format are built straight in their button (`RibbonControl.Glyph`).**
`TabToolsControl.Text` draws nothing for a one-character caption; found here, root cause not investigated.

**A control added under a destroyed parent is now warned about** — see [[destroyed-parent-orphans]] for the bug found while verifying W4.

**Workspace tab drag moves on drop, not live while dragging.**
The bar rebuilds on every `WorkspaceControl.changed`, which would destroy the dragged button mid-drag.

**The drop marker is a real stack child.**
Tabs right of the gap shift ~4 px while it shows. Rejected: an edge on the neighbouring tab (clashes with the shown tab's accent edge); an overlay arranged outside the stack (needs a custom arrange).

**Tear-off triggers only when the drop is accepted by nothing AND the pointer was outside every app window.**
"Outside" is sampled in `OnDrag` each tick, because `UIEngine.EndDrag` clears `mouseOverWindow` (firing `DraggedOutOfWindow`) before `OnDragStop`.

**Torn-off windows reuse the `tab-window` document for both pane and workspace tear-offs** (user-approved).
Every secondary tab window now shows a workspace bar (a pane tear-off shows a "General" tab). Rejected: a separate workspace-window document — a workspace could then not be dropped onto a pane-tab window.

**Last-workspace rule: the primary keeps its last workspace; a secondary window that loses its last one closes.**
`Move` refuses to take the primary's only page; the secondary closes like `TabViewControl.CloseIfEmptied`; tearing off a window's only workspace is refused.

**The tray button acts only on the primary.**
The tray only restores the primary; the action reuses `Background.Hide`. The tray icon is up from boot whatever `OnClose` says ([[desktop-stickies]]).

**The drag threshold sits on the button, like `TabStripButtonControl` and the planner's `TicketCard`.**
Moves dispatch to the hovered control, so a flick that leaves a narrow tab before travelling 12 px over it never starts a drag; the Reorder test drags with 24 steps for that reason.

## Known gaps
- `TabToolsControl.Text` one-character caption bug (cause unknown).
- OS dialogs (Picture…, Import CSV…) and `Sheets.ConvertOpenCsv` untested; the folder-picker refactor needs one manual vault-add.
- Inspector width from a splitter drag is not saved in the session.
- Inspector List row is text (None/Bullets/Numbers/Tasks), not icons.
- Status bar (Thorium `UI.ui.xml`, `Thorium.Editor.StatusBarActions`) is a mockup: storage / connection / git items toggle static state with no backing; no store/server/branch icons yet. The design's Explorations ideas are still out.
- W3/W4 are not GUI-verified: no real-app run.
- An empty pane shows nothing at all (no strip, no "No open files" message).
- Folders stay listed under every filter, including ones with no matching file.
- After a switch the active control is the editor control, not its caret, so typing needs a click first (not verified either way).
- Workspace tab drag: test-verified (`_Build/test.sh Workspace`, 7 Workspace tests pass; Boot fails on the pre-existing baseline sampler error); full suite 286 passed / 2 failed / 40 skipped, the 2 (Boot, Perf.TypeLargeNote) pre-existing, no [Vulkan] lines. NOT GUI-verified: mouse drag between two OS windows, drop outside every window by hand, marker look, tray button look/icon, tray hide/restore by hand. No test for the tray button (it would hide the test runner's primary; `Background.Hide` is covered by `Background.ClosePrompt`).
- Flick-drag across a narrow workspace tab can miss the 12 px threshold.
- Ribbon and inspector exist only in the primary; a Docs/Sheets workspace in a secondary window has no ribbon there (its saved category is kept).
- No "Move to new window" entry in the workspace context menu.
- Sticky window has no workspace bar.
- Index computation and marker shift can flicker within a ~4 px band at a tab's centre.
- Duplicate keeps the same title.
- Not GUI-verified: click, double-click rename, right-click, the "+" menu, a real quit and relaunch. Not perf-measured.
- W2: full Thorium suite 262 passed / 3 failed / 40 skipped; the 3 were the pre-existing baseline (Boot, Sheet.FixedSize, Perf.TypeLargeNote).
- W3/W4: full Thorium suite 279 passed / 2 failed / 40 skipped; the 2 (Boot: default sampler error; Perf.TypeLargeNote: 3 errors) predate this work. 11 Ribbon-suite tests pass; 4 goldens approved by the user after reading.

Related: [[session-restore]], [[note-model]], [[tab-view-control]], [[planner]], [[sheets]], [[document-format-bar]], [[splitter-and-pane-sizing]], [[destroyed-parent-orphans]], [[note-images]]
