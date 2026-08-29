# SoulRecorder hands/controller boundary

## Current implementation

Release uses EFT's supported `SetEmptyHands` transition while SoulPlayer displays its packaged
animated recorder/hands prefab. This is not the old `PlayerBody`/`LimbIK` prototype: no EFT arm
bones are modified. The packaged Animator owns the recorder grip, cassette grip, insertion,
ejection, and exit clips; the existing interaction lifecycle owns audio and restoration.

The attempted transient Vortex donor route is paused. Runtime and installed-database inspection
proved that the rangefinder template has an empty `UsePrefab`. The ordinary prefab returned by
the convenience factory and the attempted usable-prefab path both lack the `WeaponPrefab`
hierarchy required by `ItemHandsController.Setup`, so a detached manufactured donor cannot be
equipped safely. A future native-controller pass requires a real SPT inventory/template item with
a valid custom hands prefab; it must not reuse this failed transient donor approach.

The cassette follows the packaged `CassetteGrip`, transfers once to `CassetteSlot`, reverses on
ejection, activates the status LED, and spins both reels during playback. If packaged construction
fails, the recorder-only procedural visual remains the safe fallback. Audio begins only after the
empty-hands transition and presentation interaction have started.

Weapon restoration is callback-first. EFT can leave the pre-holster firearm controller and item
referenced while its lowering operation is still running, so that stale reference is never valid
proof that restoration completed. Callback-independent observation is accepted only after the
post-holster `SetInHands` request has been dispatched and a different controller owns the saved
item. The forced holster timeout is disabled once that request begins, so it cannot destroy a
weapon that is legitimately raising back into view.

## Runtime sequence

```text
M
  -> save current Item/HandsController identity
  -> Player.DropCurrentController
  -> Player.SetEmptyHands
  -> instantiate SoulPlayer packaged animated-hands presentation
  -> play SoulRecorder_Enter then SoulRecorder_Insert
  -> cassette follows CassetteGrip and transfers to CassetteSlot
  -> logical tape/audio milestone completes and the cassette reels animate
  -> play SoulRecorder_Exit and hide packaged presentation
  -> Player.DropCurrentController (empty hands)
  -> Player.SetInHands(saved item)
```

The existing tokenized ownership state remains responsible for exactly-once restoration,
timeouts, late callbacks, repeated cycles, and releasing M after failures. Failed acquisition
now promotes the saved state into the native restore path before reporting failure. Audio,
collection, tape selection, raid cleanup, and the M state machine are otherwise unchanged.

The older `EftNativeHandsRuntime` manual `PlayerBody`/`LimbIK` prototype and the failed transient
native-controller bridge remain in the authoring worktree for reference, but Release selects
neither path.

## MIT architecture reference

The native controller pattern was studied and adapted from the MIT-licensed project:

- project: `https://github.com/danauraborealis/ManimalIcebreaker`
- pinned reference: `b5799b192d18ea2f80ef2bccdc1e46fad08dce94`
- studied files: `icebreaker-client/Blowtorch/BlowtorchController.cs` and
  `icebreaker-client/Blowtorch/BlowtorchPatches.cs`
- copyright: `(c) 2026 danauraborealis`

Only the controller/equip/operation architecture was studied. The reference explains why a real
inventory-owned item and valid hands prefab are required for a future native controller. No
blowtorch models, textures,
sounds, prefabs, bundles, or gameplay content are included in SoulPlayer.

## Runtime acceptance

The next EFT test should verify only this boundary:

1. Equip a weapon and press M.
2. Confirm Tarkov holsters the weapon and the SoulPlayer packaged hands/recorder appear.
3. Confirm the authored Enter and Insert clips run rather than showing empty hands.
4. Confirm the cassette travels into the recorder and remains visible in the slot while the
   recorder is held.
5. Confirm the controller holsters and the exact previous weapon returns.
6. While music plays, change weapon slots and press M; confirm the current weapon is saved
   for that stop interaction and restored afterward.
7. Confirm the cassette reverses from the slot toward the authored eject point.
8. Repeat start/stop twice and confirm M never remains locked.
9. Capture the `SoulRecorder HANDS`, `SoulRecorder STATE`, and `PLAY REQUEST` one-shot logs.
10. If recorder visual setup is deliberately made unavailable, confirm audio and exact weapon
    restoration still complete and M does not remain locked.
