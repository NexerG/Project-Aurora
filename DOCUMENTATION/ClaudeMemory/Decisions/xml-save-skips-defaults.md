# XML save writes only what differs from a fresh instance

Landed 2026-08-07. Replaces the `IsControlChrome` filter in `XmlReflection.ScalarMembers`.

## Standing decision (user, 2026-08-08)

**A value equal to its default is not written, and that is the intended behaviour — not a wart.** An
author who typed `Level="1"` will not find it in the file after a save. Periodic is an editor; the
note XML is its storage format, not a hand-authored document, and anyone reading the raw file is on
their own. Do not propose "was this attribute present" tracking, formatting preservation, or an
`explicit`/`isSet` flag to round-trip redundant attributes.

## What changed

`DocumentXml.WriteElement` now compares each scalar against the same member on a throwaway instance
of the type and skips it when equal. `XmlReflection.Defaults(Type)` builds and caches that table,
one probe per type. `IsControlChrome` is deleted.

The writer is now the mirror of the reader. `ApplyAttributes` already did *absent attribute → keep
the field initializer*; the writer does *equals the field initializer → omit the attribute*.

## Why the chrome filter existed, and why it had to go

`WriteElement` wrote every annotated scalar. A `TextRun` inherits ~20 from `VulkanControl`, of which
`Margin`/`Padding` are `Thickness` — no `ToString` override, so `Convert.ToString` emitted
`ArctisAurora.Core.UISystem.Controls.Thickness`. On reload `TypeDescriptor` refused it and threw:
**a saved note could not be reopened.** The fix was to drop every member declared on `VulkanControl`
or above, justified as "a note stores content, not control chrome".

That filter infers content-vs-chrome from **declaring type**, which is a proxy, and it broke the
moment a content-meaningful member turned out to live on `VulkanControl`: `controlColorHex`. Per-letter
colour is a standing requirement, so `runColorHex` was declared on `TextInputControl` purely to sit
below the filter's cutoff — duplicate colour state on an object that already had `controlData.style.tint`.
The filter manufactured the problem it was then the reason not to fix.

Skipping defaults subsumes all of it: `Margin` is untouched in a note so it is never written (no
converter needed), inherited layout chrome drops out for the same reason, and `ColorHex` becomes
writable — absent at white, present when set.

## Why a probe instance and not a declared default

Rejected: `[A_XSDElementProperty(..., Default = 16)]`. States the value twice — attribute and field
initializer — free to drift, and needs a sweep over every annotated member with a non-zero default.
Its one advantage is that `XSDGenerator` could emit `default=` into the schema; not worth the drift.

Rejected: `RuntimeHelpers.GetUninitializedObject`. Field initializers run as part of the constructor,
so the probe would report `fontSize` default `0` and `FontSize` would be written on every run forever.

The probe is constructed for real and torn down. `VulkanControl`'s constructor takes a pool row, an
`EntityRegistry` "Controls" entry, an asset lookup and a `CommitTransform`, so `Defaults` calls
`Destroy()` on anything that is an `Entity`; the free is deferred to the frame edge like every other
destroy. One probe per distinct type per process, cached.

## Load-bearing details

- `Defaults` is called only from `WriteElement`. The readers (`AssetRegistries`, `AssetImporter`,
  `DocumentXml.ParseElement`) use `ScalarMembers` alone and never construct a probe, so no
  bootstrap-order hazard — but a save now requires pools and asset registries to be up. True at
  runtime, not true in a bare unit-test process.
- The cache is keyed by `MemberInfo`, not by name. `Type.GetMembers` returns both declarations when a
  derived class shadows a member with `new`, and names would collide.
- Removing `IsControlChrome` also un-filters the **read** path, since `ScalarMembers` is shared. A
  hand-authored note may now set `Width`/`Margin` on a run and have it applied. No note on disk does;
  `SampleNote.xml` is hand-written and carries content attributes only.
- Inert for `AssetRegistries`/`AssetImporter`: their types do not derive from `VulkanControl`, so the
  filter never matched their members. Removing it changes nothing there.

## Not done

- **Complex members are still written unconditionally.** `WriteElement` emits a nested element for any
  non-null complex member, so a note still gains a `<DocumentLayout>` it did not have. Skipping it
  means comparing the whole object member-wise against the probe's. Cosmetic, not a crash.
- **Cascade-written members.** `Block.ApplyLayout` copies `fontSize`/`lineHeight` from the document
  cascade onto runs before a save, so those hold cascade output, not authored input. A heading run
  writes `FontSize="24"` and `ApplyLayout` overwrites it on load — inert noise, unchanged by this work.

## Verified

25/25 on a throwaway console harness that boots the real engine (`Engine.Init(false)` — full
bootstrap, GLFW window, Vulkan device, renderer, both worker threads) with cwd set to Periodic's
output so the VFS resolves Periodic's `Data`. Nothing calls `RichTextDocument.Save` in the app yet —
Ctrl+S is the unchecked P3 item — so the harness is the only save trigger that exists.

- `SampleNote.xml` -> save -> reload: block count, run count, run text, `Bold` and heading levels all
  survive. Saved file is within an attribute of the hand-authored original.
- Omitted as defaults: `Margin`, `Padding`, `DockMode`, `Bold="False"`, `ColorHex="#FFFFFF"`,
  `FontName="default"`, `Level="1"`. Kept because non-default: `Bold="True"`, `Level="2"`,
  `FontName="electrolize"`/`"arial-b"`. No glyph elements.
- `Level="1"` disappearing from the file is lossless — `level` initializes to 1, so the reload
  returns 1. Asserted, not assumed.
- Colour set **after** text (the undefined-attribute-order case): every glyph reports the run's hex
  and the pool row's `style.tint` is actually red. Colour set **before** text, and characters appended
  afterwards: both coloured.
- `ColorHex="#FF8800"` on one run writes exactly once, leaves every other run bare, and comes back on
  reload with its glyphs coloured — the case the old chrome filter made impossible.

## Enabled (done, same day)

- `runColorHex` deleted; a run carries the ordinary `ColorHex`. `VulkanControl.controlColorHex` is now
  `virtual` and `TextControl` overrides it to `RepointGlyphs()`, which pushes the colour onto every
  glyph child; `SyncGlyphs` colours new glyphs as it appends them. Only viable because `ColorHex` is
  declared on `VulkanControl` and the chrome filter would otherwise have kept it out of saved notes.
