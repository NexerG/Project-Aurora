---
date: 2026-05-30
tags:
  - Engine
  - d_XML
  - d_XSD
  - d_System
cssclasses:
  - Aurora.css
Status: Current
Linker:
  - "[[Arctis Aurora]]"
System:
  - "[[XSDGenerator]]"
Dependencies:
  - "[[IXMLParser]]"
  - "[[A_XSDElementPropertyAttribute]]"
  - "[[A_XSDTypeAttribute]]"
  - "[[A_XSDActionDependencyAttribute]]"
Implementors:
  - "[[INPUT]]"
  - "[[Asset Registries]]"
  - "[[Vulkan Control]]"
Namespace: ArctisAurora.Core.Registry
SourceFiles: ParticleSimulator/Core/Registry/XSDGenerator.cs
VerifiedAgainst: 2026-05-30
---
## Overview

The data layer that lets the engine be driven by XML. It has two halves:
1. **Schema generation** — reflect over types tagged with the `A_XSD*` attributes and emit `.xsd` schemas describing the allowed XML.
2. **XML parsing** — parse XML documents (validated against those schemas) into live engine objects.

Almost everything configurable rides on this: the [[Asset Registries]] definitions, the UI tree ([[Vulkan Control]]), keybinds ([[INPUT]]), the [[Bootstrapper]] sequence, and samplers. The C# type *is* the schema — there is no hand-authored XSD.

## Architecture

```mermaid
graph TD
  Types[Types tagged A_XSDType / A_XSDElementProperty / A_XSDActionDependency] --> Gen[XSDGenerator]
  Gen --> Schemas[.xsd schemas]
  Schemas -. validate .-> Docs[XML documents]
  Docs --> Parse[IXMLParser implementations]
  Parse --> Objects[engine objects]
  AnyType[AnyXMLType] -. string ↔ Type .-> Parse
```

- **`A_XSDType(name, category)`** — marks a class/struct/enum as an XSD type (supports `AllowedChildren`, `Min/MaxChildren`). See [[Attributes & Conventions]].
- **`A_XSDElementProperty(name, category)`** — a member becomes an XSD attribute (scalars) or element (collections).
- **`A_XSDActionDependency(name, category)`** — a method becomes a referenceable action (keybinds, bootstrap steps).
- **`XSDGenerator`** — emits the schemas. **`AnyXMLType`** — resolves type-name strings ↔ `Type` at parse time. **`IXMLParser<T>`** — the parse contract systems implement.

## Lifecycle / Flow
1. At startup (before bootstrap) `XSDGenerator.GenerateXSD()` reflects all assemblies and writes the schemas to `Paths.XMLSCHEMAS` (skipping unchanged ones).
2. Each system then parses its own XML through the [[Bootstrapper]] steps — `EntityRegistry`, `AssetRegistries`, `InputHandler`, `VulkanControl`, all using `AnyXMLType` to resolve type names.

## Data / XML formats
Generated per run:
- `{Category}TypeSchema.xsd` — complex + enum types for one category
- `AllTypesSchema.xsd` — union of all type names across categories
- `actionSchema.xsd` — all `[A_XSDActionDependency]` methods as string enumerations

## Invariants & gotchas
- **Do not hand-edit** generated `.xsd` files — overwritten each run.
- `MemberMap` (C# primitive → `xs:*`) and `AnyXMLType.typeMap` (string → `Type`) are two halves of one mapping and must be kept in sync by hand.
- An `Action` attribute only resolves if the XML name matches the method name exactly (known sharp edge tracked in the engine WIP list).

## Key types
- [[XSDGenerator]] · `AnyXMLType` · [[IDeserialize]] / `IXMLParser`

## Related systems
- [[Bootstrapper]] · [[Asset Registries]] · [[INPUT]] · [[Vulkan Control]]
