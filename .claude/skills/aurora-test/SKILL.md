---
name: aurora-test
description: Run Aurora's in-engine test suite and get pass/fail back with one command, `_Build/test.sh [Suite]`. Also the entry point for writing a test or suite, golden-image checks, looking at what the app draws, and driving a running app through `--send` instead of SetCursorPos, keybd_event, SendKeys or capture.ps1 — those are in this skill's writing.md.
---

# Running Aurora's tests

```bash
bash _Build/test.sh [Suite]
```

- Builds Debug, runs Thorium with `--test` (or `--test=<Suite>`) and prints only `FAIL`/`NEW`/`APPROVED` lines,
  `new error:` lines and the `N passed, M failed, K skipped, J new — <results.xml>` line.
- Runs at the plan's midpoint and at the end, not per step (CLAUDE.md §5).
- Exit code = failed entries; 255 = the build failed, its errors printed above.
- That output is the whole answer. Open the log (`%TEMP%\aurora-test\run.txt`) only when the results line is missing
  — the run died, see `aurora-debug`.
- **Boot** fails today on pre-existing errors. `new error:` is an `ERROR`/`FATAL` kind absent from
  `_Build/test-baseline.txt`; a change broke Boot only if one appears. After fixing a baseline error, or when a new
  one is intended, rerun with `--baseline`.
- A pass earns **test-verified** on `aurora-verify`'s ladder; a passing golden **golden-verified**.

## Everything else

Read `writing.md` in this folder before writing a test or suite, reading or approving a golden (`--test-approve`),
taking a throwaway screenshot, running a host other than Thorium, or sending console commands to a running app.
