---
date: 2026-05-29
tags:
  - Engine
  - d_Convention
cssclasses:
  - Aurora.css
Status: Current
Linker:
  - "[[Arctis Aurora]]"
---
%% Singleton reference note. Cross-cutting conventions referenced by every class/system doc.
   Not a class doc — do not template it as one. %%

## Engine attributes
The reflection-driven attributes the engine scans for. These are the *current* names — the older `A_Vulkan*` family was renamed to the `A_XSD*` family.

| Attribute | Applies to | Purpose |
| --- | --- | --- |
| `A_XSDType(name, category)` | class / struct / enum / interface | Marks a type as an XSD type. Supports `AllowedChildren`, `MinChildren`, `MaxChildren`, `Description`. |
| `A_XSDElementProperty(name, category)` | field / property | Marks a member as an XSD attribute (scalars) or element (collections/lists). |
| `A_XSDActionDependency(name, category)` | static / instance method | Marks a method as a callable action reference in XML. Used by **keybinds** (resolve by name) and the **[[Bootstrapper]]** (steps with `category="Bootstrap"`). |
| `A_ActiveContext(name)` | static field / property | Registers a named entry in the [[Context]] system. |
| `A_BootstrapStage(stage)` | static method | **Legacy.** The old stage-based bootstrap (`PreGPUAPI`/`PostGPUAPI`). Superseded by XML-driven `A_XSDActionDependency(..., "Bootstrap")` sequencing — see [[Bootstrapper]]. |
| `Serializable` | class / struct | Auto-discovered for serialization; assigned a hashed ID in the `IDMap` registry. |

## Documentation tags (`d_*`)
Domain tags used to slice the docs (and power the [[Engine Docs]] base). Use **lowercase `tags:`** in frontmatter — Obsidian does not index capital `Tags:` as tags.

- `d_System` · `d_Module` — system- and module-level docs
- `d_Rendering` · `d_UI` · `d_Entity` — domains
- `d_Registry` · `d_Filing` · `d_XML` · `d_XSD` — data layer
- `d_Convention` · `d_Decision` — meta (this note, ADRs)

## Doc status values
Set `Status:` in every doc's frontmatter. Drives the **Needs attention** view in [[Engine Docs]].

- `Stub` — skeleton only, no real content yet
- `Draft` — partial / in progress
- `Current` — verified against the code
- `Stale` — known to be behind the code (do not trust without checking)
- `Deprecated` — documents a superseded/`[Obsolete]` type (e.g. [[Vulkan Renderer]])

## Bootstrap step names
The `A_XSDActionDependency(name, "Bootstrap")` actions sequenced by `Bootstrap.xml` (see [[Bootstrapper]]):

`EntityRegistry.ParseXML` → `InputHandler.LoadInputs` → `Engine.SystemSetup` → `Engine.InitWindowing` → `AssetRegistries.InstantiateRegistries` → `AssetRegistries.RegisterSerializableTypes` → `Renderer.InitRenderer` → `Renderer.PreInitialize` → `Renderer.Initialize` → `Context.LoadContexts` → `AssetRegistries.PrepareDefaultAssets` → `AssetRegistries.PrepareAllAssets` → `Renderer.SetupObjects` → `Renderer.PrepareDescriptors` → `Renderer.SetupPipelines` → `Renderer.CreateSyncObjects`

## Frontmatter conventions
- `tags:` lowercase (capital `Tags:` is not indexed as tags)
- `Linker:` always a list, even with one entry
- `Type:` / `Attributes:` always lists
- `SourceFile:` repo-relative path; `VerifiedAgainst:` date or commit
