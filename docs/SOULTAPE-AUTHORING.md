# SoulTape curated-anchor authoring

SoulTape cassette locations are chosen deliberately by the mod author. The
development-only placement tool lets an author walk through a raid normally, look at
a suitable surface, and save a solved cassette anchor without copying coordinates.

An ordinary SoulPlayer Release build does not include the active authoring behavior.

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

Install that DLL in the same location as the normal SoulPlayer DLL while authoring.
Keep the normal Release DLL for ordinary play and distribution. The development build
logs a prominent startup warning that F9 anchor authoring and the optional
Ctrl+Shift+F8 Hybrid Noclip are available.

## Record curated anchors

1. Start SPT/EFT and enter the raid/map you want to author.
2. Walk to the desired area using normal Tarkov movement. Optionally press
   **Ctrl+Shift+F8** to enable hybrid player/camera authoring noclip.
3. Put the center of the gameplay view over a floor, desk, shelf, crate, or other
   suitable upward-facing surface.
4. Press **F9**.
5. SoulPlayer immediately:
   - casts from the vertically offset authoring camera while noclip is on, or
     the active full-screen gameplay camera while noclip is off;
   - aligns the cassette to the hit surface;
   - validates cassette collision, duplicate distance, map ID, and authoring data;
   - nudges the cassette outward when needed;
   - saves a valid anchor;
   - shows the exact solved green or rejected red ghost for about two seconds;
   - displays the map, per-map anchor count, and exact save/rejection result.
6. Walk normally or move the hybrid authoring view to the next location and repeat.

F9 works in both modes and never requires noclip. Ctrl+Shift+F8 toggles Hybrid
Noclip and can be used at any time during a live raid. SoulPlayer displays
and logs `SoulPlayer Noclip: ON` or `SoulPlayer Noclip: OFF` after each successful
toggle.

Hybrid Noclip controls are:

- **W/A/S/D**: move the real EFT player horizontally relative to camera heading.
- **Space**: raise only the authoring camera.
- **Ctrl**: lower only the authoring camera.
- **Shift**: speed boost.
- **Mouse**: normal EFT camera look.
- **F9**: solve and save from the current authoring view.

The optional semantic tag can be changed under the `Development` section of the
SoulPlayer BepInEx configuration. Two optional numeric settings apply to the next F9
capture:

- `Cassette anchor rotation degrees`: rotation around the solved surface normal.
- `Cassette anchor surface offset`: additional outward offset, from 0 to 0.2 metres.

F9 works with both settings left at their default zero values.

## Player safety and movement

With noclip off, the author remains an ordinary EFT player and uses normal Tarkov
movement. With Hybrid Noclip on, W/A/S/D move the real EFT player only across X/Z so
map streaming and portal state follow the author horizontally. The player's current Y
is preserved by every teleport. Space/Ctrl instead adjust a SoulPlayer-owned authoring
camera from -25 to +25 metres relative to the live gameplay camera.

Horizontal input builds a stable target every rendered frame; the player is
repositioned only from Unity's fixed physics update, normally about 50 Hz. Each
horizontal reposition is capped at 0.75 metres. Larger requested movement is consumed
over subsequent fixed updates rather than applied as one large teleport.

SoulPlayer temporarily suppresses EFT movement deltas and disables only enabled,
non-trigger colliders found on the player hierarchy when noclip starts. It scans those
colliders once, preserves each original enabled value, and restores them exactly on
exit. Trigger colliders are left untouched. On a normal author toggle-off or plugin
shutdown, the player is first returned to the known-safe entry position before
collision is restored.

While noclip is enabled, SoulPlayer captures the exact original movement-delta,
damage-coefficient, and fall-safe-height values. It sets damage coefficient to zero
and fall-safe height to a very large value, without healing or otherwise changing
health. This protects the author from fall and environmental damage. The exact
captured values are restored when noclip is disabled.

Noclip is also forced off on player death/unspawn/extract, raid or scene transition,
application exit, component destruction, and plugin shutdown. Cleanup restores the
captured movement, collision, and protection state without touching saved anchors.

## Camera and placement solving

With noclip off, the authoring ray comes from the verified gameplay camera. With
Hybrid Noclip on, SoulPlayer copies that camera's rendering settings, horizontal
position, and rotation into a development-only authoring camera, then adds the
clamped vertical offset. F9 synchronizes it immediately before creating:

```csharp
authoringCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
```

The tool prefers an active, enabled, full-screen `Camera.main`. If that camera is not
usable, it can select another verified full-screen world camera. Render-texture,
orthographic, partial-viewport, optic, weapon-only, UI, disabled, and inactive cameras
are rejected. The tool does not fall back to a potentially misaligned player transform.

The SoulPlayer camera is disabled and destroyed on every noclip cleanup path. The EFT
gameplay camera is never modified, so turning noclip off returns rendering and F9 to
the normal camera exactly. The solver, collision validation, duplicate detection, and
save path remain identical in both modes.

F9 uses a multi-hit physics query rather than accepting the first collider. Candidate
hits are filtered before the slope solver runs. SoulPlayer ignores:

- the local player and player-body hierarchy;
- the active hands controller, held-item/weapon root, and controller object;
- objects parented to the first-person gameplay camera;
- SoulPlayer authoring preview/ghost objects;
- trigger colliders;
- hits less than 0.5 metres from the gameplay camera.

The nearest remaining world hit supplies the placement point and surface normal. If
no valid world hit remains, F9 hides the ghost, reports that no valid world surface is
targeted, and does not save an anchor. The existing steep-surface and collision checks
then run unchanged against the filtered world hit.

The pure placement solver:

1. aligns the cassette up axis with the hit surface normal;
2. applies cassette half-thickness plus a small clearance;
3. applies the optional configured offset and rotation;
4. checks the cassette-sized box against scene geometry;
5. nudges outward in small steps when clipped;
6. rejects the placement if clipping remains;
7. rejects near-vertical and downward-facing surfaces;
8. rejects an anchor within 0.15 metres of an existing anchor on the same canonical
   map.

The saved transform is the solver result, not the raw raycast point.

## Output file

Anchors are appended to:

```text
BepInEx\config\SoulPlayer\authoring\spawn-anchors.json
```

The JSON contains the canonical EFT/SPT location identifier from
`GameWorld.LocationId`, position, Euler rotation, surface normal, optional semantic
tag, source, and enabled state. Numbers use invariant culture. Existing anchors are
preserved, the previous document remains available as `.bak`, and a missing or
corrupt primary can recover from that backup.

The same coordinates on different maps remain valid because duplicate detection is
scoped to the canonical map ID.

## Placement policy

Curated anchors are authoritative. Production raid selection chooses from valid
curated anchors when they exist for a map, with only one to three cassette discoveries
active per raid. Automatically derived loose-loot locations may be used later as an
explicit fallback or research source, but they do not override curated placement.

The authoring tool does not add new world-spawn, pickup, collection, or progression
behavior.
