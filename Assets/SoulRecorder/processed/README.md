# Generated processed assets

This directory receives SoulPlayer-derived recorder/cassette/hands FBX files, deterministic
recorder/hands textures, and geometry audit reports from
`tools/Prepare-SoulRecorderAssets.ps1`. The immutable CC0
source archives stay under `D:\Resources\SoulPlayer` and are never copied here.

The recorder output keeps five 2048x2048 source-map derivatives and adds Unity-ready
flap-alpha and metallic-smoothness composites. The cassette output is unbranded and uses
generated materials, so it has no external texture dependency. The hands output uses the
permitted CC0 base-color texture and a reduced shared-root, two-motion-bone skinned rig.

Generated binary assets stay out of source-only review bundles. Only derived redistributable
files may be shipped after their reports and runtime result have been reviewed; the external
master archives are never copied here.
