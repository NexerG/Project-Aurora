---
name: aurora-debug
description: Find out why Aurora crashed — from evidence, not a guess. Use on any crash, unhandled exception, `FATAL [Crash]` or `[Threading] … died on frame N` line, a --test run that dies mid-suite (exit 127, results line missing), an intermittent failure, and whenever asked "why did X crash / get destroyed / go null". Covers instrumenting the call line-up with temporary logs, numbering loop iterations to find the one that fails, reproducing intermittent crashes, and removing every trace afterwards.
---

# Debugging a crash

**A hypothesis is not a cause.** Code reading gives candidates; the cause is the one a log line shows happening.
Say "best explanation, not proven" until a run has printed it.

## 1. Read what the crash already says

```bash
grep -an 'Unhandled\|\[Crash\]\|died on frame' run.txt | head
grep -a -A25 -m1 '\[Crash\]' run.txt | grep -a ' at ' | head -20    # the line-up, innermost first
grep -aE '\[Test\]' run.txt | tail -3                               # which test was running
```

- `[Threading] <Step> died on frame N` names the frame step and frame. The same frame across runs means the crash
  sits at a fixed point of the script; the cause may be frames earlier.
- `DataPool.GetRef` → `IndexOutOfRangeException` is a **freed row**: `_slots[id]` is `-1` once a destroyed
  control's row is freed at the frame edge. Someone holds a reference to a destroyed control. The question is then
  who destroyed it and who kept the reference.
- A destroyed control is dropped from the active/hover contexts by `UIEngine.Forget` **without `onBlur`** —
  anything that cleans up on blur does not run when its control is destroyed.

## 2. Instrument the line-up

Temporary lines, each ending `// TEMP-DEBUG` so they can be found and removed. Not `// DEBUG` — `DataPool` and
`LayoutEngine` carry permanent `// DEBUG` comments that a cleanup grep would hit:

- **next to the crash** — the state the crashing line reads, one line before it;
- **at each step of the line-up** — every frame of the stack trace that is our code;
- **along the object's lifecycle** — where it is created, closed/destroyed, and read. A use-after-destroy is
  solved at the destroy site, not the crash site; log the caller there (`Environment.StackTrace`, trimmed).

Each line prints identity and state: `GetHashCode()`, the flags the code branches on, `destroyed` (internal to
`Entity`, readable inside the engine), the frame number when timing matters.

**In a loop**, print the index and the item's key data so the failing iteration names itself:

```csharp
for (int i = 0; i < items.Count; i++)
{
    Console.WriteLine($"DEBUG {i} {items[i].GetHashCode()} {items[i].name}"); // TEMP-DEBUG
    ...
}
```

Then dump that item's data in full — the index says *which*, the data says *why*.

`Console.WriteLine` is allowed here and only here: these lines never ship, and stdout lands in the `--test`
redirect file next to the crash. Everything else keeps the logging rules (`LogChannel`, no per-frame logs).

## 3. Reproduce

- A crash in a test: `--test=<Suite>` with output redirected; grep `DEBUG` and the crash together, in order:
  `grep -anE 'DEBUG|\[Crash\]|\[Test\]' run.txt`.
- **Intermittent:** loop runs to the first crash and record the rate (`k of n`). Compare with the same loop on the
  commit before the change (`git stash -u`, build, run, restore) before blaming the change. Test runs rewrite
  `Thorium/Data/Icons/default/default.import.xml`; `git checkout` it before `git stash pop` or the pop aborts.
- Cap the loop (about 10). No crash in the cap → report that, not a guess. The instrumented lines still show the
  path a clean run takes; read them before removing them.
- **Crashes only when you were in another app** (Claude or a browser on top while `--test` ran) point at OS focus:
  `ContextMenus.Tick` closes every menu when no app window is focused, and `--test` does not gate it. A window
  launched from the shell gets the foreground even with another window on top, so reproduce with a thief that
  waits for the app's window and then takes the foreground with the Alt-key workaround:

  ```powershell
  # thief.ps1 -targetPid <pid>: a topmost form that re-takes the foreground every 50 ms once the target has a window
  param([int]$targetPid)
  Add-Type -AssemblyName System.Windows.Forms
  Add-Type @"
  using System; using System.Runtime.InteropServices;
  public static class Fg {
      [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
      [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
      [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
      public static void Take(IntPtr h) { keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); SetForegroundWindow(h); }
  }
  "@
  $f = New-Object System.Windows.Forms.Form; $f.TopMost = $true; $f.Text = 'focus-thief'
  $t = New-Object System.Windows.Forms.Timer; $t.Interval = 50
  $t.Add_Tick({ $p = Get-Process -Id $targetPid -ErrorAction SilentlyContinue
      if ($p -and $p.MainWindowHandle -ne [IntPtr]::Zero -and [Fg]::GetForegroundWindow() -ne $f.Handle) { [Fg]::Take($f.Handle) } })
  $f.Add_Shown({ $t.Start() }); [void]$f.ShowDialog()
  ```

  Keep it in the scratchpad, not the repo. Start the app first, then the thief with the app's pid, wait for the
  app to exit, stop the thief. A thief started *before* the app, or one that only calls `Activate()`, loses the
  foreground to the app and proves nothing — log the focus state (`GetWindowAttrib(…, Focused)`) to confirm the
  window really was `unfocused`.
- Build before the loop and stop on failure — a loop over a failed build runs the old binary and proves nothing.
- A pointer (`WindowHandle*`) has no `GetHashCode`; print `(nint)ptr`.

## 4. Conclude, then clean up

- Report the log lines that show the cause, then the fix as a plan (CLAUDE.md §2) — instrumenting was approved,
  fixing was not.
- Remove every temporary line: `grep -rn 'TEMP-DEBUG' --include=*.cs .` must print nothing, and the build must
  succeed, before anything is reported as done or committed. Commits take the whole tree (§9), so a forgotten
  line ships.
