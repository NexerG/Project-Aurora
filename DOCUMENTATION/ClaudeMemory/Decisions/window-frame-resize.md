# Decision — the resize border is a control's padding, not a special case in the engine

**Date:** 2026-08-18
**Status:** LANDED. **GUI-verified** — every edge, a corner, the clamp and the maximized no-op.
**Scope:** `ArctisAurora.Core.UISystem.Controls` (`WindowFrameControl`),
`Periodic/Data/XML/Documents/UI.xml`.

## Decisions

### 1. Four invisible edge strips ahead of the content, not padding

`WindowFrameControl` wraps the shell, and the shell is arranged to the frame's **full rect** — no
inset, no visible border, the layout is byte-for-byte what it was before the frame existed. Four
4px strips overlay the outermost pixels and are `children.Insert`ed ahead of the content, so
`FindDeepestValid` — which walks children forward and takes the first match — reaches them first.
They are masked `invisible` and sit behind the content in paint order, so they are hit zones and
nothing else. They carry no logic: each forwards hover/click/drag to the frame, which owns the
resize.

**First attempt used the frame's `padding` as the ring**, which put the ring in the layout: the
shell was inset 6px on every side and the window grew a visible border. Rejected by the user
(2026-08-18) — the edge should count for ~4px *without* the window having an outline to resize from.
Padding cannot express "hit here but do not consume space"; a sibling ahead in the list can.

**Also rejected: a border band checked in `UICollisionHandling` before hit-testing the tree.** No
strips and no XML change, but it puts window-chrome knowledge in the engine's hover path, where
every application inherits it whether or not its window is undecorated. Chrome is the application's
business — the window has always been created `Decorated=false` and draws its own title bar out of
ordinary controls, so its resize edge should be ordinary controls too.

Consequence to accept: the outer 4px of whatever the strips cover belongs to the resize, so the very
top of the title bar resizes rather than moves, and the leftmost 4px of the sidebar does not click a
row. That is how a real window border behaves.

Corners need no special geometry. The left and right strips run the full height and the top and
bottom strips the full width, so they overlap at the corners; whichever wins asks the frame for
`EdgesAt`, which recomputes all four booleans from the window rect regardless.

### 2. The drag arithmetic is in screen space

Grab records the window's position and size plus the pointer in screen coordinates
(`windowPos + InputHandler.mousePos`), and each tick recomputes the rect from the current pointer.
Window-relative coordinates move underneath a resize — dragging the left edge changes the origin, so
the same screen point has a different window-relative x one tick later, and per-tick deltas would
compound that error.

Raw window pixels, not design space, for the same reason `TitleBarControl` uses them: the window
moves in screen units. Edge *detection* stays in design space, because it compares the pointer
against `arrangedRect` and both are design-space.

### 3. An edge past its minimum stops the far edge

Dragging the left edge past the 320px minimum pins the width and stops moving the origin, rather
than letting the origin keep travelling and dragging the window along. `x = grabX + (grabW - w)`
falls out of the clamped width, so the two can never disagree.

Minimums are constants (320x240), not properties. `Thickness` still has no `TypeConverter` so the
padding could not be authored anyway, and nothing has asked for a configurable one.

### 4. Maximized turns the strips off rather than ignoring their clicks

`Arrange` sets `hitTestable = !IsMaximized` on all four. A maximized window has no meaningful edge to
drag, and an untestable strip is skipped by `FindDeepestValid` outright, so those 4px go back to
whatever is underneath instead of being swallowed by a grip that would decline to act. Refusing the
work inside the click handler would have left the pixels dead.

Restoring on drag, the way the title bar arguably should, is deliberately not done here — that is a
separate open item about the title bar.

### 5. The cursor changes only when the shape changes

The shape depends on *where* in the ring the pointer is, so enter/exit is not enough — but
`AGlfwWindow.ChangeCursor` allocates a GLFW cursor per call and frees none, and the hover callback
runs every tick. Tracking the last shape bounds it to actual transitions.

Same untouched leak recorded in [[splitter-and-pane-sizing]]: `ChangeCursor` had no live callers
before these two controls, and a shape → cursor dictionary is the fix.

## The crash that was not

Window resize was reported blocked earlier in this session by
`Failed to create swapchain ErrorOutOfDeviceMemory` out of `RecreateSwapchain`, and that report was
**wrong**. Driving the resize from another process with
`SetWindowPos(hwnd, NULL, 0, 0, w, h, SWP_NOZORDER | SWP_NOACTIVATE)` produced a malformed `WM_SIZE`
whose 16-bit `HIWORD` was `0xFFFF`; GLFW handed the callback `900x65535` and Vulkan's
`currentExtent` agreed, so the driver was being asked for a 900x65535 swapchain and honestly ran out
of memory.

`MoveWindow` never reproduces it, nor does `ShowWindow(SW_MAXIMIZE)`, nor — as this slice proves —
`glfwSetWindowSize` driven by a real pointer drag. Two speculative renderer changes made against the
false diagnosis (skipping the render-thread `UpdateWindowSize`, clamping `ImageExtent` to the surface
caps) were **reverted**; the renderer is untouched by this work.

`RecreateSwapchain` does still call `glfwGetFramebufferSize` from the render thread, and GLFW
documents its window queries as main-thread only. That is a real latent threading violation, but it
is not what caused this crash and it is not fixed here.

## Verified

- Builds clean. Nine resizes across the session, growing and shrinking, no crash.
- **Measured by window rect, not eyeballed:** right edge 1280x720 → 1003x720 with the origin held;
  left edge 0,0 1003 → 200,0 803, so the origin moved by exactly the width lost; bottom edge
  720 → 503; top-left corner 200,0 803x503 → **497,197 506x306**, matching the prediction exactly;
  right edge dragged 450px inward from 506 stopped at **320**, the minimum, not 56.
- Maximized: dragging the right edge 417px left changed nothing.
- **No regression:** the title-bar drag still moves the window (100,100 in, 100,100 out) now that the
  frame is its parent, and the shell renders correctly at 1280x720, 506x306 and maximized 1920x1080.

### After the rework to edge strips

- **Screenshotted:** the shell is flush to all four edges again — title bar at y=0, sidebar at x=0,
  close button against the right — indistinguishable from before the frame existed.
- Resize still works from inside the 4px band: right edge at x=1278 took 1280 → 1002; the
  bottom-right corner took 1002x720 → 802x602 on both axes at once.
- **Content just inside the band is untouched:** a title-bar drag at y=12 moved the window 100,100
  without resizing, and a sidebar row 52px in still opened its note.
- Maximized: right-edge drag changed nothing, now because the strips are not hit-testable.

## Trap for later

`Engine.HandleUI` returns immediately while `UICollisionHandling.isInWindow` is false, so **synthetic
input that parks the pointer outside the window makes the next drag silently do nothing.** Two
resize tests read as failures for this reason before the pointer was warmed inside the window first.
Move the cursor into the window and let a tick pass before driving any test drag.

Related: [[window-chrome-and-label]], [[splitter-and-pane-sizing]], [[ui-clipping]],
[[window-scaling-modes]]
