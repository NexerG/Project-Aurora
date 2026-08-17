# Decision — the UI splits into data and visualization; L2 virtualization is dropped

**Date:** 2026-08-17
**Status:** PLANNED, not started. **Sequenced after Periodic and the test/profiling platform** —
the UI ships as it is first, then the engine is redone against this.
**Supersedes:** the L2 "virtualized view over a `DocumentLayoutCache`" plan, which was built
(`34567d1`) and reverted (`542e7d7`, 2026-08-07). The cache, `TextRunControl` and
`DocumentCanvasControl` are deleted and are not coming back.

## The direction (user, 2026-08-17)

Data and controls get separated. **Most of the UI becomes data; controls become visualization.**

This is the same conclusion L2 reached, generalized off the document and onto the whole UI. L2's own
closing finding was that virtualizing the *view* was not the binding constraint — a note's glyphs
exist because parsing builds them, before any view is consulted, so the cost is in the model being
controls rather than in the drawing. Virtualization removed a second copy and left the first. The
answer is not a smarter view over a control tree; it is that the tree stops being the data.

## Why this and not the alternatives

- **Not virtualization.** Tried, measured, reverted. It halved the cost and left the ceiling where
  it was, and it cost a second geometry system that the caret had to be kept in step with.
- **Not frustum culling.** Recorded repeatedly and still true: culling stops off-screen controls
  being *drawn*. The entity, the pool row and the descriptor slot all survive it, and the ceiling is
  made of those, not of draw calls. See [[text-layout-one-measurer]].
- **Not "make glyphs plain rows".** Standing decision, unchanged: per-letter colour, rotation and
  animation are required, so whatever a glyph becomes has to keep carrying its own transform and
  tint. See [[glyphs-as-pool-data]] and the auto-memory `periodic-is-a-rich-editor`. This split is
  about *where the state lives*, not about taking capability away.

## Shape (user, 2026-08-17)

### 1. The parent/child tree becomes data

This **reverses** [[ecs-rework-data-pools]]'s locked 2026-07-18 decision, which read *"UI hierarchy
(parent/children) **stays in class graph** (OO tree) — measured as negligible at UI scale. But
ControlData/settings move to arrays."* Parentage moves into the pool alongside the other columns.

The pool is already the right shape for it: `Pools.xml` declares `UIControls` **Ordered**, canonical
order is DFS pre-order of the tree, `UI.DFSOrder` + `Resequence` already run on a tree mutation, and
`CompactOrdered` already preserves relative order on a destroy. What is missing is that the links
themselves still live in `Entity.children`, so the pool knows the *order* of the tree without
knowing the tree.

The prize is that layout and hit-test become flat forward loops over a DFS-ordered array instead of
virtual dispatch down an object graph — parent before child is both painter order and layout
dependency order, so one pass serves both. The cost is that per-type `Measure`/`Arrange` behaviour
has to stop being a virtual override and become data the loop switches on (ecs-rework's *"enum/flag
field + switch in loop"*), and that everything written against `Entity.children` today —
`AddChild` on five container types, `DocumentXml.AttachChild`, `VulkanControl.ParseXML`,
`TextControl.SyncGlyphs`, `UICollisionHandling.FindDeepestValid` — is written against the thing being
replaced.

### 2. A control stays one object per element, presenting a row

Not one visualizer per kind. A control keeps its identity and its API and owns no state — it reads
and writes its row through its handle, which is what `VulkanControl` already does for
`TransformData` / `ControlData` / `GpuTransform`. This keeps ecs-rework's *"classes stay as proxies
where useful"* even while reversing the tree half, and it is the answer consistent with the standing
decision that a glyph keeps its own transform and tint.

**Consequence to be clear about: this does not reduce the number of controls.** See
[[#What this does and does not buy]].

### 3. The data rides the existing pools

More columns on `UIControls` in `Pools.xml`, not a second store. Ordered compaction, DFS resequence,
`PoolCursor` and the two persistent SSBO mirrors were all built for this pool specifically and
already work; reusing them makes the split "columns plus a flat loop" rather than new
infrastructure with its own lifetime and ordering rules.

The known friction is the things that do not want to be a fixed-stride unmanaged struct — a
control's children (variable length), its text (a string), its style inheritance. ecs-rework already
has the answer for the first: `(start, count)` ranges into a shared array rather than inline. The
other two are unsolved and are the first thing to design when this starts.

## What this does and does not buy

**Does:** one source of truth for UI state; layout and hit-test as flat forward loops in DFS order;
the whole UI snapshottable and serializable as columns; per-type behaviour expressed as data.

**Does not: it does not lower the control count.** One object per element means 56.7k glyphs stay
56.7k objects and 56.7k rows. The glyph ceiling recorded in [[periodic-editor-architecture]] is made
of exactly those, so **this split is not its fix** — the only thing that touches the count is the
escape hatch already written down there (a run holds `text` + its `BlockLayout` with no glyph
children and calls `SyncGlyphs()` when visible), or a per-kind visualizer, which was considered and
not taken because per-letter colour, rotation and animation are required.

Worth saying plainly because the reverse was briefly written into the WIP list and corrected: the
ceiling and this split are two separate problems that share a cause.

## Sequencing (user, 2026-08-17)

1. **Finish the UI as it is.** No redesign work lands while Periodic's editor is being built on it.
2. **Periodic reaches its first version**, and the test/profiling platform — the one built *on* the
   UI, per Phase A — comes up with it.
3. **Then redo the engine's UI against this split**, with the profiler available to say what the
   numbers actually are rather than inferring them from control counts.

The ordering is the point: the profiler is built on the UI it is going to be used to measure, so the
UI has to work before there is anything to measure with. Redoing the UI first would mean rebuilding
the profiler's own foundation underneath it.

## Open

- **How variable-length and managed members live in columns.** Children have ecs-rework's
  `(start, count)` answer; a control's `text` (a string) and its style inheritance do not. A managed
  reference inside a pool struct poisons the pool, so this is the first design job.
- **What per-type layout becomes.** A flat loop cannot call a virtual `Measure`/`Arrange`, so every
  container's behaviour has to be expressible as a layout-kind column plus a switch. Whether every
  current container survives that is unverified — `TextBlockControl`'s inline flow with the
  `firstLineOffset` / `lastLineEndX` handshake is the awkward one.
- **Whether the glyph count is dealt with separately, or accepted permanently.** See
  [[#What this does and does not buy]]. Nothing in this split addresses it.

Related: [[ecs-rework-data-pools]], [[text-layout-one-measurer]], [[glyphs-as-pool-data]],
[[periodic-editor-architecture]]
