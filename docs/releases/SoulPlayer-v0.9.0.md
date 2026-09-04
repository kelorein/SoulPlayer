# SoulPlayer 0.9.0 for SPT 4.1.3

SoulPlayer 0.9.0 turns the cassette prototype into a full music-discovery and raid-playback system. Find SoulTape cassettes at curated locations, permanently unlock music from your own local library, organize favorites, and carry the SoulRecorder into raids with fast controls and a polished two-dimensional interface.

## Highlights

- **SoulTape collection across curated maps:** 108 hand-authored cassette locations cover Bigmap (Customs), Factory Day, Interchange, Laboratory, Lighthouse, Reserve, Ground Zero, Shoreline, Streets of Tarkov, and Woods.
- **A new discovery mix every raid:** SoulPlayer chooses one to three distinct anchors and tracks per raid, prioritizing undiscovered music and avoiding duplicate songs or locations.
- **Your library becomes the collection:** available local tracks generate stable SoulTape entries, while discoveries and Favorites persist per profile across restarts and library rescans.
- **Flexible raid shuffle:** choose `FavoritesOnly`, `FavoritesFirst`, or `Discovered` for raid cassette playback.
- **Fast SoulRecorder controls:** press **M** to start or stop and **N** to play the next discovered cassette.
- **Polished presentation:** the recorder and discovery interfaces now use responsive 2D overlays embedded in the plugin. World pickups use a redistribution-safe CC0 cassette visual derived from the BlendSwap asset by comeinandburn.
- **Live volume control:** F12 changes apply immediately; NumPad0–4 select volume presets and show a compact HUD.
- **Exact outcome music:** route each track to **Main**, **Extract**, or **Death** and SoulPlayer preserves the exact selected outcome track through the post-raid transition.
- **Small quality-of-life improvements:** **ESC** closes SoulPlayer, path handling is more portable, and cassette/raid lifecycle cleanup is more robust.

## Installation

Extract `SoulPlayer-v0.9.0.zip` into the SPT installation directory. The archive installs:

- `Soulplayer.dll`
- `NAudio.Core.dll`
- `NAudio.Flac.dll`
- `soultape_world.bundle`

Existing SoulPlayer configuration and profile collection files live outside the plugin folder and are preserved when updating normally.

## Compatibility and known limitations

- Built and validated for **SPT 4.1.3**.
- **Windows is the officially tested runtime platform.**
- Linux-friendly path normalization and case-sensitive path behavior provide groundwork for Linux/Wine/Proton use, but EFT/SPT runtime behavior on those platforms has not been verified for this release.
- Fika has not been tested.
- Factory Night deliberately does not reuse Factory Day cassette coordinates. Maps without curated anchors spawn no SoulTapes.
- SoulPlayer plays local files only. It does not stream, upload, modify, or redistribute your music.

## Packaging and licensing

The ZIP contains no downloaded candidate music, EFT/Battlestate assets, preview renders, debug symbols, or source-authoring artifacts. The world cassette bundle is visual-only and contains the CC0-derived cassette prefab. Attribution, license, and transformation details are included in `THIRD-PARTY-NOTICES.txt` and the repository asset manifest.
