SoulPlayer 0.9.1 for SPT 4.1.x
==============================

INSTALL OR UPDATE
1. Close Escape from Tarkov, the SPT Launcher, and the SPT Server.
2. Copy the BepInEx folder from this archive into your SPT folder.
3. Allow Windows to merge the folders and replace older SoulPlayer files.
4. Start SPT normally.

The final plugin path must be:
<SPT>\BepInEx\plugins\SoulPlayer\Soulplayer.dll

SoulRecorder now uses a fully two-dimensional screen overlay embedded in Soulplayer.dll.
The separate soultape_world.bundle contains only SoulPlayer's redistribution-safe CC0 world
cassette visual. No recorder or hand model is loaded from it. SoulRecorder does not manipulate Tarkov
hands, weapons, first-person skeletons, or world-space models for its presentation.
No EFT or Battlestate model, texture, animation, prefab, or AssetBundle file is included.

Your configuration is stored separately under BepInEx\config and is preserved
when updating the plugin.

Source, issues, and release notes:
https://github.com/kelorein/SoulPlayer

