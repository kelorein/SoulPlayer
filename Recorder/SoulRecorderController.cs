using System;
using System.Linq;
using System.Reflection;
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
    /// Physical-hands validation first tries SPT's radio-transmitter controller.
    /// If that item is not equipped, SoulPlayer asks the current hands controller
    /// for its existing compass utility-item state through reflection. SoulPlayer
    /// does not copy or redistribute any EFT recorder assets.
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
        private string _physicalProxyName = string.Empty;

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
                "Physical proxy waits for confirmed tape playback before raising.");
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

            // Do not raise the temporary hands proxy until Unity confirms the tape
            // AudioSource is actually playing. This keeps the animation synchronized
            // with real playback and makes regressions visible in the log.
            if (_active && _audioPlayer.IsPlaying && !_physicalProxyRaised &&
                Time.unscaledTime >= _nextPhysicalProxyAttempt)
            {
                _nextPhysicalProxyAttempt = Time.unscaledTime + PhysicalProxyRetrySeconds;
                if (TrySetPhysicalProxyState(true))
                {
                    Plugin.Log.LogInfo(
                        "SoulRecorder HANDS -> " + _physicalProxyName + " proxy raised after audio start.");
                }
            }

            if (_active && !_audioPlayer.IsLoading && !_audioPlayer.IsPlaying &&
                _audioPlayer.CurrentTrack != null)
            {
                string reason = string.IsNullOrEmpty(_audioPlayer.LastError)
                    ? "tape finished"
                    : "audio failed: " + _audioPlayer.LastError;
                StopRecorder(reason);
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
            _physicalProxyRaised = false;
            _physicalProxyName = string.Empty;
            _loggedMissingPhysicalProxy = false;
            _nextPhysicalProxyAttempt = 0f;

            _audioPlayer.Play(track);

            Plugin.Log.LogInfo(
                "SoulRecorder PLAY REQUEST -> " + track.Artist + " - " + track.Title +
                " [waiting for confirmed audio start before hands proxy]");
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
            _physicalProxyName = string.Empty;

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
                object hands = player == null ? null : player.HandsController;

                if (hands == null)
                {
                    LogMissingProxy(raised, "no local hands controller is available yet");
                    return false;
                }

                RadioTransmitterController radio = hands as RadioTransmitterController;
                if (radio != null)
                {
                    if (_physicalProxyRaised != raised || radio.CurrentRadioTransmitterState != raised)
                    {
                        radio.SetAim(raised);
                    }

                    _physicalProxyRaised = raised;
                    _physicalProxyName = "radio-transmitter";
                    _loggedMissingPhysicalProxy = false;
                    return true;
                }

                if ((!_physicalProxyRaised || !raised) && TryInvokeBoolMethod(hands, "SetCompassState", raised))
                {
                    _physicalProxyRaised = raised;
                    _physicalProxyName = "compass";
                    _loggedMissingPhysicalProxy = false;
                    return true;
                }

                LogMissingProxy(
                    raised,
                    "current hands controller '" + hands.GetType().FullName +
                    "' exposes neither an active radio-transmitter proxy nor a usable SetCompassState(bool) path");
                return false;
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                LogProxyFailure(raised, inner.Message);
                return false;
            }
            catch (Exception ex)
            {
                LogProxyFailure(raised, ex.Message);
                return false;
            }
        }

        private static bool TryInvokeBoolMethod(object target, string methodName, bool value)
        {
            Type type = target.GetType();
            Type[] signature = { typeof(bool) };

            while (type != null)
            {
                MethodInfo method = type.GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null,
                    signature,
                    null);

                if (method != null)
                {
                    method.Invoke(target, new object[] { value });
                    return true;
                }

                type = type.BaseType;
            }

            return false;
        }

        private void LogMissingProxy(bool raised, string detail)
        {
            if (!raised || _loggedMissingPhysicalProxy)
            {
                return;
            }

            _loggedMissingPhysicalProxy = true;
            Plugin.Log.LogInfo(
                "SoulRecorder physical proxy not active: " + detail + ". Audio still works.");
        }

        private void LogProxyFailure(bool raised, string message)
        {
            if (!raised || _loggedMissingPhysicalProxy)
            {
                return;
            }

            _loggedMissingPhysicalProxy = true;
            Plugin.Log.LogWarning("SoulRecorder physical proxy failed: " + message);
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
