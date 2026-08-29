---
date: 2026-05-30
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
  - "[[StackPanel]]"
Parent Class:
  - "[[Vulkan Control]]"
Interfaces:
Used by:
Type:
  - Public
Attributes:
  - A_XSDType
  - A_XSDElementProperty
Namespace: ArctisAurora.Core.UISystem.Controls.Containers
SourceFile: AuroraEngine/Core/UISystem/Controls/Containers/StackPanelControl.cs
VerifiedAgainst: 2026-05-30
---
## Description

A container control that lays its children out along one axis â€” vertical by default â€” with optional `Spacing` between them and WPF-style **star sizing**. Extends `AbstractContainerControl` (itself a [[Vulkan Control]]). Declarable from UI XML as `<StackPanel>`.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `Measure(availableSize)` | override | Two-pass measure: size fixed children, then distribute leftover main-axis space across star children. |
| `Arrange(finalRect)` | override | Position children along the main axis (cursor + spacing), align each on the cross axis. |

## Fields & Properties

```C#
[A_XSDElementProperty("Orientation", "UI")]
public Orientation orientation = Orientation.Vertical;   // Horizontal | Vertical

[A_XSDElementProperty("Spacing", "UI", "Space between children in pixels.")]
public float Spacing = 0f;
```

## Methods

### Measure (2-pass)
Pass 1 measures non-star children (and accumulates star weights); pass 2 distributes the remaining main-axis space to star children by weight. Cross-axis size is the max child cross size. `Spacing` is added between children.

The box the two passes divide comes from the panel's own `Width`/`Height` when either is set, and only falls back to what the parent offered when it is not. That matters because a stack hands a *non-star* child `float.MaxValue` on the main axis: a panel that took the offer literally would compute `remaining = MaxValue - fixedChildren` and hand its star child the whole float range, reporting a desired size no parent could clip back. A pinned axis is the box to share out, not a floor under the offer — see [[Split View]], where a fixed-width pane splitting itself was exactly this.

`Width`/`Height` still act as a *floor* on the reported desired size at the end, so children that genuinely exceed a pinned size push the panel wider rather than being cut off.

### Arrange
Recomputes the star allocation against the final size, then walks a cursor along the main axis placing each child, applying margins and cross-axis alignment (`Stretch`/`Left`/`Center`/`Right`, etc.).

```
Arrange(finalRect)
	inner = finalRect shrunk by padding
	recompute starUnit against the final size
	cursor = inner's start on the main axis
	for each child control
		cursor += Spacing unless this is the first child
		cross size = the full cross axis when the child is auto or Stretch, otherwise its desired cross size clamped to the axis
		main size = the child's star share when it is starred, otherwise its desired main size
		main size = clamp(main size, 0, inner's end on the main axis - cursor - the child's main-axis margin)
		child.Arrange(rect at cursor, of that size)
		cursor += main size + the child's main-axis margin
```

A child never runs past the end of the panel's own box, and the clamp on the line above is what guarantees it. It matters because `Measure` hands a non-star child `float.MaxValue` on the main axis and [[Vulkan Control]] gives a control with no `Width`/`Height` whatever it was offered: without the clamp, one unsized child reports `float.MaxValue` as its desired size, that number becomes both its quad and the cursor, and the result is a single child covering the whole panel in its own colour with every later sibling arranged off-screen.

The clamp does nothing inside a scroll viewport, which is the case it could plausibly have broken. Scrollable arranges its content at the larger of the content's desired size and the viewport, so a stack inside one is already given a box as tall as its own content, and there is nothing left over to clamp against.

## XML
```xml
<StackPanel Orientation="Vertical" Spacing="8">
  <!-- child controls -->
</StackPanel>
```

## Related
- [[Vulkan Control]] â€” base control + layout fields (`heightStar`, `margin`, alignment)
- Sibling containers: Grid, Scrollable, Docking
