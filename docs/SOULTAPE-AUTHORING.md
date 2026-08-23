# SoulTape curated-anchor authoring

SoulTape cassette locations are chosen deliberately by the mod author. The placement
tool lets an author fly through a raid, aim at a surface, adjust a cassette preview,
and save the solved location without copying coordinates by hand.

The tool is development-only. An ordinary SoulPlayer Release build does not contain
active placement-mode behavior.

## Build the placement-tools version

From the SoulPlayer source folder, run:

```powershell
dotnet build .\SoulPlayer.csproj `
  -c Release `
  -p:SptRoot=D:\SPT_4.1.2 `
  -p:SoulPlayerPlacementTools=true `
  -p:OutputPath=bin\PlacementTools\
```

The development DLL is written to:

```text
bin\PlacementTools\Soulplayer.dll
```

Install that DLL in the same place as the normal SoulPlayer DLL while authoring. Keep
the normal Release DLL for ordinary play and distribution. On startup, the development
build logs a prominent warning that Placement Mode is available.

## Record curated anchors

1. Start SPT/EFT and enter the raid/map you want to author.
2. Press **Ctrl+Shift+F8** to enable Placement Mode.
3. Fly the player to the desired area:
   - **W/A/S/D**: fly using the current first-person view;
   - **Space**: move up;
   - **Ctrl**: move down;
   - **Shift**: speed boost.
4. Aim at a floor, desk, shelf, crate, or other suitable upward-facing surface.
5. Inspect the SoulPlayer-owned cassette preview:
   - bright green means the exact current validation result is technically valid;
   - bright red means it is invalid;
   - the bottom-center status names the precise state that F9 will save or reject.
6. Adjust the preview if needed:
   - **mouse wheel**: rotate around the surface normal in five-degree steps;
   - **Shift+mouse wheel**: move the cassette farther away from the surface.
7. Press **F9** to save the currently valid solved anchor.
   A two-second confirmation names the map and new anchor number, or gives the exact
   rejection reason. The always-visible authoring summary updates its per-map saved
   anchor count immediately.
8. Repeat, then press **Ctrl+Shift+F8** to exit Placement Mode. The tool first returns
   the player to the position where Placement Mode was enabled, then restores normal
   movement and collisions.

The hotkeys, flight speed, boost multiplier, and optional semantic tag can be changed
under the `Development` section of SoulPlayer's BepInEx configuration while using a
placement-tools build.

While Placement Mode is active, existing enabled anchors for the canonical current map
are shown as cyan, collider-free cassette markers. These are SoulPlayer-owned authoring
visuals only. They make duplicates and coverage easier to see and are absent from a
normal Release build.

## Safety and movement

Placement Mode moves the actual local player root with SPT's public
`Player.Teleport(Vector3, bool)` API. It suppresses movement-animation displacement,
temporarily disables the player's colliders, and reapplies the authored flight position
every frame, so normal gravity and geometry do not constrain author movement. The
normal first-person camera and look input remain attached to the player.

While active, the tool sets the local `ActiveHealthController` damage coefficient to
zero and raises the safe-fall height. It does not heal the player or otherwise change
current health merely by entering Placement Mode. The original damage coefficient and
safe-fall height are restored exactly on exit.

The position at which Placement Mode was enabled is retained as the v1 safe position.
On a normal exit, the player is teleported back there before movement and collider state
are restored, avoiding re-enabling collisions while the author is inside geometry. If
the raid player has already disappeared, the tool restores every surviving state
reference it can without throwing. Bot hostility is not changed; altering AI targeting
was considered too invasive for authoring v1.

## Placement solving

The preview ray starts at `Player.CameraPosition` and follows its forward direction.
`Camera.main` is only a fallback if the player camera transform is temporarily missing.

The pure placement solver:

1. aligns the cassette's up axis with the hit surface normal;
2. applies cassette half-thickness plus a small clearance;
3. applies the author's optional extra offset and rotation;
4. checks the cassette-sized box against scene geometry;
5. nudges outward in small steps when clipped;
6. rejects the placement if clipping remains;
7. rejects near-vertical and downward-facing surfaces for authoring v1.

The saved transform is the solver result, not the raw raycast point.

## Output file

Anchors are appended to:

```text
BepInEx\config\SoulPlayer\authoring\spawn-anchors.json
```

The JSON contains the canonical EFT/SPT location identifier from
`GameWorld.LocationId`, position, Euler rotation, surface normal, optional semantic tag,
source, and enabled state. Numbers are written using invariant culture. Existing
anchors are preserved, the previous document is retained as `.bak`, and a corrupt
primary can recover from that backup.

Anchors within 0.15 metres of an existing anchor on the same canonical map are rejected
as accidental duplicates. The same coordinates on different maps are allowed.

## Placement policy

Curated anchors are authoritative. Future production raid selection will choose only
from valid curated anchors when any exist for a map, with one to three cassette
discoveries active per raid. Automatically derived loose-loot locations may later be
used as an explicit fallback or research source, but they will never override curated
placement.

Actual world-item spawning, pickup, and discovery progression are intentionally paused
and are not implemented by this authoring tool.
