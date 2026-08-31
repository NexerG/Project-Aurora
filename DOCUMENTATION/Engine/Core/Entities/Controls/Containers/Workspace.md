---
date: 2026-08-31
Status: Current
tags:
  - d_UI
  - d_Entity
cssclasses:
  - Aurora.css
Linker:
  - "[[Entity]]"
System:
  - "[[SESSION]]"
Class:
  - "[[Workspace]]"
Parent Class:
  - "[[Panel]]"
Interfaces:
Used by:
  - "[[Thorium]]"
Type:
  - Public
Attributes:
  - A_XSDType("Workspace", "UI")
Namespace: ArctisAurora.Core.UISystem.Controls.Containers
SourceFile: AuroraEngine/Core/UISystem/Controls/Containers/WorkspaceControl.cs
VerifiedAgainst: 2026-08-31
---
## Description

The place in a window where panes go, and the only thing [[SESSION]] needs in order to know where to put a restored arrangement.

It holds one child and paints nothing — its mask is `invisible`, like every structural container — so it is invisible in a layout except as the slot its child fills. What it adds is two names: `Default`, the UI document built into it when the session has nothing recorded for this window, and `Pane`, a document holding one empty pane that a restored arrangement splits to rebuild itself.

Declaring one is a single element. In [[Thorium]] the main window's whole editing area is `<Workspace Name="Workspace" HeightStar="1" Default="workspace" Pane="tab-pane"/>`, and the two-pane arrangement that used to sit inline there now lives in the `workspace` document instead.

## Why the seed is a document and not children

The panes an application authors are its first-run arrangement, and once a session exists they are not built at all. Leaving them inline as the workspace's XML children would mean parsing them on every boot and destroying them immediately — and those children carry `DocumentEditor Source=`, so that is two whole notes turned into control trees and thrown away, at one control per glyph. Naming a document instead means the seed is only ever read when it is used.

It also makes the arrangement's status legible. `Default="workspace"` says out loud that the document is a fallback, where a subtree that restore silently replaced would have made editing it look broken.

## Why `Pane` is separate from `Default`

A pane is not a bare `TabView`. It carries tab metrics, three colours, a `TearOffDocument` and a context menu, all authored, and a restored arrangement needs panes that have all of it. Building one in code would carry none of it.

Only one has to be parsed. [[SplitView]] copies the source pane's kind and every one of those attributes onto each pane a split creates, so a single seed grows into an arrangement of any depth with the chrome intact. For a window whose default *is* one empty pane — a torn-off window — `Default` and `Pane` simply name the same document.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `defaultDocument` | `Default` attribute | UI document built in when the session has nothing for this window. |
| `paneDocument` | `Pane` attribute | UI document holding one empty pane, which a restored arrangement splits. |
| `LoadDefault()` | method | Parses `Default` and adopts it. |
| `LoadPane()` | method | Parses `Pane`, adopts it, and returns it for a restore to build on. |
| `In(control)` | static | The workspace somewhere under a control, or null for a window that declares none. |

## Methods

### LoadDefault
```
LoadDefault()
    if defaultDocument is empty -> return
    AddChild(ParseXML(defaultDocument))
```

### LoadPane
Returns the pane rather than just attaching it, because the caller is about to split it and needs the handle.

```
LoadPane()
    if paneDocument is empty -> return null
    parsed = ParseXML(paneDocument)
    if parsed is not a TabView -> return null
    AddChild(parsed)
    return parsed
```

### In
```
In(control)
    if control is null -> return null
    if control is a Workspace -> return control
    for each child of control
        found = In(child)
        if found is not null -> return found
    return null
```

One workspace per window is the assumption everywhere, and the first found in depth-first order is the one that is used.

## Who fills it, and when

Not the control. A workspace is filled by an explicit call right after its window's root has been assigned, from the three places that assign one: the host at startup, [[SESSION]] for each window it reopens, and `TabViewControl.TearOff` for a window a tab has just been dragged into.

It cannot fill itself during parsing, because the record it would need is per window and a tree is not attached to a window yet. It cannot do it from `OnStart` either — `Engine.Interpolate` drains that queue with a `foreach`, and building a document creates entities.

## Known holes

It is a single-child host, which no other container in the codebase was, and that exposed an ordering defect in [[SplitView]]'s collapse: the survivor of a collapsing split was re-parented into the host while the split was still a child of it. Every previous host was a `StackPanel`, which tolerates the transient second child. Collapse now detaches the split first, matching what its own `Split` had always done.

Nothing enforces one workspace per window; a document declaring two would have the second one ignored by everything that goes looking.

## Related
- [[SESSION]] — what fills a workspace, and the record it is filled from
- [[SplitView]] — how a seed pane becomes an arrangement, and what it copies onto each pane
- [[Tab View]] — what a pane is
- [[UI Document]] — how `Default` and `Pane` resolve a name to a file
