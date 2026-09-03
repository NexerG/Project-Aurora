# Decision — the title bar is ordinary controls, and text on a button needed a control that is not an input

**Date:** 2026-08-17
**Status:** LANDED. All three buttons **verified by synthetic clicks**: minimize iconifies the window,
maximize fills the screen and the document reflows, close exits the process with no stderr.
**Scope:** `ArctisAurora.Core.UISystem.Actions` (`WindowActions`), `...Controls.Text`
(`LabelControl`), `Thorium` (`UI.xml`, `VaultBrowserControl`).

## Context

GLFW creates the window with `WindowHint(Decorated, false)` in **every** mode, so there has never
been a native title bar, minimize, maximize or close. The application has to draw its own.

## Decisions

### 1. Three engine actions, and the bar itself is XML

`Window.Minimize`, `Window.MaximizeRestore` and `Window.Close` are `[A_XSDActionDependency]` statics
in the engine; the bar is a horizontal `StackPanel` of `Button`s naming them through `onRelease`
(`onClick` until 2026-08-18 — see [[button-states-and-hover-bubbling]] decision 5). No new control,
no app-side input code — the same shape as `TextInputActions`.

`Window.Close` calls `Engine.engineInstance.Stop()` rather than `Environment.Exit(0)`, which is what
`Thorium.Decorations.ExitApplication` still does. Nothing polls GLFW's `WindowShouldClose`, so the
close flag is not an option. `Stop()` was the risk — it stops three threads synchronised by
`AutoResetEvent` pairs and could have deadlocked instead of exiting; it was tested by clicking, and
the process exits.

### 2. `TextInputControl` silently eats every click, so button captions needed `LabelControl`

**The bug:** `TextInputControl.ResolveOnClick` calls `BeginEdit()`, sets `cursorPosition` and
**returns without calling base** — so `bubbleClick` is dead on it no matter what the XML says. Any
`<Button><TextInput/></Button>` is an unclickable button.

This was not only the title bar. **The vault browser's rows had never worked either** — same
`Button` + `TextInput` caption pattern, so clicking a note did nothing. Landed a day earlier and
recorded as "not GUI-verified"; it was broken.

`LabelControl : TextControl` is text that is drawn and never edited: no-op `BeginEdit`/`CommitEdit`/
`CancelEdit`/`WriteChar`, no `ResolveOnClick` override, and `BubbleAll()` in its constructor on the
same reasoning `GlyphControl` already uses — decoration must never consume input.

**Rejected: making `TextInputControl` call base.** One line, and it restores a contract the override
defeats — but a caption would stay an *input*: it still `BeginEdit()`s on click, and `Text.Write`'s
fallback targets `activeControl as TextControl` when `isEditing`, so clicking a sidebar row and then
typing would edit the row's caption. The right fix for a caption is not to make the input bubble, it
is not to use an input.

**Still open:** the swallow itself is untouched. Anything else nested inside a `TextInput` hits it.

### 3. The bar drags the window, and the drift is the whole algorithm

`TitleBarControl : StackPanelControl` records `InputHandler.mousePos` on click, calls `StartDrag()`,
and each `ResolveDrag` moves the window by however far the pointer has drifted from that grab point.
Moving the window carries the pointer with it, so the drift returns to zero — it converges rather
than running away, and no screen-space cursor query is needed at all.

Raw `InputHandler.mousePos`, **not** the design-space coordinates `ResolveDrag` is handed: the window
moves in screen pixels, and the two only coincide while `WindowControl.autoscaling` is off.

Buttons need no exclusion. `bubbleClick` defaults to false, so a click on a `Button` stops there and
never reaches the bar; the `Label` and the star-width spacer bubble on purpose so the title text and
the empty run of the bar are both grab handles.

This is the first real consumer of the drag lifecycle finished in [[document-selection]] decision 6 —
`dragging` had been declared and never assigned by anything until then.

**Verified by the user, by hand.** A synthetic press-move-release through `mouse_event` did not move
the window and was misleading; dragging works when a person does it.

### 4. Shutdown has one implementation

`Thorium.Decorations.ExitApplication` (Ctrl+Backspace) delegated to `Environment.Exit(0)` and now
calls `WindowActions.Close()`, so the keybind and the title bar's X go through the same
`Engine.Stop()`. Verified by sending Ctrl+Backspace: the log line prints and the process exits.

`ExitApplication2` in the same file was a second `Environment.Exit(0)` referenced by nothing but
commented-out XML, and is **deleted** (user, 2026-08-17). `actionSchema.xsd` drops it on the next
run, so nothing has to be edited by hand.

### 5. Captions are ASCII, because the atlas is

`-`, `[]`, `X`. The imported glyph set is ASCII plus Lithuanian diacritics — no `−`, `□` or `✕`.
A glyph outside the atlas is not a fallback, it is a missing quad.

### 6. Every child of the bar carries an explicit size

The first attempt made the title a `WidthStar="1"` label and the bar came out ~160px tall instead of
32. **`StackPanelControl.Measure` measures a star child in pass 1 with `0` on the main axis**, so an
8-character label wrapped to eight one-character lines, and pass 2 only ever `Max`es `maxCross` — it
never re-bases it against the real width. The stale 168 won.

Dodged rather than fixed: the title is a fixed-width `Label`, the spacer is a `Panel WidthStar="1"`
with an explicit `Height`, and every button names `Width` and `Height`. The engine bug is on the WIP
list.

## Verified

- Builds clean; boots with no stderr.
- **Synthetic clicks through user32** (`SetCursorPos` + `mouse_event`, entering the window from
  outside so GLFW sees a real crossing): minimize → `IsIconic` goes False → True; maximize →
  screenshot shows the window filling 1920x1080 with the document rewrapped; close → the process
  exits.
- Diagnosis was probe-driven: `isInWindow=True`, mouse at the button, `hovering=GlyphControl`,
  `SolveLMBPress` firing, and the bubble chain printing `GlyphControl → parent=TextInputControl` and
  then stopping — which is what identified the override.

## Rounded corners are DWM's, not a `CornerRadius` (2026-08-21)

`AGlfwWindow.RoundCorners()` sets `DWMWA_WINDOW_CORNER_PREFERENCE` (33) to `DWMWCP_ROUND` (2) on the
HWND, taken from `new GlfwNativeWindow(_glfw, handle).Win32!.Value.Hwnd`. Called from all four
creation paths, so the primary window, torn-off windows, the drag ghost and the menu/prompt windows
all round.

**Rejected: a `CornerRadius` on the root `WindowControl`.** The shader discards outside the rounded
rect, so the corner pixels would fall through to the swapchain clear colour — an opaque `0.05` grey
notch, not a rounded window. The framebuffer is not transparent (`GLFW_TRANSPARENT_FRAMEBUFFER` is
never hinted) and making it so is a much larger change. DWM clips at composition instead, which needs
nothing from the renderer.

First `dwmapi` P/Invoke in the codebase; `user32` was already in `DisplayNames`. Windows-only, which
the `net10.0-windows10.0.22621.0` target already is. Windows squares off a maximized window itself,
so no special case is needed for that.

Cost: the corner ~8px are clipped away, so `WindowFrameControl`'s diagonal corner resize grips lose
their outermost pixels. The edge bands either side of each corner still hit.

Also gone the same day: the `EdgeThickness="2" EdgeColorHex="#C42B1E"` left on `UI.xml`'s maximize
button from testing the edge feature.

## The move is Windows', not ours (2026-09-03)

Decision 3's drift algorithm is **gone**. `TitleBarControl.ResolveOnClick` now calls
`AGlfwWindow.DragByCaption`, which is `ReleaseCapture()` plus
`SendMessage(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, 0)`. Windows runs its own modal move loop from
there, so **snap to the top edge, drag-to-restore of a maximized window, the side and quadrant
snaps, the translucent snap preview and window shake all arrive for free.** No code of ours
implements any of them, and none of it can be implemented faithfully — the snap overlay and the
snap zones are not public API.

**Rejected: re-implementing snap by hand** — restore-under-cursor on first movement while
maximized, plus a monitor-work-area top band checked each `ResolveDrag` and a `MaximizeWindow` in
`StopDrag`. It keeps the drag ours and never parks main, but it is an approximation of Windows'
behaviour: no preview rectangle, top-edge only, and it would drift from whatever the OS does next.

### Blocking main turned out not to block anything visible

`SendMessage` does not return until the button comes up, so main parks for the whole drag. That was
the objection to the handoff, and it was **wrong**: `RenderSystem`'s epoch wait is a **startup gate
only**, so after the first tick the render thread draws on its own loop regardless of what main is
doing. The window keeps repainting through the drag.

The one thing main still owes the loop is layout, because a snap resizes the window mid-drag.
GLFW's size callback fires from inside the modal loop and already calls `uiRoot.FitTo`, and
`WriteArrangedTransform` writes **straight into the pool** the render thread reads — no deferred
command queue in between. So `UILayout.ResolveLayout()` + `RefreshWindowRanges()` is the entire
pump, driven by `SetTimer` with a `TIMERPROC`: `DispatchMessage` invokes the callback directly for
`WM_TIMER`, which is how an app animates during size/move without owning the loop.

**The pump must never pump messages.** A nested `PollEvents` — `PeekMessage(PM_REMOVE)` — would
remove the mouse-moves the OS loop is tracking the drag with. No `WndProc` subclass either, for the
same reason: nothing of ours gets between GLFW's proc and the loop.

### Two desyncs the handoff leaves behind, both handled at the call site

- **The button-up is eaten** by the loop that it ends, so GLFW never reports it and `keyTracker`
  would hold `MouseLeft` down forever. `ResolveOnClick` synthesises the release through
  `InputHandler.ProcessMouseClick(..., InputAction.Release, 0)` — the same path the real callback
  takes.
- **`deltaTime` would be the drag's length.** `MainSystem` measures tick to tick and the parked tick
  is one tick, so the next one would be handed multiple seconds — key repeat, the tap window and the
  caret blink all count real seconds off it. New `MainSystem.ResyncClock()` drops the baseline so
  that tick starts from zero.

Cost accepted: one multi-second `MainTick` per drag in the profiler capture.

### Snap is bought by taking a framed window and deleting the frame

The handoff alone gave **drag-to-restore but no snap-to-top**: `SC_MOVE` restores on its own and
needs no styles, but the shell arranges only windows it considers arrangeable. Two things disqualify
GLFW's: `getWindowStyle` adds `WS_MAXIMIZEBOX | WS_THICKFRAME` **only in the `decorated` branch**,
and an undecorated window is `WS_POPUP`, which the shell will not arrange at any style. Adding
`WS_MAXIMIZEBOX` on its own was tried first and **changed nothing** — `WS_POPUP` is the disqualifier.

So `AGlfwWindow.AllowSnapping()` does what Chromium does for a frameless window: make it an
*ordinary* framed window and then delete the frame's geometry.

1. Clear `WS_POPUP`, OR in `WS_CAPTION | WS_THICKFRAME | WS_MAXIMIZEBOX`, `SWP_FRAMECHANGED`.
2. Subclass the WndProc — `SetWindowLongPtr(GWLP_WNDPROC)`, chaining everything else to GLFW's
   through `CallWindowProcW` — and answer **one** message: `WM_NCCALCSIZE` with `wParam == TRUE`
   returns `0`, which makes the client rect the whole window rect. No caption, no border, layout
   byte-for-byte what it was.
3. Same handler, maximized: with no non-client area the window rect overhangs the monitor by the
   frame it no longer has and covers the taskbar, so the proposed rect is replaced with the
   monitor's work area.

Called alongside `RoundCorners()` on the boot window, a torn-off window and a `withChrome` menu
window — the three that draw a `TitleBar`. Not the ghost, not a plain menu.

The subclass is passive and pumps nothing, so the rule above still holds: nothing of ours gets
between the OS move loop and its messages.

GLFW keeps thinking the window is undecorated, and that stays *consistent* — it sizes through
`AdjustWindowRectEx` with a popup style, which adds no padding, and after `WM_NCCALCSIZE` the window
genuinely has none. Nothing re-derives the style afterwards; GLFW only rewrites it on
`glfwSetWindowAttrib`, which nothing calls.

`WM_NCHITTEST` is deliberately not handled — the frame is gone, so there is no non-client band to
hit, and resizing is `WindowFrameControl`'s grips ([[window-frame-resize]]).

## Still open

- **No hover or press feedback** on the buttons — they are the `ButtonControl` default grey
  throughout.
- Resizing landed separately — see [[window-frame-resize]]. Its manual edge grips and this native
  move now coexist; a native snap-resize goes through the same size callback either way.

Related: [[vault-browser-and-shell]], [[named-input-modifiers]]
