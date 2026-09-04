# SoulRecorder screen-space presentation

## Active runtime presentation

SoulRecorder now renders as a deterministic, fully two-dimensional IMGUI overlay in the
bottom-right of the gameplay screen. It does not call `SetEmptyHands`, `SetInHands`, create
an EFT hands controller, modify `PlayerBody`, install `LimbIK`, or instantiate recorder,
cassette, hand, or arm objects in the raid world. The current weapon stays untouched.

The recorder and cassette PNGs are deterministic editor renders of SoulPlayer-owned models
and are embedded directly in `Soulplayer.dll`. The archived AssetBundle, native-controller,
arm-rig, and authoring work remains in the repository for reference but is not prewarmed,
patched, loaded, copied beside the DLL, or selected by normal runtime behavior.

## Architecture

`SoulRecorderInteractionController` continues to own tape/audio state. Its presentation seam
now binds `SoulRecorderOverlayView` and `SoulRecorderScreenOverlayTransition`:

```text
Idle -> LoadingTape -> Ready -> Playing (presentation hidden) -> Ejecting -> Idle
```

The overlay shares one pure `SoulRecorderOverlayTimeline` with the offline Unity preview.
Insertion is 1.24 seconds, followed by a 0.26-second fade. After M, the overlay remains
hidden while audio preparation completes. Clip loading, source assignment, and a muted
one-frame pre-roll finish first; SoulPlayer then waits two additional Unity frames before
starting the unchanged insertion timeline. The seated event only unmutes and unpauses the
primed source. A 15-second preparation timeout releases the interaction without ever showing
the insertion overlay, so preparation cannot freeze a visible animation or permanently lock M.
Ejection is 1.00 second total and retains the existing audio-stop event timing.
`HeadlessSoulRecorderHandsView` remains the safe fallback: a missing or corrupt embedded PNG
is logged once, disables only the visual, and cannot block M or music playback.

Default layout is BottomRight at `0.78x` the approved original presentation, approximately
15.6% of screen width, with a 40 px 1080p margin and 48 px 1440p margin. Existing installs
using the former exact `1.0` default migrate once; customized scales remain unchanged.
Scale, inward horizontal/vertical offsets, and animation speed are configurable.
The cassette rotates to zero before its final approach, then inserts without a late snap;
ejection reverses the path. The center/crosshair region is never used.

The Unity editor batch preview generates the same 1920x1080 insertion/ejection frame
sequences, a 1920x1080 key-state sheet, and 1920x1080 plus 3440x1440 composition images under
`Artifacts/SoulRecorderOverlayPreview`. MP4 encoding is a post-render developer step; EFT is
not required for animation review.

## Archived 3D presentation research

Everything below this heading documents superseded experiments. It is deliberately retained
but is not the active Release architecture.

Runtime hierarchy is deliberately split between import correction and gameplay tuning:

```text
active full-screen gameplay camera
  SoulRecorder First-Person Presentation     # position/rotation/scale tuning
    soulrecorder_fp                           # identity packaged/procedural visual root
      SoulRecorderModel                       # Unity FBX axis/unit correction only
      CassetteInsertionStart
      CassetteSlot
      CassetteWindow
      StatusLed
      ReelWindowLeft
      ReelWindowRight
      soultape_cassette                       # distinct animated object
        SoulTapeCassette
          Shell / Label / ReelLeft / ReelRight
```

## Superseded manual-presentation placement and timing

Before that camera-local presentation becomes visible, SoulPlayer calls EFT's public
`Player.SetEmptyHands(...)`; after the presentation exits it restores the saved
`HandsController.Item` through `Player.SetInHands(...)`. No firearm renderer is toggled by
SoulPlayer. The presentation container is attached to an active, full-screen perspective gameplay camera, preferring
`Camera.main`, then another eligible non-render-texture full-screen camera, with
`Player.CameraPosition` as a defensive fallback. The recorder prefab remains identity beneath that container, so camera-relative
tuning cannot regress the model's validated import scale or orientation.

Central prototype tuning is:

| Value | Prototype setting |
| --- | ---: |
| Held position | `(0.060, -0.195, 0.620)` camera-local metres |
| Held rotation | `(4, -5, 2)` degrees |
| Recorder scale | `0.78` |
| Enter | `0.38 s` ease-out with restrained settle |
| Exit | `0.34 s` accelerating ease-in |
| Cassette insertion | `1.12 s` (`0.28` lead-in + `0.72` transport + `0.12` contact settle) |
| Cassette ejection | `1.02 s` (`0.34` recorder lead-in + `0.68` transport) |

The runtime-accepted first bundle was too close and too far right. The revised enter pose
starts lower and farther right, then eases into the lower-right/lower-center
held pose without covering screen center. Camera changes reparent the owned visual. The
cassette follows authored `CassetteInsertionStart -> CassetteAlignment -> CassetteSlot`
markers rather than one arbitrary linear offset. The exterior pose is
`(0.105, -0.080, -0.125)`, alignment is `(0.012, 0.012, -0.062)`, and the final center is
`(0, 0.018, -0.008)` within the recorder. With the cassette's rotated 9.1-mm half-thickness,
its player-facing surface is approximately 3.5 mm behind the packaged recorder front,
instead of sitting on top of it. Ejection uses the inverse two-segment path toward the
dedicated `(0.095, -0.065, -0.115)` `CassetteEject` marker.
Interrupted insertion uses a short `0.05 s` response delay, reverses from its current travel,
then uses the same one-shot presentation-exit and weapon-restoration path.

When audio first confirms playback, the reels/LED activate briefly while the recorder exits.
The previous firearm/item then returns while audio continues. Pressing M during `Playing`
puts that item away again, re-enters with the cassette already seated, stops playback, and
runs the staged 1.02-second return/ejection from the same bay. None of this changes selection,
persistence, progression, or audio resolution rules.

## Shared cassette visual

The recorder uses the packaged custom SoulTape cassette as an independent animated object.
Unity imports it as `0.0011 x 0.0007 x 0.000182 m`; the prefab applies a `-90` degree local-X
axis correction and approximately `100x` uniform scale. Its self-tested runtime contract is
`0.11 x 0.0182 x 0.07 m` (X width, Y thickness, Z face height). Its seven renderers remain
active on Default layer 0, and its `Shell`, `Label`, `ReelLeft`, and `ReelRight` transforms
are retained.

The cassette starts at `CassetteInsertionStart`, moves independently to `CassetteSlot`,
stays seated until the start presentation exits, and is restored directly to that seated
pose when the stop/eject presentation returns during `Playing`.
Interrupted insertion ejects from the current interpolated travel rather than snapping.
The entire recorder is never toggled as a substitute for cassette motion.

`SoulTapeCassetteVisual` remains the procedural fallback and the current world-pickup visual.
Primitive colliders are disabled and destroyed.

## Native EFT recorder investigation

The installed SPT/EFT 4.1.2 data was inspected without loading EFT or modifying game files.
The item database and English locale expose no Audio Recorder item/template ID. The Windows
asset manifest has no recorder prefab or bundle entry. The only recorder/cassette-named
entries are ordinary VHS/quest cassette assets, unrelated to the tutorial presentation.
The sandbox tutorial scene files contain no recorder, dictaphone, audio-tape, hands,
animator, or socket identifier that can serve as a stable runtime lookup.

Managed-assembly inspection also found no recorder type or string literal. The complete set
of concrete `Player.ItemHandsController` descendants contains the established firearm,
meds, knife, grenade, quick-use, empty-hands, range-finder, and radio-controller families.
Only `PortableRangeFinderController` and `RadioTransmitterController` derive from
`UsableItemController`; there is no Audio Recorder subclass or native recorder view type.

There is still no native EFT recorder prefab/controller/animator contract. SoulPlayer does
not use one. The native-hands prototype instead reuses only the active player's installed arm
renderer, SkeletonHands, and LimbIK while retaining SoulPlayer's recorder/cassette assets and
validated usable-item lifecycle. See `SOULRECORDER-NATIVE-HANDS.md`.

## Disabled custom-hands authoring work

The provided DevMops low-poly arms source is CC0. Its source scene contains one mirrored
mesh, a large Rigify rig/metarig and widget set, a 128x128 skin texture, and no authored
actions. Blender now drives the permitted Rigify finger controls into a restrained
recorder-holding curl, evaluates/bakes the mirrored deformation, and removes the metarig,
control/mechanism bones, widgets, drivers, and scene helpers. The derived runtime asset is
one 530-vertex bundle-authored `SkinnedMeshRenderer` with a stable `SoulRecorderHandsRoot` and two motion
bones, `HoldingHand` and `CassetteHand`.

The authoring design keeps `HoldingHand` fixed around the recorder while `CassetteHand` receives the same
recorder-local transport delta as the cassette (94% positional follow plus a restrained
rotation follow), so it brings the cassette from
the exterior pose through alignment, settles at the bay for Ready/Playing, and follows it
out during ejection. The motion is deterministic code driven; no empty or source Rigify
animation clips are shipped. This is a legally independent first rigged presentation pass,
not an EFT hands replacement. This authoring asset is retained for reproducibility only.

Runtime test #2 identified `SoulRecorderArms` as the tall brown geometry. The original
two-sibling export let Unity choose the moving `CassetteHand` as
`SkinnedMeshRenderer.rootBone`, which made the renderer unsafe. The rebuilt derivative adds
`SoulRecorderHandsRoot` above both motion bones. Blender validates the hierarchy, Unity's
same-invocation bundle self-test requires the exact root/three-bone/three-bind-pose contract,
and pipeline tests retain the same root/bounds provenance checks. Runtime no longer keeps a
hands-prefab reference, exposes no `InstantiateHands` method, and never activates this
geometry. The clean recorder-only procedural fallback remains available if the packaged
recorder or cassette fails.

## Runtime material contract

The first runtime bundle's white/light-grey body was not the intended finish. The FBX had
retained texture paths into Blender's deleted temporary extraction folder. Preparation now
rewires those paths to the processed assets, and Unity assigns explicit SoulPlayer-owned
Standard body/flap materials instead of trusting FBX-internal materials. The body binds
base color, normal, and a metallic map whose alpha is derived from inverted roughness. The
flap uses a derived base-color texture with the authored opacity mask in alpha, making the
cassette window readable. Unity dependency validation and same-invocation bundle loading
reject missing or unresolved textures. The intended source finish is predominantly dark
charcoal/black with light functional panels—not a plain white block.

## Placement-tools tuning

`SoulPlayerPlacementTools=true` adds a compile-gated config bridge for presentation-root,
recorder, hands, and cassette start/alignment/inserted/eject position/rotation values.
After editing the BepInEx config, `Ctrl+Shift+F7` reloads it and logs one camera/local
snapshot. Normal Release contains no tuning hotkey or config surface.

The Unity asset project also contains a development-only contact-rig authoring window.
Unlike the legacy camera-offset tuner, it exposes the actual arm/wrist, each required finger
joint, and recorder/cassette transforms. The Animator is disabled while authoring, edits are
applied live, and a fixed FOV-70 player-camera mirror shows the exact first-person result.
The prior contact markers are optional reference gizmos only. The tool provides deterministic
reset, fine translation/rotation steps, and separate JSON save/load for `SupportHold` and
`CassetteCarry`. Markers will be regenerated from the human-approved poses. Insertion and
ejection choreography must not be re-authored until those static grips are accepted.

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
The activated path is logged once: native EFT arms plus packaged recorder/cassette,
packaged recorder/cassette only, procedural fallback, or safe headless fallback.

Normal ejection finishes before the recorder eases out. Local-player death/unspawn and the
existing `PostRaidCoordinator.RaidResultQueued` signal both invoke the same immediate reset
used by raid loss and plugin/controller disposal. It stops audio, cancels transitions,
hides the view, clears interaction state, and restores hands only when the player is still
available. Repeated cleanup is safe. `ForceReset`, component disable/destruction, and
player/camera loss hide or destroy owned objects safely. The view
does not use `DontDestroyOnLoad`, create physics bodies, or leave primitive colliders. Owned
materials and visual roots are destroyed with the presentation.

## Focused EFT acceptance

After offline validation, one milestone run should verify:

1. M enters from hip-fire and the complete recorder settles lower-center/lower-right without
   dominating the view.
2. The selected cassette visibly inserts, then Electric Dreams (or the selected track)
   starts through the unchanged audio backend.
3. The dark textured body/flap render correctly and do not regress to plain white.
4. The cassette aligns before insertion and reads as recessed behind the transparent bay
   flap, rather than attached to the front surface.
5. EFT firearm/hands are absent for the interaction and return after insertion while music
   continues; no SoulPlayer CC0 arm geometry appears.
6. Reel hubs and the amber LED animate during the short visible interaction.
7. M visibly ejects the cassette from the same bay, then removes the recorder.
8. Rapid M during insertion cancels cleanly and restores the firearm without a stuck object.
9. Death, extract, and raid result during Loading/Playing/Ejecting leave no audio or recorder
   in menus, and M does nothing outside the live raid.
10. A world cassette retains its authored pose and existing targeting/LOS behavior while
   using the polished shared visual.
