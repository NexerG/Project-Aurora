# Mistake — a crash reproduced only through synthetic input is not yet an engine defect

**What I did wrong (2026-08-18):** while starting the sizeable-panels work I drove a window resize
with a raw `SetWindowPos(hwnd, NULL, 0, 0, w, h, SWP_NOZORDER | SWP_NOACTIVATE)` from a PowerShell
process. The render thread died with `Failed to create swapchain ErrorOutOfDeviceMemory`. I checked
it against a stashed, unmodified build, saw the same crash, and reported it as a **pre-existing
engine defect that blocks window resizing** — writing it into the WIP list and asking the user to
choose how to handle the blocker.

**Why it was wrong:** the stash test only proved *my changes* were not the cause. It said nothing
about whether the *input* was well formed. A probe eventually showed GLFW handing the window-size
callback `900x65535`: `SetWindowPos` from another process had produced a `WM_SIZE` whose 16-bit
`HIWORD` was `0xFFFF`. Vulkan's `currentExtent` agreed, so the driver really was being asked for a
900x65535 swapchain and really did run out of memory. The error was honest and the engine was fine.

`MoveWindow` resizes it correctly. So does `ShowWindow(SW_MAXIMIZE)`. So does the real feature
(`glfwSetWindowSize` from a pointer drag) — seven consecutive resizes, growing and shrinking, no
crash. The blocker never existed.

**What it cost:** the user answered a question whose premise was false, and two renderer changes were
made against the wrong diagnosis and then reverted. `CLAUDE.md` fences off the Vulkan internals; I
went in on evidence that did not hold.

## Rules

- "The unmodified build does it too" proves **only** that the change is not the cause. It is not
  evidence that the engine is at fault — the harness is also unmodified.
- Before reporting an engine defect found via synthetic input, **reproduce it through a second,
  independent input path**. Here: `MoveWindow` vs `SetWindowPos`, or the app's own chrome.
- Probe the values before naming a cause. Two rounds of reasoning about `RecreateSwapchain` produced
  nothing; one `Console.WriteLine` of the requested extent produced the answer immediately.
- Retract in the same places the claim was made — the WIP list and every decision note that repeated
  it — not only in the newest file.

## Related trap, same session

`Engine.HandleUI` returns immediately while `UICollisionHandling.isInWindow` is false. Synthetic
input that leaves the pointer outside the window makes the **next** drag silently do nothing, which
reads as a broken feature. Move the cursor inside and let a tick pass before driving a test drag.
Two window-resize tests were misread this way before being re-run correctly.

Related: [[verify-what-the-user-sees]], [[window-frame-resize]], [[ui-clipping]]
