# Switch Control

## Description
`SwitchControl` (`<Switch Options="Axml, Markdown, Plain text" Selected="0">`) is a segmented pill that holds any number of options: a row of captions on a track, with a thumb that slides behind the chosen one. The options can wrap onto several rows with `Columns`. The Settings window uses one for every setting whose enum has two to four names, where an enum with more names gets a dropdown, and the inspector uses one for the Paragraph list shape. For an on/off choice with a caption per state see [[Toggle Switch Control]].

## Parts
`Options` is a comma separated list; each entry is trimmed and empty entries are dropped. From code the options are set as a list of strings, and setting them rebuilds one label per option. `Selected` is clamped to the options; setting it from code moves the thumb at once.

The track is painted in the Chrome role without a gradient. The thumb is painted in the ground role, the chosen caption in the ink colour and the others in the muted ink colour, so the control follows the palette. Each segment is as wide as its caption plus ten pixels of padding on each side, so segments differ in width. The thumb is inset three pixels in a track 24 pixels tall, with 13 pixel captions.

## Rows
`Columns` (`Columns="3"`) is the most options on one row; the default of 0 keeps them all on one row. The number of rows is the option count divided by the columns, rounded up. Each row is its own track pill, the rows are stacked with a four pixel gap, and every row is stretched to the width of the widest row with the extra width shared evenly over that row's segments. The children are rebuilt as the tracks, then the thumb, then the captions, so the captions draw over the thumb.

## Arranging
The thumb's horizontal position is not a pixel value but the progress of a slide between 0 and 1, and that is what the tween animates. The control remembers which option the slide started from and moves the thumb in a straight line from that segment to the chosen one, so across rows it slides diagonally.

#### Arrange Core
for each track row
	for each option on the row
		place its caption in its segment
		record the segment's rect
	stretch the row to the widest row's width, sharing the extra over its segments
lerp the thumb's x, y, width and height from the starting option's segment rect to the chosen option's segment rect by the progress

## Switching
#### On Pointer Release (e)
if the release is a left click
	find the caption whose centre is nearest the pointer in x and y
	if it is the chosen one
		report nothing
	else
		remember the previous choice as the start of the slide
		set `Selected` to the new one
		reset the progress to 0
		tween the progress to 1
		report the new index through `onChanged`

The progress tweens over 0.18 seconds with a cubic in-out curve. Setting `Selected` from code makes the starting option the chosen one and sets the progress to 1, so the thumb jumps.

## In the Settings window
The options come from the enum names with the PascalCase split into words, so `PlainText` reads "Plain text", and the selected option is the one that matches the current value.

## In the inspector
The Paragraph section's List row is one switch with the options None, Bullets, Numbers, Tasks and Sorted and `Columns` 3, so it takes two rows, on its own line under a "List" caption. The chosen index is the list shape's position in its enum. A click applies the list shape to the note, and the inspector then reflects the note's real shape on its next tick, which puts the thumb back when nothing changed.
