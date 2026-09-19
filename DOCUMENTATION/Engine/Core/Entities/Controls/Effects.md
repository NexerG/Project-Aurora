---
date: 2026-09-19
Status: Current
tags:
  - d_UI
cssclasses:
  - Aurora.css
Linker:
  - "[[Vulkan Control]]"
  - "[[Gradients]]"
System:
  - "[[ANIMATION]]"
Class:
  - "[[Effects]]"
Parent Class:
Interfaces:
Used by:
Type:
  - Public
Attributes:
Namespace: ArctisAurora.Core.UI
SourceFile: AuroraEngine/Core/UI/Effects.cs
VerifiedAgainst: 2026-09-19
---
## Overview

An effect is a small animation the graphics card plays by itself: a wave through a heading, letters sliding up and fading in one after another, a panel that pulses. It is written once in an `Effects/*.effects.xml` file — the engine's own or the app's, all of them are read — under a name and then used anywhere, the same way a gradient is — a control takes `Effect="wave"`, and a run of text in a note takes `Effect="wave"` too.

Nothing on the CPU steps an effect. Each drawn quad carries which effect it plays and the time it started, and the vertex shader works out where the quad should be at the current engine time. That is what makes it affordable on every letter of a document.

An effect only changes how something looks. Layout, clicking and the caret all see the letter where it would be without the effect.

## Authoring

```xml
<Effects xmlns="http://arctisaurora/AuroraUITypes">
	<Effect Name="wave" Duration="1" Loop="PingPong" Ease="SineInOut" Stagger="0.08" OffsetFrom="0,0" OffsetTo="0,-6"/>
	<Effect Name="rise" Duration="3" Loop="Once" Ease="CubicOut" Stagger="0.1" OffsetFrom="0,20" OffsetTo="0,0" AlphaFrom="0" AlphaTo="1"/>
</Effects>
```

`Duration` is seconds per cycle. `Loop` is `Once`, which plays and holds its end, `Loop`, which starts again, or `PingPong`, which runs forward and back. `Ease` is any of the animation system's curves; `CubicBezier` also reads `X1 Y1 X2 Y2`, and `Steps` reads `Steps`.

The channels are an offset in design pixels, a scale, a rotation in degrees and an alpha, each with a `From` and a `To`. A channel left out does nothing.

`Stagger` delays each letter of a run by that many seconds after the one before it, which is what turns one movement into a wave.

## When an effect starts

An effect starts when it is given to a control, or when a note loads a run that has one. A `Once` effect therefore plays when a note opens and not again while it is edited. `RestartEffect()` on a control plays it again from now.

## Methods

| Member | Kind | What it does |
| --- | --- | --- |
| `LoadEffects()` | bootstrap step | Reads every `Effects/*.effects.xml` the engine and the app carry and fills the effect table. |
| `IndexOf(name)` | method | The table row for a name; no name is row 0, which plays nothing; an unknown name is an error. |
| `Stagger(id)` | method | The seconds between letters for that effect. |
| `Pool` | property | The `Effects` data pool; the UI module copies it to the GPU. |

```
Effect progress, per vertex
	t = (engine time − start) / Duration, never below 0
	Once: hold at 1 · Loop: fraction of t · PingPong: back and forth
	k = the ease curve at t
	scale and rotate the quad about its centre by the mixes at k
	move it by the offset mix at k
	multiply its alpha by the alpha mix at k
```
