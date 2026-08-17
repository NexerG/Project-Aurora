# Decision — a setting is its own type, so it is its own element with its own typed attributes

**Date:** 2026-08-15, reshaped 2026-08-17
**Status:** LANDED (2026-08-17). `GraphicsSettings` is the only category; `DocumentSettings` stays a
plain `ISettingsGroup`.
**Scope:** `ArctisAurora.Core.Registry` (`Setting`, `SettingScope`, `SettingCategory`,
`UserSettingsFile`, `SettingsRegistry.Apply/SaveAll/ApplyCategory/CarryCategoryUnknowns`),
`ArctisAurora.EngineWork.Rendering` (`GraphicsSettings` + its four setting types),
`Periodic/Data/XML/Settings/Graphics.xml`, `AuroraEngine/Data/XML/Settings/DocumentSettings.xml`.

## The ask (2026-08-17)

The 2026-08-15 shape was judged a mess and reworked to the user's sketch: a category is a class, its
children are setting objects, each setting is a separate element in the user's XML, and some settings
must be beyond the user's reach even though they sit in the same category as ones that are not
(resolution yes, raytracing-vs-rasterizer no).

## What changed, and why the old shape was the mess

| | 2026-08-15 | 2026-08-17 |
|---|---|---|
| A setting is | one `Setting` class, `Name`/`Value` strings | its own type, its own `[A_XSDType]` |
| In XML | `<Setting Name="Vsync" Value="false"/>` | `<VSync On="false"/>` |
| Declared by | `Declare("Vsync", true, User, "…")` in a ctor | `public readonly VSyncSetting vsync = new …;` |
| Read by | `Get<bool>("Vsync")` + a hand-written typed property | `.vsync.on` |
| In the schema | `Value` is `xs:string` | `On` is `xs:boolean`, `Mode` is the `WindowMode` restriction |
| Multi-value | impossible — `WindowWidth` + `WindowHeight` | `<Window Mode Width Height/>` |

The name used to appear three times (Declare, typed property, XML) and the schema knew none of the
types. Both costs were recorded as accepted on 2026-08-15; this reverses them. The 2026-08-15
decisions that **survive unchanged**: per-name merge, scope declared by code and mounts but never by
the write root, unknowns carried forward, `Apply()` batching, `RequestSwapchainRebuild` setting a
flag rather than doing the work.

## Decisions

### 1. A category's children are its own `Setting`-typed fields

`SettingCategory.settings` reflects `GetType().GetFields(Public | Instance)` on **first use**, not in
a constructor. The base constructor would also work — C# runs derived field initializers before the
base ctor body — but a setting assigned in a derived *constructor body* would then be silently null
and silently skipped. Lazy collection is correct for both.

Declaring a setting is therefore one field and nothing else. No registration call, no `Declare`, and
defaults are field initializers exactly as in a plain group, so a category with no file still
resolves (decision 2 of [[settings-registry]] survives).

Per-setting metadata rides the object initializer:
`new VSyncSetting { onChanged = "Renderer.RequestSwapchainRebuild" }`.

### 2. Setting elements resolve against the category, not `AnyXMLType.FindType`

`ApplyCategory` calls `category.Find(element.Name.LocalName)`. **A setting name only has to be unique
inside its category.** This is what makes one-class-per-setting affordable: `FindType` matches on name
alone across every category, and a flat namespace fills up fast once every knob is a type
(`Resolution`, `Quality`, `Mode`, `Volume`).

Verified in the shipped set: `Window` is also `[A_XSDType("Window", "UI")]` on `WindowControl`, and
`<Window>` under `<Graphics>` resolves to `WindowSetting` regardless. The two complexTypes live in
different target namespaces, so XSD is happy too.

**Category** names still go through `FindType` and still must be globally unique. `Physics` is
**already taken** by `PhysicsSystem` (`Systems` category) — a future physics settings category needs
another name.

### 3. Scope stays runtime state on the setting instance (user, 2026-08-17)

`Setting.scope` and `Setting.onChanged` are `[A_XSDElementProperty]` attributes on the abstract base,
so every concrete setting's complexType carries `Scope` and `OnChanged`. The category's field
initializer is the declaration; a mount manifest may retune it; the write root never can.

`ApplyCategory` implements the last rule by **saving both, applying the element, and restoring them**
when `fromWriteRoot` — cheaper than teaching the shared `XmlReflection.ApplyAttributes` an exclusion
list, which four other readers use unchanged. `WriteDiff` skips any scalar whose `DeclaringType` is
`typeof(Setting)`, so a save never writes them back either.

**Rejected: scope declared on the setting type** (`public override SettingScope scope => App`). It is
static and therefore generator-visible, which is the only thing that could support decision 4's
rejected option — but it kills Periodic's per-host retune, which is in active use
(`<VSync Scope="App"/>` in `Graphics.xml`), and would make `VSync` App-scoped for every host or none.

### 4. "The user must not meddle" is enforced at load, not by the schema (user, 2026-08-17)

One `SettingsTypeSchema.xsd` describes the engine's manifests, the app's, and the user's. An
App-scoped setting is therefore *legal* in a user's file — it has to be, since the engine's own
manifest is where an App value is declared — and the registry reads it, warns, and refuses it.
`Save` never writes one, so it only appears there if typed by hand.

**Rejected: a second `UserSettingsTypeSchema.xsd`** emitting only User-scoped types, so the write root
fails validation on an App setting. Real editor-time feedback, but it forces decision 3's rejected
option (the generator runs before `LoadAll` and cannot see instance state), and adds a
settings-specific path to `XSDGenerator`. The warning was judged enough.

### 5. `Apply()` diffs every member of a setting, not one string

`Setting.applied` is a `Dictionary<MemberInfo, object>` refreshed by `MarkApplied`, which reports
whether *any* value member moved. `Setting.ValueMembers` is `ScalarMembers` minus anything declared on
`Setting` itself, so `Scope` and `OnChanged` never count as a change. A `Window` whose height alone
moved fires its action. `LoadAll` ends by calling `MarkApplied` on everything, so loading is still not
a change and the first `Apply()` still fires nothing.

### 6. One `UserSettings.xml`, and no `Save<T>` (user, 2026-08-17)

`SaveAll` writes every group into one document in the write root, matching the sketch. A group with
no diff and nothing stored is left out rather than written empty.

`Save<T>` and `Save(Type)` are **deleted** — rewriting one group means rewriting the document holding
the others, so a per-group save is a lie. It had no callers. Reading is unchanged: the write root is
still every `*.xml` in it, so a file from the old one-per-group layout is still applied rather than
orphaned. Nothing prunes it (pre-existing).

### 7. `Commit()` is the screen's one call (user, 2026-08-17)

`Commit()` = `Apply()` then `SaveAll()`. The UI fires it — the registry cannot know when a person is
finished, which is what the batch exists for — and it is one call because it was two a screen had to
remember, where forgetting `SaveAll` gives a change that works until the next launch.

Order is Apply-then-Save so a missing write root fails *after* the actions ran: the app is in the
state the user asked for and only persistence failed. Both halves stay public — `Commit` inherits
`SaveAll`'s throw, so a host that never calls `SetWriteRoot` (AuroraEditor does not; only Periodic
does) calls `Apply` directly instead. **Rejected: `Commit` skipping the save when no write root is
set** — that silently turns a missing `SetWriteRoot` in Periodic into settings that never persist.

**What `Commit` does not do is gate the value.** The UI writes the live `Setting`, so the value is in
effect when the widget moves; only the action waits. Consequences, accepted: no Cancel, and a reader
mid-edit sees a half-edited category (only `VSync` is read late enough to notice). **Rejected for
now: a working copy** cloned per category, committed on OK and dropped on Cancel — real Cancel, no
torn reads, same shape as `DocumentEditSession`. Not built because no settings screen is built; this
is the thing to build if a screen ever wants Cancel.

**Rejected: fire-on-write setters.** Loses the batching (five edits, five swapchain rebuilds) and
still needs an explicit save.

Scope does not gate a runtime write either — `App` means the *file* cannot carry the value, not that
code cannot set it. An App-scoped setting assigned in memory fires its action and still never reaches
the user's file. A settings screen is what decides not to offer it.

### 8. The root is `UserSettings`, not `Settings`

`XSDGenerator` emits `<xs:simpleType name="{Category}">` per category that owns an enum, so
`SettingsTypeSchema.xsd` already has a global type named `Settings`. A complexType of the same name is
a duplicate global definition and XSD rejects the **whole file**. Renaming the union emission to
`{Category}Enums` would free it but regenerates every category schema for one word — rejected.
`SettingsManifest` → `UserSettingsFile` (C#) / `UserSettings` (XSD).

### 9. `ISettingChild` is deleted

Decision 5 of the 2026-08-15 version added it because `AllowedChildren = typeof(Setting)` produced an
**empty** `xs:choice` — the generator filters `ty != AllowedChildren` and `Setting` was a concrete
leaf. Now that `Setting` is an abstract base with `IsAbstract = true`, it is the ordinary
`Document`→`Block` / `Window`→`IXMLChild_UI` pattern and the marker is redundant. The generator was
**not** changed; this is the shape it always wanted.

Concrete categories still re-declare `AllowedChildren = typeof(Setting)` themselves —
`GenerateTypesPerCategory` reads the attribute with `inherit: false`, same as every UI container.

## Verified

Running `Periodic` (engine manifest + Periodic's `Graphics.xml` + a seeded write root), harness in
`Periodic.Main`, since removed:

- Regenerated `SettingsTypeSchema.xsd`: `Graphics` offers `Device`/`Monitor`/`Window`/`VSync`;
  `Window` has `Mode` as `types:WindowMode`, `Width`/`Height` as `xs:unsignedInt`; `VSync.On` is
  `xs:boolean`. Every setting carries `Scope` (`types:SettingScope`) and `OnChanged`.
- Field collection: `settings.Count == 4`, in declaration order.
- Write root `<VSync On="false"/>` against Periodic's `<VSync Scope="App"/>` → warns, ignored,
  `vsync.on` stays `True`. `<Window Width="1600" Height="900"/>` and `<Device Name="rtx"/>` are
  User-scoped and applied. `onChanged` survives from the code declaration.
- `<LongGone Keep="me"/>` warns as undeclared at load and is **carried forward whole** by the save.
- `Bloom="true"` (undeclared attribute on a declared `Device`) survives the save.
- `Apply()` with no change prints nothing; after `vsync.on = false` and `window.width = 1440` it
  prints `Running: Renderer.RequestSwapchainRebuild` **once**; a third `Apply()` prints nothing.
- `SaveAll` wrote one `UserSettings.xml`: `Device` and `Window` present, `Mode` omitted (equals the
  tier value), **`VSync` absent** (App-scoped), `DocumentSettings` absent (no diff).
- `Commit()` after two edits fires `Renderer.RequestSwapchainRebuild` **once** and writes the file in
  the same call; a second `Commit()` fires nothing and rewrites the file identically. The edit to
  App-scoped `vsync.on` fired its action and stayed out of the file, as decision 7 says it should.
- Full bootstrap completes, all three threads start, no stderr.

## Still open

- **`Commit()` has no caller in the app** — there is still no settings screen.
- **No Cancel, and no working copy.** Decision 7; the fix is known and deliberately unbuilt.
- **Nothing re-reads a setting after the system consumed it.** `Device`, `Monitor` and `Window` are
  read once during bootstrap; only `VSync` has a live path, because only the swapchain can be rebuilt.
- No range/validation metadata on a setting (min, max, allowed values). Now cheaper than before — it
  would be members on the setting's own type — but still unbuilt.
- `RenderSettings` gained no `IMigratableSettings` on the way to `GraphicsSettings`, so a write-root
  file in the old `<Setting Name="Vsync"/>` shape would warn per element and be carried forward as
  unknown rather than migrated. No write root existed on the dev machine; a shipped build would need
  the migration.
- The `schemaLocation` a saved file gets is a long relative path back to the app's schema folder
  (pre-existing).

Related: [[settings-registry]], [[cross-system-change-notification]], [[xml-save-skips-defaults]]
