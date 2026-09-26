# Animation writes its own data — `Main.Apply` and `AnimationValues` deleted

**Status:** deferred rework (user, 2026-09-24). All four forks settled. Split out of [[frame-scheduler-plan]] step 3 because it grew into a UI data migration. A8 landed 2026-09-24, A1–A4 2026-09-25, A5–A7 2026-09-26 — **complete**; see [[animation-core]] § Drained pools.

**Outcome:** `Animation.Step` writes every animated value straight into pool data; no Main step applies values. `Main.Apply`, `Animations.ApplyValues`/`OnValue`, `AnimationValue` and the `AnimationValues` pool are gone.

## What `Main.Apply` does today, and where each goes
| Today | After |
|---|---|
| Setter path for 5 properties: `Control.alpha`, `Control.edgeThickness`, `ButtonControl.state`, `StateBinding.state`, `ContextMenuControl.reveal` | Each becomes a pool field written in place (A1–A4) |
| `changed` (`InvalidateLayout`/`InvalidateArrange`) after an in-place `ArrangeData` write | `LayoutDirty` rows drained by layout (A5) |
| `onDone` + binding release when a track finishes | `AnimationDone` rows drained by `Main.Logic` (A6) |

## Steps
- [x] **A1. `VulkanControl` becomes a `UIElements` column. Landed 2026-09-25 — [[animation-core]] § In place.** `Pools.pools.xml` adds it; `Control._visual` goes, `visual` becomes `ref Pool.GetRef<VulkanControl>(dataHandle)`. The row exists before `Control()` runs (the `Entity` constructor allocates it). The `.visual` uses outside `Control` keep compiling (still a `ref`); `TextRunControl.Emit` reads `effectStart` once, not per glyph. Every `visual` access is a pool lookup — **not measured** (user: no profiling).
- [x] **A2. `alpha`, `edgeThickness` in place. Landed 2026-09-25.** `[A_Animatable(typeof(VulkanControl), …)]` with no `changed` — it became optional, and `OnValue` calls the setter only for `width == 0`. `VulkanControl.edgeThickness` is a `Thickness` (was `Vector4`, same bytes) so `InPlace`'s type check passes. The Clear-role gate went to `Control.PaintRow`, run by `Emit` on the copied row. `TextRunControl`'s `alpha` override went — the inherited in-place attribute would have bypassed its `_alpha`.
- [x] **A3. Button `state` in place. Landed 2026-09-25, fork 3 → (b).** `ButtonControl.state` is `VulkanControl.state`; `PaintState` became the `PaintRow` override — same branches, on the drawn row. The column keeps the rest paint, raw `s` and the unscaled alpha.
- [x] **A4. `StateBinding` and `reveal`. Landed 2026-09-25 — [[animation-core]] § Awake list.** Grew two forks at planning (user): `AnimationRequest` replaced by physics-style waking, and the step walking only awake tracks (B, over (A) "writes wake, step still scans rows"); the orphaned setter path deleted now (b, over leaving it to A7).
	- `StateBinding` stops being an animated object: one mapped spring per `BindingTrack`, all following the binding's signal, each writing its own property in place. `state`, `Apply()`, `_spring`, `Track` gone.
	- `AnimationTrack` gains `mapped` + `rest`/`hover`/`press`: a mapped spring springs `s = value.X` and `WriteTarget` writes `s ≤ 1 ? lerp(rest, hover, s) : lerp(hover, press, s − 1)` — `Apply()`'s map. New overload `Animations.Spring(…, follow, rest, hover, press)`.
	- `ContextMenuControl.reveal` is `ArrangeData.reveal`, in place with `InvalidateArrange`; `ArrangeCore` reads it through the getter.
	- `AnimationRequest`, `AnimationOp`, `Animations.Write` gone: calls write the `AnimationTrack` row and wake it onto `Animations.Awake`; `Signals.Set` wakes its followers when the value changes; `SeedFade` takes plain arguments.
	- (b) `A_Animatable()`, the `column == null` branches, `OnValue`'s setter call, the `width > 0` guard, `AnimatableProperty.set`/`FromVector` gone — every animatable property is pool-stored.
- [x] **A5. Dirty marking via `LayoutDirty` (fork 1 → a). Landed 2026-09-26.** Row type `DirtyLayout`, enum `LayoutChange { None, Measure, Arrange }`. Animation appends `(handle, measure|arrange)` rows; `UIEngine.ResolveLayout` drains them first through the existing `InvalidateLayout`/`InvalidateArrange`. Layout's algorithm untouched. `A_Animatable.changed` becomes an enum (`Measure`/`Arrange`), the compiled `Action<object>` goes.
- [x] **A6. `onDone` via `AnimationDone` (fork 2 → `Main.Logic`). Landed 2026-09-26.** Row type `FinishedTrack`, drained by `Animations.DrainDone`. Animation appends `(track, generation)` for finished tracks; the start of `Main.Logic` releases the binding and runs `onDone`. `onDone` fires one frame later than today (`Logic` runs before `Animation.Step`). Only caller today: `FileTreeControl.Collapsed`.
- [x] **A7. Deletions + graph. Landed 2026-09-26, with A5–A6 (user).** `Animation.Step` keeps `Animations Paints` in its writes; `Main.Layout` also writes `LayoutDirty`. `Main.Apply` action and step; `Animations.ApplyValues`/`OnValue`/`Values`; `AnimationValue`; the `AnimationValues` pool. (The setter path and `AnimatableProperty.set` already went in A4.) `Frame.frame.xml`: `Animation.Step` writes `UIElements.ArrangeData UIElements.VulkanControl LayoutDirty AnimationDone`; `Main.Logic` adds `AnimationDone`.
- [x] **A8. Same-property clash → replace (fork 4). Landed with `Jobs.For`, 2026-09-24 — [[frame-scheduler]] § Step 3.** `Bind` stops an existing live track on the same `(target, property)` through a `(target, property) → id` dictionary. Behaviour change: a second `Tween` replaces the first instead of fighting it. Matters for `Jobs.For` too — two chunks writing one field make the winner nondeterministic.

## Settled (user, 2026-09-24)
- 1 → (a) `LayoutDirty` drained by layout. Rejected (b): a parent-handle column on `UIElements` with flags set up the chain in data — changes the dirty-root mechanism and layout's guarantees.
- 2 → drain at the start of `Main.Logic`, next frame. Rejected: start of `Main.Layout` same frame — layout would run arbitrary callbacks.
- 4 → `Bind` replaces. Rejected: a DEBUG assert only.
- 3 → (b) CPU on the drawn row (user, 2026-09-25). Rejected (a) shader: `hoverPaint`/`pressPaint` on every quad row, glyphs included (96 → 104 B), a Clear-fade flag, `UIEngine.*` rebuilt ×4. Since paint-at-draw, `InheritPaint → ApplyRole → PaintState` already ran per drawn palette button every frame, so (b) moved that work into `Emit` rather than adding it; the earlier lean to (a) predated it.

## Left out
- `ResolveLayout` re-arranging the whole root stack; `StopAll` being O(bindings) — own WIP entries.

## Verification
1. A1–A2 → build; Thorium palette crossfade and Clear-role controls unchanged; nothing reads `visual` from a stale row.
2. A3 → button hover/press with hex and palette colours; a Clear-role button fading in. If (a): 4 `.spv` copies byte-identical.
3. A4 → `UI.anim.xml`/`Thorium.anim.xml` bindings (edge, padding) hover/press as before; context menu slides open. **Skipped (user, 2026-09-25)** — A4 got build, `--profile-scenario=animation` to exit 0 and a Thorium boot only.
4. A5–A7 → file-tree expand/collapse incl. a collapse interrupted by a re-expand; `Collapsed` fires; heights end right. Scenario margin hold vs [[frame-scheduler]] § Step 2 table, minus `Main.Apply`.
5. A8 → a second `Tween` on one property replaces the first.

Related: [[frame-scheduler-plan]], [[frame-scheduler]], [[animation-core]], [[animation-plan]]
