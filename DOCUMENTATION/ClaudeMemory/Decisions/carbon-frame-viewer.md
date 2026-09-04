# Decision — Carbon is a fourth application, and the frame file is read where it is written

**Date:** 2026-09-02
**Status:** LANDED, v1. Builds clean. **GUI-verified** — booted, a session loaded, all four views
drawn, zoom and pan exercised, every number cross-checked against the file that produced it. Verified
against a synthetic three-thread capture from the real writer **and against two real Thorium
captures** (300 frames of `Main`/`Render`/`Physics`, F9, clean exit).
**Scope:** new `Carbon/` project; `ArctisAurora.Core.Diagnostics.FrameCaptureReader`;
`Thorium/Data/XML/Documents/Inputs/InputMap.inputs.xml`.

## What was there before

`FrameSpool` wrote `Profiling/<session>/<thread>.frames.xml` and **nothing read them**. No capture
had ever been produced by a real application either — `Mode` defaults to `Off` and nothing bound
`Profiling.Capture`. See [[engine-profiling]] for the writer.

## Decisions

### 1. A fourth application, not a Thorium panel (user, 2026-09-02)

The roadmap said the viewer was a panel on the test/profiling platform, sequenced after Thorium v1.
It is instead **`Carbon`**, a host application beside `Thorium` and `AuroraEditor`: same engine, same
UI, its own window. Dogfoods the UI exactly as the roadmap item wanted, without waiting for Thorium
v1 and without putting a profiler pane inside a note editor.

**Rejected: a standalone HTML viewer.** Canvas gives zoom, pan and hover free, and it would have
validated the XML before the format was baked into an engine panel. Rejected because it is a second
UI stack in a repo that deliberately owns its own, and it would be thrown away when the platform
lands.

**Rejected: a C# tool emitting a static report.** Stays in C#, but loses hover, zoom and pan, which
is most of the value once a capture is 240 frames deep.

### 2. The reader is engine-side, the drawing is Carbon-side (user, 2026-09-02)

`FrameCaptureReader` sits in `Core/Diagnostics` next to `FrameSpool` — one format, one place, and
`WriteFrame` read backwards is the whole implementation. The four controls stay in
`Carbon/Editor/CustomControls/`, unlike the planned `LogViewControl` which [[log-viewer-plan]] puts
in the engine. Nothing else wants them yet; promoting one later is a namespace move.

### 3. Truncation is an outcome, not an error

A file whose process died has no `</FrameCapture>`, so `XmlReader` throws at EOF. That is caught,
`truncated` is set, and **everything already read is kept**. The frame the writer died inside is
dropped — its spans are unreferenced by any committed frame, so they are trimmed rather than left as
orphans. Verified by chopping 200 bytes off a good file: 5 of 6 frames survive, `truncated` is true,
`spans.Count` ends exactly at the last frame's slice.

`<Z … />` **is emitted self-closing** when a span has no children and no counters, so the reader
tracks `IsEmptyElement` rather than waiting for an `EndElement` that never comes. This was the one
real trap in the format; a `Draw` span sitting as a sibling of `Layout` is the case that catches it.

### 4. One control draws both the flame chart and the timeline

`SpanChartControl` takes a window of the absolute clock and a set of lanes. `Mode="Frame"` pins the
window to the selected frame with one lane; `Mode="Timeline"` gives every thread and lets the window
zoom and pan. A single-frame flame chart *is* the degenerate aligned timeline — [[engine-profiling]]
§10 made `<F T>` process-wide precisely so the lanes share an axis, so building the two separately
would have been the same layout twice.

**Row 0 of a lane is the frame, and spans start at row 1.** The gap between the root span's end and
the frame bar's end is the parked tail, drawn without being computed.

### 5. A timeline opens on a count of frames, and the count is the selected thread's

Opening the timeline on the whole capture was a blank pane, and correctly so: 7.65 s across 919 px is
8.3 ms per pixel, so a 5 ms frame is 0.6 px and every span culls itself against `MinSpanWidth`.

**Rejected: a fixed `DefaultWindowMs`.** 200 ms was fine for the synthetic capture and blank for the
real one, where main does ~0.3 ms of work per 8.4 ms frame — 1.4 px, just under the cull. A duration
cannot be a constant when the thing it has to frame is a frame.

**Rejected: the tightest interval across threads.** Threads run free and record N frames *each*, so
Thorium's render thread finished its 300 at ~0.47 ms apart while main's took 8.4 ms apart; taking the
minimum collapsed the window to 4.7 ms and showed one main frame and nothing else.

So `_windowSpan` is left **unset** by `SetSession` and sized by the first `SetFrame` from *that
thread's* frame interval × `DefaultWindowFrames` (10). Selecting a frame afterwards **re-centres
without changing the zoom**, so clicking a spike in the strip lands on it in both charts.

### 6. Materialisation is windowed, and that is real virtualization

A span outside the window is not built, one under `MinSpanWidth` (1.5 px) is not built, and
`MaxRects` truncates the rest. Unlike [[log-viewer-plan]]'s 500-entry cap this is not a mitigation —
an off-window span genuinely has nothing to draw. Rects and labels are **pooled and reused**, hidden
rather than destroyed, because a drag emits events far faster than 720 entities can be recreated.
Label text is only reassigned when it actually changes, since a label is one `GlyphControl` per
character.

Rebuilding happens on data change, zoom and pan — **never inside `Arrange`**, because `AddChild`
invalidates layout and layout is what would be calling.

### 7. The frame strip buckets, and a bucket stands for its longest frame

`MaxBars` (200) per lane. With more frames than bars a bar covers a run of them and reports the
**longest**, never the mean, so a spike cannot average away. Bars are not hit-testable; the strip
maps the click itself, which is one handler instead of 600 controls that each own one.

### 8. The aggregate is rolled up from the stream, and disagrees with the report on purpose

Zone totals, calls, min/max and counters are derived at load, per [[engine-profiling]] §8's point
that the aggregate is derivable from the stream and not the reverse. The rollup sums **every span
instance**, so a zone that re-enters itself totals more here than the 1-second report's
outermost-only rule gives it. The two are documented as disagreeing; no shipped zone re-enters today.

### 9. Scale is a ruler and a number on the span, not a gridded background (user, 2026-09-03)

Rectangles against nothing say how long *nothing* took. Two additions, deliberately separate:

- **A duration beside every span's name** — `MainTick 0.841ms`. `LabelMinWidth` rose 44 → 70 px so a
  half-drawn number never appears, and span labels became `clipOutOfBounds` so a long caption is
  truncated inside its own rectangle instead of running over the next span, which they had been doing.
- **A time ruler across the top**, ticks on a 1/2/5 × 10ⁿ step chosen so marks land ≥ 80 px apart.

**Marks stay in the ruler row; no gridlines down through the lanes** (user, 2026-09-03). Full-height
rules would read cross-thread alignment better and were offered on that basis; the repeating vertical
pattern behind every lane was not wanted.

**Offsets are anchored to the capture's start, not the window's**, so panning slides the numbers
instead of renumbering them from zero.

That anchoring is what makes the label format non-obvious: a flame chart's window is one frame, ~0.8 ms
wide, sitting a whole second into the capture, so **precision has to come from the step and not from
the magnitude**. Formatting `1.1254 s` by its size prints `1.13s` on all eight marks. `Decimals()`
takes `-log10(step)` in whatever unit is being printed, which gives `1.09s` at a 10 ms step and
`1.1254s` at a 0.1 ms one.

### 10. The zone table is a call tree, not a sorted list (user, 2026-09-03)

Rows nest by the depth the span stream already carries: spans arrive in pre-order, so an
`openNames[depth]` stack names each zone's parent the same way the reader recovers nesting. Rows emit
depth-first with **siblings ordered by total**, indented by a left margin, the name column narrowing
to match.

**The consequence is that the most expensive zone is no longer the first row** — it is the biggest
number at whatever depth it sits. That is the trade for nesting, and it is what makes the table line
up with the flame chart beside it.

Depth is carried by a **swatch bar** at each row's left edge in the chart's own `DepthColorsHex`, not
by tinting the text: that palette is mid-tone fills and would be low-contrast as type on `#F2F1ED`.
A 1 px rule separates one thread's zones from the next.

A zone whose recorded parent never became a row would silently vanish, so a second pass emits any
zone the tree walk missed at depth 0. Nothing hits it today; a diagnostic table quietly dropping a
row is the failure worth spending five lines on.

### 11. Allocation shows on every span, on the frame bar and in the table (2026-09-03)

[[engine-profiling]] §12 put bytes on every span and every frame. Carbon draws all four of them:

- **The zone table's thread header** carries the thread's whole allocation over the capture, summed
  from `<F A>` — which includes what ran outside any zone, so it is larger than the root zone's.
- **A zone row's detail line** carries that zone's bytes, rolled up the same way its time is.
- **The frame bar's caption** (row 0 of a lane) carries the frame's bytes beside the thread name.
- **A span's caption** carries that span's own bytes, third after its name and duration.

**Reversed 2026-09-03: spans were first shipped without bytes and now carry them** (user — a boot
capture reads `Bootstrap 243.58MB` on the frame bar with nothing on `Settings.LoadAll`, which is the
number actually wanted). The original reason stands as a *consequence*, not a veto: a span needs
`LabelMinWidth` (70 px) to earn a caption at all, and `{name} {duration} {bytes}` overruns a bar
barely over that threshold — span labels are `clipOutOfBounds`, so the bytes are what clips off.
Deliberately not traded for: a wider `LabelMinWidth` (fewer labelled spans), or dropping the duration.
Both charts take it, since `Flame` and `Timeline` are one control.

**The number is inclusive, matching §12 of [[engine-profiling]]** — a parent span's bytes contain its
children's, the same way the zone table's rollup does. Bootstrap steps are flat so the boot capture
reads as self-bytes; a nested zone does not.

A span that allocated nothing keeps the two-part caption, because `<Z>` is written without an `A` and
the caption branches on `bytes != 0` — which is also what an older capture file, written before
allocation existed, reads back as.

Formatting is `CapturedThread.Bytes`, a static beside the instance `Ms` — one place both controls
share, on the reader's side of the boundary §2 draws.

### 12. The timeline scrolls with a bar; the flame chart does not (user, 2026-09-03)

The timeline panned by dragging its body and zoomed on the wheel, with nothing on screen saying where
the window sat in the capture. A track across the plot column, with a thumb whose width is
`_windowSpan / _boundsSpan` and whose offset is `(_windowStart - _boundsStart) / ScrollRange`, says
both at once and drags.

**Nothing in the engine could be reused.** `ScrollableControl.ArrangeThumb` returns before doing
anything unless `CanScrollVertical`, so there is no horizontal scrollbar to inherit, and
`ScrollThumbControl` takes a `ScrollableControl` in its constructor and writes through
`SetScrollOffset`. `ChartScrollThumbControl` is Carbon's own `ButtonControl` — which is also what buys
the hover and press tints for free. `SpanChartControl` exposes `ThumbTravel`, `WindowStart`,
`ScrollRange` and `ScrollTo`, deliberately the shape of `ThumbTravel` / `MaxScrollOffset` /
`SetScrollOffset`, so the thumb's arithmetic is the engine's with X substituted for Y.

**Frame mode gets no bar.** Its window *is* the frame the strip picked, so the thumb would be a
hairline standing for a window nothing may move, and dragging it would fight the strip for ownership
of `_windowStart`. Track and thumb arrange to `LayoutRect.Empty`, which draws no pixels and fails the
hit-test — the same collapse `ScrollableControl` uses when its content fits.

**The bar takes no vertical room from the lanes.** It is pinned to `inner.Bottom` rather than placed
below the deepest lane, because lanes already grow unbounded downward: reserving space would have
changed existing geometry to avoid a collision three lanes never reach.

**Rejected: clicking the track to page or jump.** The engine's scrollbar has no track control at all,
let alone that behaviour, and a press on the track falls through to the chart and pans, which is what
was already there.

Colours are XML attributes on the `Timeline` element, the `Thumb*ColorHex` triple `SessionList` and
`ZoneTable` already carry. `Flame` is left without them, since it never draws one.

Measured in the running app against a real 60-frame capture: track 425→1383 px, thumb 70 px — 7.3% of
it, for a ~137 ms window on a ~1.9 s capture — and hover resolving to `#C9C6BC`. **The drag itself was
confirmed by the user, not by the harness**: a scripted press registered the press tint and then never
moved the thumb, so that path proved nothing either way.

### 13. The two charts split 1:2 and the split drags (user, 2026-09-03)

`Flame` and `Timeline` were `HeightStar="1"` each, so a boot capture — one lane, a handful of
bootstrap steps — got the same half of the column as a three-thread timeline. `Timeline` is now
`HeightStar="2"` and a `Splitter` sits between them.

**Both panes keep their stars; the grip trades weight between them.** That is a new path in
`SplitterControl` and the reason it exists — see [[splitter-and-pane-sizing]]'s second amendment. The
alternative was pinning `Flame` to a pixel height and leaving `Timeline` the only star, which is the
shape the engine already had, but then the 1:2 the user asked for could only be authored as a number
that stops being 1:2 the moment the window resizes.

**`MinHeight="10"` on both** (user, 2026-09-03). It is the drag's clamp only — `StackPanelControl`
does not enforce `minHeight` on a star child, so a small enough window still squeezes past it.

**`ClipToBounds="true"` on both, which they did not carry before.** Neither chart clipped: lanes grow
downward from `inner.y + rulerHeight` unbounded (§12), and until the panes could be dragged small
that never reached anything. A pane at its 10 px floor would otherwise paint its lanes over its
neighbour.

The ruler's step is unaffected — `Ticks()` chooses from `PlotWidth()` and this grip moves heights, so
the "no `Rebuild` on resize" gap below is not reachable through it.

## Facts that were expensive to establish

- **A `widthStar` `LabelControl` inside a horizontal `StackPanel` is measured at width 0 and wraps to
  one glyph per line.** `StackPanelControl.Measure` offers a horizontal star child
  `new Vector2D<float>(0, inner.height)`, and `LabelControl` does **not** override
  `TextControl.WrapWidth`, so `contentWidth` is 0. An 8-character zone name became a 155 px row. Use
  fixed `preferredWidth` columns in a horizontal strip, or a control that overrides `WrapWidth`.
- **A control outside the engine assembly cannot clear `isMeasureDirty` / `isArrangeDirty`** — both
  are `internal set`. A custom container calls `base.Measure` / `base.Arrange` for the transform, the
  clip and the flags, then positions its own children afterwards. `PanelControl` overrides neither,
  so `base` is `VulkanControl`'s.
- **`Hide()` collapses the clip, and `Arrange` rewrites it.** A hidden child must be *skipped* by its
  parent's `Arrange`, not merely hidden, or it reappears.
- **Debug paths resolve against the working directory.** `Paths.GetPath` returns
  `Path.GetFullPath(Path.Combine("..","..","..", path))`, so an app must be launched from its own
  `bin/Debug/<tfm>/`. `dotnet run` sets the repo root instead and boot dies at `XSDGenerator` with
  `DirectoryNotFoundException: D:\Data\XML\Schemas\…`. Visual Studio gets this right by default.
- **`Shaders/` is per application by the same rule** — `UIModule` reads
  `"../../../Shaders/UIRasterizer/UI.vert.spv"` and `RenderWindow` builds a `CompositorModule`
  unconditionally, so Carbon needs four `.spv` copies. **That makes four copies of the UI shaders,
  not three**; `CLAUDE.md` and [[../Context/where-things-live]] said three and were corrected.
- **Carbon's `Data/Fonts` and `Data/Icons` are copied from Thorium with their `.import.xml` stamps**,
  so first boot bakes nothing (`AssetImporter.RunImports` — 13 ms). Without the stamps it would bake
  into `Carbon/Data/Fonts`, since `Paths.FONTS` is the *running app's* folder.
- **`InputHandler.ParseXML` throws on an action name it cannot resolve**, so a dangling `Action=` is a
  boot failure, not a warning. It matches on the attribute's name only and ignores the category.
- **`ExitApplication` is defined per application**, in each host's `Decorations.cs`, not in the
  engine. Carbon needed its own.
- **U+2026 (`…`) is not in the baked charset**, so "Load XML…" renders as "Load XML". Thorium's
  "Add vault…" has the same hole.

## Left standing

- **No `.xsd` for the frame format** (user, 2026-09-02 — asked for a Load XML button instead).
  `PROFILING.md` had promised the schema would ship with the reader; it does not. The reader is the
  only consumer and it is hand-written, so a schema would validate only files the engine itself wrote.
- **"Load XML" picks a folder, not a file.** `FolderPicker` is the only picker the engine has
  (`FOS_PICKFOLDERS`), and a session is a folder of per-thread files, which is what the aligned
  timeline wants. A single-file picker needs a `FolderPicker.PickFile` that does not exist.
- **Zoom is about the window centre, not the pointer** — `ResolveOnScrollUp/Down()` take no
  coordinates.
- **A resize does not `Rebuild`.** `Ticks()` picks its step from `PlotWidth()`, so after the window is
  resized the ruler keeps the step it chose at the old width — 10 ms marks were seen 70 px apart,
  under `tickMinSpacing`'s 80 — until the next zoom, pan or frame selection re-runs it.
- **Nothing draws counters in the charts.** They are in the aggregate table only.
- **No diffing two captures, no live tailing of a Continuous session, no export.**
- **`Periodic/` at the repo root still holds a stray `obj/`**, dead since the 2026-08 rename.
  Unrelated, untouched.

## Verified

Round-trip through the real writer, from a scratch console harness outside the repo:

| Case | Result |
|---|---|
| header | `Thread`, `Frequency` = `Stopwatch.Frequency`, `Mode="Burst"`, `Requested="6"` |
| name table | 6 ids, every `N=` resolves |
| nesting | `Text` inside `Layout`; `Draw` a sibling, and self-closing |
| counters | `Measure 3` on Layout, `Glyph 5` on Text, none on the root |
| parked tail | `D` (107918) − root `E` (94961) = 1.296 ms, matching the raw XML |
| truncation | 200 bytes chopped → 5 of 6 frames, `truncated` true, no orphan spans |

In Carbon, against a synthetic 3-thread × 240-frame session:

| Case | Result |
|---|---|
| session list | folder listed newest first with its thread names, no parsing |
| load | 1200 / 720 / 480 spans and 480 / 240 / 0 counters — the generator's exact counts |
| strip | 3 lanes × 200 bars, click re-selects and retints across lanes |
| flame | frame bar, `MainTick`, then `PollEvents`/`HandleUI`/`Interpolate`, `Layout` inside `HandleUI` |
| timeline | 3 lanes interleaved, tick rates visibly 11.1 / 16.6 / 32 ms apart |
| zoom / pan | 4 wheel notches widened the labels; a drag scrolled and clipped a frame at the edge |
| aggregate | `Module 2880` = 12 × 240, `Window 240` = 1 × 240, every min/max inside its generated range |
| sum check | `RenderTick` 1413.23 ms vs `Draw` + `Present` = 1412.82 ms |

Against two real Thorium captures (`%APPDATA%\Thorium\Profiling\`, 300 frames per thread):

| Case | Result |
|---|---|
| real names | `MainTick`, `PollEvents`, `ActivateKeybinds`, `HandleUI`, `Interpolate`, `FrameEdge`, counter `Window` |
| aggregate | `MainTick` 47.73 ms x300 (min 0.101 max 0.841), `Interpolate` 27.24, `HandleUI` 9.93 — `Window 300` |
| render | `RenderTick` 128.75 ms x300, `Draw` 128.47 — so Draw is essentially all of the render tick |
| empty frames | `Physics` reads 300 frames and **zero** spans, from self-closing `<F … />` |
| alignment | at the capture's start Render packs the lane while Main shows 3 frames in the same 84 ms |
| span durations | flame reads `MainTick 0.841ms` and `HandleUI 0.222ms` — the table's `max` for both |
| ruler, timeline | `1.09s … 1.16s` on a 10 ms step across an 84 ms window |
| ruler, flame | `1.1254s … 1.1261s` on a 0.1 ms step across one 0.846 ms frame |
| ruler under zoom | five wheel notches re-stepped 10 ms → 5 ms, precision following it |
| call tree | `MainTick` over `Interpolate` 27.24 / `HandleUI` 9.93 / `PollEvents` 5.44 / `ActivateKeybinds` 3.39 / `FrameEdge` 0.43, and `RenderTick` over `Draw` |
| separators | rules above `Physics` and `Render`, none above the first thread |

Three things that capture said about the **engine**, not about Carbon:

- **`FrameEdge` is not in `ThreadedSystem.Loop`.** `Loop` has `Frame.Begin`/`End` and `Report`, and
  nothing else; `FrameEdge` is one of `Engine.MainTick`'s zones. [[engine-profiling]] §11 and
  `PROFILING.md` both claim "the frame edges themselves are in `ThreadedSystem.Loop`, so every system
  has them, physics included" — **that is wrong**, and Physics recording 300 spanless frames is the
  proof. Not corrected here; it is the profiler's note to fix.
- **The render thread ticks about 18× faster than main** — ~0.47 ms between frames against ~8.4 ms.
- **A burst of N frames covers a different wall-clock span on every thread**, so the threads only
  overlap near the *start* of a capture. Render's 300 frames spanned ~141 ms; main's spanned ~2.5 s.
  Continuous mode would not have this shape.

Related: [[engine-profiling]], [[engine-logging]], [[log-viewer-plan]], [[ui-data-control-split]]
