# Data-oriented layout — the UI measure/arrange over `UIElements` rows

**Status:** AGREED 2026-09-25. Steps 0–3 landed 2026-09-25; step 4 (the walks) not yet approved as a build. Decisions A–D and the step-1 forks settled (below). Supersedes the L1/L2 items of 2026-09-25. Cold boot: [[layout-dod-handoff]].

**Why:** layout is ~0.46 µs per control on a full relayout because it walks the object tree (`children` lists, virtual calls, a `Dictionary<Type>` column lookup per `arrange` access, 10–15 per child), and its cost follows the tree, not the changes — 1,000 animated buttons cost what 200,000 do (Baseline). Target: sparse changes at 1M under 1 ms of layout; a full 1M relayout ~20–40 ms single-threaded (estimate, bandwidth-bound).

## Shape
- `UIElements` rows are in tree pre-order (`UIEngine.ElementOrder`); a subtree is the row range `[i, i + count)`.
- Column **`LayoutNode`**: `parent` row, subtree `count` (0 = not sequenced yet — a fresh `Allocate` row, or one no window tree reaches), `kind` (`LayoutNodeKind`), a stack's `axis` and `spacing`.
- Kinds: **`Single`** (today's `Control` measure/arrange — buttons, panels, containers), **`Stack`** (today's `StackPanelControl` math, verbatim), **`Custom`** (a type overriding `MeasureCore`/`ArrangeCore`; one virtual call on its own row, its children re-enter the engine through `child.Measure`/`Arrange`). Custom is detected by reflection once per type (`LayoutEngine.KindOf`).
- Measure: one forward pre-order walk with a stack of open parents — offer on enter, fold into the parent on leave, a stack's star children on the parent's leave. Skip a clean row offered what it was last measured with (`measuredOffer`) → jump its range.
- Arrange: forward walk; rect from the parent's cursor, clip from the parent's; subtree bounds unioned on leave (replaces `RefreshSubtreeCache`). Skip a clean row with the same rect and inherited clip.
- Rows not sequenced (created during layout — grips, thumbs; measured before attach — context-menu panels) take an object walker running the same kind math.
- Paint is not part of layout: resolved at draw (step 2), so the walks never touch a control object for flattened kinds.

## Decisions (user, 2026-09-25)
- **A → (a)** structure rebuilt by a separate object walk (`LayoutEngine.BuildStructure`) when `UIElements.OrderVersion` changed. Rejected for now: folding it into `ElementOrder` (saves one O(n) walk per structural change, couples layout to the sort, needs a version tag). Measured: `Layout.Structure` 2.5 ms worst at 20k, unoptimized Debug.
- **B →** a destroyed child tells its parent: `Entity.Destroy` keeps its inline detach and calls `protected virtual OnChildDetached(Entity)` on the parent; `Control` marks the tree order dirty and invalidates layout. Rejected: detaching through `parent.RemoveChild(this)` (first pick) — `TabViewControl.FinishClose` nulls `activeItem` then `item.Destroy()`, and `TabViewControl.RemoveChild` → `CloseIfEmptied()` would collapse a split / close a secondary window that still has tabs. Rejected: leaving `Destroy` alone — a destroyed row stays in its old parent's range until freed next frame, the parent's size stale until something else invalidates it.
- **C →** flatten only `Single` and `Stack`; docking, document, grid list stay Custom until a profile says otherwise.
- **D →** splitting `ArrangeData` (140 B) into hot/cold columns: a possibility, much later. Not planned.
- **Enum `LayoutNodeKind`**, not `LayoutKind` — that name shadows `System.Runtime.InteropServices.LayoutKind` for the 7 `[StructLayout(LayoutKind.Sequential)]` in `UIData`, `Palettes`, `Gradients`, `Effects`. Rejected: qualifying those 7.
- **`StackPanelControl.orientation`/`Spacing` became properties**: the getter keeps a backing field (the old stack loops read them per child), the setter mirrors into `LayoutNode` and invalidates layout — a runtime change did not invalidate before. Rejected: copying them only in `BuildStructure` (a later change missed until the order changes).
- **Step 2 is the user's design**: no paint invalidation at all — resolve at draw. See [[ui-palettes]] § Resolution runs at draw.

## Steps
- [x] **0. Before/after check (2026-09-25)** — `UITreeDump` adds clip, `paint`, `edgePaint`, `alpha` and `Dump(label)` → `uitree-<label>.xml`; `ProfileScenario --dump-tree` dumps `open`/`typed`/`settings` (document) and `grid-<N>` (animation, after each build's settle); stage `Scenario.SparseMarginStart`/`SparseMarginHold` (`profile-margin` on every N/1000th button). Byte-identical across two runs of one build. The real cursor over the window hovers a button — its alpha differs; nothing else does.
- [x] **1. Structure (2026-09-25)** — `LayoutNode` + `LayoutNodeKind` in `UIData`; `<Component Type="LayoutNode"/>` on `UIElements`; `<Step Edge="UIElements"/>` between `Main.Apply` and `Main.Layout`; `LayoutEngine.BuildStructure` (called first in `UIEngine.ResolveLayout`, zone `Layout.Structure`) + DEBUG `VerifyStructure`; `Control.node`; stack settings mirrored; `Entity.OnChildDetached`. Dumps geometry-identical; DEBUG verify silent over both scenarios (18 rebuilds inside the captures alone). Nothing reads the structure yet.
- [x] **2. Paint out of arrange (2026-09-25)** — `UIEngine.Collect` calls `InheritPaint` before `Emit`; `WriteArranged` no longer does; `InheritPaint` returns nothing; `RepaintChildren`/`PushPaint` deleted with their callers (`colorHex`, `alpha`, `ButtonControl.PaintState`); repaint-only `InvalidateArrange` deleted (`role`, `paletteName`, `edgeRole`, `TextRunControl.alpha`, the theme switch). Dumps: geometry identical, on-screen paint identical, off-screen paint unresolved (as designed). Cost: 5–10 µs a frame in the document view (scratch zone), +0.1 ms on the grids (~3.6k drawn buttons).
- [x] **3. `Measure`/`Arrange` non-virtual (2026-09-25)** — entries call `protected virtual MeasureCore`/`ArrangeCore`; 37 overrides and 12 `base.` calls renamed (sweep by `aurora-mechanic`, diff checked line by line). Dumps identical; Debug scenarios clean.
- [ ] **4. The walks** — `LayoutEngine.Measure`/`Arrange` (range or object walker); `StackPanelControl` overrides, `RefreshSubtreeCache` and `ResolveLayout`'s recursion deleted; `ArrangeData` + `measuredOffer`, − `subtreeCount` (→ `LayoutNode.count`). Needs its own plan and go.
- [ ] **5. Docs** — `Decisions/layout-dod.md` + INDEX, `where-things-live`, `ui-orientation`, vault `UI-ENGINE.md` (the walks), WIP.

## Baseline — old layout (2026-09-25, Release+PROFILE, steps 0/3 and the edge move in)
| ms/frame | `Main.Layout` | measure | arrange | subtree cache | frame |
|---|---|---|---|---|---|
| 200k margin hold (all animate) | 105.9 | 26.0 | 62.6 | 17.3 | 135.3 |
| 200k sparse hold (1,000 animate) | 106.7 | 25.9 | 63.5 | 17.2 | 108.4 |
| 1M sparse hold (1,000 animate) | 463.2 | 117.8 | 263.8 | 81.1 | 465.6 |
- 1M ran as a scratch cut (build, sparse, teardown) to fit the 3-minute cap: build frame 3.1 s, teardown worst frame 273 ms.
- The 200k margin hold was 92.5 ms on 2026-09-24 (ladder `{ 20000, 200000 }`); this run is `{ 200000 }` alone — compare within a run.

## Verified / not
- Verified: builds (Debug, Release+PROFILE); tree dumps as above; a Release capture of the document scenario window draws palette-painted text.
- **NOT GUI-verified:** theme switch, hover/press colours, Clear-role controls fading in, context-menu edges, file tree expand/collapse, closing tabs (the `OnChildDetached` path), Carbon's charts.

## Left out
Draw lists and hit-test as range walks; parallel ranges (L3); parent-relative rects (L4) — scrolling or a row-height change still re-arranges everything below; `Main.Apply` (in-place A5, the next bottleneck); resequence O(n) per structural change (~20 ms at 200k); SIMD.

## Risks
- Skips expose invalidations today's full relayout hides — the dump check and a GUI pass.
- Children added to a `Stack`/`Single` during layout lay out a frame late (DEBUG warning planned).
- `ApplyShape` writes `edgeThickness` for an `accentRole` every drawn frame — would override an animated `edgeThickness` on the same control. Nothing sets `accentRole` today.

Related: [[ui-engine-stack]], [[ui-palettes]], [[animation-core]], [[pool-shrink]], [[frame-scheduler]], [[engine-profiling]]
