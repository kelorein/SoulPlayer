using System;
using BepInEx;
using BepInEx.Logging;
using SoulPlayer.Audio;
using SoulPlayer.Configuration;
using SoulPlayer.Library;
using UnityEngine;

namespace SoulPlayer
{
    [BepInPlugin("com.kelorein.soulplayer", "SoulPlayer", "0.8.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log { get; private set; }
        internal static Plugin Instance { get; private set; }
        internal static SoulPlayerSettings Settings { get; private set; }
        internal static MusicLibrary MusicLibrary { get; private set; }
        internal static SoulAudioPlayer AudioPlayer { get; private set; }
        internal static TarkovMusicMuter TarkovMusicMuter { get; private set; }
        internal static PostRaidCoordinator PostRaidCoordinator { get; private set; }

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
            AudioPlayer = gameObject.AddComponent<SoulAudioPlayer>();
            AudioPlayer.Initialize(Settings);
            TarkovMusicMuter = gameObject.AddComponent<TarkovMusicMuter>();
            TarkovMusicMuter.Initialize(Settings);
            PostRaidCoordinator = gameObject.AddComponent<PostRaidCoordinator>();

            EnablePatch("menu screen", () => new Patches.MenuScreenPatch().Enable());
            EnablePatch("menu taskbar", () => new Patches.MenuTaskBarPatch().Enable());
            EnablePatch("in-game folder browser", () => new Patches.FolderBrowserPatch().Enable());
            EnablePatch("post-raid result", () => new Patches.PostRaidResultPatch().Enable());

            MusicLibrary.BeginScan(Settings.GetScanFolders());
            Log.LogInfo("SoulPlayer 0.8.0 loaded. Library scan started.");
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
    }
}
