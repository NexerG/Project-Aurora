---
date: 2026-09-23
tags:
  - Engine
  - d_Convention
cssclasses:
  - Aurora.css
Status: Draft
Linker:
  - "[[Arctis Aurora]]"
---
%% Cross-cutting guidance for math-heavy loops, not a class doc. No engine code follows it yet — the animation SIMD rework is the first intended user. %%

## Description
SIMD runs one instruction on 8 numbers at once, but only if those 8 numbers already sit side by side in one register — for example the X velocity of 8 different items. AoS, SoA and AoSoA are only different answers to how those numbers get into that register, and what it costs to get them there.

The rule this engine follows: **store data the way it is found, compute it the way it is processed.** Rearranging data into SoA is worth it only while there is a lot of math to do on it. Keeping data permanently in SoA is worth it only when nothing needs to find it one item at a time.

## The cases

| The data is… | Store it | Compute it |
| --- | --- | --- |
| shared by several batches or systems, reached by index | AoS, with the hot fields split from the cold ones | load 8 items, rearrange them into SoA registers, compute, write back |
| private to one batch and reused over several passes | SoA, built once per step and thrown away after | read it straight, no rearranging |
| light math over items in order | AoS | a plain loop — SIMD does not make memory faster |
| large, dense, the same items in the same order every tick, with moderate math | the one case for storing SoA permanently | read it straight |

## Why shared data stays AoS
When items are picked by index — a contact naming the two bodies it touches, a bone naming its parent — the 8 items one batch needs are never next to each other in memory. Stored SoA, fetching them means 8 scattered reads for every single field. Stored AoS, each item's fields sit together, so one or two reads fetch everything about it.

Shared data also needs one home. Several batches read and write the same item, and each has to see what the previous one wrote. The AoS row is that home.

## Why it becomes SoA for the math
The rearranging happens inside registers and touches no memory, so it is cheap — but not free. It pays when the math between loading and writing back is long: a spring step with its exponential, sine and cosine, an easing curve, a contact solve. For a single lerp it may cost more than it saves.

#### Solve Batch
##### (8 item indices)
for each `lane` in 8
	`row` = `index` is none ? identity row : rows[`index`]
	load `row`
rearrange the 8 rows into one register per field
run the math once, on all 8 lanes together
for each `lane` in 8
	if `index` is none, skip
	write back only the fields the math changed

## When data is stored SoA
Data that belongs to one batch alone, and is read again on every pass, is stored SoA from the start. A prepare step builds it once, every later pass reads it with no rearranging, and it is discarded when the step ends. Box2D and Box3D store their contact constraints this way.

## When SIMD is not worth it
Walking items in order with light math — `velocity += gravity · dt`, `position += velocity · dt` — is limited by how fast memory arrives, not by arithmetic. A plain loop already runs at that speed.

## Before a batch can run
- Every lane must be the same kind of item. Sort or bucket by kind first; a branch that differs per lane turns into computing every branch.
- No two lanes may write the same target, or one write is lost. Graph colouring guarantees it; so do targets that are unique by construction.
- The active items must be packed together. Move parked items into a separate set rather than skipping flagged rows.
- Empty lanes get identity values built in registers, never a shared dummy row in the array — threads sharing it would fight over its cache line.
- Write back only what the batch changed.

## Determinism
Only the simulation clock needs this; UI and presentation animation do not.
- No fused multiply-add in a kernel that also has a scalar version — the rounding differs.
- The engine owns its sine, cosine and exponential approximations, and both the scalar and the SIMD path use the same ones.
- The last batch is padded with identity lanes instead of finishing with a scalar loop, so an item's result does not depend on where it sits in the array.

## .NET specifics
- The default width is 8, `Vector256<float>`. On ARM64 `Vector256` is not hardware accelerated, so a kernel needs a second path built on two `Vector128`.
- .NET 10 has vectorised `Exp`, `Sin`, `Cos` and `SinCos` on `Vector256`.
- Managed arrays are only 8- or 16-byte aligned, so some 32-byte loads straddle a cache line. Aligned native memory fixes it, but only if a measurement says it matters.
- `Vector3` and `Vector4` already use SIMD across one item's x, y, z and w. That is the right tool for one-off geometry, not for batches.
- A transform of position, quaternion and uniform scale is exactly 32 bytes — one `Vector256` per item, the cheapest possible load. Non-uniform scale makes it 40 bytes.
- Measure a Release build before and after — see [[PROFILING]].

## Worked example — Box2D and Box3D
Both engines store bodies AoS. The solver only touches a small hot row per body — 32 bytes in Box2D, 56 in Box3D — and everything else lives in a parallel cold array. Bodies move between sets (awake, static, disabled, sleeping) so the awake ones are always packed together.

Only the contact solver goes wide. Contacts are coloured so that within one colour no body appears twice, which also lets threads split a colour without locks. Each batch of 4 contacts (8 with AVX2 in Box2D) loads its bodies, rearranges them into registers, solves, and writes the velocities back. Box2D's 32-byte row lets it load 8 bodies with 8 aligned reads and one in-register transpose.

The contact data itself is private to its batch, so it is built in SoA form once per step and read straight on every pass after. Integration and contacts that do not fit a colour stay plain scalar loops.

Catto measured the result on a 5,050-body pyramid with 4 workers on a 7950X: scalar 1.91 ms, SSE2 1.02 ms, AVX2 0.90 ms. On an Apple M2, scalar 1.47 ms and NEON 0.95 ms. Box3D ships 4-wide only.

## Sources
- [SIMD Matters — box2d.org](https://box2d.org/posts/2024/08/simd-matters/)
- [erincatto/box3d](https://github.com/erincatto/box3d)
- [erincatto/box2d](https://github.com/erincatto/box2d)
