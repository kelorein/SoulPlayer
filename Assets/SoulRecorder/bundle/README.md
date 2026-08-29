# Generated runtime bundle

`tools/Build-SoulRecorderAssets.ps1` writes the archived full `soulplayer_assets.bundle`
and the runtime `soultape_world.bundle` here using Unity 2022.3.43f1 for Standalone
Windows 64-bit. Release packaging includes only the visual-only world bundle beside
`Soulplayer.dll`; source archives, the full recorder/hands bundle, and the Unity project
cache are not packaged.

The world cassette bundle is generated from the validated unbranded CC0 cassette prefab,
contains no colliders, and is accepted by the same-editor contract check before packaging.
