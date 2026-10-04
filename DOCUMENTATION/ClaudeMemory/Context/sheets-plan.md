# Sheets plan — spreadsheet editing in Thorium

**Status:** S1 landed 2026-10-02; S2a, S2b1, S2b2 2026-10-03. S2b3, S2c and S3 2026-10-04. S4 is next (not designed);
S2d, S4 and S5 are an ordered wish list, not designed.
Decision record: [sheets](../Decisions/sheets.md). Part of the Thorium productivity suite (WIP list, Phase A):
notes, sheets and a todo board under Blender-style workspace tabs, plus a simplified mode with familiar layouts.

## Settled by the user (2026-10-02)
- A sheet is its own file, `*.sheet.xml`, opened in a tab like a note.
- "Layers" means both: pages (bottom tabs, one file = a workbook) and layers stacked over one page's grid.
- Tabs talk to editors through `IFileEditor`.
- Order after S1: formulas first.

## Settled by the user (2026-10-03)
- Reach: a cell can be read from other pages of its file, from other sheet files, by notes, and by note math.
- Ctrl+V into a sheet pastes values; Paste link (Ctrl+Shift+V) pastes references. OS clipboard gets values.
- A sheet copy pasted into a note: one cell → an inline live value; a range → a note table of live values.
- Excel precedence (`-2^2` = 4).
- Sliced S2a (one file) then S2b (the rest).
- S2b answers: notes match the sheet's keys (Ctrl+V plain, Ctrl+Shift+V live); `.md` writes
  `![[Budget.sheet.xml#Expenses!B3]]`; a rename inside Thorium rewrites references, a delete or outside rename is
  `#REF!`; freshness is Thorium's own saves (no watcher, no mtime checks).
- S2b forks: a range in a note is one read-only live grid block (implemented in S2b3 as a display object span, not a block kind), same in `.xml` and `.md` (replaces "a note table of
  live values" above); S2b split into S2b1/S2b2/S2b3; `[file]Page!` syntax, page required; undo on the document.

## Settled by the user (2026-10-04)
- S2c is substitution only: `\sheet{…}` holds the note-link reference string; evaluating the formula is a possible S2d.
- S3: formats live per page, shared by all layers (not per layer on the cell).
- S3: formats are applied from Ctrl+B and a grid context menu; a sheet toolbar comes later, with the workspace tabs.
- S3: numbers copy as numbers (unformatted), in sheets and in a note's plain-text copy.
- S3: General and Scientific are one preset; currency is euros.
- S3: note links show the number format, not bold or fill.

## Slices
| # | Slice | State |
|---|---|---|
| S1 | grid, selection, in-cell editing, undo, TSV clipboard, `.sheet.xml`, any-editor tabs | landed |
| S2a | formulas in one file — arithmetic, `SUM`, cell/range/page references, Kahn recalc, errors as values | landed |
| S2b1 | vault-wide graph (`SheetBook`), `[Budget]Expenses!B3` (unopened files loaded from disk), rename rewrites | landed |
| S2b2 | Paste link in sheets (Ctrl+Shift+V) | landed |
| S2b3 | notes show cells: inline live value (`StyleSpan` object like math), range = read-only live grid block, `<Run Sheet>` / `![[…]]`, `Text.PasteLink`, notes refresh on `SheetBook.changed`, rename reaches notes | landed |
| S2c | note math reads sheet cells — `\sheet{file#Page!A1}` in a formula is replaced by the cell's value before parsing (substitution only, no evaluation); Ctrl+Shift+V in the formula popup; rename rewrites formulas | landed 2026-10-04 |
| S2d | evaluate note formulas (result after a trailing `=`) — not designed | — |
| S3 | formatting (bold, fill, number formats), column/row resize by dragging header edges | landed 2026-10-04 |
| S4 | page tabs along the bottom, layer list (show/hide, pick the edited layer) | — |
| S5 | CSV import/export | — |

## Owed for S2b3 — resolved 2026-10-04
- Done: Ctrl+Shift+V stays on `Sheet.PasteLink`, which falls back to `Text.PasteLink` outside a sheet — `Text.PasteLink`
  took over that fallback.
- Rewriting `.md`/`.xml` notes on a sheet rename: re-saving a note does not reproduce its bytes, so this needs a
  token-level rewrite — done (`SheetLinks.Renamed`).

## Carried gaps from S1
See [sheets](../Decisions/sheets.md) § Known gaps.
