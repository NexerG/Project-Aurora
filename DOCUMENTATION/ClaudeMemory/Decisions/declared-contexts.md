# Decision — contexts are declarable in XML, and a declared one can derive from another

**Date:** 2026-08-22
**Status:** LANDED. Boot-verified. **NOT GUI-verified** — no pane click has been walked.
**Scope:** `ArctisAurora.Core.Registry` (`Context`, new `ContextDefinition`/`ContextMap`),
`ArctisAurora.Core.UISystem` (`UICollisionHandling`), `Bootstrap.xml`,
`Periodic/Data/XML/Documents/Contexts/Periodic.xml`, `Periodic.Editor.CustomControls`
(`VaultBrowserControl`).

## The problem this solves

`VaultBrowserControl.Open` always opened a note in the **left** pane. `FocusedTabs` walked up from
`UICollisionHandling.activeControl` looking for a `TabViewControl`, but the browser sits beside the
`SplitView` in `UI.xml`, not inside a pane — and clicking a row *makes that row the active control*.
The walk therefore ended at the root every time and `Open` fell through to `FindByName("Tabs")`.

**The pane focus is destroyed by the very click that needs it.** Any fix has to remember it before
it is lost, which is a stateful, sticky thing — not something a caller can recompute on demand.

## Decisions

### 1. Contexts no longer require a C# static

`LoadContexts` binds `[A_ActiveContext]` members through reflected get/set; that is still how
`Hovering`, `Dragging` and `ActiveControl` work, and it is right for them — they are read on the
hover path every tick and belong to engine code that owns them.

A **declared** context has no member. `LoadDeclared` registers an entry whose get/set close over a
local:

```csharp
object? slot = null;
Register(definition.name, valueType, () => slot, v => slot = v);
```

The compiler's closure class *is* the storage. No second `ContextEntry` shape, no nullable value
field, no branch on "bound or declared" at read time — `Get<T>` cannot tell the difference. The
`slot` declaration must stay inside the per-element loop or every context shares one box.

### 2. Loaded from a folder, not a file

`VirtualFileSystem.EnumerateAll("XML/Documents/Contexts", "*.xml")` — the `Inputs/` pattern.

`Gradients.xml` and `ContextMenus.xml` use `TryResolveFile`, which is *first mount wins*: Periodic
shipping a `Gradients.xml` means the engine's would be invisible. That is correct for a theme, where
the app wants to replace what the engine offers. It is wrong for contexts, where the user's ask was
explicitly that "systems and apps can add custom contexts" — an app declaring one must not silence
the engine's. `EnumerateAll` unions across mounts and only dedupes on *file name*, so one file per
contributor composes.

The engine ships no `Contexts/` folder today. It declares nothing that needs one; the scan means it
can gain one later without touching Periodic's.

### 3. `From` derives a context as the nearest ancestor

```xml
<Context Name="ActiveTabViewer" Type="TabView" From="ActiveControl"/>
```

Whenever `ActiveControl` is set, every context registered under it is recomputed by walking
`Entity.parent` from the new value, inclusive, to the first entity assignable to `Type`.
`Type` resolves through `AnyXMLType.FindType`, so the XML names the XSD name (`TabView`) and gets
`TabViewControl` — and `IsInstanceOfType` means `EditableTabsControl` matches, which is what
`UI.xml` actually instantiates.

The walk is over `Entity`, not `VulkanControl`, so `Context` stays out of the UI namespace and the
mechanism is not UI-only.

**Alternatives rejected:**

- *A plain named slot the app sets itself.* Nothing would ever set it — there is no focus-change hook
  in Periodic, and adding one is the C# wiring the XML was supposed to replace. `From` is what makes
  a declared context self-filling.
- *A `TabViewControl activeTabs` static in `UICollisionHandling`.* Six lines and it works, but it is
  engine focus code naming one container type, and it is not what the user asked for.
- *Reviving the commented-out generic `activeContainer`.* Needs a `canBeActiveContainer` virtual on
  `VulkanControl` to decide what counts. `Type` in the XML already answers that question per context,
  and without a new flag on every control.

### 4. Sticky is the definition of a derived context, not an option

`Derive` assigns **only on a hit**. No ancestor of the right type means the previous value stands.

This is deliberately not a `Sticky="true"` attribute. A non-sticky derived context is just "walk up
from the active control", which the caller can do inline in four lines — the registry buys nothing.
The value it *does* buy is surviving a click that lands outside the derived subtree, i.e. exactly the
browser row. Stickiness is the feature; making it optional would make the useless case the default.

Consequence: `Context.Forget` must exist, or a collapsed pane stays derivable. It nulls any entry
holding the value, writing through the entry setter directly rather than via `Set`, so no derivation
cascade runs off a teardown.

### 5. `SetActiveControl` routes through `Context.Set`

`activeControl = control` became `Context.Set("ActiveControl", control)`. Derivation lives in `Set`,
so nothing derives unless the assignment goes through the funnel.

This closes half of the funnel defect the WIP list has carried since 2026-08-19. It does **not** close
the other half: `Set` still does not fire the `IContext` callbacks, `SetActiveControl` keeps its own
add/remove pair around the call, and `SetDragging` is untouched. Nothing needs notifying about
`ActiveTabViewer`, so unifying the callbacks stayed out of this pass.

**The cost:** `Set` silently no-ops on an unregistered name, so if `Context.LoadContexts` ever failed
to run, the active control would stop being assigned at all rather than throwing. Bootstrap ordering
makes that unreachable — `LoadContexts` is a step, a failing step halts the phase, and no click can
happen before `engine.Run()`.

### 6. Derivation skips a value already held

```csharp
if (found != null && !ReferenceEquals(Value(derivation.name), found)) Set(derivation.name, found);
```

Two reasons. Clicking repeatedly inside one pane re-derives the same `TabViewControl` on every press,
and the setter is a reflected `FieldInfo.SetValue` for bound contexts — worth not re-running per
click. And it terminates a `From` cycle: `Set` → `Derive` → `Set` recurses, so `A From B` /
`B From A` would otherwise stack-overflow. No cycle detector was added; the guard makes an authored
cycle settle instead of crash.

## Verified

- `dotnet build ArctisAurora.sln` — 0 errors. The new warnings in `VaultBrowserControl` are the
  file's pre-existing nullable set; no touched line added one.
- Boots: `ContextTypeSchema.xsd` generated, `[Bootstrap] Running: Context.LoadDeclared` between
  `Context.LoadContexts` and `PrepareDefaultAssets`, **no** `[Context] … skipping` line — so
  `TabView` resolved and `ActiveTabViewer` registered. All 26 steps pass.
- Not GUI-verified. No click has been walked, in either pane.

## Still open

- **`Type` is metadata, not a contract.** `Register` stores it and nothing checks a `Set` against it.
  A derived context can only ever hold the type it walked for, but a plain declared one set from code
  can hold anything.
- **Derivation only fires from `Set`.** `UICollisionHandling.Forget` and `SolveHover`'s direct writes
  do not derive. Only `ActiveControl` currently has a follower, and it is the one path that funnels.
- **`Context.Forget` walks every entry on every control destroy.** Four entries, a `ReferenceEquals`
  each, and the reflected setter only fires on a hit — but it is on the teardown path that closing a
  note runs tens of thousands of times.
- **The three original lines in `UICollisionHandling.Forget` are now redundant** with
  `Context.Forget` for `hovering`/`dragging`/`activeControl`. Left alone deliberately; `hinted` and
  `lastPressTarget` are not contexts and must stay either way.
- **A torn-off window is still not remembered separately.** `FocusedTabs` filters the derived value to
  `Engine.primary`, so after clicking in a torn-off window the first browser click falls back to the
  left pane rather than to the last *primary* pane.

Related: [[tab-view-control]], [[vault-browser-and-shell]], [[splitter-and-pane-sizing]],
[[ui-gradients]], [[cross-system-change-notification]]
