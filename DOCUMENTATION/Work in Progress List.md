# NON ESSENTIALS
- [ ] fix resolution stuff associated with DPI and stuff. use `glfwGetMonitorContentScale`
- [ ] Registry and Bootstrapper rework
	- [ ] Settings (some day)
- [ ] update logging with .NET 11
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
- [ ] Render graph upgrade
	- [ ] garbage collector
- [ ] XML material directed acyclic graph (DAG)
- [ ] Renderer upgrade
	- [ ] LODs. First person/ non fps mesh details

---
# NEXT
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

- [ ] Text editor
- [ ] Renderer
	- [ ] framebuffer removal
	- [ ] resource manager
	- [ ] Buffer device address (BDA) for vertex buffers
	- [ ] descriptor sets
		- [ ] create global descriptor set (time, settings, etc)
		- [ ] texture set
			- [ ] massive texture buffer
		- [ ] sampler set
		- [ ] per object data

# ESSENTIALS
- [ ] figure out a way to do UITrees (save only tree tops in the registry)
- [ ] UI collision
	- [ ] add handle states - game, ui etc
	- [ ] Update engine class so the mouse inputs are handled in input handler
- [ ] renderer update
	- [ ] separate whole renderer features away from modules like `TimelineSemaphores` into an array. (settings)
	- [ ] `vk_khr_dynamic_rendering` - removes render pass and framebuffer.
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
	- [ ] figure out why the renderer breaks the second monitor
	- [ ] Vulkan module upgrade
		- [ ] figure out how to do GPU occlusion culling
			- [ ] after buffered descriptor sets
			- [ ] compute shaders. this CAN create a few independent simultaneously executing branches
		- [ ] try to figure out a way to better differentiate between renderer types (compute, ray trace, raster).
		- [ ] shared resources
		- [ ] research making shader resources cache friendly
	- [ ] update command buffers. Have one persistent one and copy it over to the others instead of updating every one each time before a new frame
- [ ] UI
	- [ ] fix up UI shaders (samplers, transparency)
	- [ ] checkout `Pretext` by Cheng Lou for UI layout calculations (apparently 500x faster than the current implementation)	
	- [x] Stack panel
	- [x] Grid
		- [x] update grid logic
		- [x] add gaps in between grid cells
		- [x] only one item per grid cell
	- [ ] Window Splitter
	- [x] Scroll
	- [x] UI "start" scaler with multipliers
	- [ ] fix action xsd attribute bcz it dont find actions if theyre not named EXACTLY the same (action - attribute)
- [ ] text editor
	- [ ] fix beziers
	- [ ] turn msdf into mtsdf
	- [ ] add the rest of the alphabet (eu languages)
		- [ ] create language packs?
	- [ ] editor
		- [ ] Markdown insertions
		- [ ] page system
		- [ ] pageless system
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


- [ ] DOCUMENT
	- [ ] UI
		- [ ] Controls
			- [ ] Default
			- [ ] Containers
	- [ ] Registry
	- [ ] Bootstrapper
	- [ ] Keybinds
	- [ ] Context
	- [ ] XSD
		- [x] Code
		- [ ] System
	- [ ] Renderer
		- [x] The Renderer system
		- [ ] Rasterizer
		- [ ] Lazy renderer
		- [ ] document what i have now. basically make a Vulkan guide for myself
			- [x] each step of the renderer
			- [ ] each small detail as to why that over that
			- [ ] design patterns why they were made
				- [x] descriptor sets
- [ ] 


---
# SOME FUCKING DAY
Random features whenever
- [ ] Procedural:
	- [ ] land generation
	- [ ] prop placement
- [ ] Decal placement
- [ ] rendering
	- [ ] fix ray-tracer
	- [ ] fix and optimize 2D radiance cascades
		- [ ] figure out how to make it nicer
		- [ ] transfer it i to 3d (magistras)
- [ ] home audio system controller
- [ ] home LED lighting system controller
- [ ] Render Graph
- [ ] Gaussian splats for foliage [[Gaussian Splats for games]]

---
# Nusiskundimai Blenderiu
- [ ] negaliu procedurally isskaiciuot SDF ir jo displayint