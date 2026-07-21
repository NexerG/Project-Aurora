# Aurora Roadmap

The long-term goal is a Tarkov-style extraction FPS (~2030). The path there is sequenced so each side project forces general engine capabilities the game needs anyway: **Periodic** (Obsidian-style note app, in flight now) matures text/UI/input, **AuroraMotion** (motion-graphics editor, ~end of 2026, replacing the Blender geometry-nodes workflow for video production) forces animation, scenes, offscreen rendering and video export, and the editor/physics/audio work after that builds directly toward the game.

## Standing decisions

| Topic | Decision |
|---|---|
| Video export | ffmpeg subprocess (frames piped over stdin, no NuGet); codec presets for H.264/mp4, VP9, AV1 |
| Audio | Simple playback + export mux layer first (Phase C); genuine audio engine (mixing, 3D spatialization) in Phase E |
| Networking | Design ECS for determinism/snapshotting now, build netcode ~year 3 (Phase F) |
| Physics | Custom **AVBD** (Augmented Vertex Block Descent) solver, benchmarked against Jolt (JoltPhysicsSharp as dev/test-only dependency) |
| ECS rework | Happens early (Phase B) so animation/procedural/scene systems are built on the final data-oriented foundation |
| Animation core | Is the predecessor of the procedural systems — one parameter-evaluation foundation, procedural geometry/SDF ops plug into it |
| Settings | XSD/XML-driven engine settings/preferences (GPU selection, CPU/threading, misc) land in Phase B |

## Dependency spine

Headless tests → data-oriented ECS + dynamic rendering + settings → animation/evaluation core → procedural ops + XML scenes + video export → editor tooling + renderer maturity → AVBD physics + audio engine → game systems → netcode.

## Phase A — now → ~Jul 2026: Periodic MVP + engine hygiene

Forces text-editing maturity, input routing, file I/O and testability — reused by every later tool and by the game UI (the game is UI-heavy).

- Finish Periodic L1–L2 then P3–P5: document layout engine + virtualized view (cache-based geometry, 100-page scale) first, then edit session + caret + char input, selection + run styles, vault browser.
- Headless test project: wire `AuroraTesting` into the solution so engine logic is testable without booting GPU/window; unblocks regression-safe rework in Phase B and everything after.
- UI input handle states (game/ui contexts) and mouse input moved fully into `InputHandler`.

## Phase B — ~Jul–Sep 2026: ECS rework + renderer/settings foundation

Pulled forward so animation, procedural and scene systems are built on the final ECS instead of needing migration insurance later.

- ECS rework: object lists → data-oriented struct components, designed determinism/snapshot-friendly for later netcode; entity logic stays on the main-thread `Interpolate()` model.
- Renderer: `VK_KHR_dynamic_rendering` — removes render passes and framebuffers; done before Phase C because offscreen render targets for video export get much simpler.
- Engine settings/preferences: XSD/XML-driven settings (GPU device selection, CPU/thread counts, misc engine options) on the existing registry infrastructure; ties into the Bootstrapper/Registry rework's "Settings" item.

## Phase C — ~Sep–Dec 2026: Animation core + AuroraMotion

Forces animation/timeline and scene serialization (game needs both) plus offscreen rendering (replays/screenshots later). The animation core is deliberately the seed of the procedural system.

1. Animation/evaluation core (engine, not app): keyframes + curves (reuse the existing Bezier math), property tracks bound to ECS component fields via the `[A_XSDElementProperty]` reflection machinery using stable IDs (entity GUID + component type + member name), clips, evaluation clock — designed as a general "evaluate parameters over time" foundation that procedural ops plug into.
2. Procedural geometry/SDF evaluation: XML-declared operation chain (XSD types as ops) driven by the same evaluation core — geometry-nodes-like workflow without a node-graph UI; shares design with the planned XML material DAG; covers the Blender SDF complaint.
3. XML scene format: finish scene load/save as XSD/XML; the binary `Serializer` stays for blobs only.
4. Offscreen rendering + readback: fixed-timestep render to image + GPU→CPU copy.
5. Video export: pipe raw frames to an external `ffmpeg.exe` subprocess via stdin; codec targets H.264/mp4, VP9, AV1 (a codec is an ffmpeg argument preset, not engine code).
6. Simple audio layer: load + play audio files and mux audio tracks into exports via ffmpeg; no mixing/spatialization engine yet.
7. Timeline UI: timeline/dopesheet control built from existing containers; forces the Window Splitter item.
8. New host project `AuroraMotion` (same pattern as `Periodic`: thin app over the engine).

## Phase D — 2027: Editor shell + renderer maturity

- AuroraEditor becomes real: scene hierarchy panel, reflection-driven inspector (off XSD attributes), asset browser — shared infrastructure with Periodic's vault browser and Motion's timeline panels.
- Renderer upgrades, now sequenced: bindless descriptor sets (global/texture/sampler/per-object), BDA vertex buffers, lazy renderer, game+UI render blending, mesh component on the new system, GPU occlusion culling, LODs.
- Bootstrapper/Registry XML-driven rework completed (execution ordering finally defined).

## Phase E — 2027–2028: Physics + audio engine + gameplay foundation

- AVBD physics engine (custom): broadphase, narrowphase, solver; a real physics thread replaces the 32 ms sleep stub; benchmarked against Jolt via JoltPhysicsSharp as a dev/test-only dependency (comparison harness only, not shipped).
- Character controller + ballistics raycasts (extraction-FPS core).
- Genuine audio engine: mixing, 3D spatialization, occlusion; upgrades the Phase C playback layer in place so the Motion timeline keeps working.

## Phase F — 2028–2030: The game

- World streaming, procedural land generation + prop placement (graduating the "some day" items via Phase C's procedural system), AI, inventory/stash UI (Phase A/D UI payoff), netcode on the snapshot-ready ECS (~year 3), content.
