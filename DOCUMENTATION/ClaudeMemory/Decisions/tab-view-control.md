# Decision — hiding is a collapsed clip, and a closed tab is destroyed rather than kept

**Date:** 2026-08-19
**Status:** LANDED. Builds clean; boots and parses. **NOT GUI-verified.**
**Scope:** `ArctisAurora.Core.UISystem.Controls` (`VulkanControl`),
`ArctisAurora.Core.UISystem.Controls.Containers` (`TabViewControl`, `TabItemControl`),
`Periodic` (`VaultBrowserControl`, `UI.xml`).

The tab well [[entity-reparenting-and-names]] was groundwork for, and the thing multi-windowing is
sequenced behind — tearing a tab off into its own OS window needs a tab to tear off first.

## Decisions

### 1. Hiding is a collapsed clip rect walked over the subtree

`VulkanControl.Hide()` / `Show()` / `hidden`. `Hide` walks the subtree assigning
`LayoutRect(0, 0, -1, -1)`; the existing `ClipRect` setter mirrors each one into `controlData.clip`
and marks the pool row dirty. `UI.frag` discards every fragment outside the clip and `HitTest` is
`ClipRect.Contains`, so **one walk removes the subtree from both the draw and the hit-test**.
`FindDeepestValid` rejects on the first failed `HitTest`, so a hidden document costs one comparison,
not a descent.

`Show()` only clears the flag — the `Arrange` that follows rewrites every clip in the subtree, since
no `Arrange` override early-outs on the dirty flag. Both early-out when the state is unchanged, so
the cost is one walk **per switch**, not per frame.

The rect is degenerate, not `LayoutRect.Empty`. `Empty` is `(0,0,0,0)` and `Contains` is inclusive on
both edges, so it still contains the origin — a pointer parked in the window's top-left corner would
have hit a hidden tab. Negative extents make `Right`/`Bottom` less than `x`/`y`, which no point
satisfies.

**Rejected: arranging inactive items off-screen.** It needs no engine code at all — a child inherits
its parent's clip verbatim unless it sets `clipOutOfBounds`, so an item arranged outside the TabView
is discarded by the inherited clip, and `SolvePositions`' quad test rejects it for input. It was
rejected on cost: it re-arranges every hidden subtree on every layout pass, so one hidden 400-block
note is ~56k `Arrange` calls per window resize, against a single walk per switch.

**Rejected: destroy and rebuild the page on switch.** Loses scroll, caret and selection, and rebuilds
the document tree on every click.

`hidden` is **not** an XML attribute and no other container was taught to skip hidden children. Only
`TabViewControl` produces one, and a hidden child of a `StackPanelControl` would still get its
`Spacing` gap — the flag would be a trap the moment it were authorable.

### 2. Closing destroys; only being inactive hides

`CloseTab` saves the page, then `item.Destroy()` — which enqueues the whole subtree and frees the pool
rows at the frame edge, so a 56k-glyph note is gone rather than parked (user, 2026-08-19).

**The strip button is destroyed explicitly**, because it is not in the item's subtree. `RebuildStrip`
`Destroy()`s the buttons it replaces rather than dropping the references — an orphaned control has no
`VulkanControl` parent, so `UI.DFSOrder` collects it as a *root*, and it keeps its pool row and keeps
drawing at its last transform.

Nothing is cached for reopening. Clicking the note again re-parses it from disk; scroll and caret are
gone.

### 3. `activeItem` is a reference, not an index

An index is wrong the moment a tab closes. `SetActive` hides the outgoing item and shows the incoming
one; `Arrange` then only ever touches `activeItem`, so hidden pages are not in the layout at all.
`CloseTab` clears `activeItem` **before** `Destroy()`, or `SetActive` would walk the dying subtree
collapsing clips it is about to free.

### 4. The tab is a button whose containers have no bare area

`tab(Button) -> row(StackPanel) -> [wrapper(Panel, star) -> caption(Label), close(Button)]`.

`SolveLMBPress` stores `ActiveTarget(hovering)` — the first ancestor with `canBeActiveContext` — and
`SolveLMBRelease` fires only when the release resolves to the same control. `LabelControl` and
`GlyphControl` answer false, but **every container answers true**, so any pixel of a container that a
press can land on is a place where press-here-release-there fires nothing.

So the row tiles the tab exactly (no padding on the tab), the wrapper carries the caption inset as its
own `padding` rather than the row carrying it, and the close button is full strip height rather than a
centred 16px square. There is no bare container pixel left inside a tab.

The wrapper exists because vertical centring needs it: `StackPanelControl` gives a child with
`preferredHeight == 0` the full cross extent, and `TextControl.Arrange` lays glyphs from the top of
the rect it is given. A `Panel` whose single child is the caption falls into `VulkanControl.Arrange`'s
one-child path, which centres by `verticalPosition`. Same shape as a vault-browser row.

The wrapper carries `preferredHeight = tabHeight` to dodge an engine defect, not for looks — see
"a star child poisons the cross measurement" below.

`row` and `wrapper` `BubbleAll()` so a caption click reaches the tab. The close button bubbles
**enter and exit but not release** — so the tab keeps its hover tint while the pointer is on its own
x, and closing never also activates.

The caption is ASCII `x`, like the window chrome — the atlas has no `✕`.

### 5. Tab identity is `Entity.name`, holding the note path

`FindTab(name)` scans the items. `VaultBrowserControl` sets `name` to the note path and `header` to the
file name, so re-clicking an open note focuses its tab instead of opening a second one. This reuses the
naming system that landed the same day rather than inventing a second identity — the alternative was a
`Dictionary<string, TabItemControl>` on the browser, which is parallel state that can desync from the
tree.

### 6. Switching notes no longer saves the one being left

[[vault-browser-and-shell]] decision 4 saved on switch because the note was about to be **discarded** —
one editor, `LoadPath` over the top of it, no undo. With tabs the note stays live in its own tab, so
the reason is gone. Saving moved to close.

**Consequence to accept:** Periodic has no dirty tracking and no autosave, so a crash with several tabs
open now loses more than it did before.

### 7. `TextInputActions` needed no changes

`Editor()` walks up from `UICollisionHandling.activeControl` and has never looked a control up by name,
so Ctrl+S, typing and every caret action already target whichever editor was last clicked in. Only
`VaultBrowserControl` used `FindByName("Editor")`.

## Verified

- Builds clean, 0 errors.
- Boots: `UI.xml` parses with `<TabView>`, `XSDGenerator` emits `TabView`/`TabItem` into
  `UITypeSchema.xsd`, `OpenFirstNote` builds the first tab from `Main`, all three threads start, no
  exception.
- **NOT GUI-verified** — nothing has been eyeballed or clicked. See [[verify-what-the-user-sees]].

## Engine defect found and fixed — a destroyed control stayed in the input contexts

Nothing cleared a destroyed control out of `hovering` / `dragging` / `activeControl`, so the collision
handler kept notifying entities whose pool rows had been freed. `DataPool.GetRef` reads
`_slots[h.StableId]` with no `Alive` check and a freed slot is `-1`, so the next notification indexed
`data[-1]`: **"Index was outside the bounds of the array"**.

Closing a tab is the first thing in the engine that destroys *the control the pointer is on*:

1. Release on the x bubbles glyph → label → close button → `onRelease` → `CloseTab`.
2. `CloseTab` destroys the item, and `RebuildStrip` destroys every strip button — including the close
   button under the pointer and the glyph that is `hovering`.
3. `ProcessDestroys` frees their rows; `FrameEdge` sets `_slots[stableId] = -1`.
4. Next tick `SolveHover` resolves a different control and calls `hovering.ResolveExit()`, which
   bubbles into `ButtonControl.ResolveExit` → `ApplyState()` → `controlData.style.tint` → crash. The
   close button's `bubbleExit` (decision 4) carries it to the tab button too.

Fixed with `UICollisionHandling.Forget(control)` called from a `VulkanControl.OnDestroy()` override.
`ProcessDestroys` calls `OnDestroy()` **before** `Pool.Free`, so the row is still readable there.
`Forget` assigns the three directly rather than routing `dragging` through `SetDragging` — that is the
sanctioned single writer, but it notifies the outgoing control via `IContext`, and calling into a
dying control is the exact thing being fixed.

**Latent everywhere else, not tab-specific.** `VaultBrowserControl.Rebuild()` destroys every row and
would crash identically if anything called it while a row was hovered; `DocumentEditorControl
.LoadDocument` destroys a whole document, and only survived because `hovering` was always the vault
row, which lives.

**Rejected: guarding `DataPool.GetRef` with `Alive(h)`.** It is on the path of every tint and transform
write, and it converts a real lifetime defect into a silent no-op.

## Engine defect found, worked around not fixed

**A star child poisons a `StackPanelControl`'s cross measurement.** `Measure` pass 1 probes each star
child at **0** on the main axis (`crossOffer` is `(0, inner.height)` horizontally), only to learn its
cross size; pass 2 re-measures at the real allocation. `maxCross` is a running `MathF.Max` across both
passes, so any control whose cross size depends on its main size — all text — contributes its pass-1
value permanently.

This is what shipped the tab strip with no visible close button. Probed rects, before:

```
tab     225,32  180x28
row     225,-38 180x168      <- 168 tall, centred, so 70px above the tab
close   385,-38 20x28   clip 225,32 -> 1280,60    <- entirely outside its clip
caption 233,35.5 57.6x21     <- right by accident: centred in a box centred on the tab
```

`"SampleNote"` measured at content width `0 - 8 = -8` wraps one character per line: 8 lines × 21 =
168. The caption looked correct, which is what made it read as "the x was never built".

Worked around by pinning `preferredHeight = tabHeight` on the wrapper — `VulkanControl.Measure` skips
the child-driven height when `preferredHeight > 0`, so the poisoned value never reaches `maxCross`.
After: `row 225,32 180x28`, `close 385,32 20x28`.

The real fix is in `StackPanelControl.Measure` — pass 2 should *replace* each star child's cross
contribution rather than max against the probe, or pass 1 should not measure star children on the
cross axis at all. Deliberately not done here: it is engine layout code well outside this slice and
every existing star consumer is calibrated against current behaviour.

## Traps for later

**Clicking a tab or its x makes that button `activeControl`**, so Ctrl+S does nothing until the user
clicks back into the document. Pre-existing — the same is true after clicking a vault-browser row.

**The strip does not scroll.** Tabs are a fixed `TabWidth` and the strip clips, so past
`width / TabWidth` tabs the rest are unreachable.

**`ApplyTabColors` pairs `strip.children[i]` with the i-th item by position**, and reaches the close
button as `tab.children[0].children[1]`. Both hold because `RebuildStrip` is the only thing that
builds the strip; both break silently if anything else inserts into it.

**Retinting the active tab while the pointer is on it** shows the active ground rather than the hover
ground until the pointer moves — `controlColorHex` writes the tint directly and `ButtonControl`'s
`ApplyState` only re-derives on the next enter/exit. Cosmetic.

**A whole-tab hide is O(subtree).** Switching away from a 400-block note walks ~56k controls once.
Acceptable per switch; it would not be per frame, which is why decision 1 went the way it did.

**`AllowedChildren` cannot name one concrete type.** `XSDGenerator` collects *subtypes* of it
(`IsAssignableFrom(ty) && ty != AllowedChildren`), so `typeof(TabItemControl)` emits an **empty**
choice — a schema forbidding all children — rather than one permitting only `TabItem`. `TabView` is
therefore `typeof(IXMLChild_UI)` like every other container, and "TabItem only" is enforced by the
throw in `AddChild` alone. Tried and reverted, 2026-08-19.

---

# Amendment — the tab menu, and the split that acted on the wrong tab (2026-08-21)

**Status:** LANDED. Builds clean, boots, `ContextMenus.LoadMenus` binds every entry. **Not
GUI-verified.**
**Scope:** `ArctisAurora.Core.UISystem.Actions` (`TabActions`, `ViewActions`),
`ArctisAurora.Core.UISystem.Controls.Containers` (`TabViewControl`, `SplitViewControl`),
`AuroraEngine/Data/XML/Documents/ContextMenus.xml`.

## The defect

Right-clicking a tab and choosing `Split right`/`Split down` moved **the view's active tab, not the
one under the pointer** (user, 2026-08-21).

`TabStripButtonControl` offered no menu entries, so `VulkanControl.OpenContextMenu` walked past it —
button → strip → `TabViewControl`, which is what names `ContextMenu="view"` in `UI.xml`. The menu's
owner, and therefore `ContextMenus.invoker`, was the *view*. `ViewActions.Split` had nothing to act on
but `source.activeItem`, so right-clicking tab C while A was showing split A off and left C where it
was.

The keybinds (`Ctrl+\`) were always correct in this respect: with no pointer involved, the active tab
*is* the intended one.

## Decisions

### 1. The strip button owns a menu, which makes it the invoker

`TabViewControl.BuildTab` sets `contextMenus` on each strip button from a new `TabContextMenu`
property (default `"tab"`), so `Compose` yields entries on the button and `OpenContextMenu` stops
there. The button already carried `item` and `owner` for the drop path; every entry reads them.

A property rather than a hardcoded name because the strip button is built in code and never authored,
so a host has no other way to name its own menu — the same shape as `TearOffDocument`. An unknown
name contributes nothing, so a host that defines no `tab` menu simply gets none.

`SplitViewControl.NewPane` now copies `tabContextMenu` **and** `contextMenus` from the source, or the
pane created by a split had no menus at all — pre-existing for `contextMenus`, and it would have been
a new hole for the tab menu.

### 2. One split operation, two callers

`TabViewControl.SplitOff(item, edge)` holds the move; `ViewActions` passes `activeItem` and
`TabActions` passes the clicked tab. The `ownOnly` guard — refuse only when the item is ours *and* we
hold fewer than two — matches what `PendingEdge` applies to a drop, so splitting off a pane's only
tab still cannot empty it.

`ResolveDrop` was deliberately **not** rewritten onto `SplitOff`: it needs the fall-through when
`Split` returns null, and it is the one path here that is GUI-confirmed. The three duplicated lines
are cheaper than touching it.

### 3. Entries are XML with `EnabledWhen`, not code

All six live in `ContextMenus.xml` bound to `[A_XSDActionDependency]` statics taking a
`VulkanControl` — the form `ContextMenus.BindAction` already resolves to `Action<VulkanControl>`.
`Close others`, `Close to the right`, both splits and `Move to new window` grey out through
`Tab.HasSiblings` / `Tab.HasRight` / `Tab.CanTearOff` rather than being conditionally added, so
nothing needs the code hook.

No separators: `ContextMenuBuilder.BeginMenu` is internal and divides *between* named menus, so a
single menu cannot group itself. Not worth widening the API for.

## Known consequences

- **`Close others` / `Close to the right` stop at the first unnamed edited note.** Each goes through
  `CloseTab`, which prompts for a name, and `NoteNameWindow.Ask` refuses while another prompt is open
  — it returns without invoking any callback, so those tabs simply stay open. Nothing is lost and the
  gesture can be repeated; a chained close would fix it properly.
- Right-clicking a tab no longer reaches the `view` menu. Right-clicking the page below the strip
  still does, and that one still acts on the active tab, which is correct there.

Related: [[entity-reparenting-and-names]], [[vault-browser-and-shell]], [[ui-clipping]],
[[button-states-and-hover-bubbling]], [[splitter-and-pane-sizing]], [[ui-data-control-split]],
[[context-menu-invoker]]
