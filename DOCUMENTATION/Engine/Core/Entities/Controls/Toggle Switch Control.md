# Toggle Switch Control

## Description
`ToggleSwitchControl` (`<ToggleSwitch OnText="Yes" OffText="Script" On="false">`) is an on/off pill: a knob slides along a track, and only the caption of the current state shows inside the track, on the side away from the knob. When it is on, the `OnText` caption sits at the left and the knob at the right; when it is off, the knob is at the left and the `OffText` caption at the right. Nothing in the engine uses it yet, and the Settings window picks a [[Switch Control]] for its enums instead.

## Parts
The track is painted in the Chrome role. Inside it sits a private panel painted in the accent role, whose alpha is tweened from 0 to 1 together with the knob, so the track fades from grey to accent as the knob slides. The knob is a fixed white circle. The `OnText` caption is white and the `OffText` caption is drawn in the muted ink colour, which keeps each readable on its own track colour. The caption that does not belong to the current state is hidden.

The track is as wide as the knob, its inset, the padding around a caption and the wider of the two captions, so the control does not change size when it is toggled.

The accent panel is a child of the track and not of the switch, because a button resets the alpha of its direct children whenever it is enabled or disabled, which would show the panel while the switch is off.

## Measuring
#### Measure Core
measure both captions and keep the wider one
measure the track at the track width and the track height
report the track width and height, plus padding

The track is measured at the size it will be arranged to, so its single child fills it on the first pass.

## Switching
#### On Pointer Release (e)
if the release is a left click
	flip `On`
	swap the visible caption at once
	slide the knob to the new end of the track with a tween on its horizontal position
	fade the accent panel to 1 when on, to 0 when off, with the same tween
	report the new value through `onChanged`

The tween takes 0.18 seconds with a cubic in-out curve. Setting `On` from code skips the tween and puts the knob and the accent panel straight at their end values.
