# Decision — a block keeps its flattened advances; a rewrap only re-breaks

**Date:** 2026-10-02
**Scope:** `ArctisAurora.Core.UI` — `TextMeasurer` (`Flatten`, `MeasureBlock`, `MeasureAround`, `FillLine`, `AppendLine`,
`AdvanceAt`, `RunOf`), `BlockLayout` (`advances`, `flags`, `count`, `runs`, `MeasuredRun`); `ArctisAurora.Tests.LayoutTests`

## What changed
- `PenChar` and the static `TextMeasurer._penChars` are gone. Each `BlockLayout` keeps its own flattened characters as
  two arrays: `float[] advances` and `byte[] flags` (`BreakAfter`, `Picture`, `Tab`), grown ×2 from 16, never shrunk.
- `BlockLayout.runs` is a `List<MeasuredRun>`: per run its key (`text` reference, `charStart`, `charCount`, `atlas`
  reference, `face`, `fontSize`, `picture`, `math`, `floating`), where its characters start (`start`, `length`) and
  its line box (`ascent`, `descent`).
- `Flatten(layout, runs, …)` compares every run with `MeasuredRun.Matches`. All match → text-run characters are kept;
  any mismatch → every text character is re-measured. Picture and math characters are rewritten every measure. Line
  boxes are recomputed per run every measure.
- The break loop, `FillLine`, `MeasureAround` and `AppendLine` read the arrays; `runIndex`/`charIndex` and the line box
  come from `MeasuredRun` (`RunOf` scans for the run holding a character — used for tabs and once per line).
- `AppendLine` walks the runs overlapping the line, one segment per run with characters on it.
- No signature changed. `TextRunControl` already passes `_layout` as `reuse` from `MeasureCore` and `LayoutAround`.
- `Layout.RewrapMatchesFresh`: mixed runs, two tabs, a formula run, a 45-letter word; 400 → 150 → 400 px plainly and
  around a 40 px band, then a new string of equal length. Every reused measure must equal a fresh one bit for bit.

## Why these choices

**The cache validates itself against the runs instead of trusting `InvalidateLayout`.**
`StyleSpan`s are mutated directly in ~20 places (`MarkCode`, `SplitSpanAt`, `MergeSpans`, …) and not all were checked
to invalidate. A key compare is O(runs) and catches a new string (typing), a new size (zoom), a new atlas (font
re-import creates a new `AtlasMetaData`) and a restyle, with no contract on callers.

**`BuildRuns` still runs on a rewrap.** 0.2–0.4 ms for 1000 blocks, and it picks up paint/gradient/effect changes
that do not invalidate layout. Skipping it was rejected for that risk.

**Two arrays (5 B/char), not a `PenChar[]` per block (24 B/char).** The 1M-char note would keep ~24 MB, past the
16 MB L3, and stream it every rewrap where the old shared 24 KB scratch sat in L1. Ascent/descent were per character
only to be maxed per line; they are per run.

**Bit-identical to the old measurer.** Same advance values, summed in the same order (pen, segment and line widths).
Word-at-a-time stepping was deliberately left out (user, 2026-10-02): pre-summing a word's width changes float
summation order, so breaks could differ at the boundary. It is the next step if rewrap still matters.

## Measurements

`Perf.RewrapLargeNote` (1000 × 1000-char blocks, page width 1 mm a tick), Release+`PROFILE`, `--test=Perf`.

| | old | new |
|---|---|---|
| `Text.MeasureBlock` p50, tiering on, alternated pairs | 8.79 / 7.71 / 7.95 ms | 2.96 / 3.13 / 7.45 |
| `Text.MeasureBlock` p50, tiering on, 3 more new runs | — | 3.06 / 2.89 / 3.18 |
| `Step.Main.Layout` p95, tiering on, same 6 new runs | 11.42 / 8.78 / 9.13 ms | 4.52 / 4.51 / 8.58 / 4.46 / 4.12 / 4.69 |
| `Text.MeasureBlock` p50, `DOTNET_TieredCompilation=0`, 2 pairs | 11.73 / 11.58 ms | 4.93 / 4.97 |
| `Step.Main.Layout` p95, `DOTNET_TieredCompilation=0`, 2 pairs | 13.23 / 13.21 ms | 6.85 / 6.98 |

| `Perf.TypeLargeNote` `Step.Main.Layout` p50 / p95 | old (3) | new (6) |
|---|---|---|
| tiering on | 0.20–0.23 / 0.60–0.72 ms | 0.20–0.31 / 0.52–0.76 |

- One new run of six (pair 3) ran the whole rewrap test at old speed, flat across all 120 frames. The test is
  deterministic (nothing reassigns text), and tiering-off pairs show 2.4× every time — read it as the machine's
  slow mode or a JIT state, not a cache miss. Not proven.
- Earlier the same day the old build measured 15–16 ms p50 on this test — the machine's two speed modes
  ([[large-note-measure-cost]]). Only alternated pairs compare.
- Every `RewrapLargeNote` frame's 1,280–5,120 KB "alloc" is the profiler's `CaptureBatch.spans` doubling to hold
  ~2,000 per-block zones a frame, charged to whichever zone is open — not the measurer.

## Known gaps
- `RewrapLargeNote` p95 now passes 8 ms in 5 of 6 runs; `max` still fails (8.2–13.5 ms), and the p95/AllocKB budgets
  in `Perf.tests.xml` were not re-derived.
- Memory: ~5 B per character per block, kept for the block's life (~5 MB at 1M chars, up to 2× with growth).
- The open (first) measure is unchanged in kind — it still flattens everything, on tier-0 code.
- `Align` still re-walks `MeasureAdvance` per character on centred/right lines.
- `RunOf` is a linear scan; a block with very many runs and many lines pays runs × lines.

Related: [[large-note-measure-cost]], [[text-layout-one-measurer]], [[markdown-blocks-and-alignment]], [[engine-testing]]
