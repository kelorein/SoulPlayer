# Changelog

All notable SoulPlayer changes are documented here.

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
