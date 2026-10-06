# Mistake — a sheet `Fill` stores a hex colour, never the menu caption

**What I did wrong (2026-10-06):** hand-writing `Company finances.sheet.xml`, I wrote `<Format Fill="Yellow"/>` and
`Fill="Green"` — the captions of the Fill menu. Opening the sheet threw `Hex color must be 6 characters long.` from
`Control.HexToRGB` when the grid painted the fill.

**Why:** `SheetActions.Fill(caption)` maps the caption through `DocumentToolbarControl.highlightOptions` to a hex
(`Yellow` → `#FFF3A3`, `Green` → `#C8E6A0`, …) before `SheetEditorControl.SetFill`; the file holds that hex.

**Why it was not caught:** the check loaded the sheet through `SheetBook.Get` and read values from `SheetBook.calc` —
no control was built, so no fill was ever painted. Values evaluating is not the sheet opening.

**Now:** `SheetXml.Fill` drops any `Fill` that is not `#RRGGBB` / `RRGGBB`, so a bad colour reads as no fill instead
of crashing (`Sheet.Formats` covers it).

**Rule:** writing a `.sheet.xml` by hand, use the hex from `highlightOptions`, and verify by opening it in a
`SheetEditorControl` (the `ShowSheet` test helper) and showing every page — not by reading values.
