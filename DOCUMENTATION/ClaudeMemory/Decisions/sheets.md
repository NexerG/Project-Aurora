# Decision — a sheet is its own file kind, drawn visible-cells-only, with pages and layers in the format from day one

**Date:** 2026-10-02
**Scope:** `ArctisAurora.Core.UI` — `SheetDocument`, `SheetPage`, `SheetLayer`, `SheetCell`, `SheetXml`,
`SheetControl`, `SheetEditorControl`, `SheetCellEdit`, `SheetActions`, `IFileEditor`, `TabViewControl.FileEditorOf`;
`Thorium.Editor.CustomControls.VaultBrowserControl` (`BuildSheetTab`, `NewSheet`, `BaseName`, `Extension`)

**Status:** PARTIAL — S1 of [sheets-plan](../Context/sheets-plan.md) landed; formulas (S2) are next.

## What changed
- A sheet is a `*.sheet.xml` in the vault: `<Sheet Name><Page Name><Column At Width/><Row At Height/><Layer Name Visible><Cell At="B3">text</Cell></Layer></Page></Sheet>`.
  Addresses are A1 (`Column At="B"`, `Row At="4"`, 1-based rows). Cells written row-major. No namespace, no XSD.
- Model: `SheetDocument` → `pages` → `SheetPage` (sparse `columnWidths`/`rowHeights`, `layers`) → `SheetLayer`
  (`Dictionary<long, SheetCell>`, key `row << 32 | column`, `visible`). Absent = empty. Unknown elements kept in
  `extra` at each level and written back.
- `SheetPage.Shown(r, c)` = topmost **visible** layer holding the cell. Edits land in the topmost layer.
- Band geometry: `ColumnLeft`/`RowTop` sum default + sparse deltas; `ColumnAt`/`RowAt` binary-search them.
- View: `SheetEditorControl : ScrollableControl` (Both axes, `Surface` ground) holding one `SheetControl` canvas.
  The canvas measures to used extent (+100 rows, +26 columns, and past the active cell) and in `ArrangeCore`
  reads the scroller's inner rect, then places pooled parts for the visible window only: grid lines and cell
  `LabelControl`s in a `Parts` container, a selection wash (`Accent`, alpha 0.18), four `Accent` outline bars on
  the active cell, the in-cell `TextBoxControl`, then headers (`Chrome` bands, `MutedInk` names) pinned to the
  viewport. Unused pooled parts arrange to `LayoutRect.Empty`; pools never shrink.
- Numbers (invariant `double.TryParse`) right-align when they fit; text is cut at the cell edge.
- Editing: typing over a cell (`Sheet.Write` on `AnySymbol`) opens the field empty; F2 / double-click keeps the
  text with the caret at the end. Enter / Tab commit and step (Shift reverses), Esc cancels (`Text.Cancel` →
  `TextBoxControl.Cancel`), blur commits without taking focus back. One `SheetCellEdit` per commit, paste, clear or cut.
- Clipboard: `IClipboardTarget` on the editor — copy writes the selection as TSV (`\t`, `\r\n`), paste writes TSV
  from the active cell and selects the block. A blank pasted field clears its cell.
- `IFileEditor` (`path`, `isDirty`, `Save`, `Repath`, `ViewState`, `RestoreView`) is what tab close, open-document
  lookup, session capture/restore and vault rename talk to. `TabViewControl.EditorOf` stays for note-only paths
  (naming prompt).
- Sheet `ViewState` reuses `SessionTab`: `caretBlock`/`caretOffset` = active row/column, `anchorBlock`/`anchorOffset`
  = anchor, `scrollX`, `topDelta` = scroll Y.
- Thorium: `BuildTab` routes `*.sheet.xml` to `BuildSheetTab`; `Sheets.New`/`Sheets.NewHere` in the File, Vault and
  VaultFolder menus; rename and duplicate keep `.sheet.xml` whole (`BaseName`, `Extension`).

## Why these choices

**Its own file, not a block inside a note.**
A note table (`TableControl`) is a `GridListControl` whose cells are stacks of blocks — a control per cell, built
eagerly. That is right for a 5×10 table in prose and wrong for 10,000 rows. A sheet is a different document kind
in its own tab, which is also the shape the suite's workspace tabs (Notes / Sheets / Board) want.

**`.sheet.xml`, not `.sheet`.**
Matches the repo's `[name].[type].xml` convention. Cost: `Path.GetExtension` sees `.xml`, so anything that names
or re-extends a file goes through `VaultBrowserControl.BaseName`/`Extension`. `Accepts` needed nothing — `.xml` is
already accepted. `WriteName` works unchanged because the root carries `Name`.

**Only visible cells have controls, and they are reused.**
`Sheet.ScrollKeepsControls` measured 10,000 rows: the control count is under 1000 and identical before and after
scrolling to row 5000. Rejected: drawing glyphs straight from a custom `Emit` (no child controls at all) — less
overhead, but it duplicates `TextRunControl`'s glyph path; pooled labels reuse it as is.

**A rebind costs one extra layout pass, accepted.**
Setting a pooled label's `text` inside `ArrangeCore` calls `InvalidateLayout`, which marks every ancestor measure-dirty
and registers the root, so the frame after a scroll re-measures the chain once and settles (no text changes the
second time). Cheap — siblings are cached by offer — and avoided only by rebinding before layout, which the thumb
drag and `ScrollIntoView` paths do not offer a hook for.

**Keys are added as second binds, not by teaching `Text.*` about sheets.**
`GestureMatcher.Update` fires every bind on a trigger with the same modifier count; each `Sheet.*` action no-ops
unless a `SheetEditorControl` is at or above `UIEngine.activeControl`, exactly as `Text.*` no-ops without its editor.
Order does not matter: Enter fires `Text.NewBlock` (commits the field via `Box()`) and `Sheet.Enter` (commits if
still editing, then steps). Shift+Enter is read inside `Sheet.Enter` — a `Shift+Enter` bind would shadow
`Text.NewBlock` in notes. Shift+Tab needs its own bind (`Sheet.TabBack`) because `Text.Outdent` already shadows the
plain Tab binds when Shift is held.

**`IFileEditor.isDirty` is implemented explicitly.**
`Entity` already has a public `isDirty` (the ECS dirty flag). A public `isDirty` on an editor would hide it.
`SheetEditorControl` keeps its own flag as `unsaved`.

**Pages and layers are in the format before any UI shows them.**
Chosen (1c): a file is pages (bottom tabs, like a workbook) and each page is layers stacked over one grid. S1 shows
page 0 and edits its top layer, but reads and writes every page and layer, so S4 adds UI and never migrates files.

**The editor paints `Surface`.**
`DocumentEditorControl` is transparent because its pages draw their own paper. A sheet has no page, and on
the bare window ground it read as ink on black in the first golden.

## Known gaps
- **A dirty sheet is not saved on quit or on a window settling** — `NoteActions.SaveEditedIn` only matches
  `DocumentEditorControl`. Tab close does save. Fix waits on the user (outside the agreed plan's files).
- No formulas, formatting, resize, freeze panes, page/layer UI, CSV (S2–S5).
- Arrows inside an open cell move the caret (Excel's edit mode); there is no enter mode where arrows commit.
- Paste reads plain TSV — quoted fields with embedded tabs/newlines are split literally.
- Cut-off text shows a partial glyph at the cell edge (label clip), no ellipsis.
- Select-all (Ctrl+A) selects the used range, not the whole grid.
- No hit-testing on headers (clicks there do nothing), no autoscroll on drag-select past the edge.
- `File ▸ Save note` (`Text.Save`) does not reach a sheet; Ctrl+S does (`Sheet.Save`).
- **NOT GUI-verified** — test- and golden-verified only (`Sheet.*`, `Sheet.GridDraws.Grid.png`).

Related: [[document-tables]], [[session-restore]], [[vault-browser-and-shell]], [[document-undo]], [[text-clipboard]]
