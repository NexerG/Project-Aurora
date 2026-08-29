---
date: 2026-08-28
tags:
  - d_Filing
  - d_Registry
  - d_UI
cssclasses:
  - Aurora.css
Status: Current
Linker:
  - "[[Asset Registries]]"
Class:
  - "[[UI Document]]"
Parent Class:
  - "[[Asset]]"
Interfaces:
Used by:
  - "[[Vulkan Control]]"
  - "[[UI Rasterizer Module]]"
Type:
  - Public
Attributes:
  - A_XSDType
Namespace: ArctisAurora.Core.Registry.Assets
SourceFile: AuroraEngine/Core/Registry/Assets/UIDocumentAsset.cs
VerifiedAgainst: 2026-08-28
---
## Description

A UI document — the XML file that describes a whole window's control tree — is an asset like a font or a texture, held in the `uiDocuments` registry and fetched by name rather than by filename.

The asset stores a resolved path and nothing else. It deliberately does not hold a parsed tree, because parsing produces live `VulkanControl` entities that register themselves and take rows in the `UIControls` pool, so a cached tree would be a live tree rather than a template, and every window needs one of its own anyway.

Naming rather than pathing is what lets the engine ask for a document the application supplies. `SettingsWindow` is engine code and wants "the settings shell"; only the application knows which file that is, and only the application ships it.

## API summary

| Member | Kind | Summary |
| --- | --- | --- |
| `name` | field | The registry name the document was declared under. |
| `path` | field | Absolute path, resolved through the [[Virtual File System]] at load. |
| `Load(name, source)` | override | Resolve `source` and remember it. Does not read the file. |

## Methods

### Load
```
Load(name, source)
	this.name = name
	path = VirtualFileSystem.ResolveFile(source)
```

## Data / XML

Documents are declared in the ordinary asset manifest under `Data/XML/Assets/`, so they load in `AssetRegistries.PreloadAssets` beside meshes, fonts and textures.

```xml
<AssetManifest xmlns="http://arctisaurora/AuroraAssetRegistryTypes">
  <Asset Type="UIDocumentAsset" Name="main"       Source="XML/Documents/UI.xml"/>
  <Asset Type="UIDocumentAsset" Name="tab-window" Source="XML/Documents/TabWindow.xml"/>
  <Asset Type="UIDocumentAsset" Name="settings"   Source="XML/Documents/Settings.xml"/>
</AssetManifest>
```

The manifest is unioned across mounts with the application winning, so an application overrides an engine document by declaring the same name against its own file — the same override story every other asset already has.

The engine declares one document of its own, `settings`, against the shell `SettingsWindow` needs — a window frame, a title bar and two panels named `Categories` and `Rows`. An application that ships no settings document therefore still has a working settings screen, and one that wants a different shell declares `settings` against its own file and wins.

## Loading and swapping a UI

`VulkanControl.ParseXML(document)` takes the registry name, looks the asset up, and parses the file at its path. The reflection scan that collects every `[A_XSDActionDependency]` in the process used to run once per call; it now runs once per process and is shared by every load.

`UIModule.SetUI(document)` is the swap: it parses the named document and assigns the result to `uiRoot`, and the setter destroys the tree that was there.

```
SetUI(document)
	uiRoot = ParseXML(document)

uiRoot.set(value)
	if uiRoot exists
		uiRoot.Destroy()
	uiRoot = value
	UILayout.InvalidateWindowRanges()
	value.FitTo(window size)
```

A swap has to happen on the main thread, and the natural place is a UI action, because a button's action fires inside `HandleUI` — before `Interpolate` drains the destroy queue and before `ResolveLayout` runs. Calling it from any other thread mutates the control tree while the main thread is measuring it, which fails inside `StackPanelControl.Measure` rather than anywhere that would name the real cause.

There is no swap-back: the outgoing tree is destroyed, not parked. Keeping it would leave it registered and holding pool rows while invisible. A layout that wants to be returned to is reached by swapping to it again, which means the document being left has to carry a control that asks for the one to come back to.

### Authoring a swap

An action bound from XML is a zero-argument `Action`, so it cannot carry the name of the document to show. A host therefore writes one named action per document it can display, alongside its other decorations, and a control names that action the way it names any other.

```
ShowAlt()
	UIActions.Invoking().ui.SetUI("alt")
```

`UIActions.Invoking()` answers which window asked. It takes the context menu's owner when an entry is running, otherwise whatever the collision handler last made active, otherwise whatever is under the pointer, and resolves that control to its `RenderWindow`; a keybind pressed with no tree involved falls back to the primary window. The same helper is what lets `Settings.Open` open the settings screen over the window it was asked from rather than always over the primary.

The cost of naming rather than parameterising is that a swap target is compiled: adding one is a C# edit and a rebuild, not an XML edit. That is the right trade while a document has one or two swaps in it, and the wrong one if a host ever wants them authored per button.

## Related
- [[Asset Registries]] — the `uiDocuments` registry and the manifest load step
- [[Vulkan Control]] — `ParseXML`, and the tree the document builds
- [[Virtual File System]] — how `Source` resolves across mounts
- [[UI Rasterizer Module]] — `UIModule`, which owns the root and draws it
