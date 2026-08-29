using System;
using System.Diagnostics;
using EFT;
using SoulPlayer.Library;
using UnityEngine;

namespace SoulPlayer.Recorder
{
    /// <summary>
    /// Recorder-specific interaction/audio state that remains independent of the
    /// native EFT hands controller used to present the held recorder.
    /// </summary>
    internal sealed class SoulRecorderInteractionController
    {
        private const float AudioPreparationTimeoutSeconds = 15f;
        private const int AudioPrimeSafetyFrames = 2;
        private SoulRecorderAudioPlayer _audioPlayer;
        private ISoulRecorderHandsView _handsView;
        private readonly SoulRecorderInteractionLifecycle _lifecycle =
            new SoulRecorderInteractionLifecycle();
        private readonly SoulRecorderInteractionCompletionGate _completionGate =
            new SoulRecorderInteractionCompletionGate();
        private Player _interactionPlayer;
        private MusicTrack _activeTape;
        private SoulRecorderState _state;
        private float _transitionAt;
        private float _presentationExitAt;
        private bool _exitCompletesStop;
        private bool _playRequested;
        private bool _audioPreparationRequested;
        private float _audioPrepareAt;
        private float _audioStopAt;
        private bool _insertionPresentationStarted;
        private float _preparationTimeoutAt;
        private int _audioReadyFrame = -1;
        private int _framesWaitedAfterPrime;
        private long _prepareRequestedTimestamp;
        private double _prepareRequestToReadyMs;

        internal event Action<MusicTrack> TapeInsertionStarted;
        internal event Action<MusicTrack> TapeInserted;
        internal event Action<MusicTrack> TapeEjectionStarted;
        internal event Action<MusicTrack> TapeEjected;
        internal event Action<SoulRecorderState, SoulRecorderState> StateChanged;
        internal event Action PresentationOwnershipReleaseRequested;

        internal SoulRecorderState RecorderState { get { return _state; } }
        internal bool IsInteractionActive { get { return _state != SoulRecorderState.Idle; } }
        internal bool IsPresentationActive { get { return _lifecycle.IsPresentationVisible; } }
        internal SoulRecorderPlaybackState PlaybackState { get { return _lifecycle.PlaybackState; } }
        internal SoulRecorderInteractionPhase PresentationPhase { get { return _lifecycle.Phase; } }
        internal MusicTrack ActiveTape { get { return _activeTape; } }
        internal bool IsInsertionPresentationStarted
        {
            get { return _insertionPresentationStarted; }
        }

        internal void Bind(
            SoulRecorderAudioPlayer audioPlayer,
            ISoulRecorderHandsView handsView)
        {
            _audioPlayer = audioPlayer;
            _handsView = handsView ?? HeadlessSoulRecorderHandsView.Instance;
            _state = SoulRecorderState.Idle;
            _lifecycle.ForceCleanup();
            _lifecycle.TakeHandsRestoreRequest();
        }

        internal bool EnterInteraction(Player player, MusicTrack tape)
        {
            if (_state != SoulRecorderState.Idle || player == null ||
                !TrackRoutingService.AllowsDirectPlayback(tape) ||
                _audioPlayer == null || !_lifecycle.BeginStartInteraction())
            {
                return false;
            }

            _interactionPlayer = player;
            _activeTape = tape;
            _playRequested = false;
            _audioPreparationRequested = false;
            _insertionPresentationStarted = false;
            _audioReadyFrame = -1;
            _framesWaitedAfterPrime = 0;
            _prepareRequestedTimestamp = 0L;
            _prepareRequestToReadyMs = 0d;
            _completionGate.BeginInteraction();

            SetState(SoulRecorderState.LoadingTape);
            _transitionAt = 0f;
            _audioPrepareAt = Time.unscaledTime;
            _preparationTimeoutAt = Time.unscaledTime +
                AudioPreparationTimeoutSeconds;
            EnsureAudioPrepared();
            return true;
        }

        internal void ManualRecorderUpdate(float unscaledTime)
        {
            CompletePresentationExitIfReady(unscaledTime);
            if (_audioStopAt > 0f && unscaledTime >= _audioStopAt)
            {
                StopAudioAtEjectionContact();
            }

            switch (_state)
            {
                case SoulRecorderState.LoadingTape:
                    if (!_insertionPresentationStarted)
                    {
                        UpdatePreAnimationAudioGate(unscaledTime);
                        break;
                    }
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
                        FinishPlaybackNaturally();
                    }
                    break;

                case SoulRecorderState.Ejecting:
                    if (_completionGate.TryFinishEjectionMotion(unscaledTime))
                    {
                        _transitionAt = -1f;
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

            if (_state == SoulRecorderState.LoadingTape)
            {
                if (_insertionPresentationStarted)
                {
                    InterruptInsertion("insertion cancelled");
                }
                else
                {
                    AbortPreAnimationPreparation(
                        "pre-animation preparation cancelled");
                }
                return;
            }

            BeginStopInteraction();
        }

        internal bool BeginStopInteraction()
        {
            if (_state != SoulRecorderState.Playing && _state != SoulRecorderState.Ready)
            {
                return false;
            }

            if (_state == SoulRecorderState.Playing)
            {
                if (!_lifecycle.BeginStopInteraction())
                {
                    return false;
                }

                InvokeView(
                    () => _handsView.OnInteractionEntered(_interactionPlayer),
                    "re-enter stop interaction");
                InvokeView(() => _handsView.OnTapeInserted(_activeTape),
                    "restore seated tape presentation");
                InvokeView(() => _handsView.OnPlaybackChanged(true),
                    "restore playback presentation");
                _completionGate.BeginInteraction();
            }
            else if (!_lifecycle.BeginReadyEjection())
            {
                return false;
            }

            _playRequested = false;
            _audioPreparationRequested = false;
            _audioPrepareAt = 0f;
            float audioStopDelay = Mathf.Max(
                0f,
                _handsView.TapeEjectionAudioStopSeconds);
            if (audioStopDelay <= 0f)
            {
                StopAudioAtEjectionContact();
            }
            else
            {
                _audioStopAt = Time.unscaledTime + audioStopDelay;
            }
            _presentationExitAt = 0f;
            _exitCompletesStop = false;
            MusicTrack tape = _activeTape;
            SetState(SoulRecorderState.Ejecting);
            RaiseTapeHook(TapeEjectionStarted, tape);
            InvokeView(() => _handsView.OnTapeEjectionStarted(tape), "start tape ejection");
            _transitionAt = Time.unscaledTime + Mathf.Max(0.01f, _handsView.TapeEjectionSeconds);
            _completionGate.ArmEjectionMotion(_transitionAt);
            return true;
        }

        internal void InterruptInsertion(string reason)
        {
            if (_state != SoulRecorderState.LoadingTape && _state != SoulRecorderState.Ready)
            {
                return;
            }

            _audioPlayer.Stop();
            _audioStopAt = 0f;
            _playRequested = false;
            _audioPreparationRequested = false;
            _audioPrepareAt = 0f;
            _presentationExitAt = 0f;
            _exitCompletesStop = false;
            if (!_lifecycle.BeginInsertionCancellation())
            {
                CompleteInteractionAndRestoreHands(
                    "insertion cancellation rejected",
                    true,
                    true);
                return;
            }

            MusicTrack tape = _activeTape;
            SetState(SoulRecorderState.Ejecting);
            RaiseTapeHook(TapeEjectionStarted, tape);
            InvokeView(
                () => _handsView.OnTapeEjectionStarted(tape),
                "reverse interrupted tape insertion");
            _transitionAt = Time.unscaledTime +
                Mathf.Max(0.01f, _handsView.TapeEjectionSeconds);
            _completionGate.ArmEjectionMotion(_transitionAt);
            Plugin.Log.LogInfo(
                "SoulRecorder insertion cancellation is reversing the cassette (" +
                reason + ").");
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
            _audioPreparationRequested = false;
            _audioPrepareAt = 0f;
            _audioStopAt = 0f;
            _insertionPresentationStarted = false;
            _preparationTimeoutAt = 0f;
            _audioReadyFrame = -1;
            _framesWaitedAfterPrime = 0;
            _prepareRequestedTimestamp = 0L;
            _prepareRequestToReadyMs = 0d;
            _transitionAt = 0f;
            _presentationExitAt = 0f;
            _exitCompletesStop = false;
            _activeTape = null;
            _interactionPlayer = null;
            _lifecycle.ForceCleanup();
            SetState(SoulRecorderState.Idle);
            CompleteInteractionAndRestoreHands(reason, false, true);

            if (hadInteraction)
            {
                Plugin.Log.LogInfo("SoulRecorder interaction reset (" + reason + ").");
            }
        }

        private void UpdatePreAnimationAudioGate(float unscaledTime)
        {
            if (_audioPlayer.IsPrepared)
            {
                if (_audioReadyFrame < 0)
                {
                    _audioReadyFrame = Time.frameCount;
                    _prepareRequestToReadyMs = _prepareRequestedTimestamp <= 0
                        ? 0d
                        : ElapsedMilliseconds(_prepareRequestedTimestamp);
                    Plugin.Log.LogInfo(
                        "SoulRecorder audio is fully primed after " +
                        _prepareRequestToReadyMs.ToString("0.0") +
                        " ms; waiting two safety frames before presentation.");
                    return;
                }

                _framesWaitedAfterPrime = Math.Max(
                    0, Time.frameCount - _audioReadyFrame);
                if (_framesWaitedAfterPrime >= AudioPrimeSafetyFrames)
                {
                    StartInsertionPresentation(unscaledTime);
                }
                return;
            }

            if (!_audioPlayer.IsLoading &&
                !string.IsNullOrEmpty(_audioPlayer.LastError))
            {
                AbortPreAnimationPreparation(
                    "audio preparation failed: " + _audioPlayer.LastError);
                return;
            }

            if (unscaledTime >= _preparationTimeoutAt)
            {
                AbortPreAnimationPreparation(
                    "audio preparation timed out after " +
                    AudioPreparationTimeoutSeconds.ToString("0") + " seconds");
            }
        }

        private void StartInsertionPresentation(float unscaledTime)
        {
            if (_insertionPresentationStarted || !_audioPlayer.IsPrepared)
            {
                return;
            }

            _insertionPresentationStarted = true;
            _audioPlayer.RecordAnimationGateTiming(
                _prepareRequestToReadyMs,
                _framesWaitedAfterPrime,
                true);
            InvokeView(() => _handsView.OnInteractionEntered(_interactionPlayer),
                "enter prepared interaction");
            RaiseTapeHook(TapeInsertionStarted, _activeTape);
            InvokeView(() => _handsView.OnTapeInsertionStarted(_activeTape),
                "start prepared tape insertion");
            _transitionAt = unscaledTime + Mathf.Max(
                0.01f, _handsView.TapeInsertionSeconds);
        }

        private void AbortPreAnimationPreparation(string reason)
        {
            if (_state != SoulRecorderState.LoadingTape ||
                _insertionPresentationStarted)
            {
                return;
            }

            if (_audioPlayer != null)
            {
                _audioPlayer.Stop();
            }
            Plugin.Log.LogWarning(
                "SoulRecorder did not start its hidden preparation interaction: " +
                reason + ".");
            _lifecycle.ForceCleanup();
            CompleteInteractionAndRestoreHands(reason, true, true);
        }

        private void CompleteTapeInsertion()
        {
            long seatStarted = Stopwatch.GetTimestamp();
            EnsureAudioPrepared();
            MusicTrack tape = _activeTape;

            long operationStarted = Stopwatch.GetTimestamp();
            InvokeView(() => _handsView.OnTapeInserted(tape), "complete tape insertion");
            double uiCallbacksMs = ElapsedMilliseconds(operationStarted);

            operationStarted = Stopwatch.GetTimestamp();
            RaiseTapeHook(TapeInserted, tape);
            double eventCallbacksMs = ElapsedMilliseconds(operationStarted);

            operationStarted = Stopwatch.GetTimestamp();
            SetState(SoulRecorderState.Ready);
            _lifecycle.MarkReady();
            double stateMs = ElapsedMilliseconds(operationStarted);

            operationStarted = Stopwatch.GetTimestamp();
            RequestPlayback();
            double playbackRequestMs = ElapsedMilliseconds(operationStarted);
            _audioPlayer.RecordSeatTiming(
                uiCallbacksMs,
                eventCallbacksMs,
                stateMs,
                playbackRequestMs,
                ElapsedMilliseconds(seatStarted));
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
                _lifecycle.MarkPlaybackStarted();
                SetState(SoulRecorderState.Playing);
                BeginPresentationExit(false);
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
            _audioPlayer.PlayPrepared(_activeTape);
        }

        private void EnsureAudioPrepared()
        {
            if (_audioPreparationRequested || _activeTape == null ||
                _audioPlayer == null)
            {
                return;
            }

            _audioPreparationRequested = true;
            _prepareRequestedTimestamp = Stopwatch.GetTimestamp();
            _audioPlayer.Prepare(_activeTape);
            Plugin.Log.LogInfo(
                "SoulRecorder AUDIO PREPARATION started before cassette seating.");
        }

        private void StopPlayback()
        {
            if (_state != SoulRecorderState.Playing && !_playRequested)
            {
                return;
            }

            ForceReset("usable-item stop action");
        }

        private void CompleteTapeEjection()
        {
            StopAudioAtEjectionContact();
            MusicTrack tape = _activeTape;
            InvokeView(() => _handsView.OnTapeEjected(tape), "complete tape ejection");
            RaiseTapeHook(TapeEjected, tape);
            _lifecycle.BeginExitAfterStop();
            BeginPresentationExit(true);
        }

        private void StopAudioAtEjectionContact()
        {
            if (_audioStopAt <= 0f &&
                (_audioPlayer == null || (!_audioPlayer.IsPlaying && !_audioPlayer.IsLoading)))
            {
                return;
            }
            _audioStopAt = 0f;
            if (_audioPlayer != null)
            {
                _audioPlayer.Stop();
            }
            if (_handsView != null)
            {
                InvokeView(() => _handsView.OnPlaybackChanged(false),
                    "stop playback at cassette ejection contact");
            }
        }

        private void BeginPresentationExit(bool completesStop)
        {
            if (!_completionGate.IsInteractionPending || _presentationExitAt > 0f)
            {
                return;
            }

            _exitCompletesStop = completesStop;
            InvokeView(() => _handsView.OnInteractionExited(), "exit interaction");
            _presentationExitAt = Time.unscaledTime +
                Mathf.Max(0.01f, _handsView.InteractionExitSeconds);
        }

        private void CompletePresentationExitIfReady(float unscaledTime)
        {
            if (_presentationExitAt <= 0f || unscaledTime < _presentationExitAt)
            {
                return;
            }

            _presentationExitAt = 0f;
            CompleteInteractionAndRestoreHands(
                _exitCompletesStop
                    ? "ejection presentation exited"
                    : "insertion presentation exited",
                _exitCompletesStop,
                true);
        }

        private void FinishPlaybackNaturally()
        {
            _audioPlayer.Stop();
            _playRequested = false;
            InvokeView(() => _handsView.OnPlaybackChanged(false),
                "stop completed playback animation");
            InvokeView(() => _handsView.ForceReset(), "finish completed playback");
            _presentationExitAt = 0f;
            _exitCompletesStop = false;
            _lifecycle.PlaybackFinished();
            _activeTape = null;
            _interactionPlayer = null;
            SetState(SoulRecorderState.Idle);
            CompleteInteractionAndRestoreHands(
                "playback completed",
                false,
                true);
            Plugin.Log.LogInfo("SoulRecorder tape finished; playback and interaction are idle.");
        }

        private void CompleteInteractionAndRestoreHands(
            string reason,
            bool completesStop,
            bool resetPresentation)
        {
            if (!_completionGate.TryReleaseInteraction())
            {
                return;
            }
            _transitionAt = 0f;
            _presentationExitAt = 0f;
            _exitCompletesStop = false;

            if (resetPresentation)
            {
                InvokeView(() => _handsView.ForceReset(),
                    "complete and release interaction");
            }

            _lifecycle.CompletePresentationExit();
            if (completesStop)
            {
                _activeTape = null;
                _interactionPlayer = null;
                SetState(SoulRecorderState.Idle);
            }

            RequestHandsRestore();
            Plugin.Log.LogInfo(
                "SoulRecorder interaction completion requested once (" + reason + ").");
        }

        private void RequestHandsRestore()
        {
            if (!_lifecycle.TakeHandsRestoreRequest())
            {
                return;
            }

            Action handler = PresentationOwnershipReleaseRequested;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning(
                    "SoulRecorder hands restoration hook failed: " + ex.Message);
            }
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

        private static double ElapsedMilliseconds(long startedAt)
        {
            return (Stopwatch.GetTimestamp() - startedAt) *
                1000d / Stopwatch.Frequency;
        }
    }

    internal static class SoulRecorderAudioPreparationSchedule
    {
        internal static float Calculate(float insertionStartedAt,
            float insertionSeconds, float preparationLeadSeconds)
        {
            float duration = Mathf.Max(0.01f, insertionSeconds);
            float lead = Mathf.Clamp(preparationLeadSeconds, 0f, duration);
            return insertionStartedAt + duration - lead;
        }
    }
}
