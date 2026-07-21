---
date: 2026-05-30
tags:
  - d_Filing
  - d_Serialization
  - d_Registry
cssclasses:
  - Aurora.css
Status: Current
Linker:
  - "[[Arctis Aurora]]"
Class:
  - "[[Paths]]"
Parent Class:
Interfaces:
Used by:
  - "[[Bootstrapper]]"
  - "[[Asset Registries]]"
  - "[[INPUT]]"
Type:
  - Public
  - Static
Attributes:
Namespace: ArctisAurora.Core.Filing.Serialization
SourceFile: AuroraEngine/Core/Filing/Serialization/Paths.cs
VerifiedAgainst: 2026-05-30
---
## Description

Central place for resolving engine/app data paths. On first access it **sets up the [[Virtual File System]] mounts**, then exposes the `Data` sub-folder constants plus `Doc()` / `SamplerDoc()` helpers that resolve through the `VFS`. Because of this, an application inherits engine-default config (Bootstrap, registries, samplers) without copying it â€” those files are read from the engine project's `Data` via a lower-priority mount.

Mounts (set in `Mount()`, called from the first static field initializer):
- **Primary** â€” the running app's own `Data` folder.
- **Engine fallback (Debug)** â€” the engine project's `Data` (`AuroraEngine/Data`, a sibling of each app), so apps inherit engine defaults and can still override any file locally.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `DATA`, `XML`, `XMLSCHEMAS`, `XMLDOCUMENTS`, `XMLDOCUMENTS_INPUTS`, `XMLDOCUMENTS_SAMPLERS`, `FONTS`, `UIMASKS`, `BUILD_UI`, `SCENES` | static readonly | Resolved paths to `Data` sub-folders (primary mount). |
| `BOOTSTRAP` | static readonly | `Doc("Bootstrap.xml")` â€” resolved across mounts. |
| `Doc(name)` | static | Resolve `XML/Documents/{name}` across all mounts (app first, engine fallback). |
| `SamplerDoc(name)` | static | Resolve `XML/Documents/Samplers/{name}` across all mounts. |

## Fields & Properties

```C#
private static readonly bool _mounted = Mount();   // must stay first â€” sets up the VFS

public static readonly string DATA                   = GetPath("Data");
public static readonly string XMLDOCUMENTS           = GetPath("Data\\XML\\Documents");
public static readonly string XMLDOCUMENTS_INPUTS    = GetPath("Data\\XML\\Documents\\Inputs");
public static readonly string XMLDOCUMENTS_SAMPLERS  = GetPath("Data\\XML\\Documents\\Samplers");
public static readonly string XMLSCHEMAS             = GetPath("Data\\XML\\Schemas");
public static readonly string FONTS                  = GetPath("Data\\Fonts");
public static readonly string UIMASKS                = GetPath("Data\\UIMasks");
public static readonly string SCENES                 = GetPath("Data\\Scenes");
public static readonly string BOOTSTRAP              = Doc("Bootstrap.xml");
```

## Methods

### `Doc` / `SamplerDoc` *(public)*
Thin wrappers over `VirtualFileSystem.ResolveFile(...)` for documents under `XML/Documents` (and its `Samplers` sub-folder). Use these instead of `XMLDOCUMENTS + "\\" + name` so engine-default documents resolve from the engine mount when an app doesn't ship its own copy.

### `Mount` *(private)*
Mounts the app `Data` (primary) and, in Debug, the engine project's `Data` (fallback). Skipped if the engine folder doesn't exist or equals the primary.

### `GetPath` *(private)*
Debug â†’ path relative to the running app's source (`../../../Data`). Release â†’ relative to `AppContext.BaseDirectory`. (Shipping bundles data into archives â€” a future `PakMount` â€” instead.)

## Related
- [[Virtual File System]] â€” the mount layer `Paths` configures
- [[Bootstrapper]], [[Asset Registries]], [[INPUT]] â€” consumers
