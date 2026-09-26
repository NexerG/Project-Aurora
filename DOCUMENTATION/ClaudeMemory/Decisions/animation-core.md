# Decision — animation is its own system, steered by track rows Main writes and wakes, values written in place (applied by Main until 2026-09-26, posted until 2026-09-23)

**Date:** 2026-09-19
**Scope:** `ArctisAurora.Core.Animation` — `AnimationSystem`, `Animations`, `Curve`, `EaseKind`, `Spring`, `AnimationTrack`, `DirtyLayout`, `FinishedTrack`, `LayoutChange`, `A_Animatable`, `AnimatableProperty`, `AnimationLibrary`, `ClipDefinition`, `BindingDefinition`, `Keyframe`, `ClipLoop`, `StateBinding`, `Signals`; `ArctisAurora.Core.UI` — `Control` (`clip`, `hoverClip`, `pressClip`, `stateBinding`, `visual`, `alpha`, `edgeThickness`, `PaintRow`), `ButtonControl` (`state`, `PaintRow`), `ContextMenuControl` (`reveal`), `ArrangeData`, `TextRunControl`, `VulkanControl`, `UITreeDump`, `UIEngine.ResolveLayout`; `ArctisAurora.Core.Threading` — `ThreadedSystem` (`Post`, `OnPost`, `Drain`), `MainSystem.Logic`; `ArctisAurora.Core.Data` — `DataManager.FrameEdge(owner)`, `Commands.CommandOp.Post`, `SystemCommand.Post`; `Engine`; `UI.Control`; data `AuroraEngine/Data/XML/Documents/Pools.pools.xml`

Slice 3 of [../Context/animation-plan.md](../Context/animation-plan.md).

## Track split into a hot row and per-driver columns (2026-09-26)
- **`AnimationTrack` is the 64-B row every step reads:** `driver sleeping mapped changed width loop direction hold`, `column offset`, `follow`, `target`, `elapsed`, `value`, `to`. The rest are columns of the `Animations` pool at the same row: `TweenParams` (`duration from curve`, 44 B), `KeyParams` (`duration firstKey keyCount`, 12 B), `SpringParams` (`velocity frequency damping`, 24 B), `TrackCold` (`generation`, `rest hover press`, 52 B).
- `Start` writes the hot row and `TrackCold.generation`; `Tween` / `Play` / `Spring` write their driver's params row after it, the mapped `Spring` also `TrackCold.rest/hover/press`. `Direct` reads `KeyParams.duration`, `Retarget` writes `TweenParams.from`. A row's other drivers' params stay unwritten and unread.
- `Execute` takes one span per column and reads its driver's row by ref; `WriteTarget(track, cold, i)` reads `TrackCold` only when `mapped`; a finishing track reads `TrackCold.generation`.
- **Why: on 15 threads `Execute` is bandwidth-bound.** The prefetcher streams the whole 192–196-B row whatever the code reads. A scratch loop over 200k tracks with the real types: full row 12.8 ns/track; hot + params 6.9 (clips) / 8.0 (springs); one thread ~25 ns either way.
- **Rejected: reordering fields inside one struct** (tried, measured, replaced). 196 → 192 B, hot fields first: no better for clips, springs 22% worse over 3 runs, reproduced single-threaded in the scratch loop (23.0 → 25.5 ns) — it split the spring's contiguous `to value velocity frequency damping` across lines.
- **Rejected: one union params column** (user, fork F1 → per-driver columns): overlaid types, and clips would stream 44 B of params instead of 12.

| 200k `Anim.Step`, ms/frame, Release+PROFILE | 196-B row (§ `Emit` folded) | reordered 192 B, 3 runs | split, 2 runs |
|---|---|---|---|
| state hold | 2.53 | 2.62–2.95 | 1.55–1.59 |
| margin hold | 3.61 | 3.68–3.95 | 2.82–2.87 |
| spring hold | 2.56 | 3.13–3.15 | 1.84–1.89 |
| `StopAll` | 2.48 | 2.86–3.01 | 1.70–1.85 |

- State-hold frame 3.45 → 2.27–2.34 ms.
- ETW CPU sampling (Visual Studio collector, 4 kHz, whole run): `Execute` 132,720 → 80,955 samples; its own code 101,697 → 54,886; `Spring.Step` + `ucrtbase` math ~16k → ~19k, now ~24% of `Execute`.
- **Verified:** Debug and Release+PROFILE builds; sizes 64 / 44 / 12 / 24 / 52 B; Debug animation (200k) and document scenarios exit 0, no asserts; `--profile-pools` counts as in § `Emit` folded; grid dump identical but for the button under the real cursor. **NOT GUI-verified** (user, fork F2): hover / press colours (mapped springs → `TrackCold`), held clips reversing (`Direct` → `KeyParams.duration`), `Retarget`. Incidental: the button under the cursor dumped `Alpha="1"` in two runs, as before the split.
- Generated `DataPoolsTypeSchema.xsd` files gain the four types on the next boot.

## `Emit` folded into the chunks (2026-09-26)
- **`Execute` decides each track's outcome while its row is in cache.** A layout change goes to `_dirtyRows[start + n]`, a finishing tween / non-held clip gets `driver = None` and a `FinishedTrack` in `_doneRows[start + n]`, a done or stopped track gets `sleeping = true`, and a running track's id is written back into `awake` at the front of the chunk's own range (writes never pass reads; a chunk touches only `[start, end)`). Per-chunk kept / dirty / done counts join min / max / stepped in `_stats`. `_outcome` is gone.
- **`Emit` is a stitch.** One slice copy per chunk of kept ids, dirty rows and done rows, in chunk order — the awake, `LayoutDirty` and `AnimationDone` orders are what the serial pass produced. Pool appends stay out of the chunks (`AssertStructural` refuses them there): `DataPool.Append(int count)` reserves all of a frame's rows at once.
- **Why:** `Emit` walked all 200k rows again on one thread for `driver`, `hold` and `changed` — two more cache lines per track, just written by other cores; 70% of its samples were those reads.

| 200k, ms/frame, Release+PROFILE | `Anim.Step` before → after | `Anim.Emit` before → after | frame before → after |
|---|---|---|---|
| state hold | 5.13 → 2.53 | 2.72 → 0.07 | 5.66 → 3.45 |
| margin hold | 6.66 → 3.61 | 3.91 → 0.69 | 53.5 → 50.3 |
| spring hold | 4.66 → 2.56 | 2.50 → 0.06 | 5.32 → 3.38 |
| `StopAll` | 4.82 → 2.48 | 2.41 → 0.06 | 5.59 → 3.45 |

- `Execute`'s own share barely moved (state 2.41 → 2.46 ms): the outcome work is nearly free once the row is loaded. Margin `Emit` is the 200k `DirtyLayout` copy.
- **Verified:** Debug and Release+PROFILE builds; Debug animation (200k) and document scenarios exit 0, no access or structural asserts; `--profile-pools`: 200,000 `AnimationDone` rows in the burst frame and 0 stepped the frame after (compaction), 200,000 stepped every state / margin hold frame, 1,000 in the sparse stage, margin `Main.Layout` unchanged (the dirty rows arrive). **NOT GUI-verified:** hover / press, menu slide, file-tree collapse via `onDone`.
- `DrawLists` went 0.47 → 0.85 ms in the state hold (0.5 → 0.7 in spring / `StopAll`) — one run each, not explained.

## Drained pools — layout dirtying and `onDone`; `Main.Apply` gone (2026-09-26)
A5–A7 of [[animation-in-place-plan]]. Supersedes § Step 2's `AnimationValues` pool, `OnValue` and "invalidate at the end" in `Main.Apply`; § Awake list's "appends values" and the `AnimationValue.value` line.
- **`A_Animatable(column, field, LayoutChange changed = None)`.** `LayoutChange { None, Measure, Arrange }` replaces the method name; `InPlace` compiles nothing. `Start` copies it into `AnimationTrack.changed`. `Control` `Width Height MinWidth MinHeight Margin Padding` → `Measure`; `HorizontalPos VerticalPos`, `ContextMenuControl.reveal` → `Arrange`.
- **`LayoutDirty` pool** (`DirtyLayout { DataHandle target; LayoutChange change }`, handle-less). `Emit` appends one row per stepped track with `changed != None`, finishing steps included. `UIEngine.ResolveLayout` drains it first (`DrainLayoutDirty`: `UIElements.DenseOf` → `OwnerAt`, skips dead handles and destroyed controls, then `InvalidateLayout`/`InvalidateArrange`) and rewinds it. The layout algorithm is unchanged; duplicate rows cost the invalidation's early return.
- **`AnimationDone` pool** (`FinishedTrack { int track; uint generation }`, handle-less). `Emit` appends where a tween or non-held clip finishes. `Animations.DrainDone` runs first in `Main.Logic`: `IsLive` → `Release` → `onDone`, then rewinds. **`onDone` fires the frame after the final value**, since `Main.Logic` precedes `Animation.Step`. A replaced or stopped track fails `IsLive` and its `onDone` never runs, as before.
- **Deleted:** `Main.Apply` (action and step), `Animations.ApplyValues`/`OnValue`/`Values`, `AnimationValue`, pool `AnimationValues`, the `Anim.ValueApplied` counter.
- **Graph:** `Main.Logic` += `AnimationDone`; `Animation.Step` writes `Animations Paints UIElements.ArrangeData UIElements.VulkanControl LayoutDirty AnimationDone`; `Main.Layout` += `LayoutDirty`. Still 7 stages — `Edge.UIElements` takes `Main.Apply`'s place.
- **Each consumer rewinds its own pool**, so a row lives from the append to the next drain: `LayoutDirty` within the frame, `AnimationDone` across one frame boundary.

**Known gaps**
- **Measured** → § Measured at 200k (2026-09-26) — `Emit` now appends only dirty and done rows instead of a value per stepped track.
- `LayoutDirty` and `AnimationDone` have no edge step either ([[pool-shrink]] § Known gaps).
- **Verified**: Debug build clean; `--profile-scenario=animation` (ladder to 20k, margin and state tweens, clips, springs) ran to exit 0 with no `AssertAccess` hit; boot errors only the known sampler + barrier set. **NOT GUI-verified**: file-tree collapse and `Collapsed` via `onDone`, menu slide, theme crossfade, press colours, state bindings.

## Awake list — calls write and wake track rows; state bindings and `reveal` in place (2026-09-25)
A4 of [[animation-in-place-plan]], grown by two forks (user). Supersedes § Step 2's `Animations.Write(in AnimationRequest)` and "setter path stays", and § In place's "still on the setter path".
- **No request struct.** `AnimationRequest`, `AnimationOp` and `Write`'s switch are gone. `Tween`/`Spring`/`Play` build an `AnimationTrack` and `Start` it (`Row(id)` appends up to the id, new rows asleep; `Start` stamps generation and target). `Retarget`/`Direct` edit the row through `Row` and wake it; `Stop` sets `driver = None`. The row-side generation checks went — `IsLive` on Main guarantees them now that writes are immediate. `Signals.Set` writes its own row; `FadeSlots` calls `AnimationSystem.SeedFade(source, first, count, duration, curve)`. `Anim.Request` still counts each track write, signal set and fade.
- **Awake list (physics-style sleep).** `Animations.Awake` holds the ids the step visits; a row's `sleeping` is false exactly while its id is on it. `Wake(id)` pushes a sleeping track; `Start` keeps the row's list membership across the overwrite, so a stopped id reused the same frame is not pushed twice. `Animation.Step` runs `Jobs.For` over the list, not the rows; `Emit` walks it serially, appends values, retires finished tracks and compacts it (`KeepAwake`). A settled spring or a held clip at an end drops off and costs nothing until woken.
- **Waking.** Tweens and clips wake at start; a spring starts asleep unless its signal already differs from its value (`Signals.Differs`), the old scan's rule. `Retarget`, `Direct` and a changed `Signals.Set` wake; a `Set` to the same value wakes nothing. Followers: `Animations.followers` (signal id → `HashSet` of track ids, so `Release` is O(1)), kept by `Start`/`Release` through `Binding.follow`. An awake follower still reads its signal every step.
- **State bindings are mapped springs.** `StateBinding` holds a signal and one `Animations.Spring(target, property, …, follow, rest, hover, press)` per `BindingTrack`. A mapped track springs `s = value.X` over 0/1/2 and `WriteTarget` writes `s ≤ 1 ? lerp(rest, hover, s) : lerp(hover, press, s − 1)` — the old `Apply()`. Its `AnimationValue.value` carries `s`; only `changed` reads the row.
- **`reveal` is `ArrangeData.reveal`**, tagged `[A_Animatable(typeof(ArrangeData), nameof(ArrangeData.reveal), nameof(InvalidateArrange))]`; `ContextMenuControl.ArrangeCore` reads it through the getter. Rows are zeroed on `Allocate`, so it starts at 0.
- **Every animatable property is pool-stored (fork b).** `A_Animatable()` is gone, `column`/`field` non-nullable; `AnimatableProperty` is `get` plus the field (`set`, `FromVector` gone); `OnValue` only calls `changed`; `WriteTarget` has no `width > 0` guard.
- **Rows:** `AnimationTrack` +48 B (`mapped` sits in existing padding); `ArrangeData` +4 B.

**Why the awake list over keeping the scan (user chose B over A).** The scan visited every row up to the high-water mark of track ids ([[pool-shrink]] § Known gaps), sleeping or stopped; the list costs what moves. Every hovered button and bound control holds a spring that sleeps most of its life. Not renamed to `awake`: `sleeping` already meant "not stepped" and only needed new rows appended asleep.

**Known gaps**
- **Binding springs are bound to `(control, property)`**, so `StopAll(control)` or a later tween/clip on a bound property stops them for good; `StateBinding` does not re-spring (`ButtonControl.EnsureSpring` does). No current control hits it.
- **Value order is wake order, not track order**, so `OnValue`'s `changed`/`onDone` calls follow it.
- **The awake list is plain C# state**, ordered only because every caller and `Animation.Step` declare `Animations` (as `_fades` with `Paints`); DEBUG `AssertAccess` does not see it.
- A signal set and set back within one frame wakes its followers for one step; the scan would not have.
- `Animations.Retarget` has no caller.
- **Not measured** (no profiling asked): list vs scan; the 200k tables below predate it.
- **Verified**: build clean; `--profile-scenario=animation` (ladder to 20k) ran to exit 0; Thorium boot-verified with a clean shutdown. **NOT GUI-verified** (user skipped): binding hover/press, menu slide-open, hover clips, file-tree tweens, theme fade.

## In place — `visual` a `UIElements` column; alpha, edge, button state (2026-09-25)
A1–A3 of [[animation-in-place-plan]]. Supersedes § Step 2's "setter path stays" for `Control.alpha`, `Control.edgeThickness`, `ButtonControl.state`; still on the setter path: `ContextMenuControl.reveal`, `StateBinding.state` (both moved in A4, § Awake list).
- **`VulkanControl` is a `UIElements` column.** `Control.visual` is `Pool.GetRef<VulkanControl>(dataHandle)`; the `_visual` field is gone. `Emit` copies it into the `UIQuads` row, then runs `PaintRow(ref row)` on the copy, before the gradient words.
- **`[A_Animatable(column, field)]`** — `changed` is optional; `InPlace` compiles no call without it. `OnValue` calls the setter only when `width == 0`, else `changed` if there is one.
- **In place on `VulkanControl`:** `Control.alpha`, `Control.edgeThickness`, `ButtonControl.state`. `VulkanControl.edgeThickness` is a `Thickness` (was `Vector4` — same 16 B and order, the shader still reads a `vec4`), because `InPlace` requires the property's type. `Animation.Step` declares `UIElements.VulkanControl`.
- **The column holds what was authored or animated; the drawn row is derived.** `Control.PaintRow` zeroes an unauthored Clear role's alpha (was the `alpha` setter, `colorHex`, `ApplyRole`). `ButtonControl.PaintRow` is the old `PaintState` on the row: rest paint + raw `s` for a palette surface rest (GPU blend), a CPU lerp and `state = 0` otherwise, `alpha × min(s, 1)` for a Clear palette button. `ApplyRole` and the constructor write `restPaint` to the column.
- **`TextRunControl`'s `alpha` override removed** — it kept `_alpha` apart from the column, so an in-place write would have bypassed it. Glyphs take the column's alpha, read once per `Emit`, as is `effectStart`.
- **`UITreeDump` dumps `visual` passed through `PaintRow`**, so its `Paint`/`Alpha` stay what is drawn.

**The button blend stays on the CPU (fork 3 → b, user).** Paint-at-draw already ran `PaintState` for every drawn palette button every frame, so moving it to `Emit` adds work only for authored-colour buttons off rest. The shader route would have put hover/press words on every quad row, glyphs included — what [[ui-palettes]] rejected on 2026-09-19.

**Known gaps**
- **Not measured** (user): every `visual` access is a pool column lookup, as `arrange` already was — ~4–5 per drawn control per frame (an array index since 2026-09-26, [[ecs-rework-data-pools]] § Column keys; `DrawLists` 0.80 → 0.45 ms at 200k); `UIElements` rows +96 B (the `Control` object −96 B), and resequencing moves the column.
- A hovered button with authored state colours hands its children its rest colour as ground for ink contrast, not the blended one; palette buttons already did.
- `visual` read on a destroyed control after its row is freed throws or reads another row — as `arrange` already did.
- `ApplyShape` rewrites `edgeThickness` every drawn frame on `accentRole` controls (`TabViewControl` tabs, `FileBrowserControl` rows), so an edge animation there loses — before and after.
- **Verified**: Debug build clean; Thorium and Carbon boot-verified. **GUI-verified** (Thorium, capture): menu-button hover fade, its `underline` clip in place and its reversal; `menu-row` binding (edge bar, padding) and its return to rest; a toolbar Clear button's hover (pixel diff); tab and window close red hover (CPU lerp); `Add vault` Chrome GPU blend + `chrome-press` edge; the caret blink (`alpha` setter). **NOT GUI-verified**: press colours in detail, theme crossfade, disabled-button dimming, profile scenarios and the tree-dump compare (not run).

## Step 2 of the frame scheduler — requests written in place, values read from a pool (2026-09-23)
Supersedes every `Post`/`OnPost`/lane path below, the push-vs-pull choice, `FadeSeeded`, and the 682-start / ~1,024-value ceilings. See [[frame-scheduler]] § Step 2.
- **Requests are direct writes.** `Animations.Write(in AnimationRequest)` (was `Send`) applies the request to the pools on the spot, from whichever Main step calls it — the body of the old `AnimationSystem.OnPost`, moved. The step must declare `Animations` (and `Signals` for `Signals.Set`, `Paints` for `FadeSlots`); the graph orders it against `Animation.Step`. A `Stop` lands the same frame. `Write` cannot refuse, so `Tween`/`Spring`/`Play` never return `None`/empty for backpressure — the refusal branches are gone (call-site `None` checks in `FileTreeControl` and `ProfileScenario` are now unreachable, left in place).
- **Requests during bootstrap now apply** — before, no sending system existed and `Post` refused them silently.
- **Values go through pool `AnimationValues`** (`AnimationValue` column, handle-less, 256 + 256). `Animation.Step` rewinds it and appends one row per stepped track; `Main.Apply` reads it (`Animations.ApplyValues`, via `Backing`) the same frame and calls `OnValue` per row. No cap: values applied = tracks stepped on every frame at 20k.
- **Pool-stored properties are written in place.** `[A_Animatable(typeof(ArrangeData), nameof(ArrangeData.preferredWidth), nameof(InvalidateLayout))]`: `AnimatableProperty` resolves the field's offset (`Marshal.OffsetOf`, guarded by a marshal-vs-managed size check) and width (1/2/4 floats), and compiles the `changed` call. Starting a track stores `target` (`Entity.dataHandle`), `column`, `offset`, `width` in `AnimationTrack`; `Animation.Step` writes the value through `DataPool.ElementBytes` (empty when the handle is stale — the write is skipped). `OnValue` then calls `changed` (the invalidation) instead of the setter. Today: `Control` `Width Height MinWidth MinHeight Margin Padding` (→ `InvalidateLayout`), `HorizontalPos VerticalPos` (→ `InvalidateArrange`); `Animation.Step` declares `UIElements.ArrangeData`.
- **Mark dirty now, invalidate at the end (user).** The value row is the dirty mark; the invalidation runs once per changed row in `Main.Apply`, before `Main.Layout`. Animation does not set the row's own `ArrangeFlags` — `InvalidateLayout` returns early on an already-dirty row and could then never propagate. Removing the invalidation later means layout finds changed rows itself and the `changed` argument goes.
- **Setter path stays** for properties that are plain fields: `Control.alpha`, `Control.edgeThickness`, `ButtonControl.state`, `ContextMenuControl.reveal`, `StateBinding.state`. Moving them into pools is its own change.
- **Slot fades seed on Main.** `FadeSlots` → `Write` → `AnimationSystem.SeedFade` (now internal) copies the colours and queues the fades, then `FadeSlots` runs `onSeeded` on the spot. `FadeSeeded`, `pendingFades`, `OnFadeSeeded`, `_unsentSeeded` are deleted. The `_fades` list is shared C# state, ordered only because every caller and `Animation.Step` declare `Paints`.
- **`Animation.Step` reads `Signals` and `Keyframes` through `Backing`** — `GetSpan` asserts a write.
- **Entity transforms are not animatable yet.** The mechanism is generic (any `Entity` pool column); the day an entity property is tagged, `Animation.Step` gains `Entities.TransformData` and the Animation-vs-Physics order on shared entities must be decided.

## What changed

- **Fourth thread.** `AnimationSystem : ThreadedSystem` (`[A_XSDType("Animation","Systems")]`, 120 Hz, own `Stopwatch` dt). Built with the others before `ResolveOwners`/`BuildLanes`, started with physics and render, stopped in `Engine.Stop`. **REVISED 2026-09-23:** a step of the frame graph, run every frame by whichever worker claims it; no 120 Hz of its own — [[frame-scheduler]].
- **`FrameEdge` is per system.** `DataManager.FrameEdge(ThreadedSystem owner)` runs only that owner's pools. Main calls it with `mainSystem`, Animation at the end of its `Tick`. The first non-Main pool would otherwise have thrown the owner assert from `MainTick`. **REVISED 2026-09-23:** `FrameEdge()` runs the pools the running step writes in full; pools have no `System` owner.
- **`CommandOp.Post`** — a message to the owning *system*, not to a pool column. `SystemCommand.Post(kind, offset, size, producer)`: `ColumnId` = kind, `Count` = payload bytes. `ThreadedSystem.Post<T>(target, kind, in T)` enqueues on the producer's existing lane to `target`; `Drain` hands a `Post` to the virtual `OnPost(kind, ReadOnlySpan<byte>)` instead of `CommandApplier`. False on backpressure or off a system thread.
- **Tracks.** Pool `Animations`, `System="Animation"`, handle-less, row index = track id, column `AnimationTrack` (driver, sleeping, generation, curve, from/to/value/velocity, duration/elapsed, frequency/damping). Rows are appended on demand when a request names an id past the count; never freed.
- **Main → Animation:** `AnimationRequest` (`Tween`, `Spring`, `Retarget`, `Stop`), post kind `AnimationSystem.requestKind`.
- **Animation → Main:** `AnimationValue` (track, generation, value, done), post kind `valueKind`, one per active track per tick. `MainSystem.OnPost` → `Animations.OnValue` → the property setter, during Main's `Drain` — before input, `OnTick` and `ResolveLayout`.
- **Ids and bindings live on Main** (`Animations`): a free list of ids, each with target, `AnimatableProperty` and a generation bumped per start. A value whose generation does not match is dropped, so a recycled id never takes the old track's value. A tween's `done` frees its id after the final value applies. `Stop` frees at once.
- **API (Main thread only):** `Tween(target, property, Vector4 to, seconds, Curve, Action? onDone)` (`onDone` runs once after the final value applies; never on `Stop`), `StopAll(target)` (called from `Control.OnDestroy`, so no ad-hoc tween writes into a destroyed control; looks up only that target's tracks through `byTarget`, 2026-09-24), `Spring(target, property, frequency, damping)` (rests at the current value), `Retarget(handle, Vector4)`, `Stop(handle)`. Start returns an `AnimationHandle(id, generation)`, `AnimationHandle.None` when the post was refused; a handle whose generation no longer matches is ignored (user, 2026-09-19 — rejected: a bare int id, which a finished tween's reuse made unsafe).
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
- **`StopAll` was O(bindings) per call** — closed 2026-09-24: `Animations.byTarget` (target → live track ids, kept in `Bind`/`Release`) makes it O(that target's tracks). 200k teardown 267 s → 78 s; the rest was entity-group removal, closed 2026-09-24 by the tick list → [[entity-tick-group]]. See § Measured at 200k.
- **Slot fade over every paint slot is negligible.** The Animation frame mean was 0.04 ms.
- `Interpolate` with no layout work: 1.65 ms at 20k controls, 12.5 ms at 200k. This is the entity tick loop, i.e. [[entity-tick-group]] measured.
- Today's UI runs tens of tracks and everything stays well under a millisecond. The first real-use limit is the 682 start ceiling, e.g. a folder of more than 682 rows expanding in one tick. **Both ceilings are gone since 2026-09-23** — see § Step 2 of the frame scheduler and [[frame-scheduler]] § Step 2 Measured.
- **The table above is unoptimized Debug JIT** — see [[profiling-unoptimized-jit]]; § Measured at 200k is not.

## Measured at 200k (2026-09-24, optimized JIT)

`--profile-scenario=animation` with a scratch ladder `{ 20000, 200000 }`, `dotnet build -p:Optimize=true --no-incremental` (DEBUG and its asserts still on), 16 logical cores. Mean ms per frame over each 240-tick hold.

| 200k, `Threads=1` → auto | Frame | `Anim.Step` | `Anim.Emit` | `Main.Apply` | `Main.Layout` | `Main.Logic` |
|---|---|---|---|---|---|---|
| burst (tween) | 23.4 / 23.5 | 4.7 / 4.4 | 3.8 / 3.8 | 0.2 | — | 17.7 (tweens finishing, `onDone`) |
| state clip | 48.4 / 46.0 | 7.5 / 4.4 | 2.9 / 3.2 | 36.9 | — | 3.4 |
| margin clip | 253.9 / 238.3 | 9.7 / 5.5 | 2.8 / 3.2 | 45.1 | 195.1 | 3.2 |
| spring | 10.7 / 10.2 | 2.0 / 1.2 | 0.4 / 0.4 | 4.7 | — | 3.2 |

- **Animation itself is the small part.** The margin frame is layout 195 (arrange 101, measure 41, `SubtreeCache` 24, `VerifyCache` 29 — `[Conditional("DEBUG")]`), `Main.Apply` 45, `Anim.Step` 10.
- **The run took 10.5 min, 612 s of it at 200k, and 475 s was not animating:** teardown 267 s (30 frames, 8.9 s each, all `Main.Logic`) and stopping handles 209 s (2,401 frames at 250 a tick, layout still running).
- **Teardown was two quadratics.** `StopAll` per destroyed control over every binding (fixed, `byTarget`), and `EntityGroup.Remove` → `List.Remove` over 200k entities per destroy — one 78 s frame here; closed 2026-09-24, the `"Entities"` list is deleted → [[entity-tick-group]].
- **The tick loop was 3.3 ms a frame at 200k** with nothing ticking — every entity visited for its `tickable` flag; since 2026-09-24 only opted-in entities are walked → [[entity-tick-group]].
- **`Main.Apply` ~0.19 µs per value.** One `Profiling.Zone.Increment` per value (two string-tuple dictionary lookups) was part of it; it is now one per frame: state hold 37.6 → 23.2 ms, margin 45 → 30 ms.
- **Emit does not parallelise** (~3 ms serial at 200k), so `Anim.Step` only goes 7.5 → 4.4 ms from 1 to 14 workers. Leaves with `Main.Apply` ([[animation-in-place-plan]]).
- **Render redraws unpaced while Main has nothing new** — ~3,500 frames a second against Main's 4. The Render lane is 275k frames per 64 MB capture file, so a 200k capture rolls across eight folders.
- **After the fixes** (`byTarget`, one `Increment`, scenario batches scaled to N — ~20 ticks a ramp, ~80 a stop pass): 176 s at 200k, run 3.2 min; teardown 78 s in one frame; `StopAll` stage 230 → 35 ms a frame.
- **Verified:** scenario ran to completion at 20k and 200k, exit 0. **NOT verified:** `StopAll` from real UI (file-tree collapse, destroy mid-animation) by hand.

## Measured at 200k (2026-09-26, Release+PROFILE, in place)

Ladder `{ 200000 }`, build `02b2214`. Mean ms per frame over each 240-tick hold; "was" is the same script on the 09-25 build before the in-place landing.

| 200k, ms/frame | Frame (was) | `Anim.Step` (`Emit`) | `Main.Layout` (measure / arrange / subtree) | `DrawLists` |
|---|---|---|---|---|
| state hold | 6.1 (25.0) | 5.2 (2.8) | — | 0.8 |
| margin hold | 106 (135) | 7.1 (4.4) | 98.3 (19 / 51 / 12.5) | 0.7 |
| sparse margin hold | 83.9 (108) | 0.07 | 83.0 (19 / 51 / 12.6) | 0.7 |
| spring hold | 4.9 (7.3) | 4.1 (2.1) | — | 0.8 |
| `StopAll` | 5.9 (24.3) | 4.8 (2.4) | — | 0.8 |

- **`Main.Apply`'s 20–24 ms is gone; `Anim.Step` gained 1.4–2.6 ms** writing values in place. `Emit` is still serial (its per-track pass moved into the chunks the same day — § `Emit` folded into the chunks).
- **`DrainLayoutDirty` is ~16 ms of `ResolveLayout` at margin hold** — by difference, it has no zone; 0.35 ms at 1,000 rows, ~80 ns a row.
- Build frame 1,156 ms: `Scenario.Build` 575, first `ResolveLayout` 535 (arrange 309, measure 120). Scenario 62 s. With `DOTNET_TieredCompilation=0` they are 373 and 143 — the first layout is mostly tier-0 JIT.
- **Kept from 2026-09-25, not beaten:** teardown worst frame 49 ms (was 78 s) — [[entity-tick-group]]; this run 175 ms, the 09-25 script run 177.
- **Kept from 2026-09-25, not beaten:** burst worst frame 4,658 → 192 ms. It was 200k `Tween`s 3.65 s + first `Emit` 937 ms, both additive +256 pool growth → [[pool-shrink]]. This run 403 ms, 387 of it the `Tween` calls; the 09-25 script run 439.
- **Column keys and the stack's single row read (same day):** margin hold 106 → 50 ms, sparse 84 → 31 — [[layout-dod-plan]] § Baseline.
- **Layout is ~0.15 µs per control on a full relayout** (was ~0.41 while every `arrange` access was a `Dictionary<Type>` column lookup), and `Measure`/`Arrange` recurse into clean children. 1M in 1 ms is ~1 ns per control — not reachable by relayout; see the WIP layout entry.

Related: [[ui-palettes]], [[ui-gradients]], [[ecs-rework-data-pools]], [[cross-system-change-notification]], [[entity-tick-group]], [[engine-profiling]]
