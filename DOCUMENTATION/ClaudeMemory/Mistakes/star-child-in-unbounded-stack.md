# Mistake — a height-less child in a horizontal `StackPanelControl` measures unbounded tall

**Date:** 2026-10-07 (planner P1 card strip, again in P4 category popup)
**Scope:** `ArctisAurora.Core.UI` — `StackPanelControl`, `PlannerCategoryPopup`, `PlannerBoardControl` (card colour strip), `ContextMenus.Host`

## What happened
- **P1:** a board card's colour strip was a side strip with no preferred height. In a horizontal `StackPanelControl` a child with no preferred height fills the cross axis, so it measured unbounded and stretched the first card to the column's full height. Fixed by putting the strip across the top.
- **P4:** the category popup's button row was a horizontal `StackPanelControl` holding a star spacer and no preferred height. It measured `float.MaxValue` tall, which pushed the popup into its own OS window and, under `--test`, crashed with "Failed to create the context menu window". Seen through a temporary log in `ContextMenus.Host` (removed).

## The rule
- A horizontal stack whose children have no preferred height (a star spacer, a strip) gives the cross axis no bound. Give the row a fixed height, or give the child a preferred height.
- The category popup's button row has a fixed height for this reason.
- A popup that suddenly opens in its own OS window, or "Failed to create the context menu window" under `--test`, points at an unbounded measure — check the stacks inside it before the host code.

Related: [[planner]], [[rebuild-inside-pointer-dispatch]], [[context-menus]]
