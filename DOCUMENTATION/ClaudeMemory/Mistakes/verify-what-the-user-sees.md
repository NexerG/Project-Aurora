# Structural checks are not visual verification

2026-08-07, during the one-measurer rework.

## What happened

Slice 3 replaced the document view with a stack of block controls. Screen capture kept returning
white, so it was verified with a control-tree dump instead: parents, arranged rects, glyph counts,
block spacing, cascaded font sizes. All correct, and it was reported as verified.

The user then looked at the window: **mostly white**. `DocumentCanvasControl` had set
`maskAsset = "invisible"`; the `StackPanelControl` that replaced it did not, and `VulkanControl`
defaults `maskAsset` to `"default"` — a solid quad. A full-viewport white sheet under white text.

The dump could not have caught it. Nothing about the tree, the rects or the counts is wrong when a
control paints a background it should not.

## Rules

- A structural dump verifies **geometry and wiring**. It says nothing about what is drawn. Do not
  report "verified" off one when the change could alter what appears on screen.
- When capture fails, say the visual check did not happen — do not silently substitute a proxy and
  reuse the same word for both.
- New container in a draw path? Check `maskAsset`. Opting out of drawing is per-control and explicit
  (`WindowControl`, `TextControl`, `TextBlockControl`, `DocumentEditorControl`, `DocumentControl` all
  set `"invisible"`); the default is to paint.

## Capturing this app

Screen capture (`CopyFromScreen`) loses to whatever owns the foreground — usually the Claude window
— and `SetForegroundWindow` from a background shell is blocked by Windows' foreground lock.
`PrintWindow` returns blank because the surface is Vulkan-rendered.

What works: `SetWindowPos(hwnd, HWND_TOPMOST, …, 0x43)`, capture, then `HWND_NOTOPMOST`. Synthetic
input for interaction: `SetCursorPos` + `mouse_event`, and `[System.Windows.Forms.SendKeys]` for
characters.

Launch note: `Paths.GetPath` resolves `..\..\..` against the **process working directory**, so the
exe only starts from its own output folder. `dotnet run` from the repo root looks in `C:\Data\`.
