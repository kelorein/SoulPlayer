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

- Press **M** during a live, local-player raid to begin the SoulRecorder interaction.
- EFT's normal `SetEmptyHands` transition temporarily puts away the current firearm/item.
  The recorder enters, inserts the cassette, starts audio, exits, and restores the exact
  previously held item through `SetInHands`.
- Music continues in the background after the recorder leaves, so normal combat resumes.
- Press **M** while music is playing to repeat the hands transition, show the recorder with
  its cassette seated, stop/eject it, exit, and restore the previous item.
- Pressing **M** during insertion cancels immediately, hides the presentation, and restores
  hands without pretending that a completed cassette needs to eject.
- Natural playback completion returns playback to idle rather than keeping a recorder on
  screen.
- Player death/unspawn, the existing post-raid result signal, raid teardown, and component
  or plugin disposal all force an immediate, idempotent audio/presentation reset.
- The preferred starter cassette remains **Scott Buckley - The Long Dark**. Recorder
  playback now resolves through the active profile's SoulTape collection and will only
  use another track when its cassette ID is also unlocked.

**M now controls the recorder interaction itself.** Normal builds do not change compass
state and do not depend on a compass or radio transmitter being equipped.

## SoulTape collection integration

The recorder lifecycle remains unchanged for SoulTape Collection v1. Before entering,
the raid host loads the active profile's cassette collection and resolves an unlocked
catalog entry with available audio. It never falls back to an arbitrary locked library
track.

New profiles permanently receive the **Scott Buckley - The Long Dark** cassette ID.
Missing audio prevents that cassette from playing but does not remove its unlock or
favorite state. See [SOULTAPE-COLLECTION.md](SOULTAPE-COLLECTION.md) for the catalog,
persistence, save-recovery, and future world-pickup design.

## Controller architecture

`SoulRecorderController` is the raid-level host. It detects **M**, resolves the cassette,
gates input to a live local raid player, owns raid teardown, and coordinates
`SptSoulRecorderNativeHandsControllerTransition`.

That transition saves the current `HandsController.Item`, holsters it through
`Player.DropCurrentController(...)`, enters EFT empty hands through `SetEmptyHands`, and later
holsters the empty-hands controller before calling
`Player.SetInHands(savedItem, ...)`. Restoration requests remain tokenized and idempotent.

`SoulRecorderInteractionController` independently owns tape transport, audio, and state
transitions. `ProceduralSoulRecorderHandsView` instantiates SoulPlayer's packaged animated
hands, recorder, and cassette after empty hands are confirmed. No EFT asset is shipped, and
the failed transient rangefinder donor route is not selected by Release.

## Explicit recorder states

| State | Meaning | Exit condition |
| --- | --- | --- |
| `Idle` | No recorder interaction is active. | **M** resolves the starter cassette and enters `LoadingTape`. |
| `LoadingTape` | Cassette insertion has started. | The presentation's insertion duration completes, then the controller enters `Ready`. |
| `Ready` | Cassette is inserted; audio may be loading or waiting for a play action. | Confirmed audio playback enters `Playing`; **M** enters `Ejecting`. |
| `Playing` | The recorder `AudioSource` has confirmed playback. Recorder presentation exits and the previous hands item is restored while this state continues. | Natural completion returns to `Idle`; **M** reacquires hands and enters `Ejecting`. |
| `Ejecting` | Playback is stopped and cassette ejection has started with empty-hands ownership. | Ejection and presentation exit complete, hands restore once, then the controller returns to `Idle`. |

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

The packaged custom recorder body, textured materials, cassette mesh, transform contracts,
and deterministic cassette transport are implemented. The CC0 hand derivative remains in
the reproducible authoring pipeline, but runtime has no hands-instantiation API and never
activates it. Installed SPT 4.1.2 inspection did not expose a supported native EFT recorder
template/controller/prefab contract, so remaining presentation work is:

1. runtime-accept the recorder-only packaged presentation and cassette transport;
2. revisit hands only if EFT later exposes a stable runtime-only native contract or new
   SoulPlayer-owned art is deliberately commissioned;
3. optionally add original mechanical button, lid, insertion, and ejection sounds;
4. add SPT inventory/template registration only if a later milestone intentionally moves
   from the current safe camera-local presentation into physical `ObjectInHands` ownership.

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

The next in-raid acceptance pass must verify the real empty-hands/firearm restoration flow,
background playback, terminal cleanup, and safe rejection of the current malformed hands
rig. Offline validation cannot prove those EFT controller transitions.
