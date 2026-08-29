# SoulTape Collection v1

## Intended gameplay loop

The final SoulTape loop is:

```text
world cassette
    -> discover and pick up in a raid
    -> unlock its stable cassette ID permanently for the active SPT profile
    -> browse it in the collection
    -> optionally favorite it
    -> let SoulRecorder shuffle eligible discovered cassettes in raids
```

Collection v1 implements the catalog and permanent progression underneath that loop.
World Discovery v1 supplies the first curated raid spawns and direct pickup path.
Collection + Favorites UI v1 exposes that progression in the existing MUSIC window.
SoulRecorder's raid shuffle UX selects from permanent discoveries and favorites without
requiring a manually loaded recorder tape.

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

The active profile is resolved from SPT's backend session, with session and local
raid-player fallbacks. The menu patch additionally captures the authoritative
`EFT.Profile` already passed to `MenuScreen.Show` and sends its `ProfileId` through the
same `SoulTapeCollectionHost`. Collection data is stored under:

```text
BepInEx/config/SoulPlayer/collections/<profile-id>.json
```

Each profile file contains a schema version, profile ID, unlocked cassette IDs, and
favorite cassette IDs. Older files may still contain `SelectedRecorderCassetteId`;
SoulPlayer reads that legacy field without deleting it, but it no longer affects the UI
or SoulRecorder playback. Mutations save immediately and log collection load, save,
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

SoulRecorder's validated usable-item and anti-stutter audio lifecycle is unchanged. The
persisted `Raid cassette playback mode` config controls its eligible pool:

- `FavoritesOnly`: discovered, favorited cassettes with available audio;
- `Discovered`: every discovered cassette with available audio;
- `FavoritesFirst` (default): favorites when at least one is available, otherwise all
  discovered cassettes.

An in-memory per-raid shuffle bag plays every eligible stable ID once before reshuffling
and avoids repeating the boundary tape. Favorites, discoveries, and restored/missing
audio are reconciled for the next selection without interrupting the current track. The
bag is intentionally not profile persistence and starts fresh with each raid/restart.
Undiscovered and unavailable tracks are never selected.

`Next raid cassette hotkey` defaults to `N` and is separate from the normal Library
`Next track hotkey`. While stopped it starts the next bag entry through the normal hidden
audio-preparation and insertion flow. While playing it reserves the next eligible entry,
runs the existing eject/stop transition, waits for presentation ownership to release,
then prepares and inserts the reserved cassette. One request may wait behind an active
prepare/insert/eject transition; repeated requests are ignored until that request has
completed.

## Curated authoring milestone

The opt-in placement-tools build supports an in-raid workflow for creating anchors
without copying coordinates manually. The author walks normally, aims the full-screen
gameplay camera at a surface, and presses F9. SoulPlayer solves surface alignment,
clearance, collision, nudging, and duplicates; it immediately saves a valid anchor and
briefly previews the exact result with a SoulPlayer-owned placeholder cassette.

The tool never teleports the player or changes player movement/collider state. See
`SOULTAPE-AUTHORING.md` for the build command, controls, output location, and safety
behavior. Normal Release builds exclude active authoring behavior.

## World Discovery v1

The release contains 108 committed anchors across ten curated maps. At raid startup SoulPlayer activates
one to three of them, limited by the number of available mode-eligible songs and anchors.
Anchor-to-track pairs exist only for that raid. Undiscovered songs are selected before
already discovered songs; discovered songs become fallback candidates only when too few
locked songs remain. Generated personal-library entries are eligible without a rarity,
allowing a large library to rotate through a much smaller reusable anchor set over many
raids.

The old per-profile `assignments/<profile>.json` mapping is legacy data. Runtime does not
read or update it and never deletes it automatically. Collection unlocks and historical
metadata remain independent and continue to persist under `collections/`.

Collecting a cassette calls `UnlockTape(id)` immediately. The profile JSON is saved at
pickup time: extraction is not required and dying later does not revoke the discovery.
The pickup does not enter stash inventory. The temporary world cassette and discovery
notification use SoulPlayer-owned presentation that can be replaced by final art later.

Final art and duplicate rewards remain later milestones. Collection browsing and
favorites are implemented in the existing MUSIC window.

## Collection browser

The MUSIC window now switches between its existing Library page and a SoulTape
Collection page without restarting scans or changing playback. The Collection is an
eight-card page (four columns by two rows) with ALL, DISCOVERED, FAVORITES, and
UNDISCOVERED filters plus `DISCOVERED n / total` progress.

The runtime collection catalog is the union of the current mode-eligible track IDs and
the profile's historically unlocked IDs. It is not capped by the number of anchors.
Generated and personal-library entries with null rarity remain collectible slots, while
historically discovered tracks remain visible even if their file or current mode changes.
A pure `SoulTapeCollectionProjection` removes artist, title, rarity, favorite, and audio
metadata from locked cards before Unity rendering receives them. Locked cards therefore
show only `SOULTAPE`, `???`, and `UNDISCOVERED`.

Discovered cards show artist, title, a restrained centralized rarity accent, favorite
state, and `AUDIO MISSING` when the current file cannot be resolved. Missing audio never
removes discovery or favorite progression.
Favorite buttons call the existing
transactional `SetFavorite(id, bool)` API and immediately re-project actual collection
state; a failed save leaves the card unchanged and reports that it was not saved.

The page listens to both collection and catalog change events. Unlocks, profile changes,
favorite mutations, and delayed library refreshes mark the view dirty and refresh it
while open without closing the MUSIC window. An unloaded profile renders only
`COLLECTION UNAVAILABLE`, never stale cards from an earlier projection.

The curated target remains approximately 20–50+ collectible cassettes. See
`SOULTAPE-COLLECTION-UI.md` for the presentation and acceptance details.

No copyrighted Battlestate recorder or cassette assets are included.

## Verification

Automated tests cover:

- starter grant and initial persistence;
- unlock/favorite API behavior;
- rollback when a save fails;
- preservation of missing catalog IDs;
- generated ID stability after a file rename;
- recovery from a valid backup after primary-file corruption;
- per-raid seeded World Discovery selection with undiscovered-first priority;
- unique track/anchor selection across libraries much larger than the anchor set;
- committed Factory Day anchor packaging;
- immediate discovery persistence, reload, rollback, events, and profile separation.
- FavoritesOnly, Discovered, and FavoritesFirst pool behavior;
- non-repeating shuffle cycles, boundary repeat avoidance, and live pool reconciliation;
- graceful empty favorite/discovery feedback and legacy selection isolation.
