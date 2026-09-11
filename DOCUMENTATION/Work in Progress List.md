# ROADMAP
Phases, dates and standing decisions live in [[Roadmap]]. Items below are grouped by phase; video/content work and research are separate sections at the bottom.
This file holds **open work**. A landed entry moves to [[Changelog]]; one that still has open children stays here until they close. Keep an open entry to one line — anything longer earns a `ClaudeMemory/Decisions/` note and points at it. See the `aurora-docs` skill.

---
# PHASE A — Thorium MVP + engine hygiene (now → ~Jul 2026)
- [x] **UI Engine landing 6a — the base gap (2026-09-06)** — landed, See `ClaudeMemory/Decisions/ui-engine-stack.md`
	- [ ] **landings 6b–6d: 6b is across (6b3, 2026-09-12)** — chrome, containers, tabs, files, menus, the settings interactables, the drag ghost. Left: deleting `NextProbe.ui.xml`, 6c text/documents, 6c2 the secondary windows, hosts, tear-off and `SessionLayout` (moved to the end, user 2026-09-11), then 6d the delete. Per-landing detail in `ClaudeMemory/Context/ui-engine-plan.md`
	- [ ] **the UI camera reads a `UIElements` row on the render thread** — `WindowRoot.ViewportSize` reads `preferredWidth` past its `!autoscaling` short-circuit, so the first autoscaling window asserts the way the ghost did → `render-thread-reads-pool-row`
	- [ ] **6b3's splits are not GUI-verified** — Tab ▸ Split, View ▸ Split, Ctrl+\ / Ctrl+Shift+\ and Close on an inactive tab all need two tabs in one view, which only a drag makes → `next-context-menus`
	- [ ] **no XML event attribute binds on the new stack** — `ResolveAttributes` binds an `Action`-typed member and `Control`'s handlers are `Func<PointerEvent,bool>`, so `onClick` and the `Bubble*` flags have nothing to bind to. Either `Control` grows `Action` events beside the funcs, or the parser wraps a tagged method and `BubbleClick="true"` comes to mean "return false" — which changes what every `Bubble*` already in a `*.ui.xml` does. Blocks 6b
	- [ ] **the drag reaches its claimant; the drop half is still missing** — a left press on a control whose new `draggable` flag is set calls `StartDrag()` from the base `OnPointerPress`, publishing the `NextDragging` context and telling the parent `ChildDraggedOut`; `UIEngine.CheckDrag` then runs each tick from `Poll`, holding **one drag target the way one control is hovered** and bubbling `DraggingOverStart` / `DraggingOver` / `DraggingOverEnd` from it; a left release calls `UIEngine.EndDrag(point)` **ahead of the release guards** (a click only counts where it started, a drag ends wherever the pointer got to), which fires `DraggingOverEnd`, then `Control.FinishDrag(dragged, point)` on the target, then clears both. `HitTest` gained a `skip` parameter, rejected at the subtree root, so the dragged control *and everything under it* stay out of the answer — the old stack skips nothing and only survives because `TabViewControl` happens to be an ancestor of the button being dragged. Landed since: per-tick delivery (`onDrag`/`onDragStop`/`SolveDrag`), the stale-release guard, and `Poll`'s `ownsDrag` exemption with `WindowOf` — `NextWindowFrameControl`, `NextSplitterControl` and `NextScrollThumbControl` all run on them. Missing and load-bearing: **the drop does not walk up**, where `OfferDrop` offered each ancestor in turn; and `EndDrag` clears `NextDragging` *after* the callbacks. Seven controls claim a drag in the old stack (`TabStripButtonControl`, `ScrollThumbControl`, `SplitterControl`, `DocumentEditorControl`, `TextBoxControl`, `WindowFrameControl`'s grips, Carbon's `SpanChartControl` twice) and **five need only per-tick delivery**, which now exists — `ScrollThumbControl`, `SplitterControl` and `WindowFrameControl`'s grips are ported and running on it; `DocumentEditorControl` and `TextBoxControl` wait on 6c. The drop, hint and ghost half (`HitFor`, `WindowAt`, `OfferDrop`, `UpdateDropHint`, the `NextHinted` context, `RaiseHovered`, `DragGhost`, `draggingOpacity`) exists for tab dragging alone: `TabViewControl` is the repo's only `ResolveDrop`/`ResolveDropHint` implementor and `TabStripButtonControl` its only `DragGhost.Show` caller. Full table in `ClaudeMemory/Decisions/ui-engine-stack.md` § the drag gap
	- [ ] **context menu gaps** — no monitor clamping or flipping, no Escape or keyboard, a windowed panel renders 1:1 under autoscale, an in-window open or close re-lays out the whole window → `next-context-menus`
	- [ ] **neither `WindowRoot` nor `ContainerControl` arranges more than one child** — the container base only lifts the one-child restriction; it inherits the single-child measure and arrange, so the new stack still cannot lay out a list until a 6b subclass does it
	- [ ] **`WindowRoot.Arrange` ignores a child's `margin`** — landing 2 behaviour, found by 6a's probe when a `Margin="0,0,56,0"` swatch arranged flush to the padding edge
	- [ ] **`UIEngineModule` re-records its command buffer every frame** — landing 1 scaffolding that contradicts the record-only-when-dirty rule. It is what makes a range change land today
	- [ ] **the old stack does not re-lay out on a `MoveWindow` resize** — content stays at the previous width and clips, while the new stack refits from the next line of the same GLFW callback. Reproduced on the commit before landing 2, so it predates the rework
	- [ ] **a run's per-character settings have no XML shape yet** — bold, colour, italic, size and animation as spans over a string. Nothing in the document format expresses it, and it is a landing-4 design job
	- [ ] **`WindowFrameControl`'s maximized grips still need context logic** — 6a ported `hitTestable`, so the grips can opt out of the test again, but opting out hands their pixels to a *sibling drawn behind them* and the new walk resolves overlapping siblings by paint order, which is not the same thing. Deferred to context logic (user, 2026-09-06)
	- [ ] **selection and cross-block caret placement are deferred, each on a specific blocker** — `SelectionControl` needs drag ported before anything can drive it, and `DocumentEditorControl.CaretAtPoint` resolves a point across *blocks*, of which the new stack has none. The per-run `IndexAt` exists and is what the caret uses today
	- [ ] **the font system has no home once `Core.UISystem` is deleted** — `Core.UI` compiles against `TextMeasurer`, `FontStyle`, `Glyph`, `AtlasMetaData` and `GlyphControl`'s cell constants, none of which are the control stack. L6 deletes the namespace around them and nothing decides where they go
- [ ] **ticking should be a group, not a flag** — `Engine.Interpolate` iterates *every* entity and virtual-calls `OnTick` on the tickable ones, and `Control : Entity` has now put every UI element in that loop. At the plan's 1M-element target that is ~1M iterations per tick to find the few controls animating. `EntityRegistry` already has groups; an entity that needs ticking joins `"Tickable"` and the loop iterates that instead. Raised and parked (user, 2026-09-06) — will not measure at today's ~1,418 controls. See `ClaudeMemory/Decisions/entity-tick-group.md`
- [ ] **`_components` and `children` are allocated on every entity whether used or not** — 32 B each, 64 B per entity, ~64 MB at 1M. User chose to leave both eager (2026-09-06); `_components` is read at 13 sites all inside `Entity`, `children` at 136 sites across 37 files, most of them in the UI stack being deleted
- [ ] **`DataPool` has no `Write<T>(handle, value)`** — assign plus `MarkContentDirty` in one call. Every caller hand-rolls it
- [ ] **decide what Alt+F4 does.** Nothing polls `WindowShouldClose` and no GLFW close callback is registered, so the OS close request is ignored outright today. Leading option (user, 2026-08-21): **intercept it and let it do nothing**, the way Valve's games do. Alternatives are routing it through `Shutdown.Request()` like the X button, or quitting outright. Worth deciding alongside whether the taskbar close and Alt+F4 should differ
- [x] **session restore (2026-08-31)** — landed, See `ClaudeMemory/Decisions/session-restore.md`
	- [ ] **the primary window's rect is applied after `Engine.Init` returns**, so it shows at the `GraphicsSettings` size for the ~800ms of bootstrap and then jumps. `SettingsRegistry.LoadAll` is the first bootstrap step and `InitWindowing` follows it, so nothing can read the session earlier. Fix is either creating the primary hidden and showing it after restore, or sourcing the scope key from somewhere available before `LoadAll`
	- [ ] **switching vaults does not capture the vault being left** — `VaultsWindow.Switch` closes every tab and writes the setting, and capture only ever runs at shutdown, so switching away and quitting elsewhere loses the old vault's arrangement. Fix is a `SessionLayout.Capture()` in `Switch` before `CloseTabs`, against the outgoing scope. Related and pre-existing: `Switch` only closes tabs in `Engine.primary`, so a torn-off window keeps notes from the vault that was left and capture then files them under the new vault's key
	- [ ] caret position, scroll offset and selection are not recorded, only the ordered tab paths and the active one; an iconified window comes back normal; a window whose every recorded note was deleted comes back as an empty pane rather than not at all
- [ ] bootstrap and shutdown steps report success unconditionally — the `bool` is wired end to end but no step actually detects its own failure yet
- [ ] Test/profiling platform — built **on the UI**, scheduled **after the text editor's first version**: GC/allocation, execution time, and general "does it work" checks. Dogfoods the UI while doubling as the profiler. Engine work stays manually GUI-verified until then. (Supersedes the headless `AuroraTesting` console runner.)
- [ ] UI collision
	- [ ] add handle states - game, ui etc
	- [ ] Update engine class so the mouse inputs are handled in input handler
- [ ] **context menus get redone, and the factory goes first** (user, 2026-08-28) — redone on the new stack 2026-09-11 with no factory (inline when it fits, windowed when not). The old stack's `ContextMenus.menuFactory`, the solution's only factory, dies with it at 6d → `next-context-menus`
- [ ] **`TextInputControl.ResolveOnClick` swallows every click** — it calls `BeginEdit()` and returns without calling base, so `bubbleClick` is dead on it whatever the XML says. Any control nested inside a `TextInput` cannot receive a click. Worked around by adding `LabelControl` for non-editable text rather than changing the override; the swallow is still there
- [ ] **`StackPanelControl.Measure` pollutes `maxCross` from star children** — pass 1 measures a star child with `0` on the main axis, so a text child wraps to one character per line and reports a huge cross size; pass 2 only ever `Max`es `maxCross` and never re-bases it against the real star width. An 8-character label made a 32px title bar 168px tall. Dodge is an explicit size on every child
- [ ] **keybinds bind to a declared intent, not straight to an action** (user, 2026-08-31) — one `<AbstractKeybind>` names the intent and N `<Keybind Trigger= Bind=>` point at it; today numpad Enter duplicates `Action="Text.NewBlock"` and `Rebind` makes it fire twice. See `ClaudeMemory/Context/keybind-intent-plan.md`
- [ ] text editor
	- [ ] fix beziers
	- [ ] add the rest of the alphabet (eu languages)
		- [ ] create language packs?
	- [ ] editor
		- [ ] Markdown insertions
		- [ ] glyph ceiling — every character is a `GlyphControl`, always (~56.7k on the 400-block note, past `UIModule`'s 50,000 cap). Accepted knowingly. **The UI data/visualization split does not fix this** — one control per element means the count is unchanged; the two share a cause but are separate problems. Escape hatch that does not change the design: a run holds `text` + its `BlockLayout` with no glyph children and calls `SyncGlyphs()` when visible
		- [ ] P4 — selection + Ctrl+B/I run split/merge
			- [x] **Ctrl+B/I over the range, and the format bar (2026-08-30)** — landed, See `ClaudeMemory/Decisions/document-format-bar.md`
				- [x] **a style chosen with nothing selected is armed, not discarded (2026-08-31)** — landed, See `ClaudeMemory/Decisions/armed-style-at-the-caret.md`
					- [ ] an arm dies on **any** caret move, Enter and Backspace included; Word keeps it across both, which needs the arm pinned to a caret position rather than to "the caret has not moved"
				- [ ] `Text.Bold`/`Text.Italic` toggle from the selection's **first** run, so a mixed selection flips to the opposite of whatever that run was rather than to all-on
				- [ ] strikethrough is now the only one of the three still declared and unread
			- [x] **undo/redo (2026-08-22)** — landed, See `ClaudeMemory/Decisions/document-undo.md`
				- [ ] `InsertFragment` is the piece to distrust — ~50 lines that exist only as the inverse of a delete, called by nothing else until paste lands. Cross-block with a destroyed tail run is where to look first. Holding Backspace is one step per repeat firing (~60 for a two-second hold against a 500-step cap, and worth re-checking now that repeat fires at all); undo restores the caret but not the selection, and does not reach `TextBoxControl`
		- [ ] **the engine never recovers a key released while unfocused** — `Text.Write` is bound `<Continuous />`, so a key whose release went to another window keeps firing and pours characters into the focused note. Found because `SendKeys` triggers it every time the prompt steals focus mid-keystroke; same class as the stale-release `SolveDrag` already guards. Any GUI probe of text must use explicit key-up, and must restore any note it writes
		- [ ] nothing prompts for a note with no file. `TextBoxControl` has no clipboard, no double-click-select-word and no horizontal scroll past its width — the rename field runs its text under the clip rather than following the caret. A clean note is still rewritten on tab close (byte-identical, but it touches mtime)
		- [ ] L3 — page system (paginator over measured lines + page chrome; pageless is the L1 default mode)
		- [ ] code blocks (B1 — monospace, no wrap, view-time syntax coloring)
		- [ ] custom expressions (maths)
	- [ ] **the `"window"` menu is the whole app's fallback** — `UI.xml` names `ContextMenu="window"` on the root `<Window>`, so right-clicking a splitter or the outer panel still offers Minimize/Maximize/Close; `TabWindow.xml` names it nowhere, so a torn-off window offers them *not even on its own title bar*. Fix is moving the attribute onto `<TitleBar>` in both. Deferred 2026-08-20 — the crash is fixed, the placement is a separate call
	- [ ] no selection highlight; nothing watches the vault folder, so a note added on disk shows up only after a toggle rebuilds; `FileObject.icon` is still set by nothing; clicking a row while renaming commits the edit *and* opens that row's note
	- [ ] **the editor's folder browser is not built** — double-clickable folders that open inside, over the same `FileObject` model. No longer blocked: `ResolveOnDoubleClick` is dispatched as of the tab-rename pass, and nothing wires a folder row to it yet. Also needs a project-root notion; `AuroraEditor` has no settings category and its `UI.xml` is nine coloured buttons
	- [ ] dragging a maximized window moves it while still maximized instead of restoring first
	- [ ] captions are ASCII (`-`, `[]`, `X`) because the atlas has no `−`, `□` or `✕`
	- [ ] **custom cursors defined in XML**, alongside the standard GLFW shapes — an asset naming its image and hotspot, resolved the same way masks and textures already are, so a control can name a cursor instead of picking from the fixed `CursorShape` enum. Include **animated cursors** (a frame list plus a frame time) in the same design rather than bolting them on after; GLFW has no animated cursor, so the engine drives frames itself by re-applying the cursor on a timer
	- [x] hover/press feedback — landed, See `ClaudeMemory/Decisions/button-states-and-hover-bubbling.md`
		- [ ] `Context.Set` still does not fire the `IContext` callbacks, so `SolveHover`, `SolveLMBPress`, `SetDragging`, `UpdateDropHint` and `ClearDropHint` each carry their own copy of the add/remove dance — five sites since `hinted` and `pressTarget` became the `Hinted` and `PressTarget` contexts (2026-08-27). Every setter now goes through the funnel: `activeControl` since the declared-contexts pass (2026-08-22), `dragging`, `Hinted` and `PressTarget` since 2026-08-27; only `Forget` still writes directly, deliberately, so a teardown runs no derivation. What is left is unifying the callbacks into `Set`, plus routing `DocumentControl`'s caret repoint through it
		- [ ] dragging off a held button and back on re-activates on release (correct) but the press tint does not come back — `ResolveOnEnter` sets `hovered` and nothing tells it the button is still down; `IsKeyDown(Keys.MouseLeft)` is what it would read. Cosmetic
		- [ ] a button held while the pointer leaves the **window** keeps its press tint — `Engine.HandleUI` returns before `SolveHover` when `isInWindow` is false, so no exit fires. The GLFW cursor-enter callback is where it would go
	- [ ] Claude, chatgpt, other chatbot integrations.
	- [ ] text upgrade
		- [ ] simple color — the format bar's colour dropdown applies one to the selection through `StyleDelta`; picking an entry is not GUI-verified, and there is no custom-colour entry
		- [x] **gradient (2026-08-22)** — landed, See `ClaudeMemory/Decisions/ui-gradients.md`
			- [ ] a gradient cannot cross runs — a heading built from two runs gets two ramps. `GradientSpace="Self|Inherit"` on `VulkanControl`, letting the `arrangedRect` setter take the parent's rect, is the ~5-line generic fix; not built without a use for it
			- [ ] the table uploads once and is never rewritten, so a gradient cannot animate or be edited at runtime. `MCUI.CreateGradientTable` is the only writer — a dirty flag away
			- [ ] no gradient on the edge, the outline, or a button's hover/press colour
		- [ ] alignment — needs a block-level line-width pass that has not existed since the L2 revert: runs measure themselves, so no run knows the width of a visual line it shares. Priced separately, deferred (user, 2026-08-30)
		- [ ] horizontal lines (honestly its just a panel)
		- [ ] tables
	- [ ] cursor change on context
- [ ] UI
	- **Standing decision:** glyphs stay full controls with their own mat4 and tint — per-letter colour, rotation and animation are required. Do not propose making them plain data rows
	- [x] edge + outline colour (2026-08-19) — landed, See `ClaudeMemory/Decisions/control-edge-and-outline.md`
		- [ ] outline width is screen px while edge thickness is design px; the fragment stage has no design→screen scale and `fwidth(boxDist)` is not one (unit-gradient SDF, 1.0 axis-aligned vs 1.414 diagonal). Moot while Thorium is `WindowSize`
	- [x] **font atlases were uploading as sRGB (2026-08-19)** — landed, See `ClaudeMemory/Decisions/atlas-is-unorm-not-srgb.md`
		- [ ] the atlas's alpha channel carries no information — it duplicates `median(RGB)` exactly, so a quarter of it is wasted and the `> 0.1` guard can never fire on merit. A real true-SDF alpha is what MTSDF is for, and would give rounder outline joins at acute corners. Generator-side, outside this repo
		- [ ] `pxRange = 4.0` is hardcoded in the Thorium fragment shader and stated nowhere else; `AtlasMetaData` carries only `glyphCount`, `chars` and `glyphs`. A font generated at another range renders wrong with no diagnostic
	- [ ] `UI.frag` MSDF-decodes every control, including plain panels sampling the `invisible` mask. A per-control flag or a second pipeline once `textureIndex` exists
	- [x] **a stack clamps its children in Arrange (2026-08-29)** — landed, See `ClaudeMemory/Decisions/stack-panel-arrange-clamp.md`
		- [ ] **`Measure` still reports the absurd number upward.** A `ScrollableControl` wrapping a stack that holds an unsized child stores `contentSize = MaxValue` and computes a nonsense scroll range from it — the clamp fixes what is drawn, not what is reported. Siblings after the offender also collapse to zero, which is the honest consequence rather than a repair for authoring an unsized child in a bounded stack. **Old stack only** — `NextScrollableControl` guards it past a `float.MaxValue * 0.5f` sentinel (2026-09-08)
	- [ ] **UI animation driver — nothing in the UI can change over time on its own.** No per-frame ticker a control can register a value with; gradients, position/size and colour all want one, and overscroll's rubber-band was rejected for its absence. See `ClaudeMemory/Context/ui-animation-plan.md`
	- [ ] **an in-engine file browser, to retire the OS folder dialog (2026-08-30)** — `FolderPicker` is Win32 `IFileOpenDialog` on an STA thread, the first hard blocker under the engine's own portability. See `ClaudeMemory/Context/file-chooser-plan.md`
	- [ ] control frustum culling — engine-wide cull of off-screen controls; note it cuts *draw* work only, so it is not an answer to the glyph ceiling — culled controls keep their entity and their pool row
	- [ ] **UI data/visualization split (2026-08-17)** — most of the UI becomes data, controls become visualization; sequenced after Thorium v1 and the profiler. See `ClaudeMemory/Decisions/ui-data-control-split.md`
	- [ ] fix up UI shaders (samplers, transparency)
	- [ ] checkout `Pretext` by Cheng Lou for UI layout calculations (apparently 500x faster than the current implementation)	
	- [ ] **engine defect — a star child poisons a `StackPanelControl`'s cross measurement** (found 2026-08-19 building the tab strip). `Measure` pass 1 probes each star child at **0** on the main axis (`crossOffer` is `(0, inner.height)` horizontally) purely to learn its cross size; pass 2 re-measures at the real allocation. But `maxCross` is a running `MathF.Max` across both passes, so any control whose cross size depends on its main size — i.e. all text — contributes its pass-1 value forever. A 10-character caption measured at content width `-8` wraps one character per line and reported **168px** tall instead of 21; the panel arranged itself 168 tall and centred, putting a sibling 70px above the strip where the clip discarded it. Worked around by pinning `preferredHeight` on the star child, which makes `VulkanControl.Measure` skip the child-driven height. Real fix is for pass 2 to *replace* each star child's cross contribution rather than max against the probe, or for pass 1 to skip cross measurement of star children entirely
	- [x] **double-click a tab to rename its note (2026-08-21)** — landed, See `ClaudeMemory/Decisions/tab-rename-and-double-click.md`
		- [ ] a rename opened while another is live is lost — the first one's commit calls `Retitle`, which rebuilds the strip and destroys the field the second one was just opened on. Any `RebuildStrip()` does it, opening or closing a tab included; the browser row has the same hole
	- [ ] strip does not scroll — tabs are a fixed `TabWidth` and the strip clips, so past `width / TabWidth` the rest are unreachable. No drag-to-reorder, no `+`, no keyboard switching, no persisting open tabs across runs
	- [ ] **switching notes no longer saves the one being left** — `vault-browser-and-shell` decision 4 saved because the note was about to be discarded; with tabs it stays live, so saving moved to close. Thorium still has no dirty tracking and no autosave, so a crash with several tabs open loses more than it did before
- [ ] fix resolution stuff associated with DPI and stuff. use `glfwGetMonitorContentScale` *(non-essential)*

---
# PHASE B — ECS rework + renderer/settings foundation (~Jul–Sep 2026)
- [ ] renderer foundation
	- [ ] figure out why the renderer breaks the second monitor
	- [ ] **oversight — device creation hardcodes two queue families.** `Renderer.CreateLogicalDevice` builds a graphics and a transfer `DeviceQueueCreateInfo` and passes `QueueCreateInfoCount = 2` unconditionally, but `QueueAllocator` resolves each flag to whichever family carries the fewest extra bits — on a GPU exposing a single universal family, which is most Adreno and Mali, both resolve to index 0, and duplicate family indices in `pQueueCreateInfos` are invalid, so `vkCreateDevice` fails outright. Blocks Android before anything else gets a chance to. Fix is to dedupe the indices and size the array to the distinct set, cheap once the per-frame buffer work has reduced the transfer queue to load-time uploads only
- [ ] Engine settings/preferences — XSD/XML-driven (GPU device selection, CPU/thread counts, misc engine options)
	- [ ] **oversight — settings have no Cancel.** A settings screen writes the live `Setting`, so a value takes effect the moment a widget moves; `Commit()` (`Apply` + `SaveAll`) gates only the *action* and the persistence. A screen offering Cancel has to restore the old values itself, and a reader mid-edit sees a half-edited category (only `VSync` is read late enough to notice). Fix when a settings screen exists: edit a cloned category and commit it on OK, the way `DocumentEditSession` already edits a note. See `ClaudeMemory/Decisions/settings-categories.md` decision 7
- [ ] figure out a way to do UITrees (save only tree tops in the registry)
- [x] **a swap is a decoration action, and the engine ships the settings shell (2026-08-29)** — landed, See `ClaudeMemory/Decisions/ui-document-registry.md`
	- [ ] **no pool-count evidence for a swap.** Nothing logs `UIControls` occupancy, so the round trip is "no FATAL and the app stayed alive", not the `1325/1325` check the landing pass used
	- [ ] **Thorium's `Settings.xml` is a byte-identical duplicate** of the engine's now. It shadows the engine copy, so Thorium never exercises the fallback; deleting it would prove the fallback and drop the duplication
- [ ] **`uiRoot = uiRoot` would destroy the live tree.** No call site self-assigns and `SetUI` structurally cannot, so no guard was added; the setter is public, so it is a hazard rather than a bug
- [ ] no hot-reload — nothing re-reads a document when the file on disk changes
- [ ] **the editor renders all text as solid blobs** — layout is right, blob widths track caption lengths, so it is the editor's atlas or its `.spv`, not the layout. It baked its fonts fresh this session. No before-picture exists to compare against, since nothing in its old UI drew any text
- [ ] **no per-channel level overrides.** `SettingCategory` resolves children by name against its `Setting` fields, so a repeated `<Channel Name= Level=/>` overwrites one instance. Fits as a sibling `<LogChannels>` group on the non-category `ISettingsGroup` path, which already handles child lists
- [ ] **`ThreadedSystem.Send` backpressure is still silent** — four `false` returns are four dropped writes, unlogged. Zero callers today, so nothing was wired; do it when ECS systems start cross-writing
- [ ] **log viewer in Thorium — planned 2026-08-22, not started.** One `LogViewControl` with two feeds: a live `MemorySink` ring and the tail of a log file. See `ClaudeMemory/Context/log-viewer-plan.md`

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
- [ ] `AuroraMotion` host project (same pattern as `Thorium`: thin app over the engine)

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
	- [ ] **an entity registry group of top-level controls**, so a window can grab or reference a root without walking the tree — wanted by multi-windowing slices 1, 3 and 5, and retires the `FindByName` walks that open a note in the wrong window. See `ClaudeMemory/Context/multi-windowing-plan.md`
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
- [ ] Renderer
	- [ ] Rasterizer
	- [ ] Lazy renderer
	- [ ] document what i have now. basically make a Vulkan guide for myself
		- [ ] each small detail as to why that over that
		- [ ] design patterns why they were made
---
# WHENEVER / RESEARCH
- [ ] Figure out a better system for XML XElements than LINQ. CAUSE APPARENTLY ITS IN THERE.
	- [ ] recreate XML parsing myself.
	- [ ] MAYBE recreate XSD parsing and writing myself
	- [ ] MAYBE recreate all XML/XSD logic myself
- [ ] Research profiling
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
