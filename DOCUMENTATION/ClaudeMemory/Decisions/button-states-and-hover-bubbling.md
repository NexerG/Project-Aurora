# Decision — enter/exit were dead events, and a button's visual state is the first thing that needed them

**Date:** 2026-08-18
**Status:** LANDED. **GUI-verified by the user** — hover and press tints, row spacing, the
left-aligned note names, and activation on release with press/release pairing all work on screen.
**Scope:** `ArctisAurora.Core.UISystem` (`UICollisionHandling`), `...Controls` (`VulkanControl`),
`...Controls.Interactable` (`ButtonControl`), `Periodic` (`UI.xml`, `VaultBrowserControl`).

## Context

Two open items from the same day — the vault browser's rows "read as one block" and the window chrome
had "no hover or press feedback" — turned out to share a cause below the app.

**`onEnter` has never fired.** `ResolveOnEnter()` had **zero callers** in the solution. `ResolveExit()`
had exactly one, in the `SolveHover` branch that runs only when the pointer is over *nothing* in the
tree — so moving from one control straight to another fired neither. Both events are declared on
`VulkanControl` and published into the XSD of all three projects. Same shape as the `dragging` field
found in [[document-selection]] decision 6: declared, read, never assigned.

## Decisions

### 1. Enter/exit bubble on flags, not on a hover chain diff (user, 2026-08-18)

The deepest hit under a button caption is a `GlyphControl`, two levels below the `Button` — a label's
glyphs, not the label and not the button. Firing enter/exit on the deepest control alone therefore
never reaches the thing that wants to tint itself.

`bubbleEnter` / `bubbleExit` join the existing `bubble*` set on `VulkanControl`, `ResolveOnEnter` and
`ResolveExit` walk to the parent the way `ResolveOnClick` already does, and `BubbleAll()` sets them —
so `GlyphControl` and `LabelControl` pass hover up for free, on the reasoning they already use for
clicks.

**Rejected: an ancestor-path diff in `SolveHover`** — keep the hovered control's ancestor list, diff it
frame to frame, fire exit on what left the path and enter on what joined. Correct by construction and
flicker-free by construction, but it is new state on the collision handler, it redefines `onExit` from
"the pointer left the tree" to "the pointer left this subtree", and it abandons the idiom every other
mouse event in the file already uses. The flag version was chosen for consistency.

**The flicker the flag version is supposed to have does not materialise.** Moving from a button's
label onto the button's own padding fires exit-on-glyph (bubbling to the button) and then
enter-on-button — the button un-tints and re-tints. Both happen inside one `SolveHover` call, and
both only `MarkContentDirty` on the pool row; the GPU upload happens later in the frame, so it sees
one tint per frame and the intermediate value never reaches a pixel. This is a property of the
deferred pool upload, not of the bubbling — it would stop being true if a tint write ever became a
direct upload.

### 2. The release stays with whatever the cursor is over — a release off the control fires nothing

**Tried and reverted (user, 2026-08-18).** `SolveLMBRelease` was briefly routed to
`activeControl ?? hovering` — mouse capture, pairing the release with the press target — on the
reasoning that a button pressed and dragged off would otherwise stay stuck in its press tint.

That reasoning was already stale by the time it was written. Decision 1 gives `ResolveExit` a path to
the button, and `ButtonControl.ResolveExit` clears `pressed` as well as `hovered`, so dragging off
un-presses the button on the hover event — the release never had to do it. What capture *did* buy was
a bug: it fires `onRelease` on a control the pointer has left, which is an activation the user never
asked for. Press, think better of it, drag away, release is the standard way to cancel a click.

So `hovering?.ResolveOnRelease()` stands as it was. The drag branch above it was never touched, so
document drag-selection keeps its own path.

**Still stuck in one case:** `Engine.HandleUI` returns before `SolveHover` when
`uiCollisionHandler.isInWindow` is false, so a pointer that leaves the *window* while held fires no
exit and the button keeps its press tint until the pointer returns and moves to a different control.
The GLFW cursor-enter callback would be the place to fire exit; not done, nobody asked.

### 3. A button's base colour had to become real before it could be restored

`ButtonControl`'s constructor wrote `controlData.style.tint` directly and left `controlColorHex` at
the inherited `"#FFFFFF"` — so the property said white while the control painted grey. Any
state machine that restores "the base colour" from `controlColorHex` would have turned every
unstyled button white on the first mouse-out.

The constructor now sets `controlColorHex = "#8C8C8C"` instead, which is the same colour it always
painted (0x8C/255 = 0.549 against the old 0.55) and makes the property truthful. `ApplyState` writes
`controlData.style.tint` directly and never touches `controlColorHex`, so the authored base survives
every state change.

`hoverColorHex` and `pressColorHex` are `[A_XSDElementProperty]` strings defaulting to null, falling
back down the chain — press → hover → base. A button that declares neither behaves exactly as before.
Deliberately *not* derived from the base by lightening it: the close button wants Windows' red, which
is not a function of `#141414`.

`ResolveExit` clears `pressed` as well as `hovered`, which is what makes the drag-off case land on
the base colour from both directions.

### 4. Rows are plates that are invisible at rest

The vault rows were the `ButtonControl` default grey, stacked with no gap, against a `#171717`
sidebar — one continuous bright slab with text on it. They are now tinted *to* the sidebar ground, so
at rest the sidebar reads as a plain text list (Obsidian's look) and the plate only appears under the
pointer. 2px `Spacing`, 6px text inset, folders `#8A8A8A` against notes `#D4D4D4` so the dimmer
entries are the non-actionable ones.

Note labels also needed `horizontalPosition = 0f`: `VulkanControl.Arrange` centres a single child by
default (0.5), so every note name had been floating in the middle of its 220px row.

Title bar buttons take `ColorHex="#141414"` to sit flush in the bar rather than being three grey
slabs on it, hover `#2A2A2A`, press `#3A3A3A`; close hovers `#C42B1E` and presses `#A82318`.

### 5. A button activates on release, not on press (user, 2026-08-18)

Pressing the X and having the process die under the cursor before the button comes back up does not
feel like a button. Every live button action moved from `onClick` to `onRelease` — the three window
chrome actions in `UI.xml` and the vault row's `Open`, which is the whole set; nothing else in the
solution registers a click as a command. Presses that are *not* commands stay on press:
`TitleBarControl` grabs the window on click, and the document editor places its caret on click.

`ButtonControl` still sets `pressed` on click, since that is the visual state and it belongs on the
way down. With decision 2 leaving the release on `hovering`, press-then-drag-away-then-release is now
a real cancel: the button un-tints on exit and no action fires.

### 6. A control declares whether it can hold the active context, and the release compares against it

Moving activation to release opened a hole: a press that began elsewhere activated whatever it was
released over, so pressing in the document, dragging up to the X and releasing closed the window.

`VulkanControl.canBeActiveContext` is a virtual `bool`, default true, overridden false on
`GlyphControl` and `LabelControl`. `ActiveTarget(control)` walks up to the first control answering
true; `SolveLMBPress` stores that instead of the raw hit, and `SolveLMBRelease` fires only when the
release resolves to the same control (user, 2026-08-18).

Resolving *before* comparing is the whole trick. The raw hit is always a `GlyphControl` — the deepest
thing under a caption — so a bare `hovering == activeControl` would have died on any click that
drifts a pixel onto a neighbouring glyph, or off the caption onto the button's own padding. Resolved,
both ends of that click answer `Button` and the comparison is a plain `==`.

The cancel path falls out: press a button, drag to the document, release — the release resolves to
something else, nothing fires, and the button had already un-tinted on `ResolveExit`.

**Rejected: a shared-ancestor walk.** The first version computed the deepest common ancestor of the
press and release chains and delivered `ResolveOnRelease` from there. It works, and it needs no new
member on `VulkanControl` — but it re-derives "which control is this gesture on" out of two chains on
every release, when it is a property the control can simply answer. Six lines against twelve plus a
`HashSet` per release, and the flag leaves `activeControl` meaning something worth having: a glyph
was never a sensible answer to "what is the active control".

### 7. Hovering text had been silently rewriting `activeControl`

`GlyphControl` implemented `IContext`, and its `OnContextAdded` reassigned
`UICollisionHandling.activeControl` to its parent. `SolveHover` calls `OnContextAdded` on the newly
hovered control — so **moving the pointer across any text changed the active control**, no click
involved. On its own that made decision 6 worthless: press the X, drag over a note title, release,
and the recorded press target had been overwritten on the way.

`GlyphControl`'s `IContext` implementation is **deleted**. `ActiveTarget` reaches the same control by
resolution rather than by side effect — a press on a document glyph resolves to the `TextRun` exactly
as the old reassignment did, so `Text.Write`'s `activeControl as TextControl` fallback is unchanged.

**One behaviour goes with it.** `GlyphControl.OnContextRemoved` propagated to its parent, so moving
the pointer off the whole UI tree fired `TextInputControl.OnContextRemoved` → `CommitEdit()`.
**Confirmed unintended (user, 2026-08-18)** — a side effect of the glyph's `IContext`, not a save
path. `DocumentEditSession` plus Ctrl+S is the real one, so it is not replaced.

### 8. `IContext` callbacks name the context that changed

Deleting `GlyphControl`'s `IContext` narrowed decision 7's `CommitEdit` leak but did **not** close it.
`SolveHover` fires `OnContextAdded`/`OnContextRemoved` on the control it is *hovering* — so a
`TextInputControl` hit directly rather than through a glyph (a `TextRun`'s own box, a standalone
`TextInput`) still committed its edit when the pointer left the UI tree. The glyph deletion only
removed the common case.

**Rejected: taking `IContext` out of the hover path.** Tried first, and it is the wrong shape —
`Hovering`, `Dragging` and `ActiveControl` are three peer `[A_ActiveContext]` entries (user,
2026-08-18), so hover is as entitled to the lifecycle as focus is. Deleting its calls fixed the
symptom by removing a capability.

The actual defect is that `IContext` could not say *which* context changed. One interface served
three contexts, and the only implementer — `TextInputControl` — writes its methods meaning *focus
gained* and *focus lost*, so it committed on any removal from any context.

`OnContextAdded(string context)` / `OnContextRemoved(string context)` now carry the name, the two
solvers pass `"Hovering"` and `"ActiveControl"`, and `TextInputControl` commits only on
`"ActiveControl"`. Hover keeps its lifecycle and `CommitEdit` fires exactly when a click moves the
active control off a text input, which is what it was written for.

The registered name was `"Draggin"` and is now `"Dragging"` — nothing read the string, so the rename
is the attribute alone.

### 9. The drag context notifies too

`Dragging` carried `[A_ActiveContext]` — registered, readable through
`Context.Get<VulkanControl>("Dragging")` — but nothing ever fired `IContext` for it, so no control
could learn that it had started or stopped being the drag target. Only the dragged control found
out, through its own `StartDrag`/`ResolveDrag`/`StopDrag`.

`UICollisionHandling.SetDragging(control)` is now the one writer, and the three assignment sites go
through it: `VulkanControl.StartDrag`, `SolveLMBRelease`'s clear, and `SolveDrag`'s stale-release
clear.

**It assigns before it notifies**, unlike the `Hovering` and `ActiveControl` dances, which call
`OnContextRemoved` on the outgoing control while the field still points at it. `SolveLMBRelease`
already documented the opposite order for this field — *cleared before the callbacks, so a handler
asking whether a drag is live gets no* — and that invariant is the more defensible one, so the drag
setter keeps it rather than matching the other two. The other two are left alone.

The field stays a public static, so the funnel is a convention rather than an enforcement; making it
private behind a getter would touch every reader.

Nothing observable changed — the two `StartDrag` callers, `TitleBarControl` and
`DocumentEditorControl`, are not `IContext` implementers. This wires the capability to parity with
the other two contexts.

**Still hand-rolled.** `Context.Set` is the registry's single funnel and would be the natural place to
fire both callbacks automatically for every registered context, but `activeControl` and `dragging`
are assigned *directly* rather than through it, so `SolveHover`, `SolveLMBPress` and `SetDragging`
each carry their own copy of the dance. Unifying means routing `SolveLMBPress`, `SetDragging` and
`DocumentControl`'s caret repoint through `Context.Set`.

**Dragging off a held button and back on does re-activate on release** — correct — but the press tint
does not come back on re-entry, because `ResolveOnEnter` sets `hovered` and nothing tells it the
button is still down. `InputHandler.instance.IsKeyDown(Keys.MouseLeft)` is what it would read.
Cosmetic; left alone.

## Verified

- Builds clean, 0 errors.
- Boots from the output directory through every bootstrap step to all three threads, no stderr.
  `UITypeSchema.xsd XSD updated` on the run, confirming `HoverColorHex`, `PressColorHex`,
  `BubbleEnter` and `BubbleExit` were reflected into the schema — the shipped `.xsd` did not have
  them and nothing validates XML against it at parse time, so authoring them was safe before the
  regeneration.
- **GUI-verified by the user, in two passes.** First the tints, the row spacing and the left-aligned
  note names; then activation on release, the press/release pairing and the cancel path, after
  decisions 5–7 landed.

## Still open

- `Context.Set` does not fire the `IContext` callbacks, and `activeControl`/`dragging` bypass it
  entirely — see decision 8. `Dragging` is a registered context nothing ever notifies.
- A button held while the pointer leaves the *window* keeps its press tint — see decision 2.
- No hover feedback anywhere but `ButtonControl`. Rows, chrome buttons — that is the whole set.
- Captions are still ASCII (`-`, `[]`, `X`); the atlas has no `−`, `□` or `✕`.
- Rounded corners are not in this: the analytic rounded-box SDF needs a corner radius on
  `ControlData` (48 bytes and 16-aligned today, so it wants a repack to 64) and a panel-vs-glyph
  branch in `UI.frag`. See the MTSDF note below.

## MTSDF and rounded edges, for when that lands

The alpha channel of an MTSDF is a **true Euclidean** distance; the RGB median is not — the median
reconstructs a corner as the intersection of half-planes, which is why msdfgen keeps corners sharp.
Offsetting a field is what rounds it: `d - r` is the Minkowski sum with a disc of radius `r`, so
convex corners come back as arcs. Iso-lines of the true field around a convex corner are arcs;
iso-lines of the median are mitres. One texture fetch, two offset joins — `.rgb` for sharp, `.a` for
round.

**Panels should not sample it.** A baked field is metrically correct only at the aspect ratio it was
baked at; stretched across a 220x24 row the radius stretches with it and the AA band goes
anisotropic. Glyph quads never hit this because they are uniformly scaled. Rounded panels want
`sdRoundedBox` evaluated in the fragment shader — the same maths the alpha channel encodes, computed
rather than sampled, exact at every size and aspect, no atlas slot. `UI.vert` already declares
`inUV` and never uses it, and the control's pixel size falls out of the transform basis
(`length(m[0].xyz)`), so the only genuinely new per-control datum is the radius.

Related: [[window-chrome-and-label]], [[vault-browser-and-shell]], [[document-selection]],
[[verify-what-the-user-sees]], [[glyphs-as-pool-data]]
