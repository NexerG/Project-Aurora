# Decision — an entity's columns are what its pool declares, not a hardcoded transform

**Date:** 2026-09-06
**Status:** landed, GUI-verified through Thorium.
**Scope:** `ArctisAurora.Core.ECS.EngineEntity` — `Entity`, `TransformEntity`, `LightSourceEntity`;
`ArctisAurora.EngineWork.ComponentBehaviour` — `EntityComponent`;
`ArctisAurora.Core.Registry` — `EntityRegistry`; `ArctisAurora.Core.UI` — `Control`;
`ArctisAurora.Core.UISystem.Controls` — `VulkanControl`

Authorised by the user, against CLAUDE.md's standing "do not refactor the entity/component model without
asking" constraint.

## What changed

- `Entity` lost `transform`, `SetPosition`, `SetScale`, `SetRotation`, `SetTransform`, and the constructor's
  `transform.scale = (1,1,1)` seed.
- `private void AllocatePooledTransform()` → `protected virtual void AllocatePooledData()`. It binds `PoolName`
  and takes a row; it no longer touches any particular column.
- New `TransformEntity : Entity` carries the accessors, the four `Set*` helpers, and the scale seed in an
  `AllocatePooledData` override.
- New `protected DataHandle AllocateIn(string poolName)` on `Entity` — a row in a second pool, recorded in a
  `DataHandle[]` that stays null until used.
- New `internal void FreePooledData()` frees the primary row and every extra. `DataHandle` carries its own
  `PoolId`, so nothing has to remember which pool a row came from.
- `EntityRegistry.ProcessDestroys` calls `entity.FreePooledData()` instead of `entity.Pool.Free(entity.dataHandle)`.
- `EntityComponent` gained `protected ref TransformData transform => ref ((TransformEntity)parent).transform;`
  and its 13 `parent.transform` sites dropped the `parent.`.
- Retyped to `TransformEntity`: `VulkanControl`, `LightSourceEntity`, `SimulatorEntity`, `TextEntity`.
  `Layer` and `TestingEntity` use no transform and stay on `Entity`.

## Why these choices

**`transform` was never a field, so the saving is in the pool, not the object.**
It is a ref-returning property over a pool column — zero bytes per object. The 36 B of `TransformData` lives in
the column, and which columns a pool has is already declared per-pool in `Pools.pools.xml`. `Entity` hardcoded
only the *assumption* that the column was present, in the constructor seed and the accessors. Removing both is
what makes an entity on a transform-less pool cost nothing for one.

**C# has no partial inheritance, so the accessors had to move down.**
A derived class always carries every field its base declares; there is no way to omit one. Leaving `transform`
on `Entity` and letting it throw was the cheaper option (three lines) but leaves dead API on every `Control`.
The split was chosen instead: `Control : Entity` now has no transform API at all.

**Per entity-kind, not per instance.**
A dense pool column is capacity-sized, so every row in a pool has every column. "This entity has a transform,
that one doesn't, within one pool" would need archetypes or a sparse side-table — a real ECS build, explicitly
not taken.

**A handle set rather than one handle per entity.**
`Control` needs two rows, in `UIElements` and `VulkanControls`. The alternative was subclasses freeing their
extra handles in `OnDestroy`, which works because `ProcessDestroys` calls `OnDestroy` before the free — but it
makes every multi-pool entity responsible for its own teardown. A `DataHandle[]` that is null for the common
single-pool case costs 8 B and makes the free generic.

## Known gaps

- **`Control` now joins the entity lifecycle.** It gets `EnqueueStart`, lands in the `"Entities"` group, and is
  iterated by `Engine.Interpolate`'s tick loop. Intended — animation is to run on `OnTick` plus components —
  but it is what makes [[entity-tick-group]] necessary.
- **`Control` inherits `children` as `List<Entity>`**, so tree walks cast. Required, not preference:
  `Entity.Destroy()`'s `EnqueueSubtree` walks that list, and a shadowed typed list would silently orphan
  subtrees.
- `EntityComponent.transform` throws for a component attached to a non-`TransformEntity`. Loud, not silent.
- `AuroraEditor.Editor.cs` has a `test.transform.SetWorldPosition` inside a comment block that will not compile
  if uncommented until that entity becomes a `TransformEntity`.
- `_components` and `children` are still eagerly allocated on every entity — 32 B each, 64 B per entity,
  whether used or not. User chose not to make them lazy (2026-09-06). `_components` is read at 13 sites, all
  inside `Entity`; `children` at 136 sites across 37 files, most of them in the UI stack being deleted.
- `DataPool` still has no `Write<T>(handle, value)` — assign plus `MarkContentDirty` in one call. Every caller
  hand-rolls it (`TransformEntity.SetPosition`, `VulkanControl.UpdateControlData`, `Control.Publish`).

Related: [[entity-tick-group]], [[ui-engine-stack]], [[ecs-rework-data-pools]], [[entity-lifecycle-queues]],
[[entity-reparenting-and-names]]
