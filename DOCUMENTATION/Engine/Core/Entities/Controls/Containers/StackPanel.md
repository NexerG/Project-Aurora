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

### Arrange
Recomputes the star allocation against the final size, then walks a cursor along the main axis placing each child, applying margins and cross-axis alignment (`Stretch`/`Left`/`Center`/`Right`, etc.).

## XML
```xml
<StackPanel Orientation="Vertical" Spacing="8">
  <!-- child controls -->
</StackPanel>
```

## Related
- [[Vulkan Control]] â€” base control + layout fields (`heightStar`, `margin`, alignment)
- Sibling containers: Grid, Scrollable, Docking
