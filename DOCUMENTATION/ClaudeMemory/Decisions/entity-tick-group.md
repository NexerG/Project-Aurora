# Decision — ticking is an explicit opt-in tick list, not a flag on every entity

**Date:** 2026-09-24 (raised and parked 2026-09-06)
**Status:** landed
**Scope:** `ArctisAurora.Core.ECS.EngineEntity` — `Entity` (`SetTicking`, `tickSlot`, `HooksOf`, `AfterStart`,
`ApplyEnableChange`, `CreateComponent`); `ArctisAurora.Core.Registry` — `EntityRegistry` (`SetTicking`,
`ProcessTicks`, `ProcessDestroys`); `ArctisAurora.EngineWork` — `Engine.Interpolate`, `Engine.SetupSystems`;
`CaretControl`, `DocumentToolbarControl`, `ProfileScenario`; `EntityRegistry.entities.xml`

## What changed
- `Engine.Interpolate` no longer walks every entity. `EntityRegistry.ProcessTicks()` runs `OnTick` over a packed
  tick list: `Entity[] _ticking` + `_tickingCount`; a member's `Entity.tickSlot` is its index, `-1` when unlisted.
- **Ticking is explicit.** `Entity.SetTicking(bool)` (public) sets `_wantsTick` and queues through the enable
  queue. Membership = `_wantsTick && _notifiedEnabled`, reconciled at the end of every `ApplyEnableChange`.
  Overriding `OnTick` does nothing on its own.
- Join/leave is `EntityRegistry.SetTicking(Entity, bool)` (internal): append, or move the last member into the
  hole. O(1), order not kept. Called only from `ApplyEnableChange` and `ProcessDestroys`, never during
  `ProcessTicks`.
- The `tickable` guard stays in the loop: an entity destroyed mid-pass is skipped that pass and leaves at the
  next `ProcessDestroys`.
- **The queues are filtered by override detection.** `Entity.HooksOf(type, root)` reflects once per type
  (cached `Dictionary<Type, Hooks>`), `Hooks { Start, Enable }` = overrides `OnStart`; overrides `OnEnable` or
  `OnDisable`, below `Entity` (or `EntityComponent` for a component).
  - no `Start` → skips the start queue; `AfterStart()` runs in the base constructor
  - no `Enable` and not opted in → the first enable notification is settled silently (`_notifiedEnabled =
    enabled`), no queue visit
  - `CreateComponent` ORs the component's hooks into the entity's
- `"Entities"` registry list and `Engine.entities` deleted — the tick loop was their only reader. `Unregister`
  no longer searches a list of every entity on each destroy.
- Opt-ins: `CaretControl` at construction and in `Focus`, out in `Blur` (a text box blurs its caret in its
  constructor, so text-box carets never join); `DocumentToolbarControl` and `ProfileScenario` at construction.

## Why these choices

**Explicit opt-in, the user's choice over override detection (2026-09-24).**
The requirement was conditional ticking — an entity is listed while it has work, e.g. the caret only while
focused. Override detection is what Unity (`Update`), Godot (`_process`) and Unreal Blueprints (Event Tick)
do, and every one of them pairs it with an off-switch (`enabled`, `set_process(false)`, `SetActorTickEnabled`).
Cost of the choice: an `OnTick` override without `SetTicking(true)` never runs, silently. Detection stays for
`Start`/`Enable`, whose callbacks are unconditional.

**A dedicated array with the slot on the entity — not a registry group, not a hash set.**
Measured on the user's Ryzen 9 9900X, .NET 10 Release, all four in one process, random removal order, 200k
objects of one class (scratch benchmark, not in the repo):

| Structure | Add all | Tick / entity | Remove all | Tick / survivor after 95% removed |
|---|---|---|---|---|
| `List<T>` (what `EntityGroup` wraps) | 1.0 ms | 0.61 ns | 6,066 ms | 0.82 ns |
| `HashSet<T>` | 8.3 ms | 0.68 ns | 3.6 ms | 11.1 ns |
| array + `Dictionary` slot map | 8.1 ms | 0.37 ns | 9.1 ms | 0.47 ns |
| array + slot field (landed) | 0.84 ms | 0.34 ns | 1.2 ms | 0.44 ns |

- `List.Remove` searches linearly: quadratic, 1M removals took 205 s. The parked shape of this note ("join a
  `Tickable` registry group") would have inherited it.
- `HashSet` hashes per instance (the 26-bit identity hash; 1M objects of one class → 99.3% distinct), so
  same-type objects are fine. But its enumerator walks every slot it ever used: survivors of a mass removal
  cost 19–25× per tick until the slots are reused or `TrimExcess()` runs. It also forces `foreach`, which
  throws on mid-loop mutation.
- Run-to-run spread between two runs of that benchmark: `List` removal 4×, the others within 2×. Compare rows
  of one run only.

**Swap-remove over order-preserving compaction.** No live ticker depends on tick order. Engines that need an
order layer it on top — Unreal tick groups, Godot `process_priority`, Unity script execution order.

## Measured — engine before/after
Headless harness (scratch, not in the repo): one source compiled against the before and after engine DLLs,
run back-to-back on the same machine. `Idle` overrides nothing; `Starter` overrides `OnStart`, like `Control`.
Destroy is `Destroy()` in creation order, so the stack pops newest first — what leaves-first destroy produces,
and the worst case for `List.Remove`. The 200k rows are single runs.

| 200k entities | Before | After |
|---|---|---|
| Idle — first `Interpolate` | 2.25 ms | 0.00 ms |
| Idle — steady tick | 400 µs | 0 µs |
| Idle — destroy all + drain | 34,359 ms | 3.9 ms |
| Starter — first `Interpolate` | 2.71 ms | 0.63 ms (start visits remain) |
| Starter — steady tick | 333 µs | 0 µs |
| Starter — destroy all + drain | 14,942 ms | 3.3 ms |
| create, Idle / Starter | 36 / 14 ms | 36 / 10 ms |

## Verified
- Builds clean; no new warnings in the touched files.
- Harness functional checks on the after build, 15/15: joins in the tick it was created; opt-out and back in;
  opt-out from inside `OnTick` finishes that pass and is gone the next; a ticker destroyed mid-pass by an
  earlier one is skipped, then removed; `OnStart` before the first `OnTick` in one `Interpolate`; created and
  destroyed in one tick never ticks; an `OnTick` override without opt-in never ticks; slots consistent after
  swap-removes; destroying everything empties the list.
- Thorium boot, temporary probe (removed): 1 of ~2,155 UI elements ticking at boot (`DocumentToolbarControl`),
  all 10–13 text-box carets unlisted; one click into a note → the document caret joined (2 ticking). A text
  box was destroyed during boot, so the destroy drain ran live. **GUI-verified**: crops of the caret alternate
  shown/hidden — it blinks.
- **NOT verified**: the toolbar's bold/italic reflection by eye; `--profile-scenario` not run (its capture prune
  deletes earlier sessions); AuroraEditor and Carbon not run.

## Known gaps
- `EntityGroup.Remove` is still `List.Remove`. The remaining lists — `EntitiesToUpdate` (written, never
  drained) and `EntitiesOnDestroy` (unused) — are empty in practice, but `Unregister` still walks them on every
  destroy, with an `IsAssignableFrom` each.
- `Destroy()` detaches the subtree root with `parent.children.Remove`, O(siblings); destroying many siblings
  one by one is still quadratic there. The child list is ordered, so that is a tree-storage change.
- `LightSourceEntity` and `SimulatorEntity` override `OnTick` but are never constructed, and do not opt in.
- A component overriding `OnEnable`, attached in the constructor of an entity type without an `OnStart`
  override, misses its first `OnEnable` — `AfterStart` already ran in the base constructor. Nothing does that.
- `RemoveComponent` does not recompute hooks (no callers).
- A deserialized entity whose type lacks an `OnStart` override would not start its deserialized components.
  No entity deserialization is live.

Related: [[entity-lifecycle-queues]], [[entity-transform-split]], [[ecs-rework-data-pools]], [[frame-scheduler]]
