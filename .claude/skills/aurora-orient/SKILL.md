---
name: aurora-orient
description: Find your way around Aurora without spending the session's context on orientation. Use at the start of any change, whenever you are about to read an index, a large source file, the WIP list, or a build log — the four files CLAUDE.md tells you to start from are 84 KB together and every one of them is a grep target, not a read target.
---

# Orienting cheaply

Every turn re-sends the whole conversation. A file read at turn 5 is still being paid for at turn 40, so the
cost of a read is its size times the turns remaining — not its size once. This is why the budget in CLAUDE.md
§2 exists, and this is the arithmetic behind it.

## What the orientation files actually cost

| File | Size | ≈tokens | Treat as |
|---|---|---|---|
| `DOCUMENTATION/Work in Progress List.md` | 33 KB | ~8k | readable whole, but see below |
| `CLAUDE.md` | 31 KB | ~8k | already in context, free |
| `Context/where-things-live.md` | 20 KB | ~5k | **grep only** |
| `NAMESPACES.md` | 19.5 KB | ~5k | **grep only** |
| `Decisions/INDEX.md` | 13 KB | ~3.3k | **grep only** |

Reading all four on a first pass costs ~21k before any analysis. Grepping them costs ~1k.

## The chain

Stop at the first rung that answers. Do not skip to a whole-tree grep.

| # | Question | Where | How |
|---|---|---|---|
| 1 | which decision settles this? | `Decisions/INDEX.md` | grep — 70 rows, each names its Key symbols |
| 2 | where does this concept live? | `Context/where-things-live.md` | grep the concept |
| 3 | which file holds the symbol? | `NAMESPACES.md` | grep the namespace |
| 4 | what methods does the class have? | the file | method grep, below |
| 5 | what does the method do? | the file | `sed -n 'START,ENDp'` |

A row in `INDEX.md` that does not match the task means that note does not need opening. That is what the
"Settles" column is for.

**There is no method index and there should not be one.** It is a grep, verified on the largest UI class —
`VulkanControl.cs`, 1194 lines / 47 KB in, ~40 lines out, 80× cheaper and it cannot go stale:

```bash
grep -nE '(public|private|internal|protected).*\(.*\)\s*$' path/to/File.cs
```

A written index would rot exactly the way `Patterns/finding-code.md` says recorded paths rot, and faster.

**UI work starts at `Context/ui-orientation.md`** — one entry per `UINext` component: what it does, its XML
element, entry points, region names. It is a component index, not a method index: the entry says which region
to `sed`, and methods stay a grep. Read the entry, not the source file; dig into code only when the entry is
not enough.

**A note over ~10 KB is read like a source file** — `grep -n '^#' note.md`, then `sed` the one section.
`Decisions/ui-engine-stack.md` is 48 KB and ordered by landing, not topic; the orientation file links its
sections, never the whole note.

## When a whole-file read is right

Almost never. 225 `.cs` files, 1.88 MB, largest 1810 lines, median 8.4 KB. CLAUDE.md §2's three cases —
changing control flow, changing a type's shape, working somewhere the indexes do not name — widen the read to
the regions the change touches or calls into, not to the file. Map it, then read only those regions:

```bash
grep -nE '#region|(public|private|internal|protected).*\(' path/to/File.cs
sed -n 'START,ENDp' path/to/File.cs
```

An off-topic region stays unread — `Control`'s authored XML properties were no input to context menus, and
reading all 702 lines of it cost more than any other read in that session. Read end to end only when the map
cannot say which regions matter.

Otherwise: grep the symbol, `sed` the window around the hit, and change the lines you came for. "No plan built
on a guess" means do not guess about the lines you are changing — not read 1,200 to change 3.

**Do not re-read a file you just edited.** `Edit` errors if it did not apply.

## The WIP list

**33 KB and 202 open items, as of 2026-09-08.** It was 187 KB — 81% of it completed work — until the landed
entries moved to `DOCUMENTATION/Changelog.md` and the multi-part plans to `ClaudeMemory/Context/*-plan.md`.
At this size reading it whole is a fair ~8k, but one phase is usually all you want:

```bash
grep -n '^# ' "DOCUMENTATION/Work in Progress List.md"          # phase line numbers
sed -n '5,186p' "DOCUMENTATION/Work in Progress List.md" | grep '\[ \]'   # Phase A, open only
```

Tripwire, one call, ~20 tokens — if this is over 40 KB the file needs curating (see `aurora-docs`):

```bash
wc -c "DOCUMENTATION/Work in Progress List.md"
```

## Build output

A full `dotnet build` log is thousands of tokens of restore chatter. Filter it:

```bash
cd "$(git rev-parse --show-toplevel)" && dotnet build AuroraEngine/ArctisAurora.sln 2>&1 | grep -E "error|Build succeeded|Build FAILED"
```

A clean build is then **one line**. Do not add `warning CS` to that filter — the tree carries dozens of
pre-existing nullable warnings and they bury the result. Ask for warnings deliberately, in their own command,
scoped to the file you touched.

Same rule for any long command: pipe it through `grep`, `tail`, or `wc -l` before it reaches context.

## Handing the reading to someone else

When the reading is much larger than the answer and you do not know where to look, that is `aurora-handoff`
and `aurora-scout`. When you already know the file, read it yourself — a spawn costs more than the read.
