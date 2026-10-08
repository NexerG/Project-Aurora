# Decision — the SVG icon importer reads fills and strokes; evenodd is per glyph, strokes are baked from the centreline, dashes are next

**Date:** 2026-10-09
**Scope:** `ArctisAurora.Core.Filing.Serialization` — `SvgPath`; `ArctisAurora.Core.Filing` — `Glyph`, `StrokeRun`, `StrokeCap`, `StrokeJoin`; `ArctisAurora.Core.Generators` — `MTSDFGen`; `ArctisAurora.Tests` — `SvgTests`; `AuroraEngine/Data/XML/Documents/Tests/Svg.tests.xml`

User goal (2026-10-09): full SVG shaping support except colouring. The first pass covered fills; the second (same day) covers strokes. Dashes and the rest are listed under Known gaps.

## What changed
- `SvgPath.TryLoad` walks every element in the SVG namespace (was: only `<path>`).
- Refuses an icon mixing nonzero and evenodd ("mixes nonzero and evenodd fill rules"); sets `Glyph.evenOdd`.
- Empty result refusal message was "no filled shape produced any contour"; since the stroke pass it is "no shape produced any contour or stroke".
- `SvgPath.Property` inherits: walks `AncestorsAndSelf`, attribute then `style=""`, nearest wins. Applies to `fill` and `fill-rule`.
- NEW static set `unrendered` (`defs`, `clipPath`, `mask`, `symbol`, `pattern`, `marker`) — shapes under these are skipped.
- NEW `ShapeData(XElement)` — path data per shape:
  - `path`: its `d`.
  - `rect`: synthesized, incl. rounded; rx/ry auto per SVG2 (a missing one takes the other, clamped to half the side); zero-length straight edges omitted.
  - `circle`/`ellipse`: two 180 degree arcs.
  - `polygon`: closed. `polyline`: closed by the fill anyway.
  - `line` and zero-size shapes yield nothing.
- NEW `TryTransform(XElement, out Matrix3x2, out string)` composes the element's and every ancestor's `transform`; NEW `ParseTransform` handles `matrix`, `translate`, `scale`, `rotate` (1 or 3 args), `skewX`, `skewY`. An unknown name or arity refuses the icon.
- NEW `AddArc` — SVG 1.1 F.6.5 endpoint-to-centre conversion; out-of-range radii scaled up; zero radius becomes a line; coincident endpoints dropped; one cubic per at most 90 degrees.
- NEW `ReadFlag` — arc flags are single digits and may be written without separators (`a40 40 0 0180 0`).
- `ParseData`: `A`/`a` supported. An iteration that consumes nothing fails with "malformed path data at N" (before: a number after `Z` or a stray character looped forever and hung the bake).
- REMOVED the `Log` channel from `SvgPath` (its only use was the evenodd warning).
- `Glyph`: NEW `[@NonSerializable] public bool evenOdd` — bake-time only, not in `.aid`.
- `MTSDFGen.GenerateCell`: "outside" is `glyph.evenOdd ? (winding & 1) == 0 : winding == 0`.
- NEW `SvgTests` and suite `Svg.tests.xml`: `Svg.InheritedFill`, `Svg.Unrendered`, `Svg.Transform`, `Svg.Malformed`, `Svg.FillRule`, `Svg.Arc`, `Svg.Shapes`.

## What changed — strokes (second pass)
- `SvgPath` imports `stroke`, `stroke-width`, `stroke-linecap` (butt/round/square), `stroke-linejoin` (miter/round/bevel; SVG2 `arcs` and `miter-clip` fall back to miter) and `stroke-miterlimit`. `fill="none"` is no longer refused; it means no fill. Stroke properties go through the inherited `Property`, so `stroke-*` on `<svg>`/`<g>` is inherited.
- NEW `Filing.StrokeRun` (`[@NonSerializable]`, in `Bezier.cs` next to `Edge`): `List<Edge> edges` (the centreline in the subpath's own pre-transform coordinates), `bool closed`, `float halfWidth`, `StrokeCap cap`, `StrokeJoin join`, `float miterLimit` (default 4), `Matrix3x2 toGlyph` (local to normalized Y-up glyph space).
- NEW enums `StrokeCap { Butt, Round, Square }` and `StrokeJoin { Miter, Round, Bevel }` (in `Bezier.cs`).
- `Glyph`: NEW `[@NonSerializable] public List<StrokeRun> strokes` — bake-time only, not in `.aid`.
- `SvgPath.TryLoad`: each shape element resolves `filled` (fill not `none`) and `stroked` (stroke paint not `none`/missing, and width > 0); neither means the element is skipped. Fill-rule checks apply only to filled elements. `vector-effect="non-scaling-stroke"` refuses the icon. The viewBox normalisation is also a `Matrix3x2 normalize`, and `run.toGlyph = transform * normalize`; fills are still normalised point by point.
- `SvgPath.ParseData(data, contours, runs, out reason)`: either output may be null.
- `SvgPath.Close(contours, runs, ref contour, current, start, closed)` records each subpath as a stroke run before any fill closing segment is added:
	- an explicit `Z` gives a closed run, with a closing line unless the data already returned to the start point
	- otherwise the run is open
	- `M x y Z` (zero-length, closed) records one degenerate-edge "dot" run with `closed=false`; a lone moveto records nothing
- NEW `SvgPath.Run(points, closed, current, start)` (subpath to `StrokeRun`); NEW `SvgPath.Number(string, fallback)`, which `Length` now calls.
- `MTSDFGen.GenerateCell` precomputes per run the inverse of `toGlyph`, a distance scale `sqrt|det(toGlyph)|` and a glyph-space reach box; runs with a singular matrix are skipped. Per pixel: `stroke = max over runs of StrokeDistance(p mapped to local) × scale`, then each of `minR/minG/minB/minAll = max(channel, stroke)`.
- NEW private in `MTSDFGen`: `RunBounds`, `StrokeDistance`, `Cap`, `Join`, `Direction`, `Box`, `Polygon`.
- Tests: NEW `Svg.StrokeRuns` and `Svg.StrokeBake` in `Svg.tests.xml`; `Svg.InheritedFill` now expects the "no shape" refusal.

## Why these choices

**Evenodd is per glyph, not per path.**
The bake decides inside/outside by one winding count summed over all edges at a pixel, so it cannot apply nonzero to some contours and parity to others; an icon mixing the two rules is refused. Rejected: per-path winding buckets in `RowCrossings` — more work in the hot cell loop for a case no icon needs yet.

**Shapes become path-data strings and go through `ParseData`.**
Transform, fill and close handling are then one code path. Rejected: building contours directly per shape — duplicate close/arc code.

**Transforms are applied to the cubic control points after parsing.**
Exact, since cubics are affine-invariant. Composition: within a list, right-to-left (`matrix = item * matrix`); across ancestors, element first then parents (row-vector `Vector2.Transform`).

**Malformed data fails the icon, never loops.**
A single no-progress guard per parser iteration covers number-after-Z and stray characters.

**Strokes were refused in the first pass** (`fill:none`, also when inherited); two options were recorded for the second, and (b) landed:
- (a) stroke-to-outline geometry — rejected. Overlapping outline pieces leave interior seams in a multi-channel distance field unless boolean-unioned first (large).
- (b) bake strokes directly as distance-to-centreline minus half width — landed (see below). Exact for round caps/joins (what Lucide-style icons use), no union; `Glyph` carries stroke runs and `GenerateCell` changed.

**Strokes are a signed distance baked from the centreline, not stroke outlines.**
An outline is many overlapping or abutting closed contours. Under the bake's global min-distance plus winding, every internal overlap edge becomes a false boundary, so seams appear inside the shape. Avoiding that needs a boolean union or a per-contour overlap combiner, which is the large option (a).

**The formula, per subpath, in local units: `s = hw − distance to centreline`.**
Exact for round caps and joins; inner corners fall out of the min with no special case. Then:
- Butt cap / bevel join (subtract): `s = min(s, max(−beyond, |p−V| − 1.5·hw))`. `beyond` is the signed distance past the end line or bevel line. The bevel line sits at `hw·cos(turn/2)` along the outward bisector.
- Square cap / miter join (add): `s = max(s, −box)` (a hw/2 × hw half-extent box past the end) or `max(s, −kite)` (V, V+nA, tip, V+nB, with tip = V + bisector·hw/cos(turn/2)).
- Miter falls back to bevel when `cosHalf · miterLimit < 1` (the SVG ratio 1/sin(θ/2)).
- A reversal (turn ≈ π) uses the incoming tangent as the outward direction, so it behaves like a butt cut.
- Joins with a turn under 0.01 rad are skipped (tangent-continuous curves, and the pieces an arc is split into). Round joins never add anything.

**There are no seams.**
An added shape's inner edges lie inside the round stroke. A subtraction is confined to a 1.5·hw disc and never cuts the segment bodies — checked: for straight segments no body point lies beyond the bevel line.

**The stroke unions with the fill by per-channel max, and subpaths union by max too.**
max(·, s) commutes with the median, so the result is exactly fill ∪ stroke. A butt or bevel subtraction can therefore only cut its own subpath. Rejected: one formula over every subpath, which would let a butt end cut a different subpath that touches it (for example an arrowhead join at a shaft end).

**The stroke is single-channel** (user-approved recommendation): the same distance goes into R, G, B and A.
Round caps and joins lose nothing; miter tips and square-cap corners round off slightly at small sizes, while fills keep sharp corners. Rejected: outlining the stroke only to colour its edges, which brings back most of the outlining work.

**Each subpath is evaluated in its own pre-transform space** (user-approved recommendation).
The pixel is mapped through the inverse of `toGlyph` and the distance multiplied by `sqrt|det|`. Shape is exact under any affine transform, including non-uniform scale and skew; only the edge softness stretches. Rejected: scaling the width by `sqrt|det|` in glyph space, which gives the wrong shape under `scale(2,1)` or skew. The `Svg.StrokeBake` ellipse sample (96,50) passes only with the local evaluation.

**Zero-length subpaths** with a round or square cap draw a dot (round falls out of the formula; square faces +x per the SVG spec). With butt they draw nothing (the run returns −∞).

**Degenerate tangents:** a coincident control point falls back to the next control point, then the chord. The direction at a run's ends is the first or last non-zero tangent.

**Dashes are deferred to their own pass** (user-approved): a pre-pass in `SvgPath` that measures arc length, splits cubics with de Casteljau and emits open runs.

`ImporterVersion` is not bumped for strokes: no SVG in the repo has a stroke, so no existing atlas changes.

Atlas cells are stored Y-flipped (existing convention, unchanged).

## Verification
- Test-verified: `Svg` suite 7/7 pass. Full suite at midpoint: 292 passed, 2 failed (Boot — sampler error in test baseline; `Perf.TypeLargeNote` — baseline layout errors), no new error kinds.
- Regression: the 45-icon default set baked with the new importer is byte-identical (`default.aid`, `default_atlas.png`) to a bake with the old importer.
- Visual bake check of scratch icons: evenodd ring hollow, the same ring under nonzero filled, rounded rect, rotated square, circle, ellipse, polygon all correct.
- Not GUI-verified (no icon in the shipped set uses the new features).

Stroke pass:
- Test-verified: `Svg` suite 9/9 pass.
	- `Svg.StrokeRuns`: run count, closed flags, inherited properties, `toGlyph` mapping, a filled and stroked rect, dot runs, non-scaling-stroke refused.
	- `Svg.StrokeBake`: bakes cells and samples the median at known points — butt/round/square ends; miter tip in, round in/out, bevel and over-limit miter out; texels on the join seams at 0.25 and 0.5 hw are > 0.75 for miter, round and bevel; the closed rect's start-vertex miter corner; evenodd ring fill ∪ stroke; non-uniform ellipse; round dot; butt dot draws nothing.
- Full suite at the midpoint: 295 passed, 2 failed (Boot = baseline sampler error; `Perf.TypeLargeNote` = baseline layout errors), no new error kinds. End run: 296 passed, the same 2 failed, no new error kinds.
- Regression: the 45-icon default set baked with the new code is byte-identical to the baseline bake.
- Visual: 8 scratch Lucide-style stroked icons baked and compared by eye, via a thresholded atlas view, against the same SVGs rendered in the browser pane — round zigzag, a circle with an arc smile and h.01 dots, butt+miter, square+bevel, the miter-limit triangle, the non-uniform ellipse, a filled rect with stroke plus round and square dots, a round-join arrow. They matched shape for shape, no seams.
- Not GUI-verified: no shipped icon is stroked.

## Known gaps
- `stroke-dasharray` / `stroke-dashoffset` — next pass.
- Miter and square corners are slightly rounded at small atlas sizes (single-channel stroke).
- A subpath that loops back within 1.5·hw of its own butt end or bevel join gets cut there.
- Two subpaths that only abut (not overlap) can show a faint seam where they meet; browsers have the same conflation artifact.
- Pre-existing parser gap, noticed: a drawing command right after `Z` with no `M` (legal SVG, the new subpath starts at the last start point) fails with "line before any moveto".
- Percentage `stroke-width` falls back to 1. `paint-order` and `stroke-opacity` are colour concerns and out of scope.
- `<use>`/`xlink:href`, nested `<svg>` viewports, `preserveAspectRatio`, percentage/unit lengths (lengths strip a trailing px/em and read the number), `display="none"`/`visibility`.
- Mixed fill rules in one icon are refused.
- A polygon/polyline with an odd coordinate count reads the missing y as 0.
- `ImporterVersion` in `*.import.xml` not bumped — existing atlases are byte-identical, so no rebake is needed.
- Pre-existing, noticed, not touched: the four default-icon atlas copies differ — AuroraEngine and AuroraEditor hold an older `default_atlas.png`/`default.aid` than Thorium and Carbon (md5 886bb... vs fb2b7...).

Supersedes the importer-limit statements in [[workspaces]] (W4b) and [[document-format-bar]] (Icons): no `A` command and evenodd-as-nonzero are no longer true. The stroke pass supersedes their "strokes are refused" and "`fill:none` is refused" statements too.

Related: [[workspaces]], [[document-format-bar]], [[atlas-bake-one-edge-pass]], [[asset-manifest-and-import]]
