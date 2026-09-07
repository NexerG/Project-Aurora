---
name: aurora-handoff
description: Hand mechanical work to a cheaper subagent and verify what comes back. Use when a change repeats across many call sites, when a question needs sweeping many files, or whenever CLAUDE.md §10 says the decision is made and only the typing is left.
---

# Handing work off

CLAUDE.md §10 sets the policy — what goes out, what stays, and that approving the plan is the permission to
spawn. This is the mechanics.

## Why it pays

Your tokens are re-sent on every turn for the rest of the session. A subagent's die when it finishes.

Reading 12 KB into your own context at turn 5 of 40 is paid 35 more times. The same 12 KB inside a scout that
returns 300 tokens is paid once. That is the whole trade, and it inverts near the end of a session — at turn
38 a spawn is a wash, so just read it.

**The floor.** A spawn starts cold and re-derives project context before it does anything. Below roughly
three files, or a question you could answer with two greps, doing it yourself is cheaper. §10's "one agent per
mechanical sweep, not one per file" is this same arithmetic.

## Which one

| | `aurora-mechanic` | `aurora-scout` |
|---|---|---|
| does | exact edits across a named file set | answers one question, read-only |
| tools | Read, Edit, Glob, Grep — **no Bash** | Read, Glob, Grep, Bash |
| send when | old text, new text and the file list are all fixed | the reading is much larger than the answer |
| never send | anything needing a judgment call | anything that must decide something |

Neither can notice it is departing from a plan it was never shown (§8). If the work could turn into a
departure, it stays with you.

## The brief

It must stand alone. The agent has none of this conversation.

```
Files:      <explicit paths, or one glob that you have already run and counted>
Old text:   <exact, copy-pasted, including case and whitespace>
New text:   <exact>
Boundary:   <what it may NOT touch — adjacent lines, comments, usings, other occurrences>
On a miss:  report it, do not adapt
```

The test from §10: could a wrong result be obvious on sight? If you are writing "use your judgment" or
"and anywhere else it appears", the brief is not ready and the work is not eligible.

## Verifying what comes back

**The report is not evidence** (§10). Never repeat an agent's claim as a verified result.

```bash
git diff --stat                    # did it touch what the brief named, and only that?
git diff -- <one representative file>
```

Then build (`aurora-verify`). Read the stat first — a file in the diff that was not in the brief is the
failure mode worth catching, and it shows up there for ~50 tokens.

Do **not** verify by re-reading the files the agent read. That spends your context on exactly the reading you
paid to avoid.

## After

Answer for the diff as if you wrote it. If it is wrong, it is your change that is wrong.
