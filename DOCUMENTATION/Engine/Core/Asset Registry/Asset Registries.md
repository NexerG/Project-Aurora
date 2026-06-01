---
date: 2026-05-30
tags:
  - d_System
  - d_Registry
  - d_Filing
cssclasses:
  - Aurora.css
Status: Current
Linker:
  - "[[Arctis Aurora]]"
System:
  - "[[XML-XSD]]"
Class:
  - "[[Asset Registries]]"
Parent Class:
Interfaces:
  - "[[IXMLParser]]"
Used by:
Type:
  - Public
Attributes:
  - A_XSDType
Namespace: ArctisAurora.EngineWork.Registry
SourceFile: ParticleSimulator/Core/Registry/AssetRegistries.cs
VerifiedAgainst: 2026-05-30
---
## Description

A **type-indexed library of typed dictionaries** — the single place to fetch any loaded asset (mesh, font, texture, sampler, style, action, …). Each registry is a `Dictionary<TKey, TValue>` stored in two parallel lookups: `library` keyed by the **value Type**, and `libraryByName` keyed by a **string name**. The set of registries is declared in `Registry.xml` and built at bootstrap; assets are then loaded into them. Resolved through [[Paths]] / [[Virtual File System]], so the engine's default `Registry.xml` is used unless an app overrides it.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `GetRegistryByValueType<K,V>(Type t)` | static | The dictionary whose value type is `t`. |
| `GetRegistryByKeyType<K,V>(Type t)` | static | The dictionary whose key type is `t`. |
| `GetRegistryByName<K,V>(string name)` | static | The dictionary registered under `name`. |
| `GetAsset<T>(string name)` | static | One asset of type `T` by name (throws if missing). |
| `AddLibraryEntry(string name, object dict, Type t)` | static | Register a dictionary under both lookups (no-op if `t` already present). |
| `ParseXML(string xmlName)` | static | Build registries from `Registry.xml`. |

**Bootstrap steps** (`[A_XSDActionDependency(..., "Bootstrap")]`): `InstantiateRegistries` → `RegisterSerializableTypes` → `PrepareDefaultAssets` → `PrepareAllAssets`.

## Fields & Properties

```C#
public static Dictionary<Type, object> library = new();   // value Type → dictionary
public static Dictionary<string, object> libraryByName = new();   // name → dictionary
```

## Methods

### Lookup
`GetRegistryByValueType` / `GetRegistryByKeyType` / `GetRegistryByName` return the underlying typed dictionary; `GetAsset<T>(name)` is the convenience accessor for a single named asset.

### Building (bootstrap)
- `InstantiateRegistries` → `ParseXML("Registry.xml")` creates an empty `Dictionary<K,V>` per `<Dictionary>` element and registers it under both lookups.
- `RegisterSerializableTypes` scans all assemblies for `[Serializable]` types and stores them in the `IDMap` registry keyed by a hashed ID (used by the [[Serializer]]).
- `PrepareDefaultAssets` loads engine defaults: default mesh, the `uidefault` quad mesh, default font, default + invisible textures, a default `ControlStyle`, and the default sampler.
- `PrepareAllAssets` loads every sampler via `SamplerAsset.LoadAll` (unioned across mounts).

### Gotcha
`PrepareDefaultAssets` still imports the `uidefault` mesh from a **hardcoded absolute FBX path** — a known piece of engine techdebt; the asset isn't in the repo yet.

## Data / XML

```xml
<AssetRegistries xmlns="http://arctisaurora/AuroraAssetRegistryTypes">
  <Dictionary Name="meshes" KeyType="xs:string" ValueType="AVulkanMesh"/>
  <Dictionary Name="fonts" KeyType="xs:string" ValueType="FontAsset"/>
  <Dictionary Name="Samplers" KeyType="xs:string" ValueType="SamplerAsset"/>
  <!-- … -->
</AssetRegistries>
```

## Related
- [[XML-XSD]] — type resolution + parsing
- [[Asset]] — the base type loaded into these registries
- [[Paths]] · [[Virtual File System]] — where `Registry.xml` is resolved
