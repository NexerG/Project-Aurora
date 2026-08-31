# Decision — a data XML file names its kind: `[name].[type].xml`

**Date:** 2026-08-28
**Status:** LANDED. Builds clean, Thorium boots all 24 steps, every loader verified to have found
its files. **NOT GUI-verified.**
**Scope:** all 24 files under `*/Data/XML/**`, and the 14 loaders that resolve or enumerate them.

**Merged 2026-08-30.** This landed on one machine while [[ui-document-registry]]'s registry design
landed on another, and the two met on a merge. The registry design won; the `ui` row below is the
part of this that changed. See that file's 2026-08-30 amendment.

## The problem this solved

A file name said what a document was called, never what kind it was. Nothing could tell a UI
document from a settings document from a bootstrap sequence without opening it and reading the root
element. Concretely, that cost:

| Defect | Where |
|---|---|
| A folder had to be single-kind before it could be preloaded | `Documents/UI/` existed only so `UIDocuments` could scan something |
| `Documents/` root holds six unrelated kinds, and nothing there is self-describing | `Bootstrap`, `Shutdown`, `Pools`, `Registry`, `EntityRegistry`, `ContextMenus`, `Gradients` |
| A file in the wrong folder is handed to the wrong parser and dies | `<BootstrapSequence>` into `AnyXMLType.FindType` |

The convention already existed in one place — `arial.import.xml` — and was never generalised.

## The rule

`[name].[type].xml`. The **name** half is the identity a loader keys on and **may not contain a
dot**. The **type** half names the loader that owns the file. Enumerating loaders match
`*.[type].xml`, so a mis-typed file is inert rather than mis-parsed.

| Type | Files | Loader |
|---|---|---|
| `bootstrap` / `shutdown` | `Bootstrap`, `Shutdown` | `Bootstrapper`, `Shutdown` |
| `pools` | `Pools` | `DataManager` |
| `registry` | `Registry` | `AssetRegistries` |
| `entities` | `EntityRegistry` | `EntityRegistry` |
| `menus` | `ContextMenus` ×2 | `ContextMenus` |
| `gradients` | `Gradients` | `Gradients` |
| `contexts` | `Thorium` | `Context` |
| `inputs` | `InputMap` ×2 | `InputHandler` |
| `sampler` | `ControlSampler` | `SamplerAsset` |
| `ui` | `UI` ×2, `Settings` ×2, `TabWindow`, `Alt` | declared in a manifest, loaded by `UIDocumentAsset` |
| `assets` | `EngineAssets`, `EditorAssets`, `ThoriumAssets` | `AssetRegistries.PreloadAssets` |
| `imports` | `EngineFonts` | `AssetImporter` |
| `settings` | `DocumentSettings`, `Graphics`, and the written `UserSettings` | `SettingsRegistry` |
| `import` | font stamps, pre-existing | `AssetImporter` |

## Decisions (user, 2026-08-28)

### 1. The suffix does not replace the kind folders

`ui-document-registry.md` had recorded the opposite — that `[name].[type].xml` would be "what
replaces the folder as the way a file declares its kind". Rejected on building it. The four kind
folders (`UI/`, `Contexts/`, `Inputs/`, `Samplers/`) also carry each kind's **VFS composition
policy**, which is not uniform: `EnumerateAll` for contexts, UI documents and samplers,
`TryResolveFile` for gradients, per-mount `FileExists` for menus. Flattening would make every loader
walk one large folder to filter it and would lose the place that policy is expressed. The suffix's
real payoff is `Documents/` root, which is mixed-kind and now self-describes.

### 2. A UI document is asked for by its name half

`UIDocuments.Instantiate("UI")`, `TearOffDocument="TabWindow"`, `SettingsWindow.document =
"Settings"`. The registry only ever holds `.ui.xml`, so restating the type at every call site is
noise. Same shape as `InputHandler`, which has always keyed keybind groups by the file's base name.

**Rejected: `Instantiate("UI.ui.xml")`.** Fewer moving parts, but every call site then repeats what
the registry already guarantees.

**Superseded 2026-08-30 by [[ui-document-registry]].** A UI document is asked for by the name its
manifest gives it, not by its file name at all — `"main"`, `"tab-window"`, `"settings"` — so the file
name and the call site are now decoupled and the `Source=` attribute carries the suffixed path. The
reasoning above survives only for `InputHandler`, which is `DocName`'s one remaining caller.

### 3. Name halves were not renamed, stutters and all

`Bootstrap.bootstrap.xml`, `Pools.pools.xml`, `Gradients.gradients.xml`, `UI.ui.xml` read badly —
for a singleton document the name *is* the type. The fix is renaming the name half to whoever ships
it (`Engine.bootstrap.xml`, `Thorium.gradients.xml`), which would also compose across mounts. Left
for later deliberately: a convention change plus a rename pass makes every diff two changes and a
typo indistinguishable from a bug.

## Constraints found while building

- **`Path.GetFileNameWithoutExtension` stops being the name.** It yields `InputMap.inputs`, which
  would have renamed the keybind group and broken `Thorium.Main`'s
  `SetActiveKeybindGroup("InputMap")`. New `Paths.DocName(path)` cuts at the **first** dot — which
  is what makes "a name half may not contain a dot" a rule rather than a style note.
- **`Paths.Doc` stays single-argument.** A two-arg `Doc(name, type)` was considered and dropped:
  `DocumentEditorControl` passes a relative *path* (`../../Notes/SampleNote.xml`) through it, not a
  document name.
- **No csproj change was needed.** `AuroraEngine.csproj` globs `Data\**\*.*`; the apps' `Data/XML`
  is not `Content` and is read from source in Debug.
- **Renames went through `git mv`** — content untouched, rename detection preserved, and none of
  this repo's CRLF/BOM files were rewritten.
- **`InputHandler.ParseXML(string)` ignores its argument** — it enumerates the folder. Pre-existing
  dead parameter, left alone; the literal was updated so it does not read as a lie.

## What is verified

A temporary probe in `Thorium.Main` (reverted) confirmed the four loaders that fail *silently* on a
pattern miss actually found their files:

| Probe | Result | Proves |
|---|---|---|
| keybind groups | `[InputMap:19]` | `*.inputs.xml` matched **and** `DocName` strips both extensions |
| menus | `view=True thorium=True` | `*.menus.xml` on both mounts |
| contexts | `7`, `ActiveTabViewer=True` | `*.contexts.xml` |
| vsync scope | `App` | `*.settings.xml` — only `Graphics.settings.xml` sets it |

The rest are proven by the boot itself: `Gradient="titlebar"` resolves and `Gradients.IndexOf`
throws on an unknown name; `UIModule` indexes `["ControlSampler"]` raw; `AssetImporter` warned about
`Electrolize-Regular.ttf`, which it can only have read from `EngineFonts.imports.xml`;
`ParseXML("main")` logged no registry miss and returned a tree; the `Doc()`-resolved singletons would
have thrown `FileNotFoundException` out of `XElement.Load`.

## Open

- **`Data/Notes/*.xml` was left alone.** Thorium's vault is user data, not `Data/XML/**`. A
  `.note.xml` vault would touch `VaultBrowserControl`'s filter, create, rename and duplicate paths
  plus every `Source=` attribute. Its own call, and arguably right — it is what Obsidian's `.md` is.
- **`Schemas/SchemaManifest.xml` was left alone.** `.xsd` already carries its type and
  `SchemaManifest.manifest.xml` is worse than what is there.
- **An existing `%AppData%\Thorium\Settings\UserSettings.xml`** would now be ignored, silently
  reverting saved settings. No such file exists on the dev machine — the write root has never been
  created — so nothing was migrated.
- **Stale file names in prose were swept** — 21 code comments across 12 files, every current-state
  vault page under `DOCUMENTATION/Engine/**`, `Patterns/document-xml-persistence.md`, and
  `Context/log-viewer-plan.md` (whose instructions are for work not yet done, so its paths have to
  resolve). Deliberately **not** rewritten: the dated records under `ClaudeMemory/Decisions/*`, and
  the completed-progress entries in `Context/multi-windowing-plan.md` and
  `Context/thorium-editor-architecture.md` — all were accurate when written.
- **The convention now has a home** — `Engine/Attributes & Conventions.md` carries the rule and the
  type list; `Paths.md` documents `DocName`; `Virtual File System.md` explains why the kind folders
  outlived the naming convention that was meant to replace them.

Related: [[ui-document-registry]], [[declared-contexts]], [[settings-registry]],
[[asset-manifest-and-import]]
