# UI animation driver — wanted, nothing designed yet

**Raised:** carried as an open WIP item. **Nothing built, and the shape is not settled** — this file holds
what is known, not an agreed plan.
**Checklist form:** the UI-animation item in `DOCUMENTATION/Work in Progress List.md`.
**Distinct from Phase C's animation core** — see Scope below. Confusing the two is the main risk here.

## The gap

Nothing in the UI can change over time on its own. Every visual is written once by whatever last touched
it and holds until something else writes it. There is no per-frame ticker a control can register a value
with, so anything easing, springing or looping has to be faked by the thing that wants it.

## Who wants it — three, so far

| Consumer | What is missing today |
|---|---|
| **Gradients** | cannot animate at all — the ramp table uploads once and is never rewritten. Needs a dirty flag *and* a driver. Tracked under `text upgrade → gradient` |
| **Position / size** | a splitter, a pane resize, a tab move or a context menu snaps rather than eases |
| **Colour** | `ButtonControl.ApplyState` swaps hover/press tints instantly |

**Overscroll (2026-08-30) is the first thing to have been *shaped* by the absence.** The rubber-band form
was rejected outright because there is nothing to run the spring; static scroll-past-end shipped instead.
That is the cost of the gap showing up in a decision, not just in polish.

## Scope — not Phase C

Phase C's animation core is content/scene animation, bound to ECS component fields, for AuroraMotion.
This is UI chrome. **The two have very different lifetimes and the UI one needs no serialization.**

## Open questions — all three still open

- **Where it runs:** on `Interpolate()` alongside `OnTick`, or its own `ThreadedSystem`.
- **Who owns an animation:** the control, or a central table. The UI data/visualization split wants the
  table — see `ui-data-control-split.md`.
- **Whether it shares Phase C's keyframe/curve evaluation**, or stays a separate small thing for chrome.

## Before building

Answer the three above first. The lifetime difference in Scope is what decides the third, and the
data/visualization split decides the second — so this is sequenced behind that split, not ahead of it.
