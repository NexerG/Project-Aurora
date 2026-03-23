# MyEngine — Solution Briefing
**Stack:** C#, .NET | Silk.NET.Vulkan, Silk.NET.GLFW | Visual Studio 2026 | GitHub

## Solution Structure
| Project | Purpose | Status |
|---------|---------|--------|
| `Engine` | Core game engine | Active — see Engine/CLAUDE.md |
| `Editor` | Visual editor built on Engine | Early stage |
| `TextEditor` | Obsidian/Notion-style note app | Planning |
| `Hackathon` | Standalone hackathon project | Active |

## Shared Conventions
- **Style:** Mix of OOP and data-oriented (ECS-first for runtime, OOP for tooling/editor)
- **Naming:** PascalCase types, camelCase locals, `I`-prefix interfaces
- **No raw resource management** — wrap Vulkan handles in disposable C# types
- **GitHub branching:** [add your branch strategy here if any]

## Current Focus
> ⚡ Update this line before each new chat session
> e.g. "Working on UI widget parenting and scaling in Engine/Rendering"

---
*Paste Engine/CLAUDE.md alongside this when working on the engine.*
