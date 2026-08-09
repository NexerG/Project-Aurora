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

**Schema:** `UITypeSchema.xsd` compiles as of 2026-07-31 — see [[xsd-generator-cross-category]].
Load/save here are reflection-driven and do **not** consult the XSD, so they work regardless; XML
validation now works too.

## Member kinds must match the generator's three-way split (2026-07-31)
`DocumentXml` and `XSDGenerator` classify an `[A_XSDElementProperty]` member the same way, and a
mismatch produces XML the schema rejects:

| Member type | Schema | DocumentXml |
|-------------|--------|-------------|
| `List<T>` | element, `maxOccurs=unbounded` | `ChildListFields` → repeated child elements |
| complex `[A_XSDType]` (not enum) | element, `minOccurs=0 maxOccurs=1` | `ComplexMembers` → one nested element |
| everything else | `xs:attribute` | `ScalarMembers` → XML attribute |

`ScalarMembers` originally excluded only complex members, so an annotated `List<>` fell through to
the attribute bucket and wrote `HeadingStyle="System.Collections.Generic.List\`1[...]"`. Any list
member would have hit it, not just headings.

## The writer omits defaults (2026-08-07)
`WriteElement` writes a scalar only when it differs from the same member on a fresh instance of the
type (`XmlReflection.Defaults`, one cached probe per type). Mirror of the reader, which leaves the
field initializer alone when an attribute is absent. A hand-authored attribute set to the default
value does not survive a save — **intended**, the note format is storage, not a document (see the
standing decision in [[xml-save-skips-defaults]]).

Consequence: **whatever supplies the default at load time is what unstyled content becomes.** A note
that omitted `FontSize` is not "16px forever", it is "however big body text is now":

```xml
<Run Text="hello" />       <!-- no size of its own -->
```

Change the body size in `DocumentStyles.xml` and that run restyles, in every note that never
overrode it. **This is the point, not a hazard** (user, 2026-08-08) — it is how an editor-wide
restyle is supposed to work, and it is why styling defaults belong in the styles file rather than in
C# field initializers. A C# initializer is the fallback when no style file speaks; the style file is
the knob.

The corollary for code: a run that *did* write `FontSize="20"` is pinned and will not follow the
scheme. The file cannot distinguish "the author chose 18" from "18 was the default" — that was never
written, deliberately.

Complex members are still written unconditionally.

**Naming constraint:** `WriteElement` names an element after the **type** it wrote, while the schema
names it after the **member**. They must match — hence `RichTextDocument.layout` is annotated
`"DocumentLayout"`. Two members of the same complex type cannot work until the writer stamps the
member name (`child.Name = meta.Name`) and `AssignComplexMember` matches by name instead of by type.

## Saved notes are namespace-qualified (2026-07-31)
`WriteElement` puts elements in `XSDGenerator.NamespaceFor(category)`, and `Save` stamps
`xsi:schemaLocation` on the root with the schema path computed **relative to the note** (a vault sits
wherever the user put it). Before this, `Save` wrote bare element names: saved notes matched no
schema at all, and re-saving a hand-authored note silently stripped its `xmlns`. Parse is unaffected —
it matches on `LocalName`, so notes written before this still load.

Watch for a false pass when testing: validating a bare-namespace document reports **nothing**, because
the validator finds no schema to match rather than failing.
