# Decision — a context menu is a document a control names, and it is hosted wherever it fits

**Date:** 2026-09-11
**Scope:** `ArctisAurora.Core.UI` — `NextContextMenus`, `NextContextMenuControl` (+ nested `Row`), `ContextMenu`,
`ContextMenuEntry` / `ContextMenuButton` / `ContextMenuLine` / `ContextMenuSubmenu`, `Control` (`contextMenu`,
`stopsContextMenu`, `ParseMenu`, `RecursiveParse`), `UIEngine` (`SolvePress`, `SolveRelease`), `NextButtonControl`;
`ArctisAurora.Core.Registry.Assets.ContextMenuAsset`; `Core.UISystem.Actions.WindowActions.Acting`; `Engine.MainTick`.
2026-09-12 (landing 6b3): `NextMenuButtonControl`, `NextDropdownControl`, `NextTabViewControl.tabContextMenu`,
`Core.UISystem.Actions.TabActions`, `ViewActions`, `UIActions.Invoking`

Designed fresh from the user's spec (2026-09-11) — the old stack's `ContextMenus` was deliberately not read or
ported. It stays for the old stack until 6d deletes it.

## What changed
- **A menu is its own document** — `*.menu.xml`, root `<NextContextMenu>`, children `<NextContextButton Text Action>`,
  `<NextContextLine/>`, `<NextContextSubmenu Text>` (nesting entries). Registered as `ContextMenuAsset` in a new
  `contextMenus` registry dictionary (`Registry.registry.xml`), listed in a host's `*.assets.xml`.
- **A control names one menu** — `ContextMenu="name"` (string) and `StopsContextMenu="true"` on `Control`.
- **Code builds a `ContextMenu` and `NextContextMenus.Register(name, menu)`s it**; `Get(name)` checks registered
  menus first, then parses the document on first use and caches it. An unknown name logs a `Warn` and offers nothing.
- `Control.ParseMenu(path)` parses a menu document; `RecursiveParse` now takes an `object` owner and recurses into
  non-control elements, which is what lets a submenu carry entries. Existing non-control elements (grid
  definitions) are self-closing, so nothing else changed.
- **Right-button release** (`UIEngine.SolveRelease`) calls `NextContextMenus.OpenOn(hovering, point)`: `Collect`
  walks up from the hovered control appending each named menu's entries, a line between groups, stopping after
  the first control with `stopsContextMenu`. Nothing collected → no menu.
- **Hosting** (`Host`): the panel is measured at the pointer in the origin window's design space. Fits entirely →
  appended as the **last child of the origin `WindowRoot`**. Out by a pixel → `Engine.OpenMenuWindow` with its own
  `WindowRoot`, placed at the pointer's screen position.
- **Submenus** open on row hover, beside the row, top-aligned with it; hovering another row of the parent closes
  them. Each is hosted by the same rule, always tested against the origin window.
- **Closing**: a clicked button row runs its `action`, then everything closes; any press not on a panel closes and
  passes through (`SolvePress` → `DismissUnlessInside`); `NextContextMenus.Tick` (from `Engine.MainTick`) closes
  when no application window has focus.
- `NextContextMenus.target` is the control the menu was opened on, set before an action runs and cleared on close.
  `WindowActions.Acting` reads it first.
- Thorium's `NextTitleBar` names `title-bar` (`Menus/TitleBar.menu.xml`: Minimize, Maximize, line, Close) and stops.
- **Found and fixed on the way (user, 2026-09-11):** `DataPool.Allocate` now clears the new row (see
  [[ecs-rework-data-pools]]); `NextButtonControl` runs `onRelease` for the left button only, so a right-click on a
  button opens its menu instead of firing it — a menu `Row` still fires on either button through its own override;
  the `Latin` charset gained `›` (U+203A) for the submenu arrow, re-baking Thorium's and Carbon's fonts.

## Why these choices

**Menus are separate documents, not child elements of the control (user, 2026-09-11).**
"UI builds the UI" — a `*.ui.xml` describes the tree, and a menu written inside a control's element mixes a second
concern into it. Rejected: entries authored inline, which the parser could already route into a `List<>` field on
the control. The cost of the choice is one indirection (a name) and one registry dictionary.

**A control holds a name, not a menu object.**
A `ContextMenu`-typed member with a `TypeConverter` from a name was the alternative. The XSD generator treats any
`[A_XSDType]`-tagged class member as a nested element, not an attribute, so the schema would reject
`ContextMenu="x"`. A string is an `xs:string` attribute, and code reaches the same lookup through `Register`.

**Entries are data; the panel is built on open.**
A menu authored as controls would be attached, measured and drawn wherever it was parsed. As data it is parsed once,
shared by every control that names it, and turned into controls only while open. Merging several controls' groups is
a list append rather than reparenting subtrees.

**In-window hosting is the last child of the root — no overlay layer.**
The draw list is DFS pre-order and the hit-test walks children last to first, so the last child of the root is
drawn on top and hit first. That is exactly "drawn last" with zero new engine surface. The panel overrides `Arrange`
to sit at its own `position`, ignoring the box `WindowRoot` offers. Cost: `WindowRoot.AddChild`/`RemoveChild`
invalidate the root, so opening or closing an in-window panel re-lays out the whole window — the cost of a resize.
When documents land on the new stack this may want a separate overlay list collected after the root.

**Each windowed panel gets a uniquely named window.**
`Engine.Publish` keys windows by name. Reusing a name while the previous window is `closeRequested` but not yet
reaped overwrites its entry, and `ReapClosedWindows` then never finds it — an orphaned OS window.

**Every panel is positioned and fit-tested in the origin window's design space.**
A windowed panel keeps its logical `position` there and arranges at 0,0 in its own window; a submenu's position is
its parent's plus the row offset, so the rule is one formula whether the parent is in-window or not. A submenu of a
windowed menu can therefore land back inside the origin window.

**Opens on right-button release, whatever the release handlers returned.**
`NextButtonControl` consumes every release, so "consumed suppresses the menu" would mean no button could offer one.

**Focus loss is a per-tick question, not the focus callback.**
When a submenu window takes focus, its parent's window loses it; at callback time nothing says where focus went.
`Tick` asks GLFW whether *any* application window is focused, so focus moving between menu windows keeps the chain
open and focus leaving the application closes it.

**`target` is a plain static, not an `[A_ActiveContext]`.**
Nothing keybinds against it; actions read it. Without it `WindowActions.Acting` resolved the window of `hovering` —
the menu's own window when the menu is windowed.

## Menu bar, tab and view menus — 2026-09-12, landing 6b3

- `NextMenuButtonControl` `<NextMenuButton>`: a left press opens only the menu it names (`Get(contextMenu)` →
  `Open`) at its bottom-left edge; `takesActiveControl => false`. Its caption is an authored `<NextLabel>` child.
- Thorium's `thorium` / `file` / `edit` and the engine's `view` / `tab` are `*.menu.xml`. The engine's are registered
  in `EngineAssets.assets.xml`, which every host's file system mounts below its own.
- `NextTabViewControl.tabContextMenu` (`TabContextMenu`, default `tab`) names each strip button's menu, and every
  strip button sets `stopsContextMenu`. `NextSplitViewControl.NewPane` carries `tabContextMenu` and `contextMenu`
  onto a split-off pane.
- `TabActions`' Close / CloseOthers / CloseRight / SplitRight / SplitDown take no argument: the
  `NextTabStripButtonControl` at or above `target`, else the old `ContextMenus.invoker`. `ViewActions` looks for a
  view above `target`, `UIEngine.activeControl`, `UIEngine.hovering` in turn. `UIActions.Invoking` resolves the new
  stack's window first. Each keeps the old stack's path as a fallback until 6d.
- `NextDropdownControl` builds `ContextMenuButton(text, action)` entries in code and opens them under itself.

**The tab actions lost their parameter because the new format binds zero-argument `Action`s only.**
`ResolveAttributes` makes an `Action` delegate by name, which throws on a `(VulkanControl)` method. The old binder
already runs a zero-argument action inside `InvokeFor` with `invoker` set to the control it used to pass, so the old
tab menu behaves the same. Rejected: a same-named zero-argument overload — `TaggedActions` resolves names with
`FirstOrDefault`, so which of the two binds would be reflection order.

**`ViewActions` tries each candidate rather than the first non-null.**
View ▸ Split from the title bar has the menu button as `target`, with no view above it; stopping there did nothing.
Falling through to the active control is the reason the menu button leaves it alone.

**A menu button drops its own menu, not the right-click walk (user).**
`Collect` from a title-bar button would append the title bar's `title-bar` group to every menu.

**Tab buttons stop the walk (user).**
Without it a tab's menu carried the view's group after a line, so Split right and Split down appeared twice —
once for the tab under the pointer and once for the view's active tab.

**`EnabledWhen` is not on the new format (user: skip).**
`Tab.HasSiblings` / `HasRight` / `CanTearOff` stay old-only and every new row is enabled. `SplitOff` already refuses
a view's only tab and the close variants find nothing to close, so greying would only be cosmetic.

## Known gaps
- No flipping or clamping to the monitor — a windowed menu near a screen edge goes off-screen.
- A windowed panel renders 1:1 even when the origin window autoscales; only its position is converted.
- No Escape, no keyboard navigation, no hover delay for a diagonal move into a submenu.
- A bad `Action` name in a menu document throws at the first open, not at boot — the document is parsed lazily.
- Each window opened after boot logs the pre-existing `DemoteToHelperInvocation` validation error once more.
- The Editor's and the engine's own font copies were already stale (importer v1 and v3) and were not re-baked.
- A menu button's second press re-opens its menu — `SolvePress` dismisses it, then the press opens it again.
- New note, Vaults, Settings, Save note, Undo and Redo run old-stack actions, which draw nothing since `UIModule` left
  the module list. They come alive at 6c and 6c2; Exit works.

## Verified
GUI-verified in Thorium 2026-09-11, synthetic input + topmost capture:
- in-window at the pointer over the title bar, row hover tint, Maximize from a row maximizes the main window and
  restores it on the second use, the panel is removed after the action;
- walk and stop: a title-bar button's own menu, an automatic line, then the title bar's group, and none of the root's;
- a press outside closes the menu;
- near the bottom-right corner the menu is its own 112×100 window at the pointer, past the main window's edge; its
  submenu and a third level open as further windows beside their rows while the parent stays open; Maximize from
  the third level maximizes the **main** window and all three menu windows are reaped;
- Alt+Tab away closes a windowed menu (no press involved, so `Tick` is the only path that can have closed it).
- after the three fixes: closing one menu and opening another, and cycling a submenu three times, render cleanly
  where they had wrapped a character per line; `›` draws flush right; right-clicking Close opens the title-bar menu
  (windowed, at the edge) and the application keeps running; a left click on Close still quits.
- Boot error set unchanged (18); no exceptions across four Thorium runs.

Landing 6b3, GUI-verified in Thorium 2026-09-12, synthetic input + topmost capture:
- Thorium, File, Edit and View each drop their own entries directly under the button; pressing another swaps menus;
- a right click on a tab opens the `tab` group; Tab ▸ Close closed the tab it was opened on (its view's only tab —
  the emptied view collapsed).
**NOT GUI-verified:** the tab buttons' stop (added after that run, which still showed the view's group); every split
path — Tab ▸ Split, View ▸ Split, the Ctrl+\ keybinds — and Close on an inactive tab, all of which need two tabs
in one view; `NextDropdown`'s list; `UIActions.Invoking`, which has no new-stack caller yet.

Related: [[ui-engine-stack]], [[ui-draw-list]], [[ui-document-registry]], [[context-menu-hosting]] and
[[context-menu-invoker]] (the old stack's)
