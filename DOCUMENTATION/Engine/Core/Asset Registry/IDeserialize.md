---
date: 2026-02-19
Status: Current
tags:
  - d_Serialization
cssclasses:
Linker: "[[Serializer]]"
Class:
  - "[[IDeserialize]]"
Parent Class:
Interfaces:
Type: Internal
Attributes:
---
## Description

Interface implemented in classes that are going to be automatically deserialized in [[Bootstrapper]] or another system. Will usually use custom deserialization code. Just the one method: `void Deserialize(string path)`. Implemented by [[Aurora Font]] and [[Atlas Meta Data]].
## Input/Output

- public void `Deserialize(string path)` -> calls custom implemented function in classes that implement said interface.

## Fields & Properties



## Methods / Functions

### Constructor

### Public

### Internal

### Private

## Helpers

