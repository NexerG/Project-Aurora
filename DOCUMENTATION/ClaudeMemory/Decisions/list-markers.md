# Decision — bullets and numbers are one list kind with a marker per list

**Date:** 2026-09-29
**Status:** LANDED. Test-verified (`TextInput.ListMarkersPerList`, `EmptyItemEnterOutdents`) and golden-verified
(`ListMarkersDraw.Markers`, viewed before approval). **NOT GUI-verified:** the right-click "List marker" submenu.
**Scope:** `ArctisAurora.Core.UI` — `ListMarker`, `ListMarkers`, `BlockControl` (`listMarker`, `shownMarker`,
`listNumber`, `ShowMarker`, `SyncMarker`), `ListLevel`, `DocumentLayout.listLevels`/`MarkerFor`, `DocumentControl`
(`RenumberLists`, `ListsChanged`, `SetListMarker`, `TypeMarkdownPrefix`, `SplitBlock`), `DocumentEditorControl.SetListMarker`,
`TextInputActions` (`List.*`), `DocumentXml`, `MarkdownFormat`; `Thorium/…/Menus/Note.menu.xml`

## What changed
- `ListKind` stays `None | Bullet | Task`. **`Bullet` means "a list item with a marker"**; the marker is
  `ListMarker`: `Disc Circle Triangle TriangleOutline Square SquareOutline Decimal UpperAlpha LowerAlpha LowerRoman UpperRoman`
- A block's `listMarker` is its own choice, null = the layout's `MarkerFor(level)`. `DocumentLayout.listLevels` is a
  `<ListLevel Marker>` per level, cycling past the last, inherited from `DocumentSettings` (default: one `Disc`)
- `RenumberLists` (run at the top of `MeasureCore` when `listsDirty`) counts per level: a plain block clears all
  counters, a task clears its level and deeper, a container change starts afresh, **a marker change at a level starts
  a new count there**. It hands each item `ShowMarker(marker, number)`
- `ListsChanged()` is called from `InsertBlockAfter`, `RemoveBlock`, `SetBlockList`, `ShiftListLevel`,
  `RestoreBlocks`, `SetListMarker` and the editor's `SetLayout`
- Markers: shapes are `IconControl`s (`bullet-*.svg` in the default set), numbers a `LabelControl` right-aligned in
  the indent at the block's font size; `ListMarkers.Format` writes `1.`, `A.`, `a.`, `i.`, `I.` (letters a..z, aa..;
  roman to 3999)
- `SetListMarker`: every item at the caret's level in the caret's list (contiguous items at that level or deeper, in
  one container) takes the marker — one `BlockStateEdit`. Right-click → **List marker ▸** (`Note.menu.xml`, eleven
  `List.*` actions); the note editors now name `ContextMenu="note"`
- Typing `1. ` / `1) ` at a plain paragraph's start makes a `Decimal` item; `- ` makes a default-marker item. Enter
  continues an item's marker (`SplitAt` copies `listMarker`)
- **Enter on an empty item outdents it one level, and at level 0 ends the list** — all list kinds (user, 2026-09-29)
- XML: `<Block … Marker="UpperRoman">`, `<DocumentLayout><ListLevel Marker/>`. Markdown: numeric markers write
  `N. ` counted per level with the same restart rules; shapes write `- `; `N.`/`N)` read as `Decimal`

- **Letters and numerals in `.md` (2026-10-01), Pandoc `fancy_lists`.** `BlockLine` writes `ListMarkers.Format`
  (`b.`, `iii.`), with two spaces after a capital and a period, so `A. Smith` stays prose. `MarkdownFormat.Read`
  matches `lettered` and checks the gap with `LetteredGap`. `LetteredMarker` decides the marker:
  - alpha continues only when the token is `NextLetters` of the item above at the same level (`PreviousItem`;
    the token rides an `XElement` annotation);
  - roman if `IsRoman` (canonical, via `Format`) and it is multi-letter, `i`/`I`, or after a roman item;
  - otherwise a single letter starts an alpha list;
  - mixed case is never a list.
  The writer escapes the delimiter (`a\.`) of a paragraph that would read as a list, and of any uniform-case
  token straight after a list item. Test: `TextInput.MarkdownFancyLists`
- **Per-note level defaults in frontmatter:** `ListMarkers: [Decimal, LowerAlpha]` ↔ `<DocumentLayout><ListLevel>`
  → [[note-properties]]

## Why these choices

**One kind with a marker, not a `Numbered` kind.** The user asked for the two to merge; everything list-shaped —
indent, nesting, Enter/Backspace, Tab — was already written against `Bullet`, and a marker is display.

**A marker change restarts the count.** Without it a `1.` typed straight under a bullet list showed `2.`, and Markdown
would read the written `2.` as a new list starting at 2 anyway.

**Renumbering at measure, behind a flag.** Numbers depend on every item above; recomputing at each structural
primitive would be one walk per keystroke-path and easy to miss on a new path. Creating the marker control inside
`MeasureCore` follows the selection boxes, which are already created at arrange.

## Known gaps
- Markdown has no syntax for shapes: a chosen shape writes `- ` and comes back as the default. An unmarked
  item writes `- ` even when the note's defaults make that level numeric. Obsidian renders `a.`/`i.` items
  as paragraphs, not as lists
- `.md` notes with a paragraph starting `i. `, `a) ` and the like now open as lists
- A roman number above 3999 writes as digits and reads back as `Decimal`
- A list always numbers from 1
- The List marker actions act on the caret, not on the item right-clicked
- Shape markers are 6 px at 100% zoom

Related: [[note-file-formats]], [[text-decorations-and-colour]], [[document-tables]]
