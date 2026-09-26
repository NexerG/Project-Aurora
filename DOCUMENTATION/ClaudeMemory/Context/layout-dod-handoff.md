# Layout DOD rework — cold-boot handoff

**Written:** 2026-09-25, end of the session that planned step 4. **Read with:** [[layout-dod-plan]] (agreed shape, decisions A–D, steps, baseline table). Full transcript of that session, for anything missing here: `C:\Users\gmgyt\.claude\projects\D--Repositories-Project-Aurora\6baa924f-16e7-44fd-a80a-92b5859c7d91.jsonl`.

## State
- **Goal (user):** layout for ~1M animatable controls — "1 ms for 1M" if it looks possible. Focus: UI layout logic. Numbers from Release+PROFILE. No SIMD in plans yet. L3 (parallel ranges) and L4 (parent-relative rects): later, not now.
- **Done, uncommitted** — the tree is dirty since `5526908`:
  - pools grow ×2 and halve after 2 s at a quarter full, lowest free stableId first → [[pool-shrink]]
  - Release+PROFILE boots: `XSDGenerator.WriteSchema` creates the schema folder; `Thorium.csproj` copies all of `Data\` (the engine's own font bakes are stale — WIP)
  - `ProfileScenario`: sparse stage, `--dump-tree`; `UITreeDump.Dump(label)`
  - layout-DOD steps 0–3: dump check, `LayoutNode` structure, paint resolved at draw, `MeasureCore`/`ArrangeCore`
- **Also dirty, not from this work:** `CLAUDE.md`, `.claude/agents/*`, `.claude/skills/*` (the user's edits), `NAMESPACES.md` (regenerated), generated `Thorium/Data/XML/Schemas/*`. CLAUDE.md §9: when asked, one commit of everything (`aurora-committer`); never unasked.
- **Step 4 (the walks): planned, NOT approved.** Three forks open (§ Forks). Step 5 = docs.
- **Next:** fork answers + go → re-read the regions the plan touches → baseline dumps from the current tree → implement → verify → step 5 docs → terse report with the NOT GUI-verified list.

## Step 4 plan (as presented to the user, 2026-09-25)

### Design
- **One entry for every measure/arrange**, whoever calls it: `Control.Measure/Arrange` → `LayoutEngine.Measure/Arrange(control, …)`. Skip test first; then the Single/Stack math straight off the spans (flattened row), or one virtual call (Custom row — its Core keeps calling `child.Measure/Arrange`, which re-enters the entry).
- **Skip tests, every kind:**
  ```
  measure: !MeasureDirty && offer == measuredOffer                          → return desired
  arrange: !(ArrangeDirty|Remeasured) && rect == arranged
           && ClipOf(rect, parent clip) == clip                             → leave the subtree
  ```
  Custom rows skip too: a Custom inside a skipped Stack range never runs anyway, so exempting Customs buys nothing.
- **`ArrangeFlags.Remeasured`:** measure sets it on every row it recomputes, arrange clears it, so a row whose children got new desired sizes is re-arranged though its own rect is unchanged (e.g. a star child re-measured at a new offer while its arranged rect stays the same). Not `ArrangeDirty`: a row measured and never arranged (hidden, or a Custom that only measures it) would stay `ArrangeDirty`, and its later `InvalidateArrange` would stop at it.
- **Children:** rows `[row+1, row+count)` stepping by `count` when the structure is current — row `count > 0`, `BuildStructure` ran for this `OrderVersion`, `!pool.OrderDirty`; otherwise the object's `children` (the object walker). Grips, thumbs and highlights created mid-layout mark the order dirty, so the rest of that pass reads objects — no child lays out a frame late. A fresh row (`Allocate` clears it) is `count 0`, `kind 0` = Custom, so it goes through the virtual Core, whose default runs the flat math on objects.
- **Recursion over row indices** — the parent stack is the call stack; same forward pre-order.
- **Pool growth mid-layout:** a Custom may allocate (grips, thumbs, highlights) and `Grow` reallocates the columns. Re-fetch the spans and the current flag after every Custom call; never hold a `ref` into a row across a child call.
- **Subtree bounds:** unioned on leave from the children's stored `subtreeBounds` (a skipped child's are still right). A Custom row unions over the object's children (always current).
- **Flags:** the entry clears `MeasureDirty` (and sets `Remeasured`) after measuring, and `ArrangeDirty|Remeasured` after arranging — the same point today's Cores clear them.

Entry sketch:
```
Measure(control, offer):
  row = DenseOf(control); a = arrange[row]
  if !MeasureDirty(a) && a.measuredOffer == offer: return a.desired
  kind = current && nodes[row].count > 0 ? nodes[row].kind : Custom
  desired = Custom ? control.CallMeasureCore(offer)  (then refresh spans + current)
          : Stack  ? MeasureStack(row, offer) : MeasureSingle(row, offer)
  a = arrange[row]; a.measuredOffer = offer; a.flags = (a.flags & ~MeasureDirty) | Remeasured
  return desired

Arrange(control, rect):   // the row walk passes the parent clip down; an entry reads the object parent's
  clip = ClipOf(rect, hasParent, parentClip, a.flags)
  if !(ArrangeDirty|Remeasured)(a) && a.arranged == rect && a.clip == clip: return
  Custom ? control.CallArrangeCore(rect)  (then refresh)
         : { a.arranged = rect; a.clip = clip; ArrangeSingle / ArrangeStack }
  a.subtreeBounds = union(a.arranged, children's subtreeBounds)
  a.flags &= ~(ArrangeDirty|Remeasured)
```
- **Single math** = today's `Control.MeasureCore`/`ArrangeCore`; acts only with exactly one child (rows: `count > 1 && 1 + nodes[row+1].count == count`).
- **Stack math** = today's `StackPanelControl` verbatim: pass 1 non-star, pass 2 star; arrange pre-pass (fixed total, star unit), then the cursor; hidden children skipped; `orientation`/`Spacing` → `node.axis`/`node.spacing` (Horizontal 0, Vertical 1).
- `LayoutRect` has no `==` — compare fields (`ValueType.Equals` boxes).

### Files
1. `UIData.cs` — `ArrangeFlags.Remeasured = 16`; `ArrangeData`: − `subtreeCount`, + `Vector2 measuredOffer` (140 → 144 B); `LayoutNode`: − `parent` (fork 1).
2. `LayoutEngine.cs` — the entries and walks, Single/Stack math moved in, `ClipOf` (the one clip formula); `BuildStructure` stops writing `parent`; DEBUG `VerifyLayout` (fork 3).
3. `Control.cs` — `Measure`/`Arrange` → LayoutEngine; `MeasureCore`/`ArrangeCore` defaults → `MeasureOwn`/`ArrangeOwn` (Stack math on a `StackPanelControl`, which keeps `ContextMenuControl`'s inherited measure and its `base.ArrangeCore` working; Single otherwise); + `internal CallMeasureCore`/`CallArrangeCore`; `WriteArranged` uses `ClipOf`; − `RefreshSubtreeCache`; `Hide()` invalidates the parent (fork 2).
4. `StackPanelControl.cs` — − `MeasureCore`/`ArrangeCore` and the usings only they needed.
5. `UIEngine.cs` — `ResolveLayout`: − the `RefreshSubtreeCache` pass and the `Layout.SubtreeCache` zone; `VerifySubtreeCache` keeps the bounds check, drops the count check.
6. `DataPool.cs` — + `public bool OrderDirty => _orderDirty;`.
7. Generated schemas change at boot (`ArrangeData`, `LayoutNode`).
8. Docs = step 5.

New signatures:
```csharp
// LayoutEngine
public static Vector2 Measure(Control control, Vector2 offer)
public static void Arrange(Control control, LayoutRect rect)
internal static Vector2 MeasureOwn(Control control, Vector2 offer)
internal static void ArrangeOwn(Control control, LayoutRect rect)
internal static LayoutRect ClipOf(LayoutRect rect, bool hasParent, LayoutRect parentClip, byte flags)
// Control
internal Vector2 CallMeasureCore(Vector2 offer) => MeasureCore(offer);
internal void CallArrangeCore(LayoutRect rect) => ArrangeCore(rect);
```
- No new files, no packages. No subagent handoff (§10) — nothing mechanical once the flag-clear sweep is out.
- Unchanged: every Custom override, `InvalidateLayout`/`InvalidateArrange`, the root loop in `ResolveLayout`, hit-test and draw lists (still read `subtreeBounds`), `TextRunControl`'s wrap-width early-out.

### Left out
- The per-Core `SetFlag(…Dirty, false)` lines — duplicates of the entry's clear, harmless.
- Clearing flags before a Core runs. An invalidate raised during a row's own measure is still swallowed, as today; today the next relayout of anything heals it, a skip won't.
- Incremental stack sums, L4, L3, draw lists and hit-test as range walks, more flattened kinds (C), hot/cold split (D), `Main.Apply` (A5), O(n) resequence.

### Expected (estimates, not measured)
- **Sparse hold, 200k and 1M:** a few ms (now 106.7 / 463.2). Not 1 ms: 1,000 animated buttons dirty 1,000 rows of 100, and each dirty row re-sums 100 buttons and re-places those after the change — ~100k button rows a frame at either size. Closing that needs incremental stack sums or L4.
- **All buttons animating:** still a full relayout, cheaper per row (no virtual call, no `Dictionary` lookup per field); estimate 5–10× on 105.9 ms.

### Risks
- A control that changes a layout input without `Invalidate*` now shows stale layout; today any relayout in the window hid it. Found so far: `Hide()` (fork 2). The rest: fork 3 and the user's GUI pass.
- Dumps: a hidden control keeps `Hide()`'s collapsed clip until its parent re-arranges it; today every relayout rewrote it. Invisible (hidden rows are neither drawn nor hit), but it shows in the dump.

### Verification
1. Build → Debug and Release+PROFILE clean.
2. Both scenarios with `--dump-tree`, Release → geometry and on-screen paint identical to the baseline dumps, except the clip of rows hidden or under a hidden control, and the known cursor-hover alpha.
3. Both scenarios in Debug → no `VerifyStructure`/`VerifySubtreeCache`/`VerifyLayout` errors; a scratch break in the skip test makes `VerifyLayout` fire (reverted).
4. Release+PROFILE scratch `{ 200000 }` and the 1M cut (reverted) → `Main.Layout`/measure/arrange table against the baseline in [[layout-dod-plan]].
5. Capture of the document scenario window (aurora-verify `capture.ps1`).
6. NOT GUI-verified until the user's pass: scrolling and thumbs, tab switch/close, context menus (submenu, slide), file tree expand/collapse, text boxes (caret, selection), settings controls, the note-name dialog's discard button, resize/maximize grips, Carbon's charts.

### Step 5 — docs (same go)
New `Decisions/layout-dod.md` + INDEX row; [[layout-dod-plan]] (step 4 ticked, results); `where-things-live` (layout rows); `ui-orientation` (layout region: entries, skip, `Remeasured`, `Hide`); [[ui-engine-stack]] (the "`Control.RefreshSubtreeCache` fills…" line superseded); vault `Engine/Systems/UI-ENGINE.md` (walk pseudocode) and `PROFILING.md` (zone list loses `Layout.SubtreeCache`); the WIP entry; `Changelog.md`; retire or update this handoff.

## Forks (open — ask the user)
1. **`LayoutNode.parent`** — nothing reads it once the walk is recursive. (a) delete (recommended) (b) keep for a later explicit-stack or parallel walker.
2. **`Hide()` → `(parent as Control)?.InvalidateLayout()`** — a stack skips hidden children, so hiding one changes the stack's layout; today the gap closes only on the next unrelated relayout (e.g. `NoteNameWindow`'s discard button). (a) yes (recommended) (b) leave.
3. **DEBUG `VerifyLayout`** — after a layout, re-run it on the same roots without skips, log the rows that differ (control, type, field), then restore the skipped result so Debug shows what Release shows. Debug layout time doubles on frames that lay out; Custom Cores run twice there. (a) yes (recommended) (b) no — dumps and the GUI pass only.

## Facts from the step-4 analysis (2026-09-25 — re-check before editing)

### Layout today
- `InvalidateLayout`/`InvalidateArrange` set the flags and climb `parent` until an already-dirty ancestor (return) or the top, then `RegisterDirtyRoot(top)` — a relayout runs from the window root and re-lays the whole tree. No Core checks flags except `TextRunControl` (wrap width).
- `ResolveLayout`: `BuildStructure`; per registered root with no Control parent: `MeasureDirty` → `Measure(arranged size, or MaxValue)` + `Arrange(arranged, or origin at desired)`; else `ArrangeDirty` → `Arrange(arranged)`; then `RefreshSubtreeCache` and DEBUG `VerifySubtreeCache`.
- The `Control` constructor sets `Clip|MeasureDirty|ArrangeDirty` and registers itself as a root (skipped once it has a Control parent).
- `WriteArranged`: `arranged = rect`; clip = no Control parent ? rect : `Clip` ? Intersect(rect, parent clip) : parent clip.
- `Hide()`: `Hidden` + `CollapseClip` (subtree clip `(0,0,-1,-1)`), no invalidate. `Show()`: clears `Hidden` and `MeasureDirty`, then `InvalidateLayout`. `ClipSubtree` only in `ContextMenuControl.ArrangeCore`.
- `subtreeCount` is read only by `RefreshSubtreeCache`/`VerifySubtreeCache`; `subtreeBounds` by `UIEngine.HitTest` and `UIEngine.Collect`.
- `UITreeDump` writes Type, Name, X/Y/W/H, DesiredW/H, Hidden, ClipX/Y/W/H, Paint, EdgePaint, Alpha — no subtree data.

### Kinds (`LayoutEngine.KindOf`: Custom when `MeasureCore` or `ArrangeCore` is declared below `Control`/`StackPanelControl`)
| Kind | Types |
|---|---|
| Custom — 19 declare overrides | `WindowRoot`, `WindowFrameControl`, `DockingControl`, `TabViewControl`, `ScrollableControl`, `DocumentEditorControl` (arrange only), `DocumentControl`, `GridListControl`, `TextRunControl`, `BlockControl`, `IconControl` (measure only), `SliderControl`, `TextBoxControl`, `EditableLabelControl`, `DropdownControl`, `ContextMenuControl` (arrange only), `ContextMenuControl.Row`, Carbon `SpanChartControl`, `FrameStripControl` |
| Custom — inherited | `EditableTabsControl`; `FileBrowserControl`, `FileTreeControl`, Thorium `VaultBrowserControl`, Carbon `SessionListControl`, `ZoneTableControl`; `LabelControl`, `TextBoxControl.FieldLine`; `DocumentToolbarControl.PxBox` |
| Stack | `StackPanelControl`, `TitleBarControl`, `SplitViewControl`, `DocumentToolbarControl` |
| Single | the rest — `Control`, `PanelControl`, `ContainerControl`, `ButtonControl` and its non-overriding subclasses, `CaretControl`, `HintControl`, `TabItemControl`, `WorkspaceControl` |
- `base.` calls: `SpanChartControl`, `FrameStripControl`, `SliderControl`, `WindowRoot` (KeepLocal) → `Control`'s; `ContextMenuControl` → `StackPanelControl.ArrangeCore`, and inherits its `MeasureCore`; `DocumentEditorControl` → `ScrollableControl`'s (twice when the caret scroll moves); `BlockControl` → `TextRunControl`'s.
- All 18 `MeasureCore` overrides store what they return in `desired`, so a skip may return the stored value.
- Single acts only with exactly one child; a `ContainerControl` with several children and no override lays none of them out (today's behaviour).
- Hidden children: Stack skips them in measure and arrange; `SpanChartControl`'s measure skips them; `DockingControl`/`GridListControl` don't check.

### Mid-layout mutations
- Children created during arrange, each with `MarkTreeOrderDirty()` and no invalidate, arranged by their owner: `WindowFrameControl.EnsureGrips` (4), `ScrollableControl.EnsureThumbs` (2), `DocumentControl.Highlight` (selection boxes). Grips and thumbs are never measured → `MeasureDirty` forever (no children, harmless).
- `DataPool.Allocate` may `Grow` (columns reallocated → stale spans) and clears the new row.
- `ScrollableControl.ArrangeThumbs` calls the thumbs' `Show()`/`Hide()` every arrange (no-ops unless flipping); a `Show()` mid-arrange climbs (measure flags are clear by then) and registers the root → one more layout next frame.
- `DocumentEditorControl.ArrangeCore` consumes `scrollToCaretPending` (set with `InvalidateArrange` by `RequestScrollToCaret`), may run `base.ArrangeCore` twice, and must exit with `ArrangeDirty` clear (its comment).
- An invalidate raised while its row or an ancestor is still `MeasureDirty` mid-measure returns without registering a root — pre-existing.

### Outside `ResolveLayout`
- `ContextMenus.Host`: `panel.Measure(root viewport)` during `Main.Input`, before attaching (fresh rows, order dirty → object path).
- `WindowRoot.FitTo`: `WriteArranged` + both dirty flags + `RegisterDirtyRoot`; `ControlXml` does `WriteArranged` + `RegisterDirtyRoot` on load.

### Pool and structure
- `DataPool.MarkOrderDirty` sets the private `_orderDirty`; its only caller is `Control.MarkTreeOrderDirty`. `FrameEdge` compacts freed rows and resequences, both bumping `OrderVersion`; the `UIElements` edge runs right before `Main.Layout`, and `BuildStructure` (first in `ResolveLayout`) rebuilds on a new `OrderVersion`.
- `BuildStructure` walks window roots only; detached trees (a context-menu panel before attach) keep `count 0`.
- `GetSpan` asserts write access in DEBUG; `Main.Input`/`Logic`/`Apply`/`Layout`/`DrawLists` all write `UIElements`.

### The animation scenario's tree
- `WindowRoot` → vertical `StackPanelControl` → rows of 100 (horizontal `StackPanelControl`) → 16×16 `ButtonControl`, no children.
- `profile-margin` (`UI.anim.xml`) ping-pongs the left margin 0 → 4 over 1 s: a row's width changes, its height and the rows below don't.
- Sparse stage: every (N/1000)th button → 1,000 dirty rows at 200k (every other row) and at 1M (every tenth).

## Reproducing
- **Build:** Debug `dotnet build AuroraEngine/ArctisAurora.sln`; Release+PROFILE `dotnet build AuroraEngine/ArctisAurora.sln -c Release "-p:DefineConstants=TRACE%3BPROFILE"`.
- **Run** `Thorium/bin/<Debug|Release>/net10.0-windows10.0.22621.0/Thorium.exe` with that folder as cwd — never `dotnet run`. Cap every run at 3 minutes (user).
  - `--profile-scenario`: a note of 1,000 blocks × 1,000 chars — open, type, resize, settings; dumps `open`, `typed`, `settings`.
  - `--profile-scenario=animation`: ladder `{ 100, 1000, 5000, 20000 }` under one capture; dumps `grid-<N>` after each build settles.
  - `--dump-tree` → `uitree-<label>.xml` beside the exe; `--profile-pools` → each pool's rows and bytes per frame in the capture.
  - The scenario points the settings write root at a sibling `ProfileScenario` folder; a plain boot re-saves the user's real session, so tests don't boot Thorium plainly.
- **Captures:** `%APPDATA%\Thorium\Profiling\<yyyyMMdd-HHmmss>\*.frames.xml`; older sessions are pruned.
- **Perf cuts** (scratch edits to `ProfileScenario`, reverted after): 200k = `ladder = { 200000 }`; 1M = `ladder = { 1000000 }` with the stages cut to build → settle → sparse (60-tick hold) → teardown, to fit the 3-minute cap.
- **Stage tables:** `stages.ps1 -Dirs <capture dir> -Log <run stdout> -Show Step.Main.Layout,Layout.Measure,Layout.Arrange,…` (appendix).
- **Dump compare:** `run.ps1 -Name <label> -AppArgs '--profile-scenario','--dump-tree'` (or `'--profile-scenario=animation','--dump-tree'`), then `cmpdump.ps1 -Before <old>\uitree-X.xml -After <new>\uitree-X.xml` (appendix). Baselines for step 4 = the current code (steps 0–3): the old session's `C:\Users\gmgyt\AppData\Local\Temp\claude\D--Repositories-Project-Aurora\6baa924f-16e7-44fd-a80a-92b5859c7d91\scratchpad\step2-doc` and `step2-anim` if still there — otherwise regenerate them from the tree before editing code.
- **Debug verifiers:** `LayoutEngine.VerifyStructure` (channel `Layout`) and `UIEngine.VerifySubtreeCache` (channel `UIEngine`), both at `Error`.
- **Visual:** the aurora-verify skill's `capture.ps1`. A dump is not a visual check.

## Carried gaps (earlier steps)
- NOT GUI-verified from steps 1–2: theme switch, hover/press colours, Clear-role fade-ins, context-menu edges, file tree, closing tabs (`OnChildDetached`), Carbon charts.
- `ApplyShape` writes `edgeThickness` for an `accentRole` every drawn frame — latent; nothing sets `accentRole`.
- Animation pools never shrink; the engine's font bakes are stale; resequence is O(n) (~20 ms at 200k); `Main.Apply` is the next bottleneck ([[animation-in-place-plan]] A5).
- The real cursor over the window hovers a button, so its alpha differs between dumps.

## Appendix — scratch scripts (recreate in the session scratchpad; `run.ps1` hardcodes the Release bin)

`run.ps1`
```powershell
param([string]$Name, [string[]]$AppArgs, [int]$CapSeconds = 180)
$S = $PSScriptRoot
$bin = (Resolve-Path "D:\Repositories\Project-Aurora\Thorium\bin\Release\net10.0-windows10.0.22621.0").Path
Remove-Item "$bin\uitree-*.xml" -ErrorAction SilentlyContinue
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath "$bin\Thorium.exe" -ArgumentList $AppArgs -WorkingDirectory $bin -RedirectStandardOutput "$S\$Name.log" -RedirectStandardError "$S\$Name.err" -PassThru
if (-not $p.WaitForExit($CapSeconds * 1000)) { "TIMEOUT at $($sw.Elapsed); closing"; $p.CloseMainWindow() | Out-Null; if (-not $p.WaitForExit(60000)) { "still alive" } }
"elapsed $($sw.Elapsed)"
New-Item -ItemType Directory -Force "$S\$Name" | Out-Null
Get-ChildItem "$bin\uitree-*.xml" -ErrorAction SilentlyContinue | Move-Item -Destination "$S\$Name" -Force
Get-ChildItem "$S\$Name" | ForEach-Object { "  dump $($_.Name) $($_.Length) B" }
Get-Content "$S\$Name.err" -TotalCount 6
Select-String -Path "$S\$Name.log" -Pattern "FATAL|ERROR|WARN|scenario" | Where-Object { $_.Line -notmatch 'ValidationBitExt.*SHADER_READ_BIT|default sampler|not handed within' } | Select-Object -First 12 | ForEach-Object { $_.Line }
```

`cmpdump.ps1`
```powershell
param([string]$Before, [string]$After, [int]$Show = 8)
$geo = 'Type','Name','X','Y','W','H','DesiredW','DesiredH','Hidden','ClipX','ClipY','ClipW','ClipH'
$paint = 'Paint','EdgePaint','Alpha'
$a = [xml](Get-Content $Before -Raw)
$b = [xml](Get-Content $After -Raw)
$na = $a.SelectNodes('//Control'); $nb = $b.SelectNodes('//Control')
if ($na.Count -ne $nb.Count) { "control count differs: $($na.Count) vs $($nb.Count)"; return }
$geoDiff = 0; $onScreen = 0; $offScreen = 0; $shown = 0
for ($i = 0; $i -lt $na.Count; $i++) {
    $x = $na[$i]; $y = $nb[$i]
    foreach ($g in $geo) {
        if ($x.GetAttribute($g) -ne $y.GetAttribute($g)) { $geoDiff++; if ($shown -lt $Show) { "GEO  #$i $($x.Type) $g $($x.GetAttribute($g)) -> $($y.GetAttribute($g))"; $shown++ } }
    }
    $pd = @($paint | Where-Object { $x.GetAttribute($_) -ne $y.GetAttribute($_) })
    if ($pd.Count -eq 0) { continue }
    $w = [double]$x.W; $h = [double]$x.H; $cw = [double]$x.ClipW; $ch = [double]$x.ClipH
    $ix = [math]::Max([double]$x.X, [double]$x.ClipX); $iy = [math]::Max([double]$x.Y, [double]$x.ClipY)
    $rx = [math]::Min([double]$x.X + $w, [double]$x.ClipX + $cw); $ry = [math]::Min([double]$x.Y + $h, [double]$x.ClipY + $ch)
    $visible = $x.Hidden -eq 'false' -and $w -gt 0 -and $h -gt 0 -and $rx -gt $ix -and $ry -gt $iy
    if ($visible) {
        $onScreen++
        if ($shown -lt $Show) { "ONSCREEN #$i $($x.Type) '$($x.Name)' at $($x.X),$($x.Y) $($x.W)x$($x.H): " + (($pd | ForEach-Object { "$_ $($x.GetAttribute($_)) -> $($y.GetAttribute($_))" }) -join ', '); $shown++ }
    } else { $offScreen++ }
}
"{0}: {1} controls, geometry diffs {2}, paint diffs on-screen {3}, off-screen {4}" -f (Split-Path $After -Leaf), $na.Count, $geoDiff, $onScreen, $offScreen
```

`stages.ps1` — per ladder size and scenario stage: frames, total s, frame mean/max, and each `-Show` zone's mean ms per frame (`Anim.Step` takes the max across workers).
```powershell
param([string[]]$Dirs, [string]$Log, [string[]]$Show = @('Step.Main.Input','Step.Main.Logic','Step.Animation.Step','Anim.Step','Anim.Emit','Step.Main.Apply','Step.Main.Layout','Layout.Measure','Layout.Arrange','Layout.SubtreeCache','Layout.VerifyCache','Step.Main.DrawLists','Scheduler.Barrier'))
$Bounds = @()
foreach ($m in (Select-String -Path $Log -Pattern 'Main:(\d+) \[Profiling\] animation scenario . (\d+) buttons')) {
    $Bounds += ,@([int]$m.Matches[0].Groups[1].Value, [int]$m.Matches[0].Groups[2].Value)
}
$frameMs = @{}; $phase = @{}; $zone = @{}
foreach ($dir in $Dirs) {
  foreach ($file in Get-ChildItem $dir -Filter *.frames.xml) {
    if ($file.Name -like 'Render*') { continue }
    $isMain = $file.Name -like 'Main*'
    $names = @{}; $freq = 1e7; $frame = -1
    $r = [Xml.XmlReader]::Create($file.FullName)
    while ($r.Read()) {
        if ($r.NodeType -ne 'Element') { continue }
        switch ($r.LocalName) {
            'FrameCapture' { $freq = [double]$r.GetAttribute('Frequency') }
            'N' { $names[$r.GetAttribute('I')] = $r.GetAttribute('V') }
            'F' { $frame = [int]$r.GetAttribute('I'); if ($isMain) { $frameMs[$frame] = [double]$r.GetAttribute('D') * 1000 / $freq } }
            'Z' {
                $n = $names[$r.GetAttribute('N')]
                $ms = ([double]$r.GetAttribute('E') - [double]$r.GetAttribute('B')) * 1000 / $freq
                if ($n -like 'Scenario.*') { $phase[$frame] = $n }
                $k = "$frame|$n"
                if ($n -eq 'Anim.Step') { if (-not $zone.ContainsKey($k) -or $zone[$k] -lt $ms) { $zone[$k] = $ms } }
                else { $zone[$k] = $ms + [double]$zone[$k] }
            }
        }
    }
    $r.Close()
  }
}
$rows = [ordered]@{}; $last = ''; $seg = 0; $lastNamed = ''
foreach ($f in ($frameMs.Keys | Sort-Object)) {
    $size = 0; foreach ($b in $Bounds) { if ($f -ge $b[0]) { $size = $b[1] } }
    if ($size -eq 0) { continue }
    $p = $phase[$f]; if ($p) { $lastNamed = $p } else { $p = "(after $lastNamed)" }
    if ($p -ne $last) { $seg++; $last = $p }
    $key = "{0,6} {1:D2} {2}" -f $size, $seg, $p
    if (-not $rows.Contains($key)) { $rows[$key] = New-Object System.Collections.Generic.List[int] }
    $rows[$key].Add($f)
}
$hdr = "size phase | frames | total s | frame mean/max"
foreach ($s in $Show) { $hdr += " | $s" }
$hdr
foreach ($k in $rows.Keys) {
    $fs = $rows[$k]
    $m = @($fs | ForEach-Object { $frameMs[$_] }) | Measure-Object -Sum -Average -Maximum
    $line = "{0} | {1} | {2:N1} | {3:N2}/{4:N1}" -f $k, $fs.Count, ($m.Sum/1000), $m.Average, $m.Maximum
    foreach ($s in $Show) { $v = 0.0; foreach ($f in $fs) { $v += [double]$zone["$f|$s"] }; $line += " | {0:N2}" -f ($v / $fs.Count) }
    $line
}
```

Related: [[layout-dod-plan]], [[pool-shrink]], [[ui-palettes]], [[ui-engine-stack]], [[animation-core]], [[engine-profiling]], [[animation-in-place-plan]]
