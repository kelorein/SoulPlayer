# SoulRecorder real-asset pipeline v1

## Current status

The active SoulRecorder runtime no longer loads this AssetBundle. Unity uses the recorder and
cassette prefabs only to render deterministic transparent PNGs for the 2D overlay; those PNGs
are embedded in `Soulplayer.dll`. Release packaging contains no sidecar bundle. The builder,
self-tests, source provenance, and prior 3D previews remain available as archived authoring
infrastructure and continue to validate the legally sourced model derivatives.

The reproducible source, build, packaging, runtime-loading, offline first-person preview,
validation, and fallback paths are implemented. Blender 5.2 produced and validated the real recorder, cassette, and BAMEN
articulated-hands FBX files plus recorder/hands textures from approved immutable CC0 and
CC BY 4.0 archives. Unity `2022.3.43f1` now produces a strict
Windows 64-bit Player bundle that passes same-editor load and prefab-contract validation.
The first two-prefab packaged bundle loaded successfully in EFT; this revised textured,
three-prefab presentation remains uncommitted. Runtime test #2 rejected the earlier DevMops
three-bone arms; that prefab has no runtime activation path. The replacement is a distinct
BAMEN full-rig/Animator prefab, while recorder/cassette-only fallback remains available.
The procedural and headless fallbacks remain available. A later EFT visual test accepted
the repaired materials but rejected rendering BAMEN's complete shoulder-to-hand geometry.
The current Blender pipeline therefore derives a capped FPS-only mesh and re-stages the
hands from opposite lower corners. It must pass the exact packaged Unity contact sheets and
the new sleeve-cutoff camera gates before another EFT visual acceptance run is approved.

## Compatibility decision

`D:\SPT\_4.1.2\UnityPlayer.dll` reports Unity `2022.3.43f1` revision
`85497d293fa1`. SPT also supplies `UnityEngine.AssetBundleModule.dll`, and offline contract
tests verify `AssetBundle.LoadFromFile(string)` and `AssetBundle.Unload(bool)`.

The chosen format is therefore a Unity `2022.3.43f1` Standalone Windows 64-bit AssetBundle,
built with deterministic LZ4 chunk compression. Unity 6 from the FPS Foundation reference
project is not used to build SoulPlayer bundles. `.blend` and `.fbx` files are never loaded
by the EFT runtime.

The bundle is a sidecar rather than an embedded DLL resource:

```text
BepInEx/plugins/SoulPlayer/
    Soulplayer.dll
    soulplayer_assets.bundle
```

`LoadFromFile` can memory-map/read the sidecar directly and avoids copying an embedded
resource to a temporary file at startup. A sidecar is also independently replaceable and
easy to size/hash. Embedding would make installation one file, but Unity still needs byte
or temporary-file loading, increases managed assembly size, and makes asset-only updates
less convenient.

## Approved sources and provenance

The immutable masters remain external under `D:\Resources\SoulPlayer`; they are not copied
into Git or the review bundle. The committed machine-readable record is
`Assets/SoulRecorder/source-manifest.json`.

### Recorder

- Poly Haven Cassette Player by Oday Abuzaeed, CC0.
- Archive: `cassette_player_4k.blend.zip`, 48,577,762 bytes.
- SHA-256:
  `C6B84808C8AFF29E1798022BCF0C50AFD02E5838E394BC19BC381CF6B97467A2`.
- Compressed Blender source identifies itself as Blender 2.93 after decompression.
- Binary ID audit found two model objects: `cassette_player_body` and
  `cassette_player_tape`.
- Three source materials were observed: body, flap, and tape.
- Nine 4096x4096 PNG maps are present: body/tape diffuse, normal, metallic,
  roughness, and body opacity.

The body becomes the recorder derivative; the included source cassette is removed in favor
of the separately licensed reusable SoulTape cassette. The Blender audit reports one final
mesh, 2,068 vertices, 1,971 polygons, 3,794 triangles, and two materials. Its normalized
bounds are approximately `0.109333 x 0.041281 x 0.200000 m`, with unit object scale.

Unity imports those FBX dimensions at one hundredth scale and preserves the Blender-style
X-width/Y-depth/Z-height axes. The prefab builder therefore applies a uniform `100` model-
child scale and a `-90` degree local-X rotation. The packaged contract is X width, Y height,
Z depth, with local `-Z` as the player-facing recorder front. Its self-tested final bounds
are `0.109333 x 0.200000 x 0.041281 m`; the model is recentered around the prefab root.

The 4K maps remain external master data. The derived prototype preserves five deterministic
2048x2048 source-map derivatives: body base color, OpenGL normal, metallic, roughness, and
the flap opacity mask. It also creates two Unity-ready maps: flap base color with opacity in
alpha, and metallic RGB with inverted roughness in Standard's smoothness alpha. The removed
source tape's four maps are omitted. The base-color derivation
also removes the source `Dixons TR12 Cassette Recorder` wordmark while preserving functional
control labels and the source's surface detail.

The earlier white/light-grey runtime body was a pipeline fault, not the intended finish:
the FBX referenced texture paths inside a deleted temporary extraction directory. The
Blender remapper now uses semantic image matching and Unity assigns explicit body/flap
materials. Dependency and same-invocation bundle checks prove that base color, flap alpha,
normal, and metallic-smoothness maps are resolvable.

### Cassette

- BlendSwap Cassette Tape by comeinandburn, CC0.
- Archive: `Cassette Tape.zip`, 610,705 bytes.
- SHA-256:
  `4E1CA41FFE39921B7B10B4CB0CD8BCC43FECB79D97D2DEACC5A6439216BA6246`.
- Source is Blender 2.69 and contains a 2,164,920-byte blend.
- Binary ID audit found alternate cassette assemblies. Blender confirmed that the coherent
  assembly is the `.001` group in `Collection 2`.
- Six materials were observed: clear plastic, plastic case, screws, tape, sponge, and
  white plastic.
- The retained source objects are `CassetteCase_Hero.001`, `Axle.001`, `tape.001`, and
  `WheelCogs.001`. Four isolated presentation vertices are removed from the case.
- The unsuffixed broken-transform alternate assembly, clear branding plane, high-detail
  screw meshes, duplicate center screws, and tape-head helper cubes are removed.
- `WheelCogs.001` contains twelve disconnected cog components. Six negative-X components
  become `ReelLeft`; six positive-X components become `ReelRight`. Each origin is moved to
  its physical hub bounds center before normalization.
- Source X maps to width, source Y to height, and source Z to depth. Final bounds are
  `0.110000 x 0.070000 x 0.018200 m` (110 x 70 x 18.2 mm), and every mesh has local scale
  `1,1,1`.
- The derivative has seven meshes, 7,336 vertices, 6,884 polygons, 13,796 triangles, and
  four materials. `Label` is a unique parent transform containing two slim aged-label
  panels that leave both reel hubs visible.
- Legacy paths and the Maxell label reference are eliminated. The cassette uses generated
  dark-shell, aged-label, dark-tape, and restrained amber-detail materials.

Unity imports the cassette at one hundredth scale with X width, Y face height, and Z
thickness. The prefab builder applies a uniform scale of approximately `100` and `-90`
degrees around local X. The packaged runtime contract becomes X width, Y thickness, Z face
height with self-tested bounds `0.110000 x 0.018200 x 0.070000 m`.

The inspection JSON records before/cleanup/final bounds, mesh/vertex/polygon/triangle and
material counts, object names, every final local scale and dimension, required-transform
counts, reel pivot offsets, texture outputs and dimensions, FBX size, and validation issues.
Cassette dimensions outside the configured 110x70x18 mm tolerance fail preparation.

### Hands

- FREE [FPS Arms] GameReady - RIGGED by BAMEN (`bamenwo05`) from Sketchfab.
- Source model: `296d30fc705b4dff85c2c8a2d2724e7f`.
- License: Creative Commons Attribution 4.0 International (CC BY 4.0).
- Archive SHA-256:
  `8585C328BF9E0872DBE28E08D7B72BF675411CCF8C49F26F804CF97096592BBE`.
- Source FBX SHA-256:
  `4E1FC02D9EBB61C6C5CC59BCD02B63DA09F95C8DB3C0F7457724C75BCA89B14A`.
- Blender audits one `FPS Arms` mesh: 6,942 vertices, 6,864 polygons, and 13,728
  triangles, with `FPS Arm` and `FPS Hand` material slots.
- Both arms share `FPS Arms Root`. The complete upper-arm, forearm, wrist/hand, thumb, and
  four articulated finger chains remain in the runtime skeleton; the derivative renames
  only the stable root to `SoulRecorderHandsRoot` and does not collapse or bake away that
  deformation rig.
- The runtime renderer does **not** use the full shoulder-to-hand source mesh. Blender makes
  a non-destructive `SoulRecorderArms` FPS derivative by removing the proximal 48 percent
  of each upper-arm sleeve in rest-bone space. This removes 406 of 6,942 source vertices
  (203 per side) and 408 source polygons. It then adds 52 endpoint vertices that extend the
  two sleeve terminations below the camera, producing 6,588 vertices, 6,510 polygons, and
  13,064 triangles. Retained forearm, wrist, hand, and finger vertices keep their original
  BAMEN vertex groups and weights. Only the new endpoint vertices are weighted to
  `SoulRecorderHandsRoot`, so their cutoff remains stable below the camera while the arms
  articulate. The approved archive/FBX is never modified.
- `SupportSleeveCutoff` and `CassetteSleeveCutoff` are non-deforming audit markers parented
  to `SoulRecorderHandsRoot` at the actual derived sleeve terminations. They are not runtime
  presentation controls. The Unity preview rejects any pose where a termination or its
  conservative visibility envelope enters the frame.
- `RecorderGrip` is parented to `Hand_2.L`, `CassetteGrip` to the visible-palm
  `Hand_1.R`, and `CassetteContact` to the visibly contacting index distal joint
  `Finger_2_2.R`. These are non-deforming sockets.
- Six supplied 1024px base-color/normal/roughness maps are copied deterministically. Unity
  Standard arm and hand materials also receive generated metallic/smoothness maps, with
  metallic zero and smoothness derived by inverting supplied roughness.
- Blender authors a baked 60fps master action split by Unity into Enter, Insert, StartExit,
  Hold, StopEnter, Eject, StopExit, and CancelInsert clips. Every clip has non-zero duration
  and real arm/wrist/finger curves.
- No EFT hand, rig, texture, animation, weapon-view, or Battlestate asset is used.
- Blender exports the meter conversion with `FBX_SCALE_ALL`. Unity bakes the FBX axis
  conversion and keeps the animated-hands prefab/model at unit scale. This prevents the
  Blender-to-FBX centimeter conversion from appearing as `100,100,100` armature and mesh
  transforms inherited by `RecorderGrip` and `CassetteGrip`.
- The Animator is attached to `SoulRecorderAnimatedHandsModel`, not the outer prefab root.
  Imported clip paths start at `SoulRecorderHandsRig/...`; placing the Animator one level
  higher leaves every curve unbound and silently displays the bind pose.

## Repository layout

```text
Assets/SoulRecorder/
    source-manifest.json
    Blender/prepare_soulplayer_assets.py
    processed/                 # generated FBX, 2K maps, inspection reports
    bundle/                    # generated soulplayer_assets.bundle
    UnityProject/              # minimal exact-version bundle project
tools/
    Prepare-SoulRecorderAssets.ps1
    Build-SoulRecorderAssets.ps1
    Preview-SoulRecorderAssets.ps1
```

Unity `Library`, `Temp`, logs, generated prefab imports, external archives, reference
projects, and editor caches are excluded. The Unity project contains only the deterministic
builder and exact-version configuration.

## Regeneration

Source verification does not require Blender:

```powershell
.\tools\Prepare-SoulRecorderAssets.ps1 -AuditOnly
```

To reproduce the current FBX, texture, and inspection outputs with the installed Blender:

```powershell
.\tools\Prepare-SoulRecorderAssets.ps1 `
    -BlenderPath 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
```

The script verifies all three source hashes, expands immutable archives only into a unique
temporary directory, selects one coherent cassette assembly, removes duplicates/helpers and
branding, applies SoulPlayer identity, normalizes scale and pivots, separates/names reels,
derives the recorder source/runtime maps at 2K, preserves and animates the BAMEN articulated
rig, exports three FBX files, writes and validates the inspection reports, and safely removes its
temporary directory.

After reviewing the inspection reports and derived models, build with the exact editor:

```powershell
.\tools\Build-SoulRecorderAssets.ps1 `
    -UnityEditorPath 'C:\Program Files\Unity\Hub\Editor\2022.3.43f1\Editor\Unity.exe'
```

The Unity builder creates prefabs, removes colliders, validates cassette reel transforms,
adds deterministic recorder markers, and builds for the explicit `StandaloneWindows64`
Player subtarget. The project explicitly enables Unity's built-in
`com.unity.modules.assetbundle` and `com.unity.modules.animation` packages and disables engine-code stripping; omitting the
built-in module produced an invalid file while Unity logged that the AssetBundle module was
disabled. It refuses any editor version other than `2022.3.43f1`.

The normal bundle options are deterministic LZ4 chunk compression,
`ForceRebuildAssetBundle`, and `StrictMode`. `AssetBundleStripUnityVersion` is intentionally
not used. The wrapper launches batch mode with Direct3D 11 (without `-nographics`), removes
stale output before launch, checks the Unity process exit code, and rejects the output if
the deterministic log contains the disabled-module diagnostic or a fatal AssetBundle error.

During the same Unity invocation, the builder loads the newly written bundle from disk,
loads all three logical prefabs, verifies every transform/material/texture/skinned-rig
contract and the `StatusLed` renderer, then unloads those temporary validation assets. A
file is not accepted unless all recorder, cassette, and hands self-test groups are present
in `unity-build.log`, including visibility bounds.

## Offline first-person preview

Render the exact generated hands, recorder, and cassette prefabs with their packaged
AnimatorController and clips:

```powershell
.\tools\Preview-SoulRecorderAssets.ps1 `
    -UnityEditorPath 'C:\Program Files\Unity\Hub\Editor\2022.3.43f1\Editor\Unity.exe'
```

The tool creates a development-only scene and `Artifacts/SoulRecorderPreview/` output. It
renders ordered 1920x1080 lifecycle checkpoints at 50, 60, 70, and 75 degree vertical FOV,
dense sequential start/stop evidence at FOV 70, and contact sheets for FOV 60, 70, and 75.
`preview-report.txt` contains camera-local hands/recorder/cassette bounds and visible rendered
pixel coverage; `transform-chain.txt` records every important scale/position/rotation; and
`animation-curves.txt` records imported arm/root curve ranges. `arm-cutoff-report.txt` records
the full animation audit, while `arm-silhouette-report.txt` and
`arm-silhouette-diagnostic-fov70.png` provide annotated hand, wrist, elbow, and sleeve-end
evidence. The wrapper waits for Unity to exit, checks its exit code, requires all acceptance
artifacts, and points failures to `unity-preview.log`.

Editor screenshots sample the exact clips on `HandsAnimator.gameObject`. This detail is
intentional: FBX bindings begin at `SoulRecorderHandsRig`, immediately below the imported
model root. Unity Editor animation mode is scoped with `StartAnimationMode`,
`BeginSampling`/`EndSampling`, and `StopAnimationMode` so intermediate frames are
deterministic in batch mode.

Automated gates reject unsafe/non-uniform scale chains, camera intersection, hands behind
the camera, excessive viewport coverage, implausible recorder dimensions, meter-scale
animation translation excursion, cassette/slot contact beyond 10 mm, incorrect slot
orientation, a hand-contact marker beyond 15 mm, or a contact-to-seated ownership change
beyond 10 mm. At FOV 60, 70, and 75 a 219-frame-per-FOV audit requires both sleeve
terminations, including a conservative 45 mm world-space envelope, to remain outside the
viewport by at least 0.03 viewport units. Sampled presentation poses also require the support
arm to originate on the lower-left side, the cassette arm on the lower-right side, and both
visible forearms to retain a clear diagonal instead of becoming vertical limb columns.
`cassette-contact-report.txt` records the crop markers alongside all sockets, hand bones,
visible bounds, orientation, and preserve-world transfer deltas. `-AllowQualityGateFailure`
exists only to preserve before-correction diagnostic renders; acceptance runs omit it.

The giant-arm runtime failure was reproduced offline before correction. Its complete scale
chain was `presentation 0.78 -> hands 1 -> model 0.58 -> armature 100`, producing a
`45.24` lossy scale on hand bones/sockets. Camera-local combined bounds were approximately
`6.658 x 9.536 x 6.117 m`, centered behind/intersecting the camera. After source/import
normalization, the corresponding chain is `presentation 0.78 -> hands 1 -> model 1 ->
armature 1 -> bones 1`; props attached to the hand sockets no longer inherit a 100x scale.

Ordinary C# builds do not invoke Blender or Unity. If a reviewed bundle exists in
`Assets/SoulRecorder/bundle`, MSBuild copies it beside the DLL. Release packaging requires
the reviewed bundle and never packages the external masters.

## Prefab contract

Asset names are addressable without hierarchy indices:

```text
soulrecorder_fp
    SoulRecorderModel
    CassetteInsertionStart
    CassetteAlignment
    CassetteSlot
    CassetteEject
    CassetteWindow
    StatusLed
    ReelWindowLeft
    ReelWindowRight

soultape_cassette
    SoulTapeCassette
    Shell
    Label
    ReelLeft
    ReelRight

soulrecorder_animated_hands
    SoulRecorderAnimatedHandsModel
        Animator (eight SoulRecorder clips)
        SoulRecorderHandsRig
            SoulRecorderHandsRoot
                Arm_1.L -> Arm_2.L -> Hand_1.L / Hand_2.L -> fingers
                Arm_1.R -> Arm_2.R -> Hand_1.R / Hand_2.R -> fingers
                RecorderGrip (under Hand_2.L)
                CassetteGrip (under visible-palm Hand_1.R)
                CassetteContact (under index distal contact joint Finger_2_2.R)
                SupportSleeveCutoff (under SoulRecorderHandsRoot, authoring audit only)
                CassetteSleeveCutoff (under SoulRecorderHandsRoot, authoring audit only)
        SoulRecorderArms (FPS-cropped skinned derivative)
```

The insertion-start, alignment, slot, and eject transforms must share a parent. Colliders are prohibited and
are removed both during prefab generation and defensively at runtime. The cassette is
normalized to approximately `0.11 x 0.018 x 0.07 m` with an insertion-centered pivot.
`StatusLed` must have a usable `Renderer`; a packaged recorder without one is rejected and
the procedural presentation is used instead. `SoulRecorderModel` must also contain active,
enabled, non-zero renderer geometry with a non-null shader. SoulPlayer-owned packaged
hierarchies are normalized to Default layer 0, matching the procedural presentation.
The cassette model must independently provide enabled, active, non-zero renderer geometry
with a non-null shader; otherwise the full packaged presentation is rejected before use.
The hands prefab must independently provide a textured active skinned renderer, the full
bilateral arm/hand/finger contract, the three sockets, and all eight Animator clips. Runtime
additionally requires `SoulRecorderHandsRoot` to be the renderer's common `rootBone`, at
least 40 deformation bones with an equal bind-pose count, the exact socket parents, both PBR
materials, non-zero clip lengths, and finite first-person bounds. Invalid hand instances are
destroyed without rejecting the recorder/cassette prefabs.

### Contact-rig authoring contract

The recorder prefab carries explicit `RecorderSupportPalm`, thumb/index/middle/ring/pinky
contacts plus `CassetteSlotEntry`, `CassetteSlotSeated`, and
`CassetteSlotTravelAxis`. The cassette carries `CassetteThumbGrip`,
`CassetteIndexGrip`, `CassetteMiddleGrip`, `CassetteFront`, `CassetteTop`, and
`CassetteInsertionAxis`. These empty transforms are SoulPlayer-owned metadata; they do not
contain or redistribute EFT assets.

The first automatically marker-solved still passed its numerical checks but failed human
ergonomic review. Those existing transforms are now legacy references, not pose authority.
They may be displayed for comparison, but the approved hand pose will define replacement
contacts later. No insertion/ejection work should consume the current markers.

Use `SoulPlayer > Prepare SoulRecorder Manual Grip Scene`, then the development-only
`SoulPlayer > SoulRecorder Grip Pose Authoring` window. It disables the scene Animator and
never invokes IK, marker fitting, or automatic finger curls after editing begins. The live
FOV-70 mirror exposes each arm/wrist and required finger joint plus recorder/cassette local
transforms. Sliders and 1/5 mm or 0.5/1 degree buttons support coarse and fine edits.
`Reset Pose`, separate `Save SupportHold` / `Save CassetteCarry`, and `Load Saved Pose`
operate on `Assets/SoulRecorderAuthoring/SoulRecorderGripPoses.json`. Legacy marker gizmos
are optional and off by default. This editor code is not part of `SoulPlayer.dll` or the
AssetBundle. Insertion/ejection poses remain intentionally unauthored until both static
poses pass human review. The ordinary batch preview writes
`manual-grip-authoring-status.txt` with `RESULT=PENDING`; it no longer runs or accepts the
legacy marker solver as the final ergonomic gate.

The authoring window enumerates the loaded `SoulRecorderAnimatedHandsModel` hierarchy and
resolves the upper arm, forearm, wrist, palm, and every finger joint for each side. Its
dropdowns contain only transforms that exist in that loaded scene. A binding report is
shown at the top and written beside the manual scene as `rig-binding-report.txt`; manual
authoring is not marked ready if any required joint or held object is unresolved.

Left and right upper arm, forearm/elbow, and wrist bones have separate manual skeleton
controls. They edit the imported skinned bones directly, so the existing palm, finger, and
held-prop local poses travel naturally with their parent arm. Binding validation temporarily
rotates each of the six bones by one degree, verifies that it belongs to the visible skinned
mesh and carries its approved child pose, then restores the exact original rotation. It does
not run IK, solve contacts, save the scene, or rewrite `SupportHold` / `CassetteCarry`.

At runtime, camera-relative tuning belongs to the separate
`SoulRecorder First-Person Presentation` container. The `soulrecorder_fp` asset root remains
at identity beneath it, isolating gameplay placement from the `SoulRecorderModel` and
`SoulTapeCassette` import corrections.

## Runtime architecture and fallback

### Animation preview approval gate

Generated recorder animation drafts are authoring evidence, not runtime assets. A numerical
or structural PASS does not authorize integration. Both the insertion and ejection MP4s for
the exact draft must be visually reviewed and explicitly approved by the user before its
poses, clips, or timing may be consumed by runtime code or packaged into the runtime bundle.
Until that approval, the draft remains isolated under its validation output directory and
must not be deployed to EFT.

`AutoAnimationDraft_v4` is retained as rejected visual evidence. Its visible interpolation
between the Carry and L3 support grips allowed the recorder to float and brought both
sleeves into the center. `AutoAnimationDraft_v5` instead establishes PhysicalGrip_L3 below
frame, keeps the left hand/recorder relationship rigid for every visible frame, performs the
cassette reorientation on the clear right side, and applies a per-frame support-contact and
skinned-sleeve-overlap gate. V5 remains preview-only until its two MP4s receive explicit
human approval.

`SoulRecorderAssetProvider` prewarms once during plugin startup. `SoulPlayerAssetBundle`
retains recorder, cassette, and the historical `soulrecorder_animated_hands` prefab. The old
`soulrecorder_hands` DevMops address is only a rejection sentinel and is never loaded or
instantiated. The preferred `EftNativeSoulRecorderHandsView` tells the provider not to
instantiate packaged animated hands, resolves the installed player's native arms, and uses
the recorder/cassette-only view for SoulPlayer models. It does not duplicate hands-controller
ownership or weapon restoration.

The start presentation is Enter (`0.416667s`) + Insert (`0.766667s`) + StartExit
(`0.383333s`), totaling `1.566667s`. Stop is StopEnter (`0.383333s`) + Eject
(`0.683333s`) + StopExit (`0.383333s`), totaling `1.450000s`. Hold is a `0.200000s`
loop and CancelInsert is `0.300000s`. The controller's existing deadlines remain the sole
interaction-completion authority.

The recorder is parented to `RecorderGrip`. A carried cassette is parented to
`CassetteGrip`; insertion reparents it to the recorder's `CassetteSlot`, and ejection
reparents it back to `CassetteGrip` after the contact portion of the Eject clip. Both
transfers preserve world position, rotation, and scale. Animation never calls
`SetEmptyHands` or `SetInHands`.

Preferred runtime order is:

1. installed EFT player arms plus packaged SoulPlayer recorder/cassette;
2. compatible sidecar bundle using only the SoulPlayer recorder and cassette;
3. existing SoulPlayer procedural recorder/cassette;
4. existing headless view if procedural construction also fails.

SPT 4.1.2 does not expose a supported native recorder template/controller/prefab contract.
The presentation therefore uses only SoulPlayer-owned recorder/cassette assets while
referencing the player's already-installed native arms at runtime. BAMEN is retained as
authoring history and is not an automatic runtime fallback.

Missing, incompatible, and contract-invalid bundles log once and never block playback.
The provider makes exactly one prewarm/load attempt during plugin startup. Adding or
replacing `soulplayer_assets.bundle` while EFT is already running therefore requires an EFT
restart. Pressing M only uses cached prefab references and never polls the filesystem or
retries bundle loading.

The first packaged-recorder activation emits one visibility diagnostic containing the
selected camera, root transform, every renderer's state/layer/shader/bounds and viewport
position, combined bounds, behind-camera status, viewport exclusion, and culling-mask
compatibility. It does not log again each frame or on later recorder interactions.
The report also includes camera-local recorder and cassette
start/alignment/inserted/eject positions.

On plugin shutdown or hot-disable, the backend calls `AssetBundle.Unload(false)`. This
releases the bundle backing data while preserving any recorder/cassette assets or instances
that may still be referenced during teardown. SoulPlayer does not perform per-raid unloads
or reloads.

World pickups remain procedural for this milestone. Swapping their validated visual before
a real generated cassette has been inspected would unnecessarily risk authored scale and
focus-point contracts. The packaged cassette can be adopted in a focused follow-up after
the recorder bundle passes runtime acceptance.

If this clean, self-loading bundle is nevertheless rejected by EFT as newer or incompatible,
the next step is to compare this minimal sidecar project and build invocation with the WTT
SDK pipeline intended for SPT 4+ and Unity `2022.3.43f1`. WTT SDK is a deferred diagnostic
fallback only: it has not been downloaded or integrated, and Unity-version stripping should
not be introduced before that runtime result is known.

## Reference projects

Unity FPS Foundation is MIT and was inspected as an architectural reference only. Useful
ideas were its separation of equip/holster begin/end events from the handheld model and its
composition of small motion contributors around one presentation root. SoulPlayer already
has equivalent state/timeline separation; no framework, source file, demo asset, or code
section was copied.

The `unity-procedural-motion` repository has no local LICENSE matching its README claim.
No implementation from it was copied, packaged, or used in SoulPlayer.
