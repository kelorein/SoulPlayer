# SoulRecorder prototype

## Current milestone

SoulRecorder now has a recorder-specific usable-item/controller layer for SPT 4.1.x.
The earlier audio-only and compass/radio hands experiments proved the required runtime
compatibility; they are no longer the production interaction path.

The existing `SoulRecorderAudioPlayer` backend remains responsible for loading and
playing the in-raid cassette. SoulPlayer's normal menu player still suspends for a raid,
and the recorder continues to use its separate non-looping `AudioSource`.

No live EFT recorder model, cassette, animation, prefab, decrypted metadata, or other
game asset is included or redistributed.

## Controls and behavior

- Press **M** during a live raid to enter the SoulRecorder interaction.
- Entering starts the cassette insertion lifecycle and then automatically plays the
  starter cassette once it is ready.
- Press **M** while the recorder is loading, ready, or playing to exit the interaction.
  Exit stops playback, runs the cassette ejection lifecycle, and returns to idle.
- When playback reaches the end naturally, the recorder remains ready. This allows the
  future physical recorder's play control to replay the inserted cassette through the
  standard usable-item `SetAim`/primary-action seam, or **M** can eject and close it.
- Leaving the raid force-resets the controller and stops recorder audio immediately.
- The preferred starter cassette remains **Scott Buckley - The Long Dark**. If it is
  unavailable in the active library, the first playable library track is still used.

**M now controls the recorder interaction itself.** Normal builds do not change compass
state and do not depend on a compass or radio transmitter being equipped.

## Controller architecture

`SoulRecorderController` is now only the raid-level host. It detects **M**, resolves the
starter cassette, obtains the local raid player, and owns raid teardown.

`SoulRecorderUsableItemController` derives from SPT's existing
`Player.UsableItemController`. It owns the recorder interaction, tape transport, and
state transitions. Its overrides line up with SPT's first-person usable-item contract:

- `Hide()` uses the same eject/exit path as leaving the interaction;
- `SetAim(bool)` is the play/stop action seam for the future physical recorder;
- `ToggleAim()` toggles that transport action;
- `ExamineWeapon()` is disabled for the recorder.

The current milestone intentionally does not force this controller into the player's
hands without a real item prefab. Doing so would require an inventory item, a compatible
`WeaponPrefab`/`ObjectInHands`, and recorder animations that do not yet exist in SPT.

## Explicit recorder states

| State | Meaning | Exit condition |
| --- | --- | --- |
| `Idle` | No recorder interaction is active. | **M** resolves the starter cassette and enters `LoadingTape`. |
| `LoadingTape` | Cassette insertion has started. | The presentation's insertion duration completes, then the controller enters `Ready`. |
| `Ready` | Cassette is inserted; audio may be loading or waiting for a play action. | Confirmed audio playback enters `Playing`; **M** enters `Ejecting`. |
| `Playing` | The recorder `AudioSource` has confirmed playback. | Natural completion or the usable-item stop action returns to `Ready`; **M** enters `Ejecting`. |
| `Ejecting` | Playback is stopped and cassette ejection has started. | The presentation's ejection duration completes, then the controller returns to `Idle`. |

The headless presentation uses short non-zero insertion/ejection durations so these are
real observable lifecycle states even before final animations are connected.

## Custom first-person asset hooks

`ISoulRecorderHandsView` is the presentation boundary for a future distributable
first-person recorder. It receives explicit hooks for:

- interaction enter and exit;
- cassette insertion started and completed;
- playback started and stopped;
- cassette ejection started and completed;
- insertion and ejection animation durations.

The controller also exposes cassette insertion/ejection and state-change events for
later UI, sound, and inventory integration. Audio and state do not depend on a view, so
custom assets can be added without replacing the proven recorder backend.

## Development proxy isolation

The old radio-transmitter/compass hands proxy was moved to
`DevelopmentRecorderHandsProxy` and is excluded from normal builds. It is available
only when explicitly compiling with:

```text
-p:RecorderDevelopmentProxy=true
```

That opt-in build defines `SOULPLAYER_RECORDER_DEV_PROXY`. It exists only for regression
diagnostics and is not part of the release recorder architecture. No additional proxy
work is planned.

## What still requires custom assets

The controller and animation hooks are ready, but a visible first-person recorder still
requires legally distributable custom content:

1. recorder body prefab and materials;
2. cassette mesh, materials, and a stable cassette mount/bone;
3. first-person hands positioning compatible with SPT's `ObjectInHands` setup;
4. draw, idle, insert, play/stop, eject, and put-away animation clips/controller;
5. optional mechanical button, lid, insertion, and ejection sounds;
6. an SPT inventory/template registration and bundle-loading path for the custom item.

Once those assets exist, an `ISoulRecorderHandsView` implementation can drive them from
the hooks above and the physical controller can be created through SPT's normal usable-
item/first-person hands factory path.

## Build verification

Both configurations were compiled successfully against the installed SPT 4.1.2 root at
`D:\SPT_4.1.2`:

- Release build with the production headless presentation: **0 errors**;
- Debug build with `RecorderDevelopmentProxy=true`: **0 errors**.

The four remaining warnings are existing UnityWebRequest deprecation warnings in the
menu and recorder audio backends; this milestone deliberately leaves those proven audio
paths unchanged.

The new controller/state lifecycle still needs an in-raid runtime pass after packaging,
especially once the first custom recorder prefab and animation clips are available.
