SoulPlayer v0.2 prototype for SPT 4.0.13
========================================

What changed
------------
- Adds FLAC playback through NAudio.Flac.
- Keeps MP3, OGG and WAV support.
- Scans D:\soulseek_share recursively.
- Ignores duplicate copies using normalized filename + file size.
- Ignores folders named downloading, incomplete, temp or tmp.
- Ignores zero-byte files and common partial-download extensions.
- Replaces the complete old SoulPlayer plugin folder during installation.
- Copies all required NAudio dependency DLLs automatically.
- Uses a darker Tarkov-inspired player window.
- Pauses in raids and resumes in menus.

Install
-------
1. Close SPT/Escape from Tarkov.
2. Run build.bat.
3. Launch SPT.
4. Set Tarkov's built-in Music volume to 0 so both soundtracks do not overlap.

Default music folder
--------------------
D:\soulseek_share

Controls
--------
F7  Hide/show player
F8  Next track
F9  Play/pause

Config
------
After first launch, edit:
F:\SPT_OLD\BepInEx\config\com.lumiere.soulplayer.cfg

Important
---------
The dedicated button beside Hideout is not included yet. EFT menu injection is a
separate compatibility-sensitive step. This version first replaces the playback
engine so your FLAC library works reliably.
