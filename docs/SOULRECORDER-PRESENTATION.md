# SoulRecorder procedural first-person presentation v1

## Scope and ownership

SoulRecorder now has a visible first-person prototype implemented entirely with
SoulPlayer-owned Unity primitive geometry and runtime-created materials. It uses no
Battlestate model, prefab, texture, material, weapon rig, or extracted EFT asset. The
prototype is deliberately original and generic: a charcoal portable field recorder,
muted amber stripe, cassette window, speaker grille, play/stop controls, and status LED.

This is a procedural first-person recorder prototype. An original Blender recorder,
custom hand rig, and authored hand/transport animations remain deferred.

## Architecture

`SoulRecorderUsableItemController` remains the sole owner of the validated lifecycle:

```text
Idle -> LoadingTape -> Ready -> Playing -> Ejecting -> Idle
```

Normal Release creates `ProceduralSoulRecorderHandsView`, which implements the existing
`ISoulRecorderHandsView` presentation seam. The view consumes callbacks and never starts
audio, chooses a cassette, changes recorder state, or performs gameplay physics.
`HeadlessSoulRecorderHandsView` remains the safe fallback and test implementation. The
development compass/radio proxy remains compile-gated and unchanged.

Pure `SoulRecorderPresentationState` owns clamped progress and visual-only cassette/reel
flags. All Unity object construction and transform updates stay in the procedural view.

## First-person placement and timing

The recorder is attached to an active, full-screen perspective gameplay camera, preferring
`Camera.main`, then another eligible non-render-texture full-screen camera, with
`Player.CameraPosition` as a defensive fallback. It does not modify the EFT weapon or hands
hierarchy.

Central prototype tuning is:

| Value | Prototype setting |
| --- | ---: |
| Held position | `(0.19, -0.17, 0.47)` camera-local metres |
| Held rotation | `(6, -14, 4)` degrees |
| Recorder scale | `0.92` |
| Enter | `0.25 s` |
| Exit | `0.22 s` |
| Cassette insertion | `0.62 s` |
| Playback held visibility | `1.00 s` |
| Automatic lower | `0.25 s` |
| Raise before lowered ejection | `0.23 s` |
| Cassette ejection motion | `0.55 s` |

The enter pose starts lower and farther right, then eases into the lower-right/lower-center
held pose without covering screen center. Camera changes reparent the owned visual. The
cassette moves from a visible outside pose into a deterministic seated pose. Its final
center is `(0, 0.018, -0.0305)` within the recorder: the shell sits behind an overlapping
four-sided bay frame, while only the smaller window opening exposes the label and reels.
Interrupted insertion ejects from its current travel rather than snapping to the seated
position.

During playback the two visible reel hubs rotate in opposite directions at a restrained
fixed visual rate and the status LED changes to amber. Stopping playback or beginning
ejection stops the reel state immediately. After one second of visible playback, the
recorder eases below the view over 0.25 seconds while audio and the `Playing` state
continue. Hidden reel rotation is skipped. Pressing M while lowered changes only the
visual sequence: the recorder raises over 0.23 seconds with the cassette still seated,
then the existing 0.55-second cassette ejection runs from the same bay. None of this
changes selection, persistence, progression, audio playback rules, or state-machine
semantics.

## Shared cassette visual

`SoulTapeCassetteVisual` is shared by the recorder and world pickups. Its dimensions remain
`0.11 x 0.018 x 0.07 m`, with a dark shell, aged-paper label, two hubs, and a restrained
amber accent. Primitive colliders are disabled and destroyed.

World pickup roots still use the exact authored position and rotation. The authoritative
invisible interaction target, 2.5-metre range, acquisition/retention cones, targeting,
visibility samples, and LOS rules are unchanged. The interaction focus remains above the
top face by the same `0.009 m` half-thickness plus `0.015 m` clearance.

The world cassette is intentionally recognizable at close range but has no glow, beacon,
emissive quest treatment, rigidbody, or EFT inventory behavior: hide the location, not the
object.

## Failure and cleanup

Shader lookup uses `Standard`, `Legacy Shaders/Diffuse`, `Unlit/Color`, then
`Sprites/Default`. A missing shader or construction exception logs one concise error and
routes the interaction through the headless view; playback and recorder state continue.

Normal ejection finishes before the recorder eases out. `ForceReset`, raid end, component
disable/destruction, and player/camera loss hide or destroy owned objects safely. The view
does not use `DontDestroyOnLoad`, create physics bodies, or leave primitive colliders. Owned
materials and visual roots are destroyed with the presentation.

## Focused EFT acceptance

After offline validation, one milestone run should verify:

1. M enters from hip-fire and the recorder settles below screen center.
2. The selected cassette visibly inserts, then Electric Dreams (or the selected track)
   starts through the unchanged audio backend.
3. The cassette reads as recessed behind the bay frame, with reels visible only through
   the window.
4. Reel hubs and the amber LED animate while visible, then the recorder automatically
   lowers after about one second without stopping the music.
5. M from the lowered pose raises the recorder, then visibly ejects the cassette from the
   same bay and removes the recorder.
6. Rapid M during insertion ejects cleanly without a snap or stuck object.
7. Raid exit during lowered playback leaves no recorder in menus or the next raid.
8. A world cassette retains its authored pose and existing targeting/LOS behavior while
   using the polished shared visual.
