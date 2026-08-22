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
[A_XSDElementProperty("CornerRadius","UI")]  public float cornerRadius;
[A_XSDElementProperty("EdgeColorHex","UI")]  public string edgeColorHex = "#000000";
[A_XSDElementProperty("EdgeThickness","UI")] public float edgeThickness;
[A_XSDElementProperty("OutlineColorHex","UI")] public string outlineColorHex = "#000000";
[A_XSDElementProperty("OutlineWidth","UI")]  public float outlineWidth;
[A_XSDElementProperty("Gradient","UI")]      public virtual string gradient = "";
```
Every setter writes its `controlData` field and calls `UpdateControlData()`.

`Gradient` names a ramp in `Gradients.xml` and paints it in place of `ColorHex` — see [[Gradients]]. It is a row number in a shared table rather than stops carried here, so a definition used by many controls exists once. The ramp measures across `arrangedRect`, which the setter mirrors into `controlData` so no `Arrange` override has to know gradients exist, and which a text control overwrites on its glyphs so a ramp spans a run instead of restarting per letter.

There are two separate strokes because there are two distance fields to stroke: `EdgeThickness` is a band of the rounded-box silhouette the corner radius already produces, so it is a border on the control's own rectangle and is measured in design-space pixels, while `OutlineWidth` is a second threshold on the mask's MSDF distance, so it traces the shape inside the quad — the letter, not the letter's box — and is measured in screen pixels because that is the space the MSDF distance is resolved in. A control carrying both gets a box border and an outlined glyph, and the edge is composited last so it wins where they overlap.

The edge contributes its own coverage rather than multiplying into the mask's, so a container masked `invisible` still shows its border; the outline multiplies as the fill does, since an outline with no shape to trace is nothing. Both are off at zero, which is the default and is a real branch in the shader — a zero-width stroke evaluated as a stroke would tint the antialiased boundary pixel by half.

### Rendering
```C#
public ControlData controlData; // QuadUVs + ControlStyle (sent to the GPU)
public Buffer controlDataBuffer; public DeviceMemory controlDataBufferMemory;
public Sampler maskSampler;  public TextureAsset maskAsset;
public Sampler colorSampler; public TextureAsset colorAsset;
```

### Events
`onEnter/onExit`, `onClick/onAltClick`, `onRelease/onAltRelease`, `onDoubleClick`, `onDrag/onDragStop`, `onScrollUp/onScrollDown`, plus `hover`. Each has a `bubble*` flag so an unhandled event walks up to the parent. `HitTest(point)` tests against `ClipRect`.

`hitTestable` (default true) drops a control out of the hit-test entirely, for decorations like a caret or a selection box that would otherwise swallow the click aimed past them. `canBeActiveContext` (virtual, default true) is separate and does not affect hit-testing: a control answering false is still hit, but hands the active context to its parent, so `UICollisionHandling.activeControl` lands on the `Button` rather than the `GlyphControl` actually under the cursor. `GlyphControl` and [[Label]] override it to false. The press stores the resolved control and the release compares against it, which is what makes a press that began elsewhere activate nothing.

`onDoubleClick` fires from that same release, when the key tracker's `tapCount` reads exactly two and both presses resolved to the same control. Both tests are needed: the count belongs to the mouse button rather than to anything on screen, so on its own two quick clicks across two neighbouring tabs read as one double click on the second, and `>= 2` would fire again on the third tap of a triple. It resolves on the control under the pointer and bubbles from there, so the deepest hit being a [[Glyph]] two levels beneath the button it belongs to costs the handler nothing.

## Methods

### Layout
`Measure` returns the desired size (uses `preferred*`, falls back to `min*`/available; a single child is measured inside `padding`). `Arrange` positions the control (writes `transform`), computes `ClipRect`, and arranges its single child by `horizontalPosition`/`verticalPosition`. Containers like [[StackPanel]] override both. `InvalidateLayout`/`InvalidateArrange` mark the chain dirty up to the top root and hand that root to `UILayout.RegisterDirtyRoot` (resolved each tick â†’ triggers a UI re-render).

### Events
`Register*` add handlers; `Resolve*` fire them and, if the matching `bubble*` flag is set, call the parent's resolver. `BubbleAll()` turns bubbling on for everything.

Bubbling is a contract an override can break, and one does: **`TextInputControl.ResolveOnClick` begins an edit and returns without calling base**, so `bubbleClick` is dead on it whatever the XML says and nothing nested inside a `TextInput` can ever be clicked. That is why button captions and list rows use [[Label]] — text that is drawn and never edited, bubbling from its constructor the way `GlyphControl` does, on the principle that decoration must not consume input. An override that does not call base is silently swallowing every event below it.

### XML
`ParseXML(name)` loads the doc via `Paths.Doc(name)`, builds a `WindowControl` root, then `RecursiveParse` instantiates child controls by element name (`AnyXMLType.FindType`) and `ResolveAttributes` maps XML attributes onto `[A_XSDElementProperty]` members (actions resolve via `[A_XSDActionDependency]`).

Scalars convert through `TypeDescriptor`, so a compound value needs a `TypeConverter` or the whole parse dies on it — `Thickness` is the one that has one, and `ThicknessConverter` reads `Padding="8"`, `Padding="8,4"` and `Padding="1,2,3,4"` as the struct's own one-, two- and four-argument constructors, which makes the two-value form `(horizontal, vertical)` and not CSS's `vertical horizontal`.

## Structs & enums
`ControlStyle` (tint) Â· `ControlData` (QuadUVs + style + clip + corner/edge/outline, 100 bytes) Â· `QuadUVs` Â· `Thickness` (margins/padding) Â· `LayoutRect` (Shrink/Intersect/Contains). Enums: `ControlColor`, `ScalingMode`, `HorizontalAlignment`, `VerticalAlignment`.

## Helpers
```C#
public static string EnumColorToHex(ControlColor color);
public static Vector3D<float> HexToRGB(string hex);
```

## Related
- [[Entity]] â€” base class Â· [[StackPanel]] â€” a container subclass
- [[UI Rasterizer Module]] â€” renders controls Â· [[VULKAN]] â€” the renderer
