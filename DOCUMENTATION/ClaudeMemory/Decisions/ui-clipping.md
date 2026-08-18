# Decision — the clip rect rides in the control's pool row and the fragment shader discards against it

**Date:** 2026-08-18
**Status:** LANDED. **GUI-verified** — a scrolled document is cut mid-glyph at the viewport edge, and
a glyph scrolled under the title bar no longer wins the hit-test there.
**Scope:** `ArctisAurora.Core.UISystem.Controls` (`VulkanControl`, `WindowControl`),
`ArctisAurora.Core.UISystem` (`UICollisionHandling`), `Shaders/UIRasterizer/UI.vert` + `UI.frag` in
all three host projects.

## Decisions

### 1. Shader-side clip, because the whole UI is one instanced draw

`MCUI.EnqueueDrawCommands` issues a single `CmdDrawIndexed` with `instanceCount = live control
count`. A per-control `vkCmdSetScissor` would mean one draw call per control — the glyph count alone
makes that untenable, and it would destroy the instancing the pool exists to feed.

So the clip travels as data: `ControlData` gains a `Vector4D<float> clip` holding
`(left, top, right, bottom)` in design space, `UI.vert` forwards the *pre-projection* position
alongside it, and `UI.frag` discards fragments outside. One buffer, one draw, no state changes.

The position handed to the fragment stage is `tPos.xy` — the vertex after the model transform but
before `proj * view`. That is the same space `arrangedRect` and `ClipRect` are computed in, so the
comparison needs no conversion and stays correct under every `WindowingMode`.

**Rejected: a second pipeline or a stencil pass.** Both cost more than a compare-and-discard for a
rectangle that is already known on the CPU.

### 2. `ControlData` is 48 → 64 bytes, and the GLSL struct must move with it

`scalar` block layout gives the GLSL struct byte-for-byte agreement with the `Pack = 1` C# one:
`vec2[4]` 32, `Style` 12 at 32, `uint` 4 at 44, `vec4 clip` 16 at 48. Any drift and every control
past the first reads shifted data.

The three `UI.vert`/`UI.frag` copies (`AuroraEngine`, `AuroraEditor`, `Periodic`) had **already
drifted** before this change — Periodic's fragment shader is MTSDF, the other two are the older
MSDF. The clip was applied to each separately and the drift was deliberately left alone; merging it
is a rendering decision, not a clipping one. All three were recompiled with `glslc`.

### 3. The clip is written by the `ClipRect` setter, not by each `Arrange`

Eight controls override `Arrange` and each already assigns `ClipRect`. Making the property's setter
mirror into `controlData` means none of them changed, and no future override can forget to publish.

The cost is that every arrange marks the control's pool row content-dirty, widening the range MCUI
sub-uploads. A layout pass already rewrites the transform column for the same controls, so the range
is the one being copied anyway.

### 4. The default is unbounded, not empty

`ClipRect` and the constructor both start at `LayoutRect.Infinite`. `Empty` was the old default and
is the wrong one now that something consumes it: a control between construction and its first
`Arrange` would discard every fragment and fail every hit-test. Unbounded means an unarranged
control behaves exactly as it did before clipping existed, and `Arrange` only ever narrows.

The initializer sits in the constructor body rather than on the property, because a property
initializer runs before the `Entity` base constructor has acquired `dataHandle` and the setter
touches `controlData`.

### 5. `WindowControl.Arrange` never set a clip rect, so the whole tree inherited nothing

Found while wiring this up, and it is why clipping read as merely "unimplemented": `ClipRect` is
inherited from the parent unless a control opts into `clipOutOfBounds`, the root is the top of that
chain, and `WindowControl.Arrange` was the **only** override that never assigned one. Every
`ClipRect` in the tree was therefore `Empty`, and `Intersect(rect, Empty)` is `Empty`, so even the
controls that did clip clipped to nothing.

One line. Nothing observable changed before this slice because nothing read the value.

### 6. The hit-test rejects the whole subtree, not just the control

`VulkanControl.HitTest` existed with **zero callers**. `FindDeepestValid` now returns `null` when the
point is outside `current.ClipRect`, before the quad test. Since the clip rect is inherited, one
check at a container excludes everything under it — a row scrolled past the top of its viewport is
drawn nowhere and is now clickable nowhere.

Placed before `SolvePositions` rather than after: the clip test is a rect compare and the quad test
builds and transforms four vertices.

## Verified

- Builds clean; all three shader pairs compile with `glslc`.
- **Screenshotted:** a 40-section note scrolled 5 wheel ticks shows the top paragraph sliced
  horizontally through the glyphs at the viewport's top edge, with the title bar intact above it.
  Before this, that text drew over the title bar.
- **Hit-test proven by dragging:** press at a point inside the title bar where a scrolled-off
  glyph's quad still overlaps, drag 60,50 — the window moved exactly 60,50, so the title bar won the
  hit-test rather than the glyph.
- No regression: vault rows still open notes, maximize still refits the tree, nothing vanished at
  either 1280x720 or maximized 1920x1080.

## Still open

- `UI.frag` discards before it does anything else, but it still MSDF-decodes every surviving
  fragment including plain panels sampling the `invisible` mask. Unchanged by this slice.
- Clipping is rectangular and axis-aligned. `TransformToWorld` already has the rotation path
  commented out; a rotated control would clip against its unrotated bounds.

## Retracted — the "swapchain crash on resize" was a test artifact

An earlier revision of this note recorded that resizing the window kills the render thread with
`Failed to create swapchain ErrorOutOfDeviceMemory`. **That is wrong and the claim is withdrawn.**

The crash was real but caused by the harness, not the engine: driving the resize with a raw
`SetWindowPos(hwnd, NULL, 0, 0, w, h, SWP_NOZORDER | SWP_NOACTIVATE)` from another process produced
a malformed `WM_SIZE` whose 16-bit `HIWORD` was `0xFFFF`. GLFW handed the window-size callback
`900x65535`, `glfwGetFramebufferSize` and `vkGetPhysicalDeviceSurfaceCapabilitiesKHR` both agreed,
and a 900x65535 swapchain genuinely does exhaust device memory — the driver's error was honest.

`MoveWindow` and GLFW's own `ShowWindow(SW_MAXIMIZE)` never reproduce it. Seven consecutive
`MoveWindow` resizes, growing and shrinking, all survive on an unmodified build. See
[[window-frame-resize]].

**Rule taken from this:** when synthetic input produces a crash, reproduce it through a second,
independent input path before calling it an engine defect.

Related: [[vault-browser-and-shell]], [[window-chrome-and-label]], [[window-scaling-modes]],
[[document-selection]]
