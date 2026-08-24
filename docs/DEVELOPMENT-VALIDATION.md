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
   persistence/recovery, recorder selection, recorder presentation, SPT API contracts,
   placement authoring, and World Discovery;
5. performs a non-incremental Release build;
6. classifies whether the current changed files require an EFT acceptance run.

`-SptRoot` can override the SPT installation. The default development installation is
`D:\SPT_4.1.2`; a missing supplied/default root fails clearly without fallback guessing.

The offline harness never starts `EscapeFromTarkov.exe`.

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
- placement-mode player movement, raycasts, collision checks, or Unity ghost previews;
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

SoulRecorder procedural first-person presentation v1 crosses the Unity first-person
model/animation boundary. Offline validation covers its pure timeline, timing contract,
headless fallback, selection invariants, cassette dimensions, world focus invariant, and
both Release variants. One focused EFT acceptance run is still required for view placement,
camera transitions, insertion/ejection motion, reel visibility, and raid cleanup. The
harness reports `first-person SoulRecorder/cassette presentation changed` and never starts
EFT automatically.
