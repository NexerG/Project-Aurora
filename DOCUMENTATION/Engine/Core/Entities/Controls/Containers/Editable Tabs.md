---
date: 2026-08-21
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
  - "[[Editable Tabs]]"
Parent Class:
  - "[[Tab View]]"
Interfaces:
Used by:
  - "[[Periodic]]"
Type:
  - Public
Attributes:
  - A_XSDType("EditableTabs", "UI")
Namespace: ArctisAurora.Core.UISystem.Controls.Containers
SourceFile: AuroraEngine/Core/UISystem/Controls/Containers/EditableTabsControl.cs
VerifiedAgainst: 2026-08-21
---
## Description

A [[Tab View]] whose captions rename in place on a double click. Everything else about it — the strip, the page, closing, tearing off, dropping — is the base's, unchanged.

It knows nothing about what a caption names. The gesture opens an [[Editable Label]] seeded with the tab's header and hands whatever is committed to `TabItemControl.onRename`, which is where a host puts the meaning: in [[Periodic]] that is a note on disk, and a tab authored in a UI document leaves it null and does not open a field at all. That null is the whole permission model — a strip that cannot rename says so by leaving the callback unset, not by being a different control.

Declared as `<EditableTabs>` and interchangeable with `<TabView>` at every attribute, since it adds none of its own.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `BuildCaption(item, tab)` | override | Returns an [[Editable Label]] and binds the double click to the whole tab. |
| `NewOfSameKind()` | override | Returns another `EditableTabsControl`, so a split keeps renaming. |

## Methods

### BuildCaption
The base builds the strip button first and passes it in, so the gesture can be bound to the tab rather than to the text.

```
BuildCaption(item, tab)
    caption = EditableLabel { item.header, captionSize, left-aligned, field colour }
    tab.RegisterOnDoubleClick(-> BeginRename(item, caption))
    return caption
```

Binding it to the caption alone was the first shape and is wrong. A single child is arranged at its `DesiredSize` — see [[Vulkan Control]] — so on a 180px tab a short name occupies a fraction of the surface and the rest belongs to the wrapper behind it; the gesture would work on the letters and nowhere else. The button is the only control that covers a whole tab.

The close button is not a hole in that: it deliberately does not bubble its release or its clicks, so a double click on the `x` never reaches the tab. The first of the two clicks has closed it anyway.

### BeginRename
```
BeginRename(item, caption)
    if item.onRename is null -> return
    caption.BeginEdit(name -> item.onRename(name))
```

### NewOfSameKind
A split copies the source pane's chrome onto a new view, and the construction was a hardcoded `new TabViewControl`. Splitting an editable strip would then have produced a plain one — renaming gone from the new pane, with nothing logged and nothing thrown. The virtual makes the kind part of what a split carries.

Tearing off is not the same problem: a torn window is built from `TearOffDocument`, so the XML names the kind. `TabWindow.xml` names `<EditableTabs>` for that reason.

## Input

None declared. The double click arrives through [[Vulkan Control]]'s release dispatch, and everything inside the field — typing, the caret, Enter, Escape, commit on blur — is [[Editable Label]]'s and [[Text Box]]'s.

## Known holes

A rename opened while another is still live is lost. Committing the first calls the base's `Retitle`, which rebuilds the whole strip and destroys the button the second field lives on. Nothing dereferences a freed control, because the destroy is queued and the edit ends before its own callback runs — the field simply disappears. Any `RebuildStrip()` does this, opening or closing a tab included, and [[File Browser]] has the same hole against its `Rebuild()`.

The field is sized from its text by [[Editable Label]], not from the space it is in, so a long name runs under the close button and stops at the next tab, where the strip clips.

## Related
- [[Tab View]] — the strip, the page, and the routing the tab structure is forced into
- [[Editable Label]] — the caption, and how an edit begins and ends
- [[Vulkan Control]] — where a double click is dispatched from and what guards it
