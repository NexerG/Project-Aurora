# Mistake — a `--test` run hung at exit on a "Name this note" prompt

**Date:** 2026-10-01

## What happened
- `TextInput.PictureEditUndo` edited an unnamed note; the next (last) test, `PictureRoundTrip`, called no `t.Show`.
- The dirty editor was still the tree at exit. `Shutdown.Request` ran `Notes.SettleUnnamed`, which opened the
  naming prompt and halted. A test run gets no OS input, so the process waited forever after printing its summary.
- Looked like a slow run for minutes; the run itself was 8 s.

## Rule
- The **last** test of a suite must leave no dirty session: end it with a `Show` of something clean, or order a
  non-editing test last (what `TextInput.tests.xml` does now).
- Always launch `--test` under `timeout 120 …` so a hang reports instead of stalling.
- Seeing `[Shutdown] step 'Notes.SettleUnnamed' reported failure` after the summary line = this.

Related: [[engine-testing]], [[note-images]]
