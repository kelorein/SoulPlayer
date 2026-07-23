using BepInEx;
using BepInEx.Logging;
using SoulPlayer.Audio;
using SoulPlayer.Configuration;
using SoulPlayer.Library;
using UnityEngine;

namespace SoulPlayer
{
    [BepInPlugin("com.kelorein.soulplayer", "SoulPlayer", "0.6.5")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log { get; private set; }
        internal static Plugin Instance { get; private set; }
        internal static SoulPlayerSettings Settings { get; private set; }
        internal static MusicLibrary MusicLibrary { get; private set; }
        internal static SoulAudioPlayer AudioPlayer { get; private set; }

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            DontDestroyOnLoad(gameObject);

            Settings = new SoulPlayerSettings(Config);
            MusicLibrary = new MusicLibrary();
            AudioPlayer = gameObject.AddComponent<SoulAudioPlayer>();
            AudioPlayer.Initialize(Settings);

            new Patches.MenuScreenPatch().Enable();
            new Patches.MenuTaskBarPatch().Enable();
            new Patches.PostRaidResultPatch().Enable();

            MusicLibrary.BeginScan(Settings.GetScanFolders());
            Log.LogInfo("SoulPlayer 0.6.5 loaded. Library scan started.");
        }
    }
}
