# Decision — measuring a large note: flat glyph advances, reused lines, no closure per paginated block

**Date:** 2026-10-02
**Scope:** `ArctisAurora.Core.Filing` — `AtlasMetaData`; `ArctisAurora.Core.UI` — `TextMeasurer`, `BlockLayout`,
`TextLine`, `TextRunControl`, `DocumentControl`

## What changed
- `AtlasMetaData.BuildCharIndex` also fills `advances`: em advances of chars below `AdvanceTableSize` (256), one row
  per `FontStyle` (4 × 256 floats). An unimported char takes space's advance, 0 when the font has no space — the
  same fallback `MeasureAdvance` applies. `TableAdvance(char, face)` reads it.
- `TextMeasurer.MeasureAdvance` reads `TableAdvance` for chars below 256, the `charIndex` dictionary above.
  Bit-identical results: same em value times `fontSize`.
- `BlockLayout` keeps a private `spare` list. `Reset()` moves `lines` into it and zeroes `width`/`height`;
  `NextLine()` pops a spare line (clears `segments` and its six floats) or makes one.
- `MeasureBlock(..., slots = null, reuse = null)` — `reuse ?? new BlockLayout()`, then `Reset()`. `AppendLine` and
  `EmptyLine(layout, …)` take lines from `NextLine()`.
- `TextRunControl.MeasureCore` and `LayoutAround` pass their own `_layout` as `reuse`.
- `TextLine.segments` starts at capacity 1 — one segment is the common line.
- `DocumentControl.RegisterFloats` removes the block's floats with a reverse `RemoveAt` loop instead of
  `floats.RemoveAll(f => f.block == block)`.

## Why these choices

**The lambda in `RegisterFloats` was 88 B per paginated block.** It captured `block`, so every call allocated a
closure and a delegate. `Paginate`'s settled tail calls it for every block below the edit, so one keystroke near the
top of a 1000-block note cost 85.3 KB — floats or not.

**Glyph lookup was two thirds of an optimized measure.** Per char: a `Dictionary<char,int>` probe through the
custom `CharHash` comparer (an interface call), a `Glyph` dereference and a 28-byte `GlyphMetrics` copy through
`Metrics(face)`. Harness, tier-1, 1M chars: 10 ms of a 15 ms measure. Rejected: dropping `CharHash` for the default
comparer was not tried — it would still be a hash probe per char. 256 covers Latin-1; anything above keeps the
dictionary.

**Lines are reused rather than pooled globally.** `_layout` is the only owner of a block's lines. Every other
reader of `Lines` — `DocumentControl` hit-test and `HighlightBlock`, `BlockControl` code ground and list marker,
`WrapsAround`, `TextRunControl.Paginate` — reads inside one method, and lines were already mutated in place
(`Paginate` writes `top`, `Align` writes `left`). `FloatSlots.Place` never reads the block's lines, so reusing during
`MeasureAround` is safe. **Consequence:** `Lines` is the same list object across remeasures — never hold a
`TextLine` across a layout and expect the old geometry.

**Capacity 1 is for the first measure only.** Reuse covers every remeasure; opening a note has nothing to reuse.

## Measurements

The first-measure frame is tier-0 JIT code; see Known gaps. Tables: Release+`PROFILE`.

| `--profile=25 --profile-scenario`, open frame, 3 runs | before | + advance table, `RegisterFloats` | + line reuse, capacity 1 |
|---|---|---|---|
| `Document.MeasureBlocks` | 83.3 / 87.9 / 96.3 ms | 47.7 / 47.6 / 49.7 | 51.2 / 46.3 / 47.5 |
| `Text.MeasureBlock` alloc | 2,808 KB | 2,808 KB | 2,191 KB |

| `--test=Perf`, old/new runs alternated, 3 pairs | old | new |
|---|---|---|
| `RewrapLargeNote` `Text.MeasureBlock` p95 | 38.3 / 17.6 / 30.9 ms | 21.3 / 8.4 / 20.7 |
| `RewrapLargeNote` `Text.MeasureBlock` KB/frame | 3,482 / 3,503 / 3,511 | 37 / 50 / 66 |
| `RewrapLargeNote` `Step.Main.Layout` p95 | 39.0 / 18.3 / 32.0 ms | 22.4 / 9.4 / 22.5 |
| `TypeLargeNote` `Step.Main.Layout` KB/frame | 31.6 / 30.0 / 38.1 | 0.2 / 0.1 / 0.2 |
| `TypeLargeNote` `Document.Paginate` KB/frame | 28.5 / 26.9 / 35.1 | 0.1 / 0.0 / 0.1 |

| scratch harness: `MeasureBlock` over 1000 × 1000-char blocks, no reuse, alternated, 3 runs | old | advance table |
|---|---|---|
| cold pass | 71.3 / 73.2 / 74.3 ms | 48.6 / 47.7 / 48.7 |
| pass 999 | 29.0 / 27.8 / 30.7 ms | 17.0 / 17.0 / 17.0 |
| `MeasureAdvance` × 1M | 18.4 / 17.3 / 18.4 ms | 1.8 / 1.8 / 1.7 |

- This machine (Ryzen 7 7435HS laptop) runs in two speed modes; the same build's rewrap p50 lands near 15 or near
  30 ms. Only alternated old/new pairs are comparable — a baseline from an earlier hour is not.
- The "old" build is a scratch copy of the tree with these edits reverted, dropped into a copy of Thorium's Release
  folder next to a `Shaders` folder (shaders load from `../../../Shaders`).

## Known gaps
- **Tier-0 JIT dominates the first seconds.** The scenario's typing frames: p50 2.6–3.0 ms by default, 0.4–0.7 with
  `DOTNET_TieredCompilation=0`; frames past ~1100 are equal. The open measure: 83–96 ms by default, 38–40 with
  `DOTNET_TC_QuickJitForLoops=0`, 22–24 with tiering off. Tiering off costs ~40% in steady state (no PGO). Not
  acted on — a host `<TieredCompilationQuickJitForLoops>` or `[MethodImpl(AggressiveOptimization)]` is open.
- `RewrapLargeNote` still fails its 8 ms budget; the remaining cost is per-char work across 1M chars a frame.
- Opening a note still allocates 2.2 MB of lines and 0.7 MB in `Text.BuildRuns`.
- `Paginate`'s own `floats.RemoveAll(f => …)` at its top still allocates once per call (~88 B).
- Perf budgets in `Perf.tests.xml` were not re-derived.

Related: [[text-layout-one-measurer]], [[engine-profiling]], [[engine-testing]], [[note-images]]
