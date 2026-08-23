# SoulRecorder Cassette Selection UX v1

## Per-profile selection

`SoulTapeCollectionData` keeps one optional `SelectedRecorderCassetteId` beside unlocks
and favorites in the existing per-profile JSON. Schema version remains 1. Older files
without the field load as no explicit selection and are not rewritten merely to add an
empty value.

`SoulTapeCollection.SetRecorderTape(id)` accepts only a cataloged, unlocked cassette. It
does not require current audio availability, because temporary file loss must not erase
player intent. Selection saves immediately, rolls back on failure, and raises the
existing collection change event only after success. Profile binding resets and reloads
selection with the rest of that profile's progression.

Favorites and recorder selection are independent. Selecting does not favorite a tape,
and favoriting does not select it.

## Collection UX

Discovered curated cards display `LOAD RECORDER`. The selected card displays `RECORDER
TAPE`, a subtle outline, and remains visibly selected even with `AUDIO MISSING`. The
Collection header names the selected curated tape or displays `RECORDER: NONE`.

Locked cards have no action and expose no selection state. Generated/null-rarity personal
tracks remain outside the collectible grid; their metadata is not surfaced through the
Collection header.

## Runtime resolution

Pressing M retains the existing recorder state machine and passes one resolved
`MusicTrack` into `SoulRecorderUsableItemController.EnterInteraction`.

Resolution is:

1. explicit selected tape, if unlocked and audio is available;
2. if explicit audio is missing, another unlocked available tape for this interaction
   only, with a warning and no persisted mutation;
3. if no explicit selection exists, the legacy unlocked starter-first selection;
4. otherwise no interaction until an unlocked audio track is available.

When the selected file returns and the catalog refreshes, M uses it again automatically.
The selected ID survives library refresh, file disappearance, profile reload, and game
restart.

## Temporary raid feedback

On successful M entry, a small temporary SoulPlayer-owned overlay shows:

```text
SOULRECORDER
Artist — Title
LOADING CASSETTE...
```

When playback begins it briefly changes to `NOW PLAYING`. It is not permanent and does
not replace the deferred physical recorder presentation.

The optional in-raid quick selector is not implemented in v1. Menu Collection selection
is the single required selection surface.
