# ROADMAP
Phases, dates and standing decisions live in [[Roadmap]]. Items below are grouped by phase; video/content work and research are separate sections at the bottom.

---
# PHASE A — Periodic MVP + engine hygiene (now → ~Jul 2026)
- [ ] Test/profiling platform — built **on the UI**, scheduled **after the text editor's first version**: GC/allocation, execution time, and general "does it work" checks. Dogfoods the UI while doubling as the profiler. Engine work stays manually GUI-verified until then. (Supersedes the headless `AuroraTesting` console runner.)
- [ ] UI collision
	- [ ] add handle states - game, ui etc
	- [ ] Update engine class so the mouse inputs are handled in input handler
- [ ] text editor
	- [ ] fix beziers
	- [x] turn msdf into mtsdf
	- [ ] add the rest of the alphabet (eu languages)
		- [ ] create language packs?
	- [ ] editor
		- [ ] Markdown insertions
		- [x] L1 — layout engine (pageless): `TextMeasurer` + `DocumentLayoutCache` (per-block line cache, prefix-summed block tops, per-block invalidation, `HitTest`/`CharToPoint`). Built additive — nothing consumes it yet, and no runtime verification until the profiling platform
		- [x] L2 — virtualized view: `TextRunControl` + `DocumentCanvasControl`, view driven off the cache, materialized per visible line segment ±1 viewport. Measured flat at 57–59 strips over a 400-block/22k-px note scrolled end to end, no leak. Not GUI-verified. Wrapping is now word-boundary, not per-character
			- [ ] `GlyphControl` pooling — strips are rebuilt at the buffer edge, not recycled. Deferred: no leak and flat memory measured, so it needs the profiler before it's worth the machinery
			- [ ] load-time glyph explosion — notes still build one `GlyphControl` per character at *load* (blocks/runs are controls), so the total is model + viewport rather than viewport. Waiting on the control frustum culling go-ahead; note culling alone does not fix it
		- [ ] L3 — page system (paginator over cached lines + page chrome; pageless is the L1 default mode)
		- [ ] code blocks (B1 — monospace, no wrap, view-time syntax coloring)
		- [ ] custom expressions (maths)
	- [ ] Project browser
	- [ ] Claude, chatgpt, other chatbot integrations.
	- [ ] text upgrade
		- [ ] simple color
		- [ ] gradient
		- [ ] bold/italics
		- [ ] horizontal lines (honestly its just a panel)
		- [ ] tables
	- [ ] cursor change on context
- [ ] UI
	- [ ] control frustum culling — engine-wide cull of off-screen controls (user's intended answer to document virtualization; note it cuts *draw* work only — culled controls keep their pool row and their slot in the 50,000-cap descriptor array)
	- [ ] fix up UI shaders (samplers, transparency)
	- [ ] checkout `Pretext` by Cheng Lou for UI layout calculations (apparently 500x faster than the current implementation)	
	- [x] Stack panel
	- [x] Grid
		- [x] update grid logic
		- [x] add gaps in between grid cells
		- [x] only one item per grid cell
	- [x] Scroll
	- [x] UI "start" scaler with multipliers
	- [x] fix action xsd attribute bcz it dont find actions if theyre not named EXACTLY the same (action - attribute)
- [ ] fix resolution stuff associated with DPI and stuff. use `glfwGetMonitorContentScale` *(non-essential)*

---
# PHASE B — ECS rework + renderer/settings foundation (~Jul–Sep 2026)
- [x] ECS rework — object lists → data-oriented struct components, designed determinism/snapshot-friendly for later netcode
- [ ] renderer foundation
	- [x] framebuffer removal
	- [x] `vk_khr_dynamic_rendering` - removes render pass and framebuffer.
	- [ ] figure out why the renderer breaks the second monitor
- [ ] Engine settings/preferences — XSD/XML-driven (GPU device selection, CPU/thread counts, misc engine options)
	- [ ] Settings (some day) *(graduated from the Registry/Bootstrapper rework item)*
- [ ] figure out a way to do UITrees (save only tree tops in the registry)
- [ ] update logging with .NET 11 *(non-essential)*

---
# PHASE C — Animation core + AuroraMotion (~Sep–Dec 2026)
- [ ] Animation/evaluation core — keyframes + curves (reuse bezier math), property tracks bound to ECS component fields via `[A_XSDElementProperty]` + stable IDs, clips, evaluation clock; the foundation procedural ops plug into
- [ ] Procedural geometry/SDF evaluation — XML-declared operation chain (XSD types as ops) driven by the evaluation core; geometry-nodes-like workflow without a node-graph UI
	- [ ] XML material directed acyclic graph (DAG) *(shares design with the procedural op chain)*
- [ ] XML scene format — finish scene load/save as XSD/XML; binary `Serializer` stays for blobs only
- [ ] Offscreen rendering + readback — fixed-timestep render to image + GPU→CPU copy
- [ ] Video export — pipe raw frames to external `ffmpeg.exe` via stdin; codec presets for H.264/mp4, VP9, AV1
- [ ] Simple audio layer — load + play audio files, mux audio tracks into exports via ffmpeg (no mixing/spatialization engine yet)
- [ ] Timeline UI — timeline/dopesheet control built from existing containers
	- [ ] Window Splitter
- [ ] `AuroraMotion` host project (same pattern as `Periodic`: thin app over the engine)

---
# PHASE D — Editor shell + renderer maturity (2027)
- [ ] AuroraEditor shell — scene hierarchy panel, reflection-driven inspector (off XSD attributes), asset browser
- [ ] Registry and Bootstrapper rework
- [ ] renderer update
	- [ ] separate whole renderer features away from modules like `TimelineSemaphores` into an array. (settings)
	- [ ] resource manager
	- [ ] Buffer device address (BDA) for vertex buffers
	- [ ] descriptor sets
		- [ ] create global descriptor set (time, settings, etc)
		- [ ] texture set
			- [ ] massive texture buffer
		- [ ] sampler set
		- [ ] per object data
	- [ ] bring mesh component up to speed with the new system
	- [ ] try to add normal rasterizer to the new renderer ecosystem 
	- [ ] figure out how to blend the game render and UI render
	- [ ] fix normal rasterizer
	- [ ] Lazy renderer
		- [ ] draw only if the renderer was marked dirty
	- [ ] separate queue allocation
		- [ ] fix
		   `destinationStage = PipelineStageFlags.AllCommandsBit;`
		   to
		   `destinationStage = PipelineStageFlags.FragmentShaderBit;`
		   this fix will need to move texture assigning on the graphics queue instead of the transfer queue.
	- [ ] Vulkan module upgrade
		- [ ] figure out how to do GPU occlusion culling
			- [ ] after buffered descriptor sets
			- [ ] compute shaders. this CAN create a few independent simultaneously executing branches
		- [ ] try to figure out a way to better differentiate between renderer types (compute, ray trace, raster).
		- [ ] shared resources
		- [ ] research making shader resources cache friendly
	- [ ] update command buffers. Have one persistent one and copy it over to the others instead of updating every one each time before a new frame
- [ ] Render graph upgrade *(non-essential)*
	- [ ] garbage collector
- [ ] Renderer upgrade *(non-essential)*
	- [ ] LODs. First person/ non fps mesh details

---
# PHASE E — Physics + audio engine + gameplay foundation (2027–2028)
- [ ] AVBD physics engine — broadphase, narrowphase, solver; real physics thread replaces the 32 ms sleep stub
	- [ ] benchmark vs Jolt (JoltPhysicsSharp as dev/test-only dependency — comparison harness, not shipped)
- [ ] Character controller + ballistics raycasts
- [ ] Audio engine — mixing, 3D spatialization, occlusion; upgrades the Phase C playback layer in place

---
# PHASE F — The game (2028–2030)
- [ ] World streaming
- [ ] Procedural:
	- [ ] land generation
	- [ ] prop placement
- [ ] Decal placement
- [ ] AI
- [ ] Inventory/stash UI
- [ ] Netcode on the snapshot-ready ECS (~year 3)

---
# VIDEO / CONTENT (not engine work)
- [ ] VIDEO/BLENDER/OBSIDIAN
	- [ ] MTSDF
		- [ ] Revise the video - SHOW - DON'T WRITE
		- [ ] Editing
			- [ ] Finish up blocking out part 5
			- [ ] Finish up blocking out part 6
			- [ ] Finish up blocking out part 7
		- [ ] Voice Overs
			- [ ] Test render of part 1
		- [ ] Render
	- [ ] UI XSD/XML
		- [ ] Script
		- [ ] Editing
			- [ ] Blockout
		- [ ] Voice over
- [ ] Kebabaičių Season 10
	- [ ] Klausimynas 1
	- [ ] Klausimynas 2
- [ ] Polaris Crash Course

---
# DOCUMENT
- [ ] UI
	- [ ] Controls
		- [ ] Default
		- [ ] Containers
- [x] Registry
- [x] Bootstrapper
- [x] Keybinds
- [x] Context
- [x] XSD
	- [x] Code
	- [x] System
- [ ] Renderer
	- [x] The Renderer system
	- [ ] Rasterizer
	- [ ] Lazy renderer
	- [ ] document what i have now. basically make a Vulkan guide for myself
		- [x] each step of the renderer
		- [ ] each small detail as to why that over that
		- [ ] design patterns why they were made
			- [x] descriptor sets

---
# WHENEVER / RESEARCH
- [ ] Figure out a better system for XML XElements than LINQ. CAUSE APPARENTLY ITS IN THERE.
	- [ ] recreate XML parsing myself.
	- [ ] MAYBE recreate XSD parsing and writing myself
	- [ ] MAYBE recreate all XML/XSD logic myself
- [ ] Research profiling
	- [x] light research
	- [ ] production research
- [ ] Roslyn generation update. This is (almost) necessary (for now) for compile to native.
	- [ ] or add compiler tags to not trim the classes from active running
	- [ ] all of xsd and xml
		- [ ] XSD generator
		- [ ] XML parsing
		- [ ] Bootstrapper
		- [ ] Registry
			- [ ] remake so the build generates an actual dictionary like that and its not driven by string name but by enum
- [ ] rendering
	- [ ] fix ray-tracer
	- [ ] fix and optimize 2D radiance cascades
		- [ ] figure out how to make it nicer
		- [ ] transfer it i to 3d (magistras)
- [ ] Gaussian splats for foliage [[Gaussian Splats for games]]
- [ ] Render Graph
- [ ] home audio system controller
- [ ] home LED lighting system controller

---
# Nusiskundimai Blenderiu
- [ ] negaliu procedurally isskaiciuot SDF ir jo displayint *(addressed by Phase C procedural geometry/SDF evaluation)*
