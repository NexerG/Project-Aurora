# Decision — a text run's per-run data is two struct arrays, created at first measure

**Date:** 2026-10-29
**Scope:** `ArctisAurora.Core.UI` — `TextRunControl` (private: `RunStyle`, `runStyles`, `RunObject`, `runObjects`, `runCount`, `ObjectSlots`, `_runs`, `_fitted`, `_fittedBefore`, `BuildRuns`, `FittedBox`, `SetPaint`, `PictureSizeAt`, `PictureFrame`), `TextMeasurer.MeasureBlock`, `DocumentControl.PageMargins`

## What changed
- `TextRunControl` (so `LabelControl`, `BlockControl`, every text control) no longer creates 10 parallel per-run lists and 2 dictionaries at construction. Live heap on the 1M-char profile scenario: 28.46 → 24.80 MB (−12.9%).
- Removed (all `List<…>`, eagerly `new`ed): `_runPaints`, `_runFonts`, `_runGradients`, `_runEffects`, `_runDecorations`, `_runTextures`, `_runPictures`, `_runMath`, `_runSheet`.
- Added `struct RunStyle` (font, paint, gradient, effect, highlight, underline, strike) and `RunStyle[] runStyles` — starts `Array.Empty`, grown only when a build needs more runs than it holds.
- Added `struct RunObject` (texture, math, sheet, size, rotation) and `RunObject[]? runObjects` — null until a build meets its first picture, sheet or formula span; cleared to `runCount` at each build start; dropped to null when too short. `ObjectSlots()` is `runObjects ??= new RunObject[runCount]`. Readers treat null as "no object".
- Added `int runCount` (= `Math.Max(1, spans.Count)`, set first in `BuildRuns`).
- `_runs` (`List<TextMeasurer.Run>`) is no longer readonly or eager: created in `BuildRuns` with capacity = run count, so a one-run label gets a 1-slot backing array instead of `List`'s default 4.
- `_fitted` / `_fittedBefore` (width-aware formula caches) are nullable, created in `FittedBox` on first use; `BuildRuns` swaps them and clears whichever exists.
- `SetPaint`, `BuildRuns`, `FittedBox`, `PictureSizeAt`, `PictureFrame`, the draw loop and the formula-box lookup read the arrays instead of the lists.
- No data XML touched.

## Why these choices

**Merge into arrays of structs, not just make the lists lazy.**
Every measured text control fills all ten lists (`BuildRuns` adds one entry per run to each), so lazy creation alone saves nothing for any control that measures — and 3,128 of 3,198 text controls in the scenario do. Merging removes 9 list objects (32 B each) and 9 backing arrays per control. Arrays rather than `List<T>` because the run count is known before filling (aurora-code rules 2, 7); an array grown only on a larger count means rewrap-on-resize allocates nothing.

**Split hot from cold.**
`RunStyle` is what every run's draw reads. `RunObject` (texture, picture size and rotation, `MathBox`, `SheetBox`) is only meaningful for picture/sheet/formula runs, so it lives in a separate array that most controls never create.

**Create at first use, not in a constructor.**
A field initialiser or a base constructor runs for every subclass instance, so moving `new` from the field into the constructor would still charge every `LabelControl`/`BlockControl`. Creating at first use lets a control that never takes that path pay only the 8-byte null reference slot.

**`_runs` stays a `List<TextMeasurer.Run>`.**
`TextMeasurer.MeasureBlock` takes `IReadOnlyList<Run>` and has other callers; changing it to a span was left out to keep the change to one file.

**String interning of XML-authored control strings: considered, rejected by the user.**
Measured duplicate strings in the whole live heap: 74 KB, of which duplicate colour-hex strings 3.5 KB. Interning would be one line at `ControlXml.ResolveAttributes` (`string.Intern(attr.Value)` for string members); avoiding even the parser's temporary copy would need replacing `XElement` with `XmlReader` plus a span-keyed pool. Not worth it.

## Measurements

1M-char `--profile-scenario`, 1,000 blocks × 1,000 chars, window 1280×720. Build: Thorium `-c DebugPerf -p:Optimize=true -p:DefineConstants=TRACE;PROFILE` for `net10.0` on Linux/lavapipe (plain Release fails the ImageSharp licence check). Live-heap snapshots with `dotnet-dump collect --type Heap` + `dumpheap -stat -live`, one run, taken just after the note opens and with the Settings window open. Method: [[measuring-heap-memory]].

| | before | after |
|---|---|---|
| live heap, note open | 28.46 MB | 24.80 MB |
| live heap, settings open | 29.20 MB | 25.31 MB |
| TextRun lists + dictionaries + their arrays (3,198 text controls) | 4.04 MB (≈1.28 KB per measured text control) | ≈0.55 MB (`List<Run>` 32 B + `Run[1]` 88 B + `RunStyle[1]` 56 B per control) |
| `LabelControl` object | 576 B | 520 B |
| `BlockControl` object | 624 B | 576 B |

Base sizes measured from the built assembly (`MethodTable.BaseSize`, `Unsafe.SizeOf`):

| Type | Size |
|---|---|
| `Control` | 392 B |
| `Entity` | 88 B |
| `ArrangeData` | 148 B |
| `LayoutNode` | 16 B |
| `VulkanControl` | 96 B |
| `ControlGeometry` | 96 B |
| `UIElements` row | 284 B per slot (148 + 16 + 96 + 24 handle bookkeeping) |
| empty `List<T>` | 32 B |
| delegate | 64 B |

## Finding, not acted on
- `DocumentControl.PageMargins` creates 6 slot labels and 3 rule panels per page whether the page has a head/foot or not — about 2,180 of the 3,198 text controls in the scenario (~360 pages). Each still pays a full text control.

## Known gaps
- `_runs`, `_fitted`, `_fittedBefore` keep their underscore prefix (aurora-code rule 43) — rename pending the user's answer.
- Run storage is still created at first measure / on growth inside `MeasureCore` (frame-reached code); question to the user on rule 1, unresolved.
- `MeasureBlock` still takes `IReadOnlyList<Run>`, so the `List<Run>` object remains.
- `PageMargins` unconditional labels (above).
- Not checked: Windows run, Windows goldens, GUI by hand, Carbon/AuroraEditor.
- Older notes still name the removed lists as they were when written: [[sheets]] (`_runSheet`), [[note-images]] (`_runTextures`, `_runPictures`), [[ui-effects]] (`_runEffects`), [[ui-palettes]] (`_runPaints`), [[ui-gradients]] (`_runGradients`). Not edited here.

Verification: test-verified — full Thorium suite on Linux/lavapipe (`_Build/test.sh`) before and after, 365 passed / 57 failed / 5 skipped both times with byte-identical failure lines (Boot's pre-existing default-sampler error, Sheet.PasteLinks, Sheet.NotePasteLinks, 54 goldens that fail on lavapipe with the same pixel counts). No `[Vulkan]` lines. Builds clean, no new warnings in `TextRunControl`.

Related: [[measuring-heap-memory]], [[large-note-measure-cost]], [[rewrap-advance-cache]], [[aurora-code-rules]], [[ui-data-control-split]]
