# Decision — the `aurora-codereview` skill reviews C# against `aurora-code`, by rule number

**Date:** 2026-10-28
**Scope:** `.claude/skills/aurora-codereview/SKILL.md`, `.claude/skills/aurora-codereview/scan.sh`

## What changed
- New project skill `aurora-codereview`: a review procedure for C# changes — scan, read, report — whose every finding names an `aurora-code` rule number.
- `scan.sh` reads `git diff -U0` (default `HEAD` plus untracked `.cs`; or a commit, an `a..b` range, or named files in full), keeps the `+` lines and greps them for rules 7, 8, 9, 11, 29, 35, 37, 43, 48, 49, 51, 52. Output `file:line  rule N  what | code`, then a hit count. Read-only.
- No engine code, no CLAUDE.md change, no change to `aurora-code`.

## Why these choices

**The skill cites rules and restates none.**
Two copies drift — the reason [[aurora-code-rules]] moved CLAUDE.md's style sections into `aurora-code`. The review's table says only what to *look for* per rule group. Cost: rule numbers couple the two skills; the skill says a renumber of `aurora-code` renumbers `SKILL.md` and `scan.sh` in the same change.

**Only added or changed statements are judged.**
`aurora-code`: existing code changes only when a change rewrites that statement anyway; new lines follow every rule even where the file doesn't. So a new `_field` in a file that already uses `_` is still a finding. Rejected: reporting the untouched code around a finding — ~490 LINQ calls and 434 `_` fields in `Core` would bury the diff's own findings.

**Grep pre-pass, then reading.**
About a dozen rules are visible in one line's text; a script gives the same hits every run and filters to `+` lines, which hand-typed greps get wrong. Everything else — frame reach, pools, types, naming forms, why-comments — needs the hunk read. Rejected: listing grep lines in `SKILL.md` instead of a script.

**Frame reach is decided first.**
Rule 1 (no per-frame allocation) applies only to code the frame loop reaches; boot, shutdown, importers, tests and tooling are exempt from it and from nothing else. Undecided after one grep of the callers → the finding is reported as resting on that.

**Review only; the review runs in the main session.**
A fix is a change and goes through CLAUDE.md §2. Judging against rules is the judgment CLAUDE.md §8 keeps out of subagents.

## Verification
- `scan.sh` on the 2026-10-28 dirty tree: 45 hits across `TexExpander`, `TexTypesetter`, `FrameScheduler`, `TexTests`; line numbers checked against source. Commit mode (`3bdc427`) and file mode (`MainSystem.cs`) run.
- First run's false positives fixed: "nested List" matched `List<T> x = new List<T>()`; static calls (`Math.Max`, `Array.Reverse`, `string.Concat`) matched LINQ names.

## Known gaps
- The scan is regex over single lines: a statement split across lines, a `var` or `is null` inside a string, or LINQ on a type named like a static class are missed or false.
- Nothing runs the review automatically; it is loaded by name or by its description.

Related: [[aurora-code-rules]]
