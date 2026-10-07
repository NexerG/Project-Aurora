# Planner plan — Gantt, board and calendar over one `*.planner.xml`

**Status:** every phase landed 2026-10-07 — F0, P1, P2, P3, P4 (P2: Gantt editing; P3: board drag; P4: categories and assignee chips), PC (calendar view) and C2 (attachments and links). Open follow-ups are in the WIP list under "planner".
Decision record: [planner](../Decisions/planner.md). Part of the Thorium productivity suite (WIP list, Phase A). The calendar was merged into this plan on 2026-10-07; what stays parked is in [calendar-plan](calendar-plan.md).

## Settled by the user (2026-10-07)
- A planner is its own file, `*.planner.xml`, opened in a tab like sheets.
- Board columns are the categories.
- Times are date + time ranges (`TimeRange`), not whole days.
- `User` class with a hand-set current user "Grexen"; no accounts.
- Assignees are stored as `<Assignee User/>` and shown as initials; edited as comma text (stub).
- The calendar is merged into the planner as a third view; no `*.calendar.xml`.
- Google / iCloud sync is out for now.

## Phases
| # | Scope | Status | Verify |
|---|---|---|---|
| F0 | `User`, `TimeRange` | landed 2026-10-07 | — |
| P1 | model, XML, Thorium tab + "New planner" menus, read-only Gantt, Gantt/Board switch (read-only board) | landed 2026-10-07 | `Planner` suite (4); shots; NOT GUI-verified |
| P2 | Gantt editing: "+ Ticket", drag to move / drag edges to resize, ticket popup, delete, undo | landed 2026-10-07 | `Planner` suite (12); `Input` suite (6); golden `Planner.GanttGolden`; NOT GUI-verified |
| P3 | board interaction: drag cards between and within columns (sets category + order, one undo record) | landed 2026-10-07 | `Planner` suite (15); golden `Planner.BoardGolden` (Board, Marker); NOT GUI-verified |
| P4 | categories add / rename / recolour / delete (tickets fall back to the first category); assignee initials chips | landed 2026-10-07 | `Planner` suite (18); golden `Planner.AssigneeGolden`; NOT GUI-verified (the plan's GUI check was not done) |
| PC | Calendar view, the third view (Gantt \| Board \| Calendar) | landed 2026-10-07 | `Planner` suite (28 with C2); goldens `Planner.CalendarGolden` (Week, Month); NOT GUI-verified |
| C2 | attachments and ticket links | landed 2026-10-07 | `Planner` suite (28 with PC); goldens `Planner.AttachmentGolden` (Gantt, Week); NOT GUI-verified |

### P2 — Gantt editing
- "+ Ticket" adds a ticket.
- Drag a bar to move it, drag its edges to resize. One undo record per drag, recorded in `OnDragStop`; snap to the hour on Hour zoom, to the day otherwise.
- `PlannerTicketPopup`, built like `SheetGrowPopup`: name box, category `DropdownControl`, `ColorPickerControl`, start/end `yyyy-MM-dd` (+ time) boxes, read-only creator, assignees box. Enter = one undo record, Esc cancels.
- Delete a ticket; undo/redo.
- Edit records in `PlannerEdits.cs`: `PlannerTicketEdit`, `PlannerTicketAddEdit`, `PlannerCategoryEdit`.
- `PlannerActions.cs` with `Planner.Undo` / `Planner.Redo` / `Planner.Delete` as second binds (like `Sheet.*`) in `InputMap.inputs.xml`.
- `Planner.menu.xml` context menu (Edit, Delete ticket, Move to category ▸) registered in `ThoriumAssets.assets.xml`.

**P2 landed 2026-10-07. Departures from the plan above** (record: [planner](../Decisions/planner.md), section "P2 — Gantt editing"):
- The popup's category dropdown needed an engine change: `ContextMenus.OpenFrom` (a menu opens inside an open popup without closing it).
- "Move to category" is a submenu built on open: `<ContextSubmenu Source="planner-categories"/>` + `ContextMenus.RegisterSource`.
- Added at the user's request: Ctrl+S (`Planner.Save`) and an Apply button on the popup. Added by the build: `Planner.Edit` action.
- `PlannerCategoryEdit` moved to P4 (nothing edits categories yet).
- Added: `PlannerDocument.NextOrder` and `changed`/`Changed()`, `PlannerTicket.Clone`/`CopyFrom`, `PlannerEditorControl.clock` (the injectable now).
- Open follow-ups: Tab between popup boxes (no `Planner.Tab` bind); clear a ticket colour back to its category's; dragging a bar past the span is cut off until release.

### P3 — board interaction
- Drag cards between columns (changes the category) and within a column (changes `order`); one undo record per drop.

**P3 landed 2026-10-07. Departures from the plan above** (record: [planner](../Decisions/planner.md), section "P3 — board drag"):
- The drop marker is a gap (a margin on the neighbouring card) plus an Accent outline on the target column, not a line.
- (Retracted in P4) The drop was applied one tick later with `Engine.Post` in `FinishDrag`; P4 removed that — `FinishDrag` calls `MoveTicket` directly and the board rebuild itself is posted (`RebuildSoon`).
- `PlannerTicketEdit` gained a list constructor so a renumbered column is one undo record.
- `PlannerBoardControl.CardsIn(category)` is public, for tests.
- Open follow-ups: select / open a card on the board; guard `UIEngine` pointer dispatch against controls destroyed mid-dispatch (engine; widened in P4).

### P4 — categories and assignees
- Add, rename, recolour and delete categories; tickets of a deleted category fall back to the first.
- `PlannerCategoryEdit` (moved here from P2).
- Assignee stub UI: initials on cards, comma-separated text box in the popup.

**P4 landed 2026-10-07. Departures from the plan above** (record: [planner](../Decisions/planner.md), section "P4 — categories and assignee chips"):
- `PlannerCategoryAddEdit` (the plan named only `PlannerCategoryEdit`).
- `PlannerCategoryPopup` (the plan did not name a popup), entered from a toolbar "+ Category", a double click on a category row / column, and right-click menus.
- `PlannerCategory.menu.xml` (registered `planner-category`) for the board columns; `Planner.menu.xml` gained Edit / Add category.
- `stopsContextMenu` on the chart and the board columns.
- `PlannerBoardControl.RebuildSoon`: the board rebuild is posted once per tick; the P3 per-call deferral of the drop was removed.
- The plan's GUI check in a running Thorium was not done (see the Verified lines in the record).
- Open follow-ups: reorder categories; next phases PC, C2.

### PC — Calendar view
- Day/Week time grid and a Month grid over the planner's tickets.
- Overlap packing into columns.
- Now line, one redraw per minute via `FrameScheduler.RequestFrameAt` so idle frames stay idle.
- Create, move and resize with undo.

**PC landed 2026-10-07. Departures from the plan above** (record: [planner](../Decisions/planner.md), section "PC — calendar view"):
- The step buttons are ASCII `<` `>`, not ‹ › (glyph coverage of the UI font not checked).
- Added, not in the outline: private `CalendarPool<T>` (pooled parts like the Gantt), `pendingTop` + `MorningTop()` (opens at 07:00), `SessionTab.anchorBlock` / `topOffset` reused for span and anchor day.
- `PointAt`, `BodyTop` and `TopHeight` are public, for the tests.
- Calendar C1 of [[calendar-plan]] is superseded by PC.
- Open follow-ups: drag-to-create; resize in Month and the all-day strip.

### C2 — attachments and links
- Attachments with kinds in XML (travel, prep, custom), drawn as dashed blocks before/after a ticket.
- Ticket links: B follows A; moving A moves B.

**C2 landed 2026-10-07. Departures from the plan above** (record: [planner](../Decisions/planner.md), section "C2 — attachments and links"):
- Attachments are translucent blocks (alpha 0.4), not dashed (the user's pick; the engine has no dashed drawing).
- "Moving A moves B" became: followers shift by the change of the leader's END, applied on release, through `WithFollowers`.
- The test `Planner.AttachmentText` applies with Enter, not the popup's Apply button (the button reproduced the pointer-dispatch trap).
- Added: Gantt link elbows (user's pick), `OuterStart` / `OuterEnd`, `PlannerDocument.Find` / `Followers` / `Upstream`.
- Open follow-ups: attachments on the board, Month chips and the calendar strip; followers moving live.

Handoff (CLAUDE.md §10): mechanical XML wiring may go to `aurora-mechanic`.

## Facts that were expensive
- Nested private classes count for the `AssetRegistries.RegisterSerializableTypes` name collision: `Parts` and `Scroller`, copied from `SheetControl` / `SheetEditorControl`, killed boot and were renamed `ChartParts` / `PlannerScroller`. See [[parallel-stack-name-collisions]].
- A horizontal `StackPanelControl` child with no preferred height fills the cross axis; the board card's colour strip is across the top for that reason.
- Goldens of the Gantt need an injectable "now": the today line and the span move with the clock. Solved in P2 by `PlannerEditorControl.clock`.
- Destroying or rebuilding controls inside any UIEngine pointer dispatch (found in P3 at `FinishDrag`, again in P4 at a popup button press) breaks a later perf test's layout state; post it — `PlannerBoardControl.RebuildSoon`. See [[rebuild-inside-pointer-dispatch]].
- A height-less star child in a horizontal stack measures unbounded tall (P1 card strip, P4 popup button row). See [[star-child-in-unbounded-stack]].
- An Accent edge on the card a drop lands before is invisible: the card's colour strip draws over its top edge.
- Two clicks in a test are a double click only if earlier presses are more than 250 ms old (`InputHandler` tap window, real timestamps); earlier tests' drags pushed `tapCount` to 4, so `Planner.CalendarCreate` waits 300 ms of wall time first.
- Bisecting a `subtreeBounds stale` failure: the perf test fails even alone when put into the Planner suite (it needs its own suite context) — bisect in the full run, adding tests back one batch at a time.
- The popup's Apply button, clicked in a test, is a pointer-press-dispatch rebuild and reproduced the P3 / P4 trap in C2; apply with Enter in tests. See [[rebuild-inside-pointer-dispatch]].
- A taller ticket popup that does not fit below the click opens in its own OS window, where tests' `Find` (primary window) cannot see it.

Related: [[planner]], [[calendar-plan]], [[sheets]]
