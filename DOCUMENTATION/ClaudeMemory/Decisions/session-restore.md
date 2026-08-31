# Decision — the session is a settings group, and a declared Workspace is where it lands

**Date:** 2026-08-31
**Status:** LANDED and GUI-verified. Two windows across two monitors, main maximized, restored to the
exact rects with the right notes in each; splitter-dragged pane size round-tripped; off-screen rect and
deleted note both handled.
**Scope:** `ArctisAurora.Core.UISystem` (`SessionLayout`, `SessionScope`, `SessionWindow`, `SessionPane`,
`SessionTab`, `ISessionChild`), `ArctisAurora.Core.UISystem.Controls.Containers` (`WorkspaceControl`,
`SplitViewControl.Collapse`, `TabViewControl.TearOff`), `ArctisAurora.EngineWork.Rendering`
(`RenderWindow.uiDocument`, `AGlfwWindow` placement helpers), `Shutdown.shutdown.xml`, `Thorium`
(`Thorium.Main`, `VaultBrowserControl.BuildTab`, `UI.ui.xml`, `TabWindow.ui.xml`, `Workspace.ui.xml`,
`TabPane.ui.xml`, `ThoriumAssets.assets.xml`).

Closes the `session restore` item. Builds on [[render-window-owns-the-swapchain]] and the tear-off work in
`../Context/multi-windowing-plan.md`, and stores through [[settings-registry]].

## Decisions

### 1. A declared `Workspace`, not a subtree restore guesses at

`WorkspaceControl` is a single-child host naming two UI documents: `Default`, built when the session has
nothing for this window, and `Pane`, one empty pane a restored arrangement splits to rebuild itself.

**Rejected: session replaces whatever pane subtree it finds** (the first proposal). Restore would have had
to locate the workspace by "first `TabView` or `SplitView` in DFS", and the authored panes in `UI.ui.xml`
would have been silently discarded — editing them would have stopped having any visible effect with no sign
of why. The declared control says on sight which document is the first-run seed.

**Rejected: the seed authored inline as the workspace's XML children.** It needs no second document, but the
seeded `TabItem`s carry `DocumentEditor Source=`, so every boot with a session would parse two notes into
control trees and immediately destroy them. One control per glyph makes that a real cost, not a theoretical
one.

**`Pane` exists because a restored pane needs the authored chrome.** A pane carries tab metrics, three
colours, `TearOffDocument` and a context menu; a hand-built `TabViewControl` would have none of them.
`SplitViewControl.NewPane` already copies all of it from the pane it splits, so exactly one parsed pane seeds
any arrangement.

### 2. The arrangement is replayed as splits, not rebuilt as a tree

`SessionLayout.Build` walks the record calling `SplitViewControl.Split` once per recorded sibling, then
writes the recorded fixed main-axis size onto each pane. Grips, `paneMinimum`, the first-fixed/second-star
convention and the host re-slotting are all `Split`'s, so none of it is duplicated and none of it can drift.

A recorded `Size` of 0 means star. That is what `SizePane` already writes and what `Width="525"` /
`WidthStar="1"` already produce, so the encoding needed no convention of its own.

Replay is right-nested: N siblings become N-1 nested splits. Only 2 ever occur — `Split` and `Collapse`
produce nothing else — and a hand-authored 3-pane `SplitView` would come back nested rather than flat.

### 3. One recursive record type, so nothing is polymorphic

`SessionPane` holds both `List<SessionTab>` and `List<SessionPane>`: it is a split when it holds panes and a
leaf when it holds tabs. `SettingsRegistry.ApplyInto` and `WriteDiff` both recurse `ChildListFields`
generically and dispatch by element name, so the tree persists with no reader or writer of its own.

**Rejected: a separate `SessionSplit` type.** It needs a polymorphic child slot, which the schema generator
expresses only through a marker interface anyway — and then two types where one does.

**Rejected: its own `Session.xml`, hand-parsed like `Bootstrap.xml`.** It was the first proposal, on the
grounds that a session is infrastructure rather than settings. Once the encoding stopped being polymorphic
the settings path cost nothing: no new file, no new write root, and the XSD comes out of `XSDGenerator` free.
`KnownVaults` had already set the precedent of app state living in that file.

### 4. The scope key is opaque to the engine

`SessionLayout.scope` is a static string the host sets; `SessionScope` groups windows under it and the engine
never interprets it. Thorium sets it to the resolved vault path, so a layout belongs to the vault it was
arranged in (user, 2026-08-31).

The engine has no notion of a vault and should not gain one for this. A host wanting one application-wide
session simply leaves the default empty key.

### 5. Placement is the restore rect, read from the OS

`AGlfwWindow.GetPlacement` returns GLFW's position and size for an ordinary window, and Win32
`GetWindowPlacement`'s `rcNormalPosition` for a maximized one — GLFW reports the *maximized* rect while
maximized, so saving that would lose where the window goes back to. `rcNormalPosition` is in workspace
coordinates, corrected by the work-area origin from `GetMonitorInfo`, which is non-zero only for a taskbar
docked top or left.

**A rect that lands on no monitor is centred on the primary one** (user, 2026-08-31), at the size it had.
`RectOnAnyMonitor` intersects against every `GetMonitorWorkarea`.

**No monitor name is recorded.** The first proposal had one as the fallback anchor; once the fallback became
"the primary screen" unconditionally, nothing read it. Absolute virtual-desktop coordinates already express
which screen a window was on for as long as the arrangement is unchanged.

### 6. Capture is a `Commit` step, before `Settings.SaveAll`

`Session.Capture` runs against a live tree, which is the phase's whole contract, and must precede the write
that persists it. It records every window whose `uiDocument` is set — the field is assigned only where a
window is built from a document, so menus and the drag preview are excluded without a second flag.

A tab whose editor never loaded a file is left out, and `Active` is resolved against the *recorded* index, so
a note deleted between sessions drops out without shifting which tab comes back on top.

## The `Collapse` ordering defect this exposed

`SplitViewControl.Split` detaches the source from its host before the new split arrives, and says so in a
comment: a single-child host is never asked to hold both at once. `Collapse` did not — it called
`survivor.SetParent(host)` while the split was still a child of the host. Every host that had ever existed
was a `StackPanel`, which tolerates the transient second child.

`WorkspaceControl` is the first single-child host in the codebase, so tearing the last tab out of a pane threw
`Plain VulkanControl supports only one child`. `Collapse` now removes the split from the host first, which is
the symmetry `Split` already documented. Reach beyond this work: any future single-child host would have hit
it.

## Known gaps

- **The primary window's rect is applied after `Engine.Init` returns.** `SettingsRegistry.LoadAll` is the
  first bootstrap step, so nothing can read the session before `InitWindowing` creates the window — it is
  visible at the `GraphicsSettings` size for the ~800ms of bootstrap and then jumps. Fixing it means either
  creating the primary hidden or sourcing the scope key from somewhere available before `LoadAll`.
- **Switching vaults does not capture the vault being left.** `VaultsWindow.Switch` closes every tab and
  writes the setting; the outgoing vault's arrangement is only ever captured at shutdown, so switching away
  and quitting elsewhere loses it. The fix is a `SessionLayout.Capture()` in `Switch` before `CloseTabs`,
  against the old scope.
- **`Switch` only closes tabs in `Engine.primary`** — pre-existing, and it now also means a torn-off window
  keeps notes from the vault that was left, which capture will then record under the new vault's key.
- Caret position, scroll offset and selection are not recorded — only the ordered tab paths and the active one.
- An iconified window restores normal; only maximized is carried.
- A window whose every recorded note is gone restores as an empty pane rather than closing.

Related: [[settings-registry]], [[settings-categories]], [[render-window-owns-the-swapchain]],
[[tab-view-control]], [[splitter-and-pane-sizing]], [[vault-list-and-switching]], [[shutdown-sequence]],
[[ui-document-registry]]
