# SoulTape Collection v1

## Intended gameplay loop

The final SoulTape loop is:

```text
world cassette
    -> discover and pick up in a raid
    -> unlock its stable cassette ID permanently for the active SPT profile
    -> browse it in the collection
    -> favorite and select it
    -> play the unlocked cassette through SoulRecorder
```

Collection v1 implements the catalog and permanent progression underneath that loop.
World Discovery v1 now supplies the first curated raid spawns and direct pickup path;
the full collection browser, recorder models, hands, and animations remain deferred.

## Cassette catalog

Every playable library track is represented by a `SoulTapeCatalogEntry` with:

- stable cassette ID;
- artist and title;
- audio/library reference;
- optional rarity;
- optional map/spawn hints;
- the currently resolved `MusicTrack`, when its audio file is available.

The approved bundled soundtrack has explicit human-readable IDs, including:

```text
soul-tape.scott-buckley.the-long-dark
```

Other library songs receive IDs derived from a SHA-256 audio fingerprint. Moving or
renaming an unchanged file therefore does not change its cassette ID. The catalog keeps
the fingerprint as its library reference, so later world-spawn definitions can point to
a cassette without depending on a Windows file path.

Generated entries require a complete SHA-256 fingerprint. If hashing fails because a
file disappeared or became unreadable during a scan, the track receives no generated
cassette ID for that scan; SoulPlayer logs the skip and retries naturally on a later
library scan. It never creates a shared fallback such as `soul-tape.audio.unavailable`.

Curated definitions can also carry an expected SHA-256 fingerprint. When present, both
normalized artist/title metadata and the fingerprint must match before a library track
is attached to the curated cassette ID. The current approved definitions intentionally
leave this field empty until the exact release audio files are finalized; the catalog
contains a release TODO beside those definitions so their vetted hashes can be added
without changing IDs or progression format. A supplied hash mismatch is logged and the
curated ID remains unbound.

`SoulTapeSpawnHint` carries a map ID, semantic spawn-group tag, and weight. Separately,
`SoulTapeSpawnAnchor` records an author-reviewed canonical map ID, solved world
transform, surface normal, semantic tag, source, and enabled state. Neither structure
references or redistributes EFT assets.

Curated anchors are the authoritative long-term placement source. Production World
Discovery uses only committed curated anchors. The current Factory Day set is embedded
into `Soulplayer.dll`; the author's editable BepInEx output is never read as production
spawn data. See `SOULTAPE-WORLD-DISCOVERY.md` for the selection rules.

## Per-profile progression

`SoulTapeCollection` exposes the progression API:

```text
UnlockTape(id)
IsUnlocked(id)
SetFavorite(id, bool)
IsFavorite(id)
GetUnlockedTapes()
GetFavoriteTapes()
```

The active profile is resolved from SPT's backend session, with the local raid player's
profile ID as a fallback. Collection data is stored under:

```text
BepInEx/config/SoulPlayer/collections/<profile-id>.json
```

Each profile file contains a schema version, profile ID, unlocked cassette IDs, and
favorite cassette IDs. Mutations save immediately and log collection load, save,
unlock, and favorite operations.

A profile with no existing collection is granted
`soul-tape.scott-buckley.the-long-dark` and saved immediately.

## Save safety and unavailable audio

Collection saves use a temporary file and retain the previous primary document as a
`.bak` file. Load behavior is deliberately conservative:

- a valid primary file loads normally;
- an invalid primary can recover from its valid backup;
- if both copies are unreadable, SoulPlayer leaves them untouched and refuses
  progression mutations for that profile instead of overwriting recoverable data.

Unlocked and favorite IDs are never filtered out merely because their current audio
file is missing. A missing personal track may be absent from the visible catalog for
that session, but its ID remains in progression. Restoring the same audio bytes, even
under a renamed file, reconnects the generated cassette ID automatically.

## Recorder integration

SoulRecorder's validated usable-item lifecycle is unchanged. Its entry step now:

1. ensures the active profile collection is loaded;
2. asks the collection for unlocked catalog entries;
3. prefers the unlocked **Scott Buckley - The Long Dark** cassette when its audio is
   available;
4. otherwise uses another unlocked cassette with available audio;
5. refuses playback when no unlocked cassette can resolve to an audio file.

The recorder never falls back to an arbitrary locked library track.

## Curated authoring milestone

The opt-in placement-tools build supports an in-raid workflow for creating anchors
without copying coordinates manually. It moves the local player in a safe flight mode,
raycasts from the first-person view, previews a SoulPlayer-owned placeholder cassette,
solves surface clearance/collision, and appends the valid result to editable JSON.

See `SOULTAPE-AUTHORING.md` for the build command, controls, output location, and safety
behavior. Normal Release builds exclude active placement behavior.

## World Discovery v1

Factory Day currently has ten committed anchors. At raid startup SoulPlayer activates
one to three of them, limited by the number of eligible locked cassettes. The starter,
unlocked tapes, unavailable audio, and personal/generated entries without explicit
rarity are excluded.

Collecting a cassette calls `UnlockTape(id)` immediately. The profile JSON is saved at
pickup time: extraction is not required and dying later does not revoke the discovery.
The pickup does not enter stash inventory. The temporary world cassette and discovery
notification use SoulPlayer-owned presentation that can be replaced by final art later.

Favorites/collection browsing, explicit recorder tape selection, final art, and
duplicate rewards remain later milestones.

No copyrighted Battlestate recorder or cassette assets are included.

## Verification

Automated tests cover:

- starter grant and initial persistence;
- unlock/favorite API behavior;
- rollback when a save fails;
- preservation of missing catalog IDs;
- generated ID stability after a file rename;
- recovery from a valid backup after primary-file corruption;
- World Discovery eligibility and deterministic rarity-weighted planning;
- committed Factory Day anchor packaging;
- immediate discovery persistence, reload, rollback, events, and profile separation.
