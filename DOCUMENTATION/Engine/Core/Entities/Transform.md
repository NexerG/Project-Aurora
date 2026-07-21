---
date: 2026-05-30
tags:
  - d_Entity
cssclasses:
  - Aurora.css
Status: Current
Linker:
  - "[[Entity]]"
Class:
  - "[[Transform]]"
Parent Class:
Interfaces:
Used by:
  - "[[Entity]]"
Type:
  - Public
Attributes:
  - Serializable
Namespace: ArctisAurora.Core.ECS.EngineEntity
SourceFile: AuroraEngine/Core/ECS/EngineEntity/Transform.cs
VerifiedAgainst: 2026-05-30
---
## Description

The position / rotation / scale of an [[Entity]]. Every mutating setter flips `_changed` and calls `parent.MarkDirty()`, so changing a transform automatically queues the entity for a GPU re-upload. Rotation is stored as **Euler degrees**; `GetQuaternion()` converts on demand.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `SetWorldPosition(Vector3D)` | public | Set absolute position. |
| `MoveToPosition(Vector3D)` | public | Move to a position and drag children along. |
| `SetLocalPosition(Vector3D)` / `MoveLocalPosition(Vector3D)` | public | Offset by a delta (the `Move*` variant also offsets children). |
| `SetWorldScale` / `SetLocalScale(Vector3D)` | public | Set scale. |
| `SetRotationFromVector3(Vector3D)` | public | Set Euler-degree rotation. |
| `GetQuaternion()` | public | Euler degrees â†’ `Quaternion`. |
| `GetEntityPosition()` / `GetEntityRotation()` / `GetScale()` | public | Accessors. |

## Fields & Properties

```C#
[@Serializable] public Vector3D<float> position = new(0, 0, 0);
[@Serializable] public Vector3D<float> rotation = new(0, 0, 0);   // Euler degrees
[@Serializable] public Vector3D<float> scale    = new(1, 1, 1);

[NonSerializable] internal Entity parent;
[@Serializable]   internal bool _changed = false;
```

## Methods

### Position
`SetWorldPosition` overwrites; `SetLocalPosition` / `MoveLocalPosition` add a delta. `MoveLocalPosition` recurses into children so they follow the parent.

### Scale / Rotation
`SetLocalScale` / `SetWorldScale` set `scale` directly. Rotation is set via `SetRotationFromVector3` (degrees) and read back as a quaternion via `GetQuaternion()`.

### Notes / gotchas
- `SetRotationFromQuaternion(q)` currently only calls `MarkDirty()` â€” it does **not** store the quaternion, so it has no visible effect yet.
- `MoveToPosition` computes its child delta *after* overwriting `position`, so the delta is always zero and children don't actually move. Use `MoveLocalPosition` if you need children to follow.

## Helpers

```C#
private float DegreesToRadians(float degrees) => degrees * (MathF.PI / 180f);
```

## Related
- [[Entity]] â€” owns one Transform
