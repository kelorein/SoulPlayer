# SoulTape World Discovery v1

SoulTape is intended to grow into a collection of roughly 20–50+ discoverable songs.
Each cassette has a stable catalog ID and becomes permanent profile progression when
collected. Cassettes are collectibles, not ordinary stash inventory items.

## Raid loop

```text
raid starts
    -> choose 1–3 eligible cassettes and distinct curated anchors
    -> player finds and aims at a cassette within 2.5 metres
    -> F collects it
    -> collection JSON saves the unlock immediately
    -> cassette becomes available to SoulRecorder
```

Extraction is not required. Death later in the raid does not revoke an unlock, and no
raid-end, survival, or inventory-extraction hook participates in discovery persistence.

## Eligibility

A world cassette must have a stable ID, available audio, and an explicit curated
`SoulTapeRarity`. The starter cassette
`soul-tape.scott-buckley.the-long-dark` never spawns. Already unlocked IDs are excluded.
Generated/personal-library entries currently have no rarity and therefore do not spawn.

Rarity selection is weighted without making any rarity impossible:

| Rarity | Weight |
| --- | ---: |
| Common | 100 |
| Uncommon | 60 |
| Rare | 30 |
| Epic | 12 |
| Legendary | 4 |

Weights are centralized in `SoulTapeSpawnPlanner` for later rebalancing. The planner
uses a private deterministic `System.Random`, never `UnityEngine.Random`, and selects
without replacing either cassette IDs or anchors.

## Curated maps and packaged data

Curated anchors are authoritative. Factory Day currently has ten enabled anchors in:

```text
Data/SoulTape/SpawnAnchors/factory4_day.json
```

The file is embedded as a manifest resource in `Soulplayer.dll`. Runtime does not read
the placement author's `BepInEx/config/SoulPlayer/authoring/spawn-anchors.json` file.
Maps without committed anchor resources spawn zero SoulTapes. Factory Night does not
reuse Factory Day coordinates.

## Runtime timing

The world controller recognizes each new live `GameWorld`, waits for `MainPlayer`,
ensures the correct profile collection is bound, and waits until the asynchronous music
scan has been applied to the catalog. It then creates one deterministic plan and marks
that raid complete. Picking up a cassette never causes replenishment. Remaining
SoulPlayer pickup objects are destroyed when the raid ends or `GameWorld` changes.

## Pickup and temporary presentation

The default collection key is **F** and is configurable under `SoulTape discovery` in
the BepInEx configuration. The interaction uses a narrow first-person raycast with a
default 2.5-metre range and only responds to a `SoulTapeWorldPickup` collider.

The active rendered gameplay camera defines screen center through
`Camera.ViewportPointToRay(0.5, 0.5)`. SoulPlayer projects each of its one to three tracked
pickup focus points into that camera's viewport and acquires the closest visible pickup
within 2.5 degrees of center. The current target is retained to 3.5 degrees, preventing
small aim movement from flickering the white dot and prompt. The center ray is the cone
axis; it does not need to intersect the cassette or its trigger.

For the runtime acceptance build, SoulPlayer resolves `Camera.main` during each targeting
update and uses it when it is active, perspective, screen-rendering, and effectively
full-screen. It does not select cameras by `Camera.onPreCull` render order. If the main
camera is unusable, SoulPlayer scans the small active-camera list for a perspective,
full-screen, non-render-texture camera that renders world layers, preferring the lowest
depth so weapon/UI overlays do not displace the world camera. `Player.CameraPosition`
forward is the final defensive fallback.

The optional BepInEx setting `SoulTape discovery / Show targeting diagnostics` displays
the chosen camera source, name, instance ID, tag, enabled/active state, field of view,
depth, viewport and pixel rectangles, and render-texture status. It also shows the
nearest-to-center cassette's distance, viewport coordinates, angle, acquisition-cone
result, allowed hysteresis angle, LOS result, range/front/viewport checks, and final
target result. It is off by default and produces no per-frame log spam.

When LOS is blocked, the same panel identifies the tested top-face sample and the first
actual solid blocker: GameObject, collider type, layer number/name, hit distance, focus
distance, parent, and root. This keeps runtime diagnosis visible without per-frame logs.

The visible placeholder geometry has no colliders. The existing invisible trigger stays
on Unity's `Ignore Raycast` layer but is no longer required for acquisition. Targeting
uses a dedicated focus point on the visible top face: cassette center plus the cassette's
solved local-up direction by its 0.009-metre half-thickness and 0.015 metres of clearance.
Visibility tests top center and two top-face points 0.035 metres left and right. Any clear
sample makes the cassette visible; all three must be blocked to reject it. Raycasts skip
triggers, the cassette's own hierarchy, the local player's colliders, and camera-child
view colliders. Every other solid collider remains a blocker, so walls, crate sides,
shelf panels, and other world geometry still prevent collection through cover.

While that same current target result is active, SoulPlayer draws a small understated
white center dot and the `[F] Collect cassette` prompt. Looking away clears both;
looking back restores both immediately. Collecting or otherwise removing the cassette
also clears both. The native EFT action panel was not driven because its cursor is tied
to `GamePlayerOwner` interaction state and supported EFT interactable/inventory objects,
which would introduce fragile coupling for a progression-only collectible.

World cassettes now reuse the SoulPlayer-owned procedural cassette visual used by the
first-person recorder: a dark plastic shell, aged-paper label, two recognizable reel
hubs, and a muted amber accent. The visual remains static at the authored solved transform,
has no glow or beacon, and preserves the existing interaction focus/visibility contract.
It has no rigidbody, gravity settling, EFT template, stash item, or Battlestate asset.
The primitive prototype can later be replaced with original custom art without changing
discovery progression or targeting.

Final cassette art, a custom recorder hand rig, and duplicate rewards remain deferred.
