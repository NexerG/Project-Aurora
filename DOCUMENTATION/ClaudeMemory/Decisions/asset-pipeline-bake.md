# Decision — the pak file is a pre-concatenated cook cache; GPU offsets are assigned, never baked

**Date:** 2026-08-03
**Status:** DESIGN ONLY — nothing here is implemented.
**Scope:** future asset bake/cook pipeline; `ArctisAurora.EngineWork.Registry.AssetRegistries`;
`AVulkanMesh`; the Editor's import path.

## Three layers, usually collapsed by mistake

```
1. SOURCE       .fbx, .png, scene .xml          what the user edits
      | import (slow, once per change)
2. COOK CACHE   per-asset binary blob in the    keyed by hash(source) + importerVersion
                engine's exact vertex layout
      | pack (build step)  OR  read directly
3. RESIDENCY    suballocator -> GPU ranges      identical in editor and shipped game
```

Layer 3 is the **same code** either way. Editor and game differ only in where layer 2's bytes come
from:

- **Editor** — cook on import, cache to disk, re-cook the single asset whose hash changed. (Unity's
  `Library/`, Unreal's DDC.)
- **Shipped** — the packer concatenates cache entries *in load order* into the pak, manifest
  recording where each landed.

**So the pak is just a pre-concatenated, pre-ordered cook cache.** That is the unification: nothing
above layer 2 knows which one it is talking to.

## Decisions

### 1. Two maps, not one — and only one of them is baked

**Baked, ships with the game:** the pack layout. One binary blob of vertex/index bytes plus a
manifest of per-mesh records — `{ assetId, byteOffset, vertexCount, indexCount, stride, bounds,
materialId }`. Deterministic, computed once by the build tool.

**Assigned at runtime:** the **GPU** offsets. These cannot be baked, because what is resident
depends on what is actually loaded. Baking absolute GPU offsets hard-codes "everything is always
loaded" and makes streaming and unloading impossible forever.

The mesh record carries counts and *file* position. The suballocator hands out
`vertexOffset`/`firstIndex` at upload; those live in the runtime handle (`AVulkanMesh`).

**The exception, considered and not taken:** if the whole world fits in VRAM and never streams,
absolute GPU offsets *can* be baked and loading becomes a single memcpy of the entire blob. Legitimate,
and arguably right for Periodic and the Editor, which stream nothing. Rejected anyway — baking
relative offsets and assigning at load is roughly fifty lines more and does not paint us in.

### 2. One interface over both sources

```
IAssetSource.TryOpen(AssetId) -> (MeshHeader header, ReadOnlySpan<byte> payload)
```

- `LooseFileSource` — cooks on miss, watches timestamps. **Write this one first.**
- `PackSource` — seeks into an mmap'd blob. Roughly a day's work, changes nothing above it.

Both hand the identical thing to the residency layer's `Upload(header, payload)`, which
suballocates and returns a handle. See [[engine-resource-manager]].

### 3. The editor's requirements drive the allocator design

The editor does something the shipped game never does: **replace an asset while it is referenced.**
Re-import a mesh mid-session and its GPU range must be freed and reallocated at a different size.

Two consequences, and the second is the one that is expensive to retrofit:

1. **Entities must never hold raw GPU offsets.** They hold an assetId; offsets live in the handle
   the residency layer owns. `AssetRegistries` indirection already is this — do not let a baked
   offset leak into an entity or a pool column.
2. **The suballocator needs real `Free` with coalescing, not a bump pointer.** A ship-only allocator
   can bump-allocate because nothing is ever unloaded. The editor makes that insufficient.

Build the bump version and it gets rewritten. This is the single reason the allocator must be
designed against editor requirements from day one.

Fragmentation from editor churn, when it bites: compact by copying live ranges into a fresh block
with `CmdCopyBuffer` on the transfer queue and patching handles — transparent precisely because of
point 1. **Do not build it yet; just do not make it impossible.**

### 4. Manifest is XML, payload is binary

Consistent with the CLAUDE.md XML rule. Vertex data obviously does not go in XML. The manifest is
really the on-disk form of a registry that is currently built by loading files individually.

## Build order

1. Residency layer + suballocator **with free support** — the shared piece, expensive to retrofit.
2. `IAssetSource` with only `LooseFileSource`. Cook in memory on load; no disk cache yet.
3. Disk cook cache — when import time actually hurts.
4. Packer + `PackSource` — when something first ships.

3 and 4 are purely additive. **1 is where getting it wrong costs.**

## Why this shape at all — the mesh-binding payoff

Baking meshes into one blob in load order means all vertex data lands in **one buffer** at different
offsets. `CmdBindVertexBuffers` then happens once for the whole scene and meshes differ only by
`firstIndex`/`vertexOffset` — which is also exactly the shape `CmdDrawIndexedIndirect` needs later.
The bake is not only an I/O optimisation; it is what makes the draw path collapse.

Related: [[engine-resource-manager]], [[world-streaming-prefetch]]
