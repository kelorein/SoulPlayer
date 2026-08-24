using System;
using EFT;
using SoulPlayer.Library;
using UnityEngine;

namespace SoulPlayer.Recorder
{
    /// <summary>
    /// Recorder-specific layer built on SPT's usable-item controller contract.
    ///
    /// It deliberately does not manufacture an EFT inventory item or borrow a
    /// live-EFT prefab. The same controller can be connected to a custom
    /// first-person prefab later through ISoulRecorderHandsView while the raid
    /// interaction and audio state remain unchanged.
    /// </summary>
    internal sealed class SoulRecorderUsableItemController : Player.UsableItemController
    {
        private SoulRecorderAudioPlayer _audioPlayer;
        private ISoulRecorderHandsView _handsView;
        private Player _interactionPlayer;
        private MusicTrack _activeTape;
        private SoulRecorderState _state;
        private float _transitionAt;
        private bool _playRequested;

        internal event Action<MusicTrack> TapeInsertionStarted;
        internal event Action<MusicTrack> TapeInserted;
        internal event Action<MusicTrack> TapeEjectionStarted;
        internal event Action<MusicTrack> TapeEjected;
        internal event Action<SoulRecorderState, SoulRecorderState> StateChanged;

        internal SoulRecorderState RecorderState { get { return _state; } }
        internal bool IsInteractionActive { get { return _state != SoulRecorderState.Idle; } }
        internal MusicTrack ActiveTape { get { return _activeTape; } }

        internal void Bind(
            SoulRecorderAudioPlayer audioPlayer,
            ISoulRecorderHandsView handsView)
        {
            _audioPlayer = audioPlayer;
            _handsView = handsView ?? HeadlessSoulRecorderHandsView.Instance;
            _state = SoulRecorderState.Idle;
        }

        internal bool EnterInteraction(Player player, MusicTrack tape)
        {
            if (_state != SoulRecorderState.Idle || player == null || tape == null || _audioPlayer == null)
            {
                return false;
            }

            _interactionPlayer = player;
            _activeTape = tape;
            _playRequested = false;

            InvokeView(() => _handsView.OnInteractionEntered(player), "enter interaction");
            SetState(SoulRecorderState.LoadingTape);
            RaiseTapeHook(TapeInsertionStarted, tape);
            InvokeView(() => _handsView.OnTapeInsertionStarted(tape), "start tape insertion");
            _transitionAt = Time.unscaledTime + Mathf.Max(0.01f, _handsView.TapeInsertionSeconds);
            return true;
        }

        internal void ManualRecorderUpdate(float unscaledTime)
        {
            switch (_state)
            {
                case SoulRecorderState.LoadingTape:
                    if (unscaledTime >= _transitionAt)
                    {
                        CompleteTapeInsertion();
                    }
                    break;

                case SoulRecorderState.Ready:
                    UpdateReadyState();
                    break;

                case SoulRecorderState.Playing:
                    if (!_audioPlayer.IsLoading && !_audioPlayer.IsPlaying)
                    {
                        _audioPlayer.Stop();
                        _playRequested = false;
                        InvokeView(() => _handsView.OnPlaybackChanged(false), "stop playback animation");
                        SetState(SoulRecorderState.Ready);
                        Plugin.Log.LogInfo("SoulRecorder tape finished; interaction remains ready for replay or ejection.");
                    }
                    break;

                case SoulRecorderState.Ejecting:
                    if (unscaledTime >= _transitionAt)
                    {
                        CompleteTapeEjection();
                    }
                    break;
            }
        }

        internal void ExitInteraction()
        {
            if (_state == SoulRecorderState.Idle || _state == SoulRecorderState.Ejecting)
            {
                return;
            }

            bool wasPlaying = _state == SoulRecorderState.Playing || _audioPlayer.IsPlaying;
            _audioPlayer.Stop();
            _playRequested = false;

            if (wasPlaying)
            {
                InvokeView(() => _handsView.OnPlaybackChanged(false), "stop playback animation");
            }

            MusicTrack tape = _activeTape;
            SetState(SoulRecorderState.Ejecting);
            RaiseTapeHook(TapeEjectionStarted, tape);
            InvokeView(() => _handsView.OnTapeEjectionStarted(tape), "start tape ejection");
            _transitionAt = Time.unscaledTime + Mathf.Max(0.01f, _handsView.TapeEjectionSeconds);
        }

        internal void ForceReset(string reason)
        {
            bool hadInteraction = _state != SoulRecorderState.Idle;
            bool wasPlaying = _state == SoulRecorderState.Playing ||
                              (_audioPlayer != null && _audioPlayer.IsPlaying);

            if (_audioPlayer != null)
            {
                _audioPlayer.Stop();
            }

            if (wasPlaying && _handsView != null)
            {
                InvokeView(() => _handsView.OnPlaybackChanged(false), "reset playback animation");
            }

            if (hadInteraction && _handsView != null)
            {
                InvokeView(() => _handsView.ForceReset(), "reset interaction");
            }

            _playRequested = false;
            _transitionAt = 0f;
            _activeTape = null;
            _interactionPlayer = null;
            SetState(SoulRecorderState.Idle);

            if (hadInteraction)
            {
                Plugin.Log.LogInfo("SoulRecorder interaction reset (" + reason + ").");
            }
        }

        public override bool ExamineWeapon()
        {
            return false;
        }

        public override void ToggleAim()
        {
            SetAim(_state != SoulRecorderState.Playing && !_playRequested);
        }

        /// <summary>
        /// SPT's usable-item primary-action seam. A future physical recorder hands
        /// controller can route its play/stop animation event here.
        /// </summary>
        public override void SetAim(bool value)
        {
            if (value)
            {
                RequestPlayback();
                return;
            }

            StopPlayback();
        }

        /// <summary>
        /// SPT calls Hide when a usable item is put away. For SoulRecorder this is
        /// the same interaction exit/eject path used by M.
        /// </summary>
        public override void Hide()
        {
            ExitInteraction();
        }

        private void CompleteTapeInsertion()
        {
            MusicTrack tape = _activeTape;
            InvokeView(() => _handsView.OnTapeInserted(tape), "complete tape insertion");
            RaiseTapeHook(TapeInserted, tape);
            SetState(SoulRecorderState.Ready);
            RequestPlayback();
        }

        private void UpdateReadyState()
        {
            if (!_playRequested)
            {
                return;
            }

            if (_audioPlayer.IsPlaying)
            {
                InvokeView(() => _handsView.OnPlaybackChanged(true), "start playback animation");
                SetState(SoulRecorderState.Playing);
                return;
            }

            if (_audioPlayer.IsLoading)
            {
                return;
            }

            string error = _audioPlayer.LastError;
            _audioPlayer.Stop();
            _playRequested = false;

            if (string.IsNullOrEmpty(error))
            {
                error = "audio source did not start";
            }

            Plugin.Log.LogWarning("SoulRecorder remains ready after playback failed: " + error);
        }

        private void RequestPlayback()
        {
            if (_state != SoulRecorderState.Ready || _activeTape == null || _playRequested)
            {
                return;
            }

            _playRequested = true;
            _audioPlayer.Play(_activeTape);
            Plugin.Log.LogInfo(
                "SoulRecorder PLAY REQUEST -> " + _activeTape.Artist + " - " + _activeTape.Title);
        }

        private void StopPlayback()
        {
            if (_state != SoulRecorderState.Playing && !_playRequested)
            {
                return;
            }

            bool wasPlaying = _state == SoulRecorderState.Playing || _audioPlayer.IsPlaying;
            _audioPlayer.Stop();
            _playRequested = false;

            if (wasPlaying)
            {
                InvokeView(() => _handsView.OnPlaybackChanged(false), "stop playback animation");
            }

            if (_state == SoulRecorderState.Playing)
            {
                SetState(SoulRecorderState.Ready);
            }
        }

        private void CompleteTapeEjection()
        {
            MusicTrack tape = _activeTape;
            InvokeView(() => _handsView.OnTapeEjected(tape), "complete tape ejection");
            RaiseTapeHook(TapeEjected, tape);
            InvokeView(() => _handsView.OnInteractionExited(), "exit interaction");

            _activeTape = null;
            _interactionPlayer = null;
            _transitionAt = 0f;
            SetState(SoulRecorderState.Idle);
        }

        private void SetState(SoulRecorderState next)
        {
            if (_state == next)
            {
                return;
            }

            SoulRecorderState previous = _state;
            _state = next;

            Action<SoulRecorderState, SoulRecorderState> handler = StateChanged;
            if (handler != null)
            {
                try
                {
                    handler(previous, next);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("SoulRecorder state hook failed: " + ex.Message);
                }
            }

            Plugin.Log.LogInfo("SoulRecorder STATE " + previous + " -> " + next + ".");
        }

        private static void RaiseTapeHook(Action<MusicTrack> hook, MusicTrack tape)
        {
            if (hook == null)
            {
                return;
            }

            try
            {
                hook(tape);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("SoulRecorder cassette hook failed: " + ex.Message);
            }
        }

        private static void InvokeView(Action action, string operation)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning(
                    "SoulRecorder hands view could not " + operation + ": " + ex.Message);
            }
        }
    }
}
