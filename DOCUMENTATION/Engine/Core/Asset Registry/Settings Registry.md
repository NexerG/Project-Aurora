---
date: 2026-08-09
tags:
  - d_System
  - d_Registry
  - d_Filing
cssclasses:
  - Aurora.css
Status: Current
Linker:
  - "[[Arctis Aurora]]"
System:
  - "[[SETTINGS]]"
  - "[[XML-XSD]]"
Class:
  - "[[Settings Registry]]"
Parent Class:
Interfaces:
  - "[[ISettingsGroup]]"
Used by:
  - "[[Document Layout Engine]]"
Type:
  - Public
  - Static
Attributes:
Namespace: ArctisAurora.Core.Registry
SourceFile: AuroraEngine/Core/Registry/SettingsRegistry.cs
VerifiedAgainst: 2026-08-09
---
## Description

The engine's **settings system**: one live object per settings group, filled from XML that cascades across mounts and saved back as only what the user changed. A group is an ordinary class marked `[A_XSDType(name, "Settings")]` that implements `ISettingsGroup`, with its values marked `[A_XSDElementProperty]` — the same two attributes every other serialized type in the engine already uses, so declaring settings for a game costs a class and nothing else.

Groups are found by reflection rather than by being listed anywhere, so a group with no file behind it still resolves to its field initializers and still shows up in `Groups` for a settings UI to walk. XML only ever supplies overrides.

See [[Virtual File System]] for the mount ordering this rides on, [[Asset Registries]] for the asset-side manifest it mirrors, and [[XSDGenerator]] for how the schema gets written.

## Declaring a group

```C#
[A_XSDType("Difficulty", "Settings")]
public class DifficultySettings : ISettingsGroup
{
    [A_XSDElementProperty("Permadeath", "Settings")]
    public bool permadeath { get; set; } = false;
}

bool p = SettingsRegistry.Get<DifficultySettings>().permadeath;
```

`ISettingsGroup` carries no attribute of its own — it is the `allowedChildren` target the generator scans when it emits `UserSettings`, the same role `Block` plays for a document. Every group uses the `Settings` category so that one schema and one namespace cover all of them and a single file can hold groups from unrelated systems.

A group that instead needs per-value scope or change notification is a `SettingCategory`, whose children are `Setting` types rather than typed members — see [[SETTINGS]].

XSD type names are a **flat namespace across every category**: `AnyXMLType.FindType` matches on name alone, so a group named `Document` would collide with `RichTextDocument`'s `[A_XSDType("Document", "UI")]`. Check before naming.

Groups are found by reflecting over every loaded assembly, so an **application declares its own** exactly as the engine does and needs no registration step — `Periodic`'s vault folder is a `Vault` setting on a `Periodic` category living in the app project, cascading and saving through the same machinery as `Graphics`.

## The files

Manifests live in `Data/XML/Settings/*.xml`, any number of them, free filenames.

```xml
<UserSettings xmlns="http://arctisaurora/AuroraSettingsTypes"
              xmlns:UI="http://arctisaurora/AuroraUITypes">
  <DocumentSettings>
    <UI:DocumentLayout LineHeight="1.5" BlockSpacing="8">
      <UI:TextStyle Type="Heading1" FontSize="34"/>
    </UI:DocumentLayout>
  </DocumentSettings>
</UserSettings>
```

A group whose type lives in another category — `DocumentLayout` is a `UI` type, because notes embed it — carries that category's prefix on its subtree. Groups declared in the `Settings` category need no prefix.

## Resolution order

Applied lowest priority first, so a higher tier overrides only the attributes it names:

1. every mount's `Data/XML/Settings/*.xml`, walked from the **lowest**-priority mount up (engine, then application), files within a mount in name order
2. the application's **write root**, read last

`EnumerateAll` is not used here: it de-duplicates by file *name*, so an application naming its file the same as the engine's would hide the engine's whole set instead of overriding the values it names. The registry walks `VirtualFileSystem.Mounts` in reverse directly.

Merging is **per attribute** for scalars, and recursive into a nested complex member so it lands on the existing instance rather than replacing it. A `List<>` member is different: the first file in a tier that declares any entry replaces the whole list, so a scheme is read as written instead of being merged entry-by-entry against entries its author cannot see.

## The write root

`SettingsRegistry.SetWriteRoot(path)`, called by the host before `Engine.Init`, names the folder that user settings are read from last and written to. It is deliberately **not** a mount — mounting it would let a stray file there shadow engine assets and not just settings. `Save` throws if it was never set; reads work fine without one.

Periodic uses `%AppData%/Periodic/Settings`.

## Saving

`LoadAll` snapshots every group at the moment the mounts have all been applied and before the write root is read. `SaveAll()` writes one `UserSettings.xml` into the write root holding every group, each carrying **only what differs from that snapshot** — so a user who changed one value pins one value, and keeps receiving engine changes to everything else. A group with nothing to say is left out rather than written empty. A changed list is written whole, its entries still skipping their own type defaults the way [[Document XML]] does.

There is no `Save<T>()`: rewriting one group means rewriting the document that holds the others.

## Settings that change shape between releases

Most of this needs nothing. A group that gains a member gets it from the field initializer, because the instance is constructed before any file is read and a user file that never named it simply says nothing about it; an enum that gains members still parses every name it used to; a whole new group needs no file to exist at all. The releases that *do* break a stored file are the ones where a name or a meaning moved out from under it — a member renamed, an enum member removed, a 0–100 value becoming 0–1 — and none of those can be inferred from the file itself.

A group that expects to change shape implements `IMigratableSettings`: an `int version`, and a `Migrate(int from, XElement stored)` that rewrites the stored element in place before anything on it is read. The rewrite is ordinary XLinq and the group spans its own versions, so there is no migration language to learn and no step registry to keep in sync.

```C#
public void Migrate(int from, XElement stored)
{
    if (from < 2) { rename Vsync to VerticalSync }
    if (from < 3) { divide Sensitivity by 100 }
}
```

A group migrates only when its stored element **carries** a `SettingsVersion` attribute lower than the group's own. Hand-authored engine and application manifests never carry one and are always read as current; `Save` stamps every migratable group, so user files always do. `SettingsVersion` is reserved by the registry and, because [[XSDGenerator]] emits attributes from annotated members only, is the one attribute in a saved user file that the schema does not describe.

### Nothing is deleted on the way through

An attribute in a user's file that no member claims is **carried forward**, not dropped: `Save` starts from the fresh diff and copies back everything unclaimed, on the group element and recursively on nested members. A value survives a rename nobody wrote a migration for, and survives a setting that leaves and comes back. A *known* attribute the writer chose to omit still drops out, so resetting a value back to what the tiers below say still shrinks the file. List entries are not walked — a replaced-wholesale list has no stable identity per entry.

A stored value that no longer converts at all — the enum member that was deleted — warns and leaves the member holding whatever it already had, rather than throwing. That tolerance is asked for by the settings path alone; [[Document XML]] and the asset manifests still throw on a value they cannot read.

## API summary

| Member                    | Kind   | Summary                                                                         |
| ------------------------- | ------ | --------------------------------------------------------------------------------- |
| `Get<T>()`                | static | The live group instance; throws if `T` is not a registered group.               |
| `Groups`                  | static | Every group by type — the enumeration a settings UI walks.                      |
| `SetWriteRoot(path)`      | static | Folder read last and written to. Host calls it before `Engine.Init`.            |
| `LoadAll()`               | static | The `Settings.LoadAll` bootstrap step: scan, then cascade, then snapshot.       |
| `Apply()`                 | static | Fire the `OnChanged` of every setting that moved, each action once.             |
| `SaveAll()`               | static | Write every group's diff into one `UserSettings.xml` in the write root.         |
| `Commit()`                | static | `Apply()` then `SaveAll()` — what a settings screen calls when dismissed.       |

### `IMigratableSettings : ISettingsGroup`
`int version` · `void Migrate(int from, XElement stored)`. Optional — implemented only by groups whose stored shape can go stale.

## Methods

### `LoadAll` — scan, cascade, snapshot
```
clear the groups
for every ISettingsGroup implementer carrying [A_XSDType] in any loaded assembly
    construct one and store it by type
for every mount, lowest priority first
    for every XML/Settings/*.xml in name order
        apply it
snapshot every group          // the baseline a save diffs against
for every *.xml in the write root
    apply it, and keep the element for the save to read back
```

### `Migrate` — only a stamped element is stale
```
if the group does not implement IMigratableSettings, stop
read SettingsVersion off the element; if it is missing, the element is current, stop
if it is not lower than the group's version, stop
hand the element to the group's own Migrate, then restamp it
```

### `ApplyInto` — merge one element onto a live object
```
apply the element's attributes onto the object      // XmlReflection.ApplyAttributes
for every child element
    resolve its type by element name
    if the object has a complex member of that type
        take the existing instance, or make one
        recurse into it                             // merge, not replace
    else if the object has a List<> field of that type
        clear the list the first time this file touches it
        construct an entry, recurse into it, add it
```

### `WriteDiff` — emit what differs
```
for every scalar
    skip it if it equals the snapshot's value
for every complex member
    recurse against the snapshot's nested table, and emit it only if anything came out
for every list
    skip it if every entry still matches the snapshot entry-for-entry
    otherwise write all of it, each entry diffed against a fresh instance of its type
stamp SettingsVersion if the group is migratable
copy back every attribute the stored file carried that no member claims
```

## Related
- [[SETTINGS]] — the system write-up: the cascade end to end, and how a game declares its own
- [[Virtual File System]] — the mount order the cascade walks
- [[Asset Registries]] — the asset manifest this mirrors, and its per-entry override rule
- [[Document Layout Engine]] — `DocumentSettings`, the first consumer
- [[Bootstrapper]] — `Settings.LoadAll` is the first step of the `Bootstrap` phase
