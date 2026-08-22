# SoulPlayer

[![Release](https://img.shields.io/github/v/release/kelorein/SoulPlayer?display_name=tag&sort=semver)](https://github.com/kelorein/SoulPlayer/releases/latest)
![SPT](https://img.shields.io/badge/SPT-4.1.3-e06c75)
[![License](https://img.shields.io/badge/license-MIT-4c9ee8)](LICENSE)
[![Buy me a cookie on Ko-fi](https://img.shields.io/badge/Ko--fi-Buy_me_a_cookie-ff5e5b?logo=ko-fi&logoColor=white)](https://ko-fi.com/kelorein)

SoulPlayer is an in-game local music player for **SPT 4.1.3**. It replaces Tushonka's normal menu soundtrack experience with your own local music library while keeping playback and folder management directly inside the game.

SoulPlayer does **not** stream, upload, modify, or redistribute your music. Playback stays on your computer.

> [!IMPORTANT]
> **Current release:** SoulPlayer 0.8.0  
> **Supported SPT version:** 4.1.3  
> **Operating system:** Windows

## Features

- Plays local **MP3, FLAC, OGG, and WAV** files.
- Recursively scans one or more music folders in the background.
- Full in-game music library with search, paging, seeking, shuffle, repeat, and volume controls.
- Persistent mini-player across out-of-raid menus.
- Dedicated keyboard Play/Pause, Stop, Next, and Previous media-key support.
- Optional additional hotkeys through the BepInEx configuration interface.
- Keeps music playing while matching and loading into a raid.
- Smoothly fades SoulPlayer when deployment begins.
- Suspends unnecessary interface work during raids.
- Separate playlists for survived and failed raids.
- Post-raid autoplay enabled by default on fresh installs.
- Automatically suppresses Tushonka's built-in menu music while SoulPlayer is active.
- Preserves configuration when updating the plugin.

## In-game folder browser

SoulPlayer 0.8.0 adds a completely in-game music folder browser. No external Windows folder-selection popup is required.

The browser includes:

- Quick Access for Music, Desktop, Downloads, Documents, and This PC.
- Drive browsing.
- Back and Up navigation.
- Clickable folder navigation.
- Current-location display.
- Folder pagination.
- **USE THIS FOLDER** selection.
- Manual path entry as an advanced fallback.

This keeps folder selection usable while running fullscreen.

## Post-raid music

Open **RAID MUSIC** to configure separate folders for:

- Survived raids.
- Failed / death raids.

If the selected outcome folder contains no playable tracks, SoulPlayer safely falls back to the normal library.

SoulPlayer 0.8.0 also debounces post-raid result events so UI stalls do not cause repeated track changes. The intended transition is:

**Raid ends → short transition → one post-raid track starts and continues playing.**

## Tushonka menu music

SoulPlayer is intended to act as your menu music source.

SoulPlayer 0.8.0 automatically suppresses Tushonka's built-in menu music while SoulPlayer is active. Other game audio is unaffected.

## Compatibility

| Component | Supported version |
| --- | --- |
| SPT | **4.1.3** |
| SoulPlayer | **0.8.0** |
| Operating system | Windows |
| Fika | Not tested |

SoulPlayer 0.8.0 was updated and tested specifically for **SPT 4.1.3**.

## Installation

1. Download `SoulPlayer-v0.8.0.zip` from the latest release.
2. Close the game, SPT Launcher, and SPT Server.
3. Extract the ZIP directly into your SPT installation folder.
4. Confirm the final plugin folder is:

   ```text
   <SPT>\BepInEx\plugins\SoulPlayer\
   ```

5. Confirm it contains:

   ```text
   Soulplayer.dll
   NAudio.Core.dll
   NAudio.Flac.dll
   ```

6. Start the SPT Server and Launcher normally.
7. Open **MUSIC** from the main menu.
8. Select **ADD MUSIC FOLDER**, browse to your library, and choose **USE THIS FOLDER**.

At startup, `BepInEx/LogOutput.log` should contain:

```text
SoulPlayer 0.8.0 loaded. Library scan started.
```

## Configuration

SoulPlayer settings are stored at:

```text
<SPT>\BepInEx\config\com.kelorein.soulplayer.cfg
```

Important settings include:

| Setting | Default | Description |
| --- | ---: | --- |
| Music folders | `D:\soulseek_share` | Folders scanned recursively. Separate multiple paths with `|`. |
| Volume | `0.65` | SoulPlayer playback volume from 0 to 1. |
| Shuffle | On | Randomizes the active queue. |
| Repeat mode | Off | `0` off, `1` repeat queue, `2` repeat one. |
| Show mini player | On | Shows compact controls in out-of-raid menus. |
| Mute Tushonka music | On | Suppresses the built-in menu soundtrack while SoulPlayer is active. |
| Enable media keyboard keys | On | Enables dedicated media-key control. |
| Autoplay after raid | On | Starts outcome-specific music after raids. |
| Raid fade-out seconds | `4` | Fade duration when deployment begins. |
| Post-raid track fade-out seconds | `1.5` | Fade duration before outcome music starts. |

Most normal music-management features are also available directly inside SoulPlayer.

## Updating

Close SPT and extract the new release over the existing SoulPlayer files. Your configuration is stored separately and normally remains intact.

## Uninstalling

Close SPT and remove:

```text
<SPT>\BepInEx\plugins\SoulPlayer
```

Optionally remove the following file to erase saved settings:

```text
<SPT>\BepInEx\config\com.kelorein.soulplayer.cfg
```

## Troubleshooting

### The MUSIC button or interface is missing

Confirm `Soulplayer.dll`, `NAudio.Core.dll`, and `NAudio.Flac.dll` are together in `BepInEx/plugins/SoulPlayer` and that you installed SoulPlayer 0.8.0 for SPT 4.1.3.

Check `BepInEx/LogOutput.log` for dependency, plugin-loading, or Harmony patch errors.

### No music appears

- Confirm the selected folder exists.
- Confirm it contains MP3, FLAC, OGG, or WAV files.
- Wait for the background scan to finish.
- Try **RESCAN LIBRARY**.
- Remove and add the folder again if needed.
- Check `BepInEx/LogOutput.log` for SoulPlayer messages.

### Two soundtracks play together

SoulPlayer 0.8.0 normally suppresses Tushonka's menu music automatically.

If it is still audible, restart SPT completely and check `BepInEx/LogOutput.log` for SoulPlayer music-volume patch errors. As a temporary fallback, the built-in Music volume can still be set manually to `0`.

### Post-raid music does not start

Confirm `Autoplay after raid = true` and that your survived / failed music folders contain supported audio files. Fresh 0.8.0 installations enable autoplay automatically.

## Building from source

Requirements:

- Visual Studio 2022 or the .NET SDK with .NET Framework 4.7.2 targeting support.
- A local SPT 4.1.3 installation for compile-time references.

Run:

```powershell
dotnet build SoulPlayer.csproj -c Release -p:SptRoot="C:\Path\To\SPT"
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for development expectations.

## Privacy

SoulPlayer works entirely with files stored on your own computer. It does not upload music, stream your library to an external service, modify your music files, or redistribute copyrighted music.

## Credits and licenses

SoulPlayer is released under the [MIT License](LICENSE).

Runtime audio support is provided by NAudio.Core and NAudio.Flac. Their licenses and project links are listed in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

## Support development

If SoulPlayer improves your SPT experience, you can support continued maintenance and future compatibility work:

[![Support on Ko-fi](https://img.shields.io/badge/Support_on_Ko--fi-ff5e5b?logo=ko-fi&logoColor=white)](https://ko-fi.com/kelorein)

Support is always optional. SoulPlayer remains free and open source.
