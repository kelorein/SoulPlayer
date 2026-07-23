# SoulPlayer

[![Release](https://img.shields.io/github/v/release/kelorein7/soulplayer?display_name=tag&sort=semver)](https://github.com/kelorein7/soulplayer/releases/latest)
[![SPT](https://img.shields.io/badge/SPT-4.0.13-e06c75)](https://www.sp-tarkov.com/)
[![License](https://img.shields.io/badge/license-MIT-4c9ee8)](LICENSE)

SoulPlayer is an in-game local music player for **SPT 4.0.13**. It adds a persistent, Tarkov-styled music interface to the menus, plays your own local library, supports keyboard media controls, and can switch to separate music after successful or failed raids.

SoulPlayer does not stream, upload, modify, or redistribute your music. Playback stays on your computer.

> [!IMPORTANT]
> SoulPlayer 0.7.0 fixes the rhythmic raid-time stutter present in 0.6.4. Do not use an older build.

## Features

- Plays local **MP3, FLAC, OGG, and WAV** files.
- Scans one or more Windows music folders recursively in the background.
- Adds a compact persistent mini-player across Character, Traders, Flea Market, Handbook, Hideout, and other menu screens.
- Includes a full library window with folder management, search, paging, seeking, shuffle, repeat, and volume controls.
- Supports dedicated keyboard Play/Pause, Stop, Next, and Previous media keys.
- Supports optional additional hotkeys through the BepInEx configuration interface.
- Keeps menu music playing through matching and map loading.
- Fades music when deployment begins and suspends all hidden SoulPlayer interface work during raids.
- Supports separate survived and failed-raid playlists with optional post-raid autoplay.
- Preserves settings when updating the plugin.

## Compatibility

| Component | Supported version |
| --- | --- |
| SPT | 4.0.13 |
| Operating system | Windows |
| SoulPlayer | 0.7.0 |

Other SPT releases have not been validated. Install only the SoulPlayer version built for your exact SPT release.

## Installation

1. Download `SoulPlayer-v0.7.0.zip` from the [latest GitHub release](https://github.com/kelorein7/soulplayer/releases/latest).
2. Close Escape from Tarkov, the SPT Launcher, and the SPT Server.
3. Open the ZIP and copy its `BepInEx` folder into your SPT installation folder.
4. Confirm the final plugin path is:

   ```text
   <SPT>\BepInEx\plugins\SoulPlayer\Soulplayer.dll
   ```

5. Start the SPT Server and Launcher normally.

At startup, `BepInEx/LogOutput.log` should contain:

```text
SoulPlayer 0.7.0 loaded. Library scan started.
```

### Updating

Close SPT and copy the new release over the existing files. Your configuration remains under:

```text
<SPT>\BepInEx\config\com.kelorein.soulplayer.cfg
```

### Uninstalling

Close SPT and remove:

```text
<SPT>\BepInEx\plugins\SoulPlayer
```

Optionally remove `BepInEx/config/com.kelorein.soulplayer.cfg` to erase saved SoulPlayer settings.

## Using SoulPlayer

1. Start SPT and enter the main menu.
2. Select **MUSIC** from the bottom toolbar.
3. Add a folder containing your music.
4. Wait for the background scan to finish, then select a track or press Play to shuffle the library.

The compact mini-player remains available throughout the out-of-raid interface. It automatically hides when a real raid begins and returns afterward.

### Post-raid music

Open **RAID MUSIC** in the SoulPlayer window to configure separate folders for:

- Survived raids
- Failed raids

Enable **AUTOPLAY** if SoulPlayer should start an outcome-specific track when the raid result screen appears.

## Configuration

Open the BepInEx configuration interface with `F1`, then select SoulPlayer.

| Setting | Default | Description |
| --- | ---: | --- |
| Music folders | `D:\soulseek_share` | Folders scanned recursively. Separate multiple paths with `|`. |
| Volume | `0.65` | SoulPlayer playback volume from 0 to 1. |
| Shuffle | On | Randomizes the active queue. |
| Repeat mode | Off | `0` off, `1` repeat queue, `2` repeat one. |
| Show mini player | On | Shows compact controls above the bottom-right menu toolbar. |
| Enable media keyboard keys | On | Enables dedicated media-key control. |
| Autoplay after raid | Off | Plays outcome-specific music on the raid result screen. |
| Raid fade-out seconds | `4` | Fade duration when deployment begins. |
| Post-raid track fade-out seconds | `1.5` | Fade duration before outcome music starts. |

Music folder management and the primary playback settings are also available directly inside SoulPlayer.

## Troubleshooting

### No music appears

- Confirm the folder exists and contains MP3, FLAC, OGG, or WAV files.
- Wait for the background scan to finish.
- Remove and add the folder again from the SoulPlayer library window.
- Check `BepInEx/LogOutput.log` for SoulPlayer messages.

### Two soundtracks play together

SoulPlayer and Tarkov's built-in menu soundtrack are separate. Set Tarkov's in-game **Music** volume to `0` if you only want SoulPlayer.

### The MUSIC button or interface is missing

- Confirm `Soulplayer.dll`, `NAudio.Core.dll`, and `NAudio.Flac.dll` are together in `BepInEx/plugins/SoulPlayer`.
- Confirm you installed SoulPlayer for SPT 4.0.13.
- Check `BepInEx/LogOutput.log` for dependency or patch errors.

### Reporting a bug

Use the [bug report template](https://github.com/kelorein7/soulplayer/issues/new/choose) and include the SoulPlayer version, SPT version, reproduction steps, relevant logs, and installed mod list.

## Building from source

Requirements:

- Visual Studio 2022 or the .NET SDK with .NET Framework 4.7.2 targeting support
- A local SPT 4.0.13 installation for compile-time references

Run:

```powershell
dotnet build SoulPlayer.csproj -c Release -p:SptRoot="C:\Path\To\SPT"
```

Or run `build.bat` and provide the SPT installation path when prompted.

See [CONTRIBUTING.md](CONTRIBUTING.md) for development expectations.

## Credits and licenses

SoulPlayer is released under the [MIT License](LICENSE).

Runtime audio support is provided by NAudio.Core and NAudio.Flac. Their licenses and project links are listed in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

Escape from Tarkov is a trademark of Battlestate Games. SoulPlayer is an independent community project and is not affiliated with or endorsed by Battlestate Games or the SPT project.

## Support development

If SoulPlayer improves your SPT experience, you can support continued maintenance and future compatibility work:

[![Sponsor on GitHub](https://img.shields.io/badge/Sponsor_on_GitHub-EA4AAA?logo=githubsponsors&logoColor=white)](https://github.com/sponsors/kelorein7)
[![Support on Ko-fi](https://img.shields.io/badge/Support_on_Ko--fi-FF5E5B?logo=kofi&logoColor=white)](https://ko-fi.com/kelorein7)

Support is always optional. SoulPlayer remains free and open source.
