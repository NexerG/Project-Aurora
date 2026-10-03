# Sheets plan — spreadsheet editing in Thorium

**Status:** S1 landed 2026-10-02. S2 (formulas) agreed as next; S3–S5 are an ordered wish list, not designed.
Decision record: [sheets](../Decisions/sheets.md). Part of the Thorium productivity suite (WIP list, Phase A):
notes, sheets and a todo board under Blender-style workspace tabs, plus a simplified mode with familiar layouts.

## Settled by the user (2026-10-02)
- A sheet is its own file, `*.sheet.xml`, opened in a tab like a note.
- "Layers" means both: pages (bottom tabs, one file = a workbook) and layers stacked over one page's grid.
- Tabs talk to editors through `IFileEditor`.
- Order after S1: formulas first.

## Slices
| # | Slice | State |
|---|---|---|
| S1 | grid, selection, in-cell editing, undo, TSV clipboard, `.sheet.xml`, any-editor tabs | landed |
| S2 | formulas — A1 references, arithmetic, `SUM`, recalc through a dependency graph | next, not designed |
| S3 | formatting (bold, fill, number formats), column/row resize by dragging header edges | — |
| S4 | page tabs along the bottom, layer list (show/hide, pick the edited layer) | — |
| S5 | CSV import/export | — |

## Open questions for S2
- Where a computed value lives: on `SheetCell` beside `raw`, or a per-page value cache.
- Whether a formula on a higher layer can reference a cell another layer covers (`Shown` vs per-layer reads).
- Error display (`#REF!`, `#DIV/0!`, cycles) and whether errors are values.

## Carried gaps from S1
See [sheets](../Decisions/sheets.md) § Known gaps — notably a dirty sheet is not saved on quit.
