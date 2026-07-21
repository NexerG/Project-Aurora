---
date: 2026-05-30
tags:
  - d_Filing
  - d_System
cssclasses:
  - Aurora.css
Status: Current
Linker:
  - "[[Arctis Aurora]]"
System:
Class:
  - "[[Virtual File System]]"
Parent Class:
Interfaces:
Used by:
  - "[[Paths]]"
Type:
  - Public
  - Static
Attributes:
Namespace: ArctisAurora.Core.Filing.Serialization
SourceFile: AuroraEngine/Core/Filing/Serialization/VirtualFileSystem.cs
VerifiedAgainst: 2026-05-30
---
## Description

A **layered virtual file system** for engine + application data. It holds an ordered list of *mounts*; a single-file lookup returns the first mount that has the file (so an application can override an engine default by relative path), and an "enumerate all" unions across mounts. Today the only backend is `DirectoryMount` (a folder on disk); a `PakMount` (Valve-style archive) can be added later with **no call-site changes**, because everything goes through `VirtualFileSystem` instead of touching disk directly. Logical paths are forward-slash, relative to a `Data` root (e.g. `"XML/Documents/UI.xml"`).

See [[Paths]] for how the mounts are set up, and [[Attributes & Conventions]] for the layering model.

## API summary

| Member                                         | Kind   | Summary                                                                                 |
| ---------------------------------------------- | ------ | --------------------------------------------------------------------------------------- |
| `Mount(IDataMount)` / `MountFirst(IDataMount)` | static | Append / prepend a mount (priority order).                                              |
| `TryResolveFile(rel, out full)`                | static | First mount that has the file.                                                          |
| `ResolveFile(rel)`                             | static | First mount with the file, else the primary mount's path (write target / clear errors). |
| `ResolveDir(relDir)`                           | static | First mount containing the directory.                                                   |
| `Open(rel)`                                    | static | `Stream` from the first mount that has the file.                                        |
| `EnumerateAll(relDir, pattern)`                | static | Union of files across all mounts; first mount wins on a name clash.                     |

### `IDataMount` (the backend contract)
`FileExists(rel)` Â· `DirExists(rel)` Â· `Open(rel)` â†’ `Stream` Â· `GetFullPath(rel)`Â· `Enumerate(relDir, pattern)`.

### `DirectoryMount : IDataMount`
Maps logical paths onto a real folder (`Root`). The current (and only) backend.

## Fields & Properties

```C#
private static readonly List<IDataMount> _mounts = new();   // highest priority first
public static IReadOnlyList<IDataMount> Mounts => _mounts;
```

## Methods

### Single-file lookup â€” first mount wins
`ResolveFile` / `TryResolveFile` / `ResolveDir` / `Open` walk the mounts in order and stop at the first that has the path. This is what lets an app override an engine-default file simply by shipping its own copy at the same relative path.

### Enumeration â€” union across mounts
`EnumerateAll` concatenates every mount's matches under `relDir`, de-duplicating by file name (the higher-priority mount wins). Used by directory-driven loaders â€” samplers, inputs, fonts â€” so an app inherits the engine's defaults *and* adds its own.

## Related
- [[Paths]] â€” mounts the application's `Data` plus the engine project's `Data` (fallback)
- [[XML-XSD]] / [[Bootstrapper]] â€” consumers that load through the VFS
