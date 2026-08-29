# Changelog

All notable SoulPlayer changes are documented here.

## [0.9.0] - 2026-08-29

### Added

- Added the SoulTape collectible system across 108 hand-authored locations on ten curated SPT maps.
- Added per-raid randomized cassette and song selection, prioritizing undiscovered music and selecting distinct anchors and tracks without replacement.
- Added dynamic SoulTape entries for tracks in the user's local library, with profile-specific discoveries that persist across game and SPT restarts.
- Added `FavoritesOnly`, `FavoritesFirst`, and `Discovered` raid cassette shuffle modes.
- Added SoulRecorder raid controls: **M** starts or stops playback and **N** advances to the next discovered cassette.
- Added polished two-dimensional SoulRecorder and SoulTape discovery overlays embedded directly in `Soulplayer.dll`.
- Added live F12 volume updates, NumPad0–4 volume presets, and a compact in-raid volume HUD.
- Added per-track **Main**, **Extract**, and **Death** routing with exact routed playback after each raid outcome.
- Added **ESC** handling to close the SoulPlayer interface.
- Added a redistribution-safe CC0 world cassette visual derived from the BlendSwap cassette by comeinandburn.
- Added Linux-friendly path normalization and case-sensitivity groundwork; Windows remains the only officially tested runtime platform.

### Changed

- Expanded the cassette collection and favorites interfaces with discovered/undiscovered state, unavailable-track history, and improved recorder selection.
- Replaced the previous first-person 3D recorder/hand presentation with a polished 2D recorder overlay; the release package now carries only the visual-only world cassette bundle.
- Updated SoulPlayer assembly, plugin, package, documentation, and in-game version metadata to 0.9.0.

### Fixed

- Fixed post-raid routing so the exact selected Extract or Death track is preserved through delayed result-screen transitions.
- Fixed stale, missing, and rescanned library entries reconnecting incorrectly with persistent discoveries and routing metadata.
- Improved raid lifecycle cleanup, cassette interaction stability, path handling, playback selection, and volume synchronization.
- Prevented obsolete recorder/hand bundles, preview artifacts, authoring files, and unverified music candidates from entering release packages.

## [0.8.0] - 2026-08-21

### Added

- Full SPT 4.1.3 compatibility.
- Completely in-game music folder browser with Quick Access, drive browsing, Back/Up navigation, folder pagination, current-location display, and manual path fallback.
- Automatic suppression of Tushonka's built-in menu music while SoulPlayer is active.
- Post-raid result coordinator that waits for the result UI to settle before starting one outcome track.

### Changed

- Updated SoulPlayer assembly and plugin metadata to 0.8.0.
- Updated the build target for the SPT 4.1.3 `SPT_Runtime` folder layout.
- Post-raid autoplay is now enabled by default for fresh configurations.
- Improved music-volume handling during menu changes and the Tushonka Settings screen.
- Updated menu hooks for the current client layout.
- Improved fullscreen usability by removing the need for an external folder picker.

### Fixed

- Fixed repeated post-raid track changes caused by delayed or duplicate result-screen events.
- Prevented stale raid-state updates from pausing or restarting the selected post-raid song.
- Improved post-raid behavior during UI stalls and loading delays.
- Isolated client patch startup so one incompatible patch can log its own failure without preventing the rest of SoulPlayer from initializing.

## [0.6.5] - 2026-07-19

### Fixed

- Removed repeated full-scene searches while a raid is running.
- Fully suspended the hidden mini-player interface during raids.
- Preserved deployment fade timing with a rate-limited countdown lookup.
- Eliminated rhythmic raid-time frame spikes caused by SoulPlayer 0.6.4.

## [0.6.4] - 2026-07-18

### Added

- Smooth, configurable music fade when the deployment countdown appears.
- Smooth transition from the previous song into survived or failed-raid music.

## [0.6.3] - 2026-07-18

### Added

- Dedicated keyboard media controls for play/pause, stop, next, and previous.
- Optional additional shortcuts through the BepInEx configuration interface.

### Fixed

- Clean handoff from paused pre-raid music to outcome-specific post-raid music.

## [0.6.2] - 2026-07-18

### Changed

- Music continues through matching and map loading.
- Music pauses when the deployment countdown appears or the local player spawns.
- Hideout is excluded from raid music suspension.

## [0.6.1] - 2026-07-18

### Changed

- Added a smaller translucent mini-player with icon-only controls.
- Improved timer and progress-line layout.
- Reduced the visual scale of the full library window.

## [0.6.0] - 2026-07-18

### Added

- Persistent menu interface across Character, Traders, Flea Market, Handbook, Hideout, and other menu screens.
- A shared MUSIC entry point across bottom-toolbar tabs.
- Real raid-state detection and automatic overlay hiding.

## [0.5.0] - 2026-07-18

### Added

- Local music library and recursive folder scanning.
- MP3, FLAC, OGG, and WAV playback.
- Shuffle, repeat, seeking, volume control, and scrolling track title.
- Separate survived and failed-raid playlists.

[0.6.5]: https://github.com/kelorein7/soulplayer/releases/tag/v0.6.5
[0.6.4]: https://github.com/kelorein7/soulplayer/releases/tag/v0.6.4
[0.6.3]: https://github.com/kelorein7/soulplayer/releases/tag/v0.6.3
[0.6.2]: https://github.com/kelorein7/soulplayer/releases/tag/v0.6.2
[0.6.1]: https://github.com/kelorein7/soulplayer/releases/tag/v0.6.1
[0.6.0]: https://github.com/kelorein7/soulplayer/releases/tag/v0.6.0
[0.5.0]: https://github.com/kelorein7/soulplayer/releases/tag/v0.5.0
