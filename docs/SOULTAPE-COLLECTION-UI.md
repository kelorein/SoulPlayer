# SoulTape Collection + Favorites UI v1

## Player-facing scope

The existing MUSIC taskbar entry still opens `SoulPlayerWindow`. Its sidebar now switches
between LIBRARY and COLLECTION; it does not create a second menu. Library search,
folders, pagination, rescanning, mini-player controls, post-raid settings, track rows,
and playback remain on the original Library page. The bottom player bar remains visible
on both pages and switching pages does not restart a scan or alter playback.

Collection uses a four-column, two-row grid with eight cassette slots per page. It is
designed for the eventual 20–50+ curated collectible tapes and retains its filter/page
state during the current window session.

## Collection projection and privacy

`SoulTapeCollectionProjection` is a pure, testable boundary between progression data and
Unity rendering. It includes only catalog entries whose `Rarity` is non-null. Generated
personal-library tracks therefore do not become slots and do not affect progress.

The projection copies artist, title, rarity, audio availability, favorite state, and
recorder-selected state only after the stable cassette ID is unlocked. A locked
projection contains none of that presentation metadata, which prevents accidental
disclosure through labels, filters, sorting, or empty-state text. Locked cards display
only:

```text
SOULTAPE
???
UNDISCOVERED
```

Ordering is deterministic: discovered cards first, then stable cassette ID. Favorite
changes do not reorder ALL or DISCOVERED views.

## Progress, filters, and rarity

The header reports `DISCOVERED n / total`, where both values count curated non-null-rarity
entries only. Filters are:

- ALL: every collectible slot;
- DISCOVERED: unlocked slots only;
- FAVORITES: discovered favorites only;
- UNDISCOVERED: locked generic slots only.

Discovered cards show a small rarity strip and label. The centralized palette uses gray
for Common, green for Uncommon, blue for Rare, purple for Epic, and amber for Legendary.
Card backgrounds remain dark and restrained.

## Favorites and unavailable audio

Only discovered cards receive a FAVORITE/FAV ON button. It calls the existing
`SoulTapeCollection.SetFavorite` method, which saves immediately. The card list is
re-projected after both success and failure. Because the collection rolls back failed
saves, the UI cannot display an unpersisted favorite; instead it reports `FAVORITE NOT
SAVED` and leaves the actual state unchanged.

A discovered cassette remains visible and favorited when its file is unavailable. The
card adds `AUDIO MISSING`. A later catalog refresh reconnects audio and refreshes the open
page through `SoulTapeCatalog.Changed` without changing progression.

## Recorder selection

Every discovered collectible card has a `LOAD RECORDER` action. The explicitly selected
card displays `RECORDER TAPE`, a restrained outline, and remains independent from
FAVORITE/FAV ON. The header shows `RECORDER: artist — title`, or `RECORDER: NONE` when
there is no explicit selection.

Selection saves through `SoulTapeCollection.SetRecorderTape`. A failed save rolls back
and the card list re-projects actual persisted state before reporting `RECORDER SELECTION
NOT SAVED`. Locked cards have no selection action. A selected missing-audio cassette
stays selected and displays `AUDIO MISSING`.

Null-rarity generated/personal entries remain outside the collectible UI. If another
future surface selects one, the projection can report that a selection exists but does
not expose its artist or title in this curated Collection page.

## Profile and live-refresh safety

The page consumes the already-bound `SoulTapeCollection`; it does not resolve profiles.
`MenuScreen.Show` already receives the authoritative `EFT.Profile`, so its Harmony
postfix finds that typed argument from `object[] __args` and passes `ProfileId` to
`SoulTapeCollectionController.EnsureProfileId`. That method delegates to the existing
host, keeping periodic backend resolution and the raid-player fallback intact.

When the collection is unloaded, the page renders `COLLECTION UNAVAILABLE` with no cards
or catalog totals. It subscribes to both collection and catalog changes, marks rendering
dirty, and rebuilds only after an event or direct UI action. A failed profile switch
clears prior in-memory presentation state and raises the same collection event without
overwriting either profile's persisted data. Subscriptions are removed when the page is
destroyed.

## Deferred milestone

Collection v1 selects which cassette M will load, but does not directly play, insert, or
eject it from the menu. Final recorder models, hands animations, cassette artwork,
inventory items, duplicate rewards, and achievements remain deferred. World spawning
and pickup behavior are unchanged.

## Focused EFT acceptance

After offline validation passes, one menu test should verify:

1. MUSIC opens the same SoulPlayer window and defaults to LIBRARY.
2. COLLECTION opens without changing current playback or starting a new scan.
3. Eight cards fit as a four-by-two grid and pagination remains readable.
4. Locked cards reveal no artist, title, rarity, or identifier.
5. Progress and ALL/DISCOVERED/FAVORITES/UNDISCOVERED filters match the profile.
6. A discovered card toggles FAVORITE/FAV ON and remains correct after closing/reopening.
7. `LOAD RECORDER` changes one card to `RECORDER TAPE` without changing favorites.
8. A missing-audio selected tape remains selected and shows `AUDIO MISSING`.
9. A catalog refresh updates audio availability while COLLECTION remains open.
10. Switching back to LIBRARY preserves search/page state and all existing controls.
11. With no loaded profile, COLLECTION shows only `COLLECTION UNAVAILABLE`.
