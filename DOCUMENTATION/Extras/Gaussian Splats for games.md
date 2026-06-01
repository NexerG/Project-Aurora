Gaussian Splats. A system of 2D ellipses in the world that can be of different sizes and they collect optimal lighting data from certain angles and reconstruct the lighting from a few color offset vectors depending on the view angle. Those splats can be very stretched in various directions (bound to the local 2D plane) and have their own colors and transparency.
One ideal scenario for it would be the immense detail it can give to foliage. A few things it lacks is: animation, shadows under the
- MANY splats require a bit of power. 1M splats are okay, but if we do tens of thousands per tree then we suddenly start to run like shit.
- 1M splats is like 200mb of VRAM. one tree is like utmost 15mb.
- animations are not figured out. 4D splats but a lot more data.
	- a bigger diapason of color also in space not only from angle of refraction?
	- could radiance cascades contribute to that?
- light under the tree is a problem. we don't have raster data on splats and making a shadow map is kinda not it.
	- Cinema4D has solved this somehow
- how does lighting work when animated?
- they have to be ordered in a specific kind of a way (back to front) and then that for every tree.
- reflections are tricky cause they basically create a small world inside of that reflective surface. you can go inside a TV and see what is inside on the other end.
- objects only have detail where they have been photographed to create those splats. small nooks and crannies might not get that much detail if not photographed well.