# SoulPlayer v0.9.1

A UX, performance, and stability patch for SoulPlayer. Existing v0.9.0 cassette, collection, routing, and volume features are preserved.

## What's improved

- **Configurable SoulRecorder Start/Stop hotkey:** choose the in-raid key that works for you (default **M**).
- **Configurable mini-player corner:** choose its screen position, with collision-aware stacking to keep the volume HUD and other overlays from overlapping.
- **Reliable post-raid playback:** fixed the silence/loading lifecycle so Main music does not start during loading. A configured Extract or Death cue plays once, followed by the exact pre-raid Main track at its saved position.
- **Main resume survives recorder use:** starting/stopping SoulRecorder or advancing cassettes no longer cancels the saved Main resume snapshot.
- **Major raid-FPS and per-frame optimization work:** caching and event-driven updates reduce unnecessary reflection, readiness polling, layout calculations, library copies/searches, camera lookups, and shortcut/F12 checks.
- **Faster world-cassette targeting:** cached cameras, throttled proximity checks, and reusable raycast buffers reduce recurring work while preserving occlusion checks.
- **Stability improvements:** better lifecycle transitions, readiness handling, and library rescans/remapping.

## Validation

The user completed the final EFT smoke test on this exact v0.9.1 candidate and reported that everything works fine. This covers raid performance, recorder controls, mini-player/volume-HUD placement, post-raid loading silence, Extract/Death routing, and exact Main track/position resume. Performance acceptance is qualitative; no precise FPS benchmark is claimed.

The prior release-candidate audit passed 505/505 tests, Release and PlacementTools builds, package and asset checks, and the semantic comparison against the accepted optimized baseline. Final publication checks revalidated the unchanged candidate and all six ZIP payloads without rebuilding or repackaging.

## Install or update

Close EFT and SPT, then extract `SoulPlayer-v0.9.1.zip` into the SPT installation folder. Existing configuration, collections, and routing data are preserved.

The ZIP contains exactly:

- `BepInEx/plugins/SoulPlayer/Soulplayer.dll`
- `BepInEx/plugins/SoulPlayer/NAudio.Core.dll`
- `BepInEx/plugins/SoulPlayer/NAudio.Flac.dll`
- `BepInEx/plugins/SoulPlayer/soultape_world.bundle`
- `README.txt`
- `THIRD-PARTY-NOTICES.txt`

It excludes PerformanceBefore, PerformanceDiagnostics, and PlacementTools DLLs; debug symbols; logs; temporary DLLs; preview images; obsolete recorder/hand bundles; Unity/Blender authoring assets and caches; local review ZIPs, patches, and reports; comparison-tool sources; and all music. The 108 authored anchors across ten maps and the CC0 world cassette bundle are unchanged. The developer recorder probe is absent from the Release DLL, and there are no performance-profiler call sites.

## Compatibility and limitations

- Targets SPT **4.1.3**; **Windows** is the officially tested OS.
- Linux/Wine/Proton path compatibility exists, but runtime verification remains limited.
- **Fika is unverified.**
- Factory Night does not reuse Factory Day anchors; maps without curated anchors spawn no SoulTapes.
- Exact Main resume requires the saved track to remain available and eligible; deliberate post-return playback choices can replace it.
- Four existing `UnityWebRequest` deprecation warnings remain known and non-blocking.

## SHA-256

```text
Soulplayer.dll
77822AFA614939113A71ABDE08237F748C373BAC4B3CE25CECF5D0A0E399A965

soultape_world.bundle
9F10C49D8FEC5C56A6E290E91CA50C2DA971F742CAB38DDFA97E8E60D7B31859

SoulPlayer-v0.9.1.zip
6A7BBB97E2F8CEC0F4C95156A068B8676AE7081E641230CEE6C6E42408C439CD
```
