# Animation writes its own data — `Main.Apply` and `AnimationValues` deleted

**Status:** deferred rework (user, 2026-09-24). Forks 1, 2, 4 settled; fork 3 open. Split out of [[frame-scheduler-plan]] step 3 because it grew into a UI data migration. Not started.

**Outcome:** `Animation.Step` writes every animated value straight into pool data; no Main step applies values. `Main.Apply`, `Animations.ApplyValues`/`OnValue`, `AnimationValue` and the `AnimationValues` pool are gone.

## What `Main.Apply` does today, and where each goes
| Today | After |
|---|---|
| Setter path for 5 properties: `Control.alpha`, `Control.edgeThickness`, `ButtonControl.state`, `StateBinding.state`, `ContextMenuControl.reveal` | Each becomes a pool field written in place (A1–A4) |
| `changed` (`InvalidateLayout`/`InvalidateArrange`) after an in-place `ArrangeData` write | `LayoutDirty` rows drained by layout (A5) |
| `onDone` + binding release when a track finishes | `AnimationDone` rows drained by `Main.Logic` (A6) |

## Steps
- [ ] **A1. `VulkanControl` becomes a `UIElements` column.** `Pools.pools.xml` adds it; `Control._visual` goes, `visual` becomes `ref Pool.GetRef<VulkanControl>(dataHandle)`. The row exists before `Control()` runs (the `Entity` constructor allocates it). The 8 `.visual` uses outside `Control` keep compiling (still a `ref`). Every `visual` access becomes a pool lookup — measure `Emit`.
- [ ] **A2. `alpha`, `edgeThickness` in place** — `[A_Animatable(typeof(VulkanControl), …)]`. The alpha setter's Clear-role gate (Clear without an authored colour shows 0) moves into `Emit`. `RepaintChildren` only matters when alpha crosses 0 (`GroundBelow` tests `alpha > 0`): Animation turns a crossing into an arrange-dirty row (A5); a pending arrange already repaints children.
- [ ] **A3. Button `state`** — fork 3, below. Either way `ButtonControl.state` becomes in place on `VulkanControl.state`.
- [ ] **A4. `StateBinding` and `reveal`.**
	- `StateBinding` stops being an animated object: one spring per `BindingTrack`, each following the binding's signal, each writing its own property in place.
	- `AnimationTrack` gains `rest`/`hover`/`press` (`Vector4`) + a `mapped` flag: a mapped track springs the scalar `s` and writes `lerp(rest, hover, press, s)`. Same curve as today (one scalar spring, same piecewise map). `StateBinding.state`, `Apply()`, `_spring` go.
	- `ContextMenuControl.reveal` becomes `ArrangeData.reveal` (fraction of desired height slid out), in place with arrange-dirty; `ContextMenuControl`'s arrange reads the column.
- [ ] **A5. Dirty marking via `LayoutDirty` (fork 1 → a).** Animation appends `(handle, measure|arrange)` rows; `UIEngine.ResolveLayout` drains them first through the existing `InvalidateLayout`/`InvalidateArrange`. Layout's algorithm untouched. `A_Animatable.changed` becomes an enum (`Measure`/`Arrange`), the compiled `Action<object>` goes.
- [ ] **A6. `onDone` via `AnimationDone` (fork 2 → `Main.Logic`).** Animation appends `(track, generation)` for finished tracks; the start of `Main.Logic` releases the binding and runs `onDone`. `onDone` fires one frame later than today (`Logic` runs before `Animation.Step`). Only caller today: `FileTreeControl.Collapsed`.
- [ ] **A7. Deletions + graph.** `Main.Apply` action and step; `Animations.ApplyValues`/`OnValue`/`Values`; `AnimationValue`; the `AnimationValues` pool. `AnimatableProperty.set` stays (`Bind` reads `get`; XML authoring sets). `Frame.frame.xml`: `Animation.Step` writes `UIElements.ArrangeData UIElements.VulkanControl LayoutDirty AnimationDone`; `Main.Logic` adds `AnimationDone`.
- [x] **A8. Same-property clash → replace (fork 4). Landed with `Jobs.For`, 2026-09-24 — [[frame-scheduler]] § Step 3.** `Bind` stops an existing live track on the same `(target, property)` through a `(target, property) → id` dictionary. Behaviour change: a second `Tween` replaces the first instead of fighting it. Matters for `Jobs.For` too — two chunks writing one field make the winner nondeterministic.

## Settled (user, 2026-09-24)
- 1 → (a) `LayoutDirty` drained by layout. Rejected (b): a parent-handle column on `UIElements` with flags set up the chain in data — changes the dirty-root mechanism and layout's guarantees.
- 2 → drain at the start of `Main.Logic`, next frame. Rejected: start of `Main.Layout` same frame — layout would run arbitrary callbacks.
- 4 → `Bind` replaces. Rejected: a DEBUG assert only.

## Open
- **Fork 3 — button `state` blend.** `PaintState`'s CPU lerp exists only for hex hover/press colours and the Clear-role `alpha * min(s, 1)`; palette surfaces already blend in the shader by `VulkanControl.state`.
	- (a) Shader: `VulkanControl` gains `hoverPaint`/`pressPaint`, filled by `PaintState` only when colours or palette change; the shader blends rest/hover/press by `state` for every button and applies the Clear-role scale. Zero CPU per frame; +8 B per quad row; `UIEngine.*` shaders rebuilt and mirrored (`shader-pipeline`).
	- (b) CPU in `Emit`: recompute the paint from `state`. `UIEngine.BuildDrawLists` re-emits every control every frame, so this costs per button per frame on Main whether or not anything moves.
	- Leaning (a) on performance: (b) is Main work proportional to button count every frame; (a)'s GPU cost is a few ALU ops per fragment.

## Left out
- `ResolveLayout` re-arranging the whole root stack; `StopAll` being O(bindings) — own WIP entries.

## Verification
1. A1–A2 → build; Thorium palette crossfade and Clear-role controls unchanged; nothing reads `visual` from a stale row.
2. A3 → button hover/press with hex and palette colours; a Clear-role button fading in. If (a): 4 `.spv` copies byte-identical.
3. A4 → `UI.anim.xml`/`Thorium.anim.xml` bindings (edge, padding) hover/press as before; context menu slides open.
4. A5–A7 → file-tree expand/collapse incl. a collapse interrupted by a re-expand; `Collapsed` fires; heights end right. Scenario margin hold vs [[frame-scheduler]] § Step 2 table, minus `Main.Apply`.
5. A8 → a second `Tween` on one property replaces the first.

Related: [[frame-scheduler-plan]], [[frame-scheduler]], [[animation-core]], [[animation-plan]]
