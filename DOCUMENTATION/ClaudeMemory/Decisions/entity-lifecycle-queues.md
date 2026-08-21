# Entity Lifecycle — Queues Drained By Popping

Status: implemented 2026-08-21. Replaces the start half of the lifecycle; the destroy half keeps its
shape and only changes container and pop order. Complements [[ecs-rework-data-pools]] — that note's
locked decision "Destroy: control calls Free → enqueues to destroy queue → manager drains between
frames" now describes start and enable/disable too.

## The problem

`Engine.Interpolate` drained the `EntitiesOnStart` **group** with a `foreach` and then walked the
`Entities` group with another. Both are the live `List<Entity>` the registry group owns
(`EntityGroup.As<T>()` casts `_list`, it does not copy), and `Entity`'s constructor `Add`s into both.
So constructing an entity anywhere inside `OnStart` or `OnTick` — directly, or via anything that
builds a control or a document view — bumped the list version and threw
`InvalidOperationException` on the next `MoveNext`, inside the main tick, uncaught. Loading a note
from `OnStart` was enough; `Periodic.Main` opens the first note instead as the workaround.

Four more defects sat in the same code:
- A destroyed entity still got one `OnTick`. `ProcessDestroys` runs *before* the tick loop, so
  `Destroy()` called from an `OnTick` left the victim at a later index with its parent already
  detached, and nothing guarded on `_destroyed`.
- `Entity.OnDestroy` removed from `_components` inside `foreach (_components)` — throws for any
  entity with ≥1 component. Latent only because controls carry none.
- Components got `OnStart` twice: `CreateComponent` called it immediately and `Entity.OnStart` then
  walked `_components` and called it again.
- `enabled` was decorative — `IsEnabled` had zero callers, the tick loop never read the flag, and
  `OnEnable` never fired for anything.

## What was built

Three deferred queues on `EntityRegistry`, all drained by **popping** in one lifecycle phase at the
top of `Interpolate`: `ProcessStarts` → `ProcessDestroys` → `ProcessEnableChanges`, then the tick.

| Queue | Container | Fed by | Order it produces |
|-------|-----------|--------|-------------------|
| `_toStart` | `Queue<Entity>` | `Entity` ctor → `EnqueueStart` | creation order — a parent starts before children it built |
| `_toDestroy` | `Stack<Entity>` | `Destroy()` → `EnqueueSubtree` → `EnqueueDestroy` | reverse pre-order — every descendant tears down before its ancestor |
| `_enableChanges` | `Queue<Entity>` | `IsEnabled` / `BeginLife` → `EnqueueEnableChange` | flip order |

**Popping, not snapshot-and-clear, is the whole point.** No enumerator is held and no per-drain array
is allocated, so work pushed *during* a drain is picked up by the same drain. That is what makes
create-from-`OnStart` and cascading destroy-from-`OnDestroy` legal, and it removes the outer
"loop until drained" that a snapshot approach needs.

**Container choice is teardown order, and the two phases want opposite ends.** `EnqueueSubtree`
pushes pre-order (root, then descendants), so a stack pops descendants first — a child tears down
while its parent is still readable, where the old FIFO batch had a parent tearing down against
children that were flagged but intact. Start wants the opposite: everything here builds top-down
(`CreateChildEntity` news the parent first, XML parse is top-down), so LIFO would have inverted it
and started the deepest last-built child before the root. Hence `Stack` for destroy, `Queue` for
start.

## Flags, and why the container cannot carry them

- `_started` — set *before* `OnStart()` runs, so re-entrancy cannot double-start.
- `_destroyed` — skipped by both `BeginLife` and `ApplyEnableChange`. Needed because the start queue
  is no longer a registry group, so `Unregister` can no longer quietly pull a not-yet-started entity
  out of it. An entity created and destroyed inside one tick is dropped by the guard, not started.
- `_notifiedEnabled` — the state the entity was last *notified* about, distinct from `enabled`.
- `_enableQueued` — at most one pending entry per entity, cleared at the top of the drain so a flip
  inside `OnEnable` re-queues.

All four are `[NonSerializable]`. The serializer is opt-**out**
(`GetFields(Public | NonPublic | Instance)`, skip `[NonSerializable]`), so an unmarked `_started`
would persist and a loaded entity would never start again.

## Enable/disable semantics

`IsEnabled(bool)` sets the flag and queues; it no longer invokes the hooks. The tick gate is
`tickable => _notifiedEnabled && !_destroyed` — **the notified state, not `enabled`**. That buys the
invariant *`OnTick` only ever runs between an `OnEnable` and its `OnDisable`*:

- Flip twice inside one tick → `enabled == _notifiedEnabled` at drain → no callbacks at all.
- Disable during the tick loop → the entity still ticks that pass, then next tick the transition
  drain (which runs before the loop) fires `OnDisable` and the gate closes. One tick of latency,
  never a torn pair.
- Born disabled → `BeginLife` runs `OnStart`, queues, drain finds `false == false` → started, never
  enabled, never ticked. Correct without a special case.
- Entity created during `ProcessEnableChanges` (after `ProcessStarts` already ran) → it is in
  `Entities` and inside the tick loop's captured count, but `_notifiedEnabled` is false, so the gate
  skips it. **The flag gate, not the phase order, is what prevents a tick-before-start.**

## Tick loop

`for (int i = 0, count = entities.Count; i < count; i++)`, not `foreach` and not `ToArray()`. The
captured count means an entity created in an `OnTick` waits for the next tick; growth past `count` is
safe because `List<T>`'s indexer has no version check. Safe against shrink only because `Unregister`
is called from `ProcessDestroys` alone and `RemoveFromGroup` has no callers — if either changes, this
loop needs revisiting.

## Rejected / not done

- **Deferring registration into the `Entities`/`Controls` groups** along with the start callback.
  `VulkanControl`'s ctor registers into `Controls` and the renderer picks the control up from there,
  so deferring registration would put a one-frame delay on every control appearing. Only the
  *callback* is deferred; visibility stays immediate.
- **Guarding the unbounded drain.** An `OnStart` that unconditionally creates an entity hangs the
  tick instead of throwing. Self-inflicted and obvious; not worth a depth counter.
- **`OnDisable` before `OnDestroy`.** Unity-style symmetry; not asked for, not built. A destroyed
  entity that was enabled simply never receives its closing `OnDisable`.
- **`EntitiesToUpdate`** is still written on every `isDirty` set, cascades to children, and its drain
  is still commented out in `Interpolate` — duplicates accumulate forever. That is the separate
  locked item "defer isDirty propagation to one pass per tick".
- **`EntitiesOnDestroy`** (group in `EntityRegistry.xml`, `Engine.entitiesOnDestroy` field) is dead —
  never assigned, never read, its only consumer commented out. Left alone as pre-existing.

## Verified

Builds clean. Periodic boots, renders and stays responsive. A temporary counter in the tick loop
reported `1266/1266` tickable on tick 0 and `1273/1273` on ticks 1-2 — proving `ProcessStarts` and
`ProcessEnableChanges` both land before the loop, so an entity is started, notified enabled and
ticked in the tick it was created, with no latency. That probe mattered because **nothing in
Periodic overrides `OnTick`**: a gate stuck closed would have produced no visible symptom at all.

**The destroy drain is NOT exercised at runtime.** A Periodic boot destroys nothing, and text
deletion is still unreachable ([[ecs-rework-data-pools]], seventh slice), so leaves-first `Stack`
teardown is correct by inspection only. The old `DestroySelf` scene trick is the way to exercise it.

## Files

`Entity` (ctors, lifecycle region, `OnDestroy`, `IsEnabled`, `CreateComponent`), `EntityComponent`
(`started`), `EntityRegistry` (three queues + three drains), `Engine` (`entitiesOnStart` field and
its `SetupSystems` assignment deleted, `Interpolate` rewritten), `EntityRegistry.xml`
(`EntitiesOnStart` list removed). Resolve paths via `NAMESPACES.md`.
