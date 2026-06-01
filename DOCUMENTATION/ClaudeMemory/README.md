# ClaudeMemory

Machine-readable working memory for Claude (the AI assistant), committed to the repo so it
is version-controlled and shared across machines/sessions. This is **separate** from the
human-readable Obsidian docs under `DOCUMENTATION/Engine` and `DOCUMENTATION/Extras` — do not
apply the human vault's prose/pseudocode style here. These files optimise for fast, accurate
recall by Claude, not for human reading.

## Relationship to other memory

| Store | Location | Scope | Tracked |
|-------|----------|-------|---------|
| ClaudeMemory (this) | `DOCUMENTATION/ClaudeMemory` | Repo-specific facts, patterns, decisions, past mistakes | git |
| Private auto-memory | `~/.claude/projects/<repo>/memory` | User/feedback/project facts, cross-session | no |
| Human docs | `DOCUMENTATION/{Engine,Extras}` | Authoritative human-authored Obsidian vault | git |

When a fact is repo-specific and useful to commit/share → write it here. When it is about the
user, their preferences, or session continuity → it belongs in the private auto-memory.

## Folders

- **Context/** — how the solution is laid out; project↔folder↔namespace mapping; orientation facts.
- **Patterns/** — recurring "how to do X in this codebase" recipes (e.g. how to locate code).
- **Decisions/** — Claude-facing notes that complement the root `Decisions.md` (architectural decisions).
- **Mistakes/** — things that went wrong before, so they are not repeated.

## Conventions

- One concern per file. Terse. Bullet points and tables over prose.
- **Do not hardcode file paths.** Reference code by `namespace` + class name and resolve the
  path through `NAMESPACES.md` (repo root). See [Patterns/finding-code.md](Patterns/finding-code.md).
- Cross-link with relative markdown links.
- Before suggesting code, the workflow is: read `CLAUDE.md` → check `NAMESPACES.md` → check this folder.
