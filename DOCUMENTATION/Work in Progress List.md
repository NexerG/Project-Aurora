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
		- [x] L1 — layout engine (pageless): `TextMeasurer` is now the only wrapper for all text, engine-wide. Every text control holds its own `BlockLayout` and answers `OffsetAt`/`CaretAt` for itself; the `DocumentLayoutCache` half is deleted. Geometry verified by caret round-trip on real font metrics — 607/607 exact
		- [x] ~~L2 — virtualized view~~ **REVERTED 2026-08-07.** One layout path for all text; the document is a plain control tree (`DocumentEditorControl` → `DocumentControl` → `Block` → `TextRun` → glyphs). `TextRunControl`, `DocumentCanvasControl` and `DocumentLayoutCache` deleted; each control holds its own `BlockLayout`. Wrapping stays word-boundary. Save round-trips and reloads. See `ClaudeMemory/Decisions/text-layout-one-measurer.md`
			- [x] `runColorHex` reaches no glyph — deleted; a run carries the ordinary `ColorHex`, `controlColorHex` is `virtual` and `TextControl` overrides it to repoint the glyphs. Unblocked by making the note writer skip defaults instead of filtering out everything declared on `VulkanControl`. See `ClaudeMemory/Decisions/xml-save-skips-defaults.md`. Verified 25/25 on a harness booting the real engine — save omits every default, `ColorHex` round-trips, glyphs colour in both attribute orders
			- [ ] glyph ceiling — every character is a `GlyphControl`, always (~56.7k on the 400-block note, past `UIModule`'s 50,000 cap). Accepted knowingly. **The UI data/visualization split does not fix this** — one control per element means the count is unchanged; the two share a cause but are separate problems. Escape hatch that does not change the design: a run holds `text` + its `BlockLayout` with no glyph children and calls `SyncGlyphs()` when visible
		- [x] P3 — editing on control-local layout
			- [x] click → caret: clicks bubble glyph→run→block→`DocumentControl`→editor, position from `TextControl.OffsetAt`, caret placed from `CaretAt`. Round-trip verified exact (607/607 sample)
			- [x] editability boundary — only `DocumentEditorControl` turns a left-click into a caret, so doc names/toolbars are never editable by accident
			- [x] redraw-on-write — `GlyphControl.SetCharacter` repoints a glyph instead of replacing it, so `SyncGlyphs` only adds/removes at the tail and adjusts the interior in place. Was one pool alloc + deferred free + O(n) list removal + a DFS permute *per character after the edit*, per keystroke. Engine-wide, not document-only. Compile-verified, not GUI-verified
			- [x] char input — works. A glyph's parent is a `TextRun : TextInputControl : TextControl` again, so `Decorations.Write`'s cast succeeds; the editor sets `cursorPosition` and calls `BeginEdit()` on click. Drain still lives in the app, not the engine, and `ICharacterInput` still does *not* exist
			- [x] `DocumentEditSession` + Ctrl+S writes XML — the session is `{document, path}`, **not** a working copy: the model is the control tree, so a clone would be a second control tree against the glyph ceiling. Revert is a reload. Save had to learn that a run's `FontSize` is written by `ApplyLayout` and is not authored content, or the first Ctrl+S pinned every note to that day's styling scheme. See `ClaudeMemory/Decisions/engine-side-text-input.md`
			- [x] special keys — arrows/Home/End/PageUp/PageDown. Left/right walk runs in document order and treat a run boundary inside a block as one caret slot; everything else resolves a point against every *line of every run*, because a visual line spans runs. Line start/end are the visual line's. Compile-verified, NOT GUI-verified
			- [x] input moved engine-side — `TextInputActions` holds `Text.Write` (out of `Periodic.Decorations`), `Text.Save` and eight `Text.Caret*`; a host declares the keys in XML and writes no input code. `ICharacterInput` still does not exist
			- [x] three pre-existing `DocumentXml.Save` defects found while building Ctrl+S, latent until Save became reachable — the resolved `ColorHex` written beside the `ControlColor` that produced it (skipped only when the enum is itself written, or an authored hex on a default-coloured control would be lost); `Bold="True"`, which `xs:boolean` rejects; and an empty `<DocumentLayout />` appended to every save. Load→save→load→save is now byte-identical between passes. `SettingsRegistry.WriteDiff` still has the same bool defect — `<VSync On="True"/>`
		- [ ] P4 — selection + Ctrl+B/I run split/merge
			- [x] selection exists and renders — anchor + the caret as focus, slots normalized so a run boundary is one point and `anchor == focus` means nothing selected, one `SelectionControl` box per visual line with its x span from `CaretAt`, reused and inserted at the head of `children` so they draw behind the text. Drag (incl. auto-scroll) and shift+click/arrows. Mutates nothing. Compile-verified, **NOT GUI-verified**. See `ClaudeMemory/Decisions/document-selection.md`
			- [x] engine drag lifecycle — `UICollisionHandling.dragging` was declared and read but **never assigned anywhere**, so `ResolveDrag` had never fired and `ResizeableControl`'s drag handler was dead. `VulkanControl.StartDrag()` + clearing the field on release closes it; opt-in from a click handler, so `ResizeableControl` stays dead until someone adds the call
			- [ ] selection-aware editing — typing and Backspace over a range. The hard half is cross-block delete: merge the last block's tail into the first and destroy the ones between, which is block-level structural editing nothing does yet
			- [ ] Ctrl+B/I over the range — note `bold`/`italic` are declared on `TextInputControl` and **nothing reads them**; `arial-b` is a separate font asset, so Ctrl+B is a `fontName` swap and the run split has to carry it (`StyleEquals` already compares it)
		- [ ] right-click rename via `ContextMenuControl` (stub exists — its `Open()` only logs)
		- [ ] L3 — page system (paginator over measured lines + page chrome; pageless is the L1 default mode)
		- [ ] code blocks (B1 — monospace, no wrap, view-time syntax coloring)
		- [ ] custom expressions (maths)
	- [ ] Project browser
	- [ ] Claude, chatgpt, other chatbot integrations.
	- [ ] text upgrade
		- [x] styling types — a block/run names a `StylingType` (`Text`, `Heading1-6`, `Comment`, `Code`, `Quote`) and `DocumentStyles.xml` sizes it; `HeadingBlock`/`ParagraphBlock` collapsed into one `<Block>`, `HeadingStyle` → `TextStyle`. A run's type overrides its block's; a heading past the scheme takes the last one. See `ClaudeMemory/Decisions/text-styling-types.md`
		- [ ] simple color
		- [ ] gradient
		- [ ] bold/italics
		- [ ] horizontal lines (honestly its just a panel)
		- [ ] tables
	- [ ] cursor change on context
- [ ] UI
	- [x] texture table — sampler array is indexed by *texture* (`ControlData.textureIndex`) instead of by instance (`gl_InstanceIndex`), so the descriptor cap is "distinct textures" (256) rather than "controls" (was 50,000). Every glyph in a font shares one slot, and control churn no longer writes a descriptor at all — only a pool capacity change rebuilds. Also cleared the 44-byte `ControlData` stride alignment validation error (now 48). Phase D's "texture set" arriving early. See `DOCUMENTATION/ClaudeMemory/Decisions/glyphs-as-pool-data.md`. **Runs clean with validation on; NOT yet eyeballed**
		- **Standing decision:** glyphs stay full controls with their own mat4 and tint — per-letter colour, rotation and animation are required. Do not propose making them plain data rows
		- [x] windowing modes — a `Window` root names `WindowingMode` (`KeepLocal`/`ScaleUp`/`WindowSize`, default `WindowSize`) and the mode picks one box that is used twice: as the UI ortho projection and as the root's arranged rect. `ScaleUp` derives its scale from `ContentScalingMode`, so `Vertical` keeps pixels square and lets an ultrawide show more instead of stretching. Fixes controls sticking to the XML resolution in borderless; the miss was that `VulkanControl.Measure` caps at `preferredWidth`, so the root needed its own `Measure`. Dead `fillWindow` deleted. See `ClaudeMemory/Decisions/window-scaling-modes.md`
		- [ ] clipping — every `Arrange` computes `ClipRect` and its only consumer `VulkanControl.HitTest` has **zero callers**; no scissor, no shader clip. Scrolled document text is not actually cut at the viewport today
		- [ ] `UI.frag` MSDF-decodes every control, including plain panels sampling the `invisible` mask. A per-control flag or a second pipeline once `textureIndex` exists
	- [ ] control frustum culling — engine-wide cull of off-screen controls; note it cuts *draw* work only, so it is not an answer to the glyph ceiling — culled controls keep their entity and their pool row
	- [ ] **UI data/visualization split (2026-08-17)** — data and controls separate: most of the UI becomes data, controls become visualization. Replaces the reverted L2 virtualization, generalized off the document onto the whole UI. **Sequenced last: finish the UI as it is → Periodic v1 + the profiler built on that UI → then redo the engine's UI.** Shape settled: the parent/child tree becomes data too (**reversing** `ecs-rework-data-pools`'s "the UI tree stays OO"), a control stays one object per element presenting its row, and it all rides the existing `UIControls` pool. Buys flat forward-loop layout and hit-test in DFS order and one source of truth; does **not** lower the control count. Open: strings and variable-length members in columns, and per-type `Measure`/`Arrange` becoming a switch. See `ClaudeMemory/Decisions/ui-data-control-split.md`
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
	- [x] settings system — `ISettingsGroup` + `[A_XSDType(name, "Settings")]`, discovered by reflection, values cascading per attribute over `Data/XML/Settings/*.xml` across mounts and the host's write root, saved back as a diff. `DocumentStyles.xml` folded into it as `DocumentSettings`. See `ClaudeMemory/Decisions/settings-registry.md`
	- [x] settings versioning — `IMigratableSettings` (`version` + `Migrate(from, XElement)`) rewrites a stale stored element before it is read; unclaimed attributes are carried forward through a save rather than erased; a value that no longer converts warns instead of killing bootstrap
	- [x] the engine's own groups — `GraphicsSettings` (`Graphics`): `Device` matched as a device-name substring with warn-and-fallback, `Window` (windowed/borderless/fullscreen plus the size, replacing the hardcoded `Engine.width`/`height`), `Monitor` matched against the EDID panel name Windows reports (GLFW calls every panel "Generic PnP Monitor", so `DisplayNames` joins Win32 to GLFW by virtual-desktop position), `VSync` (Mailbox vs Immediate, Fifo the fallback). No ordering work was needed — `Settings.LoadAll` is already step 1 of `Bootstrap.xml`. Thread counts deliberately skipped: no job system, so there is nothing to count; tick rates and lane sizing stay hardcoded. See `ClaudeMemory/Decisions/settings-registry.md`
	- [x] change notification + build/user scope — `SettingCategory : ISettingsGroup` holds `Setting` children, each carrying `Scope` (`App`/`User`) and an `OnChanged` action. The write root may only override `User` settings and `Save` never writes `App` ones, so a value is hardcoded per *setting* rather than per file. `SettingsRegistry.Apply()` diffs against the last-applied values and fires each distinct action once; `VSync` → `Renderer.RequestSwapchainRebuild`. See `ClaudeMemory/Decisions/settings-categories.md`
	- [ ] **oversight — settings have no Cancel.** A settings screen writes the live `Setting`, so a value takes effect the moment a widget moves; `Commit()` (`Apply` + `SaveAll`) gates only the *action* and the persistence. A screen offering Cancel has to restore the old values itself, and a reader mid-edit sees a half-edited category (only `VSync` is read late enough to notice). Fix when a settings screen exists: edit a cloned category and commit it on OK, the way `DocumentEditSession` already edits a note. See `ClaudeMemory/Decisions/settings-categories.md` decision 7
	- [x] settings rework (2026-08-17) — a setting is now its own type, so it is its own element carrying its own typed attributes (`<VSync On="false"/>`, `<Window Mode Width Height/>`) instead of `<Setting Name Value/>` strings. Declaring one is a field on the category; reads are typed end to end (`.vsync.on`) with no string literals; the schema knows the types again. Setting elements resolve against the category's own fields, so a setting name only has to be unique inside its category. One `UserSettings.xml` in the write root instead of a file per group. See `ClaudeMemory/Decisions/settings-categories.md`
	- [x] input settings (2026-08-17) — `InputSettings` (`Input`): `<DoubleClick Timeout/>` and `<KeyRepeat Delay Rate/>`, replacing the hardcoded `KeyStateTracker.tapWindow` and `RepeatCondition`'s own `Delay`/`Rate`, which are **deleted** — the timings are global and a keybind cannot override them, so a settings screen has one answer instead of two. Read at the point of use rather than seeded at parse time, so a change needs nothing re-seeded. `Engine.doubleClickTime` was already dead and now also duplicates this; left alone. See `ClaudeMemory/Decisions/engine-side-text-input.md`
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
