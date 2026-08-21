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
  - "[[Editable Label]]"
Parent Class:
  - "[[Vulkan Control]]"
Interfaces:
Used by:
  - "[[File Browser]]"
Type:
  - Public
Attributes:
Namespace: ArctisAurora.Core.UISystem.Controls.Text
SourceFile: AuroraEngine/Core/UISystem/Controls/Text/EditableLabelControl.cs
VerifiedAgainst: 2026-08-21
---
## Description

Text that reads as a plain caption until something asks it to be edited, then becomes a one-line field for the length of that edit and goes back. It is the in-place rename gesture — the browser's row names are these, and a `Rename note` menu entry is the only thing that opens one today.

Both halves exist for the control's whole life: a [[Label]] and a [[Text Box]], with exactly one of them shown. Nothing is created, destroyed or re-parented when an edit starts, because there is no insert-at-index on any container and a swap would either append the field to the end of the row or reorder it. `Measure` and `Arrange` reach only the shown half, so the hidden one keeps the collapsed clip `Hide()` gave it — the same arrangement [[Tab View]] uses to keep inactive pages off the screen.

It bubbles everything. A row's name sits inside a [[Button]] that opens the note, and a control that swallowed its clicks would break that; while the field is up the [[Text Box]] swallows them itself, which is what stops a click aimed at the caret from also opening the note.

Not declarable in XML — it carries no `A_XSDType`, and is built in code by [[File Browser|FileBrowserControl.AddRow]].

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `text` | property | The caption, and what the field is seeded with. |
| `fontSize` | property | Applied to both halves. |
| `textColorHex` | property | Applied to both halves. |
| `fieldColorHex` | property | Ground drawn behind the field; invisible at rest, since only the field paints it. |
| `isEditing` | property | True between `BeginEdit` and the commit or cancel that ends it. |
| `BeginEdit(onCommit)` | public | Swaps the field in, selects everything, takes the active context. |

## Methods

### BeginEdit
Seeds the field from the label, sizes it, shows it, and hands it keyboard focus before the pointer ever reaches it — the same handover [[Note Name Window]] performs, and for the same reason: without it the keystrokes go to whatever was clicked last.

```
BeginEdit(onCommit)
    if already editing -> return
    commit = onCommit
    box.text = label.text
    box.preferredWidth = max(minEditWidth, label.DesiredSize.X + editPadding)
    label.Hide(); box.Show()
    UICollisionHandling.SetActiveControl(box)
    box.Focus()          -- begins the edit and selects all
```

The width is pinned once, here, rather than left to the layout. A horizontal [[Stack Panel]] offers a non-star child `float.MaxValue` on the main axis and the [[Text Box]] would take that literally; a `widthStar` would walk into the star-child cross-measurement defect logged against [[Stack Panel]]. A field wider than the sidebar is clipped by the browser's viewport, which always clips. The cost is that typing past the field's width runs under the clip instead of scrolling the caret into view.

### The edit ending
Three ways in, one way out. Enter reaches `Commit` through [[TextInputActions]], Escape reaches `Cancel`, and a click anywhere outside the field raises `onBlur`, which commits.

```
Committed(value)
    commit = the stored callback
    End()                -- hide field, show label, editing = false
    commit(value)
```

The edit is closed *before* the callback runs, so a handler that rebuilds the list it lives in is not tearing down a control that is still mid-commit.

## Input

None of its own. The field inside it is a [[Text Box]], which [[TextInputActions]] finds by walking up from the active control whenever there is no document editor above it — so `Text.Write`, the caret actions, Enter and Escape all reach it with nothing declared per host.

Blur is `IContext.OnContextRemoved("ActiveControl")` on the [[Text Box]], raised only when the context landed outside it. That distinction needs `UICollisionHandling.SetActiveControl` to assign before it notifies, which it now does.

## Related
- [[Text Box]] — the field half, and where `onBlur` lives
- [[Label]] — the resting half
- [[File Browser]] — builds one per row and drives `BeginEdit` from `BeginRename`
