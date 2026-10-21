# Toggle Switch Control

## Description
`ToggleSwitchControl` (`<ToggleSwitch LeftText="Last vault" RightText="Vault browser" Right="false">`) picks between two named options: a caption on the left, a pill-shaped track with a sliding knob, and a caption on the right. The Settings window uses one for every setting whose enum has exactly two names, where any other enum gets a dropdown.

## Parts
The track is painted in the Chrome role with the `toggle-track` gradient, which fades the accent colour from 70% opacity to full along a 30 degree angle, so it follows the palette. The knob is a fixed white circle inside the track. The chosen caption is drawn in the ink colour and the other in the muted ink colour.

The gradient lives in the engine's own `Engine.gradients.axml`.

## Switching
#### On Pointer Release (e)
if the release is a left click
	flip `Right`
	slide the knob to the new end of the track
	swap which caption is the chosen one
	report the new value through `onChanged`

The knob slides with a tween on its horizontal position, 0.18 seconds with a cubic in-out curve.

## In the Settings window
The captions come from the two enum names with the PascalCase split into words, so `LastVault` reads "Last vault".
