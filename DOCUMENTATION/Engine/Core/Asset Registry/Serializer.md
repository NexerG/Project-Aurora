---
date: 2026-05-30
Status: Current
tags:
  - d_System
  - d_Serialization
  - d_Filing
cssclasses:
  - Aurora.css
Linker:
  - "[[Arctis Aurora]]"
Class:
  - "[[Serializer]]"
Parent Class:
Interfaces:
Used by:
  - "[[Aurora Font]]"
  - "[[Atlas Meta Data]]"
Type:
  - Public
  - Sealed
Attributes:
Namespace: ArctisAurora.Core.Filing.Serialization
SourceFile: ParticleSimulator/Core/Filing/Serialization/Serializer.cs
VerifiedAgainst: 2026-05-30
---
## Description

A reflection-driven **binary** serializer. It walks an object's fields and writes / reads them recursively through a `BinaryWriter` / `BinaryReader`. Two modes: **All** (every field) and **Attributed** (only `[@Serializable]` fields, skipping `[NonSerializable]`). Used for the font atlas data (`.agd`) and scenes.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `SerializeAttributed<T>(T obj, string path)` | static | Write only `[@Serializable]` fields. |
| `DeserializeAttributed<T>(string path, ref T obj)` | static | Read them back. |
| `SerializeAll<T>(T obj, string path)` | static | Write **every** field (ignores attributes). Use at your own risk. |
| `DeserializeAll<T>(string path, ref T obj)` | static | **NOT IMPLEMENTED** — `RecursiveDeserializeAll` is a stub. |

## Fields & Properties

```C#
private static readonly HashSet<Type> _builtInTypes = { string, decimal, DateTime, DateTimeOffset, TimeSpan, Guid, char, int, float };
// IsBuiltInType also covers all primitives, enums, and Nullable<T> of the above.
```

## Methods

### Serialize
`RecursiveSerialize{All,Attributed}` walk the fields:
	skip `[NonSerializable]`
	primitive → `ConvertToBytes` → write
	`string` → length + UTF-8 bytes
	struct → recurse
	array → length, then each element
	In **Attributed** mode, array/complex elements must themselves be `[@Serializable]` and are written with a **hashed type ID** (`Serializable.GenerateID`) before the element body.

### Deserialize
`RecursiveDeserializeAttributed` mirrors it:
	primitive via `ConvertToBytes(Type)` + `BitConverter`
	string via length
	struct/class via `Activator.CreateInstance` + recurse
	array elements are resolved by reading the hashed type ID and looking it up in the **`IDMap`** registry ([[Asset Registries]])
	`DeserializeAll` is currently a TODO stub.

## Helpers

```C#
private static byte[] ConvertToBytes(object value);  // primitive → bytes
private static byte[] ConvertToBytes(Type type);     // zeroed buffer sized to the primitive (for reads)
private static byte[] ReturnDefault(Type type);      // default bytes for a null serializable
```

## Related
- [[IDeserialize]] — interface for types that need *custom* (non-reflection) deserialization
- [[Atlas Meta Data]] · [[Aurora Font]] — serialized via this
- [[Asset Registries]] — provides the `IDMap` type→ID table used for polymorphic arrays
