# Decision — a window owns its swapchain, sync and modules; the renderer keeps only the device

**Date:** 2026-08-19
**Status:** LANDED (multi-windowing slice 0 of 5). **GUI-verified** — boot, resize sweep, reflow at
three sizes, maximize/restore, edge drag, close.
**Scope:** `ArctisAurora.EngineWork.Rendering` (`RenderWindow` **new**, `Renderer`, `AGlfwWindow`,
`AuroraCamera`), `...Rendering.Modules` (`RenderingModule`, `UIModule`, `CompositorModule`),
`ArctisAurora.EngineWork` (`Engine`), `ArctisAurora.Core.Threading` (`RenderSystem`),
`ArctisAurora.Core.Registry` (`EntityRegistry`), `ArctisAurora.Core.UISystem` (`UICollisionHandling`,
`WindowControl`, `WindowFrameControl`, `TitleBarControl`, `WindowActions`, `SplitterControl`,
`ResizeableControl`), `Periodic`, `AuroraEditor`.

Groundwork for tearing a Periodic tab off into its own OS window. Nothing user-visible changes: this
slice only moves the per-window state off statics so a second window becomes possible.

## Decisions

### 1. `RenderWindow` is the unit, and `Renderer` keeps only what the device owns

One `RenderWindow` per OS window, holding its `AGlfwWindow`, its swapchain (handle, extension, extent,
format, images, views, image count), its frame sync (`imageAvailable` / `renderFinished` /
`modulesFinished` / timeline / `frameCounter` / `currentFrame`), its `RenderingModule[]` and
`CompositorModule`, and its `isInWindow`. It held `uiRoot` too until slice 1 moved that onto the UI
module — a window is a surface with modules, and the tree belongs to the module that draws it. The
window keeps a typed `ui` handle to the `UIModule` it builds, so callers reach the tree as
`window.ui.uiRoot`.

`Renderer` keeps `vk`, `instance`, `gpu`, `logicalDevice`, `queueAllocator`, the three queues and the
two command pools — the things there is genuinely one of per process. `Draw()` and
`RecreateSwapchain()` now take the window they act on; `Engine.windows` is the list and
`Engine.primary` is the one the application boots into.

**Modules are per-window instances, not one module serving N windows.** Every size-dependent field on
`RenderingModule` (`outputImages`, `commandBuffers`, `isDirty`, `camera`, `frameResources`) was already
an instance field, so this is nearly a no-op diff. It costs one pipeline compile per window opened;
hoisting `pipeline`/`pipelineLayout` to statics per module type is the fix if that hitch is ever
measurable — and it is the "same every time, so cache it" case, which does not pay for itself until
there is more than one window.

**A window builds its own modules in its constructor.** No factory: per-object work belongs in the
constructor (user, 2026-08-19), so `RenderWindow`'s ctor news its `RenderingModule[]` and
`new RenderWindow(w, h)` is complete on its own — which is what slice 4 wants anyway.
`Renderer.PreInitialize` **keeps** its name and its `Bootstrap.xml` slot; it now does the prerequisite
work that step is named for, collecting the device feature set from the windows' modules, which
`CreateLogicalDevice` then just uses. A first pass deleted the step outright and was rejected — it
predates this work and was not mine to remove.

**Rejected: one module holding per-window resource blocks.** It genuinely saves the pipeline and the
descriptor pool, and it is what shared state (the texture table, the two SSBO mirrors) argues for —
but it restructures the base class every module type inherits, for a saving with no measurement
behind it.

### 2. The swapchain image count is read, not assumed

`Renderer.swapchainImageCount = 3` is gone. `CreateSwapchain` reads the count the driver actually
handed back into `RenderWindow.imageCount`, and every per-image array sizes off it:
`renderFinishedSemaphores`, `modulesFinishedSemaphores`, `RenderingModule.isDirty`, `commandBuffers`,
`outputImages`, `UIModule`'s `deferredDeletions` / `frameResources` / `_frameBuiltCapacity` /
`_frameTableVersion` / `_cursors`, and `AuroraCamera`'s UBO array.

This retires the index-out-of-range recorded in [[swapchain-extent-is-the-truth]] — `CreateSwapchain`
asks for `MinImageCount + 1` and read the real count into a local, so `swapchainImages` could be longer
than every array sized off the constant.

`RenderingModule.BindWindow(window)` joins a module to its window and sizes those arrays;
`RebindImageCount(window)` is the part that re-runs when the count itself changes, and `UIModule`
overrides it because its own per-image arrays are not on the base. A module is constructed before its
window has a swapchain (`Renderer.PreInitialize` runs before `Renderer.Initialize`), so **nothing
indexed by swapchain image may be sized in a module constructor** — `UIModule`'s constructor is now
empty for exactly this reason.

A present-mode change can hand back a different count, so `RecreateSwapchain` compares against the
previous one and rebuilds the sync objects and the per-image arrays when it moves. Command buffers are
freed through an overridable `commandBufferPool` — the compositor's come from
`Renderer.compositeCommandPool`, every other module's from its own `moduleCommandPool`.

### 3. One pointer means one hover, one drag, one active control

`UICollisionHandling` stays a singleton and `hovering` / `dragging` / `activeControl` stay static.
There is one mouse, so per-window copies would be wrong, not merely wasteful — and keyboard focus
following the active control is inherently global.

What is per window is *which tree to hit-test*: `SolveHover(mousePos)` became
`SolveHover(mousePos, root)`, and `Engine.HandleUI(window)` passes `window.ui.uiRoot`. `isInWindow` moved
onto `RenderWindow` and is written by the window's own cursor-enter callback, so
`UICollisionHandling.IsInWindow` is **deleted**.

### 4. `EntityRegistry.uiTree` is deleted rather than aliased to the primary window

It was the single global root, and keeping it as an alias would leave a landmine: a second window's
tree would not be reachable through the property everything already reads. Four app-side call sites
moved to the window's root (`Periodic.Main`, `VaultBrowserControl` ×2, `AuroraEditor.Editor`) and
five engine ones went to the window they belong to. Slice 1 re-pointed all nine at `window.ui.uiRoot`.

The `uiRoot` setter calls `FitTo(os.windowSize)`, which is what the old property's setter did.
`RenderWindow`, `RenderWindow.ui`, `UIModule.uiRoot` and `Engine.windows`/`primary` are **public**
because there is no `InternalsVisibleTo` and Periodic is a separate assembly — `uiTree` was public
for the same reason.

`WindowControl.ToDesignSpace` went from `public static` to an instance method taking the window
extent, with a forwarder so call sites stay one expression. That forwarder followed the root onto
`UIModule` in slice 1 — it is the tree's projection, not the surface's.

### 5. `AuroraCamera` holds its module instead of indexing a global array

`UpdateCameraMatrix(extent, image, cameraIndex)` switched on `Renderer.renderingModules[cameraIndex]`,
and that array no longer exists. The camera now takes its owning `RenderingModule` and reads
`_owner.rendererType`; the `cameraIndex` parameter is gone and the UI branch reads the owning module's
root rather than the global tree — since slice 1 that is `((UIModule)_owner).uiRoot`, with no hop
through the window at all.

A parameterless constructor stays for the four dead renderer types (`Rasterizer`, `Pathtracing`,
`RadianceCascades2D`, `UIRenderer`), which build a camera with no module behind it. It hardcodes three
images, which is what they always assumed; it is confined to that dead path.

`Entity.CreateComponent` asked the same question globally, so `Renderer.PrimaryRendererType` answers it
in one place.

### 6. `RecreateSwapchain` no longer queries GLFW off the main thread

It called `AGlfwWindow.UpdateWindowSize`, which is `glfwGetFramebufferSize`, from the **render** thread.
GLFW documents its window queries as main-thread only, and [[window-frame-resize]] recorded this as a
real latent violation left unfixed. It now reads the size the main thread's resize callback published
into `os.windowSize`, which is the only writer.

## Deliberately not done in this slice

- **Per-window instance ranges.** `MCUI` still draws `0 .. pool.Count`, so a second window would draw
  every window's controls. That is slice 1, since landed: `UILayout.RefreshWindowRanges` publishing
  `(first, count)` onto each `UIModule` and `CmdDrawIndexed`'s existing `firstInstance` parameter
  carrying it. No shader change is needed — `gl_InstanceIndex` includes `firstInstance`.
- **`RenderWindow.CreateGpuResources()` and a parameterised `CreateWindow`.** Both were named in the
  agreed plan and both were deferred: the primary window's GPU setup is *interleaved with asset
  loading* across five `Bootstrap.xml` steps (`Renderer.Initialize` → `AssetRegistries.*` →
  `SetupObjects` → `PrepareDescriptors` → `SetupPipelines` → `CreateSyncObjects`), so it cannot be one
  call, and a secondary window had no second caller yet. Slice 2 turned out to be that caller and
  landed `CreateGpuResources()` there; slice 4 added the parameterised `CreateWindow(title, x, y)`
  and moved `CreateGpuResources()` onto the render thread. The staged steps now operate on
  `Engine.primary` and `Bootstrap.xml` is unchanged.
- **Chrome resolving its own window.** `WindowActions`, `TitleBarControl`, `WindowFrameControl`,
  `SplitterControl` and `ResizeableControl` point at `Engine.primary` rather than the static handle.
  Slice 3 swapped that for `RenderWindow.Of(control)`, except `WindowActions`, whose zero-argument
  XML-bound delegates resolve through `RenderWindow.Of(UICollisionHandling.hovering)` instead.
- **The SSBO mirrors are not double-buffered.** Already true; N windows widen the write-while-in-flight
  window, and `UIModule.UpdateModule` will re-upload the same dirty range once per window per frame.

## Landed alongside — the GLFW cursor leak

`ChangeCursor` called `CreateStandardCursor` on every invocation and destroyed none, so every hover
transition over a splitter or a window edge leaked one GLFW cursor. It is now a process-wide
`Dictionary<CursorShape, IntPtr>` filled once per shape: GLFW creates cursors against the library and
only applies them per window, so the cache is static while `SetCursor` stays on the instance. The leak
was first recorded in [[splitter-and-pane-sizing]] and left alone there.

**The cursor shape is verified for the first time.** Both that note and [[window-frame-resize]] list it
as unverified because `CopyFromScreen` does not capture the pointer — `GetCursorInfo` does, and reading
the applied handle back is how these checks should be done from now on. Measured: the splitter reads
`IDC_SIZEWE` at x=221..224 and `IDC_ARROW` at 220 and 225, which is the 5px grip with its boundary
pixels going to the neighbouring panes exactly as that note's trap predicts; the four window edges give
WE / NS and the corners NWSE / NESW; all four still correct after 200 forced transitions.

Note the handle count is **not** evidence either way — Win32 standard cursors are shared `LoadCursorW`
handles, so the old leak was GLFW's own per-call allocation and never showed in process handles. The
proof is that at most one cursor per shape is now created.

## Verified

- Builds clean, 0 errors, both `Periodic` and `AuroraEditor`.
- Boots: all four asset stages, all three threads, **no stderr**, and the shell screenshots identically
  to before — title bar, chrome, sidebar, tab strip with its close x, and the note laid out.
- **800 one-pixel `MoveWindow` resize steps** (shrink then grow, both axes): zero validation output,
  process alive. `MoveWindow` rather than `SetWindowPos`, per the malformed-`WM_SIZE` finding in
  [[window-frame-resize]].
- **Reflow at three sizes, screenshotted:** 1280x720, 760x520 (document rewraps to the narrower
  column), and maximized 1920x1080 (rewraps wide). Proves `FitTo`, the swapchain recreate and the
  per-window camera projection all still track.
- **Maximize → 1920x1080 and restore → 760x520** via synthetic clicks on the chrome buttons.
- **Right resize edge dragged 200px inward:** 760x520 → 560x520 with the origin held, exactly the
  distance dragged.
- **Close button exits the process** with no stderr.

Related: [[swapchain-extent-is-the-truth]], [[window-frame-resize]], [[dynamic-rendering]],
[[window-scaling-modes]], [[tab-view-control]], [[ui-data-control-split]]
