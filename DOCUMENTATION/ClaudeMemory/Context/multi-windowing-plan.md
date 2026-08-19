# Multi-windowing — agreed plan and resume point

**Agreed:** 2026-08-19. **All six slices landed and GUI-verified.**
**Design + rationale for what already landed:** `../Decisions/render-window-owns-the-swapchain.md`.
**Checklist form:** the `multi-windowing` item in `DOCUMENTATION/Work in Progress List.md`.

This file exists so the work can be picked up cold. It carries the decisions already taken, the
facts that were expensive to establish, and enough per-slice detail to build from without redesigning.

## Goal and scope

Tear a Periodic tab off into its own OS window, and move it between windows.

**In-window split panes are deliberately out of scope** (user, 2026-08-19). They were offered as a
cheaper alternative that needs no renderer work — `StackPanel` + `SplitterControl` + star sizing
already expresses a split pane, so if they are ever wanted, the missing pieces are only the drop-zone
overlay and the wrap/unwrap of a `TabView` in a new `StackPanel`. The user chose multi-window only.

## Standing decisions

These are settled. Do not re-litigate them without asking.

| Decision | Why |
|---|---|
| `RenderWindow` owns swapchain, frame sync, modules, compositor, `isInWindow` | `Renderer` keeps only what there is one of per process: `vk`, `instance`, `gpu`, `logicalDevice`, `queueAllocator`, queues, command pools |
| Windows live in a **name-keyed dictionary**, published copy-on-write (user, 2026-08-19) | Two threads walk it every tick and only main mutates it; a rehash under a concurrent enumeration is broken, not merely stale |
| There is **one main window**, and closing it closes the application (user, 2026-08-19) | No promotion of a survivor to primary, no empty-map case, no reordering |
| The **UI module** owns the UI root, its design-space projection and its instance range (user, 2026-08-19) | A window is a surface with modules; the tree belongs to the module that draws it. `RenderWindow.ui` is a typed handle to the `UIModule` it builds, so callers say `window.ui.uiRoot` |
| Modules are **per-window instances** | Every size-dependent field on `RenderingModule` was already an instance field, so the diff is small. Costs one pipeline compile per window opened |
| A window **builds its own modules in its constructor** | No factory pattern in this codebase. Per-object work goes in the constructor; work that is identical every time may be cached for mass-produced items (user, 2026-08-19) |
| `Renderer.PreInitialize` keeps its name and `Bootstrap.xml` slot | It predates this work. It now collects the device feature set from the windows' modules — the prerequisite step it is named for |
| `hovering` / `dragging` / `activeControl` stay **global statics** | There is one mouse. Per-window copies would be wrong, not merely wasteful. Per window are the hit-test root, `isInWindow`, and — since slice 3 — `mousePos` and `scrollDelta`, because those are *reported* per window and a drag that leaves a window keeps reporting to the window that owns it |
| `EntityRegistry.uiTree` **deleted**, not aliased | An alias would leave a second window's tree unreachable through the property everything reads |
| Swapchain image count is **read from the driver**, never assumed | `swapchainImageCount = 3` is gone; every per-image array sizes off `RenderWindow.imageCount` |
| `MCUI` shared, its SSBO mirrors shared | They mirror the one global `UIControls` pool; duplicating them would double-upload the same bytes |

## Facts that were expensive to establish

- **`gl_InstanceIndex` includes `firstInstance`.** Per-window instance ranges need **no shader change**.
  Confirmed against `UI.vert` in all three copies (`AuroraEngine`, `Periodic`, `AuroraEditor`).
- **`MCUI.EnqueueDrawCommands` already takes an `instanceID` and passes it as `firstInstance`** — it was
  handed `0`. Both overloads live on `MeshComponent` and are shared with `MCRaster` / `MCRaytracing`,
  which is why slice 1 added a UI-only overload rather than changing the base signature.
- **`AddChild` marks tree order dirty**, so additions are covered by the resequence; destroys
  compact-ordered and shift indices *without* re-running the sort, which is why the ranges are
  published by their own frame-edge walk and not as a side effect of `DFSOrder`.
- **A module is constructed before its window has a swapchain** (`Renderer.PreInitialize` is a bootstrap
  step ahead of `Renderer.Initialize`). **Nothing indexed by swapchain image may be sized in a module
  constructor** — that is what `RenderingModule.BindWindow` / `RebindImageCount` are for, and why
  `UIModule`'s constructor is empty.
- **The primary window's GPU setup is interleaved with asset loading** across five `Bootstrap.xml`
  steps, so it cannot collapse into one call. A *secondary* window has no such problem — everything it
  needs already exists — which is what `RenderWindow.CreateGpuResources()` does, landed in slice 2.
- **GLFW window queries are main-thread only.** Slice 0 removed the render-thread
  `glfwGetFramebufferSize` from `RecreateSwapchain`; keep every GLFW call on main and every Vulkan call
  on render.
- **No `InternalsVisibleTo` anywhere.** Anything Periodic or AuroraEditor touches must be `public` —
  that is why `RenderWindow`, `Engine.windows` and `Engine.primary` are.

## Slices

### Slice 0 — `RenderWindow` exists (LANDED, GUI-verified)

See `../Decisions/render-window-owns-the-swapchain.md`. Still exactly one window; nothing
user-visible changed.

### Slice 1 — per-window instance ranges (LANDED, GUI-verified)

Without this a second window draws every window's controls, so **slice 2 is not testable before it**.

- **The UI moved off the window onto its module.** `UIModule` holds `uiRoot`, `ToDesignSpace` and the
  `(firstInstance, instanceCount)` range; `RenderWindow` keeps a public `ui` handle to the module it
  news in its constructor. Nine call sites became `window.ui.*`, `AuroraCamera`'s UI branch became
  `((UIModule)_owner).uiRoot` with no hop through the window at all.
- `UILayout.DFSOrder` — DFS each `Engine.windows[i].ui.uiRoot` in list order, then sweep the pool for
  any live orphan root not yet emitted (detached subtrees become roots, and no window draws them).
  Orphans land **after** every window so the window ranges stay contiguous from zero.
- `UILayout.RefreshWindowRanges` — walks each root counting live controls and assigns `firstInstance`
  as the running total. Called from `Engine.MainTick` right after `DataManager.FrameEdge()`, gated on
  `pool.StructuralVersion` / `OrderVersion` moving, plus an `InvalidateWindowRanges` flag the `uiRoot`
  setter sets (a root swap moves no pool rows, so no version would report it).
- `MCUI` — UI-only `EnqueueDrawCommands(firstInstance, instanceCount, …)` overload, since the base
  signature is shared with `MCRaster`/`MCRaytracing`; `UIModule.WriteCommandBuffer` passes its own two
  fields instead of `0`.

**Why the ranges are not recorded inside `DFSOrder`,** which is what this plan first said: `DFSOrder`
only runs when `_orderDirty` triggers a `Resequence`, and a plain destroy takes the `CompactOrdered`
path, which shifts every dense index without re-sorting. The frame-edge walk covers both.

**Verified:** boots and screenshots identically, no stderr. `first=0, count == pool.Count` at every
step — 635 at boot, 1273 opening a second note, +1 per keystroke to 1279, back to 635 closing the tab
(the destroy + compact path). One refresh per change, never per frame. 800 one-pixel `MoveWindow`
steps: zero validation output, process alive, renders unchanged.

### Slice 2 — a second window at boot (LANDED, GUI-verified)

- `RenderWindow.CreateGpuResources()` — surface, present-support check on *that* surface, swapchain,
  then per module `BindWindow` / `CreateDescriptorSetLayout` / `PrepareObjects` / `CreateOutputImages`
  / `CreatePipeline`, then the compositor and `CreateSyncObjects`. It arrived here rather than in
  slice 4 because slice 2 is its first caller; slice 4 adds only the thread handoff around it.
- `Engine.OpenWindow(w, h, x, y)` — GLFW window, input callbacks, position, `CreateGpuResources`, and
  **only then** `windows.Add`, so nothing walking `Engine.windows` sees a half-built window. The
  callback wiring came out of `InitWindowing` into a shared private `WireInput`.
- `AGlfwWindow.SetPosition` — both windows come from the same `GraphicsSettings` and would otherwise
  land on top of each other.
- `UIModule.PrepareObjects` — `meshComponent ??= new MCUI()`. It is static and shared by decision; a
  second window would otherwise replace it, leaking its sampler and both SSBO mirrors and resetting
  `_transformCapacity`. This is the only static in `UIModule`/`CompositorModule` — everything else on
  both is per-instance, and the compositor's `Init` creates its own descriptor set layout.
- `Periodic.Main` — temporary second window with a `WindowControl` + green `PanelControl`.

**A hand-built `WindowControl` is invisible until its Z is set.** `ParseXML` is the only writer of the
window root's `-10f` Z, and `FitTo` preserves whatever Z it finds, so `new WindowControl()` sits at
Z=0 — inside the ortho near plane of `0.01f` — and the whole tree clips away. Window B rendered as a
bare clear colour until `Periodic.Main` set it, which reads exactly like a broken instance range and
is not. Slice 5 builds its windows from template XML, so it will not hit this.

**Verified:** both windows render, **each showing only its own controls** — A the note, B its green
panel. Ranges `w0 0..635`, `w1 635..637` at boot; opening a note in A moved A to 1273 and B's
`firstInstance` by exactly the same amount, with B still rendering correctly. 800 one-pixel
`MoveWindow` steps split across the two windows independently: zero validation output, process alive,
neither window disturbed by the other's resize.

**The per-window pipeline compile is 0.6ms** — measured here, which is what this slice was named as
the point to do it. Hoisting `pipeline`/`pipelineLayout` to statics per module type is **not worth
doing**; treat that idea as closed unless a module far heavier than `UIModule` appears.

**Input is globally confused with two windows until slice 3.** `Engine.HandleUI` hit-tests every
window with the one global `InputHandler.mousePos`, and `hovering` is a static, so the last window in
the list wins. `isInWindow` covers most of it — a real GLFW crossing into A clears B and A behaves
normally — but a window the pointer has never entered keeps its initial `true` and steals the hover.
Driving synthetic input therefore has to cross into the *other* window first.

### Slice 3 — input routed per window (LANDED, GUI-verified)

- `Engine.WindowFor(WindowHandle*)` — linear scan of `Engine.windows` by `os.handle`.
- `InputHandler.mousePos` / `scrollDelta` / `scrollDeltaWrite` statics **deleted**, now on
  `RenderWindow`. `ProcessMouseMove` and `ProcessScrollWheel` write into `WindowFor(window)`;
  `ActivateKeybinds` loops the windows swapping each scroll pair.
- **Only the two positional callbacks route per window**, not all five as this plan first said.
  `ProcessKeyboard` / `ProcessMouseClick` / `ProcessCharInput` feed `keyTracker` and the char queue,
  consumed inside `HandleUI(window)` — already gated to the window that owns the pointer — and
  keyboard focus follows the global `activeControl` by decision. Per-window copies would be state
  nothing reads.
- **`isInWindow` now defaults to `false`**, seeded by `AGlfwWindow.SeedIsInWindow()` from
  `GetWindowAttrib(handle, Hovered)` after the window is created and positioned. This is the slice-2
  gap: a crossing-driven flag starting `true` meant a window the pointer had never entered still
  hit-tested and stole the global `hovering`.
- `RenderWindow.Of(VulkanControl)` — walk `parent` to the root, then scan `Engine.windows` for the
  window whose `ui.uiRoot` is that root. Null for a detached subtree, so the cursor sites use `?.` and
  `WindowFrameControl.IsMaximized` guards — it is read from `Arrange`, which can run before the tree
  is attached to a window.
- Converted: `WindowFrameControl` ×7 (`Pointer` / `ScreenPos` / `IsMaximized` went from
  `private static` to instance), `TitleBarControl` ×3, `SplitterControl` ×2, `ResizeableControl` ×2,
  `DocumentEditorControl` ×2 (behind a `PointerInWindow()` helper, the way `WindowFrameControl`
  already had `Pointer()`), `WindowActions` ×2.
- **`WindowActions` are zero-argument XML-bound delegates**, so they cannot be handed a window. They
  resolve `RenderWindow.Of(UICollisionHandling.hovering)` — the chrome button that fired the action is
  the hovering control at release.
- `Engine.primary` in `Renderer` and `QueueAllocator` left alone — those genuinely mean the primary
  window (device setup, present-family probe).
- **`Window.Close` still stops the engine** rather than destroying its own window. Destroying one
  needs the render-thread teardown after `DeviceWaitIdle` that slice 4 builds; doing it here would be
  slice 4 arriving early under another name.

**Verified** with `UI.xml` parsed into *both* windows so B has real chrome: a click straight into A
from outside now works with no crossing trick (the slice-2 regression, closed). B's title-bar drag
moved B `1320,120 → 1420,220` with A untouched at `0,0 1280x720`; B's left-edge drag took it
`1420,220 600x400 → 1520,220 500x400`, far edge held; B's maximize took B alone to 1920x1080 and
restored to 500x400; B's minimize iconified B alone. Cursor shapes read back with `GetCursorInfo`:
B's edges SIZEWE / SIZENS, its corner SIZENESW, its middle ARROW, while A's splitter still reads
SIZEWE. Five scroll notches over A scrolled A; eight over B left A pixel-identical. 800 one-pixel
resizes split across the two windows: zero validation output, no stderr.

**Not slice 3, found here:** `VaultBrowserControl` opens notes through
`Engine.primary.ui.uiRoot.FindByName(...)`, so clicking a note in **B's** sidebar opens it in **A**.
That is Periodic app code, not engine chrome, and it is what the entity-registry group of top-level
controls is for; slice 5 is where tabs get a window of their own anyway.

### Slice 4 — runtime create / destroy (LANDED, GUI-verified)

- **`Engine.windows` is a `Dictionary<string, RenderWindow>` keyed by name** (user, 2026-08-19),
  published copy-on-write: a mutation builds a whole new dictionary and `Volatile.Write`s it, so the
  render thread reads the reference once and walks a map nobody will touch again. No lock, and no
  `ConcurrentQueue` handoff — this plan's earlier shape. A `Dictionary` makes the publish *more*
  necessary than a `List` did: an insert rehashes, so a concurrent enumeration is broken, not stale.
- **One main window** (user, 2026-08-19). `Engine.primary` is a plain field, registered as `"main"`
  and never removed; `Window.Close` on it ends the application, on anything else closes that window
  alone. No promotion, no empty-map case.
- `Engine.OpenWindow(name, w, h, x, y)` makes only the OS window and publishes it. `RenderSystem.Tick`
  calls `CreateGpuResources()` for any `!gpuReady` window at the top of its tick and `Draw` skips it
  until then. Three plain `volatile bool`s on `RenderWindow` carry the handshake: `gpuReady`,
  `closeRequested`, `gpuDestroyed`.
- Close: main sets `closeRequested` and destroys the subtree (`Entity.Destroy` is deferred and
  `OnDestroy` already calls `UICollisionHandling.Forget`), `HandleUI` skips the window; render does
  `DeviceWaitIdle` → `DestroyGpuResources()` → `gpuDestroyed`; main reaps after `PollEvents`,
  unpublishing and calling `AGlfwWindow.DestroyWindow()`. A window closed before it was ever built
  skips straight to `gpuDestroyed` — otherwise main would never get the go-ahead.
- Teardown added to mirror creation: `RenderingModule.DestroyGpuResources()` (command buffers,
  `moduleCommandPool`, pipeline, layout, descriptor set layouts, each `frameResources` pool, output
  images, and a new `AuroraCamera.Destroy()` for the UBOs), `UIModule` adding its `deferredDeletions`,
  `CompositorModule` its `_sampler`, and `RenderWindow.DestroyGpuResources()` closing over sync
  objects, swapchain views, swapchain and surface. **The static `MCUI` is never touched** — its
  sampler and both SSBO mirrors are shared by every window.
- `AGlfwWindow.CreateWindow(title, x, y)` is the parameterised form; `CreateWindow()` stays the
  settings-driven primary path.
- `Decorations.ExitApplication` had to stop calling `WindowActions.Close()`, which now resolves
  through `hovering` and does nothing when nothing is hovered. It closes `Engine.primary` directly.

**Verified:** F2 opens a second window at runtime, rendering its own tree, and its title-bar drag still
moves it alone (slice 3 intact). Twenty open/close cycles, every one asserted to reach two windows and
back to one: zero failures, zero validation output across the run, main window rendering unchanged
afterwards and an 800-step resize sweep on it still silent. Its own close button closed it without
touching the app; closing the **main** window with the second still open exited the process with no
stderr. Private bytes 185.1MB → 188.0MB and handles 649 → 652 across the twenty cycles, oscillating
rather than climbing — per-process Vulkan device memory is not observable from outside, so that plus
validation silence is the whole of the leak evidence, not proof.

**GLFW focuses a newly created window,** and closing it leaves focus nowhere — so a synthetic keypress
after a close/open pair goes to no window at all. Half a twenty-cycle run silently no-opped before
this was spotted. Re-focus with a real click before every synthetic key.

### Slice 5 — tear a tab off (LANDED, GUI-verified)

**The capture assumption is confirmed.** With the button held, GLFW keeps delivering cursor positions
far outside the window — measured at 1700,946 for a 1280x720 window. No polling fallback needed.

**But capture is also what broke the first design.** The window holding the capture receives *every*
mouse message, so no other window is told the pointer is over it: it gets no cursor-enter, so its
`isInWindow` never turns on, and no cursor-pos, so its tree is never hovered. Measured mid-drag from a
torn-off window over the main one:

```
window 'main'  isInWindow=False  mouse=<1500, 850>
window 'tab-1' isInWindow=True   mouse=<-250, -334>
hovering=null
```

So a drop **cannot** be resolved through `hovering` — the target control is never hovered. It is
resolved geometrically instead (user, 2026-08-19, option A): screen point from the drag's own window
(`GetWindowPos` + its `mousePos`, which capture keeps accurate) → the window whose rect holds it →
that window's design space → `UICollisionHandling.HitTest` on its tree → offer `ResolveDrop` up from
the hit. The *target control* still decides, which was the requirement; only the lookup changed.

**A pre-existing ordering defect had to be fixed first.** `SolveDrag`'s stale-release branch fired on
*every* release, not just unseen ones: it ran before the button block in `HandleUI`, and `keyTracker`
has already published the button as up by then, so it cleared `dragging` and called `StopDrag` before
`SolveLMBRelease` ever saw a live drag. `SolveLMBRelease`'s whole drag branch was dead code, and
`dragTarget.ResolveOnRelease()` had never fired for any dragged control. `SolveDrag` now runs *after*
the button block. Reach beyond tabs: any control that starts a drag gets a real release now.

- `VulkanControl.ResolveDrop(VulkanControl dropped)` — one virtual, false meaning "not mine, keep
  walking up". No interface until a second kind of drop exists (user, 2026-08-19).
- `TabStripButtonControl : ButtonControl` — carries the `TabItemControl` it stands for, because the
  drop target has no other way to learn which tab arrived, and holds the 12px threshold that separates
  a drag from a sloppy click.
- `TabViewControl` — `ResolveDrop` accepts a dragged strip button; `TearOff` builds a window from
  `[A_XSDElementProperty("TearOffDocument")]` (XML data, **not** a `Func<>` — user, 2026-08-19), sizes
  it 900x640 at the pointer and names it `tab-N`; `RemoveChild` rebuilds the strip and re-picks the
  active item, which `SetParent`'s detach never did; `SetActive` ignores an item that is no longer a
  child, since the moved button's release still arrives.
- A view emptied by a close *or* by the last tab leaving closes its window, unless it is the main one.
- **`RebuildStrip` did not need suppressing**, contrary to this plan's prediction: the drop runs from
  `SolveLMBRelease`, which clears `dragging` before any callback, and `Entity.Destroy` is deferred so
  the button stays readable for the rest of the call.

**Verified:** a plain tab click still activates and tears nothing off. Dragging a tab to empty desktop
opened a 900x640 window at the drop point with its own chrome, tab strip and the note rendering, the
source window keeping the other tab. Dragging it back into the main strip returned it and the emptied
window closed itself. Typing "zz" in the torn window and "qq" in the main one landed in the right
documents with independent carets. Ten tear-off/return cycles: zero failures, zero validation output,
private bytes 198.8→199.3MB and handles flat at 649.

**`WindowAt` resolves overlap by map order, not z-order** — GLFW publishes none. Dropping onto a point
covered by two windows picks whichever the dictionary yields first, which need not be the one on top.

### Drag preview window (LANDED, GUI-verified)

A dragged control is previewed in a small floating window that follows the pointer, so a tab is visible
during the drag rather than only appearing once it is dropped.

**Measured, since the usual answer is "no":** this GPU reports
`supportedCompositeAlpha = OpaqueBitKhr | PreMultipliedBitKhr`, so per-pixel window transparency *is*
available under Vulkan here. It was still not used — it is a creation-time hint, cannot be toggled on a
live window, and GLFW documents that a window created with framebuffer transparency may not use
whole-window opacity. `glfwSetWindowOpacity` does the job and is runtime-settable (user, 2026-08-19).

- **The preview draws the real control, not a copy** (user, 2026-08-19). Slice 1 gave every window an
  instance range and a subtree is contiguous in DFS order, so the ghost's `UIModule.rangeRoot` points at
  the dragged control, its range is that subtree's (`DataPool.DenseOf` + `CountSubtree`), and its camera
  translates the ortho box onto the control's `arrangedRect`. No cloning, no reparenting — a second view
  of the same pool rows, and it works for any draggable control.
- `UILayout.RefreshWindowRanges` skips ghosts in the running total and ranges them separately;
  `DFSOrder` skips them for free, since a ghost's `uiRoot` is null.
- `UICollisionHandling.WindowAt` **must** skip ghosts — the preview sits under the pointer by
  definition and would otherwise swallow every drop.
- One ghost per process, built on the first drag (3.8ms on the main thread, once) and hidden between
  drags. A window per gesture would put a swapchain build inside the drag.
- Sizes and default opacity are a `UISettings.DragGhost` category; `VulkanControl.draggingOpacity`
  overrides the opacity per control, negative meaning "use the setting".

**Two bugs this shook out, both wider than the preview:**

- **A module whose instance range moves has to re-record.** The range rides in the recorded command
  buffer, and nothing marked the module dirty when it changed. For ordinary windows the range only ever
  moved when the pool moved — which dirties the cursor anyway — so it never bit; a ghost's range moves
  with no pool version behind it, and the preview rendered its clear colour. `RefreshWindowRanges` now
  flags `isDirty` on any module whose range actually changed.
- **GLFW window hints are sticky.** The ghost sets `Visible=false`, `Floating`, `FocusOnShow=false`, and
  every window created afterwards inherited them — torn-off windows were being created *invisible*.
  Every `Create*Window` now calls `DefaultWindowHints()` first.

Also corrected here: the tear-off test asked "did the item move" rather than "was the drop accepted", so
dropping a tab back onto its own strip tore a window off. The accepting view now sets a flag on the
button.

**Verified:** the preview appears past the drag threshold showing that tab, semi-transparent, centred on
the pointer, and keeps following outside every window. Release hides it and the drop lands. A full
round trip — tear off outside, drag back into the main strip — leaves both tabs home and closes the
emptied window. Ten cycles: zero failures, zero validation output, private bytes 202.0→197.7MB and
handles 654→652, never more than one preview window.

## Known traps carried forward

- **Frame pacing across N FIFO swapchains on one render thread.** Serial acquire→present will settle
  with a phase offset but the two can fight. Mailbox sidesteps it. This is where "the second window is
  janky" will come from.
- **The SSBO mirrors are not double-buffered.** Already true; N windows widen the
  write-while-in-flight window, and `UIModule.UpdateModule` re-uploads the same dirty range once per
  window per frame.
- **Present-queue family is resolved once from the primary surface** in `QueueAllocator`. Same family
  on Windows in practice; a new surface should be checked, not assumed.
- **Keyboard focus vs `activeControl`.** Clicking window B moves `activeControl` into B's tree, but
  focusing A's title bar without clicking content leaves it on B. Minor; not solved.
- **The UI data/control split** (`../Decisions/ui-data-control-split.md`) is sequenced after Periodic
  v1 and will have to honour a multi-rooted tree.

## Verification recipes that work

- **Resize sweep:** `MoveWindow`, **not** `SetWindowPos` — the latter produced a malformed `WM_SIZE`
  with a `0xFFFF` HIWORD and a bogus 900x65535 swapchain request (see
  `../Decisions/window-frame-resize.md`). 800 one-pixel steps is enough to shake out extent drift.
- **Validation output goes to stdout** via `Renderer.DebugCallback`, so redirect it and diff the line
  count around a probe.
- **Cursor shape:** `GetCursorInfo` reads the applied handle; `CopyFromScreen` does not capture the
  pointer, which is why the cursor shape sat unverified for two decision notes.
- **Synthetic input:** enter the window from outside so GLFW sees a real crossing, and warm the pointer
  inside before driving a drag — `Engine.HandleUI` returns early while the pointer is outside, so a
  drag started out there silently does nothing.
- **Screenshot the window rect, not a fixed screen region**, and front the window first. A probe saying
  every rect and colour is correct has already hidden a blank white window once
  (`../Mistakes/verify-what-the-user-sees.md`).
