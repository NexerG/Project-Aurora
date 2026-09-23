---
date: 2026-09-19
tags:
  - d_System
cssclasses:
  - Aurora.css
Status: Current
Linker:
  - "[[Arctis Aurora]]"
System:
  - "[[ANIMATION]]"
Dependencies:
  - "[[UI-ENGINE]]"
Implementors:
  - "[[ANIMATION]]"
Namespace: ArctisAurora.Core.Animation
SourceFiles: AuroraEngine/Core/Animation/*.cs
VerifiedAgainst: 2026-09-23
---
## Overview

Animation is a step of the frame graph (see [[THREADING]]), running beside physics on whichever worker claims it, after main's input and entity logic and before main applies values and lays out. Main asks for an animation, the animation step advances it every frame, and main applies each new value the same frame. A property stored in a data pool — a control's width, height, margin, padding or position — is written straight into its row by the animation step, and main only marks the control for layout; any other property, such as alpha, goes through its ordinary setter on main. Either way an animated width lays out and an animated alpha repaints exactly as if code had set them by hand.

Three kinds of motion exist today; the third, keyframed clips authored in XML, is under Clips below. A tween eases from where a property is now to a target over a fixed time, along a curve. A spring rests at the property's current value and chases whatever target it is given next, with a frequency and a damping ratio; a damping ratio of 1 settles as fast as possible without overshooting, below 1 it bounces, above 1 it creeps.

This is the first slice of a larger plan in which palettes, gradients and all styling move to the animation thread; see `ClaudeMemory/Context/animation-plan.md`.

## Architecture

### Asking for an animation

`Animations` is the main-thread side. `Tween(target, property, to, seconds, curve)` starts a tween, `Spring(target, property, frequency, damping)` starts a spring, `Retarget(handle, to)` gives a spring (or a running tween) a new target, and `Stop(handle)` ends it. Starting returns an `AnimationHandle`; once that animation has finished or been stopped, the handle quietly does nothing. The property is named by its XML attribute name, `Width` or `Alpha`, and must be marked `[A_Animatable]`. Every value travels as a `Vector4`; a `float` uses the first component, a `Thickness` all four in top, right, bottom, left order.

The first time a property is animated, its getter and setter are compiled once and kept, so later animations of the same property cost no reflection. A property that lives in a data pool says where, as `[A_Animatable(typeof(ArrangeData), nameof(ArrangeData.preferredWidth), nameof(InvalidateLayout))]`: the column, the field inside it, and the method main calls after the value has been written. The field's offset and size are worked out once too.

### Track ids

Main hands out the track ids. Each id carries a generation that goes up every time the id is reused, and a value coming back with the wrong generation is ignored, so an old animation can never write into a new one that happens to share its id. A tween's id is released when its last value lands; a spring's id stays valid until `Stop`.

### The animation thread

`AnimationSystem` keeps one row per track in the `Animations` data pool. Each frame it advances every track that is running, writes the value into the property's pool row when the property has one, and adds a row to the `AnimationValues` pool either way. A spring that has settled goes to sleep and costs nothing until it is retargeted. A track whose control has been freed skips the write.

### How requests and values move

Nothing is sent between threads. A request — start, retarget, stop, set a signal, fade the palette — is written into the animation pools on the spot by the main step that makes it, and the frame graph orders that step against the animation step, so a stop takes effect the same frame. Values travel back through the `AnimationValues` pool, which the main step `Main.Apply` reads right after the animation step and before layout: it calls the setter of an ordinary property, or the after-write method of a pool-stored one, and releases a finished tween's id. There is no limit on how many requests or values a frame carries.

A main step that makes a request lists the animation pools it writes; setting a signal is only allowed before the animation step, because the animation step reads signals and a later writer would make a loop.

### Signals

A signal is a value that springs follow. `Signals.Create()` makes an anonymous one and `Signals.Named(name)` returns the same one for the same name every time; `Signals.Set(signal, value)` changes it, and every spring started with `Animations.Spring(target, property, frequency, damping, signal)` chases the new value on the animation thread's next tick. Several springs can follow one signal, so one write can move several things at once. `Signals.Release(signal)` frees it.

### Buttons

Every button eases between rest, hover and press instead of snapping. A button owns a signal set to 0, 1 or 2 by the pointer, and a spring on its `state` follows it. When the button's colour is one of the palette's surfaces, the shader blends between the three baked shades by that number, so the colour keeps following the palette mid-fade; an authored hover or press colour is blended on the main thread instead. How fast and how bouncy the fade is comes from the palette's `StateFrequency` and `StateDamping`.

### Theme fades

Picking a palette in Settings fades the whole app into it rather than snapping. When the palette is picked, main copies the colours currently on screen into the new palette's slots, switches every control over to the new palette, and hands the fades to the animation step, which eases each slot back to its real colour over the new palette's `ThemeFade` seconds. Because the copy happens before the switch, no frame of the new theme shows before the fade begins. Main's own colour decisions — which text colour contrasts with a panel — read the palettes' load-time colours and never a colour mid-fade.

### Clips

A clip is a keyframed animation written in XML. Every `*.anim.xml` under `XML/Documents/Animations` is read at boot; a `<Clip>` holds one `<Track>` per property, and each track lists `<Key Time Value Ease/>` entries. A key's `Ease` shapes the stretch from that key to the next, as in CSS, and `Value` is one, two or four comma-separated numbers in the same order the property travels as a `Vector4`. A clip lasts as long as its latest key; a shorter track holds its last value until then, so looping tracks stay in step. `Loop` is `Once`, `Loop` or `PingPong`.

The keys of every clip sit in the `Keyframes` data pool, filled once at boot and only read after. Playing a clip starts one keyframe track per clip track: `Animations.Play(target, clip, hold)` returns their handles, and `Animations.Stop` on each handle ends it. A clip played with `hold` never finishes: at either end it sleeps on the edge key and its handles stay valid, so `Animations.Direct(handles, forward)` can turn it around. Running backward retraces exactly the path it came forward along, curves included, stops at the start and never wraps; a looping clip that is turned backward first folds its elapsed time into the current cycle, so it unwinds from where it visibly is.

A control plays clips through three attributes. `Clip` plays once when the control starts, or at once when set on a control that has already started. `HoverClip` runs forward while the pointer is over the control and backward when it leaves; `PressClip` does the same between press and release. Entering again while it runs back turns it forward from wherever it is.

### State bindings

A `<Binding>` in the same files says what a control's properties should be at rest, while hovered and while pressed: one `<BindingTrack Property Rest Hover Press/>` per property. A left-out `Rest` is the property's value when the binding attaches, a left-out `Hover` is the rest value and a left-out `Press` is the hover value. `Frequency` and `Damping` set how the state eases and fall back to the palette's `StateFrequency` and `StateDamping`.

A control names a binding with `StateBinding`. On the first pointer event it attaches a `StateBinding`, which is the button's mechanism made general: it owns a signal set to 0, 1 or 2 by the pointer and a spring on its own `state` that follows it, and every time `state` changes it blends each property between its rest, hover and press values and writes the result. A spring that overshoots carries the property past those values, which is where the bounce comes from.

### What ships

The engine's `Animations/UI.anim.xml` holds what any host can name: the `underline` hover clip, which grows a two-pixel bottom edge, and the `menu-row` binding, which every context-menu row carries in code — an accent bar on the row's left edge and a small nudge of its caption on hover. Thorium's own `Thorium.anim.xml` adds `accent-grow`, which draws the title-bar accent bar in when a window opens, and `chrome-press`, a bottom edge that steps up on hover and press for the Save and Add-vault buttons. Thorium's `Effects/Thorium.effects.xml` holds `title-in`, which lifts and fades a window title's letters in one after another. The engine's `Effects/UI.effects.xml` holds `expander-open` and `expander-close`, which turn a file tree's folder arrow as the folder opens and closes: the new arrow starts pointing the way the old one did and rotates to rest. A binding or clip named in engine code must live in the engine's file, since a name no loaded file defines throws when it is first used.

A context menu slides down out of the line it opens from. Its panel has a `reveal` value from 0 to 1, driven by the engine's `menu-open` clip; the panel is laid out at full size but pushed up by the part not yet revealed, and everything above its anchor is clipped away, so its border and rows move down together. Closing is instant.

Folders in a file tree open and close with their rows. Opening a folder rebuilds the list and grows each newly shown row from zero height to the row height, so the rows below slide down. Closing shrinks the folder's rows to zero and rebuilds the list once the last one finishes, which is what `Tween`'s `onDone` is for. Clicking again while a folder is closing finishes the close at once.

`Animations.StopAll(target)` stops every track on an object; `Control.OnDestroy` calls it, so a tween started from code never writes into a control that has been destroyed.

### Curves

Linear; Sine, Quad, Cubic, Quart, Quint, Expo, Circ, Back, Elastic and Bounce, each as In, Out and InOut; a CSS-style cubic bezier; and a step count. Out and InOut are built from each family's In, so Back and Elastic InOut differ slightly from tables that tune them separately.

## Lifecycle / Flow

```
Animations.Tween(target, property, to, seconds, curve)
	id = a free id, generation + 1
	from = the property's current value
	write the track row: tween, from, to, curve, duration
	if the property lives in a pool
		point the track at the control's row, column, field offset and size

Animation.Step, every frame
	empty AnimationValues
	for each running track
		tween: value = from → to along the curve at elapsed / duration
		spring: step toward the target; asleep once settled
		keyframes:
			if running backward
				elapsed = elapsed - dt, never below 0
			else
				elapsed = elapsed + dt, wrapped by the clip's Loop
			value = the keys either side of elapsed, eased by the earlier key's curve
			at an end: asleep if held, otherwise done
		if the track points at a pool row, and the row is still alive
			write the value into the field
		add the value to AnimationValues
	step the palette fades

Main.Apply, the same frame
	for each row in AnimationValues
		skip it if its id was released or its generation is old
		if the property lives in a pool
			call its after-write method, such as InvalidateLayout
		else
			set the property
		release the id and run its onDone when a tween reports done
```

## Gotchas

`Alpha` on a container fades only the container's own fill, not what is inside it; opacity is per quad and is not inherited.

Every call on `Animations` must come from a main step that lists the animation pools it writes, or from bootstrap, before the frame graph starts. Requests made during bootstrap take effect; a control's `Clip` still plays from `OnStart`, which is where it was put when bootstrap requests were dropped.

Properties that are plain fields rather than pool fields — alpha, border thickness, a button's `state`, a menu's `reveal` — still go through their setters on main, one call per value per frame.

The lanes between main and the animation thread set hard limits per tick. Only 682 animations can be started in one tick; the rest are refused with a warning. Only about 1,024 values reach main per tick; past that the extra tracks quietly skip a tick, so with thousands running each one updates less often. Measured with `--profile-scenario=animation`, see `ClaudeMemory/Decisions/animation-core.md` § Measured at scale.

`StopAll` looks through every binding ever made, and every control calls it when it is destroyed, so destroying a large tree after many animations is slow. That is the likely reason tearing down 20,000 buttons took over two seconds; it has not been isolated.

Hover is one control at a time and bubbles to parents, so a `HoverClip` or `StateBinding` on a panel goes back to rest while the pointer is over a child that handles the event itself, such as a button.

Unknown clip, binding or property names throw: a clip or binding when it is first used, and a track's property when the clip plays, because a clip does not say what type it animates.
