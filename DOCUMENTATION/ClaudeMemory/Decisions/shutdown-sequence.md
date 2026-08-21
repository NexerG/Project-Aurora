# Decision — shutdown is the bootstrap sequence run backwards, in two phases

**Date:** 2026-08-21
**Status:** LANDED. Builds clean, boots, all three action names resolve. **No exit path GUI-verified.**
**Scope:** `ArctisAurora.EngineWork` (`Shutdown`, `Bootstrapper`),
`ArctisAurora.Core.UISystem.Actions` (`NoteActions`, `WindowActions`),
`ArctisAurora.Core.Registry` (`SettingsRegistry`), `Paths`, `Engine`,
`AuroraEngine/Data/XML/Documents/Shutdown.xml`, and the 21 bootstrap steps.

## What was already true, and wasn't documented

`CLAUDE.md` described the Bootstrapper as `[A_BootstrapStage]` + reflection with undefined ordering,
and the XML rework as planned. **It had already shipped.** `Bootstrapper.Load` reflects over
`[A_XSDActionDependency(name, "Bootstrap")]` statics once and `RunPhase` invokes them in the order
`Bootstrap.xml` lists. Corrected in `CLAUDE.md` as part of this.

Two other findings: `SettingsRegistry.SaveAll` had **zero callers**, so settings were loaded at boot
and never written back; and nothing polls `WindowShouldClose` or registers a GLFW close callback, so
the OS close request is ignored entirely.

## Decisions

### 1. Same machinery as bootstrap, deliberately

`Shutdown.cs` is a structural copy of `Bootstrapper` — phase/step types, a name → method map built
by reflection, `RunPhase` walking declared steps. Not shared code: the two differ in category string,
in `Request`/`Resume` entry points, and in what a failure means, and folding them into one generic
sequencer would have earned a base class for two callers.

### 2. Steps return bool, and a false halts the phase

Applied to shutdown **and** retrofitted to all 21 bootstrap steps (user, 2026-08-21: "now"). The
invoker checks `method.Invoke(...) is false`, which also means a `void` step would still be treated
as success — but nothing is void any more.

Every step currently returns `true` unconditionally. The mechanism is wired end to end; per-step
failure detection is a judgement call per method and was not invented here. Logged as tech debt.

### 3. Two phases, because a prompt cannot live in a commit

`Request` may refuse — it owns anything that asks the user. `Commit` is past the point of no return,
runs against a still-live control tree, and cannot be vetoed.

A single `onAppClose` cannot serve both. Win32 splits `WM_QUERYENDSESSION` from `WM_ENDSESSION`, the
web splits `beforeunload` from `unload`, Qt gives `closeEvent` an `ignore()`. Same reason here.

### 4. Async is re-entry, not suspension

A bool cannot express "not finished yet", and a step cannot block for an answer — the prompt needs
the main loop to keep ticking to draw and take input. So:

```
Notes.SettleUnnamed()
    unnamed = first unnamed edited note across every window
    if none -> return true
    prompt, both answers calling Shutdown.Resume()
    return false
```

Each *attempt* is fully synchronous and bool-valued: steps run in order, each finishes before the
next starts, a false halts the phase. Answering re-runs the sequence, which gets one note further.
Cancelling never resumes, which is what leaves the application open — there is no explicit "cancel"
signal at all.

This is the trick `WindowActions.CloseWhenNamed` already used one level down, lifted to the sequence.

**Rejected: a tri-state `Pending`/`Ready`/`Cancel` polled from `MainTick`.** It composes without
re-entry and avoids nested callbacks, but it is a new idiom in a codebase where every asynchronous
answer is already continuation-passing (`SaveNamed`, `NoteNameWindow.Ask`, `ConfirmWindow.Ask`), and
the user asked for a bool.

**Consequence:** the discarded-notes set has to survive re-entry or the walk finds them forever. It
lives on `NoteActions`, cleared by `Request()` (a fresh gesture) and not by `Resume()`.

### 5. A failed Commit step still lets the application exit

It halts the rest of its phase and logs, but `Engine.CloseWindow(primary)` runs regardless.
Refusal belongs to `Request`; if `Commit` could veto, one deterministically-broken handler would make
the application impossible to quit. **Worth revisiting** — it means a failed save is logged and lost.

### 6. Per-window close keeps its own chain

`Window.Close` on `Engine.primary` goes through `Shutdown.Request()`; any other window calls
`NoteActions.SettleWindow(window, onSettled)`. The settle logic moved out of `WindowActions` into
`NoteActions` and both entry points share `FirstUnnamed`/`SaveEditedIn` — the shutdown step scopes
them to every window, `SettleWindow` to one.

## Known gaps

- Alt+F4 still does nothing. Deliberately left — the decision is its own WiP item, leading option
  being to intercept and ignore it, as Valve's games do (user, 2026-08-21).
- Session restore is not here. A `Commit` step can capture it; the restore side, and who wins between
  a restored layout and the panes `UI.xml` authors, is a separate slice.
- `Engine.CloseWindow(primary)` is still reachable directly and skips the whole sequence.
  `Shutdown.Request()` is the front door by convention only.

Related: [[note-naming-and-text-field]], [[settings-registry]], [[window-chrome-and-label]],
[[vault-browser-and-shell]]
