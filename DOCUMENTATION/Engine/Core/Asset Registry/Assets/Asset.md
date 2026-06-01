---
date: 2026-05-30
tags:
  - d_Filing
  - d_Registry
cssclasses:
  - Aurora.css
Status: Current
Linker:
  - "[[Asset Registries]]"
Class:
  - "[[Asset]]"
Parent Class:
Interfaces:
Used by:
  - "[[Asset Registries]]"
Type:
  - Public
  - Abstract
Attributes:
Namespace: ArctisAurora.Core.Registry.Assets
SourceFile: ParticleSimulator/Core/Registry/Assets/AbstractAsset.cs
VerifiedAgainst: 2026-05-30
---
## Description

The abstract base (type `AbstractAsset`) for everything loadable into the [[Asset Registries]] — fonts, textures, samplers, meshes. It defines three load entry points that concrete assets implement; which one the registry calls depends on whether it's loading a named asset, the engine default, or a whole directory.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `LoadAsset(AbstractAsset asset, string name, string path)` | abstract | Load one named asset from a path. |
| `LoadDefault()` | abstract | Load the engine's built-in default for this asset type. |
| `LoadAll(string path)` | abstract | Discover and load every asset of this type under a directory. |

## Fields & Properties

None — pure abstract contract.

## Methods

All three methods are abstract; see the concrete implementations for behavior.

## Related
- [[Asset Registries]] — stores and hands out assets
- [[Serializer]] — binary (de)serialization used by some asset types
- Implementors: `FontAsset`, `TextureAsset`, `SamplerAsset`
