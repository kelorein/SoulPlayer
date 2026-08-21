using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace SoulPlayer.Configuration
{
    internal sealed class SoulPlayerSettings
    {
        private readonly ConfigFile _config;
        private readonly ConfigEntry<string> _musicFolders;
        private readonly ConfigEntry<float> _volume;
        private readonly ConfigEntry<bool> _shuffle;
        private readonly ConfigEntry<int> _repeatMode;
        private readonly ConfigEntry<bool> _showMiniPlayer;
        private readonly ConfigEntry<bool> _autoPlayAfterRaid;
        private readonly ConfigEntry<string> _survivedMusicFolder;
        private readonly ConfigEntry<string> _deathMusicFolder;
        private readonly ConfigEntry<bool> _enableMediaKeys;
        private readonly ConfigEntry<KeyboardShortcut> _playPauseHotkey;
        private readonly ConfigEntry<KeyboardShortcut> _stopHotkey;
        private readonly ConfigEntry<KeyboardShortcut> _nextHotkey;
        private readonly ConfigEntry<KeyboardShortcut> _previousHotkey;
        private readonly ConfigEntry<float> _raidFadeSeconds;
        private readonly ConfigEntry<float> _postRaidTransitionSeconds;

        internal SoulPlayerSettings(ConfigFile config)
        {
            _config = config;
            _musicFolders = config.Bind(
                "Library",
                "Music folders",
                @"D:\soulseek_share",
                "Windows folders scanned recursively. Separate multiple folders with |.");

            _volume = config.Bind(
                "Player",
                "Volume",
                0.65f,
                new ConfigDescription("SoulPlayer volume.", new AcceptableValueRange<float>(0f, 1f)));

            _shuffle = config.Bind("Player", "Shuffle", true, "Shuffle the current library queue.");
            _repeatMode = config.Bind("Player", "Repeat mode", 0, "0 = off, 1 = repeat queue, 2 = repeat one.");

            _showMiniPlayer = config.Bind(
                "Interface",
                "Show mini player",
                true,
                "Show compact playback controls above the bottom-right menu toolbar.");

            _autoPlayAfterRaid = config.Bind(
                "Post-raid",
                "Autoplay after raid",
                true,
                "Automatically start outcome-specific music when the raid result screen appears.");

            _survivedMusicFolder = config.Bind(
                "Post-raid",
                "Survived music folder",
                @"D:\soulseek_share\PostRaid\Survived",
                "Tracks under this folder are used after surviving a raid.");

            _deathMusicFolder = config.Bind(
                "Post-raid",
                "Death music folder",
                @"D:\soulseek_share\PostRaid\Died",
                "Tracks under this folder are used after a failed raid.");

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

            _raidFadeSeconds = config.Bind(
                "Transitions",
                "Raid fade-out seconds",
                4f,
                new ConfigDescription(
                    "How long music fades after the deployment countdown appears.",
                    new AcceptableValueRange<float>(0f, 10f)));

            _postRaidTransitionSeconds = config.Bind(
                "Transitions",
                "Post-raid track fade-out seconds",
                1.5f,
                new ConfigDescription(
                    "How long the previous song fades before survived/death music starts.",
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

        internal bool ShowMiniPlayer
        {
            get { return _showMiniPlayer.Value; }
            set
            {
                _showMiniPlayer.Value = value;
                _config.Save();
            }
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

        internal float RaidFadeSeconds
        {
            get { return Math.Max(0f, Math.Min(10f, _raidFadeSeconds.Value)); }
        }

        internal float PostRaidTransitionSeconds
        {
            get { return Math.Max(0f, Math.Min(6f, _postRaidTransitionSeconds.Value)); }
        }

        internal IReadOnlyList<string> GetFolders()
        {
            return (_musicFolders.Value ?? string.Empty)
                .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal IReadOnlyList<string> GetScanFolders()
        {
            return GetFolders()
                .Concat(new[] { SurvivedMusicFolder, DeathMusicFolder })
                .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
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
            if (folders.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                message = "That folder is already in your library.";
                return false;
            }

            folders.Add(normalized);
            SaveFolders(folders);
            message = "Folder added. Scanning in the background...";
            return true;
        }

        internal bool RemoveFolder(string path)
        {
            string normalized = NormalizePath(path);
            List<string> folders = GetFolders()
                .Where(folder => !string.Equals(folder, normalized, StringComparison.OrdinalIgnoreCase))
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
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                string cleaned = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
                return Path.GetFullPath(cleaned).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
