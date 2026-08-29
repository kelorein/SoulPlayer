using System;
using BepInEx;
using BepInEx.Logging;
using SoulPlayer.Audio;
using SoulPlayer.Cassettes;
using SoulPlayer.Configuration;
using SoulPlayer.Library;
using SoulPlayer.Recorder;
using SoulPlayer.Recorder.Assets;
using SoulPlayer.Utils;
using SoulPlayer.World;
using SoulPlayer.UI;
using UnityEngine;

namespace SoulPlayer
{
    [BepInPlugin("com.kelorein.soulplayer", "SoulPlayer", "0.9.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log { get; private set; }
        internal static Plugin Instance { get; private set; }
        internal static SoulPlayerSettings Settings { get; private set; }
        internal static MusicLibrary MusicLibrary { get; private set; }
        internal static TrackRoutingService TrackRouting { get; private set; }
        internal static SoulTapeCatalog TapeCatalog { get; private set; }
        internal static SoulTapeCollection TapeCollection { get; private set; }
        internal static SoulTapeCollectionController TapeCollectionHost { get; private set; }
        internal static SoulAudioPlayer AudioPlayer { get; private set; }
        internal static SoulPlayerVolumePresetController VolumePresetController
        {
            get;
            private set;
        }
        internal static SoulPlayerVolumeHud VolumeHud { get; private set; }
        internal static TarkovMusicMuter TarkovMusicMuter { get; private set; }
        internal static PostRaidCoordinator PostRaidCoordinator { get; private set; }
        internal static SoulRecorderController RecorderController { get; private set; }
        internal static SoulRecorderAssetProvider RecorderAssets { get; private set; }
        internal static RecorderDiagnostics RecorderDiagnostics { get; private set; }
        internal static SoulTapeWorldDiscoveryController WorldDiscoveryController { get; private set; }
        internal static SoulTapeWorldVisualAssetProvider WorldCassetteVisualAssets { get; private set; }
#if SOULPLAYER_PLACEMENT_TOOLS
        internal static DevelopmentSoulTapeSpawnMarker TapeSpawnMarker { get; private set; }
        internal static DevelopmentSoulRecorderPresentationTuner RecorderPresentationTuner
        {
            get;
            private set;
        }
#endif

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            DontDestroyOnLoad(gameObject);

            Settings = new SoulPlayerSettings(Config);

            EnablePatch("Tushonka music volume", () => new Patches.TarkovMusicVolumePatch().Enable());
            EnablePatch("Tushonka settings music apply", () => new Patches.TarkovMusicSettingsApplyPatch().Enable());
            EnablePatch("Tushonka force-apply volume", () => new Patches.TarkovForceApplyVolumePatch().Enable());
            EnablePatch("Tushonka sound settings screen", () => new Patches.TarkovSoundSettingsTabPatch().Enable());

            MusicLibrary = new MusicLibrary();
            TrackRouting = new TrackRoutingService(
                TrackRoutingService.GetDefaultPath(),
                message => Log.LogWarning(message));
            ISoulTapeLog tapeLog = new PluginSoulTapeLog();
            // The active recorder presentation is a self-contained 2D overlay.
            // Archived 3D bundle/native-controller code remains in the repository
            // for reference but is intentionally not loaded or patched at runtime.
            RecorderAssets = null;
            WorldCassetteVisualAssets = new SoulTapeWorldVisualAssetProvider(tapeLog);
            WorldCassetteVisualAssets.Prewarm();
            TapeCatalog = new SoulTapeCatalog(tapeLog);
            TapeCollection = new SoulTapeCollection(
                TapeCatalog,
                new JsonSoulTapeCollectionStore(
                    SoulTapeCollectionController.GetDefaultCollectionFolder()),
                tapeLog);
            SoulTapeSpawnAnchorCatalog spawnAnchors =
                new SoulTapeSpawnAnchorCatalog(typeof(Plugin).Assembly, tapeLog);
            TapeCollectionHost = gameObject.AddComponent<SoulTapeCollectionController>();
            TapeCollectionHost.Initialize(
                Settings,
                MusicLibrary,
                TapeCatalog,
                TapeCollection,
                new SptProfileIdProvider(tapeLog));
            AudioPlayer = gameObject.AddComponent<SoulAudioPlayer>();
            AudioPlayer.Initialize(Settings, TrackRouting);
            VolumePresetController =
                gameObject.AddComponent<SoulPlayerVolumePresetController>();
            VolumePresetController.Initialize(Settings);
            VolumeHud = gameObject.AddComponent<SoulPlayerVolumeHud>();
            VolumeHud.Initialize(Settings);
            TarkovMusicMuter = gameObject.AddComponent<TarkovMusicMuter>();
            TarkovMusicMuter.Initialize(Settings);
            PostRaidCoordinator = gameObject.AddComponent<PostRaidCoordinator>();
            RecorderController = gameObject.AddComponent<SoulRecorderController>();
            RecorderController.Initialize(Settings);
            RecorderDiagnostics = gameObject.AddComponent<RecorderDiagnostics>();
            WorldDiscoveryController = gameObject.AddComponent<SoulTapeWorldDiscoveryController>();
            WorldDiscoveryController.Initialize(
                Settings,
                MusicLibrary,
                TapeCatalog,
                TapeCollection,
                TapeCollectionHost,
                spawnAnchors,
                tapeLog);
#if SOULPLAYER_PLACEMENT_TOOLS
            TapeSpawnMarker = gameObject.AddComponent<DevelopmentSoulTapeSpawnMarker>();
            TapeSpawnMarker.Initialize(Config);
            RecorderPresentationTuner =
                gameObject.AddComponent<DevelopmentSoulRecorderPresentationTuner>();
            RecorderPresentationTuner.Initialize(Config);
#endif

            EnablePatch("menu screen", () => new Patches.MenuScreenPatch().Enable());
            EnablePatch("menu taskbar", () => new Patches.MenuTaskBarPatch().Enable());
            EnablePatch("in-game folder browser", () => new Patches.FolderBrowserPatch().Enable());
            EnablePatch("post-raid result", () => new Patches.PostRaidResultPatch().Enable());

            MusicLibrary.BeginScan(Settings.GetScanFolders());
            Log.LogInfo("SoulPlayer 0.9.0 loaded. Library scan started.");
            Log.LogInfo(
                "SoulRecorder: press M during a raid to enter/exit; press " +
                Settings.NextRaidCassetteHotkey.MainKey +
                " to play the next raid cassette.");
            Log.LogInfo("Recorder discovery probe armed on Ctrl+Shift+F10 (development branch only).");
#if SOULPLAYER_PLACEMENT_TOOLS
            Log.LogWarning(
                "SoulTape placement-tools DEVELOPMENT BUILD active; F9 solves and " +
                "saves from the current authoring view, Ctrl+Shift+F8 toggles " +
                "hybrid player/camera authoring noclip, and recorder tuning " +
                "reload/log is " +
                "Ctrl+Shift+F7.");
#endif
        }

        private static void EnablePatch(string name, Action enable)
        {
            try
            {
                enable();
                Log.LogInfo("SoulPlayer enabled the " + name + " patch.");
            }
            catch (Exception ex)
            {
                Log.LogError("SoulPlayer could not enable the " + name + " patch: " + ex);
            }
        }

        private void OnDestroy()
        {
#if SOULPLAYER_PLACEMENT_TOOLS
            if (TapeSpawnMarker != null)
            {
                TapeSpawnMarker.Shutdown("plugin shutdown");
                TapeSpawnMarker = null;
            }
#endif
            if (RecorderController != null)
            {
                RecorderController.Shutdown("plugin destroyed");
            }

            if (RecorderAssets != null)
            {
                RecorderAssets.Dispose();
                RecorderAssets = null;
            }

            if (WorldCassetteVisualAssets != null)
            {
                WorldCassetteVisualAssets.Dispose();
                WorldCassetteVisualAssets = null;
            }
        }
    }
}
