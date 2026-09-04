# SoulPlayer default music pack

This is the approved starter soundtrack for SoulPlayer's included-music library.

The selection was finalized on 2026-08-22 after listening tests focused on instrumental metal, dark electronic, and cold cinematic music that fits SPT/Tarkov's atmosphere.

## Approved tracks

| # | Expected title | Creator | Source | License / release status |
| ---: | --- | --- | --- | --- |
| 01 | `Anders - Frostbite` | Anders / Anttu Janhunen | https://soundcloud.com/anttu-janhunen/frostbite | Artist catalog is presented as royalty-free/free to use. Keep as non-commercial audio and preserve creator credit. Do not redistribute from an unofficial mirror. |
| 02 | `Anders - False Awakenings` | Anders / Anttu Janhunen | https://soundcloud.com/anttu-janhunen/false-awakenings-reupload | CC BY-NC-SA on SoundCloud. Non-commercial redistribution only; preserve attribution and license. |
| 03 | `Anders - Into World Unknown` | Anders / Anttu Janhunen | https://soundcloud.com/anttu-janhunen/into-world-unknown-royalty-free | CC BY-NC-SA 3.0 attribution is documented for this track. Non-commercial redistribution only. |
| 04 | `Anders - Ex Nihilo` | Anders / Anttu Janhunen | https://soundcloud.com/anttu-janhunen/ex-nihilo | CC BY-NC-SA 3.0 attribution is documented for this track. Non-commercial redistribution only. |
| 05 | `Scott Buckley - Electric Dreams` | Scott Buckley | https://www.scottbuckley.com.au/library/electric-dreams/ | CC BY 4.0 |
| 06 | `Scott Buckley - Resonance` | Scott Buckley | https://www.scottbuckley.com.au/library/resonance/ | CC BY 4.0 |
| 07 | `Scott Buckley - Signal to Noise` | Scott Buckley | https://www.scottbuckley.com.au/library/signal-to-noise/ | CC BY 4.0 |
| 08 | `Scott Buckley - The Long Dark` | Scott Buckley | https://www.scottbuckley.com.au/library/the-long-dark/ | CC BY 4.0 |

## Release packaging

Run `tools/Get-DefaultMusic.ps1` before building a SoulPlayer release.

The helper downloads the four Scott Buckley tracks from their official CC-BY download URLs. The four Anders tracks must be obtained through the creator's official SoundCloud/download route and placed in `DefaultMusic` using the expected `Creator - Title` filename. SoulPlayer's normal supported extensions (`.mp3`, `.ogg`, `.wav`, `.flac`) are accepted.

Do **not** add a SoundCloud stream-ripper or third-party mirror to the release process. `-RequireComplete` can be used by release packaging to fail if any of the eight approved tracks are missing.

The final release folder should contain:

```text
BepInEx\plugins\SoulPlayer\DefaultMusic\
```

with the eight approved audio files plus this source notice.

## License separation

SoulPlayer's source code remains MIT licensed. Bundled music is **not** relicensed under MIT; each track remains under its creator's license above.

The documented CC BY-NC-SA Anders tracks are non-commercial material. If SoulPlayer is ever sold, paywalled, or otherwise used commercially, remove those tracks or obtain separate permission before release. Frostbite should remain release-gated until the exact redistribution terms for the creator-authorized copy are retained with the release records.
