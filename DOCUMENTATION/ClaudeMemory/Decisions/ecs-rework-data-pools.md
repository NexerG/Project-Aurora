# ECS Rework — Data-Oriented Pools (PARTIALLY IMPLEMENTED)

Status: design settled 2026-07-17; first slice implemented 2026-07-21.

## Implemented so far (2026-07-21)
- **Folder rename** `ParticleSimulator` → `AuroraEngine` (assembly/root namespace pinned to `ArctisAurora` via csproj). See [[project-map]].
- **Core pool** in `AuroraEngine/Core/Data/`: `DataHandle` (poolId, stableId, version), `IPoolColumn`/`PoolColumn<T>` (type-erased column over `T[]`), `DataPool` (slots/backMap/versions indirection, ordered compaction + unordered swap-remove, growth, resequence, `FrameEdge`, content/structural dirty flags), `DataManager` (static, `Pools.xml` parse at bootstrap step `DataManager.ParseXML`), `TransformData` struct, `PoolDefinition`/`PoolManifest` XSD carriers.
- **Pools.xml**: `UIControls` pool = TransformData + ControlData, Capacity 1024, Ordered, SortAction `UI.DFSOrder`, Growth Multiplicative x2.
- **Control transform cutover (DONE, "rewrite call sites directly"):** every `VulkanControl` allocates a pool handle in ctor (`ControlPool.Allocate(this)`); all Arrange overrides write via shared `WriteArrangedTransform(finalRect)` helper; reads (UICollisionHandling hit-test `TransformToWorld(TransformData)`, ResizeableControl drag/resize/cursor, ShortTextControl glyph pos, UILayout root rect, ParseXML window seed) go through `VulkanControl.PoolTransform` (ref into pool). Renderer `MCUI` (3 matrix-build sites) reads `controls[i].PoolTransform`, preserving Controls-group order (no DFS yet). NO automated tests (user tests manually — see auto-memory).

## NOT yet done (remaining Phase 2/3)
- `controlData` still per-control field + own SSBO; pool's ControlData column allocated but UNUSED.
- `DataManager.FrameEdge()` NOT yet called from `Engine.Interpolate()` — so destroy-drain / compaction / resequence don't run yet; controls never call `Free`; `orderDirty`/`UI.DFSOrder` sort provider not wired.
- Dead dirty path (contentDirty → `renderer.UpdateModules()`) NOT revived — `MarkContentDirty()` is called but unconsumed; MCUI still re-uploads only on Controls-group add/remove or swapchain (pre-existing bug: pure move/resize doesn't update same-frame).
- Schema location issue (engine XML docs reference empty `AuroraEngine/Data/XML/Schemas`; XSDGenerator writes schemas to the running app's folder) — pre-existing, DEFERRED, user aware.

## Original design (settled 2026-07-17) — unchanged below

## Agreed direction
- Entity = handle `(pool/manager id?, index, version)` — NOT a class hierarchy. Version counter invalidates stale handles after swap-remove.
- Data lives in arrays of unmanaged structs (`TransformData`, `ControlData`, ...). Array of structs = one object, contiguous; GC skips interiors.
- Behaviour lives in systems (loops over arrays), NOT per-entity virtual `OnTick`. Inheritance moves to pool/manager level (class-level polymorphism, ~once-per-pool virtual calls), not per entity.
- Conditional per-entity logic → (a) enum/flag field + switch in loop (most entities), (b) membership side-list, e.g. active-animations list (few entities), (c) script-ref escape hatch (rare one-offs).
- Variable-length data (children etc.) can't live inline in structs → `(start,count)` ranges into shared arrays, or stays in classes.
- XML/XSD-driven pool composition: XML declares pools from existing C# component structs (`Array.CreateInstance(type, capacity)` gives real contiguous `T[]`); XML never defines struct layouts. Resolve types via `AnyXMLType.FindType`. Systems bind by pool name (shape-queries deferred).
- Classes stay as proxies where useful (UI `VulkanControl` tree stays OO); proxy properties read/write array slots via handle. Systems never iterate through proxies.

## User's current sketch (latest turn)
- Single manager governs all struct arrays; capacity is a setting.
- Objects (controls) access their data via properties/fields proxying into arrays.
- GPU-associated data (transforms, control data) moves out first; CPU-side styling/scaling/settings also move out.
- Open: how to keep C# attributes (`[@Serializable]`, `[A_XSDElementProperty]`) working when data moves to structs — proxy-property attributes vs attributing struct fields.

## C# constraints that shaped this (don't re-litigate)
- Classes: no placement control, no fixed stride, GC moves instances; array-of-class = array of pointers. Cannot embed class instances in an array block.
- Structs: no inheritance; interface-typed use boxes — use `where T : struct, IFoo` generic constraint for no-box dispatch.
- Copy semantics: `List<T>` indexer returns copies (use `T[]`/Span + `ref`); `foreach` copies unless `ref var` over span.
- Cannot store `ref`/`Span` in fields — handles must be re-resolved each use (this forces the handle pattern).
- Managed refs inside structs poison pools (GC scan, no pinning) — use uint/EntityId handles, keep structs unmanaged.
- LOH arrays (>85KB) don't move in practice; `GC.AllocateArray(pinned:true)` for GPU interop.
- Delegates allocate + indirect — banned in hot loops. Reflection `SetValue` on struct array elements boxes — acceptable at load time only.

## Locked decisions (2026-07-18)
- Single static manager owning named per-category arrays (world data, UI, ...); growth = realloc bigger array; supports segmenting hot arrays into blocks (XML-tunable), blocks invisible above the manager.
- Growth/repack ONLY between frames; systems re-fetch spans at tick start, never cache spans across frames.
- Handles use indirection table (sparse-set): handle = (stableId, version) → slots table → dense slot. Repack/swap-remove patches table only; handles survive repack. Version bumps only on destroy.
- GPU data = both: CPU authoring structs (pos/rot/scale) + pack pass baking into pinned GPU buffer for dirty entries.
- UI hierarchy (parent/children) STAYS in class graph (OO tree) — measured as negligible at UI scale. But ControlData/settings move to arrays; layout system iterates arrays flat ("GO FAST"). Depth-sorted repack can make layout a forward flat loop.
- Fix separately: isDirty setter cascades subtree on every set — defer to one propagation pass per tick.
- Destroy: control calls Free → enqueues to destroy queue → manager drains between frames (version bump at drain). Same pattern as existing onDestroyEntities.
- Attributes for serialization/XSD stay on class proxy properties (getter/setter round-trips through array slot); serializer unchanged; structs stay attribute-free.

## Removal policy per pool (locked 2026-07-18)
- Pools declare in XML: `Unordered` (swap-remove; particles, world statics) vs `Ordered` (UI — array order IS render/painter order, "data id == rendered id").
- Ordered removal = batch compaction: mark dead at destroy-queue drain, ONE forward sweep (write cursor trails read cursor), patch slots table via back-map. O(n) per frame-with-deaths, order preserved. Never per-element shift.
- Canonical UI order = DFS pre-order of the OO control tree (parent before children = painter order AND layout dependency order — same order serves both).
- Insert/reparent/sibling-reorder → set pool `orderDirty` → between frames DFS-walk class tree, permute arrays, patch table. Destroys alone don't dirty order.
- "Bring to front" = move subtree to end of parent's children list + orderDirty. Z-management is just tree list order.
- Hook controls (runtime insertion points, e.g. scoreboard): hook = plain (invisible) tree node; insert under hook = tree insert + orderDirty → covered by the same DFS re-sequence. No array insert-shifting ever. N inserts in one frame = still one repack (flag, not queue).
- Later-lever ONLY if profiling demands (per-frame churn hooks, e.g. chat log): per-hook pre-reserved slack range in the sequence (gap buffer) — inserts fill inactive slots in place, no permute, dirty-subset upload; repack when gap exhausts. XML-tunable. NOT in v1.
- Frame-edge drain order: destroys → compact → growth → DFS re-sequence if orderDirty → GPU upload marks. Reorder/compact frames = full re-upload of that pool's GPU mirror (fine at UI sizes).

## Open questions
- Repack triggers for non-UI pools: explicit developer call vs XML policy (occupancy/sortedness heuristic)?
- Repack ↔ GPU buffer interplay for BIG pools (100k+): reorder forces full re-upload — keep repack deliberate/explicit there.
