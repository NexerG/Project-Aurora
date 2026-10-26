# Decision — SwitchControl is the n-option pill, ToggleSwitchControl is the on/off pill

**Date:** 2026-10-23
**Scope:** `ArctisAurora.Core.UI` — `SwitchControl`, `ToggleSwitchControl`, `SettingsWindow`, `InspectorControl`; `Thorium.Tests.StartupTests`

## What changed
- New `SwitchControl : ButtonControl`, `[A_XSDType("Switch", "UI")]`: a segmented pill holding n options; a thumb slides behind the chosen one.
  - `IReadOnlyList<string> options` (code; setting it rebuilds one `LabelControl` per option).
  - `string optionList`, XML `Options`: comma separated, entries trimmed, empties dropped.
  - `int selected`, XML `Selected`: clamped to range; a set from code jumps without animating.
  - `Action<int>? onChanged`: fires on a click that changes the choice; clicking the chosen option reports nothing.
  - Geometry: caption 13 px, track 24 px tall, thumb inset 3, segment padding 10 each side. A segment is as wide as its caption plus padding, so segments differ in width.
  - Thumb and click: see "Rows and the Inspector List row (2026-10-23)" below (the first build's fractional-index thumb was superseded). Tween: `horizontalPosition`, 0.18 s, CubicInOut.
  - Colours: track `PaletteRole.Chrome` (no gradient), thumb `Ground`, chosen caption `Ink`, others `MutedInk`.
- `ToggleSwitchControl` (`<ToggleSwitch>`) rebuilt from "caption | knob track | caption" into an on/off pill: a knob slides, and only the current state's caption shows inside the track, on the side away from the knob.
  - Renames: `leftText`/`LeftText` -> `onText`/`OnText`; `rightText`/`RightText` -> `offText`/`OffText`; `right`/`Right` -> `on`/`On`. `onChanged` stays `Action<bool>`.
  - Layout: on = OnText at left, knob at right; off = knob at left, OffText at right. Track width = knob + inset + 2x caption padding + the wider of the two captions, so it does not resize on toggle. The inactive caption is `Hide()`n.
  - Colours: track `Chrome`; a private child `PanelControl` `lit` (`Accent`) inside the track, its `alpha` tweened 0<->1 alongside the knob's `horizontalPosition`, 0.18 s CubicInOut. Knob `#FFFFFF`, OnText `#FFFFFF`, OffText `MutedInk`. Setting `on` from code jumps (knob and `lit` alpha set directly).
  - On a click the caption swaps at once and the knob slides over it.
- `SettingsWindow.Editor`: enum with 2-4 names -> `SwitchControl` (options = `Words(name)` per name, selected = index of the current value); otherwise `DropdownControl`. `SettingsWindow` no longer uses `ToggleSwitchControl`; nothing in the tree does.
- Removed: gradient `toggle-track` from `Engine.gradients.axml`.
- Tests: `Startup.ToggleSwitch` rewritten (OnText "Yes", OffText "Script"; one caption visible per state, click turns it on and reports, accent layer alpha reaches 1; goldens `Off`, `On`). New `Startup.Switch` (Options "Axml, Markdown, Plain text"; three captions, first chosen; click on the last picks index 2 and reports it; clicking the chosen one reports nothing; goldens `First`, `Last`), added to `Startup.tests.axml`. Old goldens `Startup.ToggleSwitch.Left.png` / `.Right.png` deleted.

## Rows and the Inspector List row (2026-10-23)
- `SwitchControl.columns`, XML `Columns`: most options per row; 0 (default) keeps all on one row. Rows = ceil(n / columns).
  - One `PanelControl` track per row (private `tracks` list, `Chrome`), stacked with a 4 px gap; every row is stretched to the widest row's width, the extra shared evenly over that row's segments. Tracks are measured at the size they are arranged to.
  - Children are rebuilt in order tracks -> thumb -> captions (private `Rebuild()`, called from the `options` and `columns` setters) so captions draw over the thumb.
  - Segment rects (private `LayoutRect[] segments`) are filled in `ArrangeCore`.
- Thumb movement: `horizontalPosition` is the tween's progress 0..1 from the private `_from` index's segment rect to the chosen one's, lerping x, y, width and height - a straight slide, diagonal across rows. On a click `_from` = the previous choice, progress reset to 0, tween to 1 (0.18 s CubicInOut). A code set of `selected` sets `_from` = `selected` and progress 1 (jumps).
- Click picks the caption whose centre is nearest the pointer in 2D (was x only).
- `InspectorControl`: fields `listNone, listBullets, listNumbers, listTasks, listSortedTasks` (LabelControls in two `TabToolsControl` rows) -> one `SwitchControl listShape` (options None, Bullets, Numbers, Tasks, Sorted; `columns = 3`), added to the Paragraph section after a `Caption("List", MutedInk)` line. Index = `(int)ListShape` (enum order unchanged). `onChanged` runs `Note(editor => SetList(editor, (ListShape)index))` then sets `shownList = null`, so the next tick's `ReflectNote` puts the switch back on the real shape when nothing changed (e.g. no caret). `ReflectNote` sets `listShape.selected` only when it differs, so it does not cut a running slide.
- Tests: `Startup.Switch` gains a rows case (five options, `Columns` 3 -> two tracks of one width, second below the first; a click on "Sorted" picks index 4; golden `Rows`). `Inspector.Docs` places the caret on the note's first block, clicks Tasks on the List switch (paragraph becomes `ListKind.Task`, switch `selected` 3), undoes (switch back to 0). `Ribbon.Shows` / `Inspector.Docs` width expectations restored to 240 px and 300 px after a splitter drag - the sorted-checklist commit had raised them to 291.4 / 351.4 because "Sorted tasks" overflowed the 240 px column.

## Why these choices

**"Switch" means a segmented pill that takes n options.**
The user's "switch control" was a segmented pill (picture: "This is a toggle | button", white thumb behind the chosen side, dark track), not a phone-style knob, and had to take n options (e.g. list kinds). Hence `SwitchControl`.

**The n-option pill is `SwitchControl` / `<Switch>` (user choice A2).**
Rejected: keeping the name `ToggleSwitchControl` for it — "toggle" undersells n options, and the user then asked for a real on/off toggle as a separate control.

**The on/off toggle stays `ToggleSwitchControl` (user, picture of green pills "Yes" / "Script" / "HTML").**
Each state has its own caption; only the current one shows, opposite the knob.

**Settings gives 2-4 enum names a `SwitchControl` (user choice B2).**
Rejected: exactly two only (the old behaviour). So New notes `Axml | Markdown | Plain text` is now a switch. Five or more stays a dropdown.

**`SwitchControl` colours: thumb Ground, chosen Ink, rest MutedInk, track plain Chrome (user choice D1, then "drop the gradient").**
Rejected: a fixed white thumb with dark text in both themes (glares in dark mode). The first build kept the `toggle-track` blue gradient; MutedInk captions on it were hard to read, and the user chose a plain Chrome track over keeping blue with light captions.

**`ToggleSwitchControl` caption colours: OnText white on Accent, OffText MutedInk on Chrome (user choice F1).**
Rejected: white in both states, unreadable on light-grey Chrome in the light palette.

**The toggle track fades Chrome -> Accent through a layer (user choice G2a).**
Paint colour is not animatable — only layout values, `alpha` and `ButtonControl.state` are `[A_Animatable]` — so the fade is an Accent layer inside the track whose alpha tweens with the knob. Rejected: an instant role swap (G1); making colour animatable in the engine (G2b, a rendering change). The layer is the track's child, not the switch's, because `ButtonControl.enabled` resets the alpha of a button's direct children (1 or 0.4) and would show the layer while off.

**`ToggleSwitchControl` has no consumer yet (user choice E1).**
It is a reusable control. Rejected: Settings bool rows as On/Off toggles (they stay checkboxes).

**"Multilevel" = the switch wraps onto rows (user, 2026-10-23).**
The List row did not fit: five captions at 13 px with 10 px segment padding made the inspector 322 px wide (column is 240). Rejected: settable caption size/padding on `SwitchControl`, 12 px / 6 px in the inspector (W1); accepting a wider inspector (W2).

**Rows break at a `Columns` count (user choice M1a).**
Rejected: a `|` break in `Options` (uneven rows; M1b); wrapping automatically to the offered width (M1c) - risky because the inspector grew to its content instead of offering the switch a fixed width.

**One pill per row, rows stretched to the widest (user choice M2a).**
Rejected: one rounded box holding all rows as a grid (M2b).

**The thumb slides straight, diagonally across rows (user choice M3a).**
Rejected: fade out on the old row and in on the new (M3b). Cost: on one row, a jump of several options now blends width straight from old to new instead of passing through each width between.

**The caption stays "Sorted" (user choice M4b).**
Rejected: restoring "Sorted tasks" now that its row has room (M4a).

**The List caption sits on its own line (user choice C1, earlier 2026-10-23).**

## Known gaps
- Golden-verified (user approved the shots 2026-10-23): `Startup.Switch` First/Last/Rows, `Startup.ToggleSwitch` Off/On, `Inspector.Docs` Panel (240x690). Full suite 382 passed, only the baseline Boot error; no `[Vulkan]` lines. NOT GUI-verified: the List switch in a live window (slide between rows, clicks), dark palette.
- The "List" caption is left-aligned on its own line while the other Paragraph/Page row labels (Style, Align, Layout...) are right-aligned in a 76 px label column; not changed.
- Interrupting a slide with a second click jumps the thumb to the previous choice before sliding (`_from` = previous target index, not the thumb's current rect).
- The PNGs were read: the switch shows three captions with the thumb on the first, then on the last; the toggle off shows "Script" right of a left knob on grey, on shows white "Yes" left of a right knob on Accent blue.
- NOT GUI-verified: the Settings window (the New notes row as a 3-option switch, its fit in the 220 px editor column), the mid-slide fade in a live window, the dark palette.
- Title-bar "Thorium" menu closing without acting: fixed 2026-10-23, not a switch defect — see [[context-menus]] § Entries closing without acting.

Related: [[start-at-login]], [[sorted-checklist]], [[measure-child-panel-at-arrange-size]]
