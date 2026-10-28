---
name: aurora-codereview
description: Review C# changes in Aurora against the aurora-code rules — a diff, a commit, a range or named files. Use when asked to review, check or audit C#, and after writing C# before reporting it done. Reports findings by aurora-code rule number; never fixes.
---

# Reviewing C# in Aurora

The rules are `aurora-code`'s. Load it first. This skill says only how to check a change against them and
cites them by number; it never restates or reinterprets one. A rule that seems wrong for the case is a
question to the user, not a finding and not an exemption.

Rule numbers are `aurora-code`'s. When `aurora-code` renumbers, this file and `scan.sh` renumber in the same
change.

## What is reviewed
- Default: the working tree and untracked `.cs` against `HEAD`. Otherwise a commit, an `a..b` range, or named
  files (every line).
- Added and changed lines only. A changed line puts its whole statement under review; a rewritten method puts
  the method. A line that only moved or re-indented is not a rewrite — compare it with the `-` side.
- Untouched code is not reported, even beside a finding: existing code changes only when a change rewrites
  that statement anyway. New lines follow every rule even where the file around them doesn't.
- C# only. GLSL is `shader-pipeline`'s; XML data files are CLAUDE.md's.
- The review reports and stops. A fix is a change and goes through CLAUDE.md §2.

## 1. Scan
```bash
bash .claude/skills/aurora-codereview/scan.sh [<commit> | <a>..<b> | <file.cs>...]
```
Greps the added lines and prints `file:line  rule N  what | code`. It covers 7, 8, 9, 11, 29, 35, 37, 43, 48,
49, 51, 52. A hit is a lead, not a finding: open the line, confirm it, drop false positives without
mentioning them (`.First(` on an own type's method, `% 4` that isn't a direction turn). It cannot see
anything below.

## 2. Read
Read hunks, not files: `git diff -U3 HEAD -- <file>`. Widen to the enclosing method only where a rule needs
it — frame reach, a loop's bound, where a span was fetched.

**Frame reach first.** For each changed method, decide whether the frame loop reaches it: an action named by
a `<Step>` in `Frame.frame.axml` and what it calls, `Measure` / `Arrange`, `HandleUI`, input handlers,
`OnTick`, `UpdateFrameData`, `Renderer.Draw`. Bootstrap and Shutdown actions, importers, tests and tooling
are not frame code — rule 1 is off there, every other rule is on. Undecided after one grep of the callers →
report rule 1 findings as plausible.

| Rules | Look for |
|---|---|
| 1 (frame code) | `new` of a class or array, `$"…"` / concatenation / `ToString`, a lambda capturing a local or `this`, a struct cast to `object` or an interface, `foreach` over an interface, `yield` |
| 2, 4, 5 | a list grown then `ToArray` where the count was computable; `stackalloc` over 1 KB or inside a loop; `Rent` without `Return` in `finally`; a method returning a new collection |
| 3, 17 | a per-frame buffer that is `static`, or a local `new` each call; a list other steps read that isn't a handle-less pool |
| 6 | a field write, `+=` or any read through a mapped pointer |
| 7–11 | `List<T>` where the count was knowable (child collections of an `[A_XSDType]` are exempt); nesting; `[,]`; linear search over 16+ items; a struct key without `IEquatable<T>` / `GetHashCode` |
| 12–28 | per-row data in fields; a column struct with managed fields; a span stored or used after an `Append`; a `GetRef` / span write with no `MarkContentDirty` / `MarkRangeDirty`; a dense index kept across frames; a stale handle used without `Alive` / `DenseOf`; rows shifted by hand; pool access missing from the step's `Reads=` / `Writes=` (grep `Frame.frame.axml`); events or a shared dirty flag instead of a `PoolCursor`; threading before the single-thread loop is done |
| 29 | anything the scan missed, e.g. LINQ on a `Dictionary` or an `IEnumerable` passed in |
| 30–37 | a class for a value; a struct not `readonly` / mutating members not split; a class or array carrying results; a struct parameter without `in` / `ref`; an unsealed class nothing derives from (grep `: Name`); a virtual or interface call in a data loop; floats for integer data |
| 38–42 | a loop bound on a field or `Count` instead of the local array or span; invariants inside the loop; the O(n) test before the O(1); a data-bounded loop without a cap, or a cap without `Log.Once()` in frame code |
| 43–47 | a predicate not phrased as a question; a query named like an action; short names in control flow, long ones in a formula; two metaphors in one algorithm; a collection name that doesn't say its index. Clear misses only, not taste |
| 50, 53, 54 | braces on a single-statement body; a one-expression member not `=>`; a callee above its caller |
| 56–61 | a comment saying why — tradeoff, rejected option, budget, consequence; one comment per field; a method comment longer than one short line; comments in a plain ~20-line method; an edited comment that grew; a short widow line |
| Scope of a change | a feature, abstraction, option or error path nobody asked for. Only when you know what was asked |

## 3. Report
```
file:line · rule N · what is wrong · the fix
```
- One rule per finding. The same rule several times in one method is one finding with a line range.
- Order: frame allocation (1, 3), mapped memory (6) and pool correctness (19–24) first — they are bugs or a
  broken budget; then types and collections; then LINQ; then naming, formatting and comments.
- A finding that rests on something unconfirmed (frame reach, a caller not read) says so in its "what".
- Nothing found: `No findings against aurora-code.`
- Last, `Not checked:` with what the review couldn't settle — only when there is something.
- No summary of the change, no praise, no opinions outside the rules (CLAUDE.md §5).
