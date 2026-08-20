---
date: 2026-08-20
Status: Current
tags:
  - d_Filing
cssclasses:
  - Aurora.css
Linker:
System:
Class:
  - "[[File Object]]"
Parent Class:
Interfaces:
Used by:
  - "[[File Browser]]"
Type:
  - Public
Attributes:
Namespace: ArctisAurora.Core.Filing
SourceFile: AuroraEngine/Core/Filing/FileObject.cs
VerifiedAgainst: 2026-08-20
---
## Description

A node in a tree that points at a path on disk — one per file and one per folder, with a folder holding the list of what is inside it. It is the model a browser presents and nothing more: it carries no expansion state, no filter and no display rules, because two browsers over the same folder show it differently and neither one's choices belong in the shared node.

A folder is listed on first access rather than in the constructor, so a tree whose folders start closed never reads a folder nobody opened.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `path` | field | Full path this node points at. |
| `name` | field | `Path.GetFileName(path)`, extension intact — the presentation decides whether to strip it. |
| `type` | field | `File` or `Directory`, read from the path's attributes at construction. |
| `parent` | field | Node this one was listed under; null on a root. |
| `Children` | property | Contents of a folder, listed on first access. A file returns an empty list. |
| `Refresh()` | public | Drops the listing so the next access re-reads the folder. |
| `icon` | field | Declared, and set by nothing so far. |

## Methods

### Children
Reads the folder the first time it is asked and keeps the result. A file answers with a shared empty list rather than null, so a caller can walk any node without asking what kind it is first.

```
Children
    if type is File      -> empty list
    if children is null  -> Load()
    return children
```

### Load
Directories first, then files, each sorted case-insensitively. NTFS already returns them alphabetically, so the sort changes nothing visible today — it is there so the order a browser shows is the browser's own and not whatever the filesystem happened to hand back.

```
Load()
    children = new list
    for directory in Directory.GetDirectories(path), sorted
        children.Add(new FileObject(directory) with parent = this)
    for file in Directory.GetFiles(path), sorted
        children.Add(new FileObject(file) with parent = this)
```

### Refresh
Drops the listing. The next `Children` re-reads the folder from disk.

## Related
- [[File Browser]] — the control that turns this tree into rows
