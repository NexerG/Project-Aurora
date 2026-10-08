# Decision — the app icon is host data under `Icons/app/`, set on the window, the tray and the exe

**Date:** 2026-10-08
**Scope:** `ArctisAurora.EngineWork.Rendering` — `AGlfwWindow.AppIcons`, `AGlfwWindow.SetAppIcon`,
`Background.TrayIcon`; `Thorium/Thorium.csproj` `<ApplicationIcon>`

## What changed
- `AGlfwWindow.AppIcons` loads every `*.png` under `Icons/app/` (through `VirtualFileSystem`) once, into pinned
  RGBA arrays held for the process lifetime. Empty when the host ships none.
- `SetAppIcon` hands all sizes to `glfwSetWindowIcon` from both `CreateWindow` overloads — main, torn-off and
  sticky windows. Ghost and menu windows get none.
- `Background.TrayIcon` builds an `HICON` (`CreateIconIndirect`, 32bpp top-down DIB) from the smallest image at
  least `SM_CXSMICON` wide; falls back to `IDI_APPLICATION` when there are none.
- Thorium ships `Thorium/Data/Icons/app/thorium-{16,24,32,48,64,256}.png` and `Thorium/Thorium.ico` (same six
  sizes, PNG-compressed entries) as `<ApplicationIcon>`.

## Why these choices

**Both a window icon and an exe icon.** The running taskbar button and Alt+Tab use the window's icon
(`WM_SETICON`, which GLFW sends); Explorer and a pinned taskbar button use the exe's embedded icon. One without
the other leaves half the places on the default.
**Convention path, not a setting.** The icon is the host's identity, not a user choice; a `WindowSetting`
attribute would cascade into user scope for nothing. A host with no `Icons/app/` keeps GLFW's default, so
Carbon and AuroraEditor are unaffected.
**Rejected:** GLFW's `GLFW_ICON` resource lookup — .NET's `<ApplicationIcon>` embeds under a numeric id, not
that name.
**Rejected:** loading the tray icon from the exe resource — the resource id .NET's apphost uses is not
something to depend on; the PNGs are already decoded for GLFW.

## The art
- Periodic-table tile: `#1e1f22` rounded-square fill, `#4ade80` inset outline, `Th` with a superscript `90` to
  its right, Segoe UI Semibold. 144-unit master: tile radius 18, outline inset 8, `Th` 64, `90` 26.
- 16 and 24 drop the `90` (illegible). Small sizes use hand-picked integer-aligned 1–2px outlines and type
  sizes rather than a scaled master — see the size table below.
- Rendered offline by a scratch console (ImageSharp.Drawing + Fonts, not a repo dependency); regenerate the
  same way and rebuild the `.ico` from the six PNGs.

| Size | Outline px / inset | `Th` px | `90` px |
|---|---|---|---|
| 16 | 1 / 1 | 9.5 | — |
| 24 | 1 / 1 | 14 | — |
| 32 | 1 / 2 | 14 | 7.5 |
| 48 | 2 / 2.5 | 21 | 10 |
| 64 | 2 / 3.5 | 28 | 12 |
| 256 | 7 / 14.5 | 114 | 46 |
