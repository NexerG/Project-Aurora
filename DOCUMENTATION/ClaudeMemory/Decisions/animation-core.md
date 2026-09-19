# Decision — animation runs on its own thread, steered by posted requests, results posted back

**Date:** 2026-09-19
**Scope:** `ArctisAurora.Core.Animation` — `AnimationSystem`, `Animations`, `Curve`, `EaseKind`, `Spring`, `AnimationTrack`, `AnimationRequest`, `AnimationValue`, `A_Animatable`, `AnimatableProperty`, `AnimationLibrary`, `ClipDefinition`, `BindingDefinition`, `Keyframe`, `ClipLoop`, `StateBinding`; `ArctisAurora.Core.UI` — `Control` (`clip`, `hoverClip`, `pressClip`, `stateBinding`); `ArctisAurora.Core.Threading` — `ThreadedSystem` (`Post`, `OnPost`, `Drain`), `MainSystem.OnPost`; `ArctisAurora.Core.Data` — `DataManager.FrameEdge(owner)`, `Commands.CommandOp.Post`, `SystemCommand.Post`; `Engine`; `UI.Control`; data `AuroraEngine/Data/XML/Documents/Pools.pools.xml`

Slice 3 of [../Context/animation-plan.md](../Context/animation-plan.md).

## What changed

- **Fourth thread.** `AnimationSystem : ThreadedSystem` (`[A_XSDType("Animation","Systems")]`, 120 Hz, own `Stopwatch` dt). Built with the others before `ResolveOwners`/`BuildLanes`, started with physics and render, stopped in `Engine.Stop`.
- **`FrameEdge` is per system.** `DataManager.FrameEdge(ThreadedSystem owner)` runs only that owner's pools. Main calls it with `mainSystem`, Animation at the end of its `Tick`. The first non-Main pool would otherwise have thrown the owner assert from `MainTick`.
- **`CommandOp.Post`** — a message to the owning *system*, not to a pool column. `SystemCommand.Post(kind, offset, size, producer)`: `ColumnId` = kind, `Count` = payload bytes. `ThreadedSystem.Post<T>(target, kind, in T)` enqueues on the producer's existing lane to `target`; `Drain` hands a `Post` to the virtual `OnPost(kind, ReadOnlySpan<byte>)` instead of `CommandApplier`. False on backpressure or off a system thread.
- **Tracks.** Pool `Animations`, `System="Animation"`, handle-less, row index = track id, column `AnimationTrack` (driver, sleeping, generation, curve, from/to/value/velocity, duration/elapsed, frequency/damping). Rows are appended on demand when a request names an id past the count; never freed.
- **Main → Animation:** `AnimationRequest` (`Tween`, `Spring`, `Retarget`, `Stop`), post kind `AnimationSystem.requestKind`.
- **Animation → Main:** `AnimationValue` (track, generation, value, done), post kind `valueKind`, one per active track per tick. `MainSystem.OnPost` → `Animations.OnValue` → the property setter, during Main's `Drain` — before input, `OnTick` and `ResolveLayout`.
- **Ids and bindings live on Main** (`Animations`): a free list of ids, each with target, `AnimatableProperty` and a generation bumped per start. A value whose generation does not match is dropped, so a recycled id never takes the old track's value. A tween's `done` frees its id after the final value applies. `Stop` frees at once.
- **API (Main thread only):** `Tween(target, property, Vector4 to, seconds, Curve, Action? onDone)` (`onDone` runs once after the final value applies; never on `Stop`), `StopAll(target)` (called from `Control.OnDestroy`, so no ad-hoc tween writes into a destroyed control), `Spring(target, property, frequency, damping)` (rests at the current value), `Retarget(handle, Vector4)`, `Stop(handle)`. Start returns an `AnimationHandle(id, generation)`, `AnimationHandle.None` when the post was refused; a handle whose generation no longer matches is ignored (user, 2026-09-19 — rejected: a bare int id, which a finished tween's reuse made unsafe).
- **Drivers.** Tween: `Lerp(from, to, Curve.Evaluate(curve, elapsed / duration))`, done at 1. Spring: `Spring.Step`, exact damped-oscillator solution (under, critical within 1e-4, over), dt-independent; settles under 1e-3 distance and speed, snaps to target and sleeps until retargeted — it does not finish, so its id stays valid.
- **Backpressure.** A value post that fails leaves the track as it was; a finishing tween or settling spring retires only after its final post succeeds, so the last value is re-sent next tick rather than lost.
- **Curves.** `EaseKind`: `Linear`; `Sine Quad Cubic Quart Quint Expo Circ Back Elastic Bounce` × `In Out InOut`; `CubicBezier` (Newton, bisection fallback); `Steps` (jump-end). `Out(t) = 1 − In(1 − t)`, `InOut` mirrors `In` — one formula per family.
- **Signals (2026-09-19, slice 5).** Pool `Signals` (`System="Animation"`, handle-less, row = signal id, `SignalValue { Vector4 value }`). Ids live on Main in `Signals`: `Create()` → `SignalHandle(id, generation)` (writes 0 so a reused row starts clean), `Named(name)` (same handle per name, registry on Main), `Set(handle, Vector4)` → `AnimationOp.SetSignal` post, `Release(handle)`. `Animations.Spring(…, SignalHandle follow)`: `AnimationTrack.follow` (−1 none); each tick a following spring takes the signal's value as its target and wakes when it changed. Several springs may follow one signal. Physics can later write by id on its own lane; name resolution stays on Main. (user — rejected: `Retarget` alone.)
- **Slot fades (slice 6).** `Animations.FadeSlots` → `AnimationOp.FadeSlots` (`track` = first target slot, `source` = first source slot, `count`, `generation` = request id). Animation owns `Paints` and keeps its fades in a private list; `FadeSeeded` (post kind `fadeSeededKind`) acknowledges the seed. Used by the theme crossfade — see [[ui-palettes]] § Theme crossfade.
- **Clips (2026-09-19, slice 8).** `*.anim.xml` under `XML/Documents/Animations`, XSD category `Animation` (`AnimationTypeSchema.xsd`). Root `<Animations>` admits the abstract `AnimationEntry`: `<Clip Name Loop>` → `<Track Property>` → `<Key Time Value Ease X1 Y1 X2 Y2 Steps>`, and `<Binding>`. Bootstrap step `AnimationLibrary.LoadAnimations` (after `Effects.LoadEffects`) appends keys to pool `Keyframes` (`System="Animation"`, handle-less, filled at boot like `Paints`, read-only after). Clip length = latest key; `ClipLoop` `Once Loop PingPong`; a key's curve eases the segment **leaving** it (CSS). `Value` = 1/2/4 floats in `AnimatableProperty`'s Vector4 order.
- **Keyframe driver.** `AnimationDriver.Keyframes`; `AnimationTrack` gains `firstKey keyCount loop direction hold`; `AnimationRequest` carries first key/count in `source`/`count` (as `FadeSlots`), plus `loop direction hold`. `AnimationLibrary.Sample` / `LocalTime` are static and pure. `Animations.Play(target, clip, hold)` → one `AnimationHandle` per clip track (empty array, earlier tracks stopped, when a post is refused); stop a clip by `Stop` on each handle.
- **Reversal.** `AnimationOp.Direction` + `Animations.Direct(handles, forward)`. `hold` tracks sleep at either end instead of finishing, so handles stay valid. Backward = `elapsed -= dt` to 0, never wraps; turning backward first folds `elapsed` into the current cycle (`LocalTime`), so a loop unwinds from its visible phase. Sampling is by time, so backward retraces forward exactly.
- **Control triggers.** `Clip` plays (not held) in `Control.OnStart`, or at once when set on a started control. `HoverClip` / `PressClip` play held on Enter / Press, `Direct` forward on re-entry, `Direct` backward on Exit / Release (user: an unhover reverses, never snaps — 2026-09-19). Hooks sit in the base `OnPointerEnter/Exit/Press/Release`; `OnDestroy` stops runs and detaches the binding.
- **State bindings.** `<Binding Name Frequency Damping>` → `<BindingTrack Property Rest Hover Press>`. `StateBinding` generalises `ButtonControl`: own signal (0/1/2) + spring on its `[A_Animatable] state`; the setter blends rest→hover→press per track and writes via `AnimatableProperty`. Unset `Rest` = value at attach (captured once), `Hover` falls back to rest, `Press` to hover; unset frequency/damping = palette `StateFrequency`/`StateDamping`. Attached lazily on the first pointer event so `Rest` sees authored values.
- **Name fallback (slice 5).** `AnimatableProperty.Of` tries the XML name, then the C# name, so code-only properties (`ButtonControl.state`) animate.
- **`[A_Animatable]`** on `Control`: `Width`, `Height`, `MinWidth`, `MinHeight`, `Margin`, `Padding`, `HorizontalPos`, `VerticalPos`, `Alpha`, `EdgeThickness`. Looked up by the **XML name** (`A_XSDElementProperty`), compiled once per (type, name) into `Vector4` get/set through expression trees; `float`, `Vector2`, `Vector4`, `Thickness` (top, right, bottom, left).

## Why these choices

**Requests travel as `Post` on the existing lanes, not a separate ring.** (user, 2026-09-19) The lanes already exist for every ordered pair of systems, drain at the top of the owner's tick and commit at the end of the producer's, which is exactly the batching a request wants. Physics impulses will use the same path. This is not the bus [[cross-system-change-notification]] rejected: it is point-to-point on per-pair SPSC lanes, no broker, no subscribers.

**Results are pushed, not pulled from an Animation-owned pool.** (user, 2026-09-19) Rejected: an `AnimatedValue` column Main reads by dirty range — lossless, but needed Main to poll a pool owned by another thread. Cost of the choice: a value can meet a full lane; handled by retiring a track only after its last post lands.

**Ids are assigned by the requester.** A lane `Allocate` returns no handle ([[ecs-rework-data-pools]]), so Main owns the id space and Animation grows its handle-less pool to fit — the same shape as the shared-stable-id decision for `Style` rows.

**The resolved look stays in `Control.visual`; there is no `Style` pool.** (user, 2026-09-19) With values pushed, Animation already reaches a control's look through its setters. Moving `visual` to an Animation-owned pool would have made every tree-derived paint write cross-thread. See [../Context/animation-plan.md](../Context/animation-plan.md) § C.

**Values apply at Main's `Drain`, not in `Interpolate`.** Push delivers them there for free, and it is still before `ResolveLayout`, so an animated `Width` arranges the same tick it lands.

**Spring is frequency + damping ratio.** (user, 2026-09-19) Rejected: stiffness + damping. Ratio 1 is critical regardless of frequency, so tuning feel does not retune overshoot.

**One `In` per family, mirrored.** `Back`/`Elastic` `InOut` therefore differ slightly from easings.net's separately tuned constants; the endpoints and 0.5 midpoint are exact. Chosen for one formula per family over ~30 hand-written cases.

**Lookup by XML name.** It is what an author sees, and what `*.anim.xml` tracks reference.

**Keys live in an Animation-owned pool.** (user, 2026-09-19) Rejected: a plain immutable array published before the threads start — simpler, but the only cross-thread table outside `DataManager`. Rejected: Main sequencing one `Tween` per segment — a gap and a tick of latency at every key.

**A clip is triggered; a binding is stateful.** `HoverClip` reverses rather than snapping back (user), which needs held tracks and a direction op; a binding needs neither, because a spring on a 0/1/2 state is already reversible. Bindings reuse the spring + signal path unchanged — no new driver, no new request.

**`Clip` plays in `OnStart`, not in its setter.** (user) `*.ui.xml` is parsed during bootstrap, before `AnimationSystem` exists and off any system thread, where `Post` refuses. Rejected: remembering the clip and playing on the control's first Main tick.

**`BindingTrack`, not `Track`.** (user) One XSD type name is one global element per category schema, so the binding's track could not also be `Track`.

**Clips live in their own XSD category, `Animation`.** They serve game entities too, not only UI; `ClipLoop` is new rather than reusing `UI.EffectLoop` (user).

## Known gaps

- **`Alpha` on a container does not fade its content.** `alpha` is per quad, not inherited, and a `Clear` container paints nothing — so animating a panel's alpha changes only its own fill. Fading a subtree needs an inherited opacity; not in this slice.
- **No colour tracks.** Nothing colour-valued is `[A_Animatable]` — a control's colours are paint words (`uint`), not Vector4s. Slot fades (slice 6) cover themes; a per-control colour clip needs its own design.
- **Clip property names are checked at `Play`**, not at load — a clip names no target type. Unknown clip/binding names throw at first use.
- **Held press dragged off:** Release reached the control before Exit, so a `StateBinding` headed toward hover for ~200 ms before resting. Seen in the GUI probe, cause **not investigated** (pointer event order in `UIEngine`, not the binding). `PressClip` reverses only on the Release the control receives; Exit does not reverse it.
- **Hover bubbling:** a `HoverClip`/`StateBinding` on a panel rests while the pointer is over a child that consumes Enter (a button) — single-target hover, unchanged.
- **Not in slice 8:** timeline events/markers, clip blending, hot reload, "from current value" keys.
- **Signals, GUI-verified (2026-09-19, temporary probe reverted):** two springs (browser and workspace `Margin`) following `Signals.Named("probe")` moved together, text edges 19 → 26 → 58 → 59 and 226 → 236 → 284 → 286 (+40 each). **NOT verified:** `Release` while followed, a physics writer.
- **Latency.** A value lands up to one Animation tick plus one Main tick after it is computed.
- **Seen during verification, not investigated:** with a 60 px left `Margin` on the browser inside the sidebar stack authored `Width` 400, the sidebar arranged 460 wide — a child margin widened a fixed-width stack. Layout, not animation.

## Verified (2026-09-19)

- Builds clean; no warning in new or touched files.
- Scratch harness outside the repo (not committed): every `EaseKind` 0 → 0, 1 → 1, `InOut` 0.5 → 0.5; `CubicOut(0.5)` 0.875; CSS `ease` (0.25, 0.1, 0.25, 1) at 0.5 → 0.8024 and monotonic; linear bezier = t; `Steps(4)`; springs: critical no overshoot and converges, ζ 0.3 overshoots and converges, ζ 2 creeps; one 0.5 s step equals 60 small ones for ζ 0.3/1/2.
- **Boot-verified (Debug):** log shows Main, Physics, Render and Animation starting; no owner assert from per-system `FrameEdge` or the `Animations` pool.
- **GUI-verified, temporary probe (reverted):** tween of the sidebar `Width` 220 → 400, 4 s `CubicOut` — splitter measured at 220, 287, 346, 379, 395, 400 in ~0.75 s captures, then exactly 400. Spring on the browser `Margin` left 0 → 60, 1 Hz, ζ 0.3 — row text 19 → 97 (overshoot) → 79 settled. **NOT verified:** `Stop`, retargeting a running tween, backpressure, the `Vector2` conversion.

## Verified — slice 8 (2026-09-19)

- Builds clean; no warning in new or touched files. `AnimationTypeSchema.xsd` generated with `Animations`, `Clip`, `Track`, `Key`, `Binding`, `BindingTrack`, `ClipLoop`; `actionSchema.xsd` lists `AnimationLibrary.LoadAnimations`; `Keyframe` in DataPools; the four `Control` attrs beside `Effect`.
- Scratch harness (not committed): `Sample` before/on/between/after keys, `CubicOut` mid-segment, single-key track; `LocalTime` Once clamp, Loop wrap, PingPong mirror and second cycle; stepping forward then backward gives identical values at identical times.
- **Boot-verified:** log `loaded N clip(s) with M key(s), 1 binding(s)`; no owner assert from `Keyframes`.
- **GUI-verified, temporary Thorium probe (reverted):** `Clip` on the sidebar (220 hold 0.5 s → 400 CubicOut → 300) — splitter 220, 240…400 by 1.9 s, then eased to exactly 300 at 3 s. `HoverClip` on maximize (46 → 246, 2 s linear) — ~100 px/s, exit at 1 s reversed with no jump, re-entry mid-reverse turned forward from where it was, held at 246, exit after finishing ran fully back. `Loop` hover clip wrapped at 2 s and, exited at 3 s, unwound from its phase (~146) to 46 in ~1 s. `StateBinding` on minimize (Hover 146, Press 96, 1 Hz, ζ 1) — icon moved exactly −50 on hover, +25 on press, back to rest on exit, monotonic without a press. `PingPong` `Clip` on the Settings window, closed by Alt+F4 mid-swing — no exception, no warning, app kept running.
- **NOT verified:** `PressClip` (only through the shared `RunClip`/`Direct` path), `CubicBezier`/`Steps` keys from XML, backpressure on `Play`.

## Used by the UI (2026-09-19)

- Engine `Animations/UI.anim.xml`: clip `underline` (bottom edge 0 → 2, 0.15 s `CubicOut`); binding `menu-row` (left edge 0/2/3, left padding 10 → 14 on hover). `ContextMenuControl.Row` sets `stateBinding = "menu-row"`, `edgeRole = Accent`.
- Thorium `Thorium.anim.xml`: clip `accent-grow` (`Height` 0 → 14, 0.4 s `BackOut`) on the title accent bar in `UI.ui.xml`/`TabWindow.ui.xml`; binding `chrome-press` (bottom edge 0/1/2) on Settings Save and Vaults Add. Title-bar `MenuButton`s carry `HoverClip="underline" EdgeRole="Accent"`.
- Thorium `Effects/Thorium.effects.xml`: `title-in` (offset 0,6 → 0, alpha 0 → 1, 0.35 s `CubicOut`, stagger 0.03) on the window-title labels.
- **Menu slide (drawer, user-picked over an unroll):** `ContextMenuControl.reveal` `[A_Animatable]`, clip `menu-open` (0 → 1, 0.16 s `CubicOut`). `Arrange` lays the panel out full-size shifted up by `(1 − reveal) × height`, then `Control.ClipSubtree` cuts it at the anchor — so the border and rows slide out of the line the menu opens from. `ClipSubtree` runs after `base.Arrange` because children copy the parent clip inside it. Close stays instant (user) — a sliding close would need the panel to outlive `Close()`. Starts at 0: a refused `Play` leaves the menu invisible (lane backpressure only).
- **Folder expand/collapse:** `FileTreeControl` — opening rebuilds, then the folder's new rows tween `Height` 0 → `rowHeight` (0.18 s); closing tweens the descendant rows (contiguous deeper rows in `listed`) to 0 (0.14 s) and rebuilds from the last one's `onDone`. A toggle during a collapse finishes it at once; a collapse `StopAll`s each row first so it does not fight an unfinished expand. The 2 px `RowSpacing` per row still jumps. **Expander turn:** the new caption plays a GPU effect from where the old one pointed — `expander-open` (`v`, −90° → 0, 0.18 s) on the rebuilt folder row via `turning`, `expander-close` (`>`, 90° → 0, 0.14 s) set on the live row's `FileRowControl.gutter` when a collapse starts (on the rebuild for an empty folder). Two glyphs, not one `>` held at 90° (user) — a small jump at each end because `>` and `v` sit differently. Positive rotation = clockwise (GUI-checked).
- **Names used from engine code live in the engine's file** — a name only a host defines throws at first use in the other hosts.
- **Why rows got a binding:** on `thorium-void` (`Ground #000`, `Step 0.04`) the row's state spring moves ground one step toward ink ≈ `#0A0A0A` — the colour hover is invisible. Palette `Step` left alone (user's call; it moves every hover).
- **GUI-verified:** accent grows and title letters stagger in at launch (burst capture); menu `underline` grows and reverses; context-menu row bar + nudge on hover, reverses when moving to the next row; Save press edge; menu drawer slide (burst frames: bottom row first, then all); folder expand and collapse (intermediate frames), two toggles 70 ms apart end consistent, no log warnings. **NOT verified:** submenu slide; the own-window menu host; Save's 1 px hover edge not distinguishable in capture; `title-in` on Settings/Vaults open; TabWindow; Carbon/Editor menus (same engine row path).

## Measured at scale (2026-09-19)

`--profile-scenario=animation` ([[engine-profiling]] §17), Debug with zones on, a grid of N `ButtonControl`s. Figures are per tick.

| | 1k | 5k | 20k |
|---|---|---|---|
| `Anim.Step` (Animation, budget 8.3 ms) | 0.47 ms | 1.9 ms | 6.8 ms |
| `Anim.OnValue` total (Main) | 0.5 ms | 0.56 ms | 0.55 ms |
| Main, `state` clip | 1.1 ms | 2.9 ms | 4.2 ms |
| Main, `margin` clip | 3.4 ms | 14 ms (71 Hz) | 53 ms (19 Hz) |
| `ResolveLayout`, `margin` clip | 2.3 ms | 11.6 ms | 49 ms |
| values posted / tracks stepped | ~900 / 1,000 | ~970 / 5,000 | ~920 / 20,000 |
| `StopAll`, per call | — | ~32 µs | ~130 µs |
| teardown frame (`Destroy` the grid) | 6 ms | 138 ms | 2,266 ms |

- **Start ceiling 682 a tick.** `AnimationRequest` is ~96 B, so the 64 KB arena fills before the 1,024-slot ring. Bursts of 1k/5k/20k dropped 318/4,318/19,318, one `Warn` each. Each drop is ~1.3 µs on Main.
- **Value ceiling ~1,024 a Main tick, and it is silent.** Past it, `Post` refuses and the track re-posts next tick with no log. At 5k each control updates about every 5th tick, and at 20k about every 20th (~6 Hz). A slow Main lowers it further: 158 values a tick in the 20k margin stage.
- **~0.34 µs per stepped track**, so the thread holds 120 Hz to ~24k active tracks. At 200k (partial run) it was 66 ms (15 Hz).
- **Applying a value costs ~0.55 µs.** It is never the bottleneck.
- **Layout cost follows the tree, not the changes.** ~1,000 margins change a tick at every size, and `ResolveLayout` still grows 2.3 → 11.6 → 49 ms. Each invalidation climbs to the root stack, and that re-arranges every row. This is a layout issue that animation exposes.
- **`StopAll` is O(bindings) per call.** `Control.OnDestroy` calls it, so destroying a subtree is O(subtree × bindings). The teardown hitch fits that: ~5.5 ns per comparison at both 5k × ~5k and 20k × ~21k. This is the likely cause, **not isolated** (no zone inside `OnDestroy`). `bindings` never shrinks, so after one large burst every later destroy pays its high-water mark.
- **Slot fade over every paint slot is negligible.** The Animation frame mean was 0.04 ms.
- `Interpolate` with no layout work: 1.65 ms at 20k controls, 12.5 ms at 200k. This is the entity tick loop, i.e. [[entity-tick-group]] measured.
- Today's UI runs tens of tracks and everything stays well under a millisecond. The first real-use limit is the 682 start ceiling, e.g. a folder of more than 682 rows expanding in one tick.

Related: [[ui-palettes]], [[ui-gradients]], [[ecs-rework-data-pools]], [[cross-system-change-notification]], [[entity-tick-group]], [[engine-profiling]]
