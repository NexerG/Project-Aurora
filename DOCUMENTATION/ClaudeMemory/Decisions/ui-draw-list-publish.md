# Decision — the draw list publishes its count at the end of the walk

**Date:** 2026-09-12
**Status:** LANDED. **GUI-verified** — the user ran it and confirmed the flicker is gone.
**Scope:** `ArctisAurora.Core.UI` — `DrawList`, `UIEngine.BuildDrawLists`;
`ArctisAurora.EngineWork.Rendering.Modules` — `UIEngineModule.MirrorDrawList`

**Corrects:** the "same coarse race … the fix is a 3-deep ring of lists" bullet in
[ui-draw-list](ui-draw-list.md). The race was not benign and the fix was not a ring.

## What changed

`DrawList` had one `_count`, zeroed by `Clear()` and incremented by `Next()`, read by the render
thread with no synchronisation at all. It now has two fields:

| Field | Written by | Read by |
|---|---|---|
| `_cursor` | the walk — `Clear()` rewinds it, `Next()` advances it | main only |
| `_count` | `Publish()`, once, after `Collect` returns | render, through `Count` |

- `Clear()` sets `_cursor = 0` and leaves `_count` alone.
- `Publish()` is `Volatile.Write(ref _count, _cursor)`; `Count` is `Volatile.Read(ref _count)`.
- `BuildDrawLists` calls `Publish()` between `Collect` and its `Log.Every` line, so the log reports
  this frame's count rather than the previous one.

## Why these choices

**The visible symptom was the whole window blanking, and it came from `Clear()` writing zero.**
Main and render are not parked against each other — `Engine.MainTick` runs `BuildDrawLists` at the
frame edge while the render thread is live. `MirrorDrawList` sampling `Count` just after `Clear()`
read zero and drew nothing that frame; sampling mid-walk read a truncated list. At tick rate that
reads as flicker, not as a dropped frame.

**Publishing at the end costs one int and removes the blanking entirely.**
The render thread can no longer see a count that was not the result of a completed walk. Between
`Clear()` and `Publish()` it keeps drawing the previous frame's count.

**The volatile pair is load-bearing, not decoration.**
Without it the JIT or the CPU may make the new count visible ahead of the slot writes that justify
it — the same garbage the split exists to remove, just rarer and harder to catch.

## What this does not fix

**Slots are still overwritten in place under the reader.** The count is coherent; the rows it points
at can be a mix of this frame's and last frame's, because `Next()` hands back the same slots the
mirror is copying. A control that moved can sit a frame apart from its neighbour. Judged acceptable
once the blanking stopped.

Tear-free needs a double buffer — two array pairs, the walk fills the back one, `Publish()` swaps the
reference. Roughly 48 KB at the current 256-slot capacity. Not built.

**A capacity growth still swaps the arrays mid-read.** `Next()` resizes when `_cursor` reaches the
end and the mirror holds the old reference; `Math.Min(Count, geometry.Length)` keeps it in bounds.
Rare and transient, left alone.

## Rejected

| Alternative | Why not |
|---|---|
| A ring of 3 lists — what [ui-draw-list](ui-draw-list.md) predicted | Three full copies to remove a tear the two-field split already reduces to invisibility. A ring answers a pipelining problem the engine does not have: render is one frame behind main, not three |
| A lock around the walk and the mirror | Puts the render thread's copy behind a whole tree walk, which is the expensive half of the frame edge |
| Park the render thread across `BuildDrawLists` | Reinstates the stall `DataManager.FrameEdge` was reworked to remove |

Related: [[ui-draw-list]], [[ui-engine-stack]], [[mapped-streaming-buffers]]
