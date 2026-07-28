# ECS Rework — Data-Oriented Pools (PARTIALLY IMPLEMENTED)

Status: design settled 2026-07-17; slices implemented 2026-07-21 (pools + pooled UI mirror),
2026-07-22 (GpuTransform column + frame-edge destroy lifecycle), 2026-07-27 (command lanes,
writer-side matrix bake, renderer polls the pool) and 2026-07-28 (tree mutation drives the pool —
compaction and resequence now actually run).

## Implemented so far (2026-07-21)
- **Folder rename** `ParticleSimulator` → `AuroraEngine` (assembly/root namespace pinned to `ArctisAurora` via csproj). See [[project-map]].
- **Core pool** in `AuroraEngine/Core/Data/`: `DataHandle` (poolId, stableId, version), `IPoolColumn`/`PoolColumn<T>` (type-erased column over `T[]`), `DataPool` (slots/backMap/versions indirection, ordered compaction + unordered swap-remove, growth, resequence, `FrameEdge`, content/structural dirty flags), `DataManager` (static, `Pools.xml` parse at bootstrap step `DataManager.ParseXML`), `TransformData` struct, `PoolDefinition`/`PoolManifest` XSD carriers.
- **Pools.xml**: `UIControls` pool = TransformData + ControlData, Capacity 1024, Ordered, SortAction `UI.DFSOrder`, Growth Multiplicative x2.
- **Control transform cutover (DONE, "rewrite call sites directly"):** every `VulkanControl` allocates a pool handle in ctor (`ControlPool.Allocate(this)`); all Arrange overrides write via shared `WriteArrangedTransform(finalRect)` helper; reads (UICollisionHandling hit-test `TransformToWorld(TransformData)`, ResizeableControl drag/resize/cursor, ShortTextControl glyph pos, UILayout root rect, ParseXML window seed) go through `VulkanControl.PoolTransform` (ref into pool). Renderer `MCUI` (3 matrix-build sites) reads `controls[i].PoolTransform`, preserving Controls-group order (no DFS yet). NO automated tests (user tests manually — see auto-memory).

## Implemented so far (2026-07-21, second slice — pooled UI GPU mirror)
- **MCUI transforms are pool-backed + persistent (DONE).** `MCUI.MakeInstanced` now reads
  `ControlPool.GetSpan<TransformData>()` (dense column) and bakes translate*scale into a
  persistent transforms SSBO sized to `pool.Capacity`. Ordinary adds re-bake the live range and
  patch it in place via new helper `AVulkanBufferHandler.UpdateBufferRange<T>(data, srcStart,
  dstStart, count, ...)` (CmdCopyBuffer with dstOffset) — NO teardown/recreate. The buffer is
  destroyed+recreated (DeviceWaitIdle) only when the pool grows (capacity change). Killed the old
  count-change branch that recreated the whole buffer every frame. `BakeMatrices` reuses a
  capacity-sized `_matrixScratch`. MCUI's dead `SingletonMatrix`/`UpdateMatrices` overrides left
  untouched (UI path never calls them).
- **UIModule descriptor sets are persistent + incrementally appended (DONE).** Descriptor pool +
  the two sets are allocated ONCE per swapchain image at `pool.Capacity` (variable count =
  capacity, `PartiallyBoundBit`), then only the newly added controls' descriptors are written:
  per image, append `[writtenCount, live)` to set0/b2 (control-data) and set1/b0 (samplers) via
  `WriteControlDataDescriptors`/`WriteSamplerDescriptors` (DstArrayElement = from). Camera UBO +
  transforms SSBO bound once in `WriteStaticDescriptors`. Full rebuild (`UpdateDescriptorSets`)
  only when `_frameBuiltCapacity[img] != pool.Capacity` (grow) or count shrank. Per-image state:
  `_frameBuiltCapacity[]`, `_frameWrittenControls[]`. **No more per-frame descriptor-pool
  recreation while a key is held** — that was the original perf bug. `controlCount` static removed;
  pool sizes/variable count now sized to `ControlPool.Capacity`.
- **WriteCommandBuffers** now allocates the command-buffer array once but records only the current
  image (was: record all images on first call). Each image records itself on its first dirty pass
  (all start `isDirty=true`), by which point its sets exist — required by the per-frame-lazy build.
- Descriptor per-control data sourced via `ControlPool.OwnerAt(dense)` (cast to `VulkanControl`),
  so it lines up with the transform mirror. **Assumes pool dense order == Controls-group order ==
  render order** — true while append-only (no `Free`, no DFS resequence yet). Revisit when those land.
- Scope note: transforms + descriptor persistence only. ControlData NOT folded into the pool this
  pass (would need UI.vert set0/b2 to change from array-of-buffers to a single indexed SSBO +
  .spv recompile) — deferred by user decision.
- Pre-existing coarse race (main-thread `Arrange` writes pooled transforms while render-thread
  `Draw`/`MakeInstanced` reads them) is UNCHANGED — not made worse; that's why the transform
  content upload re-bakes the live range each dirty pass instead of using a cross-thread
  dirty-range clear.

## Implemented so far (2026-07-22, third slice — GpuTransform column + frame-edge lifecycle)
- **Baked matrix moved into a pool column (DONE).** New `GpuTransform` struct (blittable
  `Matrix4X4<float>` wrapper, `[A_XSDType("GpuTransform","DataPools")]`) is now a third column on
  the `UIControls` pool (`Pools.xml`). `MCUI.BakeMatrices` writes translate*scale into
  `pool.GetSpan<GpuTransform>()`; `MakeInstanced` mirrors `pool.Backing<GpuTransform>()` (new
  `DataPool.Backing<T>()` = full capacity-length column array) to the persistent SSBO — recreate
  on capacity change, `UpdateBufferRange` on the live range otherwise. Killed the hand-managed
  `_matrixScratch` parallel array. The column now rides along through compaction/resequence
  (columns move together in `MoveDense`/`Permute`), so it stays dense-aligned automatically.
- **`DataManager.FrameEdge()` wired into the tick (DONE).** Called in `Engine.Run()` AFTER
  `t_render_end.WaitOne()` and BEFORE `t_render_start.Set()` — the only window where the render
  thread is parked, so compaction/resequence can move pool memory safely. NOT inside
  `Interpolate()` (that runs concurrently with the previous frame's `Draw()`).
- **Deferred destroy lifecycle (DONE, destroy half only).** `Entity.Destroy()` detaches the
  subtree root from the live tree and enqueues the whole subtree to `EntityRegistry`'s
  `_toDestroy`. `EntityRegistry.ProcessDestroys()` drains it at the TOP of `Interpolate()`
  (before the OnTick loop, so no mid-iteration list mutation): `Unregister` (removes from all
  matching groups incl. "Controls" → fires onChanged → UI module marks dirty) → `OnDestroy()` →
  `pool.Free(handle)` (deferred). The actual slot compaction happens at that tick's later
  `FrameEdge()`. Ordered pool → `CompactOrdered` preserves DFS order, so the renderer needs no
  new path: compaction shrinks `live`, hitting UIModule's existing `live < writtenCount`
  structural-rebuild guard. `Destroy()` is idempotent (`_destroyed` flag) and `Pool.Free` is too.
- **`UI.DFSOrder` sort provider registered (DONE, dormant).** `UILayout.DFSOrder(DataPool)`
  (`[A_XSDActionDependency("UI.DFSOrder","PoolSort")]`) = DFS pre-order of the control tree,
  returning live stableIds. Resolves the previously-dangling `Pools.xml SortAction` (was logging
  "not found" at bootstrap). Roots = live controls whose parent is not a VulkanControl, DFS'd in
  dense-scan order. **Consumed only when something marks the pool `orderDirty` — nothing does yet.**

## Implemented so far (2026-07-27, fourth slice — versioning + command lanes)
Design rationale lives in [[cross-system-change-notification]]; this is the code inventory.
- **`DataPool` generation versioning (DONE).** `ContentVersion` / `StructuralVersion` (ulong,
  volatile), bumped by new private `PublishGeneration()` at the END of `FrameEdge()` — never per
  write. Content changes accumulate into `_dirtyMin/_dirtyMax` as before, then get pushed into a
  16-entry `_dirtyLog` ring keyed by generation; `TryGetDirtyRange(since, out min, out max, out
  current)` unions the generations a consumer missed and falls back to the whole live range if it
  is >16 behind. `_publishedSlotVersion` (int[capacity], slot version or 0 for free) is rebuilt and
  republished on structural generations only; immutable once published.
- **`PoolCursor` (NEW, `Core/Data/PoolCursor.cs`).** One per consumer per pool, single-threaded, no
  internal sync. `TryConsumeContent(out min, out max)` → dense dirty range. `TryConsumeStructural()`
  → `Created` / `Destroyed` stableId lists by diffing `_provisioned` against the published slot
  versions. `Reset()` forces a full rebuild (swapchain recreation / device loss).
- **Column ids (DONE).** `DataPool` now keeps `_columnsByIndex` + `_columnIds` assigned from
  `Pools.xml` `<Component>` declaration order, exposed via `ColumnId<T>()` / `ColumnId(Type)` /
  `ColumnAt(ushort)` — this is what resolves `SystemCommand.ColumnId`. **Reordering `<Component>`
  elements silently reinterprets every in-flight command.** Also added `DenseOf(handle)` (-1 when
  stale) and `IPoolColumn.ElementSize` / `WriteBytes` / `FillBytes` / `CopyWithin` for type-erased
  command apply.
- **`CommandArena` reworked into a byte RING (was a bump allocator + `Reset()`).** Power-of-two
  capacity, monotonic `_writeSeq` with published write/read cursors, `TryWrite` (returns false when
  full, no longer throws), `Commit()` / `ReleaseTo(seq)`, records pad to the boundary rather than
  straddling. The old `Reset()` was only correct while a producer waited for its drain — nothing
  waits any more, so a producer routinely runs several ticks ahead. **API changed; nothing consumed
  it yet.**
- **`CommandLane` (NEW, `Core/Data/Commands/CommandLane.cs`).** SPSC ring of `SystemCommand` + its
  arena, one lane per ORDERED (producer, owner) pair — not one inbox per owner, so every ring has
  exactly one writer and needs no CAS. `BeginDrain`/`At(seq)`/`EndDrain`; `Commit()` publishes the
  arena BEFORE the ring so a visible command's payload is always already visible.
- **`CommandApplier` (NEW).** Per-command switch back onto the owning pool. Drops stale commands via
  `DenseOf` (version check), bounds-checks range ops, marks the dirty range. `Allocate` is
  fire-and-forget (no return path for the handle — noted in-file).
- **`ThreadedSystem.Drain()` / `Publish()` are real.** Each system holds `_inbox` / `_outbox`
  arrays of lanes indexed by the other system's id (own-id slot is null — a system writes its own
  tables in place). `BuildLanes()` wires all ordered pairs, called from `Engine.Init()` right after
  `DataManager.ResolveOwners()` and before any `Start()`. `Send<T>(handle, columnId, value)`
  enqueues a `SetOne`; returns false on backpressure rather than blocking or dropping silently.
- **Scope note: infrastructure only — NO consumer was rewired.** UIModule still uses its ad-hoc
  `_frameWrittenControls[]` / `_frameBuiltCapacity[]` high-water marks (which are dense-space and
  will break when compaction actually runs), and the lanes have **no producer**: physics is a stub
  and `MCUI` writes `GpuTransform` in place rather than through a command. Builds clean, behaviour
  unchanged. Next slice = move UIModule onto `PoolCursor`.

## Implemented so far (2026-07-27, fifth slice — matrix bake moved to the writer)
- **`MCUI.BakeMatrices` DELETED.** The translate*scale bake now happens at the write, in new
  `VulkanControl.CommitTransform()`: bake `TransformData` → `GpuTransform` row, then
  `MarkContentDirty`. `MakeInstanced` just mirrors `pool.Backing<GpuTransform>()` to the SSBO.
- **Why:** the bake was a pure per-row function with no parent chain, but ran on the RENDER thread
  over the WHOLE live range every dirty pass — move one window, re-derive all 1024 rows. Moving it
  to the writer costs one matrix per actual change, and removes the render thread's write to (and
  read of) a pool `Pools.xml` says `System="Main"` owns. `GpuTransform` is now read-only to render.
- **Call sites (3, not 9 — `WriteArrangedTransform` is shared by every `Arrange` override):**
  `VulkanControl.WriteArrangedTransform`, `VulkanControl.ParseXML` window seed,
  `ResizeableControl` drag/resize. All three previously called `MarkContentDirty` directly.
- **Plus `VulkanControl` ctor calls `CommitTransform()`** to seed the matrix from the base ctor's
  default `scale = (1,1,1)`. REGRESSION FOUND AND FIXED during this slice: `Entity`'s
  `AllocatePooledTransform` cannot bake (base class; the `Entities` pool has no `GpuTransform`
  column), and the old render-side sweep used to cover freshly allocated rows implicitly — without
  the ctor bake a control reaching the GPU before its first `Arrange` uploads a zero matrix.
- **INVARIANT: every write to a control's transform must end in `CommitTransform()`.** Convention
  only — nothing enforces it. A site that writes `transform` and skips it leaves a stale matrix
  silently. `Entity.SetPosition/SetScale/SetRotation/SetTransform` mark dirty WITHOUT baking and
  would do exactly that on a control; they currently have **zero callers repo-wide**, left alone as
  pre-existing, but they are the obvious trap.
- Does NOT by itself fix "pure move/resize doesn't update same-frame" — the matrix is correct in
  the column immediately, but `MakeInstanced` still only runs on Controls-group add/remove, so
  nothing uploads it. That needs the content cursor driving the upload (next slice); this slice is
  its prerequisite, since range-limiting the upload saves nothing while the renderer re-bakes
  everything anyway.

## Implemented so far (2026-07-27, sixth slice — renderer polls the pool)
The renderer now discovers pool changes itself instead of being told via a pushed flag.
- **`DataPool.OrderVersion` (NEW), separate from `StructuralVersion`.** Bumped ONLY when dense
  indices move — compaction and resequence. Structural covers the live SET changing (alloc/free).
  Keeping them apart is what lets a plain add still take the cheap descriptor-append path; folding
  them together would force a full rebuild every time a control is created, which is the common case.
- **`PoolCursor.TryConsumeStructural()` now returns true on ANY structural version move**, not only
  when Created/Destroyed are non-empty. A resequence permutes dense indices while the live set stays
  identical, so the slot-version diff finds nothing — under the old return it was invisible. Also
  added `OrderChanged` (dense indices moved) and `HasPending` (non-consuming peek, 3 volatile reads).
- **`MCUI.MakeInstanced(module, frame, dirtyMin, dirtyMax)`** — signature changed; uploads only the
  reported dense range via `UpdateBufferRange` instead of `[0, live)` every pass. Capacity change
  still recreates the buffer from the whole column and returns early (range ignored). Range is
  clamped to `live` since rows past Count are slack.
  **`MeshComponent.MakeInstanced(RenderingModule, int)` base virtual DELETED** — MCUI was its only
  override, and leaving it would make a stale 2-arg call silently hit an empty base method.
- **`UIModule` holds one `PoolCursor` per swapchain image**, built lazily (pools parse in the same
  bootstrap phase that constructs the module and intra-phase order is undefined). One per image, not
  per module: each image provisions its own descriptors and advances independently.
  `_frameWrittenControls` stays a DENSE high-water mark but is now guarded by `cursor.OrderChanged`
  forcing a full rebuild — that closes the documented "pure permute takes the append path and leaves
  stale descriptor→control mappings" bug.
- **`RenderingModule.HasPendingWork(frame)` (NEW virtual, default false)**, overridden by UIModule to
  `Cursor(frame).HasPending`. `Renderer.Draw()` now calls `UpdateModule` when
  `isDirty[img] || HasPendingWork(img)`. `isDirty` remains for invalidation from OUTSIDE the module
  (swapchain rebuild, window resize); pool changes are polled. Note `Renderer.UpdateModules()` (the
  old push path) has no live callers — its only call site in `Engine.Interpolate` is inside a
  commented-out block. Pre-existing dead code, left alone.
- **STILL NOT FIXED: the coarse main-writes/render-reads race.** `CommitTransform` (main) writes
  `GpuTransform` rows while `UpdateBufferRange`'s `CopyTo` (render) reads the same array — torn
  matrices remain possible. Narrowed, not closed: the copy is now the dirty range instead of the
  whole live range. The real fix is double-buffered columns (see
  [[cross-system-change-notification]] "rejected/deferred"), deliberately not smuggled into this slice.
- **`Destroyed` is consumed but not yet acted on.** The per-control `controlDataBuffer` cannot be
  reclaimed from the cursor: by the time the pool reports the slot dead, compaction has already
  removed the row, so `OwnerAt` can no longer hand back the control that owned the buffer. Deferred
  deletion has to be *initiated by the control at destroy time* (push the handle into a deletion
  queue), not discovered by the renderer afterwards. This kills the "the cursor gives you the
  deferred-delete hook" idea recorded in the previous slice.

## Implemented so far (2026-07-28, seventh slice — tree mutation drives the pool)
The ordered-pool machinery written in slice 3 (`CompactOrdered`, `Resequence`, `UI.DFSOrder`) had
never executed. This slice gives it callers. Its prerequisite — a permute forcing a FULL descriptor
rebuild — landed in slice 6 as `PoolCursor.OrderChanged`.
- **`VulkanControl.MarkTreeOrderDirty()` (NEW)**, called from every control `AddChild` override
  (`VulkanControl`, `AbstractContainerControl`, `GridListControl`, `ScrollableControl`,
  `TextControl`) and from `TextControl.SyncGlyphs`/`RebuildGlyphs` when they mutate `children`.
  A new row is appended at the dense TAIL but belongs at its parent's DFS position, so any insert
  other than at the DFS tail desynchronises dense order from paint order. Deliberately NOT
  distinguishing the tail case: it is a flag, not a queue, so N inserts in a tick cost one permute.
  **Removal deliberately does NOT dirty order** — `CompactOrdered` preserves the survivors'
  relative order, so a destroy never needs a permute (it bumps `OrderVersion`, not `_orderDirty`).
- **Glyphs are now destroyed, not just detached.** `TextControl` used to drop glyphs out of
  `children` (`RemoveRange`, `children[i] = replacement`, `Clear`) with a `// TODO` where the
  cleanup should be. New `DiscardGlyph(entity)` clears `parent` FIRST (otherwise `Destroy()`
  removes the glyph from `children` itself, shifting the list under the loop editing it) and then
  `Destroy()`s. Leaking these leaked twice: the slot was never freed, AND the glyph stayed live in
  the pool but unreachable from the tree — the one case `UI.DFSOrder` cannot produce an order for.
- **`TextInputControl` leaked a whole control per keystroke.** `TextControl.InsertGlyph` built a
  `GlyphControl`, parented it, and never added it to `children`; the `text` setter then ran
  `SyncGlyphs`, which built the real one. The orphan kept a pool slot, an SSBO and a "Controls"
  group entry (so it was still being drawn) with nothing able to reach it. The three
  `InsertGlyph`/`RemoveGlyph`/`RemoveGlyphRange` methods now only edit the string; `SyncGlyphs`
  owns glyph lifetime outright.
- **`Resequence()` returns bool; a refused resequence keeps `_orderDirty`.** It bails when the sort
  provider's order count != live count (a live row it cannot reach would be dropped from the
  permutation). The old code cleared the flag regardless, so one bad frame left dense order
  permanently disagreeing with the tree.
- **`UIModule.RetireControlBuffer` (NEW) + `_retiredControlBuffers` (`ConcurrentQueue`).**
  `VulkanControl.OnDestroy` pushes its `controlDataBuffer`/memory here and zeroes the fields;
  `UpdateModule` moves the queue onto THAT image's existing `deferredDeletions`, which frees it on
  the image's next visit. Handing it to ONE image rather than all of them is what makes it a
  handover instead of a double free, and one visit of that image is a full swapchain cycle — by
  then every other image has had its fence waited on and rebuilt without the control. This closes
  the `controlDataBuffer`-leaks-on-destroy gap and is the "initiated by the control, not discovered
  by the renderer" hook [[cross-system-change-notification]] called for.
- **`DataPool.ReleaseOwnerSlack` (NEW).** Both removal paths now `Array.Clear` the `_owners` range
  they vacated. `MoveDense` copies the managed back-reference down WITHOUT clearing the source, so
  before this a compaction left a duplicate for every shifted survivor and the entity itself for
  every dead row — the destroyed entity stayed reachable from the pool, and so uncollectable, until
  a later `Allocate` happened to reuse that exact dense index. Bounded (capacity-worth at worst),
  not a growing leak, but it meant "destroyed" did not imply "collectable". Only `_owners` needs
  this; the component columns are unmanaged, so their slack is just numbers nobody reads. Nothing
  reads `OwnerAt` above `Count`, so clearing the slack cannot be observed by a consumer.
- **Verified by running Periodic** (no automated tests — see auto-memory). Boot: one resequence of
  5 rows, then quiet (flag clears, nothing re-dirties per frame). Click + type: 6 → 7 → 8 rows, one
  resequence per keystroke, glyph children 3 → 8, no mismatch warning. Destroy (temporary scene
  with `onClick="DestroySelf"`, source restored after): `CompactOrdered` ran ("1 dead of 2") and the
  retired buffer was adopted by image 2 — the retire fires from `ProcessDestroys` BEFORE the main
  thread reaches `FrameEdge`, so the renderer can adopt it first; both orderings are correct.

## NOT yet done (remaining Phase 2/3)
- **Reparenting has no API to hook.** `MarkTreeOrderDirty` covers inserts; there is no
  `SetParent`/`Reparent`/bring-to-front method in the tree today, so "move subtree to end of
  parent's children list + orderDirty" (locked design, below) is unimplemented. Any such method
  MUST call `MarkTreeOrderDirty` — nothing enforces it.
- **Text deletion is unreachable, so the glyph-removal path is only verified via `DestroySelf`.**
  `TextInputControl.Backspace()/DeleteAt()` have no callers and plain Backspace is unbound in
  `Periodic`'s `InputMap.xml` (Ctrl+Backspace → `ExitApplication`). `SyncGlyphs`' shrink branch is
  therefore correct-by-inspection but not exercised in the running app yet.
- **`Entity.SetPosition/SetScale/SetRotation/SetTransform` still bypass `CommitTransform`** — they
  mark dirty without baking, which on a control leaves a stale matrix. Still zero callers
  repo-wide (see fifth slice); the trap is unchanged.
- **`Entity.OnDestroy()` has a pre-existing modify-during-iteration bug** (`foreach _components`
  + `Remove`). Latent: controls carry no components so the loop body never runs. Left as-is.
  `VulkanControl.OnDestroy` now overrides it and calls `base` first.
- `controlData` still per-control field + own SSBO; pool's ControlData column allocated but UNUSED
  (per-control buffers are still bound into set0/b2 as an array; only their descriptor writes are
  now incremental). Folding into one pooled SSBO is the clean follow-up (needs the shader change)
  and would delete the retire path above entirely.
- **`DataManager.FrameEdge()` is still a flat loop over every pool from `MainTick`** — throws the
  day a pool is owned by Physics. Needs to become per-system. Cannot fire today (both pools Main).
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
