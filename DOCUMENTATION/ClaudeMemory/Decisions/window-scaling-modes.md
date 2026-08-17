# Decision — a window root has a windowing mode, and the ortho box is what changes

**Date:** 2026-08-15
**Status:** LANDED (2026-08-15). All three modes verified by screenshot at 1920x1080 borderless.
**Scope:** `ArctisAurora.Core.UISystem.Controls` (`WindowControl`), `ArctisAurora.EngineWork.Rendering`
(`AuroraCamera.UpdateCameraMatrix`, `AGlfwWindow.WindwoResizeCallback`), `ArctisAurora.Core.Registry`
(`EntityRegistry.uiTree`), `Engine.HandleUI`, `DocumentEditorControl.ResolveOnClick`,
`Periodic`/`AuroraEditor` `UI.xml`.

## The bug

Borderless at 1920x1080 left every control inside the 1280x720 the XML declared. Three separate
causes, all of them "nothing tells the UI the window changed":

| Piece | State before |
|-------|--------------|
| `frameBufferResized` | reached `RecreateSwapchain` only — never `UILayout` |
| root `arrangedRect` | set once by `ParseXML` from the XML `Width`/`Height`, never again |
| `WindowControl.fillWindow`, `contentScalingMode` | declared, XSD-annotated, **read by nothing** |
| UI ortho projection | already `(0, window.Width, 0, window.Height)` — the one piece that did track |

## Decisions

### 1. Three modes on the root, not a render setting (user, 2026-08-15)

`WindowControl.windowingMode` — `KeepLocal` / `ScaleUp` / `WindowSize`, default **`WindowSize`**.
It belongs on the control because it is a property of the UI tree, not of the display: a game HUD
and a note editor want different answers on the same monitor. `fillWindow` is deleted — it was the
same question as a bool, and both `UI.xml` files now say `WindowingMode` instead of `Fill`.

### 2. One formula: the mode picks the box, and everything follows from it

`ViewportSize(window)` returns the box in design units, and it is used **twice** — as the ortho
projection (render thread) and as the root's arranged rect (main thread). That is the whole design.

| Mode | Box | Root rect | Effect |
|------|-----|-----------|--------|
| `KeepLocal` | window pixels | untouched, stays the XML size | old behaviour, now deliberate |
| `WindowSize` | window pixels | the box | 1:1 pixels, controls reach the real edges, text rewraps |
| `ScaleUp` | window / scale | the box | everything grows; anchors still reach the real edges |

`ScaleUp`'s scale comes from `contentScalingMode`, which is why that enum survived: `Vertical` makes
the design *height* authoritative and derives the width from the aspect, so pixels stay square and
**an ultrawide shows more, rather than stretching** — the user's stated requirement. `Horizontal`
mirrors it, `Both` stretches the design box over the window and distorts, `None` degenerates to
`WindowSize`.

- **`ScaleUp` costs nothing at layout time.** Changing what the ortho box is means the GPU does the
  scaling; no re-measure, no rewrap, and MSDF glyphs stay sharp at any factor. `WindowSize` is the
  expensive one — it rewraps every text run on every resize.
- Rejected: **a scale matrix at the root transform.** The projection already exists per module and
  is read every frame; a second scale would have to be composed into every control's mat4.
- Rejected: **letterboxing `ScaleUp` to the design box.** A 21:9 would get bars. Anchored controls
  reaching the true window edge is the whole point of the mode for ultrawide.

### 3. `Measure` on the root had to be overridden, and this is the part that is easy to miss

`VulkanControl.Measure` caps at `preferredWidth`/`preferredHeight`, so a root carrying the XML's
1280 measured its children at 1280 **no matter what rect it was arranged into**. The first
implementation fixed the arranged rect and changed nothing on screen for exactly this reason —
the background filled, the text still wrapped at 1280.

`WindowControl.Measure` therefore measures children at the box it was fitted to. The XML
`Width`/`Height` stay the *design* size that `ViewportSize` reads; they are not a cap the tree
inherits. `KeepLocal` still defers to the base, since its whole meaning is that the XML size wins.

- Rejected: **overwriting `preferredWidth` in `FitTo`.** It is the design size `ScaleUp` divides by,
  so the scale factor would drift on every resize, and a save would write the runtime size.
- **`preferredWidth`/`preferredHeight` default to 0 meaning "auto", while the older
  `width`/`height` still default to 72.** Every `Measure` override reads the preferred pair as
  auto-at-zero; when they too defaulted to 72 that was a legal explicit size, so "unset" and "72px"
  were indistinguishable. The two pairs coexisting is the confusing part of the file, not a bug.

### 4. Mouse coordinates are converted, not left to coincide

`InputHandler.mousePos` is raw window pixels. In `ScaleUp` those stop being layout units, so
`WindowControl.ToDesignSpace` scales them and both call sites — `Engine.HandleUI` and
`DocumentEditorControl.ResolveOnClick` — go through it. It is the identity in the other two modes,
so nothing changes unless the content actually scales. See [[periodic-editor-architecture]].

### 5. Where the fit is triggered

- `AGlfwWindow.WindwoResizeCallback` → `FitTo`. GLFW callbacks run inside `PollEvents`, which is the
  first thing `MainTick` does, and `UILayout.ResolveLayout` runs later in that same tick — so a
  resize is absorbed by the tick it arrived in.
- `EntityRegistry.uiTree`'s setter → `FitTo`, because the tree is assigned *after* `Engine.Init`
  built the window, and a borderless app is already 1920x1080 by the time the XML is parsed.

## Verified

Screenshots at 1920x1080 borderless, `Periodic` with `SampleNote.xml`:

- `WindowSize` — the long paragraph occupies one full-width line instead of wrapping at 1280.
- `KeepLocal` — identical to the pre-change build, wrapping at 1280.
- `ScaleUp` + `Vertical` — everything 1.5x (1080/720), glyphs still crisp, layout identical in shape.
  On this 16:9 monitor the box works out to exactly 1280x720; an ultrawide is what would widen it.

Not verified: a **live resize** (drag the window edge). Only startup sizing was exercised, and the
resize path is a different entry point into the same `FitTo`.

## Still open

- Nothing re-fits when the *mode* changes at runtime; it is read wherever it is read, per frame for
  the projection and per resize for the layout, so a live toggle would need a `FitTo` call.
- `ScaleUp` reads the root's mode and design size from the render thread while main may be writing
  them in `FitTo`. Two floats and an enum, torn only during a resize, consistent by the next frame —
  in line with the lock-free free-running systems, but it is unsynchronised.
- Clipping still does not exist, so a control larger than the box is not cut at the edge.

Related: [[settings-registry]], [[text-layout-one-measurer]], [[periodic-editor-architecture]]
