# SoulRecorder prototype

This development branch now contains the first runnable in-raid recorder backend.

## Current behavior

- SoulPlayer's normal menu player still fades/pauses when a raid begins.
- The recorder has a separate `AudioSource`, so recorder playback is allowed during the raid without weakening the existing menu-music raid suspension.
- Press **M** during a live raid to start/stop the recorder.
- The preferred starter tape is **Scott Buckley - The Long Dark** when that track is present in the active SoulPlayer library.
- If the preferred starter track is excluded/not installed, the prototype falls back to the first playable track in the active library.
- The cassette plays once and the recorder returns to idle when the track finishes.
- Leaving the raid always stops recorder playback.

## Physical-hands validation

SPT 4.1.x does not contain live EFT's newer `RecorderHandsController`, so the first prototype does not pretend that the final recorder model already exists.

For a safe first-person animation test, SoulPlayer temporarily recognizes SPT's existing `RadioTransmitterController` as a physical proxy:

1. Equip SPT's radio-transmitter usable item in raid.
2. Press **M**.
3. SoulPlayer calls the existing public radio-transmitter hands-controller state (`SetAim`) while the recorder audio plays.
4. Press **M** again to stop/lower it.

If no radio transmitter is currently in the player's hands, the recorder still works as an audio-only prototype and logs that the physical proxy is unavailable.

This validates the two difficult systems separately:

- reliable in-raid SoulPlayer audio/tape state;
- compatibility with SPT's existing first-person `UsableItemController` hands framework.

No live EFT recorder model, animation, prefab, decrypted metadata, or other game asset is bundled with SoulPlayer.

## Next milestone

After this prototype builds and is tested in SPT:

1. add a real `SoulRecorder` item/controller layer;
2. add cassette insert/eject state and animation hooks;
3. persist unlocked tapes per profile;
4. expose the unlocked cassette list to the recorder;
5. add physical cassette world spawns and collection notifications.
