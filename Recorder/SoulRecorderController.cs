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

            if (Plugin.MusicLibrary != null)
            {
                Plugin.MusicLibrary.Changed += OnLibraryChanged;
            }

            Plugin.Log.LogInfo(
                "SoulRecorder usable-item controller ready: M enters/exits the recorder interaction; " +
                "starter cassette is " + PreferredStarterArtist + " - " + PreferredStarterTitle + ".");
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

            MusicTrack track = ResolveUnlockedTape();
            if (track == null)
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
            if (!_usableItemController.EnterInteraction(player, track))
            {
                Plugin.Log.LogWarning("SoulRecorder could not enter from state " + State + ".");
            }
        }

        private void ResetRecorder(string reason)
        {
            _pendingEnter = false;
            if (_usableItemController != null)
            {
                _usableItemController.ForceReset(reason);
            }
            else if (_audioPlayer != null)
            {
                _audioPlayer.Stop();
            }
        }

        private MusicTrack ResolveUnlockedTape()
        {
            if (Plugin.TapeCollection == null || !Plugin.TapeCollection.IsLoaded)
            {
                return null;
            }

            SoulTapeCatalogEntry selected = SoulTapeRecorderSelector.Select(Plugin.TapeCollection);
            if (selected == null)
            {
                return null;
            }

            if (!string.Equals(
                    selected.Id,
                    SoulTapeCatalog.StarterTapeId,
                    StringComparison.Ordinal))
            {
                Plugin.Log.LogInfo(
                    "Unlocked starter cassette '" + PreferredStarterArtist + " - " +
                    PreferredStarterTitle + "' has no available audio; using unlocked cassette " +
                    selected.Artist + " - " + selected.Title + ".");
            }

            return selected.Track;
        }

        private static ISoulRecorderHandsView CreateHandsView()
        {
#if SOULPLAYER_RECORDER_DEV_PROXY
            Plugin.Log.LogWarning(
                "SoulRecorder development radio/compass hands proxy is enabled for this build.");
            return new DevelopmentRecorderHandsProxy();
#else
            return HeadlessSoulRecorderHandsView.Instance;
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

            ResetRecorder("component destroyed");
        }
    }
}
