# Decision — a sheet is its own file kind, drawn visible-cells-only, with pages and layers in the format from day one

**Date:** 2026-10-02
**Scope:** `ArctisAurora.Core.UI` — `SheetDocument`, `SheetPage`, `SheetLayer`, `SheetCell`, `SheetXml`,
`SheetControl`, `SheetEditorControl`, `SheetCellEdit`, `SheetActions`, `IFileEditor`, `TabViewControl.FileEditorOf`,
`SheetFormula`, `SheetValue`, `SheetCalc`, `SheetCellId`, `SheetBook`;
`Thorium.Editor.CustomControls.VaultBrowserControl` (`BuildSheetTab`, `NewSheet`, `BaseName`, `Extension`, `FindSheet`),
`Thorium.Editor.VaultsWindow.Enter`

**Status:** PARTIAL — S1, S2a, S2b1 and S2b2 of [sheets-plan](../Context/sheets-plan.md) landed; S2b3 (notes showing
cells) is next.

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
  lookup, session capture/restore, vault rename and `NoteActions.SaveEditedIn` (shutdown save, window settle) talk
  to. `TabViewControl.EditorOf` stays for note-only paths (naming prompt). `SheetEditorControl.Save` commits an
  open cell edit first, so quit, close and Ctrl+S keep text typed without Enter.
- Sheet `ViewState` reuses `SessionTab`: `caretBlock`/`caretOffset` = active row/column, `anchorBlock`/`anchorOffset`
  = anchor, `scrollX`, `topDelta` = scroll Y.
- Thorium: `BuildTab` routes `*.sheet.xml` to `BuildSheetTab`; `Sheets.New`/`Sheets.NewHere` in the File, Vault and
  VaultFolder menus; rename and duplicate keep `.sheet.xml` whole (`BaseName`, `Extension`).
- **S2a formulas (2026-10-03).** A cell starting `=` is a formula; the file keeps the raw text. `SheetFormula.Parse`
  (recursive descent) → nodes `Constant`, `Reference` (page name?, cell or range), `Negate`, `Binary`, `Call`
  (`SUM` only). `SheetValue` = Empty/Number/Text/Error; `FromRaw` keeps a typed number's text; `Display()` → `G15`.
- `SheetCalc`: `formulas`, `values`, `precedents`, `dependents` keyed by `SheetCellId(SheetPage, key)`.
  `Changed(page, cells)` relinks and runs Kahn over `Downstream`; leftovers get `#CYCLE!`. `PageNamed` is
  case-insensitive. `SheetControl.ArrangeGrid` shows `Display()` and right-aligns `Number`. `Copy` writes values.
- **S2b1 vault-wide graph (2026-10-03).** `SheetBook` (static) holds one `SheetDocument` per full path (`Get` loads
  and registers), `owners` (page → document), and the one `calc`. `SheetEditorControl.LoadPath` goes through
  `Get`, so tabs of one file share a document. `Register` (path-less documents, tests), `Unregister`, `Created`,
  `Deleted`, `Renamed(old, new, vaultSheets)`, `Clear` (vault switch, `VaultsWindow.Enter`).
- `SheetCalc` knows every registered document; `Add` queues one and `Settle` links queued documents, then runs
  Kahn — a file loaded while linking (a reference to an unloaded sheet) is queued, not linked re-entrantly.
- Reference syntax `[Budget]Expenses!B3`, `'[My budget]Sheet 1'!B3` (page required). `PageNamed(home, file, page)`
  resolves the file through `SheetBook.Resolve` → `findSheet`, which Thorium sets to `VaultBrowserControl.FindSheet`
  (first `*.sheet.xml` in the vault whose name or path ending matches, like `FindNote`). Missing → `#REF!`.
- `SheetFormula.RenameFile(raw, rename)` rewrites `[file]page` prefixes found while parsing (spans collected by the
  parser), via `Prefix(file, page)`, which quotes a name holding anything past letters, digits, `_`, `.` (and `/`
  in a file). `SheetBook.Renamed` rewrites loaded documents (saving those with no open tab) and every other
  vault sheet on disk that holds a reference; a file without one is not rewritten.
- `SheetDocument.undo` — one history per file. `SheetCellEdit` holds the document and calls `SheetBook.Changed`,
  which raises `SheetBook.changed(document)`; every `SheetEditorControl` redraws, and the edited file's editors set
  `unsaved`. `changed(null)` = values may have moved (create/delete/rename), redraw only.
- **S2b2 Paste link (2026-10-03).** `Copy` keeps `copiedText` + `copiedFrom` (document, path, page, range); `Cut`
  drops them. `PasteLink` writes `=` + prefix + address per cell (none on the same page, `Page!` in the same file,
  `[File]Page!` across files; file = path's base name), one undo step, block selected; a clipboard that no longer
  matches pastes plainly. `Sheet.PasteLink` on Ctrl+Shift+V falls back to `Text.Paste` outside a sheet.

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

### S2a — formulas (2026-10-03)

**Values live in `SheetCalc`, not on `SheetCell`.** (user, 2026-10-02; per document at S2a, vault-wide since S2b1)
What a cell shows comes from `Shown`, which crosses layers, so a value on one layer's cell is the wrong owner.
`SheetCell` stays exactly what the file holds. Only formula cells are cached; a typed cell reads `FromRaw` each
time, which keeps `1.50` as typed.

**The node is `(SheetPage, key)`, so it already names its file.** (user, 2026-10-03: S2a then S2b)
S2b moves the graph from one per document to one per vault; the node type and the Kahn pass stay. Cross-page
references were asked for together with cross-file ones and notes reading sheets; only the first is S2a.

**A reference reads the shown cell.** (user, 2026-10-02) A formula sees what the grid shows. A formula on a covered
or hidden layer is not evaluated. Per-layer reference syntax was rejected for now.

**Kahn order, not recursive evaluation.** A 10,000-cell chain would recurse 10,000 deep. Kahn is a loop, and its
leftover set is exactly the cells in or behind a cycle — no separate cycle walk. `Sheet.FormulaRecalc` runs the
10,000 chain.

**Excel's precedence.** (user, 2026-10-03) Unary minus above `^` (`-2^2` = 4), `^` left-associative (`2^3^2` = 64).
Spreadsheet users expect Excel, not maths.

**Errors are values that propagate, left operand first.** `#ERROR!` for an unparseable formula keeps the raw text
(Google Sheets' shape) rather than refusing the entry, which needs a prompt the grid does not have. `#NUM!` for a
non-finite result and `#REF!` past Excel's grid (16,384 × 1,048,576) were added during the build — the agreed list
did not cover overflow or a grid bound.

**Copy writes values; Ctrl+V pastes values; Paste link is Ctrl+Shift+V (S2b).** (user, 2026-10-03) The OS clipboard
wants values, and paste does not adjust relative references, so a pasted formula would point at the old cells.

### S2b1/S2b2 — across files, Paste link (2026-10-03)

**One loaded copy per file, shared by every tab.** Live cross-file reads need one truth per file; two copies of a
sheet open in two tabs would diverge. The cost — two tabs edit one history — is why undo moved onto the document
(user, 2026-10-03: one history per sheet file). `unsaved` stays per editor; every editor of the edited file sets it.

**Excel's `[file]Page!` syntax, page required; the file part resolves like a vault link.** (user, 2026-10-03) First
match wins on duplicate names, as `[[links]]` do; a folder path ending (`[Finance/Budget]`) disambiguates.

**Linking queues a loaded file instead of recursing.** Resolving `[Budget]` while linking `Summary` loads Budget,
whose own links may resolve back to Summary mid-link. `Settle` drains a queue and re-runs Kahn until nothing is
queued, so evaluation never sees a half-linked document.

**A rename rewrites references; a delete, or an outside rename, is `#REF!`.** (user, 2026-10-03) Rewriting by
re-parsing and saving is safe for sheets — `Sheet.XmlRoundTrip` proves the writer reproduces the file. Only files
that held a reference are written. A loaded sheet with no open tab is saved at once, since nothing else would.

**Thorium's own saves are the freshness boundary.** (user, 2026-10-03) A sheet is loaded once per vault session;
an edit made outside Thorium is seen on the next launch or vault switch. No file watcher.

**Paste link falls back to `Text.Paste` outside a sheet, and its bind precedes Ctrl+V's.** A Ctrl+Shift+V bind
shadows Ctrl+V only when it comes first in `InputMap.inputs.xml` (the matcher consumes triggers in file order, as
`Math.InsertDisplay` before `Math.Insert`). Shadowing would otherwise end Ctrl+Shift+V pasting in notes.

**A test may not share an input action's name.** `Sheet.PasteLink` as a test name crashed boot: the keybind
loader bound the `Test`-category method by name. The test is `Sheet.PasteLinks`.

## Known gaps
- No notes showing cells (S2b3), no formatting, resize, freeze panes, page/layer UI, CSV (S3–S5).
- Paste link uses the source file's base name; with two sheets of that name in the vault the reference resolves to
  the first match, which may be the other one. Same-file other-page links are only test-verified through `Prefix`
  (no page tabs until S4).
- Edits made outside Thorium to a loaded sheet are not seen until relaunch or a vault switch; a loaded sheet is
  never unloaded during a vault session. `Created`/`Deleted`/`Renamed` relink every loaded sheet.
- `SheetControl.ArrangeCore` loops forever on an unbounded viewport — an editor laid out as its own root (never
  added to a window) hangs the frame. Found by a test; real tabs are always bounded.
- After `Cut`, Paste link pastes plainly (the copy's source is dropped).
- Formulas: no `$A$1`, no function but `SUM`, no comparisons or strings, references are not rewritten when a page
  is renamed or added (call `SheetCalc.RecalcAll`; no UI does either yet), nor adjusted on paste.
- A range is one edge per cell: `SUM(A1:Z100000)` records 2.6M edges.
- Showing or hiding a layer needs `RecalcAll`; nothing calls it yet (no layer UI until S4).
- Errors draw in plain ink, left-aligned.
- Arrows inside an open cell move the caret (Excel's edit mode); there is no enter mode where arrows commit.
- Paste reads plain TSV — quoted fields with embedded tabs/newlines are split literally.
- Cut-off text shows a partial glyph at the cell edge (label clip), no ellipsis.
- Select-all (Ctrl+A) selects the used range, not the whole grid.
- No hit-testing on headers (clicks there do nothing), no autoscroll on drag-select past the edge.
- `File ▸ Save note` (`Text.Save`) does not reach a sheet; Ctrl+S does (`Sheet.Save`).
- **NOT GUI-verified** — test- and golden-verified only (`Sheet.*`, `Sheet.GridDraws.Grid.png`; formulas
  `Sheet.FormulaEval`, `FormulaRecalc`, `FormulaView`; across files `Sheet.CrossFile`, `RenameRewrites`, `PasteLinks`).

Related: [[document-tables]], [[session-restore]], [[vault-browser-and-shell]], [[document-undo]], [[text-clipboard]]
