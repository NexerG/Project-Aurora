# Mistake — do NOT remove `[A_XSDType]` from `VulkanControl`

**What I did wrong (2026-07-17):** to stop the XSD generator from suggesting `VulkanControl` as an
authorable element/child, I removed its `[A_XSDType("VulkanControl", "EntityRegistry")]`. User reverted.

**Why it's wrong:** the **EntityRegistry tracks all derivatives of `VulkanControl` through that
`[A_XSDType]` registration.** Dropping it de-registers VulkanControl (and breaks derivative tracking).
The attribute is load-bearing for the registry, not just for schema generation.

**The real constraint:** the single `[A_XSDType]` does double duty —
1. registers the type with EntityRegistry (**required**), and
2. tells `XSDGenerator` to emit it as an element + offer it as an allowed child (**not wanted** for a
   base class you never author in XML).

These two roles are coupled through one attribute. The correct fix decouples them (e.g. an
`abstract`/non-authorable flag on `A_XSDTypeAttribute` that keeps registration but makes the generator
skip the type when emitting elements and when building `AllowedChildren` choices) — **not** attribute
removal. See [[xsd-generator-cross-category]].

**Rule:** an `[A_XSDType]` may be needed for registration even when the type must never appear in XML.
Don't strip it to change schema output.
