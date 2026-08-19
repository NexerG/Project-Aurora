---
date: 2026-05-30
Status: Current
tags:
  - d_Entity
cssclasses:
  - Aurora.css
Linker:
  - "[[Arctis Aurora]]"
System:
Class:
  - "[[Entity]]"
Parent Class:
Interfaces:
Used by:
  - "[[Vulkan Control]]"
Type:
  - Public
Attributes:
  - A_XSDType
  - Serializable
Namespace: ArctisAurora.Core.ECS.EngineEntity
SourceFile: AuroraEngine/Core/ECS/EngineEntity/Entity.cs
VerifiedAgainst: 2026-05-30
---
## Description

The base object the engine simulates. It owns a [[Transform]], a list of components ([[EntityComponent]]), and child entities. The constructors auto-register it into the `Entities` and `EntitiesOnStart` entity groups. Marking it dirty enqueues it into `EntitiesToUpdate` and cascades to children.

> The ECS is currently class/object-based rather than data-oriented â€” a known piece of engine techdebt.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `CreateComponent<T>()` | public | Add a component (special-cases `MeshComponent` â†’ the renderer's mesh type). |
| `GetComponent<T>()` / `RemoveComponent<T>()` | public | Find / remove a component by type. |
| `AddChild(Entity)` / `CreateChildEntity<T>()` | virtual | Parent another entity. |
| `RemoveChild(Entity)` | virtual | Detach a child, keeping it and its subtree alive. |
| `SetParent(Entity)` | public | Move a live subtree to another parent. |
| `GetChildEntityByName` / `GetAllChildrenEntitiesByName` / `GetAllChildrenEntities` | virtual | Child queries, direct children only. |
| `FindByName(string)` | virtual | First entity in this subtree, itself included, carrying the name. |
| `MarkDirty()` | public | Set `isDirty` (enqueues for GPU update). |
| `Invalidate()` | virtual | Fire `OnInvalidate` on components + enqueue for update. |
| `OnStart` / `OnEnable` / `OnDisable` / `OnTick` / `OnDestroy` | virtual | Lifecycle â€” forward to components. |

## Fields & Properties

```C#
[@Serializable] bool enabled = true;
[@Serializable] public Transform transform;
[@Serializable] [A_XSDElementProperty("Name", "EntityRegistry")] public string name = "entity";
[@Serializable] public List<EntityComponent> _components = new();
[@Serializable] public List<Entity> children = new();
[NonSerializable] public Entity parent;

[NonSerializable] public bool isDirty   // setter â†’ EntityRegistry.AddToGroup("EntitiesToUpdate", this) + cascades to children
```

## Methods

### Lifecycle
`OnStart`/`OnEnable`/`OnDisable`/`OnTick`/`OnDestroy` simply iterate `_components` and call the matching hook on each (see [[EntityComponent]]).

### Components
`CreateComponent<T>()` instantiates and attaches a component (no duplicates). For `MeshComponent` it picks the concrete mesh type from `Renderer.renderingModules[0].rendererType` (`MCRaster` / `MCUI` / `MCRaytracing`). `GetComponent<T>` / `RemoveComponent<T>` scan `_components` by type.

### Tree edits
`AddChild` appends and takes ownership; `RemoveChild` drops the child and clears its `parent` without destroying it, so the subtree survives the detach. `SetParent` is the pair of them — it refuses to parent an entity into itself or its own descendant, then detaches and attaches through the new parent's own `AddChild`, so each container's rules still apply. [[Vulkan Control]] overrides `RemoveChild` to invalidate layout and flag the pool for a resequence, mirroring what its `AddChild` already does. A control left detached rather than re-attached counts as a tree root and keeps rendering at its last transform.

`FindByName` walks the subtree depth-first and returns the first match including itself, where `GetChildEntityByName` next to it only scans direct children. The name comes from XML through the `Name` attribute every `[A_XSDType]` entity inherits. [[Vulkan Control]] overrides it to return a control rather than an entity, which it can because a control only ever hosts controls as children.

### Dirty / update
The `isDirty` setter (and `MarkDirty()`) register the entity into the `EntitiesToUpdate` group via [[Asset Registries|EntityRegistry]] and propagate dirtiness to all children. `Invalidate()` additionally calls `OnInvalidate` on each component.

## Related
- [[Transform]] Â· [[EntityComponent]] Â· [[Vulkan Control]] (a derived control entity)
