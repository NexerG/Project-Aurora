# Decision — the title bar is ordinary controls, and text on a button needed a control that is not an input

**Date:** 2026-08-17
**Status:** LANDED. All three buttons **verified by synthetic clicks**: minimize iconifies the window,
maximize fills the screen and the document reflows, close exits the process with no stderr.
**Scope:** `ArctisAurora.Core.UISystem.Actions` (`WindowActions`), `...Controls.Text`
(`LabelControl`), `Periodic` (`UI.xml`, `VaultBrowserControl`).

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
`Periodic.Decorations.ExitApplication` still does. Nothing polls GLFW's `WindowShouldClose`, so the
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
moves in screen pixels, and the two only coincide outside `WindowingMode.ScaleUp`.

Buttons need no exclusion. `bubbleClick` defaults to false, so a click on a `Button` stops there and
never reaches the bar; the `Label` and the star-width spacer bubble on purpose so the title text and
the empty run of the bar are both grab handles.

This is the first real consumer of the drag lifecycle finished in [[document-selection]] decision 6 —
`dragging` had been declared and never assigned by anything until then.

**Verified by the user, by hand.** A synthetic press-move-release through `mouse_event` did not move
the window and was misleading; dragging works when a person does it.

### 4. Shutdown has one implementation

`Periodic.Decorations.ExitApplication` (Ctrl+Backspace) delegated to `Environment.Exit(0)` and now
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

## Still open

- **No resizing.** `ResizeableControl` is still dead and the window is undecorated, so there are no
  resize edges. Only maximize/restore changes the size.
- **Dragging a maximized window moves it while still maximized** instead of restoring it first, which
  is what every OS does. Two lines in `ResolveOnClick` whenever it is worth having.
- No hover or press feedback on the buttons — they are the `ButtonControl` default grey throughout.

Related: [[vault-browser-and-shell]], [[named-input-modifiers]]
