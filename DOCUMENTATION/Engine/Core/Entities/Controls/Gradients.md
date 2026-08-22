---
date: 2026-08-22
Status: Current
tags:
  - d_UI
cssclasses:
  - Aurora.css
Linker:
  - "[[Vulkan Control]]"
System:
Class:
  - "[[Gradients]]"
Parent Class:
Interfaces:
Used by:
  - "[[Periodic]]"
Type:
  - Public
Attributes:
Namespace: ArctisAurora.Core.UISystem
SourceFile: AuroraEngine/Core/UISystem/Gradients.cs
VerifiedAgainst: 2026-08-22
---
## Description

Named colour ramps authored in `Gradients.xml` and painted procedurally by the fragment shader. There is no gradient texture anywhere — a control stores a row number and the shader evaluates the ramp for the pixel it is shading.

The table is the point. A definition is written once and every control naming it shares the same row, so restyling a gradient used by forty controls is one edit to one file rather than forty attributes. Row 0 is reserved and cannot be named, which is what lets a control that never mentions a gradient carry index 0 and cost nothing.

The file is optional. A host with no gradients of its own ships no `Gradients.xml` and boots normally with an empty table; `Paths` resolves it across mounts like any other document, so an application overrides the engine's by placing its own.

A name that no gradient answers to throws at load rather than falling back to a default, on the same reasoning as a context menu action — a typo in a theme file should stop the boot, not quietly paint the wrong thing three screens later.

## Authoring

A gradient is a kind, a shape parameter and up to eight stops.

```xml
<Gradient Name="accent" Angle="0">
  <Stop Color="#3A6EA5" Pos="0"/>
  <Stop Color="#8A5CD1" Pos="1"/>
</Gradient>

<Gradient Name="glow" Kind="Radial" CenterX="0.5" CenterY="0.5">
  <Stop Color="#3A6EA5" Alpha="0.55" Pos="0"/>
  <Stop Color="#3A6EA5" Alpha="0" Pos="1"/>
</Gradient>
```

Any control names one through `Gradient`, and it ramps in place of that control's `ColorHex`.

```xml
<TitleBar Height="32" ColorHex="#141414" Gradient="titlebar"/>
<Run Text="Welcome to Periodic" Gradient="accent"/>
```

`Angle` is degrees, `0` running left to right and `90` top to bottom, since +y is down. The ramp spans the control corner to corner for whatever angle it names, so a diagonal does not skew on a wide control. `Kind="Radial"` ignores `Angle` and ramps outward from `CenterX`/`CenterY` as an ellipse fitted to the control's farthest corner.

A stop's `Alpha` multiplies the coverage the control already has, so a ramp ending at `Alpha="0"` fades out rather than fading to black.

## What it does not do

A gradient recolours; it does not paint. It appears exactly where `ColorHex` appears, which means a container masked invisible shows nothing — see [[Vulkan Control]] on masks.

The edge and the outline take their own hex colours and are not gradientable. Neither is a button's hover or press colour, so a gradient button keeps its ramp through both states.

## Text

A run hands its glyphs its own rect, so the ramp spans the run instead of restarting on every letter. This is the whole reason a gradient measures across a rect rather than across the control's own quad — a glyph is a control, and its quad is one letter wide.

The rect a run hands down is the text column's width, not the ink extent of the run. Two runs sharing a line under one gradient therefore stay continuous, and a run wrapped across several lines gets one ramp over the block rather than one per line. The cost is that a run beginning mid-line measures its ramp from the column edge rather than from its own first letter.

A gradient does not cross runs. A heading built from two runs gets two ramps.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `MaxStops` | const | Stops one gradient may declare. Eight. |
| `Count` | property | Rows in the table, including the reserved row 0. |
| `Table` | property | The baked rows, uploaded once by the UI mesh component. |
| `IndexOf(name)` | method | Row for a name. Empty gives 0; unknown throws. |
| `LoadGradients()` | action | Bootstrap step. Parses the file and bakes every row. |

## Pseudocode

```
LoadGradients:
	empty the table back to its reserved row and forget every name
	if no mount answers with a Gradients.xml
		say so and carry on with nothing loaded
	for each gradient element
		read it, remember its name against the row it is about to take
		bake it and append
```

```
Bake:
	turn the angle into a unit direction, so the shader only ever does a dot product
	carry the centre and the kind across as they were authored
	for each stop
		resolve its hex to rgb, pair it with its alpha and its position
```

Related: [[Vulkan Control]], [[Context Menu]]
