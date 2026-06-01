# Project map — abstract names → real folders

`CLAUDE.md`'s *Solution Structure* table uses abstract names. They map to actual top-level
folders/projects as follows (source of truth for namespaces: `NAMESPACES.md` at repo root):

| CLAUDE.md name | Real folder         | Root namespace(s)                                                  | Notes                                                                                                                        |
| -------------- | ------------------- | ------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------- |
| `Engine`       | `ParticleSimulator` | `ArctisAurora`, `ArctisAurora.Core.*`, `ArctisAurora.EngineWork.*` | The engine lives **inside** ParticleSimulator (no separate `Engine` project). Engine core is under `ParticleSimulator/Core`. |
| `Editor`       | `AuroraEditor`      | `AuroraEditor.*`                                                   | Visual editor; consumer of the Engine.                                                                                       |
| `TextEditor`   | `Periodic`          | `AuroraPeriodic`, `Periodic.*`                                     | **Inferred** (name ↔ Obsidian "periodic-notes"; unconfirmed). A host app whose `Main` boots `Engine` and loads UI from XML.  |
| `Hackathon`    | —                   | —                                                                  | No such folder exists in the repo.                                                                                           |

Additional top-level items not in the table:

- `AuroraTesting` — test project.
- `_Build` — tooling. `_Build/GenerateNamespaces.cmd` regenerates `NAMESPACES.md`.
- `DOCUMENTATION` — Obsidian vault (human docs) + this `ClaudeMemory` folder.

## Engine internal layout (under `ParticleSimulator/Core`)

- `Core/` — `Engine.cs`, `Bootstrapper.cs`, `InputHandler.cs`, `JobSystem.cs`, `AuroraScene.cs`.
- `Core/ECS/` — entities + components (`EngineEntity/`, `RenderingComponents/`).
- `Core/Registry/` — `AssetRegistries.cs`, `EntityRegistry.cs`, `XSDGenerator.cs`, `Assets/`.
- `Core/Rendering/` — Vulkan renderer, modules, pipelines, mesh subcomponents.
- `Core/UISystem/` — `AuroraFont`, `Glyph`, layout + `Controls/` (widget tree).
- `Core/Filing/Serialization/` — `Serializer`, `AssetImporter`, `Paths`, `VirtualFileSystem`.

> Detailed per-system facts live in `CLAUDE.md`. This file only fixes the name↔folder mapping
> that is *not* derivable from `CLAUDE.md` alone.
