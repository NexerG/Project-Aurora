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

**The rule while both stacks live:** a type in `Core.UI` that shares a simple name with anything in
`Core.UISystem` takes a `Next` prefix, and landing 6 renames it back when `Core.UISystem` is deleted. Three
places already do this — `VulkanControlData` (the XSD name of `Core.UI.VulkanControl`), `NextHovering` /
`NextActiveControl` / `NextPressTarget`, and now `NextCaretControl`.

**Still to come:** `IconControl` at landing 5, and `SelectionControl` whenever it lands. Check the name before
writing the file, not after the boot fails:

```bash
grep -rn "class <Name>" --include=*.cs AuroraEngine/ | grep -v bin | grep -v obj
```

**Rejected:** keying `GenerateID` on `FullName`. It changes the serialized id of every type at once, so every
saved note, scene and session file on disk stops reading.

Related: [[ui-engine-stack]], [[vulkancontrol-needs-xsdtype]], [[xsd-generator-cross-category]]
