---
date: 2026-05-30
Status: Current
tags:
  - d_Entity
  - d_UI
cssclasses:
  - Aurora.css
Linker:
  - "[[Entity]]"
System:
  - "[[VULKAN]]"
Class:
  - "[[Vulkan Control]]"
Parent Class:
  - "[[Entity]]"
Interfaces:
  - "[[IXMLParser]]"
Used by:
  - "[[UI Rasterizer Module]]"
Type:
  - Public
Attributes:
  - A_XSDType
  - A_XSDElementProperty
Namespace: ArctisAurora.Core.UISystem.Controls
SourceFile: AuroraEngine/Core/UISystem/Controls/VulkanControl.cs
VerifiedAgainst: 2026-05-30
---
## Description

The base class for everything in the UI. A `VulkanControl` is an [[Entity]] that participates in a **two-pass (Measure/Arrange) layout system**, carries per-control GPU data (color/UV/mask), and exposes a bubbling **event** model. It is declarable from UI XML (its properties are tagged `[A_XSDElementProperty]`, its enums `[A_XSDType]`), and it is rendered by the [[UI Rasterizer Module]].

A plain `VulkanControl` holds **one** child; use a container ([[StackPanel]], Grid, â€¦) for multiple.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `Measure(availableSize)` / `Arrange(finalRect)` | virtual | Two-pass layout (containers override). |
| `InvalidateLayout()` / `InvalidateArrange()` | public | Mark dirty up to the top root and register it with `UILayout`. |
| `SetSize` / `SetWidth` / `SetHeight` | virtual | Convenience sizing. |
| `AddChild(Entity)` | override | Adds a single child (throws on a 2nd, or non-control). |
| `Register*` / `Resolve*` (Enter/Exit/Click/DoubleClick/Release/AltClick/AltRelease/Drag/Hover/Scroll) | public | Subscribe to / fire input events. |
| `BubbleAll()` | public | Enable event bubbling for every event. |
| `UpdateControlData()` | internal | Push `controlData` (color/UVs) to the GPU. |
| `ParseXML(name)` | static | Build a control tree from a UI XML document. |
| `EnumColorToHex` / `HexToRGB` | static | Color helpers. |

## Fields & Properties

### Layout (XML-driven)
```C#
[A_XSDElementProperty("Width","UI")]  public int preferredWidth  = 72;  // 0 = size-to-content
[A_XSDElementProperty("Height","UI")] public int preferredHeight = 72;
[A_XSDElementProperty("MinWidth","UI")]  public int minWidth  = 0;
[A_XSDElementProperty("MinHeight","UI")] public int minHeight = 0;

// WPF-style proportional sizing inside a StackPanel (0 = fixed/auto)
[A_XSDElementProperty("WidthStar","UI")]  public float widthStar  = 0f;
[A_XSDElementProperty("HeightStar","UI")] public float heightStar = 0f;

[A_XSDElementProperty("Margin","UI")]  public Thickness margin;   // space outside
[A_XSDElementProperty("Padding","UI")] public Thickness padding;  // space inside

[A_XSDElementProperty("HorizontalAlignment","UI")] public HorizontalAlignment horizontalAlignment;
[A_XSDElementProperty("VerticalAlignment","UI")]   public VerticalAlignment   verticalAlignment;
[A_XSDElementProperty("HorizontalPos","UI")] public float horizontalPosition = 0.5f; // [0;1]
[A_XSDElementProperty("VerticalPos","UI")]   public float verticalPosition   = 0.5f;

[A_XSDElementProperty("DockMode","UI")]      public DockMode dockMode;
[A_XSDElementProperty("Grid.Column","UI")]   public int gridColumn;
[A_XSDElementProperty("Grid.Row","UI")]      public int gridRow;
[A_XSDElementProperty("ClipToBounds","UI")]  public bool clipOutOfBounds = false;
```
Setting `width`/`height`/`preferred*`/`margin`/`padding` calls `InvalidateLayout()`.

### Layout state (computed)
`DesiredSize`, `arrangedRect`, `ClipRect`, `isMeasureDirty`, `isArrangeDirty`.

### Styling
```C#
[A_XSDElementProperty("ColorHex","UI")]      public string controlColorHex = "#FFFFFF";
[A_XSDElementProperty("ControlColor","UI")]  public ControlColor controlColor;  // named palette
```
Both setters update `controlData.style.tint` and call `UpdateControlData()`.

### Rendering
```C#
public ControlData controlData; // QuadUVs + ControlStyle (sent to the GPU)
public Buffer controlDataBuffer; public DeviceMemory controlDataBufferMemory;
public Sampler maskSampler;  public TextureAsset maskAsset;
public Sampler colorSampler; public TextureAsset colorAsset;
```

### Events
`onEnter/onExit`, `onClick/onAltClick`, `onRelease/onAltRelease`, `onDoubleClick`, `onDrag/onDragStop`, `onScrollUp/onScrollDown`, plus `hover`. Each has a `bubble*` flag so an unhandled event walks up to the parent. `HitTest(point)` tests against `ClipRect`.

## Methods

### Layout
`Measure` returns the desired size (uses `preferred*`, falls back to `min*`/available; a single child is measured inside `padding`). `Arrange` positions the control (writes `transform`), computes `ClipRect`, and arranges its single child by `horizontalPosition`/`verticalPosition`. Containers like [[StackPanel]] override both. `InvalidateLayout`/`InvalidateArrange` mark the chain dirty up to the top root and hand that root to `UILayout.RegisterDirtyRoot` (resolved each tick â†’ triggers a UI re-render).

### Events
`Register*` add handlers; `Resolve*` fire them and, if the matching `bubble*` flag is set, call the parent's resolver. `BubbleAll()` turns bubbling on for everything.

### XML
`ParseXML(name)` loads the doc via `Paths.Doc(name)`, builds a `WindowControl` root, then `RecursiveParse` instantiates child controls by element name (`AnyXMLType.FindType`) and `ResolveAttributes` maps XML attributes onto `[A_XSDElementProperty]` members (actions resolve via `[A_XSDActionDependency]`).

## Structs & enums
`ControlStyle` (tint) Â· `ControlData` (QuadUVs + style) Â· `QuadUVs` Â· `Thickness` (margins/padding) Â· `LayoutRect` (Shrink/Intersect/Contains). Enums: `ControlColor`, `ScalingMode`, `HorizontalAlignment`, `VerticalAlignment`.

## Helpers
```C#
public static string EnumColorToHex(ControlColor color);
public static Vector3D<float> HexToRGB(string hex);
```

## Related
- [[Entity]] â€” base class Â· [[StackPanel]] â€” a container subclass
- [[UI Rasterizer Module]] â€” renders controls Â· [[VULKAN]] â€” the renderer
