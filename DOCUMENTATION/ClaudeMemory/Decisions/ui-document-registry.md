# Decision — a UI document is a registry asset, and assigning a window's root destroys the old one

**Date:** 2026-08-28
**Scope:** `ArctisAurora.Core.Registry.Assets` (new `UIDocumentAsset`),
`ArctisAurora.Core.UISystem.Controls` (`VulkanControl.ParseXML`),
`ArctisAurora.EngineWork.Rendering.Modules` (`UIModule`),
`ArctisAurora.Core.UISystem` (`SettingsWindow`), `AuroraPeriodic.Periodic`, `AuroraEditor.Editor`,
`Registry.xml`, new `Periodic/Data/XML/Assets/PeriodicAssets.xml` and
`AuroraEditor/Data/XML/Assets/EditorAssets.xml`

## What changed

- New `UIDocumentAsset : AbstractAsset`, `[A_XSDType("UIDocumentAsset", "AssetRegistry")]`. `Load` resolves
  the manifest's `Source` through the VFS and stores the path. It **does not parse** — one document builds a
  tree per window, so parsing stays at the use site.
- `Registry.xml` declares `<Dictionary Name="uiDocuments" KeyType="xs:string" ValueType="UIDocumentAsset"/>`.
- Documents are declared in the ordinary asset manifest, so they load in `AssetRegistries.PreloadAssets`
  alongside fonts and textures. Periodic declares `main`, `tab-window`, `settings`; the editor declares `main`.
- `VulkanControl.ParseXML(document)` takes a **registry name**, not a filename — `GetAsset<UIDocumentAsset>`
  then `XDocument.Load(asset.path)`. `Paths.Doc` is gone from this path.
- The assembly-wide `[A_XSDActionDependency]` reflection scan was rebuilt on **every** `ParseXML` call. It is
  now a `??=` static, `TaggedActions`. Measured: one scan across a boot plus two full UI swaps.
- `UIModule.uiRoot`'s setter calls `_uiRoot?.Destroy()` before assigning, and `UIModule.SetUI(document)` is
  the swap verb.
- Call sites carry names now: `Periodic`/`Editor` → `"main"`, `SettingsWindow.document` → `"settings"`,
  and `TearOffDocument` in `UI.xml` (×2) and `TabWindow.xml` → `"tab-window"`.

## Why these choices

**A document is an asset, so it goes in the asset manifest rather than in a registry of its own.**
The manifest already unions across VFS mounts app-first, so an application overrides an engine document by
declaring the same name — the same override story fonts and textures already have. A bespoke `UITrees.xml`
would have duplicated `PreloadAssets` and got its own override rules wrong.

**The engine names a role; the application binds it to a file.**
`SettingsWindow` is engine code that hardcoded `"Settings.xml"`, and `Settings.xml` exists only in Periodic —
so the editor's settings screen threw `FileNotFoundException` from a path that was never there. Naming
`"settings"` makes that a registry miss the app is responsible for declaring. (Still broken in the editor —
see gaps.)

**`Load` resolves but does not parse.**
Caching a parsed tree would be wrong twice: `RecursiveParse` builds live `VulkanControl` entities that
register themselves in the `Controls` group and take pool rows, so a cached tree is a live tree, and a second
window would need a copy anyway. Path resolution is the only part that is genuinely per-document.

**The reflection scan is per process, not per load.**
It walks every type in every loaded assembly. Nothing can add an assembly between two UI loads in this engine,
and the scan already assumed that by running after `Engine.Init`.

**Destroy on assignment, rather than parking the old tree for a swap-back.**
`Entity.Destroy` already enqueues the subtree leaves-first and `ProcessDestroys` drains it in the same tick,
so teardown is one call and costs nothing new. Parking a tree would leave it in the `Controls` group and
holding `UIControls` rows while invisible, and nothing asks for swap-back yet. Rejected on that basis
(user, 2026-08-28).

**The swap must be called from the main thread, and in practice that means from a UI action.**
`MainTick` order is `HandleUI` → `Interpolate` (`ProcessDestroys` → `OnTick` → `ResolveLayout`) → `FrameEdge`.
A button action fires inside `HandleUI`, so the outgoing tree is drained and the incoming one laid out in the
same tick. A probe that called `SetUI` from a background thread instead killed the main thread inside
`StackPanelControl.Measure` with `Collection was modified` — that was the probe, not the swap, and it is worth
remembering as the shape of every "swap from off-thread" bug.

## Amendment 2026-08-29 — a swap target is a decoration action, and the engine ships a default settings shell

**Scope, added:** `ArctisAurora.Core.UISystem.Actions` (`UIActions.Invoking`),
`ArctisAurora.Core.UISystem` (`SettingsWindow.Open()`), `AuroraEditor.EditorProgram.UIFunctions.Decorations`,
`Periodic.Editor.Decorations`, `EngineAssets.xml`, new `AuroraEngine/Data/XML/Documents/Settings.xml`,
new `AuroraEditor/Data/XML/Documents/Alt.xml`

### What changed

- The engine declares `<Asset Type="UIDocumentAsset" Name="settings" .../>` in `EngineAssets.xml` against its
  own `Settings.xml`, a copy of the shell Periodic already had. The app-first union means Periodic's identical
  declaration still wins; the editor, which declares none, now gets the engine's.
- `SettingsWindow` gained a zero-argument `Open()` tagged `Settings.Open`, resolving its source window through
  the new helper. `Periodic.Editor.Decorations.OpenSettings`, which hardcoded `Engine.primary`, is deleted.
- New `UIActions.Invoking()` → `ContextMenus.invoker ?? UICollisionHandling.activeControl ??
  UICollisionHandling.hovering`, through `RenderWindow.Of`, falling back to `Engine.primary`.
- The editor's `Decorations` gained `UI.ShowAlt` and `UI.ShowMain`, each one line of
  `UIActions.Invoking().ui.SetUI(name)`, plus an `Alt.xml` and its `alt` manifest entry to swap between.

### Why these choices

**A swap target is a named zero-argument action the host writes, not a parameter (user, 2026-08-29).**
Two ways to let XML name the document were on the table and both were rejected. A `uiDocument` string property
on `VulkanControl` would put a field on every control — glyphs included, at the 57k ceiling — for something one
control in a document uses. An inline argument, `onRelease="UI.SwapTo(settings)"`, generalises to any future
parameterized action, but `XSDGenerator` emits an action attribute as a strict `xs:enumeration` of names; it
would have had to become a pattern or loosen to `xs:string`, and a typo'd action name would stop being a schema
error. The cost of the decision is that a swap target is compiled: adding one is a C# edit and a rebuild.

**`Invoking()` exists because the alternative is hardcoding `Engine.primary` again.**
It has two callers the day it lands, and the second of them — `Settings.Open` — exists specifically to delete a
hardcoded `Engine.primary`. Writing a new action that hardcodes it in the same change would have put the defect
straight back. Same walk `ViewActions.Acting` already does, stopped at the window instead of a `TabViewControl`.

**The engine ships the settings shell as a default asset, rather than each host copying it.**
`SettingsWindow` requires two named panels, `Categories` and `Rows`, so the shell is really the engine's
contract and the file carried nothing Periodic-specific. Declaring it in `EngineAssets.xml` is the same thing
the engine already does for `default`, `invisible` and the fonts, and the manifest's app-first union keeps the
override free.

## Known gaps

- **A self-assignment destroys the live tree.** `uiRoot = uiRoot` would `Destroy()` the tree it is about to
  keep. No call site does it and `SetUI` structurally cannot, so no guard was added — but the setter is public.
- **A swap target cannot be authored in XML** — it is a compiled action per document. Fine at one or two per
  host; the inline-argument form above is what to build if a host ever wants swaps authored per button.
- **Periodic's `Settings.xml` is now a byte-identical duplicate** of the engine's, kept rather than deleted. It
  shadows the engine copy, so Periodic never exercises the fallback.
- **`GetAsset<T>`'s miss path warns "falling back to default" and then throws**, because `uiDocuments` has no
  `"default"` entry. Pre-existing behaviour of the shared accessor; the warning names the missing document, so
  the log is still legible.
- **Tree tops are still not registered** — `Controls` holds every control, not the roots. That is the separate
  WIP item, deliberately not done here. See [[render-window-owns-the-swapchain]] for the three callers waiting
  on it.
- No hot-reload: nothing re-reads a document when the file on disk changes.
- Release builds copy no `Data` XML at all, so the new manifests are Debug-only like every other document.
  Pre-existing.

Related: [[asset-manifest-and-import]], [[render-window-owns-the-swapchain]], [[entity-lifecycle-queues]],
[[settings-categories]]
