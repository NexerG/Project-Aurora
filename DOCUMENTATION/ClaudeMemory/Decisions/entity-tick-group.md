# Decision — ticking should be a group, not a flag

**Date:** 2026-09-06
**Status:** **FUTURE** — raised and parked by the user, nothing built. Do not describe as existing code.
**Scope:** `ArctisAurora.EngineWork` — `Engine.Interpolate`; `ArctisAurora.Core.Registry` — `EntityRegistry`;
`ArctisAurora.Core.ECS.EngineEntity` — `Entity`

## The problem

`Engine.Interpolate` iterates **every** entity and virtual-calls `OnTick()` on the tickable ones:

```
for i in 0 .. entities.Count
    entity = entities[i]
    if not entity.tickable
        continue
    entity.OnTick()
```

`tickable` is a flag, so an idle entity still costs a load, a branch and a loop iteration. That was fine while
entities were scene objects counted in hundreds.

[[entity-transform-split]] put `Control` on `Entity`, which means **every UI element now enters that loop**.
The UI Engine plan sizes the data tier for ~1M elements. At that count the loop is ~1M iterations per tick to
find the handful of controls that actually animate.

## The shape

`EntityRegistry` already has named groups — `AddToGroup("Entities", …)`, `AddToGroup("Controls", …)`,
`AddToGroup("EntitiesToUpdate", …)` — and `EntityGroup` already publishes an `onChanged` event that `UIModule`
consumes.

So: an entity that needs ticking joins a `"Tickable"` group, and `Interpolate` iterates that group instead of
`entities`. An idle control costs **nothing** per tick rather than a predicted branch, and the cost of the loop
tracks what is animating rather than what exists.

Membership changes when the flag does — the entity joins on enable and leaves on disable, so the existing
`ApplyEnableChange` path is where it hooks in.

## Why this and not the alternatives

**Not a bitset over the dense pool.** The pool is already dense and ordered, so a parallel bit array would
iterate 1M bits to find a few set ones — cheaper per element than a virtual call, but still O(all entities).
A group is O(animating).

**Not "controls don't tick".** Animation is meant to run on `OnTick` plus components (user, 2026-09-06), so
controls have to be able to tick. The question is only whether the loop visits the ones that don't.

**Not deferring until it measures.** It will not measure today — the live control count is ~1,418. It is
recorded now because [[entity-transform-split]] is what created the exposure, and the reason will not be
obvious later.

## Known gaps

- Nothing built. No group declared, `Interpolate` unchanged.
- Unresolved: whether `OnTick` on a component implies its entity joins the group automatically, or whether the
  entity opts in explicitly.
- Unresolved: interaction with `"EntitiesToUpdate"`, which is the old stack's dirty-list and may be the same
  concern under another name.
- No measurement exists. `Profiling.Zone` plus a Carbon capture over `Interpolate` is how it would be taken,
  and it needs a populated UI to say anything.

Related: [[entity-transform-split]], [[ui-engine-stack]], [[entity-lifecycle-queues]], [[engine-profiling]]
