# Decision — effects are GPU tweens from a shared table, evaluated against engine time per row

**Date:** 2026-09-19
**Scope:** `ArctisAurora.Core.UI` — `Effects`, `EffectDefinition`, `EffectLoop`, `GpuEffect`, `VulkanControl` (`effect`, `effectStart`), `Control` (`effect`, `RestartEffect`), `TextRunControl` (`StyleSpan.effect`, `_runEffects`, `WriteGlyph`), `BlockControl` (`Run.effect`, `AppendRun`, `SameStyle`); `ArctisAurora.EngineWork.Rendering.Modules.UIEngineModule`; `Shaders/UIEngine/UIEngine.vert`; data `AuroraEngine/Data/XML/Documents/Pools.pools.xml`, `Bootstrap.bootstrap.xml`, `*/Data/XML/Documents/Effects.effects.xml`

Slice 7 of [../Context/animation-plan.md](../Context/animation-plan.md).

## What changed

- **`Effects.effects.xml`**, optional per host like gradients: `<Effect Name Duration Loop Stagger Ease [X1 Y1 X2 Y2 | Steps] OffsetFrom OffsetTo ScaleFrom ScaleTo RotateFrom RotateTo AlphaFrom AlphaTo/>`. `Loop` = `Once` (holds the end) / `Loop` / `PingPong`; `Ease` = any `EaseKind`; offsets `"x,y"` design px; rotation degrees (baked to radians). Every channel defaults to no change.
- **Table.** Pool `Effects` (`System="Main"`, 32 +32, `GpuEffect`, 76 B), slot 0 reserved. Bootstrap step `Effects.LoadEffects` after `Palettes.LoadPalettes`. `IndexOf(name)` throws on an unknown name; `Stagger(id)`. Mirrored per image by `UIEngineModule.TableMirror<GpuEffect>` at set 1 binding 5, vertex stage.
- **Row.** `VulkanControl` + `uint effect`, `float effectStart`, 88 → 96 B.
- **Shader.** `UIEngine.vert`: progress `(engine.totalTime − effectStart) / Duration` (clamped at 0 before the start) → loop mode → `ease()`, a GLSL port of `Curve.Evaluate` (same families, bezier by bisection, steps). Scale and rotation about the quad centre, then the offset; alpha multiplies the tint. `fragLocal`/`fragHalfExtent` stay in the quad's own frame, scaled — the SDF rotates with it. Clip is applied after, in the moved position.
- **Start time = when the effect is assigned** (F3). `Control.effect` (`Effect=`) sets `visual.effect` and calls `RestartEffect()` (`visual.effectStart = Engine.totalTime`). Engine time is Main's clock, which is also what `Renderer.UpdateGlobalBuffers` publishes.
- **Text.** `StyleSpan.effect` → `_runEffects` (a span with none takes the control's own). `WriteGlyph` writes `effect` and `effectStart = visual.effectStart + (index in run) × Stagger` — the stagger costs no field.
- **Documents** (F2). `Run.effect` (`Effect=`) round-trips through reflection; the three copy/compare sites (`AppendRun`, `SameStyle`, split back to runs) carry it. `AppendRun` — the load path only — restarts the block's effect, so a one-shot plays on load, not on every edit.
- Channels: offset, scale, rotation, alpha (F1).

## Why these choices

**A named table, like gradients, not per-row parameters.** An effect is authored once and shared; per-row channels would put ~60 B on every glyph row. The row carries an index and a start time, 8 B.

**Evaluated on the GPU, not by the animation thread.** Per-letter animation is a standing requirement and rows are rebuilt every frame from `TextRunControl`; a CPU track per glyph would be a track per letter. Cost: fire-and-forget — no interruption, no impulses, no signals (F4, agreed).

**One start time per control, staggered at emit.** Storing a start per glyph would need storage outside the row, which is rebuilt every frame.

**Visual only.** Layout, hit-testing and the caret ignore effect offsets; a moved letter is still selected where it rests.

## Known gaps

- A document run's effect starts when its block loads; editing does not replay it, and a split block's new half inherits the old start. `RestartEffect()` exists; nothing in Thorium calls it.
- Every run in a block shares the block's start time.
- The engine clock is a `float` on the GPU: after ~24 h of uptime its step is ~8 ms.
- No colour channel; no effect on the edge band separately.
- `Back`/`Elastic` `InOut` differ slightly from easings.net, as on the CPU ([[animation-core]]).
- Thorium ships no `Effects.effects.xml`, and no toolbar control picks an effect.

## Verified (2026-09-19)

- Builds clean; one new nullable warning on `Run.effect`, the same one its neighbours `gradient` and `fontName` carry.
- `spirv-dis`: `VulkanControl` stride 96, `effect` 88, `effectStart` 92; `Effect` stride 76, `bezier` at 60 — matches `GpuEffect` (all members 4-byte aligned, no padding). Four shader trees identical.
- Boots; `Effects.LoadEffects` logs no file; at rest pixel-identical to the theme-crossfade capture except the OS corners.
- **GUI-verified, temporary probe (reverted — effects file deleted, note restored):** `wave` (`PingPong`, `SineInOut`, 1 s, stagger 0.08, offset 0 → −6) on the sample note's H1 run: sampled letter tops differ within a frame and move ~6 px between frames (e.g. 95 ↔ 101). `rise` (`Once`, `CubicOut`, 3 s, stagger 0.1, offset (0,20) → 0, alpha 0 → 1) on the bold run: peak brightness 115 → 190 → 222 → 228, top 193 → 186 → 183, then held. **NOT verified:** scale, rotation, `CubicBezier`/`Steps` on the GPU, an effect on a panel control, `Loop` mode.

Related: [[ui-gradients]], [[animation-core]], [[ui-palettes]], [[ui-quads-pool]]
