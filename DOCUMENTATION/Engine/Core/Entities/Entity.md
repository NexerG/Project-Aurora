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
| `GetChildEntityByName` / `GetAllChildrenEntitiesByName` / `GetAllChildrenEntities` | virtual | Child queries. |
| `MarkDirty()` | public | Set `isDirty` (enqueues for GPU update). |
| `Invalidate()` | virtual | Fire `OnInvalidate` on components + enqueue for update. |
| `OnStart` / `OnEnable` / `OnDisable` / `OnTick` / `OnDestroy` | virtual | Lifecycle â€” forward to components. |

## Fields & Properties

```C#
[@Serializable] bool enabled = true;
[@Serializable] public Transform transform;
[@Serializable] public string name = "entity";
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

### Dirty / update
The `isDirty` setter (and `MarkDirty()`) register the entity into the `EntitiesToUpdate` group via [[Asset Registries|EntityRegistry]] and propagate dirtiness to all children. `Invalidate()` additionally calls `OnInvalidate` on each component.

## Related
- [[Transform]] Â· [[EntityComponent]] Â· [[Vulkan Control]] (a derived control entity)
