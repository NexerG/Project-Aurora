# Engine roadmap — phases & dependency spine

Human-readable version: `DOCUMENTATION/Roadmap.md`. WIP list is grouped by these phases.
End goal: Tarkov-style extraction FPS (~2030); side projects sequenced to force engine features the game needs.

## Phases

| Phase | When | Focus | Key outputs |
|-------|------|-------|-------------|
| A | now → ~Jul 2026 | Periodic MVP + hygiene | Periodic P3–P5; `AuroraTesting` headless tests; input handle states (game/ui); mouse → `InputHandler` |
| B | ~Jul–Sep 2026 | Foundations | ECS rework (object → data-oriented structs, snapshot-friendly); `VK_KHR_dynamic_rendering`; XSD/XML engine settings (GPU/CPU/misc) |
| C | ~Sep–Dec 2026 | Animation + AuroraMotion | Animation/evaluation core; procedural geometry/SDF op chains (XML); XML scene format; offscreen render + readback; ffmpeg video export (H.264/VP9/AV1); simple audio (play + mux); timeline UI; `AuroraMotion` host project |
| D | 2027 | Editor + renderer maturity | AuroraEditor shell (hierarchy/inspector/asset browser); bindless descriptors, BDA, lazy renderer, culling, LODs; Bootstrapper/Registry rework done |
| E | 2027–2028 | Physics + audio engine | Custom AVBD solver (benchmark vs Jolt, dev-only dep); character controller + ballistics; real audio engine (mixing, 3D) |
| F | 2028–2030 | Game | World streaming; procedural land gen/props; AI; inventory UI; netcode (~year 3) on snapshot-ready ECS |

## Dependency spine
headless tests → ECS rework + dynamic rendering + settings → animation core → procedural + scenes + export → editor + renderer maturity → physics + audio → game → netcode

## Standing decisions (user-confirmed 2026-06-10)
- Video export: ffmpeg **subprocess** (frames piped via stdin) — no NuGet; codecs = ffmpeg presets (H.264/mp4, VP9, AV1).
- Audio: Phase C = simplest playback + export mux only; genuine engine in Phase E.
- Networking: design-for, build-later — ECS must be determinism/snapshot-friendly; netcode ~year 3.
- Physics: custom **AVBD** (Augmented Vertex Block Descent), compared against Jolt; JoltPhysicsSharp allowed as dev/test-only dependency.
- ECS rework deliberately **early** (Phase B) so animation/procedural/scene systems target the final storage model.
- Animation core is the **predecessor of procedural systems** — one parameter-evaluation foundation; procedural ops plug into it (shares design with planned XML material DAG).
- Animation property binding via stable IDs: entity GUID + component type + member name (reuses `[A_XSDElementProperty]` reflection).

## Cross-references
- Periodic editor status/phases: `periodic-editor-architecture.md` (P0–P2 done, P3–P5 = roadmap Phase A).
- Note persistence pattern: `../Patterns/document-xml-persistence.md`.
