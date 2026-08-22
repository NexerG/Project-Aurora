# Decision — a double click is dispatched from the release, and a renaming strip is its own control

**Date:** 2026-08-21
**Status:** LANDED. Builds clean and boots; **not GUI-verified.**
**Scope:** `ArctisAurora.Core.UISystem` (`UICollisionHandling`), `ArctisAurora.EngineWork` (`Engine`),
`ArctisAurora.Core.UISystem.Controls.Containers` (`TabViewControl`, `TabItemControl`,
`SplitViewControl`, `EditableTabsControl`), `Periodic` (`VaultBrowserControl`, `UI.xml`,
`TabWindow.xml`).

## What shipped

Double-clicking a tab turns its caption into a field with the name selected. Enter renames the note on
disk, rewrites the `Name` inside it, repaths and recaptions every tab holding that note open, and
rebuilds the vault browser — the same operation the browser's `Rename note` entry runs. Escape
restores, clicking away commits.

`VulkanControl.ResolveOnDoubleClick` and `bubbleDoubleClick` had existed with **zero callers** since
they were written; this is the first thing to dispatch either.

## Decisions

### 1. The dispatch fires on the release, and only when it lands where the press did

`SolveLMBRelease` gained a `tapCount` parameter — `Engine.HandleUI` already holds the `KeyStateEntry`
and is the single caller. The double click resolves on `hovering` and bubbles, after whichever of the
drag or release branches ran, guarded by `ActiveTarget(hovering) == activeControl`: the same
"released over what was pressed" test the ordinary release already applies.

**Rejected: firing on the second press.** It reads cleanly — `justPressed` with `tapCount == 2` needs
no guard at all — but buttons in this UI activate on release by an explicit decision (user,
2026-08-18), and a gesture that opens an editing field while the button is still down is the same
complaint that decision was about. The cost of choosing the release is that
`TabStripButtonControl.ResolveOnClick` has already called `StartDrag()`, so holding and moving after
the second click drags the tab with the field open.

### 2. Identity is tracked next to the count, because `tapCount` is per key

`KeyStateTracker` counts taps against `Input.DoubleClick.Timeout` and knows nothing about controls, so
two fast clicks on two *different* tabs arrive as `tapCount == 2` and would have renamed the second
one. `UICollisionHandling` remembers `lastPressTarget` and sets `sameTargetTap` at each press.

Two fields rather than one: the timeout carries "fast enough" and the target carries "same place", and
neither substitutes for the other — the tracker resets `tapCount` on its own schedule, which is what
stops a stale `lastPressTarget` from pairing two clicks minutes apart. `Forget` clears it with the
other contexts; the field is only ever compared by reference, so a destroyed control left there would
not crash, but leaving one context field out of the funnel is how 2026-08-19's destroyed-control bug
happened.

### 3. `== 2`, not `>= 2`

`tapCount` keeps climbing while the taps stay inside the window, so `>= 2` fires a second double click
on the third tap of a triple. Exact equality means one gesture per pair.

### 4. Renaming is a separate control, not a flag on `TabViewControl`

`EditableTabsControl : TabViewControl` (`<EditableTabs>` in XML), on the user's call. The base gained
one hook, `BuildCaption(item, tab)`, returning today's `LabelControl` unchanged.

**The hook takes the strip button as well as the item** so the derivative can bind the gesture to the
whole tab. Registering it on the caption alone was the obvious shape and is wrong: the caption is
arranged at its `DesiredSize`, so on a 180px tab most of the surface is the wrapper, and
double-clicking the empty half of a tab would have done nothing.

**Rejected: a second `TabDoubleClicked(item)` hook.** It keeps `BuildCaption` honest at the cost of
making the derivative find its own caption again — a walk like `CloseButtonOf`, or a dictionary
rebuilt on every `RebuildStrip`. One hook that hands over both objects lets the handler close over
them and look nothing up.

### 5. A split has to produce a pane of the same kind

`SplitViewControl.NewPane` hardcoded `new TabViewControl`, so splitting an editable view would have
produced a plain one and renaming would have vanished from the new pane with no error anywhere.
`TabViewControl.NewOfSameKind()` is a virtual factory the copy list then writes onto.

**Rejected: `Activator.CreateInstance(source.GetType())`.** Shorter, and covers every future subclass
without being overridden — but it turns a missing parameterless constructor into a runtime failure in
the middle of a drag, and this codebase already leans on reflection in enough places that are hard to
trace.

Tear-off is not affected the same way: it builds its window from `tearOffDocument`, so the XML decides
the kind. `TabWindow.xml` names `<EditableTabs>` for that reason.

### 6. The rename itself stays in Periodic, reached through `TabItemControl.onRename`

The engine control edits a caption and hands the string on. `Action<string>? onRename` on the tab is
what knows the string means a note: `VaultBrowserControl.Open` sets it, and a tab seeded by `UI.xml`
leaves it null, which is also the check that decides whether the field opens at all.

The closure reads `editor.session.path` **at commit time**, not the path captured when the tab was
built — otherwise a second rename writes to the name the first one moved away from.

`Rename(FileObject, string)` became a one-liner onto a static `RenameNote(string, string)`, so the row
and the tab run the same code. Static, because a tab has no row and no browser instance; it finds the
browser by name only to `Rebuild()` it, and does the file work whether or not one is there.

**Rejected: moving note operations into engine `NoteActions`.** More reuse when the editor grows a
document host, more engine surface now for one call site, and the engine would learn what a note is.

## Deliberately out

- `onDoubleClick`/`bubbleDoubleClick` as `[A_XSDElementProperty]`. Nothing authors a double click in
  XML yet, and the schema would grow two attributes per control for it.
- Double-click-select-word in [[text-box]], the browser's double-clickable folders, and double-click
  on a splitter to even the panes. The dispatch they were all blocked on now exists; none are wired.
- Bounding the field to the caption slot. `EditableLabelControl` sizes an edit from its text
  (`max(120, text + 24)`), so a long name runs under the close button and stops at the next tab —
  the strip clips there. The browser row has the same behaviour and neither is worth new sizing API
  yet.

## Known consequences

- `RebuildStrip()` during an open rename destroys the field silently — opening or closing any tab in
  that view does it, and so does the commit's own `Retitle`. Safe because `EditableLabelControl` ends
  the edit *before* invoking the callback and destroys are queued, but a second rename opened during a
  first one is simply lost. Same class as the browser's `Rebuild()`, logged in [[inline-rename]].
- The second click of the pair still runs press and release in full: the tab activates, a drag starts
  and a drop is offered. All no-ops on a tab already under the pointer.
- `Engine.doubleClickTime = 250` was deleted. It was referenced nowhere and had been superseded by
  `Input.DoubleClick.Timeout` in settings, which is in seconds and reaches the tracker through
  `InputHandler.tapWindow`.
- `XSDGenerator.BuildTypeMap`/`BuildCategoryMap` read `[A_XSDType]` with `inherit: true` and take
  `.First()`, so `EditableTabsControl` may map Type→"TabView" depending on attribute order. Affects
  Type→name lookups only; `AnyXMLType.FindType` uses `inherit: false`, so parsing `<EditableTabs>`
  resolves correctly. Untouched.
- Pre-existing, untouched: `RenameNote` writes the *typed* name inside a file `FreePath` may have
  named "Name 2", unlike `DuplicateNote`, which uses the filename it actually got.

## Related

- [[inline-rename]] — the field, the blur commit, and the tree walk a rename resyncs through
- [[tab-view-control]] — the strip, the hide primitive, and the press/release routing it forces
- [[splitter-and-pane-sizing]] — `NewPane` and what a split copies
