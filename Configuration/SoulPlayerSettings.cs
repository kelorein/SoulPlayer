using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using SoulPlayer.Library;
using UnityEngine;

namespace SoulPlayer.Configuration
{
    // Configuration Manager discovers these optional fields by reflection from
    // ConfigDescription.Tags, so SoulPlayer does not need a hard dependency on
    // the F12 Configuration Manager assembly.
    internal sealed class ConfigurationManagerAttributes
    {
        public string DispName;
        public int? Order;
    }

    internal enum LibrarySourceMode
    {
        IncludedOnly = 0,
        PersonalOnly = 1,
        IncludedAndPersonal = 2
    }

    internal sealed class SoulPlayerSettings
    {
        private readonly ConfigFile _config;
        private readonly ConfigEntry<string> _musicFolders;
        private readonly ConfigEntry<LibrarySourceMode> _librarySourceMode;
        private readonly ConfigEntry<SoulPlayer.Cassettes.SoulTapeMusicMode>
            _cassetteMusicMode;
        private readonly ConfigEntry<SoulPlayer.Cassettes.SoulTapeRaidPlaybackMode>
            _raidCassettePlaybackMode;
        private readonly ConfigEntry<float> _volume;
        private readonly ConfigEntry<bool> _shuffle;
        private readonly ConfigEntry<int> _repeatMode;
        private readonly ConfigEntry<bool> _showMiniPlayer;
        private readonly ConfigEntry<SoulPlayer.UI.SoulMiniPlayerCorner> _miniPlayerPosition;
        private readonly ConfigEntry<bool> _muteTarkovMusic;
        private readonly ConfigEntry<bool> _keepMusicPlayingAcrossMenus;
        private readonly ConfigEntry<bool> _autoPlayAfterRaid;
        private readonly ConfigEntry<string> _survivedMusicFolder;
        private readonly ConfigEntry<string> _deathMusicFolder;
        private readonly ConfigEntry<bool> _enableMediaKeys;
        private readonly ConfigEntry<KeyboardShortcut> _playPauseHotkey;
        private readonly ConfigEntry<KeyboardShortcut> _stopHotkey;
        private readonly ConfigEntry<KeyboardShortcut> _nextHotkey;
        private readonly ConfigEntry<KeyboardShortcut> _previousHotkey;
        private readonly ConfigEntry<KeyboardShortcut> _volumeMutedHotkey;
        private readonly ConfigEntry<KeyboardShortcut> _volumeQuarterHotkey;
        private readonly ConfigEntry<KeyboardShortcut> _volumeHalfHotkey;
        private readonly ConfigEntry<KeyboardShortcut> _volumeThreeQuartersHotkey;
        private readonly ConfigEntry<KeyboardShortcut> _volumeFullHotkey;
        private readonly ConfigEntry<KeyboardShortcut> _nextRaidCassetteHotkey;
        private readonly ConfigEntry<KeyboardShortcut> _recorderStartStopHotkey;
        private readonly ConfigEntry<KeyboardShortcut> _collectCassetteHotkey;
        private readonly ConfigEntry<float> _cassetteInteractionDistance;
        private readonly ConfigEntry<bool> _soulTapeTargetingDiagnostics;
        private readonly ConfigEntry<bool> _recorderOverlayEnabled;
        private readonly ConfigEntry<SoulPlayer.Recorder.SoulRecorderOverlayCorner>
            _recorderOverlayCorner;
        private readonly ConfigEntry<float> _recorderOverlayScale;
        private readonly ConfigEntry<float> _recorderOverlayHorizontalOffset;
        private readonly ConfigEntry<float> _recorderOverlayVerticalOffset;
        private readonly ConfigEntry<float> _recorderOverlayAnimationSpeed;
        private readonly ConfigEntry<int> _recorderOverlayDefaultsVersion;
        private readonly ConfigEntry<float> _raidFadeSeconds;
        private readonly ConfigEntry<float> _postRaidTransitionSeconds;

        internal event Action<float> VolumeChanged;
        internal event Action MiniPlayerChanged;
        internal event Action MenuContinuityChanged;
        internal event Action<SoulPlayer.Cassettes.SoulTapeMusicMode>
            CassetteMusicModeChanged;
        internal event Action<SoulPlayer.Cassettes.SoulTapeRaidPlaybackMode>
            RaidCassettePlaybackModeChanged;

        internal SoulPlayerSettings(ConfigFile config)
        {
            _config = config;
            _musicFolders = config.Bind(
                "Library",
                "Music folders",
                DefaultPersonalMusicFolder(),
                "Folders scanned recursively. Separate multiple folders with | or ;. Relative paths resolve from the SPT game root.");

            _librarySourceMode = config.Bind(
                "Library",
                "Library source mode",
                LibrarySourceMode.IncludedAndPersonal,
                "IncludedOnly = bundled CC0 music only, PersonalOnly = your folders only, IncludedAndPersonal = merge both libraries.");

            _cassetteMusicMode = config.Bind(
                "SoulTape discovery",
                "Cassette music mode",
                SoulPlayer.Cassettes.SoulTapeMusicMode.MergeBuiltInAndUser,
                "BuiltInOnly = bundled copyright-free tracks, UserOnly = scanned personal-library tracks, MergeBuiltInAndUser = both pools with stable-ID deduplication.");
            _cassetteMusicMode.SettingChanged += OnCassetteMusicModeSettingChanged;

            _raidCassettePlaybackMode = config.Bind(
                "SoulTape discovery",
                "Raid cassette playback mode",
                SoulPlayer.Cassettes.SoulTapeRaidPlaybackMode.FavoritesFirst,
                "FavoritesOnly = discovered favorites only, Discovered = all discovered tapes, FavoritesFirst = favorites when available with discovered tapes as fallback.");
            _raidCassettePlaybackMode.SettingChanged +=
                OnRaidCassettePlaybackModeSettingChanged;

            _volume = config.Bind(
                "Player",
                "Volume",
                0.65f,
                new ConfigDescription("SoulPlayer volume.", new AcceptableValueRange<float>(0f, 1f)));
            _volume.SettingChanged += OnVolumeSettingChanged;

            _shuffle = config.Bind("Player", "Shuffle", true, "Shuffle the current library queue.");
            _repeatMode = config.Bind("Player", "Repeat mode", 0, "0 = off, 1 = repeat queue, 2 = repeat one.");
            _keepMusicPlayingAcrossMenus = config.Bind(
                "Player", "Keep music playing across menus", true,
                "Keep the current song playing through normal out-of-raid menus, including Hideout. Raid deployment and post-raid loading still suspend playback. Disable to use legacy screen-transition behavior.");
            _keepMusicPlayingAcrossMenus.SettingChanged += OnMenuContinuitySettingChanged;
            _muteTarkovMusic = config.Bind(
                "Player",
                "Mute Tushonka music",
                true,
                "Mute Tushonka's built-in music while SoulPlayer is active. Other game audio is not changed.");

            _showMiniPlayer = config.Bind(
                "Interface",
                "Show mini player",
                true,
                "Show compact playback controls in the selected screen corner.");
            _miniPlayerPosition = config.Bind(
                "Interface",
                "Mini-player position",
                SoulPlayer.UI.SoulMiniPlayerCorner.BottomRight,
                "BottomLeft, BottomRight, TopLeft or TopRight. Updates immediately; BottomRight preserves the existing placement.");
            _showMiniPlayer.SettingChanged += OnMiniPlayerSettingChanged;
            _miniPlayerPosition.SettingChanged += OnMiniPlayerSettingChanged;

            _autoPlayAfterRaid = config.Bind(
                "Post-raid",
                "Autoplay after raid",
                true,
                "Automatically start outcome-specific music when the raid result screen appears.");

            _survivedMusicFolder = config.Bind(
                "Post-raid",
                "Survived music folder",
                DefaultPostRaidFolder("Survived"),
                "Legacy folder scanned for tracks that can be routed to Extract in the Library.");

            _deathMusicFolder = config.Bind(
                "Post-raid",
                "Death music folder",
                DefaultPostRaidFolder("Died"),
                "Legacy folder scanned for tracks that can be routed to Death in the Library.");

            _enableMediaKeys = config.Bind(
                "Global controls",
                "Enable media keyboard keys",
                true,
                "Use the keyboard's dedicated Play/Pause, Stop, Next and Previous media buttons.");

            _playPauseHotkey = config.Bind(
                "Global controls",
                "Play or pause hotkey",
                new KeyboardShortcut(KeyCode.None),
                "Optional additional play/pause shortcut. The media Play/Pause key works by default.");

            _stopHotkey = config.Bind(
                "Global controls",
                "Stop playback hotkey",
                new KeyboardShortcut(KeyCode.None),
                "Optional additional stop shortcut. The media Stop key works by default.");

            _nextHotkey = config.Bind(
                "Global controls",
                "Next track hotkey",
                new KeyboardShortcut(KeyCode.None),
                "Optional additional next-track shortcut. The media Next key works by default.");

            _previousHotkey = config.Bind(
                "Global controls",
                "Previous track hotkey",
                new KeyboardShortcut(KeyCode.None),
                "Optional additional previous-track shortcut. The media Previous key works by default.");

            _volumeMutedHotkey = config.Bind(
                "Volume presets",
                "Muted hotkey",
                new KeyboardShortcut(KeyCode.Keypad0),
                OrderedDescription(
                    "Set SoulPlayer and SoulRecorder music volume to 0%.",
                    500,
                    "0% / Muted hotkey"));

            _volumeQuarterHotkey = config.Bind(
                "Volume presets",
                "25% hotkey",
                new KeyboardShortcut(KeyCode.Keypad1),
                OrderedDescription(
                    "Set SoulPlayer and SoulRecorder music volume to 25%.",
                    400,
                    "25% hotkey"));

            _volumeHalfHotkey = config.Bind(
                "Volume presets",
                "50% hotkey",
                new KeyboardShortcut(KeyCode.Keypad2),
                OrderedDescription(
                    "Set SoulPlayer and SoulRecorder music volume to 50%.",
                    300,
                    "50% hotkey"));

            _volumeThreeQuartersHotkey = config.Bind(
                "Volume presets",
                "75% hotkey",
                new KeyboardShortcut(KeyCode.Keypad3),
                OrderedDescription(
                    "Set SoulPlayer and SoulRecorder music volume to 75%.",
                    200,
                    "75% hotkey"));

            _volumeFullHotkey = config.Bind(
                "Volume presets",
                "100% hotkey",
                new KeyboardShortcut(KeyCode.Keypad4),
                OrderedDescription(
                    "Set SoulPlayer and SoulRecorder music volume to 100%.",
                    100,
                    "100% hotkey"));

            _recorderStartStopHotkey = config.Bind(
                "SoulTape discovery",
                "SoulRecorder start / stop hotkey",
                new KeyboardShortcut(KeyCode.M),
                "Start or stop SoulRecorder in a raid. Changes apply immediately. If this and Next raid cassette share a shortcut, Start / Stop takes priority.");

            _nextRaidCassetteHotkey = config.Bind(
                "SoulTape discovery",
                "Next raid cassette hotkey",
                new KeyboardShortcut(KeyCode.N),
                "Skip through SoulRecorder's in-raid cassette shuffle bag. This is independent from Global controls -> Next track hotkey.");

            _collectCassetteHotkey = config.Bind(
                "SoulTape discovery",
                "Collect cassette hotkey",
                new KeyboardShortcut(KeyCode.F),
                "Collect a SoulTape world cassette while aiming at it within interaction range.");

            _cassetteInteractionDistance = config.Bind(
                "SoulTape discovery",
                "Cassette interaction distance",
                2.5f,
                new ConfigDescription(
                    "Maximum SoulTape pickup distance in metres.",
                    new AcceptableValueRange<float>(1.5f, 4f)));

            _soulTapeTargetingDiagnostics = config.Bind(
                "SoulTape discovery",
                "Show targeting diagnostics",
                false,
                "Show a temporary on-screen SoulTape camera and targeting diagnostic panel during runtime acceptance testing.");

            _recorderOverlayEnabled = config.Bind(
                "SoulRecorder overlay",
                "Recorder overlay enabled",
                true,
                "Show the two-dimensional SoulRecorder cassette animation. Music playback remains functional if this is disabled or its images cannot load.");
            _recorderStartStopHotkey.SettingChanged += OnRecorderBindingChanged;
            _nextRaidCassetteHotkey.SettingChanged += OnRecorderBindingChanged;

            _recorderOverlayCorner = config.Bind(
                "SoulRecorder overlay",
                "Recorder corner",
                SoulPlayer.Recorder.SoulRecorderOverlayCorner.BottomRight,
                "Screen corner used by the recorder overlay.");
            _recorderOverlayScale = config.Bind(
                "SoulRecorder overlay",
                "Recorder scale",
                0.78f,
                new ConfigDescription(
                    "Recorder overlay scale.",
                    new AcceptableValueRange<float>(0.65f, 1.5f)));
            _recorderOverlayHorizontalOffset = config.Bind(
                "SoulRecorder overlay",
                "Recorder horizontal offset",
                0f,
                new ConfigDescription(
                    "Additional pixels inward from the selected horizontal screen edge.",
                    new AcceptableValueRange<float>(-400f, 400f)));
            _recorderOverlayVerticalOffset = config.Bind(
                "SoulRecorder overlay",
                "Recorder vertical offset",
                0f,
                new ConfigDescription(
                    "Additional pixels upward from the selected vertical screen edge.",
                    new AcceptableValueRange<float>(-300f, 300f)));
            _recorderOverlayAnimationSpeed = config.Bind(
                "SoulRecorder overlay",
                "Recorder animation speed",
                1f,
                new ConfigDescription(
                    "SoulRecorder overlay animation speed multiplier.",
                    new AcceptableValueRange<float>(0.5f, 2f)));
            _recorderOverlayDefaultsVersion = config.Bind(
                "SoulRecorder overlay",
                "Overlay defaults version",
                0,
                "Internal migration version for SoulRecorder overlay defaults.");
            ApplyRecorderOverlayDefaultMigration();

            _raidFadeSeconds = config.Bind(
                "Transitions",
                "Raid fade-out seconds",
                4f,
                new ConfigDescription(
                    "Legacy value retained for compatibility. Deployment now pauses Main immediately to keep raid/loading playback silent.",
                    new AcceptableValueRange<float>(0f, 10f)));

            _postRaidTransitionSeconds = config.Bind(
                "Transitions",
                "Post-raid track fade-out seconds",
                1.5f,
                new ConfigDescription(
                    "Legacy value retained for compatibility. Post-raid playback now starts from silence only when the result/menu is ready.",
                    new AcceptableValueRange<float>(0f, 6f)));
        }

        internal float Volume
        {
            get { return _volume.Value; }
            set
            {
                _volume.Value = Math.Max(0f, Math.Min(1f, value));
                _config.Save();
            }
        }

        internal bool Shuffle
        {
            get { return _shuffle.Value; }
            set
            {
                _shuffle.Value = value;
                _config.Save();
            }
        }

        internal int RepeatMode
        {
            get { return Math.Max(0, Math.Min(2, _repeatMode.Value)); }
            set
            {
                _repeatMode.Value = Math.Max(0, Math.Min(2, value));
                _config.Save();
            }
        }

        internal bool KeepMusicPlayingAcrossMenus { get { return _keepMusicPlayingAcrossMenus.Value; } }

        private void OnMenuContinuitySettingChanged(object sender, EventArgs args)
        {
            Action handler = MenuContinuityChanged;
            if (handler != null) handler();
        }

        internal bool MuteTarkovMusic
        {
            get { return _muteTarkovMusic.Value; }
            set
            {
                _muteTarkovMusic.Value = value;
                _config.Save();
            }
        }

        internal bool ShowMiniPlayer
        {
            get { return _showMiniPlayer.Value; }
            set
            {
                _showMiniPlayer.Value = value;
                _config.Save();
            }
        }

        internal SoulPlayer.UI.SoulMiniPlayerCorner MiniPlayerPosition
        {
            get { return _miniPlayerPosition.Value; }
        }

        private void OnMiniPlayerSettingChanged(object sender, EventArgs args)
        {
            Action handler = MiniPlayerChanged;
            if (handler != null) handler();
        }

        internal bool AutoPlayAfterRaid
        {
            get { return _autoPlayAfterRaid.Value; }
            set
            {
                _autoPlayAfterRaid.Value = value;
                _config.Save();
            }
        }

        internal LibrarySourceMode LibraryMode
        {
            get { return _librarySourceMode.Value; }
            set
            {
                _librarySourceMode.Value = value;
                _config.Save();
            }
        }

        internal string DefaultMusicFolder
        {
            get
            {
                try
                {
                    string assemblyPath = typeof(SoulPlayer.Plugin).Assembly.Location;
                    string pluginFolder = Path.GetDirectoryName(assemblyPath);
                    return string.IsNullOrWhiteSpace(pluginFolder)
                        ? string.Empty
                        : SoulPath.NormalizeConfiguredPath(
                            Path.Combine(pluginFolder, "DefaultMusic"),
                            BepInEx.Paths.GameRootPath);
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        internal string SurvivedMusicFolder
        {
            get { return NormalizePath(_survivedMusicFolder.Value); }
        }

        internal string DeathMusicFolder
        {
            get { return NormalizePath(_deathMusicFolder.Value); }
        }

        internal bool EnableMediaKeys
        {
            get { return _enableMediaKeys.Value; }
        }

        internal KeyboardShortcut PlayPauseHotkey
        {
            get { return _playPauseHotkey.Value; }
        }

        internal KeyboardShortcut StopHotkey
        {
            get { return _stopHotkey.Value; }
        }

        internal KeyboardShortcut NextHotkey
        {
            get { return _nextHotkey.Value; }
        }

        internal KeyboardShortcut PreviousHotkey
        {
            get { return _previousHotkey.Value; }
        }

        internal KeyboardShortcut VolumeMutedHotkey
        {
            get { return _volumeMutedHotkey.Value; }
        }

        internal KeyboardShortcut VolumeQuarterHotkey
        {
            get { return _volumeQuarterHotkey.Value; }
        }

        internal KeyboardShortcut VolumeHalfHotkey
        {
            get { return _volumeHalfHotkey.Value; }
        }

        internal KeyboardShortcut VolumeThreeQuartersHotkey
        {
            get { return _volumeThreeQuartersHotkey.Value; }
        }

        internal KeyboardShortcut VolumeFullHotkey
        {
            get { return _volumeFullHotkey.Value; }
        }

        internal int RecorderInputRevision { get; private set; }

        private void OnRecorderBindingChanged(object sender, EventArgs args) { RecorderInputRevision++; }

        internal KeyboardShortcut RecorderStartStopHotkey
        {
            get { return _recorderStartStopHotkey.Value; }
        }

        internal KeyboardShortcut NextRaidCassetteHotkey
        {
            get { return _nextRaidCassetteHotkey.Value; }
        }

        internal SoulPlayer.Cassettes.SoulTapeMusicMode CassetteMusicMode
        {
            get { return _cassetteMusicMode.Value; }
        }

        internal SoulPlayer.Cassettes.SoulTapeRaidPlaybackMode
            RaidCassettePlaybackMode
        {
            get { return _raidCassettePlaybackMode.Value; }
        }

        private void OnVolumeSettingChanged(object sender, EventArgs eventArgs)
        {
            Action<float> handler = VolumeChanged;
            if (handler == null)
            {
                return;
            }

            foreach (Action<float> subscriber in handler.GetInvocationList())
            {
                try
                {
                    subscriber(_volume.Value);
                }
                catch (Exception ex)
                {
                    if (Plugin.Log != null)
                    {
                        Plugin.Log.LogError(
                            "SoulPlayer volume change subscriber failed: " +
                            ex.GetBaseException().Message);
                    }
                }
            }
        }

        private void OnCassetteMusicModeSettingChanged(
            object sender,
            EventArgs eventArgs)
        {
            Action<SoulPlayer.Cassettes.SoulTapeMusicMode> handler =
                CassetteMusicModeChanged;
            if (handler == null)
            {
                return;
            }

            foreach (Action<SoulPlayer.Cassettes.SoulTapeMusicMode> subscriber
                     in handler.GetInvocationList())
            {
                try
                {
                    subscriber(_cassetteMusicMode.Value);
                }
                catch (Exception ex)
                {
                    if (Plugin.Log != null)
                    {
                        Plugin.Log.LogError(
                            "SoulTape music-mode change subscriber failed: " +
                            ex.GetBaseException().Message);
                    }
                }
            }
        }

        private void OnRaidCassettePlaybackModeSettingChanged(
            object sender,
            EventArgs eventArgs)
        {
            Action<SoulPlayer.Cassettes.SoulTapeRaidPlaybackMode> handler =
                RaidCassettePlaybackModeChanged;
            if (handler == null)
            {
                return;
            }

            foreach (Action<SoulPlayer.Cassettes.SoulTapeRaidPlaybackMode> subscriber
                     in handler.GetInvocationList())
            {
                try
                {
                    subscriber(_raidCassettePlaybackMode.Value);
                }
                catch (Exception ex)
                {
                    if (Plugin.Log != null)
                    {
                        Plugin.Log.LogError(
                            "SoulTape raid-playback-mode subscriber failed: " +
                            ex.GetBaseException().Message);
                    }
                }
            }
        }

        private void ApplyRecorderOverlayDefaultMigration()
        {
            if (_recorderOverlayDefaultsVersion.Value >= 1)
            {
                return;
            }

            // Version 0 shipped 1.0 as the default. Move that exact legacy
            // default to the approved 0.78 presentation while preserving every
            // deliberately customized scale.
            if (Math.Abs(_recorderOverlayScale.Value - 1f) < 0.0001f)
            {
                _recorderOverlayScale.Value = 0.78f;
            }
            _recorderOverlayDefaultsVersion.Value = 1;
            _config.Save();
        }

        internal KeyboardShortcut CollectCassetteHotkey
        {
            get { return _collectCassetteHotkey.Value; }
        }

        internal float CassetteInteractionDistance
        {
            get { return Math.Max(1.5f, Math.Min(4f, _cassetteInteractionDistance.Value)); }
        }

        internal bool ShowSoulTapeTargetingDiagnostics
        {
            get { return _soulTapeTargetingDiagnostics.Value; }
        }

        internal bool RecorderOverlayEnabled
        {
            get { return _recorderOverlayEnabled.Value; }
        }

        internal SoulPlayer.Recorder.SoulRecorderOverlaySettings RecorderOverlaySettings
        {
            get
            {
                return new SoulPlayer.Recorder.SoulRecorderOverlaySettings
                {
                    Corner = _recorderOverlayCorner.Value,
                    Scale = Math.Max(0.65f, Math.Min(1.5f,
                        _recorderOverlayScale.Value)),
                    HorizontalOffset = Math.Max(-400f, Math.Min(400f,
                        _recorderOverlayHorizontalOffset.Value)),
                    VerticalOffset = Math.Max(-300f, Math.Min(300f,
                        _recorderOverlayVerticalOffset.Value)),
                    AnimationSpeed = Math.Max(0.5f, Math.Min(2f,
                        _recorderOverlayAnimationSpeed.Value))
                };
            }
        }

        internal float RaidFadeSeconds
        {
            get { return Math.Max(0f, Math.Min(10f, _raidFadeSeconds.Value)); }
        }

        internal float PostRaidTransitionSeconds
        {
            get { return Math.Max(0f, Math.Min(6f, _postRaidTransitionSeconds.Value)); }
        }

        private static ConfigDescription OrderedDescription(
            string description,
            int order,
            string displayName = null)
        {
            return new ConfigDescription(
                description,
                null,
                new ConfigurationManagerAttributes
                {
                    DispName = displayName,
                    Order = order
                });
        }

        internal IReadOnlyList<string> GetFolders()
        {
            return (_musicFolders.Value ?? string.Empty)
                .Split(new[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(SoulPath.Comparer)
                .ToList();
        }

        internal IReadOnlyList<string> GetLibraryFolders()
        {
            List<string> folders = new List<string>();

            if (LibraryMode != LibrarySourceMode.PersonalOnly && Directory.Exists(DefaultMusicFolder))
            {
                folders.Add(DefaultMusicFolder);
            }

            if (LibraryMode != LibrarySourceMode.IncludedOnly)
            {
                folders.AddRange(GetFolders());
            }

            return folders
                .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                .Distinct(SoulPath.Comparer)
                .ToList();
        }

        internal IReadOnlyList<string> GetScanFolders()
        {
            IEnumerable<string> folders = GetLibraryFolders();
            if (LibraryMode != LibrarySourceMode.IncludedOnly)
            {
                folders = folders.Concat(new[] { SurvivedMusicFolder, DeathMusicFolder });
            }

            return folders
                .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                .Distinct(SoulPath.Comparer)
                .ToList();
        }

        internal string GetPostRaidFolder(bool survived)
        {
            return survived ? SurvivedMusicFolder : DeathMusicFolder;
        }

        internal bool SetPostRaidFolders(string survivedFolder, string deathFolder, out string message)
        {
            string survived = NormalizePath(survivedFolder);
            string died = NormalizePath(deathFolder);

            if (!EnsureDirectory(survived, "survived-music", out message))
            {
                return false;
            }

            if (!EnsureDirectory(died, "death-music", out message))
            {
                return false;
            }

            _survivedMusicFolder.Value = survived;
            _deathMusicFolder.Value = died;
            _config.Save();
            message = "Post-raid playlists saved.";
            return true;
        }

        private static bool EnsureDirectory(string path, string description, out string message)
        {
            message = string.Empty;
            if (string.IsNullOrWhiteSpace(path) || Directory.Exists(path))
            {
                return true;
            }

            try
            {
                Directory.CreateDirectory(path);
                return true;
            }
            catch (Exception ex)
            {
                message = "Could not create the " + description + " folder: " + ex.Message;
                return false;
            }
        }

        internal bool AddFolder(string path, out string message)
        {
            string normalized = NormalizePath(path);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                message = "Enter a music folder first.";
                return false;
            }

            if (!Directory.Exists(normalized))
            {
                message = "That folder does not exist.";
                return false;
            }

            List<string> folders = GetFolders().ToList();
            if (folders.Contains(normalized, SoulPath.Comparer))
            {
                message = "That folder is already in your library.";
                return false;
            }

            folders.Add(normalized);
            SaveFolders(folders);
            message = LibraryMode == LibrarySourceMode.IncludedOnly
                ? "Folder added. Switch Library source mode to PersonalOnly or IncludedAndPersonal to hear it."
                : "Folder added. Scanning in the background...";
            return true;
        }

        internal bool RemoveFolder(string path)
        {
            string normalized = NormalizePath(path);
            List<string> folders = GetFolders()
                .Where(folder => !string.Equals(folder, normalized, SoulPath.Comparison))
                .ToList();

            if (folders.Count == GetFolders().Count)
            {
                return false;
            }

            SaveFolders(folders);
            return true;
        }

        private void SaveFolders(IEnumerable<string> folders)
        {
            _musicFolders.Value = string.Join("|", folders);
            _config.Save();
        }

        private static string NormalizePath(string path)
        {
            return SoulPath.NormalizeConfiguredPath(path, BepInEx.Paths.GameRootPath);
        }

        private static string DefaultPersonalMusicFolder()
        {
            if (SoulPath.IsWindows)
            {
                return @"D:\soulseek_share";
            }

            string music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            return string.IsNullOrWhiteSpace(music)
                ? "Music/SoulPlayer"
                : Path.Combine(music, "SoulPlayer");
        }

        private static string DefaultPostRaidFolder(string outcome)
        {
            return Path.Combine(DefaultPersonalMusicFolder(), "PostRaid", outcome);
        }
    }
}
