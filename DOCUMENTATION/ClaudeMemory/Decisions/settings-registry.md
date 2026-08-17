# Decision — settings are reflected groups, cascaded per attribute, saved as a diff

**Date:** 2026-08-09
**Status:** LANDED (2026-08-09). `SettingsRegistry` loads as the first `Bootstrap.xml` step;
`DocumentLayout.Defaults` is its first consumer; `DocumentStyles.xml` is gone.
**Scope:** `ArctisAurora.Core.Registry` (`SettingsRegistry`, `ISettingsGroup`, `UserSettingsFile`);
`ArctisAurora.Core.UISystem.Controls.Text.Document` (`DocumentSettings`, `DocumentLayout.Defaults`,
`DocumentXml.LoadLayout` deleted); `AuroraEngine/Data/XML/Settings/`; `Periodic.Main`.

## What was there before

Periodic's styling was the whole "settings system" and it registered nowhere:

| Piece | Where | State |
|-------|-------|-------|
| Declaration | `[A_XSDType]` + `[A_XSDElementProperty]` on `TextStyle`/`DocumentLayout` | generic, worked |
| Schema | `XSDGenerator` → `UITypeSchema.xsd` | generic, worked |
| File | `Data/XML/Documents/DocumentStyles.xml` via `Paths.Doc` | one file, first mount wins |
| Instance | `DocumentLayout.Defaults`, a `??=` static with the filename inlined | **the gap** |
| Use | `FontSizeFor` → `ContentBlock.ApplyLayout` → `TextRun.fontSize` | worked |

Nothing could enumerate settings, nothing was writable, and every future group would have repeated
the static + lazy load + filename literal.

## Decisions

### 1. No new attribute — a marker interface plus the existing category (user, 2026-08-09)

A settings group is a class with `[A_XSDType(name, "Settings")]` implementing `ISettingsGroup`.
The interface carries **no** `[A_XSDType]` of its own, the same way `Block` doesn't: it exists to be
the `allowedChildren` target the generator scans, and to be the reflection filter `LoadAll` uses.

```csharp
[A_XSDType("Rendering", "Settings")]
public class RenderSettings : ISettingsGroup
{
    [A_XSDElementProperty("Vsync", "Settings")] public bool vsync { get; set; } = true;
}
```

That is the whole authoring cost. Read with `SettingsRegistry.Get<RenderSettings>()`.

**Category is `"Settings"` for every group, not per-system** (user choice over the alternative of
`[A_XSDType("RenderSettings", "Rendering")]`). One `SettingsTypeSchema.xsd`, one namespace, so a
single manifest file holds groups from any system without a prefix per system.

### 2. Discovery is the type scan; the manifest only supplies values

`LoadAll` instantiates one object per `ISettingsGroup` implementer found across all loaded
assemblies **before** reading any file, so a group with no XML anywhere still resolves to its field
initializers and still appears in `Groups`. Verified from a harness-declared group in a separate
assembly.

**Rejected: manifest-driven discovery** (the literal read of "same idea as the asset manifest"). An
asset that no manifest names does not exist; a settings group that no file names is just a group at
its defaults. Making the file mandatory would mean shipping a file per group to say nothing, and a
settings UI could not list a group until someone had already overridden it.

### 3. A directory of manifests, unioned bottom-up — not `EnumerateAll`

Files live in `Data/XML/Settings/*.xml`, every file in every mount, applied **lowest priority
first**. (Root renamed `SettingsManifest` → `UserSettings` on 2026-08-17; example updated, the
decision is unchanged.)

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

`VirtualFileSystem.EnumerateAll` is **deliberately not used**: it dedupes by *file name*, so an app
naming its file the same as the engine's would hide the engine's whole set instead of overriding the
values it names — the same trap [[asset-manifest-and-import]] calls out, one granularity finer.
`LoadAll` walks `VirtualFileSystem.Mounts` in reverse instead, sorting each mount's files by name.

The manifest **is** the values file — no `Source` indirection to a second file, matching that
decision's "rejected: manifest entry pointing at a separate per-asset config file".

### 4. Per-attribute merge; lists replace wholesale (user, 2026-08-09)

A tier overrides the attributes it names and inherits the rest, so an app changing `LineHeight`
keeps the engine's `BlockSpacing` and its whole style scheme. A nested complex member merges
recursively onto the existing instance rather than replacing it.

A `List<>` member is replaced entirely by the first file in a tier that declares any entry — the
rule [[text-styling-types]] already fixed for styles, kept so a scheme is read as written rather
than merged entry-by-entry against entries its author cannot see. Merging by key was considered and
skipped: it needs a declared key attribute and nothing wants it yet.

### 5. The application owns the write root (user, 2026-08-09)

`SettingsRegistry.SetWriteRoot(path)` before `Engine.Init`. That folder is read **last** (above
every mount) and is the only thing `Save` writes to. Periodic uses
`%AppData%/Periodic/Settings`; `Save` throws if the root was never set.

Chosen over "writes target the primary mount" ([[asset-manifest-and-import]] decision 7, which
still governs *asset import*): a shipped game writing settings next to the exe is per-machine, not
per-user, and lands in Program Files. Chosen over a fixed `%AppData%/<app>` because the engine does
not know what a host wants — Periodic may eventually want its settings inside the vault.

The write root is **not** a VFS mount. Mounting it would let a stray file there shadow engine
*assets*, not just settings.

### 6. Save writes the diff against the merged mounts

(2026-08-17: still true, but the destination is one `UserSettings.xml` holding every group rather than
one file per group, and `Save<T>` is gone. See [[settings-categories]] decision 6.)

`LoadAll` snapshots every group right after the mounts are applied and before the write root is
read. `Save` walks the live object against that snapshot and emits only what differs — scalars
individually, a changed list whole. List entries are written against a fresh instance of their type,
so `xml-save-skips-defaults` still applies inside them.

This is [[xml-save-skips-defaults]] with the baseline moved: the writer compares against *the tier
below* instead of *a fresh instance*. Consequence, and the point of it — a user who never touched a
value keeps receiving engine changes to it.

### 7. Most "the settings changed shape" cases need no mechanism at all

Established before designing decision 8, and the reason it stayed small:

| Change | What happens | Needs |
|--------|--------------|-------|
| A group gains a member | the instance is constructed from its initializers, tiers apply on top, a user file that never named it leaves it current | nothing |
| An enum gains members | a stored name still parses | nothing |
| A whole new group appears | reflection finds it, no file required | nothing |
| The engine changes a default the user never touched | the user keeps receiving it | nothing (decision 6) |
| **A member is renamed** | `ApplyAttributes` walks *members* looking for attributes, so an unmatched attribute is silently ignored — value lost on load, **erased from the file on the next save** | 8, 9 |
| **An enum member is removed** | `ConvertFromInvariantString` throws, unhandled, at the first bootstrap step — **boot dies** | 10 |
| **Semantics change under a stable name** (0-100 → 0-1) | nothing structural distinguishes old from new | 8 |
| **A member moves between groups** | value lost | 8 |

### 8. Versioning is one interface, and the migration is the group's own C# (user, 2026-08-09)

```csharp
public interface IMigratableSettings : ISettingsGroup
{
    int version { get; }
    void Migrate(int from, XElement stored);
}
```

`Migrate` gets the stored element **before anything on it is read** and rewrites it in place —
rename an attribute, rescale a value, split one into two, move a subtree. The group spans its own
versions (`if (from < 2) … if (from < 3) …`) rather than the registry holding a step list.

A group only migrates when its stored element **carries** `SettingsVersion` and it is lower than
`version`. Shipped engine/app manifests never carry one, so they are always read as current; `Save`
always stamps migratable groups, so user files always have one.

**Rejected: `FormerNames` aliases on `[A_XSDElementProperty]`** (user chose one mechanism over two).
A rename would have been declarative and version-free, but it costs a second concept, and a rename
that needs a version bump anyway is the common case once semantics are involved. Consequence: a
plain rename costs a version bump plus a method body, which is friction — if renames start getting
skipped because of it, aliases are the fix.

**`SettingsVersion` lives on the group element, not the manifest root.** Migration is per group and
"one file per group" is a `Save` convention, not a guarantee — a hand-merged two-group file with a
root version would silently migrate one of them from the wrong number. Cost, accepted: `XSDGenerator`
emits attributes from annotated members only, so `SettingsVersion` is **not in
`SettingsTypeSchema.xsd`** and a validating editor flags it in a saved user file. Fixing it means
teaching the generator about `IMigratableSettings`, which couples it to the settings system.

### 9. Attributes no member claims are carried forward (user, 2026-08-09)

`Save` starts from the fresh diff and copies back every attribute the stored user element carried
that no `ScalarMembers` entry claims — on the group element and recursively on nested complex
members. **"Deleting user settings is a sin"**: a value survives a rename nobody migrated, a branch
switch, or a setting that leaves and comes back.

A *known* attribute the writer chose to omit still drops out, so resetting a value to what the tiers
below say still shrinks the file. Verified both in the same case.

**Not walked: list entries.** Paths inside a replaced-wholesale list do not identify an entry, and
lists are rewritten in full anyway. An unknown attribute on a `TextStyle` is dropped.

Only the write-root tier is stashed (`stored`), and the stash is taken **after** migration — so a
migration that renamed `Vsync` does not resurrect it as an unknown.

### 10. A value that no longer converts warns instead of throwing — settings only (user, 2026-08-09)

`XmlReflection.ApplyAttributes(element, node, tolerant = false)`. The settings path passes `true`:
warn, keep whatever the member already holds, carry on with the rest of the element. Every other
caller — notes, asset manifests, import sets — keeps throwing exactly as before, because document
loading was not in scope.

## Load-bearing details

- **`AnyXMLType.FindType` ignores category.** `RichTextDocument` is already `[A_XSDType("Document",
  "UI")]`, so the group is `DocumentSettings`, not `Document`. XSD names are a flat namespace across
  every category — check for a collision before naming a type.
- `DocumentLayout` **stays in the `UI` category**: notes embed `<DocumentLayout>` and moving it
  would change the namespace of every saved note. So `DocumentSettings` wraps it, and the manifest
  carries a `UI:` prefix on that subtree. Pure-settings groups need no prefix.
- `DocumentLayout.Defaults` is no longer lazy. Anything reading it before the `Settings.LoadAll`
  bootstrap step gets an empty scheme and `fallbackFontSize` 18 — in-app it is the *first* step, so
  only out-of-process tooling can hit this.
- `DocumentXml.LoadLayout` is deleted (its only caller was `Defaults`). `DocumentXml.ParseElement`
  and `SettingsRegistry.ApplyInto` now overlap by ~20 lines; not hoisted, because the settings side
  merges onto an existing instance and the document side constructs, and the document side carries
  the `VulkanControl`/`Entity` `AddChild` case that settings must not have.
- `SettingsTypeSchema.xsd` is generated into the *running app's* schema folder. The engine's
  committed copy is a hand-copy, same as every other schema there.

## Verified

36/36 on a throwaway console harness (deleted), cwd set to Periodic's output so `Paths` mounts
exactly as the app does, with a scratch tier `MountFirst`ed above it.

- Engine tier alone: `lineHeight` 1.5, `blockSpacing` 8, 10 styles, `Text`/`Inherit` → 18,
  `Heading1` → 34, `Heading2` → 28, `Heading6` → 16, `Comment` → 16.
- Per-attribute merge: a tier naming only `LineHeight="2"` leaves `blockSpacing` 8 and all 10 styles.
- List replace: a tier naming two styles yields exactly two; `Heading6` clamps to the last heading;
  an unlisted `Comment` takes `fallbackFontSize`, and `lineHeight` is still inherited.
- Write root read last and above the mounts; mount-tier values it does not name survive.
- Save diff: `LineHeight="1.75"` written, `BlockSpacing` omitted, no `TextStyle` elements.
- Save after a list edit: all 9 remaining entries written, each still skipping its type defaults,
  scalars still omitted.
- A group declared only in the harness assembly with no file anywhere resolves to its initializers,
  and saves a one-attribute diff.

Migration and preservation, against a v3 `HarnessRender` whose v1 called `VerticalSync` "Vsync" and
whose v2 stored `Sensitivity` on 0-100:

- A v3 file predating the `Msaa` member leaves it at 4, and leaves untouched members at theirs.
- `SettingsVersion="1"` + `Vsync="false"` → `verticalSync` false. `="2"` + `Sensitivity="80"` → 0.8.
- A file already at 3 is not rescaled; an unstamped *mount* file is read as current, not migrated.
- `Save` stamps `SettingsVersion="3"` on the migratable group and nothing on the plain one.
- `Bloom="true"` (no member) survives a save that rewrites `Msaa`; `Kerning="tight"` survives on the
  nested `DocumentLayout`; and in the same file, resetting `Msaa` to the tier value drops it while
  `Bloom` stays.
- `Quality="Ultra"` (removed enum member) warns, keeps `Medium`, and the rest of the element still
  applies — where it previously would have killed bootstrap.

Boot verified: `Settings.LoadAll` runs first, no warnings, full bootstrap completes and all three
threads start. **GUI not verified** — headings rendering at scheme sizes is a manual check.

## The engine's own group — `Graphics` (2026-08-15, reshaped 2026-08-17)

`GraphicsSettings` in `ArctisAurora.EngineWork.Rendering`, `[A_XSDType("Graphics", "Settings")]`. Four
settings, four consumers, all read once during bootstrap:

| Setting | Members | Consumer | Default |
|---------|---------|----------|---------|
| `Device` | `Name` (string) | `Renderer.ChoosePhysicalDevice` | `""` → first enumerated device |
| `Monitor` | `Name` (string) | `AGlfwWindow.PickMonitor` | `""` → primary monitor |
| `Window` | `Mode` (`Windowed`/`Borderless`/`Fullscreen`), `Width`, `Height` (uint) | `Engine.InitWindowing`, `AGlfwWindow.CreateWindow`/`CenterOn` | `Windowed`, `1280`, `720` |
| `VSync` | `On` (bool) | `AVulkanHelper.GetPresentMode` | `true` |

**Reshaped 2026-08-17:** was `RenderSettings` / `[A_XSDType("Rendering")]` with `Name`/`Value` string
settings; each setting is now its own type carrying its own typed attributes, and `Window` merges what
used to be `Mode` + `WindowWidth` + `WindowHeight`. Call sites read `.window.width`, `.vsync.on`,
`.device.name`, `.monitor.name`. See [[settings-categories]].

- **The ordering worry in the Phase B roadmap item was already solved.** `Settings.LoadAll` is step 1
  of `Bootstrap.xml`; `Engine.InitWindowing` is step 6 and `Renderer.Initialize` step 12, and the
  `ThreadedSystem`s are constructed after `RunPhase` returns. No sequencing work was needed.
- **Device identity is a name substring** (user, 2026-08-15), matched `OrdinalIgnoreCase` against
  `VkPhysicalDeviceProperties.deviceName`. Rejected: `vendorID:deviceID`, exact and rename-proof but
  meaningless to anyone hand-editing the XML; and an enumeration index, which silently means a
  different GPU when the driver reorders. A non-matching string **warns and keeps `devices[0]`** —
  an unplugged eGPU or a laptop's switched GPU must not stop the app booting.
- **Defaults change no behaviour.** `Vsync=true` asks for `MailboxKhr` and falls back to `FifoKhr`,
  which is exactly what `GetPresentMode` did unconditionally before. `Vsync=false` asks for
  `ImmediateKhr`. `Fifo` stays the fallback because it is the only mode the spec guarantees.
- **Borderless needs no GLFW hint.** The window is created `Decorated=false` in every mode already,
  so borderless is a plain window at the primary monitor's video mode size and position, and
  fullscreen is that video mode with the monitor handed to `CreateWindow`.
- `CreateWindow` now ends with `UpdateWindowSize(ref windowSize)` for **all** modes — the swapchain
  reads `Engine.window.windowSize` as its `ImageExtent`, and in fullscreen the requested size is not
  the granted one. Side effect on the windowed path: the extent is now the real framebuffer size
  rather than the requested 1280x720, which is the correct value on a scaled display.
- **`PreferredMonitor` is the EDID panel name, resolved through Win32** (user, 2026-08-15, after a
  name-substring version was built and failed on the machine). `glfwGetMonitorName` returns the
  *driver* description: on this two-monitor machine (MSI G24C6 + DELL U2414H) both report
  **"Generic PnP Monitor"**, so GLFW's own name cannot distinguish monitors at all on Windows.
  Silk.NET does not bind `glfwGetWin32Monitor` — checked, the symbol is absent from
  `Silk.NET.GLFW.dll` — so there is no direct handle→display mapping either.
  `DisplayNames.At(x, y)` in `ArctisAurora.EngineWork.Rendering` joins the two sides by
  **virtual-desktop position**, which is unique per display because Windows forbids two displays
  sharing an origin: `EnumDisplayDevices` (attached adapters) → `EnumDisplaySettings` (position) →
  the adapter's monitor child with `EDD_GET_DEVICE_INTERFACE_NAME` → `DisplayMonitor
  .FromInterfaceIdAsync().DisplayName`. Falls back to the PnP hardware id, then to the GLFW name.
  - Rejected: **an index** — distinguishes identical panels and is one mechanism, but is meaningless
    in a file and silently means another monitor once displays are re-plugged. Rejected: **index or
    name in one field** — two meanings in one attribute. Rejected: **`GetMonitorPos` coordinates as
    the setting value** — cross-platform and unambiguous, but unreadable and invalidated by dragging
    displays around.
  - Cost, accepted: this is the **only Windows-only code in the settings path**, and it is a
    P/Invoke pair plus a WinRT call. It is confined to `DisplayNames`; a port replaces that file.
  - `Windowed` ignores it — a plain window is placed by the window manager, and positioning it was
    not asked for.
  - The monitor a *fullscreen* window lands on is unrelated to "the renderer breaks the second
    monitor", still open in the roadmap.
- ~~**Not built: resolution.**~~ Built 2026-08-15 as `WindowWidth`/`WindowHeight` (User, 1280x720).
  `Engine.width`/`height` are deleted — `InitWindowing` reads the settings instead, and they are the
  size `Windowed` opens at and the size `CenterOn` centres. `Borderless`/`Fullscreen` ignore them
  and take the monitor's video mode, as they always did.
  - **No `OnChanged`.** A live resize means `glfwSetWindowSize`, and GLFW window calls have to run on
    the thread that created the window, while `Apply()` runs on whoever called it. Deferring to main
    needs a mechanism that does not exist yet — the swapchain action dodged this by setting a flag
    the render thread already polls, and there is no equivalent for the window.
- **Not built: a threading group** (user, 2026-08-15). The roadmap says "CPU/thread counts", but
  there is nothing to count — the three systems are fixed subclasses and `jobSystem` is commented
  out. The knobs that do exist and stay hardcoded are `MainSystem.TargetPeriodMs` (1000/120),
  `PhysicsSystem.TargetPeriodMs` (32.0) and `BuildLanes(commandCapacity: 1024, arenaBytes: 64KB)`.
  Revisit when a job system gives the setting a meaning.
- `SettingsTypeSchema.xsd` regenerates with `Rendering` and a `WindowMode` enumeration. The
  generator also emits a `<xs:simpleType name="Settings">` union now that the category owns an enum
  — pre-existing per-category behaviour, first visible here.

## Still open

- ~~**No change notification.**~~ Built 2026-08-15 for categories only — a `SettingCategory` holds
  `Setting` objects carrying `Scope` and `OnChanged`, and `SettingsRegistry.Apply()` fires the
  actions of everything that moved. A plain `ISettingsGroup` like `DocumentSettings` still has no
  notification. See [[settings-categories]].
- **No reload.** `LoadAll` is idempotent and safe to call again, but nothing calls it twice and the
  live object identity would survive while its contents changed under any holder.
- `FontSizeFor` falls back to the `fallbackFontSize` const (18), not to the scheme's `Text` entry —
  so a scheme with `Text` at 11 gives an unlisted `Comment` 18, not 11. Pre-existing;
  [[text-styling-types]]'s prose says "body text", the code says 18.
- ~~The Phase B engine settings group (GPU device selection, thread counts) is unbuilt.~~ Rendering
  built 2026-08-15, threading deliberately not — see the section above.
- `SettingsVersion` is not in the generated schema (decision 8). A saved user file validates except
  for that one attribute.
- A group **renamed or deleted** outright still only warns and skips — the user's whole file for it
  is orphaned, and decision 9 does not reach across group boundaries. `Migrate` cannot help either,
  since it is dispatched off a group that no longer resolves.
- Nothing prunes the write root. A group's file lingers after the group is gone.

## Rejected

- **A new `[A_Settings]` attribute** (user). `[A_XSDType]` with the `Settings` category plus the
  marker interface carries everything.
- **Settings in `AssetRegistries`.** `library` is keyed by value `Type`, `AddLibraryEntry` returns
  early on a duplicate, and `Object` is already taken by `ActiveContexts` — a second `Object`-valued
  dictionary would silently not register. Settings are not assets; a dedicated static is smaller.
- **One `Settings.xml` per mount.** Same filename-dedupe trap as decision 3.
- **Per-attribute list merge**, **hot reload**, **an `isSet` flag per member.** The last one is
  ruled out standing by [[xml-save-skips-defaults]].

Related: [[asset-manifest-and-import]], [[xml-save-skips-defaults]], [[text-styling-types]],
[[cross-system-change-notification]]
