---
Status: Current
tags:
  - Engine
  - d_UI
Class:
  - "[[LabelControl]]"
Type:
  - Public
---
## Description
Text that is drawn and never edited — a button's caption, a row in a list, a heading in an application shell. It is a `TextControl`, so it measures and wraps through the same `TextMeasurer` everything else does and owns one `GlyphControl` per character; it is deliberately **not** a [[INPUT|TextInputControl]].

Two things follow from that, and both are the reason it exists. `TextInputControl.ResolveOnClick` begins an edit and returns without calling base, so `bubbleClick` is dead on it and a `<Button><TextInput/></Button>` is an unclickable button — a `Label` does not override the resolver at all, so a click walks up to whatever wanted it. And a `TextInput` caption calls `BeginEdit()` when clicked, which makes it a target for `Text.Write`'s fallback path; a `Label`'s edit methods do nothing, so a caption can never be typed into.

`BubbleAll()` runs in the constructor rather than being left to the author, on the same reasoning `GlyphControl` uses: decoration must not consume input, and a caption that eats a click is a bug every single time.

## Authoring
```xml
<Button Width="46" Height="32" onRelease="Window.Close">
    <Label Text="X" FontSize="16"/>
</Button>
```

`Text`, `FontSize`, `FontName`, `StylingType` and `ColorHex` are the `TextControl` members; there are no members of its own.

## Related
- [[Vulkan Control]] — the bubbling contract this exists to keep.
- [[Rich Text Document]] — the editable side of the same base.
