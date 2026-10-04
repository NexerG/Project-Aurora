# Decision — the docs pass runs in a fresh-context subagent

**Date:** 2026-10-04
**Scope:** `.claude/skills/aurora-docs/SKILL.md`, `.claude/agents/aurora-scribe.md`

## What changed
- `aurora-docs` no longer writes the docs. It has the main session write a brief (landed, symbols, decisions,
  verification, gaps, destinations), spawn `aurora-scribe`, then check `git diff -- DOCUMENTATION`.
- `aurora-scribe` (Sonnet, fresh context) carries the house-style rules that used to be in the skill, plus
  working rules: grep-then-offset-read on the large indexes, all independent edits in one message, shortest
  unique `old_string`, one `Write` per new file.
- The skill's statistics and rationale paragraphs moved out; this note holds the reasoning.

## Why these choices

**The cost of the docs pass was re-reading the session's context, not writing the docs.**
Measured over 35 transcripts that invoked `aurora-docs`, window = invocation to next user message:

| Per pass (mean) | |
|---|---|
| Turns | ~20 (max 49) |
| Context at start | 140K–490K tokens, typically ~300K |
| Cache-read tokens | ~5.9M |
| Output tokens | ~18K |
| Share of the session's cache reads | 10–33% |

Some transcripts are resumed copies, so absolute totals are inflated; the ratios hold. Cache reads were
~85% of the pass's cost even at the cache discount. Edit payloads were 16% unchanged context — trimming
`old_string`s or the skill text would have saved single-digit percent.

**A fresh context cuts the per-turn price roughly 5× without cutting turns.** The scribe starts around 25K
tokens and grows with what it reads.
Fewer turns (batched edits) is the secondary lever and is a rule in the agent.

**The judgment stays in the main session, written into the brief.** CLAUDE.md §10 keeps design with the main
model; the scribe gets decided facts and places them. A fact missing from the brief is reported as `GAP`,
not invented.

**Sonnet, not inherited Opus.** Placement and house style are mechanical once the brief exists; the main
session reads the diff and fixes vault prose voice if needed.

Rejected:
- Trimming `SKILL.md` alone — ~2.5K tokens per invocation, a rounding error against the cache reads.
- Writing the docs fewer places (dropping the vault or Changelog duplication) — a docs-policy change, the
  user's call, not a cost fix.
- `/compact` before the pass — loses the reasoning the docs are meant to capture.

## Known gaps
- Not yet measured after the change; re-run the transcript tally after a few passes.
- The diff check in step 3 costs one main-context turn plus the diff size; a large vault rewrite makes that
  read non-trivial.
