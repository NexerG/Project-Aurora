# Mistake — re-deriving a page index from a float y after adding one stride

**Date:** 2026-10-28
**Scope:** `ArctisAurora.Core.UI` — `PageBands.PageOf`, `PageSpace.Push`, `PageSpace.PushLine`, `PageFloats`, `TextRunControl.Paginate`, `DocumentControl.Paginate`; `ArctisAurora.Tests.TexTests` `Tex.PageOfRounding`

## Symptom
- Thorium hangs: main thread at 100% of one core, no frames, on a paged document long enough to hit an unlucky page break.
- Found on a real 35-page bachelor thesis .tex (VU MIF template) that never exported.

## Cause
- `PageSpace.Push` advanced a line to "this page's band top + one stride" and re-derived the page with `PageOf(y)` = `floor((y - top) / stride)`.
- In float32 that division rounds back to the same page for roughly 12% of (top, stride, page) combinations (189,083 of 1,600,000 in a simulation), so the `while (true)` pushed the same line to the same y forever.

## Fix
- `PageOf` adds `PageBands.tolerance` (0.5 px) before dividing; see [[document-pages]] for the rule and the rejected alternative.
- Regression test `Tex.PageOfRounding` uses a (top, stride, page) triple that rounded back before the fix.

## Rule
- Any `while (true)` that advances by "y = bandTop + stride" and re-derives the page from y can loop forever in float32. Derive the page with the tolerance, or carry the integer index instead of recomputing it.

## How it was found
- `dotnet-stack report` samples of the running Debug build: 10 of 10 samples over 30 s sat inside `PageSpace.Push` ← `PushLine` ← `TextRunControl.Paginate` ← `DocumentControl.Paginate` ← `MeasureCore`.
- A hang that is a loop shows identical stacks in every sample; a hang that is a wait shows the blocked call.

## Known gaps
- Test-verified (`_Build/test.sh Tex`) and the thesis exported without a window; the PDF's content against the ground truth is not compared. Not GUI-verified.

Related: [[document-pages]], [[idle-frames]]
