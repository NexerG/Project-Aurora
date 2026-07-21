# Bug + fix design — XSDGenerator drops cross-category type references

**Status:** identified 2026-07-17, **not implemented** (user deferred the generator change).
**Owner code:** `ArctisAurora.Core.Registry.XSDGenerator` (resolve via `NAMESPACES.md`).

## Symptom
No per-category `.xsd` the generator emits compiles on its own. Any XML file that points at one
(`UI.xml` → `UITypeSchema.xsd`, a note → same) is therefore **not actually schema-governed** — the
schema fails to load before validation even starts.

Verified (untouched `UITypeSchema.xsd`, loaded as `UI.xml` references it):
```
COMPILE ERROR: Type 'http://arctisaurora/AuroraUITypes:VulkanControl' is not declared.
```

## Root cause
The generator emits **one schema per `[A_XSDType]` category** (`{Category}TypeSchema.xsd`,
`targetNamespace = http://arctisaurora/Aurora{Category}Types`). Every reference to another
`[A_XSDType]` type is written with the **local** `types:` prefix and **no `<xs:import>`**, so a type
owned by a *different* category becomes a dangling reference.

Types cross categories constantly:
- `VulkanControl` is `[A_XSDType("VulkanControl", "EntityRegistry")]`, but every UI container lists it
  as an allowed child → `types:VulkanControl` (× ~9) in `UITypeSchema.xsd`, i.e. `AuroraUITypes:VulkanControl`, undeclared.
- UI enums (`ControlColor`, `DockMode`, `HorizontalAlignment`, `VeticalAlignment`) are `"UI"`-category
  but are referenced from `EntityRegistry`/other schemas the same way (e.g. `EntityRegistryTypeSchema.xsd`
  and the old `TextEditorTypeSchema.xsd` both broke on `…:ControlColor`).

Only two imports are ever emitted (`actionSchema.xsd`, `AllTypesSchema.xsd`), so `actions:` and
`allTypes:` refs resolve; cross-category `[A_XSDType]` refs do not.

## Where in the code
- `ResolveTypeName(memberType, xmlAttr)` — returns `$"types:{typeMapped}"` for any `[A_XSDType]`
  scalar member: always the **local** prefix (never the owning category's).
- `GenerateComplexType` `AllowedChildren` branch — child elements hardcode
  `SchemaTypeName = new XmlQualifiedName($"types:{childName}")`: again always local.
- Schema construction — imports are hardcoded to `actionSchema.xsd` + `AllTypesSchema.xsd` only.

## Fix design (proposed, not built)
1. Build a **category → namespace URI** map (`"UI"` → `http://arctisaurora/AuroraUITypes`,
   `"EntityRegistry"` → `…/AuroraEntityRegistryTypes`, …), derivable from the same `[A_XSDType]`
   scan that already groups types by category.
2. When resolving a referenced `[A_XSDType]` type (scalar member **and** allowedChildren child),
   look up its owning category. If it differs from the schema being emitted, qualify the ref with the
   **owning** category's prefix (`entityRegistry:VulkanControl`) instead of `types:`.
3. For each foreign category referenced, declare its prefix on the `<xs:schema>` and emit a matching
   `<xs:import namespace="…" schemaLocation="{Category}TypeSchema.xsd"/>` (dedupe).
4. Circular imports are fine in XSD and expected here (UI ⇄ EntityRegistry once documents/controls
   embed each other) — `XmlSchemaSet` resolves mutual imports as long as `schemaLocation`s resolve.

## Impact / verification
- Regenerates **every** category schema (all currently affected). Not just the document types.
- The generator runs at app bootstrap (not at build); regenerated `.xsd` files can only be produced
  by running the app. Verify by: run app → compile each `{Category}TypeSchema.xsd` standalone (all
  must load) → validate `UI.xml` and `SampleNote.xml` against `UITypeSchema.xsd`.
- Do **not** hand-edit generated `.xsd` to "fix" this — they are overwritten on each run.

## Related state already landed (see [[periodic-editor-architecture]], [[document-xml-persistence]])
- Document types (`Run`/`Paragraph`/`Heading`/`Document`) moved from the orphan `"TextEditor"`
  category to `"UI"` so they generate into `UITypeSchema.xsd` and the note is authored like `UI.xml`.
  The `"TextEditor"` category is now unused; `TextEditorTypeSchema.xsd` becomes stale on next regen.
- Runs are `TextInputControl`s; blocks are `TextBlockControl` (PanelControl) derivatives — the
  document is one `VulkanControl` tree. These are prerequisites, independent of this generator fix.
