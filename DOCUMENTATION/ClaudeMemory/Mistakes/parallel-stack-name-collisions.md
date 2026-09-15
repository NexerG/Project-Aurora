# Mistake — a new type in `Core.UI` may not reuse a `Core.UISystem` type's simple name

**What I did wrong (2026-09-06):** landing 4 added `ArctisAurora.Core.UI.CaretControl` beside the outgoing
`ArctisAurora.Core.UISystem.Controls.Text.Document.CaretControl`. Different namespaces, so it compiled. It
killed the boot:

```
System.ArgumentException: An item with the same key has already been added. Key: 334778237
   at AssetRegistries.RegisterSerializableTypes()
```

**Why it happens:** three separate registries in this engine key on the **simple name** and hold every type in
one flat map, so two types sharing a name collide however far apart their namespaces are.

| Registry | Keyed by | Symptom |
|---|---|---|
| `AssetRegistries.RegisterSerializableTypes` | `Serializable.GenerateID(t.Name)` — MD5 of the simple name | throws at bootstrap |
| `AnyXMLType.FindType` / `typeMap` | `[A_XSDType]` name, **first declaration wins** | silently resolves to the wrong type |
| `Context.Set` / `A_ActiveContext` | attribute name, **last registration wins** | the other stack's context quietly stops tracking |

`[@Serializable]` is **inherited**, so the first of these catches every `Entity` subclass whether or not it
declares anything.

**The rule while both stacks lived:** a type in `Core.UI` that shared a simple name with anything in
`Core.UISystem` took a `Next` prefix. **Resolved 2026-09-15** — 6d dropped every prefix (types, XSD names,
contexts) and `Core.UISystem` no longer exists.

**The trap outlives the stacks.** The prefix drop hit it twice more, both XSD-only: `NextWindow` → `Window`
collided with `WindowSetting`'s `"Window"` (now `WindowRoot` / `WindowSetting`), and `Control`'s
`NextVulkanControl` would have taken the `VulkanControl` struct's name (now `"Control"`). Check a new name —
type and XSD — before writing the file, not after the boot fails:

```bash
grep -rn "class <Name>" --include=*.cs AuroraEngine/ | grep -v bin | grep -v obj
git grep -n 'A_XSDType("<Name>"' -- '*.cs'
```

**Rejected:** keying `GenerateID` on `FullName`. It changes the serialized id of every type at once, so every
saved note, scene and session file on disk stops reading.

Related: [[ui-engine-stack]], [[vulkancontrol-needs-xsdtype]], [[xsd-generator-cross-category]]
