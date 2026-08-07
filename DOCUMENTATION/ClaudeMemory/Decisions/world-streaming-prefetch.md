# Decision — streaming predicts by reachability, not just visibility; runtime grid over baked PVS

**Date:** 2026-08-03
**Status:** DESIGN ONLY — nothing here is implemented. Furthest-out of the three notes from this
session; revisit before building, none of it is urgent.
**Scope:** future world streaming / prefetch; navmesh; level cell structure.

## Feedback latency is not what causes pop-in

The reflex is to attack the 2-frame GPU→CPU readback delay. Wrong target — look at the whole chain:

| Stage | Cost |
|-------|------|
| feedback readback | 2 frames, ~33 ms |
| CPU decision | 1 frame, ~16 ms |
| **disk read** | **5–200 ms** |
| decompress + upload | 1–2 frames, ~33 ms |

Readback is ~10–15% of the total. Eliminating it entirely still pops. **Nobody in the industry
removes this latency; they all hide it.** Every technique below is a variant of "always have
something acceptable to draw, improve it asynchronously."

## Decisions

### 1. Always-resident coarse LOD — highest value, lowest effort

Keep the lowest LOD of *everything* permanently resident. A few hundred triangles per mesh,
single-digit MB for a whole level. Pop-in stops being "object appears" and becomes "detail
sharpens", which the eye largely forgives.

Virtual texturing does this with a fallback mip; Nanite keeps the cluster hierarchy root resident.
**If only one thing gets built, build this.**

### 2. Visibility and reachability are different sets

| | Answers | Needs loaded |
|---|---|---|
| **Visible** | what can I see from here | full-detail render assets |
| **Reachable** | where can I *be* in N frames | collision, gameplay, about-to-be-visible geometry |

A mountain 5 km off is visible and never reachable. The room behind you is invisible and imminently
needed. Classic PVS only ever addressed the first column. **Reachability is the half worth adding.**

### 3. Reachability is geodesic, not Euclidean

A wall 1 m ahead is unreachable; a straight corridor 50 m long is reachable at a sprint. So flood
the **nav graph**, not the voxel grid:

```
budget = max_speed * time_window
reachable = budgeted BFS over nav-cell adjacency from the player's cell
```

Cell adjacency + traversal cost precomputed offline; runtime is a budgeted BFS. Cheap, and it
correctly refuses to load through walls. The navmesh is needed for AI anyway, so the cost is shared.

### 4. Runtime sparse grid, not baked PVS — because of the Editor

Prior art: baked PVS (Quake, early Unreal) → designer-placed streaming volumes (UE3/4) → **World
Partition** (UE5): a grid of cells with loading ranges, hierarchical, HLOD proxies for distant cells.
UE5 moved *away* from baked visibility. Not because baking is less precise — it is more precise.

**Because baked PVS is hostile to editors.** Every level edit invalidates the bake. If moving a wall
means waiting on a visibility rebuild before streaming behaves correctly, designers stop trusting it.

Since the Editor comes first here, the ordering is:

- **runtime sparse grid** (cells with content only), distance + frustum driven — no bake, edits instant;
- **navmesh-geodesic reachability** layered on top;
- **baked PVS later, optional, per-level** — a ship-time optimisation for levels that stopped changing.

Sparse also matters for size: a dense voxel grid over a large world is mostly empty, and per-cell
visibility bitsets grow O(cells²). Sparse + hierarchy is what keeps it tractable.

### 5. Feed the same slot table

A cell's `PVS ∪ Reachable` bitset is just another producer of "these assetIds should be resident."
Runtime becomes: find my cell, OR the bitsets, diff against resident. Near-free, and it plugs into
the residency slot table from [[engine-resource-manager]] with no new mechanism.

## Also considered, lower priority

- **Dilate the request set** — off-screen frustum margin, one LOD finer than needed, hierarchy
  neighbours. Cheap; converts a sharp miss into an already-warm hit.
- **Camera velocity extrapolation** — request for where the camera will be in ~200 ms.
- **Kill disk latency** — the dominant term. GPU decompression takes the CPU out of disk→VRAM
  (DirectStorage on D3D12; on Vulkan, a compute shader or `VK_NV_memory_decompression`). Combined
  with packing in load order ([[asset-pipeline-bake]]), this is where the real milliseconds are.
- **Let the GPU skip, not wait** — the only item that addresses the readback roundtrip directly.
  Since a compute pass writes the indirect args, it can check residency and emit the fallback LOD
  for anything not yet resident, same frame, no CPU involvement; the real asset swaps in silently
  when it lands. Works by making the latency *not matter* rather than removing it. With #1, the
  transition is essentially invisible.

## Build order

#1 and dilation are small and get most of the way. #2/#3 matter once levels have real occlusion.
Disk decompression and GPU-side skipping are late-stage, and neither changes the architecture in
[[engine-resource-manager]] — the residency slot table and handle indirection support all of it.

## Research leads — UNVERIFIED, confirm before citing

Flagged as such deliberately: these came from recall, not from a search, and exact years/subtitles
were not checked.

- **Cohen-Or, Chrysanthou, Silva & Durand — "A Survey of Visibility for Walkthrough Applications"**
  (IEEE TVCG, ~2003). The canonical map of the space; best single entry point.
- **Teller & Séquin — "Visibility Preprocessing for Interactive Walkthroughs"** (SIGGRAPH '91).
  Cell-and-portal PVS; origin of what Quake/Unreal shipped. Teller's Berkeley thesis expands it.
- Airey/Rohlf/Brooks (1990) — cells and portals, contemporaneous.
- Luebke & Georges (1995) — dynamic portal culling, no bake.
- Durand et al. (SIGGRAPH 2000) — conservative from-region visibility with occluder fusion.
- Bittner / Mattausch / Wimmer — coherent hierarchical culling (CHC, CHC++); the occlusion-query
  runtime alternative to baking.
- **Prefetching specifically** (thinner): Funkhouser, mid-90s, on database management and prefetch
  for architectural walkthroughs; Correa/Klosowski/Silva on visibility-based prefetch for out-of-core
  rendering (the iWalk line, early 2000s). Parallel body in networked-VE **area-of-interest
  management** — same problem, network latency instead of disk.
- **Navmesh-reachability-driven prefetch — no known paper.** Navmesh research (van Toll/Geraerts,
  Oliva/Pelechano; Recast on the implementation side) treats navmeshes as a pathfinding structure;
  streaming literature uses visibility as the predictor. The crossover looks under-explored, though
  it may equally be unpublished industry practice.
- **Modern work is not in journals.** SIGGRAPH's *Advances in Real-Time Rendering in Games* course
  is the venue — the Nanite deep-dive (Karis et al., 2021) is there and is directly relevant to
  hierarchical residency. Virtual texturing lives in GDC talks (Barrett on sparse virtual textures,
  van Waveren) rather than papers.

Related: [[engine-resource-manager]], [[asset-pipeline-bake]]
