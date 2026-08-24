using System;
using Comfort.Common;
using EFT;
using SoulPlayer.Cassettes;
using SoulPlayer.Configuration;
using SoulPlayer.Library;
using SoulPlayer.Utils;
using UnityEngine;

namespace SoulPlayer.Recorder
{
    /// <summary>
    /// Raid-level input/lifetime host for the SoulRecorder usable-item controller.
    /// M enters or exits the recorder interaction; tape transport lives on the
    /// usable-item controller and no longer borrows compass state.
    /// </summary>
    internal sealed class SoulRecorderController : MonoBehaviour
    {
        private const KeyCode RecorderHotkey = KeyCode.M;
        private const string PreferredStarterArtist = "Scott Buckley";
        private const string PreferredStarterTitle = "The Long Dark";

        private SoulPlayerSettings _settings;
        private SoulRecorderAudioPlayer _audioPlayer;
        private SoulRecorderUsableItemController _usableItemController;
        private bool _pendingEnter;
        private bool _wasInRaid;
        private string _feedbackHeading = string.Empty;
        private string _feedbackTrack = string.Empty;
        private string _feedbackStatus = string.Empty;
        private float _feedbackUntil;
        private GUIStyle _feedbackHeadingStyle;
        private GUIStyle _feedbackTrackStyle;
        private GUIStyle _feedbackStatusStyle;

        internal bool IsActive
        {
            get
            {
                return _pendingEnter ||
                       (_usableItemController != null && _usableItemController.IsInteractionActive);
            }
        }

        internal SoulRecorderState State
        {
            get
            {
                return _usableItemController == null
                    ? SoulRecorderState.Idle
                    : _usableItemController.RecorderState;
            }
        }

        internal MusicTrack ActiveTape
        {
            get
            {
                return _usableItemController == null
                    ? null
                    : _usableItemController.ActiveTape;
            }
        }

        internal void Initialize(SoulPlayerSettings settings)
        {
            _settings = settings;
            _audioPlayer = gameObject.AddComponent<SoulRecorderAudioPlayer>();
            _audioPlayer.Initialize(settings);

            _usableItemController = new SoulRecorderUsableItemController();
            _usableItemController.Bind(_audioPlayer, CreateHandsView());
            _usableItemController.StateChanged += OnRecorderStateChanged;

            if (Plugin.MusicLibrary != null)
            {
                Plugin.MusicLibrary.Changed += OnLibraryChanged;
            }

            Plugin.Log.LogInfo(
                "SoulRecorder usable-item controller ready: M enters/exits the recorder interaction; " +
                "the selected cassette is preferred and the no-selection default is " +
                PreferredStarterArtist + " - " + PreferredStarterTitle + ".");
        }

        private void Update()
        {
            bool inRaid = GameState.IsInRaid();
            if (!inRaid)
            {
                if (_wasInRaid || IsActive)
                {
                    ResetRecorder("raid ended");
                }

                _wasInRaid = false;
                return;
            }

            _wasInRaid = true;

            if (Input.GetKeyDown(RecorderHotkey))
            {
                ToggleInteraction();
            }

            _usableItemController.ManualRecorderUpdate(Time.unscaledTime);
        }

        private void ToggleInteraction()
        {
            if (_pendingEnter)
            {
                _pendingEnter = false;
                Plugin.Log.LogInfo("SoulRecorder pending interaction cancelled (M pressed).");
                return;
            }

            if (_usableItemController.RecorderState == SoulRecorderState.Idle)
            {
                EnterInteraction();
                return;
            }

            if (_usableItemController.RecorderState != SoulRecorderState.Ejecting)
            {
                _usableItemController.ExitInteraction();
            }
        }

        private void EnterInteraction()
        {
            if (!GameState.IsInRaid())
            {
                return;
            }

            GameWorld world = Singleton<GameWorld>.Instance;
            Player player = world == null ? null : world.MainPlayer;
            if (player == null)
            {
                Plugin.Log.LogInfo("SoulRecorder interaction is waiting for the local raid player.");
                return;
            }

            if (Plugin.TapeCollectionHost == null ||
                !Plugin.TapeCollectionHost.EnsureProfile(player))
            {
                Plugin.Log.LogWarning(
                    "SoulRecorder cannot enter until the profile cassette collection is loaded.");
                return;
            }

            SoulTapeCatalogEntry tape = ResolveUnlockedTape();
            if (tape == null || tape.Track == null)
            {
                if (Plugin.MusicLibrary != null && Plugin.MusicLibrary.IsScanning)
                {
                    _pendingEnter = true;
                    Plugin.Log.LogInfo(
                        "SoulRecorder is waiting for the music library scan before entering the interaction.");
                }
                else
                {
                    Plugin.Log.LogWarning(
                        "SoulRecorder found no unlocked cassette with available audio.");
                }
                return;
            }

            _pendingEnter = false;
            if (!_usableItemController.EnterInteraction(player, tape.Track))
            {
                Plugin.Log.LogWarning("SoulRecorder could not enter from state " + State + ".");
                return;
            }

            ShowFeedback(
                "SOULRECORDER",
                tape.Artist + " — " + tape.Title,
                "LOADING CASSETTE...",
                3f);
        }

        private void ResetRecorder(string reason)
        {
            _pendingEnter = false;
            _feedbackUntil = 0f;
            if (_usableItemController != null)
            {
                _usableItemController.ForceReset(reason);
            }
            else if (_audioPlayer != null)
            {
                _audioPlayer.Stop();
            }
        }

        private SoulTapeCatalogEntry ResolveUnlockedTape()
        {
            if (Plugin.TapeCollection == null || !Plugin.TapeCollection.IsLoaded)
            {
                return null;
            }

            SoulTapeRecorderSelectionResult resolution =
                SoulTapeRecorderSelector.Resolve(Plugin.TapeCollection);
            SoulTapeCatalogEntry selected = resolution.Tape;
            if (selected == null)
            {
                if (resolution.HasExplicitSelection)
                {
                    Plugin.Log.LogWarning(
                        "SoulRecorder selected cassette " + resolution.ExplicitCassetteId +
                        " has no available audio and no unlocked runtime fallback is available; " +
                        "the saved selection was preserved.");
                }
                return null;
            }

            if (resolution.UsedRuntimeFallback)
            {
                Plugin.Log.LogWarning(
                    "SoulRecorder selected cassette " + resolution.ExplicitCassetteId +
                    " has no available audio; using " + selected.Artist + " - " +
                    selected.Title + " for this interaction only. The saved selection was preserved.");
            }
            else if (resolution.HasExplicitSelection)
            {
                Plugin.Log.LogInfo(
                    "SoulRecorder using selected cassette " + selected.Artist + " - " +
                    selected.Title + ".");
            }
            else if (!string.Equals(
                    selected.Id,
                    SoulTapeCatalog.StarterTapeId,
                    StringComparison.Ordinal))
            {
                Plugin.Log.LogInfo(
                    "Unlocked starter cassette '" + PreferredStarterArtist + " - " +
                    PreferredStarterTitle + "' has no available audio; using unlocked cassette " +
                    selected.Artist + " - " + selected.Title + ".");
            }

            return selected;
        }

        private void OnRecorderStateChanged(
            SoulRecorderState previous,
            SoulRecorderState next)
        {
            if (next == SoulRecorderState.Playing && ActiveTape != null)
            {
                ShowFeedback(
                    "NOW PLAYING",
                    ActiveTape.Artist + " — " + ActiveTape.Title,
                    string.Empty,
                    2.5f);
            }
            else if (next == SoulRecorderState.Ejecting || next == SoulRecorderState.Idle)
            {
                _feedbackUntil = 0f;
            }
        }

        private void ShowFeedback(
            string heading,
            string track,
            string status,
            float duration)
        {
            _feedbackHeading = heading ?? string.Empty;
            _feedbackTrack = track ?? string.Empty;
            _feedbackStatus = status ?? string.Empty;
            _feedbackUntil = Time.unscaledTime + Mathf.Max(0.1f, duration);
        }

        private void OnGUI()
        {
            if (_feedbackUntil <= Time.unscaledTime || !GameState.IsInRaid())
            {
                return;
            }

            EnsureFeedbackStyles();
            const float width = 430f;
            const float height = 104f;
            Rect panel = new Rect(
                (Screen.width - width) * 0.5f,
                Screen.height * 0.70f,
                width,
                height);
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.02f, 0.025f, 0.027f, 0.92f);
            GUI.Box(panel, GUIContent.none);
            GUI.backgroundColor = previousBackground;
            GUI.Label(
                new Rect(panel.x + 18f, panel.y + 10f, width - 36f, 24f),
                _feedbackHeading,
                _feedbackHeadingStyle);
            GUI.Label(
                new Rect(panel.x + 18f, panel.y + 36f, width - 36f, 28f),
                _feedbackTrack,
                _feedbackTrackStyle);
            if (!string.IsNullOrEmpty(_feedbackStatus))
            {
                GUI.Label(
                    new Rect(panel.x + 18f, panel.y + 70f, width - 36f, 20f),
                    _feedbackStatus,
                    _feedbackStatusStyle);
            }
        }

        private void EnsureFeedbackStyles()
        {
            if (_feedbackHeadingStyle != null)
            {
                return;
            }

            _feedbackHeadingStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
            _feedbackHeadingStyle.normal.textColor = new Color(0.54f, 0.67f, 0.64f, 1f);
            _feedbackTrackStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 17,
                fontStyle = FontStyle.Normal
            };
            _feedbackTrackStyle.normal.textColor = Color.white;
            _feedbackStatusStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Normal
            };
            _feedbackStatusStyle.normal.textColor = new Color(0.68f, 0.72f, 0.72f, 1f);
        }

        private ISoulRecorderHandsView CreateHandsView()
        {
#if SOULPLAYER_RECORDER_DEV_PROXY
            Plugin.Log.LogWarning(
                "SoulRecorder development radio/compass hands proxy is enabled for this build.");
            return new DevelopmentRecorderHandsProxy();
#else
            try
            {
                return gameObject.AddComponent<ProceduralSoulRecorderHandsView>();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(
                    "SoulRecorder procedural presentation could not initialize; using the " +
                    "safe headless presentation: " + ex.Message);
                return HeadlessSoulRecorderHandsView.Instance;
            }
#endif
        }

        private void OnLibraryChanged()
        {
            if (_pendingEnter && GameState.IsInRaid())
            {
                EnterInteraction();
            }
        }

        private void OnDestroy()
        {
            if (Plugin.MusicLibrary != null)
            {
                Plugin.MusicLibrary.Changed -= OnLibraryChanged;
            }

            if (_usableItemController != null)
            {
                _usableItemController.StateChanged -= OnRecorderStateChanged;
            }

            ResetRecorder("component destroyed");
        }
    }
}
