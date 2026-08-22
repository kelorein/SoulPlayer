using System;
using System.Linq;
using Comfort.Common;
using EFT;
using SoulPlayer.Configuration;
using SoulPlayer.Library;
using SoulPlayer.Utils;
using UnityEngine;

namespace SoulPlayer.Recorder
{
    /// <summary>
    /// First SoulPlayer recorder prototype.
    ///
    /// M toggles one starter tape during a live raid. Recorder audio uses its own
    /// AudioSource so the normal menu player can remain paused for the raid.
    ///
    /// For the first physical-hands validation, an equipped SPT radio transmitter
    /// is used only as a temporary animation proxy. SoulPlayer does not copy or
    /// redistribute any EFT recorder assets.
    /// </summary>
    internal sealed class SoulRecorderController : MonoBehaviour
    {
        private const KeyCode RecorderHotkey = KeyCode.M;
        private const string PreferredStarterArtist = "Scott Buckley";
        private const string PreferredStarterTitle = "The Long Dark";
        private const float PhysicalProxyRetrySeconds = 0.5f;

        private SoulPlayerSettings _settings;
        private SoulRecorderAudioPlayer _audioPlayer;
        private bool _active;
        private bool _pendingStart;
        private bool _wasInRaid;
        private bool _physicalProxyRaised;
        private bool _loggedMissingPhysicalProxy;
        private float _nextPhysicalProxyAttempt;

        internal bool IsActive { get { return _active; } }
        internal MusicTrack ActiveTape { get { return _audioPlayer == null ? null : _audioPlayer.CurrentTrack; } }

        internal void Initialize(SoulPlayerSettings settings)
        {
            _settings = settings;
            _audioPlayer = gameObject.AddComponent<SoulRecorderAudioPlayer>();
            _audioPlayer.Initialize(settings);

            if (Plugin.MusicLibrary != null)
            {
                Plugin.MusicLibrary.Changed += OnLibraryChanged;
            }

            Plugin.Log.LogInfo(
                "SoulRecorder prototype ready: M toggles the starter tape during raids. " +
                "Equip a radio transmitter to test the temporary physical-hands proxy.");
        }

        private void Update()
        {
            bool inRaid = GameState.IsInRaid();
            if (!inRaid)
            {
                if (_wasInRaid || _active || _pendingStart)
                {
                    StopRecorder("raid ended");
                }

                _wasInRaid = false;
                return;
            }

            _wasInRaid = true;

            if (Input.GetKeyDown(RecorderHotkey))
            {
                if (_active || _pendingStart)
                {
                    StopRecorder("M pressed");
                }
                else
                {
                    StartRecorder();
                }
            }

            if (_active && Time.unscaledTime >= _nextPhysicalProxyAttempt)
            {
                _nextPhysicalProxyAttempt = Time.unscaledTime + PhysicalProxyRetrySeconds;
                TrySetPhysicalProxyState(true);
            }

            // A cassette is single-play for this first prototype. When the track
            // reaches its natural end, lower the proxy and return to the idle state.
            if (_active && !_audioPlayer.IsLoading && !_audioPlayer.IsPlaying &&
                _audioPlayer.CurrentTrack != null)
            {
                StopRecorder("tape finished");
            }
        }

        private void StartRecorder()
        {
            if (!GameState.IsInRaid())
            {
                return;
            }

            MusicTrack track = ResolveStarterTape();
            if (track == null)
            {
                if (Plugin.MusicLibrary != null && Plugin.MusicLibrary.IsScanning)
                {
                    _pendingStart = true;
                    Plugin.Log.LogInfo("SoulRecorder is waiting for the music library scan to finish.");
                }
                else
                {
                    Plugin.Log.LogWarning("SoulRecorder found no playable track for the starter tape.");
                }
                return;
            }

            _pendingStart = false;
            _active = true;
            _loggedMissingPhysicalProxy = false;
            _nextPhysicalProxyAttempt = 0f;

            _audioPlayer.Play(track);
            bool physical = TrySetPhysicalProxyState(true);

            Plugin.Log.LogInfo(
                "SoulRecorder PLAY -> " + track.Artist + " - " + track.Title +
                (physical ? " [radio-transmitter hands proxy active]" : " [audio-only prototype]"));
        }

        private void StopRecorder(string reason)
        {
            bool hadState = _active || _pendingStart ||
                            (_audioPlayer != null && (_audioPlayer.IsPlaying || _audioPlayer.IsLoading));

            _pendingStart = false;
            _active = false;

            if (_audioPlayer != null)
            {
                _audioPlayer.Stop();
            }

            TrySetPhysicalProxyState(false);
            _physicalProxyRaised = false;

            if (hadState)
            {
                Plugin.Log.LogInfo("SoulRecorder STOP (" + reason + ").");
            }
        }

        private MusicTrack ResolveStarterTape()
        {
            if (Plugin.MusicLibrary == null)
            {
                return null;
            }

            var tracks = Plugin.MusicLibrary.Tracks;
            MusicTrack preferred = tracks.FirstOrDefault(track =>
                string.Equals(track.Artist, PreferredStarterArtist, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(track.Title, PreferredStarterTitle, StringComparison.OrdinalIgnoreCase));

            if (preferred != null)
            {
                return preferred;
            }

            MusicTrack fallback = tracks.FirstOrDefault();
            if (fallback != null)
            {
                Plugin.Log.LogInfo(
                    "Preferred starter tape '" + PreferredStarterArtist + " - " +
                    PreferredStarterTitle + "' is not in the active library; using " +
                    fallback.Artist + " - " + fallback.Title + ".");
            }

            return fallback;
        }

        private bool TrySetPhysicalProxyState(bool raised)
        {
            try
            {
                GameWorld world = Singleton<GameWorld>.Instance;
                Player player = world == null ? null : world.MainPlayer;
                RadioTransmitterController proxy = player == null
                    ? null
                    : player.HandsController as RadioTransmitterController;

                if (proxy == null)
                {
                    if (raised && !_loggedMissingPhysicalProxy)
                    {
                        _loggedMissingPhysicalProxy = true;
                        Plugin.Log.LogInfo(
                            "SoulRecorder physical proxy not active: equip SPT's radio transmitter " +
                            "to test the existing utility-item hands animation. Audio still works.");
                    }
                    return false;
                }

                if (_physicalProxyRaised != raised || proxy.CurrentRadioTransmitterState != raised)
                {
                    proxy.SetAim(raised);
                }

                _physicalProxyRaised = raised;
                _loggedMissingPhysicalProxy = false;
                return true;
            }
            catch (Exception ex)
            {
                if (raised && !_loggedMissingPhysicalProxy)
                {
                    _loggedMissingPhysicalProxy = true;
                    Plugin.Log.LogWarning("SoulRecorder physical proxy failed: " + ex.Message);
                }
                return false;
            }
        }

        private void OnLibraryChanged()
        {
            if (_pendingStart && GameState.IsInRaid())
            {
                StartRecorder();
            }
        }

        private void OnDestroy()
        {
            if (Plugin.MusicLibrary != null)
            {
                Plugin.MusicLibrary.Changed -= OnLibraryChanged;
            }

            StopRecorder("component destroyed");
        }
    }
}
