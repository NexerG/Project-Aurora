# Mistake — destroying or rebuilding controls inside a UIEngine pointer dispatch corrupts layout state

**Date:** 2026-10-07 (planner P3, recurred in P4)
**Scope:** `ArctisAurora.Core.UI` — `UIEngine` (`EndDrag`, pointer dispatch), `PlannerBoardControl` (`FinishDrag`, `RebuildSoon`), `PlannerCategoryPopup`, `PlannerTicketPopup`, `PlannerEditorControl`

## What happened
- **P3:** `PlannerBoardControl.FinishDrag` applied the move at once. The move raises `Changed`, `Rebuild` destroys every card, and the dragged `TicketCard` is the drag claimant — `UIEngine.EndDrag` was still running and calls `OnDragStop` on the claimant after `FinishDrag` returns. P3 worked around it by posting the drop (`Engine.Post`) in `FinishDrag`.
- **P4 recurrence:** the category popup's Delete button deleted the category inside a *press* dispatch; `Changed` rebuilt the board there and the identical errors came back. Bisected again: removing `Planner.CategoryPopup` cleared it; deferring the delete cleared it.
- **PC / C2 recurrence (2026-10-07):** `Planner.AttachmentText` clicked the ticket popup's Apply button, which closes the popup and applies the edit inside a pointer *press* dispatch (a P2 code path). The later `Perf.Controls.Scrollable.Relayout` logged `[UIEngine] 'entity' subtreeBounds (0, 0, 0, 0) != recomputed …` and `[Layout] skipped layout left … subtreeBounds stale` (×8) in the full run. Bisected by adding tests back one batch at a time to the full run; applying with Enter instead cleared it, so the test uses Enter. The popup's Apply button is unchanged — the trap is reachable through it.
- So the trap is not about `FinishDrag` or the drag claimant. Rebuilding (destroying controls) inside UIEngine's pointer dispatch — press, release, tap, drag end — leaves stale layout state.

## Symptom
- Nothing failed in the Planner suite. In the full `--test` run a later unrelated test, `Perf.Controls.Scrollable.Relayout`, logged `[UIEngine] 'entity' subtreeBounds (0, 0, 0, 0) != recomputed …` ×4 and `[Layout] skipped layout left … subtreeBounds stale` ×4 and failed.
- The perf test's body runs even when it reports SKIP for the unoptimized JIT, so stale state left by an earlier suite surfaced there. Running the Planner suite alone was clean.

## The rule
- **Do not destroy or rebuild controls inside any UIEngine pointer dispatch** (press, release, tap, drag end, `FinishDrag`, `DraggingOver*`). Post it: `Engine.Post(() => …)` runs it one tick later, after dispatch has finished with the controls.
- Fix it at the source of the rebuild, not per call site. The planner's source fix is `PlannerBoardControl.RebuildSoon`: one posted `Rebuild` per tick for every change (drop, popup Apply/Delete, undo, keys), skipped if the board was destroyed. `FinishDrag` then calls `MoveTicket` directly.
- When a perf test fails with `subtreeBounds stale` in a full run, bisect the tests that ran before it, not the perf test. Bisect in the full run: the perf test fails even alone when put into the Planner suite (it needs its own suite context).

## Known gaps
- The engine root cause is unguarded: `UIEngine` does not protect its pointer dispatch against controls destroyed mid-dispatch. Open in the WIP list.
- The board shows a change one tick late (the posted rebuild).
- `PlannerTicketPopup`'s Apply button still closes and applies inside the press dispatch (found 2026-10-07, C2); only the test avoids it.

Related: [[planner]], [[planner-plan]], [[ui-engine-stack]], [[profiling-unoptimized-jit]], [[star-child-in-unbounded-stack]]
