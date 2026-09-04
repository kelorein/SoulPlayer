# SoulPlayer Codex Instructions

This repository is the development source of truth for SoulPlayer.

## Project

- Product: SoulPlayer, a local music player plus collectible SoulTape / SoulRecorder system for SPT.
- Current public release: v0.9.1.
- Supported SPT target: 4.1.3.
- Officially tested OS: Windows.
- Local development repo: `D:\SoulPlayer-recorder-test`.
- Reference SPT install: `D:\SPT_4.1.2`.

## Safety first

Before editing:

1. Inspect `git status`, current branch, remotes, and relevant docs/tests.
2. Do not reset, clean, discard, force-checkout, or overwrite user work.
3. Make the smallest safe change that fixes the root cause.
4. Add focused regression coverage for substantive changes.

Do not, unless explicitly requested by the user:

- deploy into the live SPT installation;
- launch EFT/SPT;
- edit SPT profiles or BepInEx config;
- modify authored SoulTape anchor coordinates/IDs/rotations/map IDs;
- commit, push, tag, publish, or bump the version;
- force-push, rewrite release history, delete branches/tags, or stage unrelated files.

## Authored SoulTape data

SoulPlayer currently has 108 curated anchors across 10 maps. Treat them as production data.

Do not change anchor IDs, positions, rotations, or map IDs unless the task is explicitly an anchor-authoring task.

Preserve collection, favorites, routing, profile, and persistence compatibility.

## Current core behavior

SoulPlayer includes:

- MP3 / FLAC / OGG / WAV playback;
- recursive local library scanning;
- in-game Library UI and mini-player;
- collectible SoulTapes and persistent collection progress;
- randomized per-raid cassette/song selection with undiscovered priority;
- Favorites First / Favorites Only / Discovered raid shuffle;
- SoulRecorder in-raid playback;
- configurable SoulRecorder Start/Stop and Next Cassette hotkeys;
- Main / Extract / Death playback routing;
- exact pre-raid Main-track and playback-position restoration;
- silent post-raid loading transition;
- volume presets and OLED-style volume HUD;
- movable mini-player and collision-aware overlays;
- ESC close behavior and media-key support;
- Linux/Wine/Proton-friendly path groundwork;
- CC0 world cassette visual.

## Raid playback contract

Preserve this lifecycle unless a task explicitly changes it:

`Main track at position X -> deployment captures exact track/position -> Main suspended -> SoulRecorder may be used -> raid ends -> silence during loading -> optional exact Extract/Death cue -> original Main track resumes at position X`.

SoulRecorder use during a raid must not destroy the saved Main snapshot.

Explicit normal playback decisions after return may supersede the saved resume.

## Performance is a release gate

A severe raid FPS regression previously came from unnecessary recurring work. Do not reintroduce:

- scene-wide searches every frame;
- reflection every frame;
- repeated ConfigurationManager lookups;
- repeated music-library copies/enumeration;
- per-frame layout rebuilds when state is unchanged;
- avoidable managed allocations in hot paths;
- unnecessary `Camera.main` resolution;
- allocating raycasts where a safe non-alloc path exists;
- post-raid readiness inspection during ordinary active raid gameplay;
- repeated unchanged-state logging.

Prefer cached references, dirty flags, immutable snapshots, lifecycle/config events, transition-based diagnostics, and non-alloc APIs where practical.

## Assets and licensing

- Do not redistribute Battlestate/proprietary game assets.
- Preserve third-party license/notice files.
- Do not package downloaded or user music automatically.
- Do not distribute `DefaultMusic` candidates unless their redistribution license/provenance is explicitly verified.
- Keep `THIRD-PARTY-NOTICES.txt` accurate when assets change.

## Validation

Primary validation entry point:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-SoulPlayer.ps1 -SptRoot "D:\SPT_4.1.2"
```

For substantive changes, run the relevant focused tests plus, as appropriate:

- full unit tests;
- Release build;
- PlacementTools build;
- package audit;
- asset-pipeline validation;
- `git diff --check`.

Known non-blocking baseline warnings: four existing UnityWebRequest deprecation warnings.

## Normal task workflow

For each implementation task:

1. Reproduce or inspect evidence first; do not guess.
2. Identify the root cause.
3. Implement the smallest safe fix.
4. Add regression tests.
5. Run appropriate validation.
6. Do not deploy, launch EFT, commit, push, tag, publish, or bump versions unless explicitly requested.
7. Report root cause, files changed, tests/build results, resulting DLL hash when built, and the exact EFT runtime test still needed.

## Release workflow

Do not bump versions merely because a task is complete.

For an explicit release request:

1. Build the exact final candidate.
2. Audit ZIP contents and licenses.
3. Record DLL/bundle/ZIP SHA-256 hashes.
4. Perform the requested EFT smoke test on that exact candidate.
5. After runtime acceptance, do not rebuild production bytes unless another runtime test will occur.
6. Only then commit, tag, push, and publish the exact tested package.

## Codex project setup

When this repo is opened as a Codex project, use `D:\SoulPlayer-recorder-test` as the only writable source folder by default.

Do not add `D:\SPT_4.1.2`, user music folders, Downloads, AppData, or backup folders as writable project sources. The SPT path is a reference/runtime-test location only and should be touched only with explicit user authorization.
