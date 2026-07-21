# Pattern — note persistence is XML via DocumentXml (NOT the Serializer)

Two unrelated "serialization" paths exist in the engine — do not confuse them:

| Path | Class | Format | Use |
|------|-------|--------|-----|
| Notes / editor documents | `DocumentXml` (`Controls/Text/Document/`) | **XML** | `RichTextDocument` load/save |
| Scenes / asset blobs | `Serializer` (`Filing/Serialization/`) | **binary** (`BinaryWriter`) | not for notes |

`Serializer.SerializeAll/SerializeAttributed` write **bytes**, not XML. The note format is engine
XML, so notes go through `DocumentXml`, never `Serializer`.

## How DocumentXml works (reuse this pattern for new XML-data types)
Attribute-driven reflection, same shape as `VulkanControl.ParseXML`:
- element name → `Type` via `AnyXMLType.FindType` (matches `[A_XSDType].Name`).
- XML attribute ⇄ scalar member via `[A_XSDElementProperty]` (case-insensitive,
  `TypeDescriptor` converters).
- nested element ⇄ child attached to the parent's matching `List<>` field. The document tree is now
  all `VulkanControl`s (`Block : TextBlockControl`, `TextRun : TextInputControl`), so blocks/runs
  inherit `Entity`'s generic `children` (`List<Entity>`) and `_components` lists **alongside** the
  model's own typed lists (`RichTextDocument.blocks`, `ContentBlock.inlines`). `AttachChild` therefore
  picks the **most-specific** accepting list (by inheritance depth of the element type) so a `<Run>`
  lands in `inlines` (`TextRun`) not the inherited `children` (`Entity`). Abstract `Block`/`ContentBlock`
  carry no `[A_XSDType]`, so only concrete blocks are emitted; content blocks use
  `allowedChildren: typeof(TextInputControl)` → expands to its only `[A_XSDType]` subtype, `Run`.

Consequence: adding a new block/inline/run-style needs only the `[A_XSDType]`/`[A_XSDElementProperty]`
attributes — load and the generated XSD pick it up. No hand-mapping.

**Save caveat (latent):** blocks/runs now inherit `Entity.children` (`List<Entity>`) and
`_components`. `DocumentXml.WriteElement` writes every public `List<>` as child elements, so it would
try to emit glyphs/components (no `[A_XSDType]`) and throw. Load is unaffected (runs have no child
*elements* in XML; blocks' `<Run>` children route to `inlines`). `Save` is not wired up yet — it needs
to write only the model lists, not the inherited control plumbing, before P3 editing saves documents.

**Schema caveat:** the note points at `UITypeSchema.xsd` but that schema does not compile yet — see
[[xsd-generator-cross-category]]. Load/save here are reflection-driven and do **not** consult the XSD,
so they work regardless; only editor-side XML validation is blocked until the generator is fixed.
