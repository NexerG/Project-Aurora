---
date: 2026-08-31
Status: Current
tags:
  - Engine
  - d_System
  - d_XML
  - d_UI
cssclasses:
  - Aurora.css
Linker:
  - "[[Arctis Aurora]]"
System:
  - "[[SESSION]]"
Dependencies:
  - "[[SETTINGS]]"
  - "[[Shutdown]]"
  - "[[Tab View]]"
  - "[[SplitView]]"
Implementors:
  - "[[Workspace]]"
  - "[[Thorium]]"
Namespace: ArctisAurora.Core.UISystem
SourceFiles: AuroraEngine/Core/UISystem/SessionLayout.cs, AuroraEngine/Core/UISystem/Controls/Containers/WorkspaceControl.cs, AuroraEngine/Core/Rendering/AGlfwWindow.cs
VerifiedAgainst: 2026-08-31
---
## Overview

The system that remembers what was on screen when the application last closed, and puts it back: which windows were open, where each one sat and how large, whether it was maximized, how its panes were split, and which notes were in each pane with one of them on top.

It is a settings group and nothing more exotic. `SessionLayout` implements `ISettingsGroup`, so [[SETTINGS]] discovers it by type scan, fills it from `UserSettings.settings.xml` and writes it back, and the session needs no file, no parser and no writer of its own. What makes it work is that the record is one recursive type rather than a class hierarchy — a pane that holds panes is a split, a pane that holds tabs is a leaf — and the settings reader dispatches on element name, so the whole tree persists through machinery that already existed.

The record is partitioned by an opaque *scope* key the host sets. The engine groups by it and never asks what it means; [[Thorium]] sets it to the vault path, so a layout belongs to the vault it was arranged in, and a host that wants one session for the whole application leaves it empty.

## Where a restored arrangement lands

Nothing searches the control tree for somewhere to put the panes. A window that wants a session declares a [[Workspace]], and the workspace names two UI documents: `Default` is the arrangement built when the session has nothing to say about this window, and `Pane` is a single empty pane that a restored arrangement splits to rebuild itself.

That second document exists because a pane carries authored chrome — tab metrics, three colours, `TearOffDocument`, a context menu — and a hand-built one would carry none of it. `SplitViewControl` already copies all of that from the pane it splits, so one parsed pane is enough to seed any arrangement however deeply nested.

The consequence worth knowing: the panes and tabs an application authors are its *first-run* arrangement, and once a session exists they are not built. In [[Thorium]] they live in `Workspace.ui.xml` rather than in `UI.ui.xml`, which is what makes that visible on sight rather than surprising.

## The record

| Element | Holds | Means |
| --- | --- | --- |
| `Session` | `SessionScope` list | every partition that has been recorded |
| `SessionScope` | `Key`, `SessionWindow` list | one host-defined partition — a vault, in Thorium |
| `SessionWindow` | `Document`, `Primary`, `X`, `Y`, `Width`, `Height`, `Maximized`, one `SessionPane` | one OS window, its restore rect, and its arrangement |
| `SessionPane` | `Orientation`, `Size`, `Active`, `SessionTab` list, `SessionPane` list | a split when it holds panes, a leaf when it holds tabs |
| `SessionTab` | `Path` | one note that was open |

`Size` is the fixed main-axis size in pixels and zero means the pane takes the remainder, which is exactly what `Width="525"` and `WidthStar="1"` already produce and what a splitter drag already writes.

## Capture

`Session.Capture` is a `Commit` step in [[Shutdown]], placed before `Settings.SaveAll` because it fills the group that step then writes. `Commit` runs against a tree that is still live, which is the only phase where the windows still exist to be read.

A window is recorded when its `uiDocument` is set. That field is assigned only where a window is built from a UI document — the application's own window and a torn-off one — so menus and the drag preview are excluded without a flag of their own.

```
Record()
    captured = []
    for each window in Engine.windows
        if window.uiDocument is empty or window.closeRequested -> skip
        workspace = WorkspaceControl.In(window.ui.uiRoot)
        if workspace is null -> skip
        placement = window.os.GetPlacement()
        record = SessionWindow { window.uiDocument, window is Engine.primary, placement }
        if workspace has a child -> record.panes.Add(PaneOf(child))
        captured.Add(record)
    ScopeFor(scope).windows = captured
```

```
PaneOf(node)
    if node is SplitView
        record = SessionPane { node.orientation }
        for each child of node that is not a Splitter
            captured = PaneOf(child)
            captured.size = child's preferredHeight when vertical, preferredWidth when not
            record.panes.Add(captured)
        return record
    if node is TabView
        record = SessionPane
        for each item in node.Items
            path = editor of item -> session -> path
            if path is null -> skip
            if item is node.activeItem -> record.active = record.tabs.Count
            record.tabs.Add(SessionTab { path })
        return record
    return null
```

A tab whose editor never loaded a file is left out, because there is nothing to reopen it from.

## Restore

`SessionLayout.Restore()` runs from the host once the primary window's tree is parsed and assigned, and before the engine starts running. It cannot run any earlier: the record is per window and a tree is not attached to a window during `ParseXML`, and it cannot run from `OnStart` either, because `Engine.Interpolate` drains that queue with a `foreach` and building a document creates entities.

```
Restore()
    primary = WorkspaceControl.In(Engine.primary.ui.uiRoot)
    recorded = the SessionScope whose Key is scope
    if recorded is null or has no windows
        primary.LoadDefault()
        return
    for each record in recorded.windows
        if record.primary
            Fill(primary, record)
            Place(Engine.primary, record)
        else
            OpenRecorded(record)
    Engine.primary.Focus()
```

The focus call is not cosmetic. GLFW focuses each window as it is created, so without it the last restored window would come up in front of the one the person actually asked for.

```
Fill(workspace, record)
    if record has no panes -> workspace.LoadDefault(); return
    seed = workspace.LoadPane()
    if seed is null -> workspace.LoadDefault(); return
    Build(record.panes[0], seed)
```

```
Build(record, into)
    if record has no child panes -> FillTabs(record, into); return
    edge = Bottom when record.orientation is Vertical, otherwise Right
    panes = []
    for i in 0 .. record.panes.Count - 2
        fresh = SplitViewControl.Split(into-so-far, edge)
        if fresh is null -> break
        panes.Add(the pane that was split)
        into-so-far = fresh
    panes.Add(into-so-far)
    for i in 0 .. panes.Count - 1
        if record.panes[i].size > 0 -> write it to panes[i] on the split's main axis
        Build(record.panes[i], panes[i])
```

The arrangement is rebuilt by replaying the splits that would have produced it, rather than by constructing a tree. Every grip, the `paneMinimum` floor, the first-fixed/second-star convention and the host re-slotting are `SplitViewControl`'s and are not duplicated anywhere here, so none of it can drift out of step with the control that owns it.

```
FillTabs(record, view)
    if tabFactory is null -> warn and return
    active = null
    for i in 0 .. record.tabs.Count - 1
        path = record.tabs[i].path
        if the file does not exist -> log and skip
        tab = tabFactory(path)
        view.AddChild(tab)
        if i is record.active -> active = tab
    if active is not null -> view.SetActive(active)
```

`tabFactory` is what the host supplies, because the engine can build the editor but not what the host binds to it — in [[Thorium]] a tab carries an `onRename` that renames a note on disk. The active index is matched against the *recorded* position, so a note deleted between sessions drops out without shifting which of the survivors comes back on top.

## Placement

`AGlfwWindow.GetPlacement` answers where the window would sit if it were restored, which is not the same question as where it is.

```
GetPlacement()
    if the window is not maximized
        return GLFW's position and size, and not-maximized
    placement = Win32 GetWindowPlacement(Hwnd)
    info = GetMonitorInfo(MonitorFromWindow(Hwnd))
    offset = info.work origin minus info.monitor origin
    return placement.rcNormalPosition offset into screen coordinates, and maximized
```

GLFW reports the maximized rect while a window is maximized, so saving that would lose where un-maximizing puts it back. `rcNormalPosition` is the rect Windows itself keeps for exactly that, and it is in workspace coordinates — which differ from screen coordinates only by the work area's origin, non-zero only for a taskbar docked top or left.

A saved rect that no longer lands on any monitor is centred on the primary one at the size it had, which is what happens when a screen is unplugged or the arrangement changes between sessions. `RectOnAnyMonitor` intersects the rect against every monitor's work area to decide.

No monitor name is recorded. Absolute virtual-desktop coordinates already say which screen a window was on for as long as the arrangement holds, and once the fallback is unconditionally the primary screen, a name has no reader.

## Known holes

The primary window's rect is applied after `Engine.Init` returns, so it is visible at the `GraphicsSettings` size for the duration of bootstrap and then jumps. Nothing can read the session earlier — [[SETTINGS]] loads as the first bootstrap step, well after the window is created — so closing this means either creating the primary hidden or finding the scope key somewhere available before that step.

Switching vaults does not capture the vault being left. The arrangement is only ever recorded at shutdown, so switching away and quitting somewhere else loses it.

Caret position, scroll offset and selection are not recorded, only the ordered tab paths and the active one. An iconified window comes back normal; only maximized is carried. A window whose every recorded note has been deleted comes back as an empty pane rather than not at all.

## Related
- [[Workspace]] — the control that declares where an arrangement lands and what seeds it
- [[SETTINGS]] — how the record is discovered, merged, and written back as a diff
- [[Shutdown]] — the phase capture runs in, and why it is that one
- [[SplitView]] — the splits the arrangement is replayed through
- [[Tab View]] — the panes and the tabs that go in them
