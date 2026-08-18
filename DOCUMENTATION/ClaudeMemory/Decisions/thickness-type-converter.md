# Decision — `Thickness` parses from XML by mirroring its own constructors, not CSS

**Date:** 2026-08-18
**Status:** LANDED. Verified 12/12 on a probe against the real `TypeDescriptor` path; Periodic boots
with `Padding="8"` authored in `UI.xml`.
**Scope:** `ArctisAurora.Core.UISystem.Controls` (`VulkanControl.Thickness`,
`VulkanControl.ThicknessConverter`), `Periodic.Editor.CustomControls` (`VaultBrowserControl`).

## Decisions

### 1. The defect was a missing converter, not a missing schema

`Margin` and `Padding` have carried `[A_XSDElementProperty]` on every control since the layout
properties existed, so they looked authorable and were not. `TypeDescriptor.GetConverter` on a type
with no `[TypeConverter]` hands back the base `TypeConverter`, whose `ConvertFrom` throws
`NotSupportedException` — and both XML readers call it inside a loop with no per-attribute guard, so
one `Padding="8"` killed the entire parse rather than one attribute.

The schema side needed nothing. `Thickness` has no `[A_XSDType]`, so `XSDGenerator.ResolveTypeName`
already fell through every map to `xs:string` and emitted `<xs:attribute name="Padding"
type="xs:string" />`. The XSD had been correct and unusable the whole time.

One `[TypeConverter(typeof(ThicknessConverter))]` fixes both readers at once, because
`VulkanControl.ResolveAttributes` and `XmlReflection.ApplyAttributes` both route scalars through
`TypeDescriptor`. Neither call site changed.

### 2. `"8,4"` is `(horizontal, vertical)` — the constructor's order, not CSS's

The struct's 4-arg constructor is `(top, right, bottom, left)`, which *is* CSS order, but its 2-arg
is `(horizontal, vertical)`, which is the **reverse** of CSS's 2-value shorthand. The type had
already picked both conventions, so the converter could not satisfy both.

**Chosen (user, 2026-08-18): mirror the constructors.** `"8"` → uniform, `"8,4"` → `(h, v)`,
`"1,2,3,4"` → `(t, r, b, l)`. XML and C# always mean the same thing, and the probe asserts
`"8,4" == new Thickness(8, 4)` so the two cannot drift apart silently.

**Rejected: full CSS semantics** (`"8,4"` → `vertical horizontal`). Familiar to anyone authoring the
XML like a stylesheet, but it makes `Padding="8,4"` and `new Thickness(8, 4)` produce different
boxes — a difference nothing in the codebase would catch, in a codebase with no automated tests.

**Rejected: supporting only 1 and 4 values**, the two forms both conventions agree on. It dodges the
question instead of answering it, and leaves the shorthand to be re-litigated later.

CSS's 3-value form has no constructor to mirror, so it is a `FormatException` alongside 5+ values,
empty strings and unparseable numbers.

### 3. Read direction only

`TypeConverter` is a two-way type and only `ConvertFrom` is implemented, because the write direction
would be dead code that does not work anyway: both save paths — `DocumentXml.WriteElement` and
`SettingsRegistry.WriteDiff` — format with `Convert.ToString(value, InvariantCulture)`, which never
consults a `TypeConverter`. A `ConvertTo` would sit there uncalled while `Convert.ToString` kept
emitting the struct's type name.

Neither save path can reach a `Thickness` today: they write document nodes and settings groups, and
`margin`/`padding` are declared only on `VulkanControl`, which neither serializes. **This becomes a
real defect the moment a control tree is saved** — the fix then is routing those two writers through
`TypeDescriptor`, which is the same shape as the `xs:boolean` `True` defect still open on
`SettingsRegistry.WriteDiff`, not more converter code.

### 4. Culture comes from the caller

`ConvertFrom` parses each side with the `CultureInfo` it is handed rather than a hardcoded
`InvariantCulture`. Both readers enter through `ConvertFromInvariantString`, so the culture is
invariant in practice and `"2.5"` is 2.5 on a comma-decimal machine — asserted by the probe.

### 5. First converter in the codebase

Nothing else defines a `TypeConverter`, and nothing else parsed a comma-separated attribute, so this
sets the precedent for compound scalars: **comma-separated, whitespace trimmed, arity picks the
meaning, and the arities are whatever the C# constructors already are.** `LayoutRect` and the
`Vector2D`/`Vector3D` members are the next candidates if they are ever made authorable.

## Verified

- Builds clean; no new warnings in `VulkanControl.cs`.
- Probe against `TypeDescriptor.GetConverter(typeof(Thickness))` — **12/12**: uniform, 2-value,
  4-value, whitespace around commas, fractional values, equality with `new Thickness(8, 4)`, and
  rejection of 3 values, 5 values, `""` and `"a,b"`.
- Periodic boots to `Starting Main system` with `Padding="8"` on `<VaultBrowser>` and **empty
  stderr** — the parse that previously died now completes.
- `VaultBrowserControl`'s `padding = new Thickness(8)` and its workaround comment are gone; the
  sidebar inset is authored. Not yet eyeballed — the value is asserted, the pixels are not.

## Still open

- Save side, per decision 3.
- `Thickness` has no `ToString()`, so it prints as its type name anywhere `Convert.ToString` or
  string interpolation reaches it.

Related: [[xml-save-skips-defaults]], [[settings-registry]], [[vault-browser-and-shell]],
[[document-xml-persistence]]
