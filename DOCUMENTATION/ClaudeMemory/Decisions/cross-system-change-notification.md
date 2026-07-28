# Cross-System Change Notification — Poll, Don't Push (IMPLEMENTED 2026-07-27)

How a system learns that data another system owns has changed. Settled + infrastructure landed
2026-07-27. Complements [[ecs-rework-data-pools]] (which covers the storage) and the
`ThreadedSystem` decoupled-loop model.

## The question
With threads decoupled (no `AutoResetEvent` handshake), does a system that changes data notify
interested systems? Options weighed: direct pub/sub, a central message manager fanning out to
subscribers, or something else.

## Decision: no bus, no subscribers. Consumers poll a per-pool version.
- **Rejected pub/sub** — push routes at send time, so it **cannot coalesce**. 40 writes to one slot
  in a tick = 40 messages per subscriber. A dirty range folds them into one.
- **Rejected the central broker specifically** — it is a shared mutable structure on the
  cross-thread path, so every send needs sync and all systems serialise through one contention
  point. That undoes the reason `CommandArena` is per-producer. It is also nondeterministic
  (delivery order = scheduler order), which conflicts with the Phase B/F snapshot-netcode goal.
- **Chosen: version cursors.** Cost is O(pools) comparisons per tick instead of
  O(changes × subscribers) messages. No registry, no lifetimes, no unsubscribe.

## Key design points (do not "simplify" these away)
- **Versions count GENERATIONS, not writes.** A write only widens the open generation's dirty
  range; `DataPool.PublishGeneration()` (end of `FrameEdge`) closes it and bumps the counter with a
  release write. Per-write versions were considered and are wrong: 1000 dirty controls in one tick
  would burn 1000 versions and blow past any bounded history, forcing every consumer to full-range.
- **Per-consumer cursors, never a shared clearable flag.** The pre-existing
  `StructuralDirty`/`ClearDirty()` shape is single-consumer — whoever clears first robs everyone
  else. `PoolCursor` holds its own position instead. (`StructuralDirty`/`ClearDirty` are now
  superseded and still unconsumed; left in place, safe to delete when UIModule moves over.)
- **The published snapshot is slot-VERSIONS, not a bitset.** A bitset is 4x smaller and was the
  first design, but it cannot see ABA: free slot 5 and reallocate it between two polls and the bit
  reads 1 both times while the occupant is a different entity. Not a corner case — `_freeIds` is a
  LIFO `Stack<int>`, so a just-freed slot is the *first* one reissued. Carrying the version makes a
  recycle show up as destroy + create, which is what it is.
- **Read version, THEN snapshot.** The pool stores the snapshot then publishes the version with a
  release write. Reading the snapshot first would allow pairing an old snapshot with a new version
  and marking unseen changes consumed. `PoolCursor.TryConsumeStructural` comments this.
- **Snapshots are immutable once published** (fresh array per structural generation, ~capacity
  ints). No tearing, no locks, consumer may hold the reference across a whole scan. Only allocates
  on generations that changed shape. Swap to a ring of buffers if pools get big.
- **Dirty ranges are DENSE-space and only valid for the generation that published them.** Safe
  because any frame edge that moved dense indices also `MarkAllDirty()`, so a generation with
  movement is full-range by construction.
- **Resequence counts as structural** even though the live set is unchanged — a permute moves every
  dense index, so consumers holding dense-keyed resources must rebuild. This is the documented
  UIModule "pure permute takes the append path and leaves stale descriptor→control mappings" bug;
  the structural bump is its fix once UIModule consumes a cursor.

## Spawn/despawn contract for consumers (e.g. renderer creating GPU resources)
- **Iterate your OWN provisioned set, never the pool's live set.** A spawn racing a render tick is
  then structurally invisible — it draws one frame late instead of touching an unprovisioned slot.
  One frame of spawn latency is the price of not blocking; it is the right trade.
- **Create late is safe, destroy early is not.** `Destroyed` entries must go to the consumer's own
  deferred-delete queue (Vulkan frames in flight), not be freed immediately. This is the hook the
  known `controlDataBuffer`-leaks-on-destroy gap needs.
- **"Consumer" ≠ "system".** The renderer needs one `PoolCursor` per swapchain image; each
  provisions its own descriptors and advances independently.

## Rejected / deferred alternatives
- **XML-declared read/write dependency graph** (readers declared next to `System=` in `Pools.xml`,
  column-level). Genuinely valuable — derives the subscriber list instead of maintaining it, makes
  ownership assertable, makes the frame edge schedulable/topological, and makes ordering a property
  of the XML. **Deferred: not worth it at 3 systems / 2 pools.** Trigger to revisit: a 4th system,
  or the first cross-system race that takes >10 min to explain. See "steal this early" below.
- **Double-buffered pools** (front/back, swap at frame edge) for MAIN-thread writes — **REJECTED by
  user 2026-07-27, do not re-propose.** It was the textbook fix for the main-writes/render-reads
  transform race, but the race has never manifested in practice (project is run after every
  iteration) and the cost — 2x memory plus dirty-range copy-forward — is not worth it for UI. Note
  this is specifically about main; PHYSICS still gets prev/curr, for interpolation rather than
  tearing (see below).
- **Derived columns** (declare a column as a function of another, recompute at frame edge). Already
  done informally by `GpuTransform`. Needs the dependency graph above for recompute order.
- **A runtime event bus is still right for GAMEPLAY events** (script-driven, "who cares" decided at
  runtime). That belongs ABOVE the ECS, single-threaded within a tick, and is a per-frame buffer
  rather than a broker. Do not push it into the data layer.

## Ownership enforcement (DONE 2026-07-27)
`DataPool.AssertOwner(op)`, `[Conditional("DEBUG")]`, throws when `ThreadedSystem.Current.SystemId
!= OwnerSystemId`. Guards the write-capable entry points: `GetSpan`, `GetRef`, `CopyFrom`,
`UpdateRange`, `Allocate`, `Free`, `MarkContentDirty`, `MarkRangeDirty`, `MarkOrderDirty`,
`FrameEdge`. Release builds compile the calls away.

Two deliberate holes, both required:
- **`Current == null` allows.** `_current` is set inside `ThreadedSystem.Loop()`, so it is null for
  the whole of bootstrap — and controls are parsed from XML there, long before any thread starts.
  Single-threaded at that point, so there is nothing to catch.
- **`OwnerSystemId == 0` allows** — pools are unresolved until `DataManager.ResolveOwners()`.

NOT guarded, on purpose: `Backing<T>()`, `CopyTo`, `CopyRange`, `OwnerAt`, `Count`, `Capacity` and
the published versions. These are the sanctioned cross-thread READS — how the render thread reaches
a Main-owned pool at all. C# cannot hand out a read-only `T[]`, so read-only stays convention there.

**Latent problem this surfaced:** `FrameEdge` is guarded because compaction moves memory, but
`DataManager.FrameEdge()` is a flat loop over EVERY pool called from `MainTick`. The day a pool is
owned by Physics, that throws. The loop needs to become per-system first. Cannot fire today —
`Pools.xml` has both pools on `System="Main"`.

The previously-known violation (`MCUI.BakeMatrices` writing `GpuTransform` from the render thread)
is already gone — the bake moved to `VulkanControl.CommitTransform` in the fifth slice.
