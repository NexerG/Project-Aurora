---
date: 2026-05-30
aliases:
  - EntityComponent
  - Entity Component
Status: Current
tags:
  - d_Entity
cssclasses:
  - Aurora.css
Linker:
  - "[[Entity]]"
Class:
  - "[[Entity Component]]"
Parent Class:
Interfaces:
Used by:
  - "[[Entity]]"
Type:
  - Public
Attributes:
  - Serializable
Namespace: ArctisAurora.EngineWork.ComponentBehaviour
SourceFile: AuroraEngine/Core/ECS/EntityComponent.cs
VerifiedAgainst: 2026-05-30
---
%% Historically linked as [[WIP]] from class docs while unwritten â€” now the EntityComponent base.
   Aliased so both [[WIP]] and [[EntityComponent]] resolve here. %%

## Description

The base class for **components** attached to an [[Entity]] â€” the object-oriented half of the engine's ECS. A component holds a back-reference to its `parent` entity and exposes virtual lifecycle hooks the engine calls at the right moments. Concrete components (mesh, light, simulation, etc.) override the hooks they care about.

> Note: the ECS is currently class/object-based rather than data-oriented (a known piece of engine techdebt â€” a struct/data-oriented model is planned).

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `OnStart()` | virtual | Runs when the component is created in the world. |
| `OnEnable()` / `OnDisable()` | virtual | Runs when the component is enabled / disabled. |
| `OnTick()` | virtual | Runs every engine tick. |
| `OnDestroy()` | virtual | Runs when the component is destroyed. |
| `OnInvalidate()` | virtual | Runs when the component is invalidated. |

## Fields & Properties

```C#
[NonSerializable] public Entity parent;
```

## Related
- [[Entity]] â€” owns a list of components; `CreateComponent<T>()` / `GetComponent<T>()` / `RemoveComponent<T>()` operate on this type
