# Decision — reparenting is detach + the new parent's own AddChild, and a name lives on Entity

**Date:** 2026-08-19
**Status:** LANDED. Runtime-probed, **NOT GUI-verified**.
**Scope:** `ArctisAurora.Core.ECS.EngineEntity` (`Entity`), `ArctisAurora.Core.UISystem.Controls`
(`VulkanControl`).

Groundwork for the tab/dock system: moving a tab between panes is a subtree reparent, and a pane has
to be addressable without a `Find<T>` type walk.

## Decisions

### 1. `SetParent` routes through the existing virtual `AddChild`

Detach from the old parent, then call `newParent.AddChild(this)`. Each container's own rules keep
applying for free — a plain `VulkanControl` still throws past one child, `AbstractContainerControl`
still appends and invalidates.

**Rejected: a dedicated `MoveChild` on containers.** That is a second attach path that would have to
re-implement every container's rules and would drift from `AddChild` the first time one changed.

Calling `AddChild` on a control that still has a parent puts it in two `children` lists, `UI.DFSOrder`
then emits it twice, `order.Count != _count` and `DataPool.Resequence` **skips the whole resequence**
with a console line. `SetParent` exists so callers do not hand-roll that pair.

### 2. `RemoveChild` is virtual on `Entity`, overridden on `VulkanControl`

The insert half already did `MarkTreeOrderDirty()` + `InvalidateLayout()`; only the detach half was
missing, so the override is those same two calls after `base`.

It marks order dirty even though a following `AddChild` marks it again. A bare detach with no
re-attach also changes DFS order, and `_orderDirty` is a bool — the redundancy costs one resequence
at a frame edge, at most.

### 3. The name is `Entity.name`, not a new field on the control

The field already existed and is `[@Serializable]`; it only lacked the XML attribute. A `controlName`
on `VulkanControl` would be a second identity that can silently disagree with the first.

Category is `"EntityRegistry"` and it still reaches every UI type: `XSDGenerator.GetAnnotatedMembers`
calls `GetMembers` **without `DeclaredOnly`**, so an annotated base member is emitted onto every
inheriting complex type in that type's own schema. `string` maps to `xs:string`, so no cross-category
import is involved (see [[xsd-generator-cross-category]]).

### 4. `FindByName` searches the subtree; the lookalike next to it does not

`Entity.GetChildEntityByName` already existed and scans **direct children only** — which is why
`VaultBrowserControl` wrote its own recursive `Find<T>` rather than using it. `FindByName` is the
recursive one (self included, DFS pre-order, first match). Both were left in place; the shallow one
has callers and is not mine to remove.

`VulkanControl` overrides it with a covariant `VulkanControl` return — a control's children are always
controls, since every `AddChild` on the control side throws on anything else, so the walk can hand
back the control type instead of making every caller cast through `Entity`.

`VaultBrowserControl`'s private `Find<T>` type walk is **deleted**; both its lookups go through the
override against `Name="Browser"` / `Name="Editor"` in `UI.xml`, held as constants on the control.
That trades a lookup nothing could break for one that returns null if the XML is renamed — the editor
would simply stop opening notes, with no error. Accepted (user, 2026-08-19).

### 5. The cycle guard throws rather than being left to the caller

Parenting into your own descendant makes `UI.DFSOrder` recurse forever and takes the main thread with
it. An ancestor walk in `SetParent` turns a hang into a stack trace at the call site. Drop-target
logic in the dock system should still refuse the move before it gets here.

## Verified

- Builds clean.
- **Runtime probe** (temporary, reverted): `Name` set in `UI.xml`, resolved through `FindByName` to
  `VaultBrowserControl` / `DocumentEditorControl` / `StackPanelControl`; an unknown name returns null.
- Reparenting the vault browser out of the horizontal pane into the vertical root, with an explicit
  `preferredHeight = 200`: old parent 3 children → 2, root 2 → 3, browser lands at `0,520 1280x200`
  (32 title bar + 488 star pane) and the document editor reclaims the vacated pane, `225,32 1055x688`
  → `5,32 1275x488`. **Pool count 617 before and after** — the subtree lived through the move.
- The cycle guard throws on `root.SetParent(browser)`.

## Traps for later

**A detached subtree keeps rendering.** `UI.DFSOrder` collects roots as live controls with no
`VulkanControl` parent, so a control removed and *not* re-attached becomes a root: it stays in the
pool, keeps its slot, and keeps drawing at its last transform. Detaching is therefore **not** a way to
hide an inactive tab — that still needs its own answer.

**An unsized control moved across axes measures to infinity.** The browser has `Width="220"` and no
height; moved into a vertical stack it was offered `float.MaxValue` on the main axis and
`VulkanControl.Measure` returned it, collapsing the sibling to zero. Pre-existing behaviour of
`StackPanelControl.Measure` + the base `Measure`, not of reparenting — but a dock system that moves
panes between orientations will hit it constantly.

**`Destroy()` does not go through `RemoveChild`.** It still calls `parent.children.Remove(this)`
directly. Left alone deliberately — its comment says removals never need a resequence because
`CompactOrdered` preserves relative order.

Related: [[ui-data-control-split]], [[splitter-and-pane-sizing]], [[xsd-generator-cross-category]],
[[ecs-rework-data-pools]]
