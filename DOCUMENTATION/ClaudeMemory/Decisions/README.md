# Decisions (Claude-facing)

The authoritative architectural decision log is **`Decisions.md` at the repo root** — read it
before suggesting architectural changes (CLAUDE.md mandates this).

This folder is for Claude-facing complements to that log: short notes capturing *why* a
decision constrains how Claude should work, or decisions discovered while working that are not
yet written up in the root log. Keep one decision per file.

Standing constraints already recorded in `CLAUDE.md` (do not re-litigate without asking):

- ECS storage model is **not finalised** — do not assume archetype/sparse-set.
- Vulkan pipeline internals are **off-limits** to refactor; help lives at the widget-graph layer.
- `BootstrapStage` enum is **being replaced** by XSD/XML-driven sequencing — don't add enum values.
- Physics thread design is **unsettled** — don't propose physics changes unprompted.
- XSD/XML is the serialization format — **no JSON** or alternatives.
