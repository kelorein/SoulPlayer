# SoulRecorder manual IK prototype (superseded as primary)

## Status and scope

This document records the earlier manual `PlayerBody`/`LimbIK` experiment. It is no longer
the preferred Release presentation. The active architecture is documented in
`SOULRECORDER-NATIVE-CONTROLLER.md` and uses Tarkov's real `UsableItemController` pipeline.

The experimental runtime view reused the local player's already-installed EFT
first-person arms, gloves, materials, and skeleton. SoulPlayer continues to own and ship the
recorder and cassette models. This is an initial runtime proof, not final interaction art: it
established discovery, temporary IK ownership, wrist sockets, restrained finger overrides,
and exact cleanup before the cassette motion is polished.

No EFT mesh, texture, material, shader, animation, AssetBundle, or extracted file is copied
into SoulPlayer. Runtime references point only at objects already present in the user's own
running EFT process.

## Read-only architectural reference

Architecture was studied from the public SPT-VR repository at:

- repository: `https://github.com/cybensis/SPT-VR`
- reference commit: `00600ee5f496da0be07cf3686ffe7ec88c00eaa8`
- commit title/date: `v1.3.0 - Release day`, 2026-06-23
- local read-only checkout: `D:\Resources\SoulPlayer\References\SPT-VR`

The repository had no declared license file at the pinned commit. Consequently no source was
copied, adapted line-for-line, or made a dependency. Only the following API-level concepts
were studied: `PlayerBody.UpdatePlayerRenders` timing, `BodySkins[Hands]`, `SkeletonHands`,
the original arm renderer, collarbone/arm role discovery, wrist attachment, existing
`LimbIK` reuse, native material preservation, and post-IK hand posing. SoulPlayer's resolver,
state guard, target lifecycle, socket layout, diagnostics, and fallback logic were designed
and implemented independently.

## Runtime discovery

`EftNativeHandsDiscovery` accepts only the active local first-person `EFT.Player`. It resolves:

1. `Player.PlayerBody` and `Player.PointOfView`;
2. `PlayerBody.BodySkins[EBodyModelPart.Hands]`;
3. the first usable native `AbstractSkin.SkinnedMeshRenderer`;
4. `PlayerBody.SkeletonHands.Bones`;
5. left/right collarbone, upper arm, forearm, wrist, palm, and finger roles;
6. existing collarbone `RootMotion.FinalIK.LimbIK` components where present;
7. current hands-controller and EmptyHands state for diagnostics.

Bone roles are selected semantically and checked against parent relationships. There is no
hard-coded Transform path. On first success, one diagnostic prints the resolved hierarchy,
renderer/mesh/material count, current point of view, EmptyHands status, and whether each
LimbIK came from EFT or was temporarily created by SoulPlayer.

## IK and grip architecture

Existing EFT `LimbIK` components are preferred. Their enabled flag, target, bend goal,
position/rotation weights, and bend weight are captured once before mutation. If a collarbone
has no usable component, SoulPlayer may create one isolated `LimbIK` and initialize it against
the discovered upper-arm/forearm/wrist chain. It is destroyed during cleanup.

Targets are children of the selected full-screen gameplay camera. Elbow goals stay to the
respective lower sides, while the native shoulders remain attached to and controlled by the
player body. SoulPlayer never detaches or reparents collarbones, shoulders, or arm bones.

The attachment chain is:

```text
native left wrist
  -> SoulRecorderGripSocket
    -> SoulPlayer recorder prefab

native right wrist
  -> SoulTapeGripSocket
    -> SoulPlayer cassette prefab
```

The cassette transfers once from `SoulTapeGripSocket` to the recorder's `CassetteSlot` at
seating, then back to the hand socket at the eject grasp. Existing preserve-world transfer
and controller timing remain authoritative. The models follow the native hands; the hands do
not chase separately animated SoulPlayer objects.

## Fingers and restoration

Native finger local transforms are captured once. A restrained support curl and cassette
thumb/index/middle pinch are applied after the active LimbIK solver update and are always
computed from the captured base rotations, so repeated frames cannot accumulate rotation.

Normal exit, forced reset, component disable/destruction, player loss, death/unspawn, and raid
cleanup all use the same idempotent restoration path:

- detach/hide SoulPlayer recorder and cassette before deleting temporary wrist sockets;
- unsubscribe post-IK callbacks;
- restore finger transforms and existing LimbIK state in reverse capture order;
- continue restoring remaining state if one subscriber/object fails;
- destroy temporary IK, targets, bend goals, and sockets;
- clear every reference so the next interaction starts cleanly.

Terminal cleanup never changes SoulRecorder hands-controller ownership or tries to restore a
weapon independently; the validated controller lifecycle remains the sole weapon-restoration
authority.

## Fallback and packaging

Historical prototype fallback order was:

1. manual native EFT player-arm IK plus SoulPlayer recorder/cassette;
2. SoulPlayer packaged recorder/cassette only;
3. SoulPlayer procedural recorder/cassette;
4. headless/audio-only.

The current Release controller does not select this manual IK view. The prototype view
explicitly disables packaged BAMEN instantiation. BAMEN source,
attribution, and authoring history remain available for reproducibility, but BAMEN is not an
automatic runtime fallback.

`scripts/Package-Release.ps1` now allowlists every release file and rejects game-asset
extensions, Battlestate/EFT-named assets, undeclared files, and every AssetBundle except
`soulplayer_assets.bundle`. `tools/Test-SoulPlayerPackageAssets.ps1` verifies the same source
and manifest policy offline.

## First one-raid acceptance test

1. Equip a weapon and enter a local raid in first person.
2. Note the current gloves/sleeves, then press M.
3. Confirm the weapon is suppressed and the same gloves/sleeves remain visible, connected to
   the player's shoulders/body.
4. Confirm the support hand is positioned near and carries the SoulPlayer recorder, while the
   interaction hand is positioned near and carries the SoulPlayer cassette.
5. Let insertion finish and verify the previous weapon returns normally.
6. Change to a different weapon slot while music plays, then press M to stop/eject.
7. Confirm the current weapon returns and M remains reusable.
8. Repeat start/stop twice, then cancel one interaction if practical.
9. Move, lean, look around, and equip/fire the weapon afterward; verify no persistent arm,
   finger, IK, glove, or skeleton corruption.
10. Capture the single native-hands resolution diagnostic and any fallback/error line.

Acceptance for this prototype is structural: correct player appearance, connected arms,
reasonable hand targets, clean weapon lifecycle, and exact restoration. Final cassette pose
and animation polish begins only after this proof passes.
