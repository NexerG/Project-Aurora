# Engine — Context File
**Language:** C# | **Libs:** Silk.NET.Vulkan, Silk.NET.GLFW

## Architecture Overview
Mixed OOP + ECS. ECS drives runtime simulation; OOP used for engine services and tooling boundaries.
ECS design is still being settled — avoid refactoring the entity/component model without asking first.

## Systems & Status
| System | Status | Notes |
|--------|--------|-------|
| Rendering | ✅ Stable | Full Vulkan pipeline rendering UI. Needs CPU-side logic work. |
| Asset Registry | ✅ Stable | Dual lookup: GUID handles + path/string |
| ECS | ✅ Stable | Architecture TBD — do not assume archetype or sparse-set |
| XSD / Data Layer | ✅ Stable | XSD schema generation for serialized data |
| Filing | 🔧 In progress | File I/O utilities |
| Threading | 🔧 In progress | Basic threading, design not finalised |

## Engine Loop & Threading — Key Facts
- **3 threads:** main thread (engine tick), physics thread, render thread
- Threads are synchronised with `AutoResetEvent` pairs — not locks or mutexes:
  - `t_physics_start` / `t_physics_end` — main signals physics, waits for it to finish
  - `t_render_start` / `t_render_end` — main waits for render to finish, then signals it
- **Main thread tick order:**
  1. `PollEvents` → `ActivateKeybinds` → `HandleUI`
  2. Signal physics → wait for physics done
  3. `Interpolate()` — entity lifecycle + `OnTick()` + dirty entity updates
  4. Wait for render done → signal render
- **`Interpolate()`** is where entity logic runs — not a physics/render thread concern:
  - Drains `onStartEntities` and `onDestroyEntities` queues each tick
  - Calls `OnTick()` on all entities
  - Processes `entitiesToUpdate` (dirty list) under a lock, then calls `renderer.UpdateModules()`
- **Physics thread** is mostly a stub — 32ms sleep, placeholder for future work
- **Bootstrap is two-stage** via `Bootstrapper`:
  - `PreGPUAPI` — registries, serializable types
  - `PostGPUAPI` — default assets, pipelines, descriptors, sync objects
- `inputHandler` is bootstrapped in PreGPUAPI via InputHandler.Bootstrap()
- `doubleClickTime = 250ms` is a global engine constant
- **Do not** move entity logic into the render or physics threads
- **Physics thread design is unsettled** — don't suggest physics system changes without asking

## Input System — Key Facts
- `InputHandler` is a singleton (`InputHandler.instance`) bootstrapped at `PreGPUAPI`
  via `InputHandler.Bootstrap()` — this is also where `Engine.inputHandler` is assigned,
  so it is never null by the time `Init()` runs
- **Keybinds are XML-driven** — loaded from all `*.xml` files in `Paths.XMLDOCUMENTS_INPUTS`;
  actions are resolved by scanning `[A_XSDActionDependency]` methods across all assemblies
  via reflection and bound as `Action` delegates
- **Keybind groups** — XML files map to named groups (`keybindGroups`); swap active group
  with `SetActiveKeybindGroup(string)` e.g. per game mode or editor context
- **Double-buffered input queues** — GLFW callbacks write to `inputWriteQueue` /
  `keysDownWrite`; each tick `ActivateKeybinds()` swaps read/write queues under a lock,
  then drains the read queue. Same pattern for `charInputWriteQueue` / `charInputReadQueue`
- **Three input streams handled separately:**
  - Keyboard → `ProcessKeyboard()` → `inputWriteQueue`
  - Mouse buttons → `ProcessMouseClick()` → `inputWriteQueue` (mouse buttons are `Keys` too)
  - Character input → `ProcessCharInput()` → `charInputWriteQueue` (raw Unicode codepoints)
- **`Keys.AnySymbol`** — special wildcard key; any character key press also fires keybinds
  registered to `AnySymbol`, used for text input listeners
- **`ICharacterInput`** interface — implement for widgets/controls that need raw char input
- **Held key repeat** — `repeatDelay = 0.35s` before repeat starts, `repeatRate = 0.01s`
  between repeats; tracked per-keybind via `repeatWatch` / `isRepeating`
- Mouse and keyboard share the `Keys` enum — mouse buttons are `MouseLeft`, `MouseRight` etc.
- **Do not** add new input processing outside `InputHandler` — all input flows through here

## Bootstrapper — Key Facts
- `Bootstrapper.Bootstrap(stage)` reflects over all loaded assemblies and invokes every
  `public static` method tagged with `[A_BootstrapStage(stage)]` for the given stage
- `[A_BootstrapStage]` is `AllowMultiple = true` — one method can register for multiple
  stages (e.g. `AssetRegistries.Bootstrap` runs at both `PreGPUAPI` and `PostGPUAPI`)
- `IBootstrap` interface marks a class as having a `Bootstrap(BootstrapStage? stage)`
  method — but the bootstrapper doesn't use the interface directly, it finds methods
  purely by attribute, so the interface is a convention signal only
- Current stages: `PreGPUAPI`, `PostGPUAPI`; two physics stages exist but are
  marked `NOTIMPLEMENTED`
- Execution order within a stage is **undefined** — reflection order, not declaration order

## Bootstrapper — Planned Rework
- Goal: bootstrap order and configuration driven by XSD/XML, not hardcoded stage enums
- Methods to be bootstrapped will still be marked by an attribute
- Sequencing and dependencies will be declared in XML and executed via reflection
- **Do not** suggest adding new `BootstrapStage` enum values — the enum is being replaced
- **Do not** assume current stage ordering is intentional — it is a temporary design

## Rendering — Key Facts
The full Vulkan pipeline is working and rendering UI:
- Instance, device, swapchain, render passes, pipelines, shader loading — all present
- **Current pain point:** CPU-side widget logic — positioning, parenting, children, scaling
- Do NOT redesign the Vulkan pipeline; focus help on the scene/widget graph layer above it

## Asset Registry — Key Facts
- `AssetRegistries` is a **type-indexed library of typed dictionaries** — each registry
  is a `Dictionary<TKey, TValue>` stored in two parallel lookups:
  - `library`: keyed by **value Type** (e.g. `typeof(AVulkanMesh)`)
  - `libraryByName`: keyed by **string name** (e.g. `"Meshes"`)
- Registry definitions are **XML-driven** — `Registry.xml` declares each dictionary's
  name, key type, and value type; parsed at bootstrap via `ParseXML()`
- Type resolution uses `AnyXMLType.typeMap` with fallback to `AnyXMLType.FindType()`
  for engine-specific types
- Retrieval API: `GetAsset<T>(name)`, `GetRegistryByValueType<K,V>()`,
  `GetRegistryByName<K,V>()`, `GetRegistryByKeyType<K,V>()`
- Bootstrap is **two-stage**: `PreGPUAPI` parses XML + registers serializable types;
  `PostGPUAPI` loads default assets (meshes, fonts, textures, styles)
- Assets derive from abstract `Asset` with `LoadAsset()` and `LoadDefault()`
- Serializable types are auto-discovered via `[Serializable]` attribute + reflection,
  stored as `Dictionary<uint, Type>` with hashed IDs
- **Do not** assume direct field-based asset access — always go through the registry API

## ECS — Key Facts
- Architecture is not finalised — ask before assuming storage strategy
- Components and entities exist; query/iteration pattern TBD

## XSD / Data Layer — Key Facts
- `XSDGenerator` reflects over all loaded assemblies at runtime and emits `.xsd` schema
  files to `Paths.XMLSCHEMAS` — fully automatic, no manual schema authoring
- **Three attribute types drive everything:**
  - `[A_XSDType(name, category)]` — marks a class/struct/enum/interface as an XSD type;
    supports `AllowedChildren`, `MinChildren`, `MaxChildren` for element constraints
  - `[A_XSDElementProperty(name, category)]` — marks a field or property as an XSD
    attribute (scalars) or element (collections/lists)
  - `[A_XSDActionDependency(name, category)]` — marks a static/instance method as a
    callable action reference in XML
- **Output files** generated per run:
  - `{Category}TypeSchema.xsd` — one per category, contains complex + enum types
  - `AllTypesSchema.xsd` — union of all known type names across all categories
  - `actionSchema.xsd` — all `[A_XSDActionDependency]` methods as string enumerations
- **Type resolution** uses two maps:
  - `MemberMap` — C# primitives → `xs:*` names (used when generating member types)
  - `typeMap` — reflection-built map of `[A_XSDType]`-annotated types → their XSD names
  - `AnyXMLType.typeMap` — inverse map used at XML parse time to resolve strings → `Type`
- **`AnyXMLType.FindType(string)`** resolves an XSD type name string back to a C# `Type`
  by scanning `[A_XSDType]` attributes across all assemblies — used by `AssetRegistries`
  during `Registry.xml` parsing
- **Do not** hand-edit generated `.xsd` files — they are overwritten on each run
- **Do not** add new primitive mappings to `MemberMap`/`AnyXMLType.typeMap` separately —
  they must be kept in sync manually (both maps exist, one for each direction)
  
## What Claude Should NOT Do
- Don't refactor the Vulkan pipeline internals
- Don't assume a specific ECS storage model
- Don't replace XSD with another serialization approach unprompted
- Don't introduce new NuGet dependencies without flagging it first

## Current TODO
- [ ] UI widget parenting & children logic
- [ ] Widget scaling / anchoring
- [ ] Positioning system (relative, absolute?)
- [ ] Threading design
- [ ] ECS query/iteration API