---
date: 2026-09-06
tags:
  - d_System
cssclasses:
  - Aurora.css
Status: In progress
Linker:
  - "[[Arctis Aurora]]"
System:
  - "[[UI-ENGINE]]"
Dependencies:
  - "[[Bootstrapper]]"
  - "[[Entity]]"
  - "[[VULKAN]]"
Implementors:
  - "[[UI-ENGINE]]"
Namespace: ArctisAurora.Core.UI
SourceFiles: AuroraEngine/Core/UI/*.cs, AuroraEngine/Core/Rendering/Modules/UIEngineModule.cs, AuroraEngine/Shaders/UIEngine/*
VerifiedAgainst: 2026-09-06
---
## Overview

The UI is being rebuilt in a new namespace beside the old one rather than migrated in place, so the existing editor keeps running untouched while the replacement grows underneath it. The old stack draws first and the new one composites over it; when the new stack is complete the old one is deleted in a single pass.

The rebuild exists to separate three kinds of data that were previously one row. What the layout pass reads never reaches the GPU at all. What the arrange pass produces and what the paint properties produce go to the GPU as two independent buffers, because a window resize and a colour change dirty completely different bytes and had been forcing each other's uploads.

> The stack draws, lays itself out and answers the pointer. Text and images are designed and agreed but do not exist yet.

## Two things are called a control

This is the single most confusing thing about the system, and reading a sentence with the two swapped makes it say the opposite of what it means.

A **Control** is the CPU-side thing: a node in the tree, with logic, layout and children. One per element on screen.

A **VulkanControl** is the GPU-side thing: one drawn quad, a plain struct with no behaviour, tagged with which of three kinds it is. A `Control` owns anywhere from zero to many of them.

A panel owns exactly one VulkanControl. A run of text owns one per glyph, which is what lets a paragraph exist as a single Control holding a string rather than one object per letter.

The kinds are `MTSDFControl` for anything drawn from a distance field, `PanelControl` for a rounded box, and `ImageControl` for a textured quad.

## The three kinds of data

`ArrangeData` is the measure and arrange state — the authored width, height, margin, padding, alignment and star weights, the arranged and clip rectangles the pass produces, and two caches used to make collision and insertion cheap. It is 140 bytes and it never leaves the CPU.

`ControlGeometry` is what arrange produces for the GPU: the baked model matrix, the clip rectangle the fragment shader discards against, and the rectangle a gradient ramps across. It is 96 bytes.

`VulkanControl` is paint: the kind, the texture coordinates, the tint, the texture index, the corner radii, one stroke colour and width, and a gradient index. It is 92 bytes.

The two GPU structs live in one pool and the layout struct in another, so a `Control` holds a row in each. An element's row in the draw pool is what the renderer mirrors; nothing walks the tree to draw.

## Why the stroke is one pair and not two

The old shader carried both an outline and an edge, and they did genuinely different things. The outline thresholded the distance field from the mask texture further out, stroking the silhouette of a letterform or an icon, and its width was in screen pixels. The edge banded the analytic rounded rectangle inward from the control's own border, and its thickness was in design pixels.

Neither had a single consumer anywhere in the engine — no control set either property and no UI document authored either.

They collapsed into one `edge` pair whose meaning the kind selects: on an `MTSDFControl` it strokes the glyph silhouette, on anything else it bands the rounded box, and it is design pixels in both cases so a themed two-pixel border means the same thing on a letter and on a panel. What is lost is a control carrying both at once, which the icon control documented and nothing used.

## Masks

A mask gives a control an arbitrary silhouette instead of a rectangle, and it is meant to be reached for. The default mask paints, an invisible mask paints nothing, and a control can name any texture in the table.

Because a mask is a capability rather than a special case for text, the sampler set serves all three kinds. A panel with a mask samples it; a panel without one does not, and the shader branches on whether a mask is assigned rather than on which kind the control is.

## Depth

The camera projects an orthographic box that only accepts world z between −512 and −0.01, so anything at z of zero is clipped by the near plane and draws nothing at all — with no error, no validation message and no clue as to why.

A window root sits at −10 and each level of depth steps one thousandth of a unit toward the camera, which is both painter order and the order the tree is walked in.

## Laying out

Layout is the two-pass shape the old stack used, carried over unchanged in behaviour and moved onto the pooled arrange row. Measure asks an element how big it wants to be given a box; arrange tells it the rectangle it actually got. A plain control handles a single child, offering it the box minus its own padding and then, if it has no size of its own, shrinking to fit what the child asked for.

Arrange is where an element becomes something drawable. It writes the rectangle into its arrange row, bakes a scale-and-translate matrix into its geometry row at one depth step nearer the camera than its parent, and settles its clip rectangle — inheriting the parent's, or intersecting it with its own rectangle when the element clips.

Nothing lays out every frame. Changing an authored property marks the element dirty and walks up the tree marking ancestors, stopping at the first one already dirty, and registers the topmost newly dirtied element as a root. The tick then resolves each root once.

```
ResolveLayout()
	if nothing is dirty
		return
	take a copy of the dirty roots and clear the set
	for each root
		if its measure is dirty
			offer it its own arranged size, or infinity if it has never been arranged
			measure it
			arrange it into its arranged rectangle, or into its desired size if it has none
		else if only its arrange is dirty
			arrange it into the rectangle it already has
		refresh the subtree caches under it
```

Two caches ride on every element: the rectangle covering it and everything beneath it, and how many elements its subtree holds. Both are filled by a separate walk after arrange rather than by arrange itself, so no future override can forget to maintain them. In debug builds the same walk is repeated independently and any disagreement is logged as an error.

## The window root

One kind of element holds siblings: the root of a window's tree. Everything else takes a single child, which is what containers exist to change.

The root also owns the box the whole tree is laid out in. By default that box is the window's pixels, so a control's coordinates are screen pixels; with scaling switched on it is the window divided by whatever the chosen axis implies, so the tree keeps an authored design size and everything below it scales with the window without knowing anything about it. Pointer positions are converted into the same box before anything is hit-tested.

A resize refits the root, which re-lays the tree and moves the camera's projection box to match.

> A root has no appearance of its own. Until masks arrive it opts out of drawing by being fully transparent — an opaque root is a full-window quad that hides everything the old stack composited underneath.

## Dense order and per-window ranges

The draw pool is shared by every window, and the order rows sit in is the order they are drawn. That order is a depth-first walk of the control tree, which is painter order and layout-dependency order at the same time — a parent before its children, always.

The pool does not maintain that order as elements are created. Inserting or reparenting flags the pool, and at the frame edge it asks for the whole permutation and applies it in one pass. Both pools are walked together, since one holds a row per element and the other a row per drawn quad, but the walk yields a different handle for each.

Because the order is depth-first, a window's tree is a contiguous run of rows, and a window can be drawn as a slice of the shared pool rather than a buffer of its own.

```
RefreshWindowRanges()
	if no pool version moved and nothing invalidated the ranges
		return
	remember the versions
	first = 0
	for each window
		count = the size of its root's subtree, or zero if it has no root
		if the window's range changed
			publish it and mark every one of its command buffers for re-recording
		first = first + count
```

The count is walked fresh rather than read from the subtree cache, because destroying an element detaches it without invalidating any layout — the cache would be stale at exactly the moment the range is recomputed.

## The draw module

The new stack is a second rendering module on every window, sitting beside the old one in the same module list. The compositor blends module outputs in order of a per-module sort key, so the new module carries a higher key and clears its own image transparent, letting the old UI show through everywhere the new stack has drawn nothing.

Mirroring the pool to the GPU:

```
MirrorPool(image, dirtyFirst, dirtyLast)
	if the pool is empty
		return
	if the pool capacity changed since the last mirror
		wait for the device to go idle
		destroy the old buffers
		for each swapchain image
			create a mapped buffer for the geometry column
			create a mapped buffer for the paint column
			copy both columns in full
		remember the new capacity
		return
	clamp the dirty range to what is live
	copy the geometry column's dirty range into this image's buffer
	copy the paint column's dirty range into this image's buffer
```

Because the two columns have their own dirty ranges, a resize copies 96 bytes per changed row and a repaint copies 92, rather than both paying for either.

## Entry points

The engine drives the UI from two places in the tick rather than one, and the split is deliberate.

Input is polled where the old collision handler was called, before entity logic runs. Layout is resolved after entity logic, because anything that invalidates layout from inside a tick — a glyph resync, a caret move — would otherwise land a frame late and show up as a one-frame lag that is very hard to attribute.

```
Poll(window)
	if the window has no tree
		return
	turn the window's pointer position into the tree's own units
	if the pointer is no longer inside this window
		if the hovered control belongs to our tree
			clear the hover
		return
	resolve the deepest control under the pointer
	if it is not the one hovered last tick
		dispatch an exit from the old one
		make the new one the hovered control
		dispatch an enter from it
	dispatch a move from the hovered control
	for each mouse button
		if it went down this tick
			dispatch a press
		if it came up this tick
			dispatch a release
```

Every window polls, and the hover is global because there is one pointer — hence the check that a leaving window only clears a hover that was its own.

## Hit-testing

Every control is hit-testable. There is no opt-out flag, and a decoration sitting over a button no longer has to be excluded from the test by hand.

The deepest control under the pointer always wins the hit, and the event then walks up the tree until some handler consumes it by returning true. A control with no handler consumes nothing, so the click reaches whatever above it does care, and the event carries the control that was actually under the pointer so an ancestor handling it still knows what was hit.

```
HitTest(control, point)
	if the control is hidden
		return nothing
	if the point is outside the rectangle covering its whole subtree
		return nothing
	for each child, last to first
		ask the same question of that child
		if it answered, return that answer
	if the point is inside both the control's clip and its own box
		return the control
	return nothing
```

The walk rejects whole subtrees using a cached bounding rectangle that covers an element and everything under it, which is a strictly tighter test than the inherited clip rectangle the old stack used.

Children are asked last to first because that is the reverse of the order they were drawn in, so the sibling painted on top is the one that answers. The old stack asks them front to back and takes the first hit, which quietly hands the click to whichever overlapping sibling happens to be underneath.

The test itself is the intersection of the control's clip rectangle and its own arranged box. Both are axis-aligned, and nothing in the UI rotates, so an axis-aligned test is exact rather than an approximation. If rotation ever arrives, this is the one place that changes.

## Hovering

Exactly one control is hovered: the deepest one under the pointer. Its parents are not hovered. Hovering a letter does not mean the paragraph, the document, the panel and the window are all hovered too.

What reaches the parents is the *event*, by the same bubbling every other pointer event uses. The letter is told it was entered; if it does not consume that, its run is told, then the paragraph, and so on up until something does. That is what makes hovering a button's label light the button, without the button having to know a label exists.

When the pointer moves from one control to another, the old one is sent an exit and the new one an enter — each bubbling on its own. Moving the pointer within a control sends it a move, every tick.

## Text and the caret

A run of text is one control holding a string and its settings, and the measurer turns that into lines, segments and caret geometry without a single glyph object existing.

The caret belongs to the document. Pressing a glyph tells the document which run was hit and which character index within it, and the document places its own caret beside that character.

> Per-character settings — bold, colour, italic, size, animation — have to live in the run's data, because a glyph exists only as a GPU row and has nowhere to keep state of its own. The document format has no way to express those spans yet.

## Related

[[Entity]], [[Transform]], [[VULKAN]], [[Renderer Module]], [[Bootstrapper]]
