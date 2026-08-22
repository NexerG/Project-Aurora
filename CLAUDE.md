# Arctis Aurora Engine — Solution Briefing
**Stack:** C#, .NET | Silk.NET.Vulkan, Silk.NET.GLFW | Visual Studio 2026 | GitHub

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Plan, Then Wait

**Analyze, write the plan, stop. Don't start until I say go.**

Every change to the repo runs four steps, in order:

1. **Analyze** - read the code that's actually involved. No plan built on a guess about what a file
   contains.
2. **Write the plan** - files touched and what changes in each, anything *new* (file, type, signature,
   dependency), what's deliberately left out, and the verification per step (§5's format). Forks go
   here as forks, not as a choice already made (§1).
3. **Ask. Then stop.** The plan is the whole turn. Not a plan followed by the diff in the same breath.
4. **Go, or don't.** Approved: build exactly that. Rejected: stop and re-plan - never build a rejected
   plan in reduced form.

A small change gets a small plan - one line is fine. It doesn't get to skip the gate.

Reading, searching, building and answering questions aren't changes. Writing to a file is.

The plan I approve is the "agreed plan" §8 protects: once you're coding, departing from it stops and
asks again.

The test: about to write to a file, and you can't point at the message where I said go? You've broken this.

## 3. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 4. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 5. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

## 6. Comments In Code

**Rationale lives in the docs. Source files carry names, not explanations.**

- **No "why" in code.** Tradeoffs, rejected alternatives, budgets, consequences, design reasoning -
  all of it goes to `DOCUMENTATION/ClaudeMemory/*` and `DOCUMENTATION/Engine*`. Never above a method.
- **Variables: comment the group, never the member.** One short label over a block of related fields.
- **Methods: one very short descriptor, or nothing.** Not a sentence about context or consequences.
- **Inside a method: only hard loops and genuinely long methods**, to label sections. A straightforward
  ~20-line method gets nothing inside it.
- Don't re-comment code you're editing. If a comment must change, it doesn't get longer.

```csharp
// WRONG - one per field, and a rationale paragraph over the method
public float x;          // horizontal position
public float top;        // top edge
public float height;     // line height

// Re-measures one block and slides everything below it - the typing path, where a keystroke
// changes one paragraph and leaves every other block's lines as they were. The shift is a loop
// rather than a Fenwick tree because a document is a few hundred blocks and the measurement
// that just ran costs more than the additions do.
public void InvalidateBlock(int index)

// RIGHT - one label for the group, one short descriptor for the method
// caret geometry in document space
public float x;
public float top;
public float height;

// Re-measures one block and shifts the tops below it.
public void InvalidateBlock(int index)
```

The test: if the comment explains *why*, it belongs in ClaudeMemory, not the file.

## 7. Answering Back

**Terse plus caveats. No recap.**

- Default reply: a line or two of what changed, then genuine caveats as bullets. Nothing else.
- **Never** restate the plan, walk through code that's already in the diff, explain what the change
  "unlocks", or append next steps that weren't asked for.
- Caveats are only things that bite: assumptions made, signatures changed, scope left out, things
  that will now break. No caveats means no caveats section - don't invent one.
- **Architecture is terse by default too.** Full reasoning when asked "why", when asked for options,
  or in plan mode. Don't volunteer the deep dive.

## 8. Mid-Implementation Changes

**Any departure from the agreed plan stops and asks. No exceptions.**

- If implementing reveals the plan doesn't work, stop. State what broke, give the options, wait.
- This includes small departures: a different data structure, an extra parameter, a changed return
  type, a new file, renaming something already named, an edge case handled a way not discussed.
- Never "just make it work" and report it afterwards. A round-trip is cheaper than drift I don't notice.
- Do finish every part that doesn't depend on the answer before asking, so the round-trip costs one
  message and not the whole task.

## 9. Git Commits

**Subject plus one-line bullets. Nothing else.**

- Imperative subject, sentence case, no period, ~50-70 chars, no `feat:`/`fix:` prefix.
- Blank line, then one-line bullets of *what changed* - as many as the commit needs, each fitting a
  single unwrapped line. A small commit is subject-only.
- No opening prose paragraph, no `Key changes:` header, no closing paragraph, no metrics or
  verification block.
- Bullets say what changed, not why. Rejected alternatives and measurements go to
  `DOCUMENTATION/ClaudeMemory/Decisions/*`, same as §6.
- **Never** add a `Co-Authored-By` trailer.
- **One commit unless I ask for more.** Everything in the tree lands as a single commit, however many
  concerns it spans. Split into slices only when I explicitly ask for multiple commits.
- **Nothing is left behind.** Anything uncommitted in the tree goes into the next commit, including
  changes that predate the session or were made by someone else. Never commit a subset of paths and
  leave the rest dirty - if it is changed and uncommitted, it ships with the next commit.

```
Virtualize the document view onto the layout cache

- View presents visible line segments from the cache, not a control per block
- Canvas height is the cache extent, not the sum of its children
- Materialized unit is the line segment, so TextRunControl does no wrapping
- SetContentWidth reports rewraps so keyed controls get dropped
- Text wraps on word boundaries
```

## 10. Cheaper Agents For Easier Work

**Hand off the mechanical. Keep the judgment. Verify everything that comes back.**

Standing permission to spin work out to a cheaper subagent - no need to ask me each time.

- **Goes out:** work where the decision is already made and only the typing is left. Applying an
  agreed pattern across N call sites, mass renames, gathering "which files mention X", reading a
  file set to answer one specific question, transcribing a table.
- **Stays with you:** everything in §1 and §2 - design, forks, the plan itself. Vulkan, threading and
  layout correctness. Anything that could turn into a departure (§8), because a subagent cannot
  notice it is departing from a plan it was never shown.
- **The brief must stand alone.** Paths, exact old and new text, and the boundary of what it may
  touch. If you find yourself writing "use your judgment", it does not qualify.
- **Their report is not evidence.** Read the diff, build it, and answer for it as if you wrote it.
  Never repeat a subagent's claim to me as a verified result.
- Splitting is for saving cost on bulk, not for skipping the thinking. One agent per mechanical
  sweep, not one per file.

The test: could you write the instruction precisely enough that a wrong result would be obvious on
sight? Then send it. Otherwise do it yourself.

This project is a C# game engine called Aurora using Silk.NET/Vulkan/GLFW.
Always check CLAUDE.md and NAMESPACES.md before suggesting new code.
Current focus is in "DOCUMENTATION/Work in Progress List.md".
Use deep thinking for architectural problems. Explain architectural decisions - why one way and not another - when asked for the reasoning; see §7 for when to volunteer it and §6 for where it gets written down.
When given to generate code DO NOT copy the whole file. Only write what (or if needs to be added where) needs to be changed and with what new code. When creating new classes write them out in entirety (without includes). Skip includes unless they're from a new nuget package.
If can use xml - use xml. NO JSON or other similar formats.
When creating new logic or systems update DOCUMENTATION/ClaudeMemory/* and DOCUMENTATION/Engine*

## Solution Structure
Abstract names below map to real top-level folders. Source of truth for code locations is
`NAMESPACES.md` (repo root). Detailed name↔folder mapping: `DOCUMENTATION/ClaudeMemory/Context/project-map.md`.

| Name | Real folder | Root namespace(s) | Purpose | Status |
|------|-------------|-------------------|---------|--------|
| `Engine` | `AuroraEngine` | `ArctisAurora.*` (`Core`, `EngineWork`) | Core game engine — lives in `AuroraEngine/` under `Core/`; no separate Engine project. Folder renamed from `ParticleSimulator` (2026-07); assembly/root namespace still `ArctisAurora`. | Active |
| `Editor` | `AuroraEditor` | `AuroraEditor.*` | Visual editor; consumer of the Engine | Early stage |
| `TextEditor` | `Periodic` | `AuroraPeriodic`, `Periodic.*` | Obsidian/Notion-style note app; host that boots the Engine (mapping inferred, unconfirmed) | Planning |
| — | `AuroraTesting` | — | Test project | — |
| — | `_Build` | `_Build` | Tooling; `GenerateNamespaces.cmd` regenerates `NAMESPACES.md` | — |

## Memory
Claude keeps repo-committed working memory under **`DOCUMENTATION/ClaudeMemory/`**. This is
machine-readable (terse bullets/tables) and version-controlled — distinct from the
human-readable Obsidian vault under `DOCUMENTATION/Engine` and `DOCUMENTATION/Extras` (do not
apply the vault's single-line-prose / pseudocode style to ClaudeMemory files).

Layout:
- `ClaudeMemory/Context/` — solution layout, project↔folder↔namespace mapping, orientation.
- `ClaudeMemory/Patterns/` — recurring "how to do X here" recipes.
- `ClaudeMemory/Decisions/` — Claude-facing complements to the root `Decisions.md`.
- `ClaudeMemory/Mistakes/` — past errors, so they are not repeated.

**Referencing code via `NAMESPACES.md`:** `NAMESPACES.md` (repo root) is an auto-generated
`namespace -> relative path` index (built by `_Build/GenerateNamespaces.cmd`; do not hand-edit
— regenerate it). ClaudeMemory notes reference code by **namespace + class name** and resolve
the path through `NAMESPACES.md` rather than hardcoding paths, which rot on refactor. See
`ClaudeMemory/Patterns/finding-code.md`.

**Workflow before suggesting code:** read `CLAUDE.md` → check `NAMESPACES.md` → check
`DOCUMENTATION/ClaudeMemory/`. (Architectural changes: also read root `Decisions.md`.)

## Shared Conventions
- **Style:** Mix of OOP and data-oriented (ECS-first for runtime, OOP for tooling/editor)
- **Naming:** PascalCase types, camelCase locals, `I`-prefix interfaces
- **No raw resource management** — wrap Vulkan handles in disposable C# types
- **GitHub branching:** [add your branch strategy here if any]

## Techdebt
This paragraph will describe of the known issues with the engine that at some point will be tried to fix

Engine's ECS is currently object based and not data (struct (or even record)) based system
Vulkan renderer currently renders only UI (rendering modules not yet ready)
Hardcoded absolute paths in default asset preparation
bootstrap steps all report success unconditionally — the bool return is wired, the per-step failure
detection is not
unfinished or unsafe threading

## Projects



### Engine — Context File
**Language:** C# | **Libs:** Silk.NET.Vulkan, Silk.NET.GLFW

#### Architecture Overview
Mixed OOP + ECS. ECS drives runtime simulation; OOP used for engine services and tooling boundaries.
ECS design is still being settled — avoid refactoring the entity/component model without asking first.

#### Systems & Status
| System | Status | Notes |
|--------|--------|-------|
| Rendering | ✅ Stable | Full Vulkan pipeline rendering UI. Needs CPU-side logic work. |
| Asset Registry | ✅ Stable | Dual lookup: GUID handles + path/string |
| ECS | ✅ Stable | Architecture TBD — do not assume archetype or sparse-set |
| XSD / Data Layer | ✅ Stable | XSD schema generation for serialized data |
| Filing | 🔧 In progress | File I/O utilities |
| Threading | 🔧 In progress | Basic threading, design not finalised |

#### Engine Loop & Threading — Key Facts
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

#### Input System — Key Facts
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

#### Bootstrapper — Key Facts
The XML rework has **landed**. There is no `BootstrapStage` enum and no `[A_BootstrapStage]`
attribute any more; both were replaced by declared phases in `Bootstrap.xml`.
- `Bootstrapper.Load(Paths.BOOTSTRAP)` reflects over all loaded assemblies once, collecting every
  static method tagged `[A_XSDActionDependency(name, "Bootstrap")]` into a name → method map
- `Bootstrap.xml` declares `<Phase Name="…">` elements holding ordered `<Step Action="…"/>`
  entries; the action string is the attribute's name, and `Bootstrapper.RunPhase(name)` invokes
  them **in the order the XML lists them**
- **Execution order is data, not reflection order** — to change what runs when, edit
  `Bootstrap.xml`; there is exactly one phase today, `"Bootstrap"`
- A step whose action name resolves to nothing is logged and skipped, not fatal
- Every bootstrap step returns `bool`. **A step returning `false` halts its phase** — nothing after
  it runs and `RunPhase` returns false. Steps that cannot meaningfully fail return `true`
- Comments in `Bootstrap.xml` mark the pre-renderer / post-renderer boundaries, which is what the
  old `PreGPUAPI` / `PostGPUAPI` stages encoded

#### Shutdown — Key Facts
The bootstrap sequence run backwards, and deliberately the same shape — `Shutdown.cs`,
`Shutdown.xml`, `[A_XSDActionDependency(name, "Shutdown")]`, `Shutdown.RunPhase(name)`, `bool`
returns that halt a phase.
- **Two phases, and the difference matters.** `Request` may refuse: it is where anything that asks
  the user something lives. `Commit` is past the point of no return and runs against a tree that is
  still live, before any teardown
- **A step that must ask the user returns `false` and re-enters.** It opens its prompt, returns
  false to halt the attempt, and the prompt's callbacks call `Shutdown.Resume()`, which re-runs the
  sequence — one question per attempt, in order. Cancelling simply never resumes, which is what
  leaves the application running. `Shutdown.Request()` starts a *fresh* attempt and clears what a
  previous one remembered
- A `Commit` step returning false is logged and skips the rest of its phase, but the application
  still exits — otherwise one broken handler makes it unquittable
- `Window.Close` on `Engine.primary` calls `Shutdown.Request()`; any other window settles its own
  notes through `NoteActions.SettleWindow` and closes alone
- **Do not** put a prompt in a `Commit` step — nothing there can suspend or refuse

#### Rendering — Key Facts
The full Vulkan pipeline is working and rendering UI:
- Instance, device, swapchain, render passes, pipelines, shader loading — all present
- **Current pain point:** CPU-side widget logic — positioning, parenting, children, scaling
- **Set 0 is the renderer's, not a module's.** `Renderer` owns a global descriptor set holding
  `GpuEngineStats` (per-system tick times, total/wrapped time, frame index), one host-visible mapped
  buffer per swapchain image, built in `Renderer.PrepareDescriptors`. A module's own sets start at 1
- **Per-frame buffer writes have two owners:** `Renderer.Draw` writes the global ones after the
  timeline wait, then calls each module's `UpdateFrameData(imageIndex)` for its own. Do NOT put
  module-specific buffer updates back into `Draw`
- **Buffer memory is chosen by write frequency, not by buffer type.** Rewritten every frame →
  `AVulkanBufferHandler.CreateMappedBuffer`, which prefers `DEVICE_LOCAL|HOST_VISIBLE|HOST_COHERENT`
  and falls back to plain host-visible, with **one buffer per swapchain image**. Uploaded once →
  `CreateBuffer<T>`, which stages into `DEVICE_LOCAL`. Textures always stage — optimally-tiled images
  cannot live in host-visible memory on any platform. Nothing branches on ReBAR; the memory-type
  query is what absorbs PC/Android/Apple differences
- **Never read back through a mapped pointer** — the target may be write-combined, where one read
  costs orders of magnitude. Whole-struct `Unsafe.Write` and `WriteMappedRange` are forward-only and
  safe; `ptr->field = x`, `+=` or a read-to-compare are not.
  See `ClaudeMemory/Decisions/mapped-streaming-buffers.md`
- Module command buffers are recorded **only when dirty**, so anything that must change per frame has
  to change through memory a stable descriptor already points at — not through a re-record
- Shaders live in **three physical copies** (`AuroraEngine/`, `Periodic/`, `AuroraEditor/Shaders/`).
  Edit the `AuroraEngine` copy, compile with `glslc --target-env=vulkan1.3`, copy the `.spv` to all
  three — the UI `.spv` are SPIR-V 1.6 and must stay byte-identical across projects
- Do NOT redesign the Vulkan pipeline; focus help on the scene/widget graph layer above it

#### Asset Registry — Key Facts
- `AssetRegistries` is a **type-indexed library of typed dictionaries** — each registry
  is a `Dictionary<TKey, TValue>` stored in two parallel lookups:
  - `library`: keyed by **value Type** (e.g. `typeof(AVulkanMesh)`)
  - `libraryByName`: keyed by **string name** (e.g. `"Meshes"`)
- Registry definitions are **XML-driven** — `Registry.xml` declares each dictionary's
  name, key type, and value type; parsed at bootstrap via `ParseXML()`
- Type resolution uses `AnyXMLType.typeMap` with fallback to `AnyXMLType.FindType()`
  for engine-specific types
- Retrieval API: `GetAsset<T>(name)`, `GetRegistryByValueType<K,V>()`,
  `GetRegistryByName<K,V>()`
- Bootstrap is **two-stage**: `PreGPUAPI` parses XML + registers serializable types;
  `PostGPUAPI` loads default assets (meshes, fonts, textures, styles)
- Assets derive from abstract `Asset` with `LoadAsset()` and `LoadDefault()`
- Serializable types are auto-discovered via `[Serializable]` attribute + reflection,
  stored as `Dictionary<uint, Type>` with hashed IDs
- **Do not** assume direct field-based asset access — always go through the registry API

#### ECS — Key Facts
- Architecture is not finalised — ask before assuming storage strategy
- Components and entities exist; query/iteration pattern TBD

#### XSD / Data Layer — Key Facts
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
  
### What Claude Should NOT Do
- Don't refactor the Vulkan pipeline internals
- Don't assume a specific ECS storage model
- Don't replace XSD with another serialization approach unprompted
- Don't introduce new NuGet dependencies without flagging it first

### Editor
Depends on: **Engine project**

#### Purpose
Visual editor for MyEngine. Built on top of the Engine — uses the same ECS, rendering, and asset systems.

#### Status
Early stage. Core editor shell is being set up.

#### Architecture Notes
- Editor is a consumer of the Engine, not a fork of it
- UI is rendered through Engine's Vulkan pipeline
- [Add: docking, panels, tool windows — describe once they exist]

#### What Claude Should Know
- Editor-specific code lives here; Engine internals are in Engine/CLAUDE.md
- Don't duplicate engine logic in the editor — extend via engine APIs

### TextEditor — Context File
Obsidian/Notion-style note-taking and document app.

#### Status
Planning phase.

#### Goals
- Rich text editing
- [Add: linked notes, graph view, tags, blocks — whatever you plan]
- [Add: file format — markdown? proprietary?]

#### Architecture Notes
- [Does this use the Engine renderer, or its own UI stack?]
- [Desktop app? Embedded? Cross-platform target?]