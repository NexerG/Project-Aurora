# Mistake — the render thread may not read a new-stack control's layout, because it is a pool row

**What I did wrong (2026-09-12):** landing 6b3's drag ghost framed its camera on the dragged control by reading
`rangeRoot.arrangedRect` inside `AuroraCamera.UpdateCameraMatrix`. The old stack's `UIModule` did exactly this
and was fine. The first tab drag killed Thorium:

```
System.Exception: [DataPool] 'UIElements' is owned by 'Main' but GetRef was called from system 'Render'.
   at DataPool.AssertOwner
   at Control.get_arrange() → Control.get_arrangedRect()
   at AuroraCamera.UpdateCameraMatrix ← UIEngineModule.UpdateFrameData ← Renderer.Draw
```

**Why it happens:** a `Core.UI.Control`'s layout state is its `UIElements` row — `Control.arrange` is
`Pool.GetRef<ArrangeData>(dataHandle)` — and `DataPool` asserts that the calling system owns the pool. So every
`ArrangeData`-backed member is a pool read: `arrangedRect`, `DesiredSize`, `hidden`, `preferredWidth`/`Height`,
margins, alignment. The old `VulkanControl.arrangedRect` was a plain field, which is why the ported line never
asserted there.

**Since 2026-09-23** the check is `DataPool.AssertAccess` ([[frame-scheduler]]): a Dedicated thread may call the read entry points (`Backing`, `CopyTo`, `CopyRange`, `OwnerAt`) but never a write one, and `GetRef` is a write one — so this still throws.

**The rule:** anything the render thread needs from a control is copied into a plain field on the module, on the
main thread. The ghost's box is `UIEngineModule.rangeRect`, built in `DragGhost.Show` before the ghost is
shown and cleared in `Hide`; the camera reads only that. `visual` is a plain field; there is no `geometry` since
2026-09-16. The render thread touches the `UIQuads` pool only through `Backing<T>()` and the module's published
range — [[ui-quads-pool]].

**Still live:** the camera's own `WindowRoot.ViewportSize(_extent)` call reads `preferredWidth`/`Height` past
its `!autoscaling` short-circuit. Safe while every window has `Autoscaling="false"`; the first autoscaling window
asserts the same way.

Related: [[ui-engine-stack]], [[ui-draw-list]], [[ecs-rework-data-pools]]
