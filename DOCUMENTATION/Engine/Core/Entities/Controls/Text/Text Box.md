---
date: 2026-08-20
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
  - "[[Text Box]]"
Parent Class:
  - "[[Vulkan Control]]"
Interfaces:
Used by:
  - "[[Note Name Window]]"
  - "[[Editable Label]]"
Type:
  - Public
Attributes:
  - A_XSDType
  - A_XSDElementProperty
Namespace: ArctisAurora.Core.UISystem.Controls.Text.Editing
SourceFile: AuroraEngine/Core/UISystem/Controls/Text/Editing/TextBoxControl.cs
VerifiedAgainst: 2026-08-20
---
## Description

A one-line editable field — the engine's only standalone text input, as distinct from editing inside a document. It is a **container**, not a text control: it holds a selection box, an inner one-line [[TextInputControl]] carrying the text and its glyphs, and a caret quad. The text cannot be the control itself because a text control's children *are* its glyphs — `SyncGlyphs` trims `children` to the character count and discards the rest, so a caret parented to the text would be destroyed by the next keystroke. [[Rich Text Document|DocumentControl]] owns the document's caret for the same reason; this is that split one level down.

Declarable as `<TextBox>`, though it is built in code today by the note-naming prompt.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `text` | property | The string being edited; forwards to the inner line. Setting it marks the value committed. |
| `Focus()` | public | Begins the edit and selects everything, so typing replaces. |
| `SelectAll()` | public | Anchor to the start, cursor to the end. |
| `WriteChar(c)` | public | Replaces the selection, then inserts. |
| `Backspace()` / `Delete()` | public | Deletes the selection if there is one, otherwise one character. |
| `MoveCaret(move, extend)` | public | One line, so vertical and page moves collapse onto its ends. |
| `Commit()` / `Cancel()` | public | Raises `onCommit` with the text, or restores the last committed value and raises `onCancel`. |
| `onCommit` / `onCancel` | field | Callbacks the host supplies. |
| `onBlur` | field | Raised when the active context lands outside the box. Unset means a blur does nothing. |

## Fields & Properties

```C#
[A_XSDElementProperty("Text", "UI", "The string being edited.")]
public string text { get; set; }

[A_XSDElementProperty("FontSize", "UI", "Font size in pixels.")]
public int fontSize { get; set; }

[A_XSDElementProperty("TextColorHex", "UI", "Colour of the text.")]
public string textColorHex { get; set; }
```

## Methods

### Arrange
Centres the one line vertically in the box, then places the caret and the selection from the line's own `CaretAt`. Both collapse to an empty rect while the box is not being edited — a zero-scale quad draws no pixels and fails the hit-test, the same trick the scrollbar thumb uses.

```
Arrange(finalRect)
    inner = finalRect shrunk by padding
    textRect = inner, height = line.DesiredSize.Y, centred vertically
    line.Arrange(textRect)

    if not editing -> caret and selection to Empty; return
    cursor = line.CaretAt(line.cursorPosition)
    caret.Arrange(textRect.x + cursor.x, textRect.y + cursor.top, CaretControl.Width, cursor.height)
    if anchor == cursorPosition -> selection to Empty; return
    other = line.CaretAt(anchor)
    selection.Arrange(spanning min(cursor.x, other.x) .. max(cursor.x, other.x))
```

### The inner line
A private `TextInputControl` subclass with three overrides. `WrapWidth` returns `float.MaxValue` so the text never wraps at the box's own width — that hook exists on [[TextControl]] precisely for this. `ResolveOnClick` begins the edit and then hands the click to its parent explicitly, because `TextInputControl.ResolveOnClick` swallows clicks and calling `base` would swallow this one too; `TextRun` does the same thing inside a document. `OnContextRemoved` extends the base — which commits the line's own edit — by telling the box it may have lost focus.

### Losing focus
Either the box or its inner line can be the control the collision handler makes active, depending on whether the pointer landed on the text or on the box's padding, so both raise the same check. It walks up from whatever now holds the active context and stays quiet if it finds the box on that chain — a pointer moving between the two halves of one field is not a blur. Anything else raises `onBlur`.

```
LoseFocus()
    for control = activeControl, up through parents
        if control is this box -> return
    onBlur()
```

This reads `activeControl` as the *incoming* control, which works because `UICollisionHandling.SetActiveControl` assigns before it raises `OnContextRemoved`, the same order `SetDragging` uses.

## Input

The box takes no input of its own. [[TextInputActions]] resolves the nearest `TextBoxControl` above the active control whenever there is no document editor, so `Text.Write`, `Text.Backspace`, `Text.Delete` and the caret actions reach it. Enter (`Text.NewBlock`) commits; Escape (`Text.Cancel`) restores.

A host wanting keyboard focus before the pointer arrives has to hand over the active context itself — see `UICollisionHandling.SetActiveControl`.

## XML
```xml
<TextBox Width="240" Height="30" ColorHex="#1B1B1B" TextColorHex="#EAEAEA" FontSize="15"/>
```

## Related
- [[TextInputControl]] — the editing methods the inner line provides
- [[TextControl]] — measurement, `OffsetAt`/`CaretAt`, and the `WrapWidth` hook
- [[Rich Text Document]] — the document-side editing path this one parallels
