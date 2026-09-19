# Animation system — palettes and gradients merged, one procedural animation core

**Status:** AGREED 2026-09-18 (design and slice order). Slices 1–3 and 5–8 landed; 9 is blocked on physics. Each slice gets its own plan before code.
**Checklist form:** the animation item in `DOCUMENTATION/Work in Progress List.md`.
**Replaces** `ui-animation-plan.md` (deleted) and Phase C's "animation/evaluation core" item — one core serves UI,
game and scripted clips (user, 2026-09-18).

## The gap

- Nothing changes over time on its own. Every visual holds until something writes it again.
- Wanted by: gradients (table never rewritten), position/size (splitter, pane, tab move, context menu snap),
  colour (`ButtonControl.ApplyState` swaps hover/press instantly), overscroll (rubber band rejected 2026-08-30
  for want of a spring driver).
- Scope asked: animate anything animatable — entities, colours, shader parameters — with easing; UI and in-game;
  procedural, driven by interaction (hover, press) and later by physics impulses.

## Palette and gradient today (why they need reworking)

- Two tables, two lifetimes: the paint table is mapped per swapchain image and replaced whole; the gradient table
  uploads once through `CreateBuffer` and is never rewritten. Neither can animate.
- `VulkanControl.gradientIndex` is a separate field that replaces only the fill: no edge gradient, no per-state
  gradient.
- A role stop takes the 8 surfaces or `Ink` only.
- Hover/press are discrete baked slots, swapped instantly.
- See [[ui-palettes]], [[ui-gradients]].

## A — one paint concept

1. **Paint word, three forms**, tag in the top bits: inline `0xRRGGBB` / palette slot / gradient (14-bit id +
   16-bit palette base). Fill, edge, text run and gradient stop are all paint words. `gradientIndex` leaves the
   row (88 → 84 B); edge and per-state gradients come for free. **Landed 2026-09-18** — see [[ui-gradients]] §9.
2. **A gradient stop = paint word + shade** — generalises today's `{rest, stepped, shade}`; a stop can name any
   slot (`MutedInk`, `EdgeAccent`, state slots).
3. **State is a continuous float on the row** (F2). Rest/hover/press are adjacent slots; the shader blends across
   them with `s ∈ [0,2]`, the way stops already blend. A fading hover keeps following the palette — no inline
   fallback, no CPU recolour. Cost: one float per row, two paint reads.
4. **`Paints` and `Gradients` become pools** on the per-image mapped path, mirrored by dirty range. A theme
   crossfade or an animated gradient is then an ordinary column write.

## B — the animation core

**An animation is a stateful integrator, not `f(t)`:** `state' = step(state, dt, inputs)`. A tween's state is
elapsed time; a spring's is position + velocity, and an impulse adds velocity. One shape covers scripted,
interactive and physical motion.

| Layer | What |
|---|---|
| Curves | Penner easings (in/out/in-out per family), `cubic-bezier`, `steps`, keyframe tracks (reuse bezier math) |
| Drivers | Tween (clock), Spring (critical/underdamped, impulse-able), Oscillator/Noise, Follow (chases a signal) |
| Signals | named float inputs anyone can write — `hover`, `press`, `focus`, `physics.contact` |
| Targets | a pool column (entity, paint slot, gradient stop, shader params) or an `[A_Animatable]` property on a managed object |
| Runner | iterates only active animations, pooled by driver type |

- `ButtonControl` stops hard-coding state: pointer events set signals, a spring follows each, the spring drives
  the state float. A pressed button and a physics impulse on a prop are the same path.
- **Colour interpolation is sRGB-space only**, matching gradients ([[ui-gradients]] §6). More spaces are wanted
  (F3); the colour-space parameter lands with the second space, not before (user, 2026-09-19 — revised from
  "a parameter from day one").
- **GPU-evaluated tweens** (start, duration, ease on the row, evaluated against `GpuEngineStats.totalTime`) for
  per-glyph effects — a slice inside this pass (F4). Fire-and-forget: not interruptible, no impulses.
- Authoring: `*.anim.xml` + generated XSD for clips, tracks, state bindings. Transient UI animations are built
  in code on the same runtime.

## C — threading and data ownership (F5)

The animation system is its own `ThreadedSystem`. It does not reach into other systems' tables; it owns a
defined set of inputs and outputs.

**Inputs — pushed to it.**
- `Signals` pool, Animation-owned. Main writes `hover`/`press`/`focus` through a lane; Physics writes contacts and
  impulses the same way. Nothing polls UI or physics state.
- Poses are read from Physics' published snapshot, never its live tables.

**Outputs — split by who must react.**
1. **Presentation — REVISED 2026-09-19 (user).** Animation owns the *behaviour over time* — states, signals,
   transitions, easing, springs — and the shared tables it animates (`Paints`, `Gradients`). The *resolved look*
   stays in `Control.visual` on Main, where the tree that resolves it lives; animated values reach it by `Post`
   through the setter, as slice 3 does. ~~`visual` splits into an Animation-owned `Style` pool the renderer reads~~
   — dropped: ~30 `visual` write sites resolve from the tree and would each have become a cross-thread message
   with a tick of latency, `Emit` would read a foreign pool per control per frame, and it needed structural
   mirroring of `UIElements` — for nothing slices 5–7 require.
2. **Values another system's logic depends on** — layout size, a transform physics reads, gameplay values.
   Animation is a producer: `Send` a command, the owner applies it at its drain. UI applies through the property
   setter so `InvalidateArrange` still fires. One tick of latency, deterministic.

**Physics and IK.**
- Not merged into Physics: different rates; merging ties hover fades to the physics step.
- Two clock domains on the Animation thread:
  - **Presentation** — wall clock, every tick. UI and style.
  - **Simulation** — advances per physics-published step, fixed dt, deterministic (netcode). Game motion.
- Per simulation step: Animation samples clips/procedural → target pose → Physics solves by authority mode →
  publishes → Animation post-pass (IK, secondary springs, look-at, foot placement) → final pose → Render.
- Transform authority per entity: **Kinematic** (Animation drives, physics follows), **Dynamic** (physics drives,
  Animation reads back), **Powered** (Animation targets, physics motors toward it — active ragdoll).
- IK lives in Animation — it is a pose modifier.
- Threads become Main (input, UI structure/layout, entity logic), Physics, Animation, Render.

## Open problems

- ~~**Cross-thread Style rows — shared stable id** (2026-09-18)~~ — moot: the `Style` pool was dropped
  (2026-09-19). The idea survives in slice 3's requester-assigned track ids.
- **Render reading Animation-owned pools** races the same way `UIQuads` does today; rides the address-stable
  storage rework.
- **Latency:** hover → pixel grows to ~2 ticks. Fine at 60 Hz; felt on press if Animation paces below render rate.

## Slices

- [x] 1. Paint word with three forms; `MutedInk` stops; edge gradients (2026-09-18) — GUI-verified, [[ui-gradients]] §9
- [x] 2. `Paints` / `Gradients` as pools on the mapped path (2026-09-19) — GUI-verified, [[ui-palettes]] § Paint and gradient tables are pools
- [x] 3. `AnimationSystem` (`ThreadedSystem`), curves, Tween/Spring drivers, `Retarget` as the minimal signal, `[A_Animatable]` binding (2026-09-19) — GUI-verified, [[animation-core]]. Results are **pushed** by `Post` (user), not pulled; tests were a scratch harness, no `AuroraTesting`
- ~~4. `Style` pool — `visual` split out of `Control`~~ — **DROPPED 2026-09-19** (user), see § C Outputs 1
- [x] 5. Continuous state float (in `visual`, driven by posted springs); `ButtonControl` on signals; named `Signals` pool; spring feel on the palette (2026-09-19) — GUI-verified, [[ui-palettes]] § Continuous state, [[animation-core]]
- [x] 6. Theme crossfade (paint slots animated); `Paints` owned by Animation; `ThemeFade` on the palette (2026-09-19) — GUI-verified, [[ui-palettes]] § Theme crossfade
- [x] 7. GPU-evaluated tweens for per-glyph effects (2026-09-19) — GUI-verified, [[ui-effects]]
- [x] 8. XML clips / XSD — `*.anim.xml` clips (keyframe tracks) and state bindings; `Clip`/`HoverClip`/`PressClip`/`StateBinding` on `Control`; hover and press clips reverse (2026-09-19) — GUI-verified, [[animation-core]]
- [ ] 9. Simulation domain, authority modes, IK, the physics impulse lane — blocked on physics existing

Related: [[ui-palettes]], [[ui-gradients]], [[ui-palette-shape]], [[ui-quads-pool]], [[ecs-rework-data-pools]],
[[cross-system-change-notification]], [[entity-tick-group]]
