---
date: 2026-08-22
tags:
  - d_System
  - d_Registry
cssclasses:
  - Aurora.css
Status: Current
Linker:
  - "[[Asset Registries]]"
System:
  - "[[XML-XSD]]"
Class:
  - "[[Context]]"
Parent Class:
Interfaces:
  - "[[IContext]]"
Used by:
  - "[[Periodic]]"
Type:
  - Public
Attributes:
  - A_ActiveContext
  - A_XSDType
Namespace: ArctisAurora.Core.Registry
SourceFile: AuroraEngine/Core/Registry/Context.cs
VerifiedAgainst: 2026-08-22
---
## Description

The engine's answer to "what is the user working on right now". A context is a name holding one live value — what the pointer is over, what is being dragged, which control has focus — kept in the `ActiveContexts` registry so anything can ask without knowing who set it.

There are two kinds, and the difference is only where the value lives.

A **bound** context is a static field or property tagged `[A_ActiveContext(name)]`. `LoadContexts` reflects over every assembly at bootstrap and registers get/set against the member, so the engine keeps its fast direct field and the registry sees the same value. The three the UI runs on — `Hovering`, `Dragging` and `ActiveControl` — are all bound, and all live on [[UI Collision Handling]].

A **declared** context is written in XML and has no member at all. `LoadDeclared` gives it a slot of its own and registers get/set against that. This is how an application adds a context the engine has never heard of, without an engine change.

## Authoring

Declared contexts live in `Data/XML/Documents/Contexts/*.xml`. Every mount contributes its own file and none overrides another, so the engine and each application can declare contexts side by side — unlike [[Gradients]] or [[Context Menu]], where the first mount holding the file wins outright.

```xml
<Contexts>
  <Context Name="ActiveTabViewer" Type="TabView" From="ActiveControl"/>
</Contexts>
```

`Type` is an XSD type name, resolved the same way a registry entry or a document element is, and a subclass of it counts. `Name` is what `Context.Get` is called with.

## Derived contexts

`From` is the interesting attribute. A context naming one is **derived**: whenever the context it follows is set, this one is recomputed as the nearest ancestor of that value which is assignable to its `Type`, counting the value itself.

The point is what happens when the walk finds nothing — the derived context keeps what it had. That stickiness is the whole reason the mechanism exists rather than each caller walking the tree itself, because the interesting cases are exactly the ones where focus has moved somewhere unrelated.

Periodic uses it for the pane a note opens into. Clicking a row in the vault browser makes that row the active control, and the browser lives outside every pane, so a walk from the active control finds no `TabView` at all — while `ActiveTabViewer` still holds the pane the user was last typing in, which is where the note should go.

A context that names no `From` is a plain slot; whoever declared it sets it.

## What it does not do

`Set` does not fire the `IContext` callbacks. A control that wants to be told it gained or lost a context is notified by the code doing the setting, which today means [[UI Collision Handling]] carries its own add/remove pair around each assignment.

`Type` is recorded and never enforced. Nothing checks a value passed to `Set` against the type its context declared.

A name nothing registered is a silent no-op on `Set` and a null on `Get`, so a misspelt context name fails quietly rather than at boot.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `activeContexts` | field | The registry itself, name to entry. |
| `Register(name, type, get, set)` | method | Adds an entry bound to whatever the delegates reach. |
| `Get<T>(name)` | method | The value, or null if unset or of another type. |
| `Set(name, value)` | method | Assigns, then recomputes whatever derives from it. |
| `Clear(name)` | method | `Set` with null. |
| `Forget(value)` | method | Drops a value out of every context holding it, without deriving. |
| `LoadContexts()` | action | Bootstrap step. Registers every `[A_ActiveContext]` member. |
| `LoadDeclared()` | action | Bootstrap step. Registers every context authored in `Contexts/*.xml`. |

## Pseudocode

```
LoadContexts:
	for each static member in every assembly tagged A_ActiveContext
		register it under its name, reading and writing the member itself
```

```
LoadDeclared:
	for each Contexts xml file in every mount
		for each context element
			resolve its Type by XSD name
			if nothing answers to that name
				say so and skip it
			give it a slot of its own and register against that
			if it names a From
				remember it as one of that context's derivations
```

```
Set:
	if nothing is registered under the name
		do nothing at all
	write the value through the entry
	for each context derived from this name
		find the nearest ancestor of the value assignable to its type
		if there is one, and it is not already what that context holds
			set it
```

```
Forget:
	for each entry currently holding this value
		write null straight through it, deriving nothing
```

Related: [[Asset Registries]], [[UI Collision Handling]], [[Bootstrapper]], [[Virtual File System]]
