# Color Picker Control

## Description
`ColorPickerControl` (`<ColorPicker Hex="#RRGGBB">`) picks a colour from a saturation/brightness field and a hue strip, or takes one typed as hex. The format bar puts one at the bottom of its text colour and highlight dropdowns, hosted in the menu through `ContextMenuContent`, which lets any control sit among a menu's rows.

## Parts
The field is three quads on the same rectangle: a flat quad in the current hue at full saturation and brightness, then the `picker-white` gradient fading white out to the right, then `picker-black` fading black in toward the bottom. The hue strip is one quad with the seven-stop `picker-hue` gradient. Only the flat quad's colour changes while dragging, so nothing is rebuilt but one paint word.

The handles are small filled quads in the colour they mark, with a white edge. Below the field sit a swatch of the current colour and a hex text box.

The three gradients live in the engine's own `Engine.gradients.xml`, which loads before a host's `Gradients.gradients.xml`.

## State
The colour is held as hue, saturation and brightness rather than as RGB, so dragging brightness to black and back does not lose the hue.

#### Hex (value)
if `value` does not parse as #RRGGBB (an #RRGGBBAA alpha is dropped), ignore it
convert it to hue, saturation and brightness
keep the old hue when the colour is a grey
repaint the parts

## Picking
#### On Pointer Press (e)
if the press is in the field or the strip
	remember which, move the colour to the press point, and start a drag

#### On Drag (e)
if dragging the field
	saturation = how far right the pointer is, clamped to the field
	brightness = how far up the pointer is, clamped to the field
if dragging the strip
	hue = how far down the pointer is, clamped to the strip
repaint the parts

#### On Drag Stop
report the colour through `onPicked`

A committed hex box also reports through `onPicked`; an invalid one snaps back to the current colour. In the format bar each report is one styling step on the note — the selection's colour, or the style armed for what is typed next — and the menu stays open for another pick.
