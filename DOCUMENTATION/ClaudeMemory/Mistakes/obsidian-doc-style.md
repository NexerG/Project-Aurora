# Mistake — wrong style when editing the human Obsidian docs

**Context:** editing notes under `DOCUMENTATION/Engine` and `DOCUMENTATION/Extras` (the
human-readable Obsidian vault).

**What went wrong (2026-05-30):** rewrites (Serializer, Virtual File System) introduced
prose-paragraph method descriptions and `;`-jammed step lines, diverging from the user's
authored style and rendering badly in Obsidian.

**Rules for the human vault (Engine/Extras only):**

1. **Prose = one physical line.** Obsidian renders soft line breaks as visible breaks. Never
   hard-wrap a paragraph / list item / blockquote across source lines — one logical line per
   physical line; let Obsidian wrap visually.
2. **Method/function bodies use pseudocode, not prose.** Model on `XSDGenerator.md` and
   `Atlas Meta Data.md`: each step on its own line, real `\t` indentation for nested
   for-each/if blocks; no `;`-separated step lists, no prose paragraphs with `;`.

**Note:** these rules apply to the **human vault**. They do **not** apply to files in
`ClaudeMemory/` — those are terse machine-readable notes (bullets/tables are fine).
