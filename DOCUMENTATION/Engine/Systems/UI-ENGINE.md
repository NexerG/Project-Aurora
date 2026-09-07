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
VerifiedAgainst: 2026-09-07 (draw list)
---
## Overview

The UI is being rebuilt in a new namespace beside the old one rather than migrated in place, so the existing editor keeps running untouched while the replacement grows underneath it. The old stack draws first and the new one composites over it; when the new stack is complete the old one is deleted in a single pass.

The rebuild exists to separate three kinds of data that were previously one row. What the layout pass reads never reaches the GPU at all. What the arrange pass produces and what the paint properties produce go to the GPU as two separate buffers, because they are written by different passes and the old single row made each of them carry the other.

> The stack draws, lays itself out, answers the pointer, renders text with a caret, and paints images, icons, masks and gradients. What is left is porting the thirty-nine existing controls onto it and deleting the old one.

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

The layout struct lives in a pool, because layout reads it in tree order and wants it packed. The two GPU structs are plain fields on the element, because nothing reads them in place — they are copied out into a list once a frame, and an element that is not on screen is never asked for them at all.

## Why the stroke is one pair and not two

The old shader carried both an outline and an edge, and they did genuinely different things. The outline thresholded the distance field from the mask texture further out, stroking the silhouette of a letterform or an icon, and its width was in screen pixels. The edge banded the analytic rounded rectangle inward from the control's own border, and its thickness was in design pixels.

Neither had a single consumer anywhere in the engine — no control set either property and no UI document authored either.

They collapsed into one `edge` pair whose meaning the kind selects: on an `MTSDFControl` it strokes the glyph silhouette, on anything else it bands the rounded box, and it is design pixels in both cases so a themed two-pixel border means the same thing on a letter and on a panel. What is lost is a control carrying both at once, which the icon control documented and nothing used.

## One sampler slot, read three ways

A drawn row names at most one texture and one rectangle within it, and what that texture *means* is decided by the row's kind rather than by a separate field for each use.

On a distance-field row it is the field itself, which is how both a letter and an icon are drawn — the shader cannot tell the two apart, because there is nothing to tell apart. On an image row it is the picture, multiplied into the colour and into the opacity, so a texture with transparent corners keeps them. On a panel row it is a mask: an arbitrary silhouette the rounded rectangle is cut down to.

This is the same shape the stroke took. One slot with three readings is smaller than three slots each with one consumer, and it is the readings that differ, not the plumbing.

The price is that the three readings are exclusive. An image cannot also carry a mask, because there is one texture and one rectangle to name it with. Giving it both means a second index and a second rectangle on every quad in the buffer, including the several hundred a paragraph of text emits, and nothing has yet wanted it.

A row that names no texture samples nothing at all. The absence is a real value rather than an empty one, because the first slot of the texture table is an ordinary texture that something is entitled to use — so the shader tests for the sentinel rather than for zero, and a plain panel issues no sampling instruction.

## Masks

A mask is a capability, not a special case for text, and it is meant to be reached for. A control names any texture in the table and its rectangle becomes that shape.

The mask is applied last, after the stroke. A masked control is one silhouette, so its border follows the mask's outline rather than floating around it as the rounded rectangle it would otherwise have been.

> A mask replaces the shape; it does not tint it. Colour, gradient and stroke all still apply, and all of them are cut by it.

## Gradients

A control can name a gradient instead of a flat colour, and the ramp is evaluated per pixel from a table uploaded once at startup rather than carried on the row — the row holds only which row of that table it wants, and the rectangle the ramp spans.

That rectangle is separate from the control's own box on purpose. Every glyph of a paragraph is its own drawn row, so a ramp measured against each row's box would restart on every letter; measured against a rectangle handed down from above, one ramp runs across the whole run.

The ramp replaces the fill rather than tinting it, so a control with both a picture and a gradient shows the gradient. Nothing does both today.

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

Two kinds of element hold siblings: the root of a window's tree, and the container base every multi-child control derives from. A plain control takes a single child and throws on a second.

Holding siblings and arranging them are separate things. The container base only lifts the restriction; it inherits the single-child measure and arrange, so a bare container places one child and ignores the rest. Deciding where several children go — stacking them, splitting them, scrolling them — is what a container subclass is for.

The root also owns the box the whole tree is laid out in. By default that box is the window's pixels, so a control's coordinates are screen pixels; with scaling switched on it is the window divided by whatever the chosen axis implies, so the tree keeps an authored design size and everything below it scales with the window without knowing anything about it. Pointer positions are converted into the same box before anything is hit-tested.

A resize refits the root, which re-lays the tree and moves the camera's projection box to match.

> A root has no appearance of its own. Until masks arrive it opts out of drawing by being fully transparent — an opaque root is a full-window quad that hides everything the old stack composited underneath.

## The draw list

Every window owns a list of quads, and the order they sit in is the order they are drawn. That order is a depth-first walk of the control tree, which is painter order and layout-dependency order at the same time — a parent before its children, always. Nothing maintains the order between frames, because the walk that produces it is the same walk that reads the tree.

The walk runs at the frame edge, after arrange has settled every rectangle and clip, and it culls as it goes.

```
BuildDrawLists()
	for each window
		clear its list
		Collect(its root, the list)

Collect(control, list)
	if the control is hidden
		return
	if the subtree's bounds do not overlap the clip it inherited
		return
	ask the control to emit its quads
	for each child
		Collect(child, list)
```

Both prunes read rectangles the layout pass already maintains. The subtree test is sound because a child's clip is always a subset of its parent's — arrange either inherits it or intersects it, never widens it — and the chain terminates at the window root's own rectangle. So "off the screen" and "outside some ancestor's clip" are one test, not two, and a scrolled document costs what is visible rather than what it contains.

A control emits nothing when its own rectangle misses its clip, but its children are still offered the walk: the clip is inherited, so a child may be arranged somewhere else entirely.

Culling decides whether a quad is *submitted*. One that is only partly on screen is submitted whole and cut per pixel by the fragment shader, exactly as before.

> Rebuilding every frame is affordable only because of the cull. The list is bounded by what fits on the screen, not by the size of the tree — a note of four hundred thousand letters and one of forty emit the same few thousand quads. Without the cull this would be megabytes a frame and the list would have to be maintained incrementally instead.

## The draw module

The new stack is a second rendering module on every window, sitting beside the old one in the same module list. The compositor blends module outputs in order of a per-module sort key, so the new module carries a higher key and clears its own image transparent, letting the old UI show through everywhere the new stack has drawn nothing.

Mirroring the list to the GPU:

```
MirrorDrawList(image)
	read the list's two arrays and its count into locals, once
	if the list's capacity changed since the last mirror
		wait for the device to go idle
		destroy the old buffers
		for each swapchain image
			create a mapped buffer for the geometry array
			create a mapped buffer for the paint array
		remember the new capacity
	if the count is zero
		return
	copy the geometry prefix into this image's buffer
	copy the paint prefix into this image's buffer
```

The arrays and the count are read once and only once, because the main thread is rebuilding the list while this runs and a growth swaps both arrays out from under a second read. The count captured here is also what the recording that follows draws, so an image never draws a newer count against an older buffer.

> That overlap is the same coarse race the old mirroring ran, made certain rather than occasional by rebuilding every frame. The worst case is a quad drawn at last frame's position for one frame. The fix, when it earns itself, is a small ring of lists rather than a lock.

## Entry points

The engine drives the UI from three places in the tick rather than one, and the order is deliberate.

Input is polled where the old collision handler was called, before entity logic runs. Layout is resolved after entity logic, because anything that invalidates layout from inside a tick — a caret move, a text edit — would otherwise land a frame late and show up as a one-frame lag that is very hard to attribute. The draw lists are built last, at the frame edge, because they read the rectangles and clips layout has just settled.

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

The deepest control under the pointer wins the hit, and the event then walks up the tree until some handler consumes it by returning true. A control with no handler consumes nothing, so the click reaches whatever above it does care, and the event carries the control that was actually under the pointer so an ancestor handling it still knows what was hit.

A control can take itself out of the test by clearing its hit-testable flag. That exists for decorations drawn inside something that owns the interaction — a caret, a selection box — which sit over the thing the pointer is aiming at and would otherwise swallow the click, because the deepest hit is the one that wins.

```
HitTest(control, point)
	if the control is hidden
		return nothing
	if the point is outside the rectangle covering its whole subtree
		return nothing
	for each child, last to first
		if the child is not hit-testable, skip it
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

## The wheel

The wheel is not special. It is a pointer event like the others: it starts at the hovered control and walks up until something consumes it, carrying the scroll amount where a move would carry its delta. A scrolling container is then just a control that handles it, rather than a case the input code has to know the name of.

## Claiming a drag

A drag is claimed, not detected. Nothing starts dragging because the pointer moved with a button down. A control that should be draggable says so with a flag, and a left press then claims the drag for it; anything wanting a drag on some other trigger claims it by hand instead. The flag is per control rather than a property of the base, because most things that drag do not move at all — a splitter, a scroll thumb and a text selection are all drags, and none of them is a thing you can pick up.

Claiming publishes the control as the dragging context, which is how anything else in the engine asks what is being dragged without the two sides knowing about each other, and tells the control's parent that it has lost a child. Destroying a control clears the context, as it clears every other one.

Two things follow from the claim living on the press. A press bubbles, so a draggable container drags when nothing beneath it consumes the press — which is usually what you want and occasionally a surprise. And a control that overrides the press without calling its base never drags, flag or not.

Every tick after that, the drag is resolved against whatever is beneath it, and exactly one control is the drag's target — the same arrangement hovering uses, where one control is hovered and the notification bubbles from it. The target is told the drag arrived, told again every tick it stays, and told when it leaves. All three walk up until something takes them, exactly as a click does, so a container can answer for a child that does not care.

Finding what is beneath a drag is the hover's own walk with one addition: the dragged control's whole subtree is taken out of the answer. It sits under the pointer by definition — that is what dragging means — so without the exclusion it would answer every time and nothing else could ever be found. The exclusion is applied at the subtree's root rather than to the one control, because the parts of a dragged thing are under the pointer too, and a dragged tab reporting that the drag is over its own label is no more useful than it reporting itself.

```
CheckDrag(point, root)
	if nothing is being dragged
		return
	find the deepest control under the point, ignoring the dragged subtree
	if that is not the target it was last tick
		tell the old target the drag left it, bubbling
		make this one the target
		tell it the drag arrived, bubbling
	tell the target the drag is over it, bubbling
```

Losing a child *out* of a container is a different event from something being dragged *over* it, and only the container is told — a tab strip needs to know a tab has left it, while the tab itself is the thing being dragged and needs no telling.

The release ends the drag, and it is handled before the checks that guard an ordinary click. A click only counts where it started, so releasing somewhere else cancels it; a drag is the opposite — it ends wherever the pointer has got to, and that is rarely where it began. The target is told the drag has left it and then that it was dropped on it, and only afterwards is the drag forgotten.

The drop goes to the target and stops there. It is not offered up the tree the way a click is, so a container that wants to accept drops aimed at its children has to be the thing under the pointer, not merely an ancestor of it.

The claimant hears the gesture too, and hears it differently from everyone else. Where the target is told things that bubble, the claimant is handed its position every tick and told once when the gesture ends — delivered straight to it, never walked up, because a drag belongs to the control that claimed it and to nothing above. That directness is what a splitter, a scroll thumb and a window frame all need: each of them only wants to know where the pointer is now, and each of them answers by moving something.

A drag whose release was never seen is abandoned rather than left running. The pointer can leave every window mid-gesture and let go somewhere the engine hears nothing about, so the button is re-checked each tick, and a claim held with the button up ends immediately — the target hears the drag leave, the claimant hears it stop, and no drop is offered, because where the release happened is not knowable. The claim also exempts its own window from the rule that a window with the pointer outside it processes nothing, since a pressed button captures the pointer to the window it went down in and that window is the only one still hearing about the gesture.

> Two halves are still missing. Nothing previews a drag and nothing reparents, so a drop that should move something between containers has no way to show what it would do; and there are no context menus. Those arrive with the controls that need them — tabs, chiefly — and until then the old stack owns both.

## Splits and scrolling

A splitter is a button that resizes the sibling ahead of it. It has no idea what a pane is — it looks at the stack it sits in, takes the control before it and the control after it, and writes sizes onto them. That is the whole design, and it is why a splitter dropped anywhere else is inert rather than broken: with no stack for a parent, there is no sibling ahead, and it simply does nothing.

Which size it writes depends on what it finds. Between a pane with a size and a pane taking a share of what is left, it writes the first pane's size and the second absorbs the difference. Between two panes that both take shares, it cannot write a size at all without dropping one of them out of share-sizing entirely, so instead it splits their combined share between them — the boundary moves and the pair's total does not. A share of zero would stop being a share, which is why each side is floored a pixel above its minimum rather than at it.

The size is computed from where the grab began, not accumulated from each tick's movement. Accumulating drifts: once a pane hits its floor the pointer keeps moving while the pane cannot, and the two part company, so that dragging back the other way does nothing until the accumulated debt is paid off.

```
OnDrag(point)
	pane = the sibling ahead of this one
	if there is none
		return
	wanted = the pane's size when the grab began + how far the pointer has moved since
	if both panes take shares
		clamp wanted between the two floors
		give the pane its proportion of the pair's combined share
		give the next pane the remainder
	else
		write wanted onto the pane as its size, no smaller than its minimum
```

A split view is a stack panel and nothing more. It exists as its own type only so that collapsing a split can never reach chrome someone authored, and it paints nothing of its own — the panes inside it are what you see.

A scrollable is a viewport with one child, and its trick is that it lies in one direction only. It measures its child against its own size rather than against infinity, so the child lays out to a real width and wraps where it should; but whatever the child comes back with, the viewport reports its own size upward. A container that sums past what it was offered is not an error — that overflow *is* the scroll range. Then the child is arranged at its full size and shifted by the scroll offset, and the clip does the rest.

A child that cannot size itself is the one case this cannot absorb. Asked how big it wants to be with no constraint to answer against, it reports the whole offer back, and a stack that sums such a child arrives at a number no scroll range can be computed from. The viewport treats a measurement that large as no measurement at all and falls back to its own size, which makes the range zero. That is the honest outcome — the content genuinely did not say how big it is — rather than a repair for authoring it that way.

Space for a scrollbar is reserved on any axis that can scroll, whether or not a thumb is currently showing. Reserving it only when needed would mean the appearance of a thumb narrows the content, and narrowing the content is exactly what can make it tall enough to need the thumb — a viewport that flickers between two states forever.

There is a thumb per axis, and each is sized by the ratio the track bears to the content, floored so it stays big enough to grab. When an axis fits, its thumb is arranged at zero size, which is enough to make it disappear completely: a zero-area rectangle shares no area with anything, so it neither draws nor answers the hit-test.

Thumbs are appended after the content rather than inserted before it. The hit-test walks children backwards and takes the first thing it finds, so the last child wins an overlap — and a thumb that lost its overlap to the content beneath it could never be grabbed.

## Building a tree from a document

A tree can be written as XML instead of constructed in code. An element names a type, its attributes name properties on that type, and nesting is parenting — so a document is read by creating the type the root element names, setting what its attributes ask for, and recursing.

Attributes bind by name rather than by position. A property is bindable when it carries the element-property attribute, and the name in that attribute is what the XML writes — so the same document text keeps working when the property behind it is renamed. Values convert through the type's own converter, which is how a padding of `"16"` and a padding of `"8,4"` and a padding of `"1,2,3,4"` all reach the same property, and how a corner radius names one, two or four corners.

```
Parse(element)
	create the type this element names
	for each attribute
		find the member whose element-property name matches
		if that member is an action, resolve the named method and add it
		else if it is an enumeration, parse the value by name
		else convert the value with the member type's converter
	if what was created is a window root
		give it its authored rectangle and register it as needing layout
	for each child element
		parse it the same way
		if it is a control, add it as a child
		else put it in the one list on the parent that accepts its type
```

The last branch is what lets a document carry things that are not controls at all — a list of gradient stops, a set of column definitions — without the parser knowing any of their names.

## Text and the caret

A run of text is one control holding a string and its settings, and the measurer turns that into lines, segments and caret geometry without a single glyph object ever existing. The run owns no per-character storage at all: a paragraph of four hundred letters is one node in the tree, and it emits a quad for each letter that is actually on the screen.

Per-character settings — bold, colour, italic, size — live on the run as a list of spans, because a glyph is only a quad in a list and has nowhere to keep state of its own. A span carries a length, a face and a colour; the spans tile the string in order and the last one absorbs whatever is left, so appending to the text needs no change to the span list at all. Each span becomes one input run for the measurer, and the measurer already reports which run every stretch of a line came from — which is how a line that crosses from regular into bold and back gets each stretch drawn in the right face.

The measurer takes a slice of the string rather than a string of its own, so the spans of one paragraph share one string and nothing is copied to measure it.

Arrange only places the block. Cutting the glyphs happens at emit, against the clip of the moment, so a run that scrolls does no work for the lines that scrolled away.

```
Arrange(rect)
	shrink the rect by padding
	offset the text inside the box by the leftover space, weighted by the authored position
	remember that as the text origin

Emit(list)
	for each line
		if the line sits entirely above the clip
			skip it
		if the line starts below the clip
			stop
		pen = the text origin
		baseline = the text origin plus the line's baseline
		for each stretch of that line
			take the colour and the face of the span it came from
			for each character in it
				cut the glyph's cell out of the atlas
				append its matrix, its clip and its atlas coordinates to the list
				pen = pen + the glyph's advance
```

Skipping a line costs the next one nothing, because the pen restarts at the origin on every line rather than carrying across them. The run never emits a quad for itself — its own box is the node, the glyphs are the ink.

> A line that is only half inside the clip is emitted whole, and the fragment shader cuts it. Culling is by line and not by glyph: a document scrolls vertically, so the horizontal case buys little and costs a test per character.

The caret belongs to the document, not to the run. Pressing anywhere in the text asks the run which character the point fell on — resolved from the same measured lines the glyphs were placed on, taking the slot after a character once the point is past its midpoint — and the document places its own caret beside that character. A caret is an ordinary control with a narrow box, blinking on its own tick.

> The slot at the end of a wrapped line belongs to the line below it. Without that rule the caret sits off the right edge of the window instead of in front of the word that wrapped.

> There is no way to author any of this in XML yet. The new stack reads no documents of its own, so spans are built in code, and the format that will express them is designed with the document rather than now.

## Related

[[Entity]], [[Transform]], [[VULKAN]], [[Renderer Module]], [[Bootstrapper]]
