# Workspace plan — W1–W5, the Thorium shell from the "Workspace Switcher" canvas

**Status:** agreed (user, 2026-10-08). W1, W2, W3, W4a and W4b landed 2026-10-08; W5 (split gestures) skipped (user, 2026-10-08), nothing built.
Design: the "Workspace Switcher" canvas artifact (https://claude.ai/artifact/TzhJxMu7YgKiYWiJLrzALK), Shell page — boards Shell, Compact sizes, Split gestures, LaTeX in tab. The Explorations page is out.
Follows [note-model-plan](note-model-plan.md): notes and `.tex` are multi-view since N3.

## Forks settled
- F1 (W2): hidden workspaces stay alive, collapsed like inactive tab pages — not captured and rebuilt on switch
- F2 (W1): note body text 18 → 16 (`DocumentSettings` `Text`)
- F3 (W2): first run creates General and Sheets; the rest come from the workspace "+" menu
- F4 (W2): workspace right-click menu — Close, Rename, Duplicate
- F5 (W3): the note cluster is the canvas's small set; the styling dropdown, colours, size and page menu are on the Docs ribbon since W4a (Format and Page), not in the tab-row cluster. Open: whether General's cluster stays the small set for good
- F6 (W2): the Calendar workspace lists planners and opens them in Calendar view; its "+" makes a planner
- F7 (W5): joining two areas moves the closing area's tabs into the survivor; nothing closes
- F8 (W4, b): the ribbon entries with no existing logic were built, as a separate W4b — not dropped
- F9 (W4): tab-row tools hide only where the ribbon covers that editor (note in Docs, sheet in Sheets); elsewhere they stay. Rejected: showing both
- F10 (W4): ribbon category and inspector toggle are per workspace and saved in the session. Rejected: in-memory only
- F11 (W4): the sheet page strip keeps its layers popup everywhere; the inspector also hosts the panel. Rejected: hiding the popup while the inspector shows
- F12 (W4): Margins dropdown = presets + T/B/L/R field row
- F13 (W4): the unused `DocumentToolbarControl` is reused as Docs Format. Rejected: rebuilding it from `TabToolsControl` parts
- F14 (W4b, a): Import CSV adds a page to the open sheet. Rejected: a new sheet file beside it
- F15 (W4b, a): an inserted picture is copied unchanged. Rejected: re-encode as PNG through `PasteImage`
- F16 (W4b, a): one internal `FileDialog` behind both pickers. Rejected: a second copy of the COM interop

## W1 — compact chrome — LANDED 2026-10-08
- Sizes: title bar 32 → 26, caption buttons 46×32 → 40×26, icons 14 → 12, maximize 18 → 14, menu captions 13 → 12, sidebar 220 → 200, splitter 5 → 3 (`UI.ui.xml`, `Workspace.ui.xml`, `TabWindow.ui.xml`, `SplitViewControl.gripThickness`); `TabHeight` 28 → 24, `TabWidth` 180 → 140 (`Workspace.ui.xml`, `TabPane.ui.xml`); tab caption 14 → 12 (`TabViewControl.captionSize`); `SheetControl` `headerHeight` 24 → 20, `fontSize` 14 → 12; note body 18 → 16
- NEW `TabViewControl.minTabWidth` (`MinTabWidth`, default 64): `FitTabs` shares the strip between tabs before `strip.Measure`; `SplitViewControl.NewPane` copies it
- NEW `TabViewControl.focused`: nearest `TabView` above `ActiveControl`, kept when the control has none (same rule as Thorium's `ActiveTabViewer` context); a change repaints every view in every window
- NEW `AccentRole.TabIdle`: the active tab of a view that is not `focused` (when one is) gets a top bar in NEW palette slot `Palettes.IdleAccent` — optional palette attribute `IdleAccent`, else `Line` stepped twice toward its ink; `Control.ApplyShape` sets the edge paint for `Tab`/`TabIdle`
- `Tex.Footnotes` pins its fixture's `Text` to 18 px, since its line counts are tuned to it (user, option a)
- Verify: suite 257 passed / 3 failed / 40 skipped, identical to the baseline; 26 goldens rebaselined (user-approved), `Sheet.FixedSize.Page.png` left as it was because it failed before W1. Shot-verified: focused bar #2F6FB3, idle #D8D6D0. NOT GUI-verified

## W2 — workspaces — LANDED 2026-10-08
**Built:**
- NEW `WorkspacePageControl` (code-only `PanelControl`: `title`, `kind`, one pane tree, `content`, `isShown`, internal `lastFocused`, static `Of(control)`) and `[A_XSDType] WorkspaceKind` in `WorkspacePageControl.cs` (user, 2026-10-08: page inside `Workspace`, over a `<Workspaces>` set)
- `WorkspaceControl` is a `ContainerControl` of pages: `shown`, `Pages`, `AddPage`, `Show` (hides the old page, focuses the new page's last-focused pane or its first), `Rename`, `RemovePage` (the last stays), static event `changed`; lays out only `shown`. `LoadDefault` = `Default` in the first `FirstRun` kind, an empty pane in the rest; NEW `LoadEmpty` = every `FirstRun` kind empty (`ChangeScope` fallback, so a vault with no session gets them too); `LoadPane()` keeps its signature, `LoadPane(page)` added
- NEW XML `FirstRun` on `<Workspace>` (space-separated kinds, General when left out); Thorium's is `General Sheets` (user, 2026-10-08: attribute over host code)
- NEW `WorkspaceBarControl` `<WorkspaceBar>` (nested `WorkspaceButton`; rebuild is `Engine.Post`ed because a rename commits from the caption it would destroy; static `BeginRename(page)`); NEW `WorkspaceActions`: `Workspace.New{General,Docs,Sheets,LaTeX,Manager,Calendar}`, `Workspace.Duplicate`, `Workspace.Rename`, `Workspace.Close`. Menus `workspace` (Rename, Duplicate, Close) and `workspace-add`; the "+" is a `MenuButton` with a "+" label (no plus icon in the set)
- NEW `SessionWorkspace` (`Title`, `Kind`, `Shown`, panes); `SessionWindow.workspaces`; `SessionWindow.panes` is read from old records only, which load as one General workspace and are written back as workspaces; `SessionLayout.Duplicate(workspace, page)`; NEW interface `ISessionWindowChild`
- `VaultBrowserControl`: `Accepts` filters by the shown kind (Docs notes, Sheets sheets + CSV, LaTeX `.tex`, Manager/Calendar planners); NEW 24 px header row with label `VaultFilter` (user, 2026-10-08); `Open` reuses a tab only from a shown workspace, else opens into the focused pane of the shown one; a planner opened in a Calendar workspace starts in Calendar view
- Per path: NEW `PlannerBook` (`Open`/`Close`/`Renamed`, in `PlannerDocument.cs`, count on `PlannerDocument.views`); `unsaved` lives on `SheetDocument`/`PlannerDocument`; `TabActions.Split` makes a second view of any file. The close prompt needed nothing: `CloseTab` only saves sheets and planners, and asks only for unnamed notes
- `TabViewControl.CloseTab(item, onClosed)` private → internal
- Pane "+": not built in W2; built in W3 (below)
- Verify: full Thorium suite 262 passed / 3 failed / 40 skipped (3 pre-existing baseline; 5 new passes: `Workspace.Switch`, `Workspace.Menu`, `Workspace.SessionRoundTrip`, `Workspace.LegacySession`, `Planner.TwoViews`). Shot-verified first run (General + Sheets in the bar, vault header "All files" / "Sheets"). NOT GUI-verified: no click, double-click rename, menu or real quit/relaunch was driven

**Planned (original):**
- `WorkspaceControl` holds N workspaces (`name`, `kind`, pane tree); only the shown one is laid out and drawn. NEW `WorkspaceKind { General, Docs, Sheets, LaTeX, Manager, Calendar }`
- NEW `WorkspaceBarControl` `<WorkspaceBar>` in the title bar: a button per workspace, "+" menu (New *kind* workspace ×6, Duplicate current, Rename…), right-click menu (F4)
- Kind drives the vault filter (`VaultBrowserControl.Accepts` + header label) and what a pane's "+" creates
- `FocusedTabs`/`FindOpenDocument` scoped to the shown workspace; a file open elsewhere opens a second view through `tabFactory`
- Per path: NEW ref-counted `PlannerBook.Open/Close` like `NoteSessions`; `unsaved` moves onto `SheetDocument`/`PlannerDocument` (`SheetBook` already shares document and undo); `TabActions.Split` makes a second view for sheets and planners; close prompt only on the last view
- Session: `SessionWindow` gains `List<SessionWorkspace>` (name, kind, active, panes); a record with panes and no workspaces loads as one General workspace
- Verify: `Workspace.Switch`, `Workspace.SessionRoundTrip`, `Workspace.LegacySession`, `Planner.TwoViews`; suite; one GUI run via `--send`

## W3 — tab-row tools — LANDED 2026-10-08
**Built** (in an earlier session, written up 2026-10-08):
- NEW `TabToolsControl` (20 px tool buttons: `Icon`, `Text`, `Separator`, `Light`, `Drop`; nested `TabToolButton` acts on press) and `NoteToolsControl` (B I U, align left, bullets, Paste link); `IFileEditor.tools` (default null) implemented by `DocumentEditorControl`, `SheetEditorControl` (B, Format menu, Fill menu, Paste link, Sum), `TexEditorControl` (Build, Live, error count, Sync, zoom), `PlannerEditorControl`; `TabViewControl.SyncTools()` mounts the active editor's tools at the right of the tab row
- The 30 px `DocumentToolbar` band is gone from Thorium's `UI.ui.xml` and `TabWindow.ui.xml`
- Pane "+" (`VaultBrowserControl.NewInPane`): Docs → note, Sheets → sheet, LaTeX → `.tex`, Manager/Calendar → planner, General → menu `pane-new` (`PaneNew.menu.xml`)
- `SheetEditorControl.AutoSum()` and a 24 px sheet formula bar (cell name, fx, formula field)
- LaTeX tools: Build, Live, error count, Sync, zoom (`TexEditorControl.tools`); planner tools carry its view and zoom switches and, in Calendar view, Today and the Day/Week/Month span (`PlannerEditorControl.tools`)
- Not built: popping the LaTeX preview out of its tab
- Verify: `TabTools.Mount/Bullets/AutoSum/FormulaBar/Tex`. NOT GUI-verified

**Planned (original):**
- One 24 px row per pane: tabs, then a cluster for the active tab's kind, replacing the `DocumentToolbar` band. Note: B I U | align, bullets | sheet link. Sheet: B, number format, fill, paste link, Sum. LaTeX: Build, build on save, warning count, sync, zoom, pop the preview out. Planner: its view and zoom switches move up. Calendar: ‹ Today ›, Month / Week
- Pane "+": a menu in General (note / sheet / LaTeX / plan), the workspace's kind elsewhere
- NEW sheet formula bar, 24 px: cell name, fx, formula field

## W4 — ribbon and inspector (Docs, Sheets) — W4a and W4b LANDED 2026-10-08
**Built, W4a:**
- NEW `RibbonControl` (30 px under the title bar, Docs and Sheets only; Docs Format/Insert/Page, Sheets Format/Insert/Data; inspector toggle at the right) and `InspectorControl` (240 px column right of the panes behind a draggable splitter; Docs Paragraph + Page, Sheets Cell + Layers). Target = the active editor of the shown workspace's focused pane. Per-workspace `ribbonCategory` and `inspectorShown`, saved as `SessionWorkspace` `Ribbon` and `Inspector`
- Docs Format is the existing `DocumentToolbarControl` (new `target`, reordered, page button removed); `TabViewControl.SyncTools` mounts nothing where the ribbon covers the editor (F9)
- `SplitterControl.grabNext`: between a star pane and a sized pane after it, the drag writes the after-pane's size
- NEW Input actions `Text.Strikethrough`, `Text.Numbers`, `Text.Tasks`, `Text.InsertPicture`; `SheetEditorControl.FocusFormula()`
- Verify: 6 Ribbon/Inspector tests, 4 goldens (user-approved); splitter drag checked by `Inspector.Docs`

**Built, W4b** (the entries that needed new logic, F8):
- `DocumentControl.ToggleList` (numbered, task) with `DocumentEditorControl.ToggleNumbers/ToggleTasks`; `DocumentEditorControl.InsertPictureFile` (Picture…); `SheetEditorControl.ImportPage` (Import CSV…); Thorium action `Sheets.ConvertOpenCsv` (Convert CSV to sheet)
- NEW internal `FileDialog` behind `FilePicker.Pick` and `FolderPicker`; 20 NEW filled icons (atlas 44 icons)
- Verify: `Note.NumbersToggle`, `Note.TasksToggle`, `Note.InsertPictureFile`, `Sheet.ImportCsv`, `Ribbon.CsvEntries`. Full Thorium suite 279 passed / 2 failed / 40 skipped (2 predate this work). NOT GUI-verified: OS dialogs, `Sheets.ConvertOpenCsv` itself
- Left: `TabToolsControl.Text` one-character caption bug; inspector width not saved; inspector List row is text. Detail in `Decisions/workspaces.md`

**Planned (original; "no new editing logic" was dropped by F8):**
- 30 px ribbon in Docs (Format / Insert / Page) and Sheets (Format / Insert / Data) workspaces only
- 240 px inspector with a toggle: Docs Paragraph + Page, Sheets Cell + Layers (the sheet layers panel moves in)
- Every entry calls an existing action; no new editing logic

## W5 — split gestures — SKIPPED 2026-10-08 (user)
- Five-zone target at the pane centre during a tab or file drag; the edge band stays
- A vault row dragged onto a strip or pane opens there
- Corner drag splits an area; dragging into a neighbour joins (F7)

## Left out
- Status bar (storage, connection, git have no backend), Explorations ideas A–D, LaTeX preview as a linked pane

Related: [[note-model]], [[session-restore]], [[tab-view-control]], [[sheets]], [[planner]]
