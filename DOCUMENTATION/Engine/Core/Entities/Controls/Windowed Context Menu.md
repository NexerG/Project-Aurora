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
  - "[[Windowed Context Menu]]"
Parent Class:
  - "[[Context Menu]]"
Interfaces:
Used by:
  - "[[Periodic]]"
Type:
  - Public
Attributes:
Namespace: ArctisAurora.Core.UISystem.Controls
SourceFile: AuroraEngine/Core/UISystem/Controls/WindowedContextMenuControl.cs
VerifiedAgainst: 2026-08-22
---
## Description

A [[Context Menu]] in a window of its own, which is what lets it spill past the edges of the window it was opened from. Everything else about it — composing, the column, the entries, closing on an entry — is the base's, unchanged.

One per process. The window is built on the first open and hidden between them, because a swapchain per open would put the build cost inside the gesture; the same reasoning [[Note Name Window]] and the drag preview are built on. Unlike the preview it owns a tree and is clicked, so its window is an ordinary one to everything else in the engine: it wires input, it hit-tests, and its entries are ordinary buttons.

A host chooses it by setting `ContextMenus.menuFactory` once at startup. [[Periodic]] does, in `Main`.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `Tick()` | override | Closes when the pointer leaves the menu window. |
| `Attach(source, point)` | override | Sizes, positions and shows the menu window at the click. |
| `Detach()` | override | Hides it and hands focus back to the window it came from. |

## Methods

### Attach
The `point` the base measured in design space is unused here: an OS window is placed in screen coordinates, so the position comes from the source window's own origin plus the pointer inside it.

```
Attach(source, point)
    Build()
    size = ceil(DesiredSize)
    _window.os.Resize(size)
    _window.ui.uiRoot.FitTo(size)
    _window.os.SetPosition(source origin + source.mousePos)
    _window.os.Show(); _window.os.Focus()
    _window.os.SeedIsInWindow()
```

The menu measures to its widest caption and the window is sized to it, never the other way round — the base leaves that measurement in `DesiredSize` before `Attach` is called.

`SeedIsInWindow` is there because the window opens *under* the pointer: no crossing happens, so GLFW fires no enter callback, and `isInWindow` would start false against a pointer that is already inside. `Tick` reads exactly that flag.

### Detach
```
Detach()
    _window.os.Hide()
    _source?.os.Focus()
```

Hiding is the whole teardown. The window is kept and so is the column in it, which is why the control instance itself is kept between opens.

### Build
```
Build()
    if _window exists -> return
    _window = Engine.OpenMenuWindow("context-menu", 160, 120)
    root = new WindowControl(); root.AddChild(this)
    _window.ui.uiRoot = root
```

The seed size is what the window is created at before any menu has been measured into it; the first `Attach` resizes it.

## Input

Its window wires input like any other, so the entries hover and click through the ordinary path. Dismissal is the pointer leaving, checked once a tick from the engine loop through `ContextMenus.Tick` — not a press, which is how the in-window base dismisses.

## Known holes

Everything the base leaves open, plus: the menu takes focus while it is up, so the window it was opened from is not the focused one until `Detach` hands it back.

Dismissal is the pointer leaving and nothing else — a press elsewhere does not take it down, because a press is only solved for the window the pointer is already in.

## Related
- [[Context Menu]] — composition, the column, and the hosting contract this fills in
- [[Note Name Window]] — the same one-window-per-process shape, for a prompt
- [[Vulkan Renderer]] — what a second window costs, and who owns its swapchain
