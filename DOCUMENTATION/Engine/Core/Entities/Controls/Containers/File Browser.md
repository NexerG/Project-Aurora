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
  - "[[File Browser]]"
  - "[[File Tree]]"
Parent Class:
  - "[[Scrollable]]"
Interfaces:
Used by:
  - "[[Vault Browser]]"
Type:
  - Public
Attributes:
  - A_XSDElementProperty
Namespace: ArctisAurora.Core.UISystem.Controls.Containers
SourceFile: AuroraEngine/Core/UISystem/Controls/Containers/FileBrowserControl.cs
VerifiedAgainst: 2026-08-20
---
## Description

A scrolling list of rows over a [[File Object]] tree, split in two so one model can carry more than one presentation. `FileBrowserControl` owns the root, what a row looks like and `Rebuild()`; `FileTreeControl` adds the openable tree — a folder row toggles and only an open folder contributes its contents. Both are abstract and neither carries `[A_XSDType]`, so neither is authorable from XML: a host subclasses the one it wants and gets an authorable element by declaring `[A_XSDType]` on its own leaf, the way `VaultBrowserControl` declares `<VaultBrowser>`.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `RootPath` | abstract | Folder the listing is read from. Resolved by the leaf, so a host can take it from settings. |
| `PopulateRows()` | abstract | Emits a row per entry the presentation wants shown. `FileTreeControl` fills this in. |
| `Activate(file)` | abstract | What a file row's click does. The engine cannot know, so the leaf answers. |
| `Accepts(file)` | virtual | Files a row is built for. Defaults to all of them. |
| `DisplayName(file)` | virtual | Name a row shows. Defaults to `file.name`, extension intact. |
| `Rebuild()` | public | Destroys every row, re-reads the root, calls `PopulateRows()`. |
| `AddRow(file, depth, expander, activate)` | protected | Builds one row and appends it. |
| `BeginRename(file)` | protected | Turns that entry's row name into a field, in place. |
| `Rename(file, newName)` | virtual | What a committed rename does. Nothing by default. |

## Fields & Properties

```C#
// row metrics
[A_XSDElementProperty("RowHeight", "UI", "Height of a single row in pixels.")]
public int rowHeight = 22;
[A_XSDElementProperty("Indent", "UI", "Left offset a row gains per level of depth, in pixels.")]
public float indent = 12f;
[A_XSDElementProperty("GutterWidth", "UI", "Width of the expander column ahead of a row's name, in pixels.")]
public int gutterWidth = 12;

// row palette — RowSpacing, RowInset, RowFontSize and the five colours follow the same shape
[A_XSDElementProperty("RowColorHex", "UI", "Ground of a row at rest.")]
public string rowColorHex = "#171717";
```

## Methods

### Rebuild
Destroys every row, re-reads `RootPath` into a fresh [[File Object]], and hands off to `PopulateRows()`. A root that does not exist on disk leaves the list empty rather than throwing. Because the root is thrown away rather than refreshed, a rebuild is also how the browser picks up anything added to the folder since the last one.

```
Rebuild()
    destroy every row
    root = Directory.Exists(RootPath) ? new FileObject(RootPath) : null
    if root is null -> return
    PopulateRows()
```

### AddRow
One row is a `Button` over a horizontal `StackPanel` holding a fixed-width gutter label carrying the expander caption, then the name as an [[Editable Label]]. A file passes an empty caption and keeps the gutter, so its name starts on the same x as the folder names around it. Depth becomes the button's left margin. The row keeps a reference to its name control alongside the entry it was built for, which is what `BeginRename` reaches for.

The content panel calls `BubbleAll()`. Without it the row is inert — a `StackPanel` bubbles nothing by default, so hover and release walk glyph → label → panel and stop before reaching the button.

```
AddRow(file, depth, expander, activate)
    gutter = Label(expander) with preferredWidth = gutterWidth
    name   = EditableLabel(DisplayName(file)) tinted folder or file
    content = horizontal StackPanel, invisible mask, BubbleAll()
    row = Button, height = rowHeight, margin.left = depth * indent
    row.label = name
    row.onRelease = activate
```

### BeginRename and Rename
The pair a host implements renaming with. `BeginRename` finds the row built for an entry and opens the in-place edit on it; `Rename` is what the commit calls, and does nothing unless a leaf overrides it — a browser that cannot rename says so by never offering the entry.

```
BeginRename(file)
    row = the FileRowControl whose file is this one
    row.label.BeginEdit(name => Rename(file, name))
```

The menu entry belongs to the leaf, in `BuildRowMenu`, because only the leaf knows whether the thing under the pointer is renameable. `VaultBrowserControl` offers it on notes and not on folders — a folder rename would invalidate the path of every tab open beneath it.

### PopulateRows (FileTreeControl)
Walks the model, emitting a row per directory and per accepted file, and recursing only into directories whose full path is in `expanded`. Toggling flips the path in that set and rebuilds; because the set holds paths rather than nodes, the tree survives a rebuild that replaces every node.

```
AddEntries(folder, depth)
    for child in folder.Children
        if child is Directory
            isOpen = expanded.Contains(child.path)
            AddRow(child, depth, isOpen ? "v" : ">", () => Toggle(child.path))
            if isOpen -> AddEntries(child, depth + 1)
        else if Accepts(child)
            AddRow(child, depth, "", () => Activate(child))
```

## XML

Not authorable. A leaf declares its own element and supplies the hooks; the metric and palette attributes above are inherited and can be authored on it.

```xml
<VaultBrowser Name="Browser" Width="220" ColorHex="#171717" Padding="8"/>
```

Note that a value authored here reaches the control after its constructor has run, so it applies to rows built by a later `Rebuild()`, not to the ones already on screen.

## Related
- [[File Object]] — the path tree the rows are built from
- [[Editable Label]] — a row's name, and the in-place rename gesture
- [[Scrollable]] — the viewport and scrollbar the rows sit in
- [[Vulkan Control]] — base control, `BubbleAll()`, margin and alignment
