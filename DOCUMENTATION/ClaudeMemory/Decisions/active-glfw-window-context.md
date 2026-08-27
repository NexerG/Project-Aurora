# Decision — the active window is a GLFW focus latch, and a drag raises what it hovers

**Date:** 2026-08-27
**Status:** LANDED. `ArctisAurora.sln` (ArctisAurora, Periodic, AuroraEditor, _Build) builds clean —
0 errors, no warning in any touched file. **NOTHING here is GUI-verified** — no drag, no alt-tab, no
raise has been walked.
**Scope:** `ArctisAurora.EngineWork.Rendering` (`AGlfwWindow`, `RenderWindow`),
`ArctisAurora.EngineWork` (`Engine`), `ArctisAurora.Core.UISystem` (`UICollisionHandling`,
`NoteNameWindow`, `SettingsWindow`).

## The problem

`UICollisionHandling.activeControl` was the only "where does input go" state, and exactly one event
moved it: a left press inside a window, in `SolveLMBPress`. Anything that moves OS focus without a
click — alt-tab, or the five explicit `os.Focus()` calls in `RenderWindow.Focus`, `ConfirmWindow`,
`NoteNameWindow`, `SettingsWindow`, `WindowedContextMenuControl` — left it pointing into the previous
window's tree. The WIP list recorded the cause as "no GLFW focus callback exists anywhere"
(2026-08-23); that is what this closes.

Separately, `WindowAt` resolved overlapping windows by `Engine.windows` map order, because GLFW
publishes no z-order. That is the drop-target lookup for a cross-window drag.

## What GLFW actually offers

| Wanted | GLFW | Verdict |
|---|---|---|
| stacking order | — | does not exist; no window enumeration either |
| has input focus | `GLFW_FOCUSED`, `SetWindowFocusCallback` | what the context is built on |
| cursor over content, nothing above it | `GLFW_HOVERED` | z-order aware, but only along the cursor ray |
| raise **and** activate | `glfwFocusWindow` | `BringWindowToTop` + `SetForegroundWindow` + `SetFocus` |
| raise **without** activating | — | Win32 `SetWindowPos` + `SWP_NOACTIVATE` |

`GLFW_HOVERED` on Win32 is a live `GetCursorPos` + `WindowFromPoint` + client-rect test, so it *is* a
real z-order query — and it is useless here anyway: `DragGhost.Follow` centres the ghost on the
pointer, so `WindowFromPoint` returns the ghost for the whole drag. That is why `WindowAt` is a
manual rect loop that skips `isGhost`.

## Decisions

### 1. `ActiveGLFWWindow` is a latch, not a mirror

`[A_ActiveContext("ActiveGLFWWindow")] UICollisionHandling.activeGlfwWindow`. Set on
`focused == true`; **`focused == false` does nothing**. Alt-tabbing to another application fires
`false` on every window, so mirroring the attribute would null the context whenever the user leaves
the app, and returning would need a fresh click to restore anything reading it.

Named `ActiveGLFWWindow` and not `ActiveWindow` (user, 2026-08-27) to leave "top window" free as a
separate concept later.

### 2. Activable is opt-in, and the creation path sets it

`RenderWindow.isActivable`, default `false`. `Engine.InitWindowing` and `Engine.OpenWindow` set it
true, so the primary window and a torn-off tab are activable for free — `TabViewControl.TearOff`
goes through `OpenWindow`, so the tear-off site needed no code. `OpenMenuWindow` windows are not
activable; `NoteNameWindow` and `SettingsWindow` opt back in by assignment because they hold text
fields.

**This is the whole point of the flag.** `CreateMenuWindow` takes focus deliberately ("so a dismissal
has something to leave"). Without it, opening a context menu would move the active window off the
window the menu acts on — the same failure `MenuButtonControl.takesActiveControl => false` exists to
prevent one level down. `ConfirmWindow` and `WindowedContextMenuControl` stay false.

`OpenGhostWindow` never calls `WireInput`, so the ghost is excluded with no check anywhere.

### 3. The callback records, the tick publishes

`RenderWindow.FocusChanged` writes `UICollisionHandling.pendingActiveGlfwWindow` and nothing else.
`Engine.MainTick` calls `ApplyPendingFocus()` after `ReapClosedWindows()` and before the `HandleUI`
loop.

**Rejected: `Context.Set` straight from the callback.** That runs `OnContextAdded`/`OnContextRemoved`
on controls inside `glfwPollEvents` and before the reap, so a handler destroying a control or window
would do it during GLFW's own event dispatch. Every other callback here already defers —
`inputWriteQueue`, `charInputWriteQueue`, `scrollDeltaWrite`, `MouseCrossedBorder`.

No `IContext` notify pair on the set: `RenderWindow` does not implement `IContext`, so the
`(x as IContext)?.OnContextAdded` dance the control contexts carry would be dead code. `Context.Set`
still calls `Derive`, which is a no-op here — `Ancestor` casts to `Entity` and `RenderWindow` is not
one.

### 4. `activeControl` is untouched

Focus does **not** move the active control. See §2: the menu and prompt windows would clobber it.
The context is a fact consumers read. Wiring "typing goes to the active window's tree" is a separate
decision, and it needs the menu exemption designed in.

### 5. `WindowAt` prefers the active window, then falls back to map order

A partial fix, knowingly (user picked it over leaving `WindowAt` alone, 2026-08-27). It resolves "the
drag's source window overlaps another", which is the common case because the source was just clicked
and is therefore focused. It does nothing for two *unfocused* overlapping windows.

The real answer when it is wanted: give the ghost `WS_EX_TRANSPARENT` so `WindowFromPoint` skips it,
then hit-test through Win32 — the HWND is already pulled for the DWM corner call.

### 6. A drag raises without activating, and assigns the context directly

`UpdateDropHint` already resolves the window every tick, so `HitFor` gained an `out RenderWindow` and
`RaiseHovered` hangs off it, latched on `raised` so a raise costs one call per crossing rather than
one per frame. `ClearDropHint` clears the latch and both drag-end paths already call it.

**`AGlfwWindow.Raise` is `SetWindowPos(HWND_TOP, SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE)`, not
`os.Focus()`.** GLFW's focus call moves the foreground, and this drag is built entirely on the source
window keeping the pointer capture — `Engine.HandleUI`, `DragGhost.Follow` and `HitFor` each say so
in their own comments. Moving the foreground to another window is very likely to drop that capture,
which would freeze the ghost and resolve the drop against a stale point. **Reasoned, not measured** —
a five-minute empirical check nobody has run.

Raising without activating fires no focus callback, so the context cannot follow the raise on its own.
User's call (2026-08-27): `RaiseHovered` assigns it directly through `SetActiveGlfwWindow`. The
alternative — raise only, leave the context — makes §5 disagree with itself, because the tiebreak
would keep preferring the drag source over the window just raised under the pointer.

**Consequence:** `ActiveGLFWWindow` means "the window input belongs to", not strictly "the
GLFW-focused window". A drag-hover moves it with no OS focus change behind it.

### 7. Closing a window clears it

`ReapClosedWindows` calls `Context.Forget(window)` before `Unpublish`. Recovery is natural — a prompt
closing calls `_source?.os.Focus()`, which re-latches.

## Still open

- **Not GUI-verified.** The capture hazard in §6, the raise itself, and every latch above are
  reasoned, not observed.
- `WindowAt` skips `isGhost` only, so a non-activable menu window is still a drop target.
- Nothing consumes `ActiveGLFWWindow` yet. It is written and readable; no call site reads it.
- GLFW callback delegates are handed to native code with nothing rooting them — `WireInput`'s six,
  and now the focus one. Pre-existing across every callback there, not introduced here, but it is a
  latent collect-then-crash.
- The `Text.Write` stale-release bug (WIP list) is *not* fixed by this. A focus callback now exists,
  so a fix has something to hang off, but nothing here touches key state.
