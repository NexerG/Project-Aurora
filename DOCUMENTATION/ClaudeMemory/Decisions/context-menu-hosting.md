# Decision — a context menu hosts itself, and where it hosts is a subclass

**Date:** 2026-08-22
**Status:** LANDED. Builds clean; **nothing here is GUI-verified.**
**Scope:** `ArctisAurora.Core.UISystem` (`ContextMenus`, `UICollisionHandling`),
`ArctisAurora.Core.UISystem.Controls` (`ContextMenuControl`, `WindowedContextMenuControl`,
`WindowControl`, `VulkanControl`), `ArctisAurora.EngineWork` (`Engine`), `Periodic`.

## What moved

A menu used to be a static host (`ContextMenuWindow`) driving a dumb column of entries
(`ContextMenuControl`). It is now one control that hosts itself: `ContextMenuControl` floats in the
window the right click came from, and `WindowedContextMenuControl : ContextMenuControl` puts the same
column in an OS window of its own. `ContextMenuWindow.cs` is deleted.

Nothing about *composition* changed — `ContextMenus.Compose`, `ContextMenuBuilder`, `invoker`,
`ContextMenus.xml` and `ContextMenuItemControl` are untouched. They never knew where the menu lived.

## Decisions

### 1. The variation is the host, and it is expressed as a subclass

User's call, 2026-08-22: in-window is the **base**, windowed is the derivative. `Open`, `Close` and
`Tick` are concrete on the base and share the whole gesture — the owner guard, `Compose`, `Fill`, the
measure — while `Attach`/`Detach` are the two virtuals that differ, plus `Tick` for a host that has
something to watch.

**Rejected: keep the static host and give it a mode flag.** The two hosts differ in every line of
attach and dismissal and in nothing else, which is a type, not a branch.

`ContextMenus` holds the one live instance and a `menuFactory`. One pointer, so one menu; the
instance is kept between opens because a windowed one owns a window that must not be rebuilt per
right click. In-window it is a **detached root** between opens, which `UILayout.DFSOrder` already
provides for.

### 2. Painting on top was free. Being reachable was not

Dense order is DFS pre-order of the tree, `UIModule`'s pipeline has depth test off, and `AddChild`
marks the pool order dirty — so the last child of the window root paints over everything, and the
resequence at the frame edge keeps that window's instance range contiguous. That much needed no work.

But `FindDeepestValid` returns the **first** child that hits, while paint puts the **last** on top. A
top-most overlay is therefore the one thing under the pointer that cannot be reached. `SolveHover`
swaps the root for `ContextMenus.OpenIn(root)` when a menu hangs in that tree, so the menu is
hit-tested and everything under it goes inert — which is what a menu wants anyway.

**Rejected: make the hit-test walk children back-to-front so it is properly reverse-paint-order.**
Correct in general and wrong to do here: `WindowFrameControl`'s four resize grips sit ahead of the
content deliberately so they are hit first, so they would have to move; every container's sibling
precedence flips at the same time; and none of it is verifiable by eye. The general rule stays wrong
for any future overlay — that is the accepted cost.

The carve-out also settles the grips: a menu overlapping the outer 4px band would otherwise lose to
them, since the frame subtree is the root's first child.

### 3. Placement lives on `WindowControl`, not on the menu

`AddOverlay(control, origin)` appends past the single-child guard and last, and `Arrange` places the
overlay at its `DesiredSize` — flipped back over the origin at the right or bottom edge, clamped when
it still does not fit. A window resize therefore re-clamps an open menu for free, and the menu knows
nothing about windows.

The base's `Measure(float.MaxValue)` before `Attach` is load-bearing for the **windowed** subclass,
which sizes its OS window from `DesiredSize`. In-window it is redundant, since the root's own measure
pass runs before the arrange that reads it.

### 4. A press that dismisses is not a click, and neither is its release

`SolveLMBRelease` fires `ResolveOnRelease` on `ActiveTarget(hovering) == activeControl` alone —
nothing records that a press happened this gesture. A dismissing press returns before
`SetActiveControl`, so: click button X, right-click to open a menu, left-click X to dismiss, and X
fires on the release without ever having been pressed. `pressSwallowed` is set by the dismissal and
consumed by the next release.

**Rejected: clear the press bookkeeping instead** — `SetActiveControl(null)` plus
`lastPressTarget = null`. No new field, but dismissing a menu with a click would also drop the active
control, so an open text field loses its keyboard to a click that went nowhere.

Right-click has no equivalent: `SolveRMBRelease` fires on whatever is hovered with no press pairing,
so a swallowed right press needs no second flag.

### 5. Dismissal is asked of the tree the press landed in

`DismissedBy(root, point)` goes through `OpenIn(root)` first. The press point is in *that* window's
design space and `arrangedRect` is in the menu's, and for a windowed menu those are unrelated spaces
— a press in the app window could land "inside" a menu rect that is not there, or dismiss and swallow
a real click on a same-tick move. Scoping it to the tree makes the comparison meaningful by
construction. `SolveLMBPress`/`SolveRMBPress` gained the `root` parameter `SolveHover` already took.

### 6. Periodic stays windowed, through one line in `Main`

`ContextMenus.menuFactory = () => new WindowedContextMenuControl();`. Menus that spill past the
window edge are the point of it for a desktop note app.

**Rejected: a bootstrap step or an XML attribute.** `Bootstrap.xml` is shared by every host, so a
Periodic-only step would be a name that resolves to nothing in the editor; and an authored attribute
buys per-menu choice nobody asked for, at the cost of a schema regen.

The consequence, and it is the whole risk in this change: **Periodic is the only runnable host, so
the in-window base ships exercised by nothing** until that line is commented out.

### 7. `Close()` before `entry.invoke()` is load-bearing now

It was incidental before — the old `onEntryInvoked` hook merely hid a window first. In-window, the
menu is parented into the tree an entry like `Window.Close` or the last `Tab.Close` is about to tear
down, and closing first is what detaches it. Reversing those two lines leaves `live` pointing at a
destroyed control.

## Verified

- `dotnet build AuroraEngine/ArctisAurora.sln` — 0 errors, 459 warnings, unchanged, and none on a
  file this touched.
- No new authorable type, so no schema regen: neither control carries `[A_XSDType]`, the same as
  `ContextMenuItemControl`, and derivative tracking still comes off `VulkanControl`'s own
  registration — not the trap in [[vulkancontrol-needs-xsdtype]].
- **Not verified — none of it is GUI-verified.** Not the in-window menu appearing, not the edge flip,
  not the press dismissal, not that Periodic's windowed menus still behave as they did.

## Still open

- A window torn down while an in-window menu hangs in its root destroys the menu with the tree and
  leaves `live` dangling. Decision 7 covers the routes that exist today; nothing covers a close that
  does not come from the menu itself.
- The dismissing press is swallowed, so right-clicking straight from an open menu onto something else
  takes two clicks. A windowed menu closes on pointer-leave and takes one.
- Still no keyboard: no Escape, no arrow navigation, no submenus.
- Every open and close dirties the root, and `Measure` does not skip clean subtrees, so a right click
  costs a full-tree measure and arrange.
- `ConfirmWindow` and `NoteNameWindow` are still separate OS windows of the old shape.
- The `ContextMenu="window"` fallback on the root `<Window>` is still there, unchanged by this.

Related: [[context-menu-invoker]], [[ui-clipping]], [[window-scaling-modes]], [[tab-view-control]],
[[render-window-owns-the-swapchain]], [[verify-what-the-user-sees]]
