---
name: aurora-docs
description: Write up a landed change into Aurora's documentation — ClaudeMemory notes, the Obsidian vault under DOCUMENTATION/Engine, and the Work in Progress List. Use after a system or decision lands, or whenever asked to update the docs, write this up, or record a decision. Three destinations with three different house styles that are easy to mix up.
---

# Writing up a change

CLAUDE.md: new logic or systems update `DOCUMENTATION/ClaudeMemory/*` **and** `DOCUMENTATION/Engine*`. Three
destinations, three voices. Getting the voice wrong has happened before — `ClaudeMemory/Mistakes/obsidian-doc-style.md`
records it.

| Destination | Audience | Voice |
|---|---|---|
| `ClaudeMemory/` | me, next session | terse; bullets and tables; one concern per file |
| `DOCUMENTATION/Engine`, `Extras` | the user, in Obsidian | prose, but **one physical line per paragraph**; pseudocode method bodies |
| `Work in Progress List.md` | the user, tracking | one dense checklist entry, dated, ending in a `See …` pointer |

## ClaudeMemory

Pick the folder by what the note *is*:

- **`Decisions/`** — a choice was made and alternatives were rejected. The common case.
- **`Context/`** — how something is laid out or where things live.
- **`Patterns/`** — a repeatable "how to do X here" recipe.
- **`Mistakes/`** — something went wrong; record it so it does not recur.

The `Decisions/` shape, as the existing files use it:

```markdown
# Decision — lowercase summary of the choice

**Date:** 2026-08-23
**Scope:** `ArctisAurora.Namespace` — `ClassOne`, `ClassTwo`, `Shaders/UIRasterizer/UI.vert`

## What changed
- Bullets. Concrete. Names of types, fields and methods.

## Why these choices

**The claim, bolded, one sentence.**
The reasoning underneath it, including what was rejected and what it would have cost.

## Known gaps
- What is not done, not verified, or known to be inconsistent.

Related: [[other-note]], [[another-note]]
```

Rules that bite:

- **A new `Decisions/` note adds a row to `Decisions/INDEX.md` in the same commit.** One line: the note, what
  it settles, its key symbols. An unindexed note is one nobody finds without grepping. Mark it `PLANNED`,
  `PARTIAL` or `FUTURE` in the row if it is not landed code.
- **A landed system updates `Context/where-things-live.md`.** Add or correct the concept row — the words the
  user would say for it, the types that own it, the data XML it reads. Renaming or moving a type makes an
  existing row wrong; fix it there too.
- **No hardcoded file paths.** Reference `namespace` + class name; the path resolves through `NAMESPACES.md`.
  Paths in this codebase move and recorded ones rot. See `Patterns/finding-code.md`.
  `where-things-live.md` is the one exception, and only for data XML, which no index covers.
- **`Related:` uses `[[wikilinks]]`**; inline references use relative markdown links. Both conventions are live
  in the folder — match the position, not one global rule.
- **Do not apply the vault's prose style here.** Bullets and tables are correct in ClaudeMemory.
- Rejected alternatives and measurements live here, never in a source comment (CLAUDE.md §6).

## The Obsidian vault — `DOCUMENTATION/Engine`, `DOCUMENTATION/Extras`

Two rules, both from `Mistakes/obsidian-doc-style.md`:

1. **One logical line = one physical line.** Obsidian renders a soft line break as a visible break, so a
   hard-wrapped paragraph, list item or blockquote breaks mid-sentence on screen. Never wrap prose in the
   source; let Obsidian wrap it visually. Go as wide as the sentence needs.
2. **Method and function bodies are pseudocode, not prose.** One step per line, real tab indentation for
   nested for-each/if blocks. No `;`-separated step lists, no prose paragraphs describing the steps. Model on
   `XSDGenerator.md` and `Atlas Meta Data.md`.

These rules apply **only** to the vault. They do not apply to ClaudeMemory.

## Work in Progress List

**The WIP list holds open work only.** It is the file that answers "what am I working on", so it has to be
readable whole in one cheap read. Two rules keep it that way, and they are the only two.

### A landed entry leaves the file

On completion the entry moves to `DOCUMENTATION/Changelog.md` — verbatim, appended, dated. It does not stay
behind as a `- [x]`. Narrative is free in the Changelog because nothing reads it whole; in the WIP list it is
the entire problem. When this rule was written the list was 187 KB, and **81% of it was completed work** —
138 done entries averaging 1095 B against 202 open entries averaging 171 B.

**A done entry with open children is two different situations, and they get opposite treatment.**

- **The children are residual gaps in the thing that landed** — they would read as nonsense standing alone.
  The parent stays, **as a stub**: headline, date, `— landed, See …`. The narrative is already in the note it
  points at, so keeping it here is pure duplication. 12 entries are in this state.
- **A child is its own piece of work that merely got filed underneath** — Alt+F4 under "shutdown is a
  sequence", the folder browser under "Project browser". **Promote the child to its parent's level and let
  the parent leave.** 9 entries went this way at the split.

The test: read the child on its own. If it still makes sense, it does not belong nested.

```
- **short headline (2026-08-23)** — what changed and the reasoning that matters, dense, single physical line. **Verified**: what was actually checked. **NOT GUI-verified.** See `ClaudeMemory/Decisions/note-name.md`
```

The verification claim is load-bearing and has its own rules — see `aurora-verify` for which word is honest.

### An open entry is an index row, not a write-up

Hard cap **~400 characters**. An item that needs a paragraph has earned a `Decisions/` note; write the note
and let the entry point at it.

```
- [ ] **`WindowRoot.Arrange` ignores a child's `margin`** — landing 2 behaviour, found by 6a's probe → `stack-panel-arrange-clamp`
```

This is the discipline `Decisions/INDEX.md` already runs on: 70 notes in 13 KB, because each row says what the
note settles and then stops. Without the cap, eviction is a one-time cut and the file regrows — 24 open
entries were already over 400 characters, carrying half of all open work between them.

### A multi-part plan gets its own file

An entry describing work in several parts — slices, stages, an A-to-N sequence, or a design with open
questions — does not belong in the list at all. It goes to `ClaudeMemory/Context/<name>-plan.md` and leaves a
descriptor: what it is, the one thing that makes it matter, and the pointer. `log-viewer-plan.md` is the model
for the file; `ui-animation-plan.md`, `file-chooser-plan.md` and `keybind-intent-plan.md` were split out this
way. Append to an existing plan file when the work is a follow-up to one — the entity-registry group went into
`multi-windowing-plan.md`, not a file of its own.

A plan file states its status in its own header. **"Agreed" and "nothing designed yet" are different files** —
do not let a wish list read as a settled plan.

- Nest open follow-ups as `- [ ]` children under their parent rather than starting a new top-level item —
  children are how a multi-part item tracks its own progress, and that is the only thing they are for.
- Strike through and mark `**REVERTED <date>**` rather than deleting an entry that was undone.
- The tripwire is in `aurora-orient`: `wc -c` over 40 KB means the list needs curating.

## What does not get written down

**A repaired bug leaves no log entry.** The docs carry decisions and open issues; once a bug is fixed, the
documentation simply describes the fixed behaviour. Do not add a "fixed X" note to the vault. A *decision* made
while fixing it still belongs in `ClaudeMemory/Decisions/`, and a *mistake worth not repeating* belongs in
`ClaudeMemory/Mistakes/`.

**A retraction goes everywhere the claim went.** If something written up turns out to be wrong, correct the WIP
list and every decision note that repeated it — not only the newest file.

## Verify

- The vault file has no wrapped prose lines — `awk 'length > 0 && length < 60 && prev ~ /[a-z,]$/' file.md`
  finds suspect breaks, or just read it.
- The ClaudeMemory note hardcodes no paths outside the `**Scope:**` line.
- Every `[[link]]` names a file that exists in `ClaudeMemory/`, or is deliberately a forward reference.
- The WIP entry's verification word matches what was actually done.
- Every `- [x]` still in the WIP list has an open descendant, and no new open entry is over 400 characters:
  `grep '\[ \]' "DOCUMENTATION/Work in Progress List.md" | awk 'length>400' | wc -l`
  (24 legacy long entries predate the cap and drain as their work lands — do not retro-cap them, 20 have no
  note to point at yet.)
- Every note in `Decisions/` has a row in `Decisions/INDEX.md`:
  `ls Decisions/*.md | while read f; do n=$(basename "$f" .md); case "$n" in INDEX|README) continue;; esac;
  grep -q "\[\[$n\]\]" Decisions/INDEX.md || echo "unindexed: $n"; done`
- The types the note names appear in a `where-things-live.md` row, under words the user would actually say.
