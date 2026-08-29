# SoulPlayer development validation

## One-command offline workflow

Run from the repository root:

```powershell
.\tools\Test-SoulPlayer.ps1
```

The script performs the normal developer validation loop without starting EFT:

1. verifies the required SPT/BepInEx contract assemblies are present;
2. restores the test project;
3. compiles and runs the complete offline test suite;
4. reports collection bootstrap, Collection Browser, delayed catalog/library refresh,
   persistence/recovery, recorder selection, recorder presentation, Asset Pipeline,
   SPT API contracts, placement authoring, and World Discovery;
5. performs a non-incremental Release build;
6. classifies whether the current changed files require an EFT acceptance run.

`-SptRoot` can override the SPT installation. The default development installation is
`D:\SPT_4.1.2`; a missing supplied/default root fails clearly without fallback guessing.

The offline harness never starts `EscapeFromTarkov.exe`.

Asset Pipeline validation parses the embedded provenance manifest, pins the approved
source hashes and Unity version, validates deterministic bundle paths and transform
and explicit texture/material/skinned-hands contracts, exercises missing/invalid/valid
three-prefab bundle backends, and verifies the procedural
fallback remains reachable. The harness reports a generated bundle's byte size when one
exists; absence is an explicit optional editor step and does not slow ordinary C# work.

External source archives can be verified separately without editors:

```powershell
.\tools\Prepare-SoulRecorderAssets.ps1 -AuditOnly
```

Blender derivation and Unity 2022.3.43f1 bundle generation are deliberate optional release
steps described in `SOULRECORDER-ASSET-PIPELINE.md`.

First-person asset changes also have an offline visual gate. Run
`tools/Preview-SoulRecorderAssets.ps1` with Unity 2022.3.43f1 to render the exact generated
hands/recorder/cassette prefabs, packaged AnimatorController, and runtime presentation
hierarchy at 50, 60, 70, and 75 degree FOV. The ordered 1920x1080 evidence, contact sheet,
transform chain, animation-curve audit, and camera-local bounds report are written beneath
`Artifacts/SoulRecorderPreview/`. Generated evidence and the generated preview scene stay
local and are not release inputs. A preview gate failure blocks another EFT visual test;
`-AllowQualityGateFailure` is only for retaining diagnostic before/after renders.

Placement-tools builds expose camera-local SoulRecorder presentation, recorder, hands, and
cassette start/alignment/inserted/eject transforms through the BepInEx config. Edit those
values and press `Ctrl+Shift+F7` in raid to reload and log one active snapshot; normal
Release builds do not contain this tuning surface.

## Profile and library simulation

The pure `SoulTapeCollectionHost` accepts an `IProfileIdProvider`. Production uses
`SptProfileIdProvider`, which prefers the client backend session and retains session and
local raid-player fallbacks. At the main menu, the existing `MenuScreen.Show` patch also
passes its authoritative `EFT.Profile.ProfileId` through the same host. No second
resolver or collection instance is created.

Offline tests use a mutable provider to model:

```text
no profile
    -> profile test-profile-001 appears
    -> collection bootstrap and save
    -> dispose/recreate and reload
    -> switch to independent profile test-profile-002
    -> switch back to profile test-profile-001
```

The same host subscribes to `MusicLibrary.Changed`, so tests can initialize with zero
available audio, complete a real background library scan later, and verify that cassette
audio reconnects without an EFT or plugin restart.

## When EFT runtime acceptance is required

An EFT launch is required when a change crosses or modifies a runtime integration
boundary, including:

- BepInEx/plugin startup or component wiring;
- player-facing Unity menu layout, navigation, or interaction changes;
- Tarkov session or profile integration;
- raid lifecycle hooks;
- world cassette/item spawning;
- one-shot placement camera raycasts, collision checks, anchor writes, or Unity ghost previews;
- pickup or interaction behavior;
- Unity audio loading/playback behavior;
- first-person hands, models, prefabs, or animations;
- SPT inventory or item-template integration.

These changes should first pass `Test-SoulPlayer.ps1`, then receive one focused EFT
acceptance run for the affected runtime behavior. Repetitive launches during ordinary
implementation are not required.

## When offline validation is normally sufficient

An EFT launch is normally unnecessary for changes limited to:

- collection algorithms;
- JSON persistence, migration, and recovery;
- catalog metadata and curated fingerprints;
- favorite and unlock rules;
- deterministic spawn-selection algorithms that do not create world objects;
- serialization;
- pure UI/view-model logic where Unity runtime integration is unchanged.

Milestone acceptance may still request a final runtime pass, but the default development
loop for these changes is the offline harness.

## Current workflow-change classification

SoulRecorder's native-hands prototype changes first-person player-body/skeleton integration.
Offline validation covers exact SPT API contracts, semantic bone-role resolution, capture-once
pose state, reverse/idempotent restoration, preferred-view BAMEN suppression, model/socket
ownership, package asset allowlisting, and both Release variants. A focused EFT test is still
required because native renderer visibility, FinalIK ordering, the installed glove/sleeve
appearance, and post-interaction skeleton restoration are runtime-specific. The harness never
starts EFT automatically.
