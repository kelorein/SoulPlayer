# SoulPlayer included music sources

The default SoulPlayer music set is intentionally limited to tracks published under **Creative Commons Zero (CC0) / public-domain dedication** so the audio can be redistributed with SoulPlayer releases.

The source/license pages below were verified on 2026-08-22. Attribution is not required by CC0, but SoulPlayer keeps these credits for transparency and respect for the creators.

| # | Bundled filename | Creator | Source | License |
| ---: | --- | --- | --- | --- |
| 01 | `SRG774 - Sector.mp3` | SRG774 | https://opengameart.org/content/dark-sci-fi-audio-pack | CC0 1.0 |
| 02 | `SRG774 - Airy.mp3` | SRG774 | https://opengameart.org/content/dark-sci-fi-audio-pack | CC0 1.0 |
| 03 | `SRG774 - Pulse.mp3` | SRG774 | https://opengameart.org/content/dark-sci-fi-audio-pack | CC0 1.0 |
| 04 | `SRG774 - Urgent.mp3` | SRG774 | https://opengameart.org/content/dark-sci-fi-audio-pack | CC0 1.0 |
| 05 | `SRG774 - Transmission.mp3` | SRG774 | https://opengameart.org/content/dark-sci-fi-audio-pack | CC0 1.0 |
| 06 | `SRG774 - Title.mp3` | SRG774 | https://opengameart.org/content/dark-sci-fi-audio-pack | CC0 1.0 |
| 07 | `tricksntraps - Endless Moons.ogg` | tricksntraps | https://opengameart.org/content/free-surrealdream-music-pack | CC0 1.0 |
| 08 | `tricksntraps - Dreaming of Leaves.ogg` | tricksntraps | https://opengameart.org/content/free-surrealdream-music-pack | CC0 1.0 |
| 09 | `tricksntraps - Mystical Fungi Cave.ogg` | tricksntraps | https://opengameart.org/content/free-surrealdream-music-pack | CC0 1.0 |
| 10 | `tricksntraps - Frozen Ocean Trip.ogg` | tricksntraps | https://opengameart.org/content/free-surrealdream-music-pack | CC0 1.0 |
| 11 | `tricksntraps - Conscious Swamp.ogg` | tricksntraps | https://opengameart.org/content/free-surrealdream-music-pack | CC0 1.0 |
| 12 | `tricksntraps - Strange Reality Warp.ogg` | tricksntraps | https://opengameart.org/content/free-surrealdream-music-pack | CC0 1.0 |
| 13 | `Alex McCulloch - Synth Wave.mp3` | Alex McCulloch / Pro Sensory | https://opengameart.org/content/synth-wave | CC0 1.0 |
| 14 | `G_P - Synthwave Type.mp3` | G_P | https://opengameart.org/content/synthwavetype | CC0 1.0 |
| 15 | `SkyleTheFrench - Blackout.mp3` | SkyleTheFrench | https://opengameart.org/content/blackout | CC0 1.0 |
| 16 | `DST - MindStream.mp3` | DST | https://opengameart.org/content/mindstream | CC0 1.0 |
| 17 | `hatmix - Canary.ogg` | hatmix | https://opengameart.org/content/canary | CC0 1.0 |
| 18 | `Centurion_of_war - Technological Messup.ogg` | Centurion_of_war | https://opengameart.org/content/technological-messup | CC0 1.0 |
| 19 | `MintoDog - Space Adventure.mp3` | MintoDog | https://opengameart.org/content/space-adventure | CC0 1.0 |
| 20 | `iamoneabe - Vintage Menu.mp3` | iamoneabe | https://opengameart.org/content/vintage-menu | CC0 1.0 |

## Release packaging

Run `tools/Get-DefaultMusic.ps1` before building the release ZIP. It downloads the 20 source files into this folder using the readable filenames above.

The audio binaries do not need to be stored in Git history. The release ZIP should contain:

```text
BepInEx\plugins\SoulPlayer\DefaultMusic\
```

with all 20 audio files plus this source notice.

Do not replace any entry with a merely "royalty-free" track unless its license also clearly permits redistribution of the audio file itself.
