---
date:
Status: Draft
tags:
  - Engine
Linker:
  - "[[Arctis Aurora]]"
System:
  - "[[VULKAN]]"
Dependencies:
  - "[[UI Rasterizer Module]]"
Implementors:
  - "[[Renderer Module]]"
  - "[[UI Rasterizer Module]]"
---
## System
The renderer is responsible for both drawing images from engine objects and displaying those images on screen. It can also be responsible for computing shaders on the GPU. *Like how i implemented 2D Radiance cascades.* 
The general way the system works is the CPU creates a window, hooks it up to the GPU API and then does the rendering math to get the picture on the screen in a hierarchy like this:
Renderer
	Queue allocator
	Rendering modules
	window

### Renderer
The renderer is responsible for the whole system. It's the engine of rendering (as per the name suggests - rendering engine). It gets the GPU, creates a way to talk to the GPU. Creates the window, it attaches it to the GPU interface. Reads the modules and assigns them queues (if possible) to optimize rendering resources. Do note that Vulkan Rendering is a very setting heavy API. It needs to know all the little details about the rendering system you're creating to squeeze out t he most performance possible disregarding per GPU kernels.

The Renderer's sequence goes as follows:
- Prerequisites
	- Modules
		- `TEMP` MAX_STORAGE BUFFERS/TEXTURES/UNIFORMS
		- Physical device features
		- Vulkan features
		- Descriptor types
		- Descriptor binding flags
		- Variable descriptor set count
		- Shader stages
- Initializing
	- Vulkan instance (API)
	- Window surface
	- Physical device
	- Queue Allocator
	- Logical Device (API - GPU interface)
	- Swapchain
	- Command Pools (per Queue)
- Descriptors (should be immutable (recreate per object))
	- Descriptor set layouts
	- Descriptor pool
	- Descriptor sets
- Pipelines
	- For each module
		- Render pass
		- Frame buffers
			- Image
			- Image View
			- Device Memory
		- Pipeline
			- pipeline layout
				- Shaders
- Synchronisation
	- Fences
	- Semaphores

Modules are discussed in the [[#Modules]] section.

`Vulkan Instance`
The Vulkan Instance is the driver being called and setup to talk with our program. For it to work you have to give: `AppName`, `EngineName` that pass through an appInfo struct. Then we setup the extensions we will want to use on the CPU side like debug extensions (`validation layers`) and create the messenger on the CPU side to tell us what the Vulkan Driver errors.

`Window Surface`
We just create the window surface. Aka platform dependent driver that lets us create a window to render images to the screen. Sets the window hints like `is resizeable`, `is decorated (top bar)`, `double buffer (swapchain)`, `RGB bits (per channel)`, `refresh rate`. bunch of other shit.
The window also separately has exposed input events like `cursor image`, `window resize`, `cursor position (cursor move)`, `key (on press)`, `scrolling`, `char (last pressed char)`, `mouse button (mouse press)`, `Mouse on window`.

`Physical Device`
Just logic to ping the Vulkan API instance to tell us how many Vulkan compatible devices are in the machine approachable to the API.

`Queue Allocator`
My own setup logic to allocate queues to rendering modules and other bits of the rendering. It has handles queues for stuff like graphics, compute, transfer (buffer and data transfer between CPU and GPU (discrete))

`Logical Device`
The logical device is the API interface between the GPU and the CPU. It is responsible for API calls that are associated with rendering sequence (descriptors, command/image/frame buffers). If it's some sort of data that will be used in rendering or associated with rendering objects it passes through the logical device.
To create it you have to give it the feature set the rendering modules will be using (raytracing, swapchains, validation layers etc). This is an important part to be able to tell whether the `Physical Device` will be able to run the rendering program. Here we create the queues for modules, and then after setting Vulkan Features and queues we create the `Logical Device`. An important note is that usually GPU resources are predetermined. So if the resources are for 10 objects to be rendered to add another object you'd have to re-allocate more resources (or pre-allocate).

`Swapchain`
The swapchain simply put is a few images that the Renderer targets it's final image to. It is done this way so the display image doesn't corrupt and display half of the rendered image. One image is for display - another is to render to. Can be more than 2 images. Think of it as a ring. One is being prepared another is being draw into.

`Command Pool`
Command pool is just an interface for the CPU to allocate commands to the GPU. A middle man between command buffers and the `Physical Device`. Now note that these pools are per queue since they are not thread safe. One Command Pool - One type of Queue.

### Modules
Modules in my Vulkan renderer are sub renderers meant to do some sort of graphics work. Tbh it can be purely compute as well. Their job is to contain a mini-renderer's required things like it's own queue, shaders, pipelines - all that is meant for one renderer. Whilst the [[#Renderer]] is a broader CPU-GPU communicator. This can do raytracing, raster (game and UI) work, compute (probably even AI training, just did soft research. Vulkan is a general purpose API, using CUDA would mean using per GPU tuned kernels...). Do no that the parts of modules are not exclusive to modules. The Renderer itself can have them as I use them to combine all the modules results in a final pass before displaying to the window.

`Render Pipeline`
a pipeline is a per renderer system that works as a static conveyor. Given shaders (compiled into shader byte code) the system creates shader modules. With those the system needs a pipeline shader stage. Essentially telling the pipeline what stage each shader will be used at. Those a usually predetermined like vertex going before fragment. After shaders the pipeline is setup with pipeline settings like vertex input state, input assembly, viewport, scissor, sampling, raster/raytracing/compute states. And after those settings passed to the `Logical Device` to be created.

`Framebuffer`
Each module has to have a render target it renders to. That's why in the list `Image`, `Image View` and `Device Memory` are marked under frame buffer. They're meant for storage but for the module to be able to render to an image it has to be tied to a framebuffer.

`Render Pass`
A render pass is just a resource describer of what images are used in a render. Color, depth, etc. It also describes what format they're in (single channel, 3 channel (rgb), 4 channel (rgba), etc).

#### UI Rasterizer Module

#### World Rasterizer Module
`TODO`

#### Raytracer Module
`TODO`

#### Radiance Cascade Module
`TODO`

### Data Buffering


### Descriptors
Descriptors are how shaders access resources. A descriptor is essentially a pointer that tells the GPU where to find a buffer, texture, or sampler. The CPU side prepares these pointers, groups them into sets, and binds them before draw calls so the shader knows what data to read.

The problem descriptors solve: shaders can't just reach into arbitrary memory. They need structured access points declared in the pipeline layout. Descriptors are those access points.
#### current method
The current method uses the standard Vulkan approach to descriptors with pools, layouts and sets. This is kind of a black box as we can only ask the driver to create the pool and layout and sets. If we want to change something we have to kill the older ones and recreate new ones with new data.
Current method uses `VK_EXT_descriptor_indexing` to be able to have a dynamically scaled data in the shader.
`VK_EXT_descriptor_indexing` is a Vulkan feature that lets the user define an upper limit in one of 
the slots of descriptor pools but have any amount (less than the upper limit) of descriptors in that slot. But if you add another object mid game then you have to recreate those descriptor sets all together and rebind them. This causes a problem - if one descriptor pool is for all of the descriptor sets (which it should be) then it has to be recreated alongside the descriptor sets if anything changes. Now a solution is to leave those pools and sets until they get used up (deferred deletion) but its just clunky and not a pretty solution.

`Descriptor Pool`
A pool is a pre-allocated chunk of descriptor memory from which sets are allocated. When creating a pool you declare how many of each descriptor type it will hold and how many total sets can come from it. The pool is an opaque object — you can't inspect or directly manipulate the memory inside it.

`Descriptor Set Layout`
A layout is a blueprint describing the shape of a descriptor set — how many bindings, what type each binding is (uniform buffer, storage buffer, combined image sampler), and which shader stages can see each binding. Layouts are created once during initialization and reused. They map directly to `layout(set = N, binding = M)` declarations in GLSL.

```
Aurora's UIModule uses two set layouts:
- Set 0: camera UBO (binding 0), transform SSBO (binding 1), per-control style SSBOs (binding 2, variable count)
  
- Set 1: mask samplers (binding 0, variable count)
```

Variable descriptor count (`DescriptorBindingFlags.VariableDescriptorCountBit`) is used on the last binding of each set so the array size can vary per allocation based on how many controls exist at that moment.

`Descriptor Sets`
A set is an instance of a layout — actual pointers to actual buffers/textures. You allocate a set from a pool (specifying which layout), then write to it via `vkUpdateDescriptorSets` which fills in the concrete buffer handles and image views. Once written and bound in a command buffer, the set must not be modified or destroyed until the GPU is done with that command buffer (fence signaled).

##### Problems:
- Uses `vkUpdateDescriptorSets`. That API call has measurable CPU cost, especially at scale (Unreal measured it dominating their RHI thread). `memcpy` is faster and you control the memory layout for cache coherence.
- `FreeDescriptorSetBit` — allows individual set deallocation via `vkFreeDescriptorSets`. Sounds useful but prevents the driver from using a fast linear allocator internally. Aurora currently sets this flag but doesn't actually free individual sets — it destroys the whole pool instead. For per-frame pools that get bulk-reset, this flag should be removed for better performance.
- Updating descriptor sets is expensive on the memory. The system really needs a resource manager. Updating second descriptor sets of the UI system costs 4 gigs per like 1 second. the second descriptor sets are responsible for setting the address of the sampler in the shaders.
![[Vulkan descriptor updates.png]]
#### Bindless descriptors V2
In this version we still use the `VK_EXT_descriptor_indexing` extension. But this time instead of remaking the pool and sets at new object creation - we create one pool with an upper limit

#### Descriptor Buffering (VK_EXT_descriptor_buffer)
`VK_EXT_descriptor_buffer` is an extension (not core, requires Vulkan 1.2+ with `bufferDeviceAddress`) that replaces the entire pool/set model with direct memory access. Descriptors become known-size blobs of bytes that you `memcpy` into a `VkBuffer` you own and manage yourself. The pool, set allocation, and `vkUpdateDescriptorSets` API calls all **DISAPPEAR**.

##### How it works:
- **Query sizes.** Ask the driver how many bytes each descriptor type occupies on this specific GPU, and what offset each binding has within a set layout. These sizes are vendor-specific (NVIDIA and AMD will report different sizes for the same descriptor type).
- **Create a VkBuffer.** Create a normal buffer with `VK_BUFFER_USAGE_RESOURCE_DESCRIPTOR_BUFFER_BIT_EXT`, map it to CPU-visible memory. This is your descriptor storage — you own it entirely.
- **Write descriptors.** Call `vkGetDescriptorEXT` which writes descriptor bytes to a pointer you provide — a location inside your mapped buffer at a computed offset. This replaces `vkUpdateDescriptorSets`. It's a `memcpy` under the hood.
- **Bind at draw time.** Instead of `vkCmdBindDescriptorSets`, call `vkCmdSetDescriptorBufferOffsetsEXT` with an offset into your buffer. Changing which descriptors are active is just changing an integer offset.
##### Pros:
- **CPU performance.** `vkUpdateDescriptorSets` has measurable overhead per call. Unreal Engine measured it dominating their RHI thread on Fortnite with ~600 draw calls. `memcpy` is faster, and you control the memory layout for cache coherence — is possible to pack descriptors for objects that draw together contiguously in memory.
- **No pool sizing guesswork.** Traditional pools require to declare upfront how many of each descriptor type you'll need. Get it wrong and the allocation fails. With descriptor buffers, it's just a buffer — grow it, suballocate it, ring-buffer it, same as any other GPU memory.
- **GPU-writable descriptors.** Since descriptors live in a buffer with a device address, compute shaders can write descriptors directly. This is powerful for GPU-driven rendering — a cull compute shader could write the descriptors for only the visible objects, eliminating CPU involvement entirely. Directly relevant for planned Hi-Z culling pipeline.
- **Simpler threading.** `VkDescriptorPool` is not thread-safe, requiring a pool per thread or locking. A descriptor buffer is just memory — multiple threads can write to different regions concurrently with no locking, same as any other buffer.
- **Natural fit for bindless.** Descriptor indexing (`descriptorBindingVariableDescriptorCount`, which Aurora already enables) combined with descriptor buffers means you can have one giant descriptor array that shaders index into dynamically. Material changes become an index change in a push constant rather than a descriptor set rebind.

##### Cons:
- **Availability.** Not universally supported. Desktop NVIDIA (driver ~525+), AMD (Mesa 23.x+ / Adrenalin 23.x+), and Intel Arc support it. Older drivers, mobile GPUs, and some integrated GPUs do not. If Aurora ever targets those platforms, a fallback pool-based path would still be needed.
- **Debugging.** RenderDoc and validation layers have less visibility into what you're doing. With traditional sets, the driver knows exactly which descriptors are bound and can validate every access. With descriptor buffers, you're pointing at raw memory — stale or uninitialized descriptors silently produce garbage rather than validation errors.
- **Complexity shift.** You trade pool management for manual memory management. You must compute byte offsets yourself, respect `descriptorBufferOffsetAlignment` (device-specific), handle the ring buffer, and ensure no region is overwritten while the GPU is still reading it. The driver does none of this for you anymore.
- **Vendor size variance.** Descriptor sizes differ between GPU vendors. Code that works on NVIDIA may write incorrect offsets on AMD if sizes are hardcoded rather than queried. Everything must go through `vkGetDescriptorSetLayoutSizeEXT` and `vkGetDescriptorSetLayoutBindingOffsetEXT`.
- **No incremental adoption.** Can't mix pool-based sets and buffer-based sets on the same pipeline bind point. It's all or nothing per pipeline. Migrating an existing renderer means converting every descriptor path at once.