---
date: 2026-08-22
Status: Current
tags:
  - d_UI
  - d_Entity
cssclasses:
  - Aurora.css
Linker:
  - "[[Entity]]"
System:
Class:
  - "[[Context Menu]]"
Parent Class:
  - "[[Hint]]"
Interfaces:
Used by:
  - "[[Periodic]]"
Type:
  - Public
Attributes:
Namespace: ArctisAurora.Core.UISystem.Controls
SourceFile: AuroraEngine/Core/UISystem/Controls/ContextMenuControl.cs
VerifiedAgainst: 2026-08-22
---
## Description

An open context menu: a translucent ground over a column of entries, floated in the window the right click came from. The column is rebuilt on every open, because the entries are composed per right click and never repeat.

It hosts itself. `Open` composes, fills, measures and attaches; `Attach` and `Detach` are the only two things a different host has to answer differently, which is what [[Windowed Context Menu]] overrides to put the same column in an OS window of its own. Which of the two a right click builds is `ContextMenus.menuFactory`, a host-level default set once at startup — [[Periodic]] sets it to the windowed one.

Nothing here composes. `ContextMenus.Compose` walks the named menus a control declares plus whatever it adds in code, and the entries arrive as a list of `ContextEntry` already bound to the control that was right-clicked — see the `invoker` decision in ClaudeMemory. This control only draws them and takes the pointer.

Nothing here is sized either. Entries measure to their widest caption and the menu to them, and where it lands is [[Window Control]]'s `AddOverlay`, which flips the menu back over the click point at the right or bottom edge and clamps it when it still does not fit.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `isOpen` | property | Whether a menu is currently attached and showing. |
| `Open(owner)` | method | Composes `owner`'s entries and shows them; false means it offered none. |
| `Close()` | method | Detaches. Called by an entry before its action runs, and by a press outside. |
| `Fill(entries)` | method | Destroys the old column and builds one row per entry. |
| `Tick()` | virtual | Host-owned dismissal. Nothing to watch for an in-window menu. |
| `Attach(source, point)` | virtual | Floats this menu in `source` at a design-space point. |
| `Detach()` | virtual | Takes it back out of whatever window root holds it. |

## Methods

### Open
The false return is what the walk up the tree reads: a control that offers nothing hands the right click to its parent, and `VulkanControl.OpenContextMenu` keeps going until something offers entries or the root runs out.

```
Open(owner)
    if Owns(owner) -> return true            // a right click inside the menu is not a new request
    entries = ContextMenus.Compose(owner)
    if entries is empty -> return false
    source = RenderWindow.Of(owner)
    if source is null -> return false
    if isOpen -> Detach()                    // re-opened in another window, so leave the first tree
    Fill(entries)
    Measure(infinite)
    Attach(source, source.ui.ToDesignSpace(source.mousePos))
    isOpen = true
    return true
```

The measure at infinity is what makes the column size to its widest caption rather than to whatever it was offered, and [[Windowed Context Menu]] sizes its window from the `DesiredSize` it leaves behind. In-window it is redundant — the root's own measure pass runs before the arrange that reads it — and kept because the two hosts should not disagree about what `Attach` may assume.

### Attach and Detach
```
Attach(source, point)
    source.ui.uiRoot.AddOverlay(this, point)

Detach()
    (parent as WindowControl)?.RemoveOverlay()
```

An overlay is appended **last**, because dense order is a DFS pre-order of the tree and the last row drawn is the one on top — see [[UI Rasterizer Module]]. Between opens the menu is a detached root, drawn by no window, which the pool's DFS sort already accounts for.

### Fill
```
Fill(entries)
    destroy every child of the column
    for each entry
        if entry.separatorBefore -> column.AddChild(separator)
        column.AddChild(item with entry.caption, enabled = entry.enabled)
    InvalidateLayout()
```

An enabled row's release closes the menu **first** and runs the action second, and that order is load-bearing rather than tidy: the menu is parented into the tree that `Window.Close`, or a `Tab.Close` on the last tab, is about to tear down. A disabled row answers to nothing at all — see [[Context Menu Item]].

## Input

The pointer, and nothing else. While a menu is open, `UICollisionHandling.SolveHover` hit-tests the menu's subtree in place of the window root, so entries light up and everything under the menu is inert. That carve-out exists because the hit-test takes the **first** child that hits while paint puts the **last** on top, so a top-most overlay would otherwise be the one thing under the pointer that cannot be reached.

A press outside the menu's rect takes it down and goes no further, and the release it pairs with is dropped too — otherwise it would reach whatever the pointer had come to rest on.

## Known holes

No keyboard. No Escape, no arrow navigation, no submenus, and no accelerator on an entry.

The dismissing press is swallowed, so right-clicking straight from an open menu onto something else takes two clicks rather than one.

A window torn down while a menu hangs in its root destroys the menu with the tree, and `ContextMenus` goes on holding it. The routes that exist today all close the menu first.

Opening and closing both dirty the window root, and `Measure` does not skip clean subtrees, so a right click costs a full-tree measure and arrange.

## Related
- [[Windowed Context Menu]] — the same column in a window of its own
- [[Context Menu Item]] — one row, and what a disabled one refuses
- [[Window Control]] — `AddOverlay`, and where an overlay is placed and clamped
- [[Vulkan Control]] — the walk that finds which control owns the menu
