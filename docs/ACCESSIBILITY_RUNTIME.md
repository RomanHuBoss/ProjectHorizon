# TASK-204 Accessibility Runtime Closure & Gamepad Tuning

## Scope

TASK-204 closes Technical Specification sections 31.2 and 31.4 without changing gameplay authority.
The existing mouse-axis inversion, UI scale, FOV, subtitle toggle, camera-shake toggle,
motion-blur toggle and separate Music/SFX/Voice volume settings are preserved.

## Gamepad input

`GamepadDeadZone` is persistent in `settings.cfg` and is applied to all analog movement/ship
actions through `InputMap.ActionSetDeadzone`. `GamepadResponseExponent` is then applied to the
post-action vector/axis. Keyboard values remain full-scale 0/1 and are therefore unchanged by
the curve.

Supported ranges:

- dead zone: 0.05..0.45, default 0.20;
- response exponent: 0.75..2.00, default 1.25;
- subtitle scale: 0.80..1.50, default 1.00.

## Closed captions

A HUD caption panel is created at runtime. It follows the existing `SubtitlesEnabled` setting
and scales from the new subtitle-size setting. Current captioned audio events are:

- radio/voice cue;
- player damage alert;
- low-oxygen life-support alarm.

The NPC/station dialogue panels remain the authoritative source of dialogue text; the caption
layer represents audible cues and warnings.

## Color-independent status cues

The HUD now exposes text/token duplicates for the core survival indicators:

- `[HP][OK|LOW|CRIT]`;
- `[SH][OK|LOW|CRIT]`;
- `[O2][OK|LOW|CRIT]`;
- `[HZ][OK|LOW|CRIT]`.

These cues do not rely on color and are hidden only when the user explicitly hides the HUD.

## Reduced-motion boundary

Camera shake and motion-blur preferences remain persistent and default to disabled. The current
vertical slice contains no mandatory gameplay mechanic that requires either effect, therefore
turning them off never changes physics, aiming, collision, AI, quests, persistence or simulation
frequency.

## F5 acceptance

F5 checks setting ranges and persistence hooks, existing on-foot/ship inversion, response-curve
math, live controller wiring, subtitle/status overlay presence, non-color severity tokens and
separate audio-volume controls. Runtime visual/gamepad feel remains an owner smoke test.
