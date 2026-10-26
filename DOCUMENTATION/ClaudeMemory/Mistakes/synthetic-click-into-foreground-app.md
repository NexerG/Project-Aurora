# Mistake — synthetic live input went into whatever application was in front

**Date:** 2026-10-23
**Scope:** live-input probe scripts (`SetCursorPos`, `mouse_event`) driving Thorium

## What went wrong
- A `mouse_event` click at screen (51,13), meant for Thorium, landed in the user's game, because Thorium was behind it.
- Synthetic input goes to the window under the screen point, not to the app under test.

## Rule
- Before every synthetic click, check that `WindowFromPoint` at the target belongs to the app under test. The probe script now aborts otherwise.
- Raise the app first with `SetWindowPos(HWND_TOPMOST, SWP_NOMOVE|SWP_NOSIZE)`. Never assume the app is in front.
- Compute click points only after the `menu-open` slide (0.16 s) has finished; a point taken mid-slide misses the row.

Related: [[synthetic-input-false-defect]], [[verify-what-the-user-sees]], [[context-menus]]
