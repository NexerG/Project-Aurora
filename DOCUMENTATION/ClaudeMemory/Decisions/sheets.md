# Decision — a sheet is its own file kind, drawn visible-cells-only, with pages and layers in the format from day one

**Date:** 2026-10-02
**Scope:** `ArctisAurora.Core.UI` — `SheetDocument`, `SheetPage`, `SheetLayer`, `SheetCell`, `SheetXml`,
`SheetControl`, `SheetEditorControl`, `SheetCellEdit`, `SheetActions`, `IFileEditor`, `TabViewControl.FileEditorOf`,
`SheetFormula`, `SheetValue`, `SheetCalc`, `SheetCellId`, `SheetBook`, `SheetLinks`, `SheetBox`, `SheetBoxCell`,
`StyleSpan.sheetRef`, `Run.sheet`, `TextInputActions.PasteLink`, `SheetLinks.ExpandMath`/`RenameMath`/`HasMathLinks`,
`TextRunControl.MathBoxFor`, `FormulaPopup.PasteLink`;
`Thorium.Editor.CustomControls.VaultBrowserControl` (`BuildSheetTab`, `NewSheet`, `BaseName`, `Extension`, `FindSheet`),
`Thorium.Editor.VaultsWindow.Enter`

**Status:** PARTIAL — S1, S2a, S2b1, S2b2, S2b3 and S2c of [sheets-plan](../Context/sheets-plan.md) landed; S3 (formatting) is next.

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
  viewport. Unused pooled parts arrange to `Hidden` (zero-size at the sheet's own corner, see S3); pools never shrink.
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
  matches pastes plainly. `Sheet.PasteLink` on Ctrl+Shift+V falls back to `Text.PasteLink` outside a sheet (S2b3).
- **S2b3 sheet links in notes (2026-10-04).** `SheetLinks` (static, `ArctisAurora.Core.UI`): `Parse(reference, out file, out page,
  out top, out left, out bottom, out right)`, `Reference(path, page, top, left, bottom, right)`, `IsLink(target)`,
  `Layout(reference, in TextMeasurer.Run, LineMetrics, zoom) → SheetBox`, `Plain(reference)`, `Renamed(oldPath, newPath, notes)`,
  private `RewriteFile`. Same file: `SheetBox` (width, height, range, cells, rules) and `SheetBoxCell(text, rect, pen, baseline, error)` record struct.
- Reference `file#Page!A1` or `file#Page!A1:B2`; file part = sheet file name with extension (`Budget.sheet.xml`); split at the
  first `#` and the last `!`; range ends ordered.
- `StyleSpan.sheetRef` / `IsSheet`; `IsObject` now includes it; `AsText` clears it. `Run.sheet` → XML attribute
  `<Run Sheet="Budget.sheet.xml#Data!B1"/>`; `AppendRun` / `Runs()` carry it.
- `TextRunControl`: `_runSheet` list; the `BuildRuns` sheet branch measures a `SheetBox` through the object-box
  `TextMeasurer.Run` (math flag, no depth); `WriteSheet` draws it in `Emit`.
- `DocumentControl` region `sheet links`: `PasteLink(text)`, `RefreshSheetLinks()`, `RenameSheetLinks(Func<string,string?>)`;
  `PlainText` (copy) writes `SheetLinks.Plain` for sheet spans. `DocumentEditorControl`: subscribes to `SheetBook.changed`
  (ctor), unsubscribes in the new `OnDestroy`; `BookChanged` → `RefreshSheetLinks`; `PasteLink()` (step "Paste link");
  `RenameSheetLinks(rename)` passthrough. `SheetEditorControl.CopiedReference(clipboardText)` (internal static).
- **S2c note math reads sheet cells (2026-10-04).** `\sheet{Budget.sheet.xml#Data!B1}` in a formula's TeX typesets the cell's live value; substitution only, nothing is evaluated.
  `SheetLinks` region `math`: `HasMathLinks(source)`, `ExpandMath(source)`, private `MathValue(reference)`, `RenameMath(source, rename)`; regexes `xmlMath` (`<Run … Math="…">`) and `mathLink` (`\sheet{…}`).
  `RewriteFile` also rewrites `\sheet{…}` tokens (whole text in `.md`; inside `Math="…"` attributes in `.xml`, decoded then re-encoded). New private `Escaped(value)` = XAttribute serialization,
  now also used for the `Sheet` attribute (replaces `SecurityElement.Escape` from S2b3; keeps `&#xA;` for line breaks in a display formula's source).
- `TextRunControl.MathBoxFor`: a formula holding a link is parsed and cached under `ExpandMath(source)`; `MathParser` unchanged and pure. `DocumentControl.RefreshSheetLinks` also invalidates blocks
  with a formula holding a link; `RenameSheetLinks` also rewrites formula sources through `RenameMath`.
- `FormulaPopup`: static `open` (the popup being edited), `PasteLink()` inserts `\sheet{ref}` into its source box via `SheetEditorControl.CopiedReference`, through `TextBoxControl.Paste` (so the live preview updates).
  `TextInputActions.PasteLink` (`Text.PasteLink`) order is now `FormulaPopup.PasteLink()` → note editor `PasteLink()` → plain `Paste()`; keys unchanged (Ctrl+Shift+V → `Sheet.PasteLink` → `Text.PasteLink` outside a sheet).
- Test `Sheet.MathLinks` (`SheetTests.cs`, `Sheet.tests.xml`), golden `Sheet.MathLinks.Math.png`.
- `TextInputActions.PasteLink` = action `Text.PasteLink`; `SheetActions.PasteLink` falls back to it instead of `Paste()`.
  No keybind change: Ctrl+Shift+V stays on `Sheet.PasteLink`.
- `MarkdownFormat`: new `embed` regex; `ParseInline` reads `![[X.sheet.xml#…]]` as a Sheet run; `Compose` writes it back;
  `Flatten` style key `sheet|ref`.
- `SheetBook.Names`, `Renamed(file, oldName, newName)`, `BaseName`, `Stem` went private → internal. Thorium
  `VaultBrowserControl.RenameNote` calls `SheetLinks.Renamed(path, target, VaultNotes())` after `SheetBook.Renamed`; new
  private `VaultNotes()` (vault `.md` + `.xml`, `*.sheet.xml` excluded).
- Tests (`SheetTests`, `Sheet.tests.xml`): `Sheet.NoteFormats`, `Sheet.NoteLinks` (goldens `Sheet.NoteLinks.Links.png`,
  `Sheet.NoteLinks.Edited.png`), `Sheet.NotePasteLinks`, `Sheet.NoteRenameRewrites`. Fixture `FolderResolver` strips `.sheet.xml` like
  `FindSheet`; new fixtures `BudgetSheet`, `NoteBlocks`, `NoteContent`.

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

**Paste link falls back to `Text.PasteLink` outside a sheet (a plain paste when no link applies), and its bind precedes Ctrl+V's.** A Ctrl+Shift+V bind
shadows Ctrl+V only when it comes first in `InputMap.inputs.xml` (the matcher consumes triggers in file order, as
`Math.InsertDisplay` before `Math.Insert`). Shadowing would otherwise end Ctrl+Shift+V pasting in notes.

**A test may not share an input action's name.** `Sheet.PasteLink` as a test name crashed boot: the keybind
loader bound the `Test`-category method by name. The test is `Sheet.PasteLinks`.

### S2b3 — sheet links in notes (2026-10-04)

**A sheet link is an object character, not a note-level block kind.** (user, 2026-10-04)
U+FFFC plus a `StyleSpan`, like pictures and formulas. Rejected: a block kind beside `TableControl`, which would touch
`Blocks()`, pagination, both writers (`.md` skips tables today) and structural undo. The object path gives undo,
selection, delete, copy and snapshots for free. `IsObject` covers it, so [[math-in-notes]]' rules (typing beside it, never merged) apply.

**A range is display-style; one cell is inline.**
A range's advance is the full column, so it sits on its own line (the display-math trick, F5′); drawn left-aligned,
cut at the column edge. One cell is inline, its box the value's text advance in the run's font and style, ascent from the line.

**A range draws values and 1 px grid rules only, no headers.** (user, 2026-10-04)
Rules are `PaletteRole.Line`. Rejected: column/row headers. Geometry is the sheet page's column widths and row heights ×
note `textZoom`; text in the run's font and size, vertically centred; numbers right-align when they fit (sheet inset 4 px),
as in the grid. Errors (`#REF!`, `#DIV/0!`, …) draw in `PaletteRole.Danger`, like a failed formula.

**Values are not cached on the span.**
`BuildRuns` lays the box out at measure (a `SheetBox` per run), so a remeasure re-reads `SheetBook.calc`. Refresh =
every open note editor invalidates the blocks holding a sheet span on `SheetBook.changed` (edit-time walk, not per frame).
Emit skips grid rows and rules outside the clip. A link to an unloaded sheet loads it through `SheetBook.Resolve` during
measure, as pictures load in `BuildRuns`.

**Paste link in a note only when the clipboard is the sheet's last copy.**
The clipboard text must equal the sheet's last copy and the source must have a path; otherwise (stale clipboard, `.txt`
note, read-only, code or rule block) it is a plain paste. One undo step ("Paste link", an `InsertRangeEdit`). Ctrl+V of a
sheet copy in a note stays plain text (tabs → spaces). `Text.PasteLink` walks to the nearest `DocumentEditorControl`
rather than adding `PasteLink` to `IClipboardTarget` — no interface change.

**Copying a sheet object out of a note writes its values.** (user, 2026-10-04)
Plain text is one value or TSV rows, not the `![[…]]` reference — "the OS clipboard gets values". Rejected: the reference
text. Our own in-process paste keeps the live span (fragment).

**Markdown: a sheet link only when the file part ends `.sheet.xml`.**
`![[Budget#…]]` without the extension stays text because it clashes with note-heading embeds. Literal typed embed text is
escaped by the writer's existing re-read check.

**A rename reaches notes by a token-level rewrite.** (user, 2026-10-04)
Re-saving a note does not reproduce its bytes, so only the link text changes (`.xml`: the `Sheet="…"` attribute on a
`<Run`, decoded and escaped; `.md`: `![[…]]`). BOM and line endings preserved; only files holding a matching link are
written. Open notes (found through `TabViewControl.FindOpenDocuments`): spans rewritten in memory too and the note's dirty
state left as is, so memory and disk agree whether the user saves or discards. Rejected: memory only + mark dirty.

### S2c — note math reads sheet cells (2026-10-04)

**Substitution only, not evaluation.** (user, 2026-10-04)
Formulas still only typeset. Rejected for S2c: a TeX-tree evaluator (digits, `+ − × · /`, `\frac`, `^`, `\sqrt`, parentheses) with a result after a trailing `=` — a slice of its own, recorded as possible S2d, not designed.

**Syntax `\sheet{…}` holding the same reference string as note links.** (user, 2026-10-04)
One format (`file#Page!A1`), and the rename rewrite reuses `SheetLinks`. Rejected: bare Excel `[Budget]Data!B1`, which clashes with TeX's bracket (optional-argument) syntax.

**Expansion happens before parsing, and the box cache is keyed by the expanded source.** (user, 2026-10-04)
A number becomes `{value}` (math digits), text becomes `\text{value}` with `{`, `}`, `\` stripped, a range becomes `\text{#VALUE!}`, a missing file/page/address `\text{#REF!}`.
Rejected for a range: a matrix, which needs math environments — `\begin` fails today. A changed value is a new cache key, so the cache never goes stale; `MathParser` stays a pure string → tree function.

**Ctrl+Shift+V in the formula popup inserts `\sheet{ref}` at the field caret when the clipboard is the sheet's last copy.** (user, 2026-10-04)
Otherwise the field pastes plainly.

### S3 — formatting and resize (2026-10-04)

**What landed.**
- `SheetFormat` (in SheetDocument.cs): `readonly record struct SheetFormat(bool bold, string? fill, string? number)`; `number` is a .NET custom numeric format string (null = General), `fill` a hex.
- `SheetPage.formats : Dictionary<long, SheetFormat>` keyed by `SheetDocument.Key`; `Format(row, column)`, `SetFormat(row, column, format)` (the default format removes the key).
- `SheetValue.Display(string? format)`: a Number through the format, anything else as `Display()`.
- `SheetXml`: `<Format At="B3" Bold="true" Fill="#C8E6A0" Number="#,##0.00"/>` under `<Page>`, after `<Row>`, row-major; older files load unchanged.
- `SheetEdits.cs`: `SheetFormatEdit` (list of row, column, before, after) and `SheetBandEdit` (column or row, index, size before/after, null = default); both raise `SheetBook.Changed(document, page, [])` so the tab goes unsaved and open notes redraw.
- `SheetEditorControl` region `formatting`: `ToggleBold()`, `SetFill(hex)`, `SetNumberFormat(number)`, private `Restyle(label, change)` (one undo step over `Range()`), internal `ResizeBand(column, index, before, after)`, `IsSelected(row, column)`.
- `SheetControl`: `fills` Parts first in draw order (pooled `fillParts`); bold through the cell label's run `style` (`FontStyle.Bold`); grid text through `Display(format.number)`; header edge resize — `EdgeAt(point, out column, out index)` (±4 px `grabWidth` around a column header's right edge or a row header's bottom edge), `OnPointerPress` starts it, `OnDrag` writes the size live (min `minimumBand` = 8 px), `OnDragStop` records it through `ResizeBand`; `OnPointerMove`/`OnPointerExit` set HResize/VResize/Arrow through `ShowCursor`; a right press on a cell outside the selection selects it.
- `SheetLinks.Layout` (inline value and range grid) uses the page's number format; `SheetLinks.Plain` and `ExpandMath` stay unformatted.
- `SheetActions` region `formatting`: `Sheet.Bold`, `Sheet.FillNone/Yellow/Green/Blue/Pink/Orange/Purple` (read `DocumentToolbarControl.highlightOptions`, now `internal`), `Sheet.FormatGeneral` (null), `Sheet.FormatNumber` (`#,##0.00`), `Sheet.FormatPercent` (`0.00%`), `Sheet.FormatCurrency` (`€#,##0.00`).
- Data: `InputMap.inputs.xml` Ctrl+B → `Sheet.Bold` beside `Text.Bold` (each checks its own editor); new `Menus/Sheet.menu.xml` (Cut, Copy, Paste, Paste link | Bold, Fill ▸, Number format ▸) registered as `sheet` in `ThoriumAssets.assets.xml`; Thorium `VaultBrowserControl.BuildSheetTab` sets `contextMenu = "sheet"`; `EngineFonts.imports.xml` Latin charset gains `€`.

**Formats live per page, shared by all layers, like band sizes.**
Rejected: per layer on `SheetCell` — it would break "an empty cell is absent", which `SheetLayer.Set`, `SheetPage.Used` and the formula graph rely on; a formatted empty cell would need a cell with no text. Consequences: a cell can be formatted while empty; Delete/Clear empties contents and keeps the format (Excel behaviour); cut/paste move values only, formats stay where they are.

**A number format is a .NET custom numeric format string, rendered with InvariantCulture.** (user, 2026-10-04)
Presets are data in actions, so adding one is an action plus a menu line. General and Scientific are one preset: General already shows G15, which switches to E-notation for very large or small numbers. Currency is euros.

**If a cell's value is a number, a number is copied.** (user, 2026-10-04)
Copy writes unformatted values (`12.50%` copies `0.125`, `€1,234.50` copies `1234.5`), so copy-then-paste inside a sheet still yields numbers. Same for `SheetLinks.Plain` (a note's plain-text copy). Note math (`\sheet{…}` substitution) stays unformatted because it must parse. Note link display (inline and range) shows the formatted text so the note matches the sheet.

**Bold toggle is Excel's.**
If the active cell is bold, unbold the whole selection, else bold it.

**Formats are applied from Ctrl+B and a grid context menu; fill swatches are the note highlight swatches (one list).**
Rejected for now: a sheet toolbar swapping in for `DocumentToolbar` — a new control and a `UI.ui.xml` change; deferred until workspace tabs reshape the chrome.

**Resize is recorded in `OnDragStop`, not `OnPointerRelease`.**
A release during a drag goes to `UIEngine.EndDrag`, never to the control's release handler. The drag writes the size live and one `SheetBandEdit` is pushed at the end.

**Spare pooled parts hide at the sheet's own corner (`Hidden`), not at `LayoutRect.Empty`.**
`SheetControl` arranges its `Parts` layers at the sheet's whole rect before arranging their pooled children. A spare child hidden with `LayoutRect.Empty` sits at absolute (0,0), and `LayoutRect.Union` does not skip zero-size rects, so when the sheet sat away from the origin the layer's cached `subtreeBounds` missed the child (DEBUG `VerifySubtreeCache` / skipped-layout check logged `subtreeBounds != recomputed`; the hit-test early-out could be wrong). Pre-existing since S1 (reproduced by scrolling a sheet at x=500 past its text with S1 code only); earlier tests had the sheet at x=0, where the origin is inside the rect. `Hidden` replaces `LayoutRect.Empty` for spare pooled lines, fills, labels and header names, so every child stays inside its layer's rect.
Rejected / not done: making `Union` skip empty rects engine-wide (touches every control). `selection` and `field` still arrange to `LayoutRect.Empty` — they are SheetControl's direct children, unioned after `ArrangeCore`, so consistent, just a looser bound.

**`€` was added to the Latin charset** because currency drew a blank glyph. Chosen over an `EUR` suffix or `$` (user). Thorium's arial, arialbd, Electrolize and consola atlases were re-baked.

## Known gaps
- No freeze panes, page/layer UI, CSV (S4–S5).
- Formatting (S3): no italic, underline, text colour, alignment, borders or font size in cells; no dates or date formats, custom format entry, increase/decrease decimals.
- Resize (S3): no autofit on edge double-click, no resizing several selected columns at once, no Esc to cancel a drag; no whole-column/row selection from headers.
- Formats do not travel with copy/paste/cut.
- The open edit field does not show bold.
- Bold and fill do not show in notes' linked cells (number format does).
- A note's range grid updates on resize only when the drag ends, not live.
- `ShowCursor` caches the last shape it set; another control changing the cursor in between can leave it out of step.
- Carbon and AuroraEditor carry their own font atlases; they re-bake with `€` on their next run (the tree goes dirty then).
- Sheet links in notes: a range wider than the column is cut at the column edge; a range taller than a page does not split across pages.
- An empty linked cell has zero width inline (invisible, hard to select).
- Undo records made before a rename still hold the old reference; undoing past a rename brings back a `#REF!` link.
- A file name containing `#` or `]]`, or a page name containing `!`, breaks the reference format.
- Every open note walks its blocks on each sheet edit (edit-time cost, not per frame).
- No click action on a link (open the sheet).
- Note math (S2c): the static math layout cache gains one entry per distinct value a linked formula has shown, and nothing evicts (edit-rate growth, not per frame).
- Obsidian renders `\sheet{…}` as an unknown command. Plain-text copy of a formula writes its source with `\sheet{…}`, not the value.
- An error value inside a formula (`#REF!`, `#VALUE!`) draws in ink, not `Danger` — only an unparseable formula is red.
- No number formatting inside formulas; the value appears as the sheet displays it (G15, so a large number can read `1E+20`).
- In `.md`, a `\sheet{…}` naming the renamed sheet is rewritten wherever it appears in the file, including in plain text outside `$…$`.
- No evaluation of formulas (possible S2d).
- Renaming or adding sheet pages does not rewrite note links (sheets do not either).
- Non-note `.xml` files in the vault holding `<Run … Sheet="` would be rewritten too (unlikely).
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
- Headers: dragging an edge resizes (S3), but plain header clicks do nothing; no autoscroll on drag-select past the edge.
- `File ▸ Save note` (`Text.Save`) does not reach a sheet; Ctrl+S does (`Sheet.Save`).
- **NOT GUI-verified** — test- and golden-verified only (`Sheet.*`, `Sheet.GridDraws.Grid.png`; formulas
  `Sheet.FormulaEval`, `FormulaRecalc`, `FormulaView`; across files `Sheet.CrossFile`, `RenameRewrites`, `PasteLinks`; notes `Sheet.NoteFormats`, `NoteLinks` (goldens
  `Sheet.NoteLinks.Links.png`, `Sheet.NoteLinks.Edited.png`), `NotePasteLinks`, `NoteRenameRewrites`). S2b3: `Sheet` suite 16/16, full `--test`
  136 passed, 41 skipped, 1 failed = Boot (pre-existing default-sampler error); no real session used Ctrl+Shift+V into a note or renamed a sheet from the vault browser.
  S2c: `Sheet.MathLinks` (golden `Sheet.MathLinks.Math.png`) checks the expansion of number, text, range (`#VALUE!`) and missing sheet (`#REF!`); a formula widens after a sheet edit while the note stays clean;
  Ctrl+M then Ctrl+Shift+V in the popup puts `\sheet{Budget.sheet.xml#Data!B1}` in the field and Enter commits it; a stale clipboard pastes plainly; a rename rewrites an open note's formula, an `.xml` note's `Math` attribute
  byte-for-byte otherwise (including `&#xA;` and `&lt;`), and an `.md` note's `$…$`. `Sheet` suite 17/17, full `--test` 137 passed, 41 skipped, 1 failed = Boot (pre-existing default-sampler error); no `[Vulkan]` lines.
  Golden looked at before approval ("x a = 20 y", "#REF! end"). Typing `\sheet{…}` and Ctrl+Shift+V in the popup were not tried in a real session.
  S3: test- and golden-verified. `--test=Sheet` 19 passed (suite 19/19), full `--test` 139 passed, 41 skipped, 1 failed = Boot (pre-existing default-sampler error); no `[Vulkan]` lines.
  `Sheet.Formats` checks the Format XML round trip; Ctrl+B bolds a selection, a second Ctrl+B unbolds and leaves no format; fill; Number `1,234.50`, Currency `€1,234.50`, Percent on a formula `24.69%`, text ignores a number format; Copy of a formatted number gives `1234.5`; `ExpandMath` gives `{1234.5}`; Delete keeps the format; undo/redo; unsaved; formats saved.
  Golden `Sheet.Formats.Grid.png` looked at before approval (bold, orange fill, `€1,234.50`, `24.69%` in the grid, the note's inline and range links formatted). `Sheet.Resize` checks a drag of B's right edge → 160 px, unsaved, undo removes the width, redo; row 1 drag deepens it and the note's linked range grows; a row dragged past its top clamps at 8 px; a press away from an edge still selects; sizes saved. Existing goldens still matched after the atlas re-bake.
  NOT GUI-verified: the right-click menu (Fill/Number format submenus, right-press selecting), resize cursor shapes on hover, Ctrl+B in a real session, resize feel.

Related: [[document-tables]], [[session-restore]], [[vault-browser-and-shell]], [[document-undo]], [[text-clipboard]]
