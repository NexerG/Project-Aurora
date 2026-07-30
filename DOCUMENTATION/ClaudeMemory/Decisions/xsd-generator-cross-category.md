# Bug + fix design — XSDGenerator drops cross-category type references

**Status:** identified 2026-07-17, **implemented + verified 2026-07-30**.
**Owner code:** `ArctisAurora.Core.Registry.XSDGenerator` (resolve via `NAMESPACES.md`).

## Symptom
The generator emits one schema per `[A_XSDType]` category (`{Category}TypeSchema.xsd`,
`targetNamespace = http://arctisaurora/Aurora{Category}Types`). Every reference to another
`[A_XSDType]` was written with the **local** `types:` prefix and no `<xs:import>`, so a type owned by
a *different* category became a dangling reference. XSD rejects the **whole file** on a dangling
type ref, not just that reference — so any XML pointing at it silently loses validation entirely.

## Root cause + where it was
Two emission sites hardcoded the local prefix (the other two `types:` sites are same-category and
were always correct — the category union and the element for the type being emitted):
- `ResolveTypeName` — `return $"types:{typeMapped}"` for any `[A_XSDType]` scalar member.
- `GenerateComplexType` `AllowedChildren` branch — `SchemaTypeName = $"types:{childName}"`.

Only `actionSchema.xsd` and `AllTypesSchema.xsd` were ever imported, so `actions:` / `allTypes:`
resolved while cross-category refs did not.

## What was implemented
1. `categoryMap` (`Type -> category`), built by the same reflection scan idiom as `typeMap`.
2. `Qualify(owningCategory, typeName, currentCategory, foreignCategories)` — emits `types:` when the
   owner is the schema being written, otherwise `{OwningCategory}:{typeName}` and records the owner.
   **Prefix = the category name verbatim** (`ProbeB:ProbeBeta`), not the camelCase the original
   design sketched — `"UI"` would camel-case to the unreadable `uI`.
3. `AddForeignImports` — declares each recorded category's prefix and emits a matching
   `<xs:import>`, called just before `WriteSchema` (the referenced set is only known after emission).
4. Both emission sites now route through `Qualify`; `currentCategory` + a `HashSet<string>` collector
   are threaded through `GenerateComplexType` / `ResolveTypeName`.
5. **Fingerprint had to change too** — `BuildCategoryFingerprint` now includes the owning category of
   each referenced type (`OwningCategoryOf(ReferencedTypeOf(memberType))`, and `Category:Name` for
   allowed children). Without this the emitted content changes while the hash does not, so
   `NeedsRegeneration` skips the file and the fix never reaches disk. `ReferencedTypeOf` mirrors the
   collection/nullable unwrapping that emission does.

**Uncategorized stays local on purpose.** Uncategorized types get no schema file, so importing one
would point `schemaLocation` at a file that does not exist and break the schema the same way.
They remain `types:`-prefixed and dangling exactly as before.

## Verification (2026-07-30)
Throwaway harness (scratchpad, outside the repo): redirect `Paths.XMLSCHEMAS` to a scratch dir by
setting the CWD before `Paths` static-initializes, run `GenerateXSD()`, then compile every emitted
`.xsd` standalone in its own `XmlSchemaSet`.
- **The repo currently contains no cross-category reference** — the fix emitted zero foreign imports
  against real types, and every category schema already compiled. The bug was **latent, not active**;
  this is preventative. (Contradicts the pre-fix claim here that no schema compiled — that was true
  in the 2026-07-17 snapshot, before `IsAbstract` removed `VulkanControl` from allowed-children.)
- Verified with **probe types added to the harness assembly only**: `ProbeAlpha` (`"ProbeA"`)
  referencing an enum + an allowed child from `"ProbeB"`. Without the fix:
  `AuroraProbeATypes:ProbeBeta is not declared` + `AuroraProbeATypes:ProbeChild is not declared`.
  With it: correct `ProbeB:` prefixes, `<xs:import>` of `ProbeBTypeSchema.xsd`, compiles clean.
- Final run with `Periodic.dll` loaded: **14/14 schemas compile standalone**, probes included.

## Harness gotcha — generator output depends on which assemblies are loaded
A run with only `AuroraEngine` loaded reports `actions:Input is not declared` and
`InputTypeSchema.xsd` failing to compile. **This is a harness artifact, not a bug.** The
`"Input"`-category actions live in the app projects — `Periodic/Editor/Decorations.cs`
(`Write`, `ExitApplication`) and `AuroraEditor/EditorProgram/UIFunctions/Decorations.cs`
(`ExitApplication`) — so `actionSchema.xsd` only declares the `Input` category when an app assembly
is loaded. Any future harness must `Assembly.LoadFrom` the app dll, matching what boot actually has.
(Also: grepping for these attributes must handle the **named-argument** form `category:"Input"`,
not just the positional `"Name", "Category"` form.)

## Separate bug found while verifying — NOT fixed
- **A complex `[A_XSDType]` as a scalar member emits an invalid schema.** Scalar members become
  `xs:attribute`, which only accepts simple types, so a member typed as a non-enum `[A_XSDType]`
  produces `... is not declared, or is not a simple type`. Latent — the repo only references enums
  across categories today. Found via the first probe iteration.

## Stale artifacts (mention only)
- `TextEditorTypeSchema.xsd` is dead — the `"TextEditor"` category is unused, so the generator no
  longer emits it, but the committed file remains (and is the only committed schema with dangling
  refs: `types:ControlColor`/`DockMode`/`HorizontalAlignment`/`VeticalAlignment`, all `"UI"`).
- `SchemaManifest.xml` lists `RegistryTypeSchema.xsd`, which no longer exists.
- Committed schemas under `*/Data/XML/Schemas/` are a **2026-07-17** snapshot; they regenerate only
  by running an app. The fingerprint change means the next boot rewrites all of them.

## Related state (see [[periodic-editor-architecture]], [[document-xml-persistence]])
- Document types (`Run`/`Paragraph`/`Heading`/`Document`) live in category `"UI"`.
- `A_XSDTypeAttribute.IsAbstract` now exists and is what keeps `VulkanControl` registered with
  EntityRegistry while excluding it from elements and allowed-children — see
  [[vulkancontrol-needs-xsdtype]].
