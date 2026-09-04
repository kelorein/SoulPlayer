using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Configuration;
using EFT;
using SoulPlayer.Configuration;
using SoulPlayer.Library;
using SoulPlayer.Utils;
using UnityEngine;
using UnityEngine.Networking;

namespace SoulPlayer.Audio
{
    internal sealed class SoulAudioPlayer : MonoBehaviour
    {
        private readonly System.Random _random = new System.Random();
        // Outcome selection must not consume the suspended Main shuffle sequence.
        private readonly System.Random _outcomeRandom = new System.Random();
        private readonly RaidPlaybackSession _raidPlayback = new RaidPlaybackSession();
        private readonly PlaybackCompletionTracker _completionTracker = new PlaybackCompletionTracker();
        private readonly MenuPlaybackContinuity _menuContinuity = new MenuPlaybackContinuity();
        private readonly PostRaidPlaybackContinuity _postRaidContinuity = new PostRaidPlaybackContinuity();
        private SoulPlayerSettings _settings;
        private TrackRoutingService _routing;
        private SoulPlayerVolumeState _volumeState;
        private AudioSource _source;
        private List<MusicTrack> _queue = new List<MusicTrack>();
        private TrackRoute _queueRoute = TrackRoute.None;
        private int _queueIndex = -1;
        private Coroutine _loadCoroutine;
        private Task<DecodedAudio> _flacTask;
        private int _loadGeneration;
        private bool _paused;
        private bool _loading;
        private bool _hasStarted;
        private bool _stopped = true;
        private bool _deploymentWasActive;
        private bool _pausedForContext;
        private bool _playWhenLibraryReady;
        private string _lastError = string.Empty;
        private ExactTrackPlaybackRequest _loadingExactRequest;
        private PlaybackIntent _loadingIntent;
        private MainPlaybackSnapshot _loadingSnapshot;
        private MainPlaybackSnapshot _restoredQueueSnapshot;
        private AudioClip _deferredClip;
        private int _nextExactRequestId;
        private MusicLibrary _library;
        private IReadOnlyList<MusicTrack> _raidLibraryTracks = new List<MusicTrack>();
        private readonly RaidPlaybackStateDiagnostics _raidStateDiagnostics = new RaidPlaybackStateDiagnostics();
        private RaidMenuEvidence _menuEvidence;
        private RaidReadinessReason _readiness;
        private string _reevaluationTrigger;

        internal bool IsRaidPlaybackActive { get { return _raidPlayback.MainSuspended; } }
        private readonly RaidReadinessPollGate _readinessPoll = new RaidReadinessPollGate();
        internal bool NeedsRaidReadinessInspection { get { return _raidPlayback.MainSuspended && _raidPlayback.Outcome.HasValue; } }
        internal bool RaidMenuReady { get { return _raidPlayback.MenuReady; } }
        internal bool RaidAudioMayContinue { get { return RaidMenuReady ||
            _postRaidContinuity.CanContinue(_settings.KeepMusicPlayingAcrossMenus, _raidPlayback.Outcome.HasValue); } }
        internal bool RaidOverlayAllowed { get { return _postRaidContinuity.CanDisplay(
            _settings.KeepMusicPlayingAcrossMenus, _raidPlayback.Outcome.HasValue, RaidMenuReady); } }
        private bool QueueShuffle { get { return _restoredQueueSnapshot == null ? _settings.Shuffle : _restoredQueueSnapshot.Shuffle; } }
        private int QueueRepeat { get { return _restoredQueueSnapshot == null ? _settings.RepeatMode : _restoredQueueSnapshot.RepeatMode; } }

        internal event Action Changed;

        internal MusicTrack CurrentTrack { get; private set; }
        internal MusicTrack DisplayTrack
        {
            get { return CurrentTrack; }
        }
        internal bool IsLoading { get { return _loading; } }
        internal bool IsPlaying { get { return _source != null && _source.isPlaying; } }
        internal bool IsPaused { get { return _paused || _pausedForContext || (_raidPlayback.MainSuspended && !RaidAudioMayContinue); } }
        internal string LastError { get { return _lastError; } }
        internal float CurrentTime { get { return _source != null && _source.clip != null ? _source.time : 0f; } }
        internal float Duration { get { return _source != null && _source.clip != null ? _source.clip.length : 0f; } }

        internal void Initialize(SoulPlayerSettings settings, TrackRoutingService routing)
        {
            if (_settings != null)
            {
                _settings.VolumeChanged -= OnVolumeChanged;
                _settings.MenuContinuityChanged -= OnMenuContinuityChanged;
            }
            _settings = settings;
            _routing = routing;
            if (_library != null)
            {
                _library.Changed -= OnLibraryChanged;
                _library.ScanStateChanged -= OnLibraryScanStateChanged;
            }
            _library = Plugin.MusicLibrary;
            if (_library != null)
            {
                _raidLibraryTracks = _library.Tracks;
                _library.Changed += OnLibraryChanged;
                _library.ScanStateChanged += OnLibraryScanStateChanged;
            }
            _volumeState = new SoulPlayerVolumeState(settings.Volume);
            _settings.VolumeChanged += OnVolumeChanged;
            _settings.MenuContinuityChanged += OnMenuContinuityChanged;
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _source.ignoreListenerPause = true;
            _source.volume = _volumeState.TargetVolume;
        }

        internal void Play(MusicTrack track, IEnumerable<MusicTrack> queue)
        {
            if (track == null) return;
            if (!CancelSavedResume(RaidCancellationReason.ManualLibrarySelection)) return;
            PlayCore(track, queue, TrackRoute.None, PlaybackIntent.Manual);
        }

        private bool CanStart(PlaybackIntent intent)
        {
            if (_raidPlayback.MainSuspended) RefreshRaidReadiness();
            return _raidPlayback.CanStart(intent, message => Plugin.Log.LogWarning(message),
                _readiness == RaidReadinessReason.Ready);
        }

        internal void RequestRaidReevaluation(string trigger)
        {
            if (_raidPlayback.MainSuspended) { _reevaluationTrigger = trigger; _readinessPoll.Invalidate(); }
        }

        private void OnMenuContinuityChanged()
        {
            _menuContinuity.Reset();
            StableRaidMenuContext.Invalidate();
            RequestRaidReevaluation("MenuContinuitySettingChanged");
        }

        internal void LogMenuTransition(EFT.UI.Screens.EEftScreenType? from,
            EFT.UI.Screens.EEftScreenType to)
        {
            // Called only by screen events, never by Update or readiness polls.
            string action = IsPlaying ? "KeepPlaying" : IsPaused ? "Pause" : "Stop";
            string reason = _raidPlayback.MainSuspended ? "RaidLifecycle/" + _readiness :
                _stopped ? "StoppedOrIdle" : _paused ? "UserPause" :
                _loading ? "PendingPlayback" :
                _settings.KeepMusicPlayingAcrossMenus ? "ContinuousMainContext" : "LegacyMenuBehavior";
            Plugin.Log.LogInfo("SoulPlayer menu playback: from=" + (from.HasValue ? from.Value.ToString() : "Unknown") +
                " to=" + to + " track=" + (CurrentTrack == null ? "none" : TrackRoutingService.GetTrackId(CurrentTrack) + "/" + CurrentTrack.Title) +
                " time=" + CurrentTime.ToString("0.000", CultureInfo.InvariantCulture) +
                " action=" + action + " reason=" + reason +
                " startReady=" + (_readiness == RaidReadinessReason.Ready) +
                " continueAudio=" + RaidAudioMayContinue + " overlayAllowed=" + RaidOverlayAllowed + ".");
        }

        private void OnLibraryChanged()
        {
            _raidLibraryTracks = _library.Tracks;
            RequestRaidReevaluation("LibraryChanged");
        }

        private void OnLibraryScanStateChanged()
        {
            RequestRaidReevaluation(_library.IsReady ? "LibraryReady" : "LibraryScanStarted");
        }

        internal void RefreshRaidReadiness()
        {
            if (!_readinessPoll.ShouldInspect(NeedsRaidReadinessInspection, Time.frameCount)) return;
#if SOULPLAYER_PERF
            SoulPlayer.Utils.RecurringWorkProfiler.Begin(SoulPlayer.Utils.RecurringWorkArea.Readiness);
            try
            {
#endif
            _menuEvidence = Plugin.PostRaidCoordinator == null ? new RaidMenuEvidence() :
                Plugin.PostRaidCoordinator.ReadEvidence();
            _readiness = _menuContinuity.Evaluate(_menuEvidence, _settings.KeepMusicPlayingAcrossMenus,
                _raidPlayback.Outcome.HasValue, _library != null && _library.IsReady);
            // START readiness still gates dispatch/deferred decoders. A rescan or
            // transient result-screen teardown must not revoke an actual start.
            SetRaidMenuReady(_menuContinuity.Evaluate(_menuEvidence, _settings.KeepMusicPlayingAcrossMenus,
                _raidPlayback.Outcome.HasValue, true) == RaidReadinessReason.Ready);
            LogRaidState(_reevaluationTrigger ?? "LifecycleEvidence");
            _reevaluationTrigger = null;
        #if SOULPLAYER_PERF
            }
            finally { SoulPlayer.Utils.RecurringWorkProfiler.End(SoulPlayer.Utils.RecurringWorkArea.Readiness); }
#endif
        }

        private void LogRaidState(string trigger)
        {
            if (_raidPlayback.Phase == RaidPlaybackPhase.Idle || _library == null) return;
            string line = _raidStateDiagnostics.Observe(_raidPlayback, _menuEvidence,
                _library.IsReady, _library.Revision, _settings.AutoPlayAfterRaid,
                _raidLibraryTracks, _routing, trigger, _readiness);
            if (line != null) Plugin.Log.LogInfo(line);
        }

        private void PlayCore(MusicTrack track, IEnumerable<MusicTrack> queue,
            TrackRoute route, PlaybackIntent intent,
            ExactTrackPlaybackRequest exactRequest = null, MainPlaybackSnapshot snapshot = null)
        {
            if (track == null || !CanStart(intent)) return;
            _queue = queue == null ? new List<MusicTrack>() : queue.ToList();
            _queueRoute = route;
            _queueIndex = _queue.FindIndex(item => SoulPath.AreEquivalent(item.FilePath, track.FilePath));
            if (_queueIndex < 0) { _queue.Insert(0, track); _queueIndex = 0; }
            _restoredQueueSnapshot = snapshot;
            BeginLoad(track, intent, exactRequest, snapshot);
        }

        internal void TogglePause()
        {
            // Pause never invalidates the saved Main candidate or the routed lifetime.
            if (_loading || (_raidPlayback.MainSuspended &&
                (!RaidAudioMayContinue || _raidPlayback.Phase != RaidPlaybackPhase.Routed))) return;
            if (_source.clip == null)
            {
                if (_queue.Count == 0)
                {
                    if (!StartFromLibrary() && Plugin.MusicLibrary.IsScanning)
                        _playWhenLibraryReady = true;
                }
                else
                {
                    _queueIndex = Math.Max(0, _queueIndex);
                    BeginLoad(_queue[_queueIndex], PlaybackIntent.Manual);
                }
                return;
            }
            if (_source.isPlaying)
            {
                _source.Pause();
                _paused = true;
                _volumeState.MarkPaused();
            }
            else
            {
                PlaybackIntent intent = _raidPlayback.MainSuspended ? PlaybackIntent.Routed : PlaybackIntent.Manual;
                // Resuming an existing paused clip is continuation, not a new
                // post-raid dispatch. Never bypass readiness for an unstarted clip.
                if (!(_raidPlayback.MainSuspended && _hasStarted && RaidAudioMayContinue) && !CanStart(intent)) return;
                if (!_hasStarted)
                {
                    // A restored paused clip already has its saved position applied.
                    _source.Play();
                    _hasStarted = true;
                    _completionTracker.Started(_source.isPlaying);
                }
                else _source.UnPause();
                _paused = false;
                _pausedForContext = false;
                _stopped = false;
                _volumeState.MarkPlaying();
            }
            NotifyChanged();
        }

        internal void Stop()
        {
            if (!CancelSavedResume(RaidCancellationReason.Stop)) return;
            StopCore();
        }

        private void InvalidateLoad()
        {
            _loadGeneration++;
            if (_loadCoroutine != null) { StopCoroutine(_loadCoroutine); _loadCoroutine = null; }
            _flacTask = null;
            if (_deferredClip != null) { UnityEngine.Object.Destroy(_deferredClip); _deferredClip = null; }
            _loadingExactRequest = null;
            _loadingSnapshot = null;
            _loading = false;
        }

        private void StopCore()
        {
            InvalidateLoad();
            _completionTracker.Reset();
            _playWhenLibraryReady = false;
            _pausedForContext = false;
            _source.Stop();
            if (_source.clip != null) _source.time = 0f;
            _paused = true;
            _hasStarted = false;
            _stopped = true;
            _volumeState.MarkStopped();
            NotifyChanged();
        }

        internal void Next()
        {
            if (!CancelSavedResume(RaidCancellationReason.Next)) return;
            NextInQueue(PlaybackIntent.Manual);
        }

        private void NextInQueue(PlaybackIntent intent)
        {
            // Guard before refreshing the queue or consuming even one shuffle value.
            if (!CanStart(intent)) return;
            RefreshAutomaticQueue();
            if (_queue.Count == 0)
            {
                if (_queueRoute == TrackRoute.None) StartFromLibrary();
                else StopPlayback();
                return;
            }
            if (QueueShuffle && _queue.Count > 1)
            {
                int next;
                do { next = _random.Next(_queue.Count); } while (next == _queueIndex);
                _queueIndex = next;
            }
            else
            {
                _queueIndex++;
                if (_queueIndex >= _queue.Count)
                {
                    if (QueueRepeat == 1) _queueIndex = 0;
                    else { _queueIndex = _queue.Count - 1; StopPlayback(); return; }
                }
            }
            BeginLoad(_queue[_queueIndex], intent);
        }

        private void AdvanceAutomatically()
        {
            if (!CanStart(PlaybackIntent.AutomaticMain)) return;
            if (_queueRoute == TrackRoute.None) StartFromLibrary();
            else NextInQueue(PlaybackIntent.AutomaticMain);
        }

        private void RefreshAutomaticQueue()
        {
            if (_queueRoute == TrackRoute.None || _routing == null) return;
            List<MusicTrack> eligible = _routing.SelectEligible(Plugin.MusicLibrary.Tracks, _queueRoute);
            // Keep the captured ordering, dropping ineligible/removed entries and
            // appending newly available songs without advancing the cursor.
            List<MusicTrack> ordered = _queue.Select(saved => eligible.FirstOrDefault(track =>
                TrackRoutingService.GetTrackId(track) == TrackRoutingService.GetTrackId(saved) &&
                SoulPath.AreEquivalent(track.FilePath, saved.FilePath))).Where(track => track != null).Distinct().ToList();
            ordered.AddRange(eligible.Where(track => !ordered.Contains(track)));
            _queue = ordered;
            _queueIndex = _queue.FindIndex(track => CurrentTrack != null &&
                SoulPath.AreEquivalent(track.FilePath, CurrentTrack.FilePath));
        }

        internal void Previous()
        {
            if (!CancelSavedResume(RaidCancellationReason.Previous)) return;
            if (!CanStart(PlaybackIntent.Manual)) return;
            if (_source.clip != null && _source.time > 4f)
            {
                _source.time = 0f;
                _source.Play();
                _paused = false;
                _stopped = false;
                _hasStarted = true;
                _completionTracker.Started(_source.isPlaying);
                NotifyChanged();
                return;
            }
            if (_queue.Count == 0) return;
            _queueIndex = Math.Max(0, _queueIndex - 1);
            BeginLoad(_queue[_queueIndex], PlaybackIntent.Manual);
        }

        internal void SetVolume(float volume)
        {
            _settings.Volume = volume;
        }

        private void OnVolumeChanged(float volume)
        {
            _volumeState.UpdateTarget(volume);
            ApplyTargetVolume();
            NotifyChanged();
        }

        private void ApplyTargetVolume()
        {
            if (_source != null) _source.volume = _volumeState.TargetVolume;
        }

        internal void SetProgress(float normalized)
        {
            if (_source.clip == null || (_raidPlayback.MainSuspended && !_raidPlayback.MenuReady)) return;
            _source.time = Mathf.Clamp01(normalized) * _source.clip.length;
            NotifyChanged();
        }

        internal void BeginRaidSuspension()
        {
            if (_source == null || (_raidPlayback.MainSuspended && !_raidPlayback.HasReturnedToMenu)) return;
            _menuContinuity.Reset();
            _postRaidContinuity.Reset();
            bool carryPendingMain = _raidPlayback.MainSuspended;
            MainPlaybackSnapshot snapshot = carryPendingMain || _source.clip == null || _stopped ||
                (_queueRoute != TrackRoute.None && _queueRoute != TrackRoute.Main) ? null :
                MainPlaybackSnapshot.Capture(CurrentTrack, _routing, _source.time, _source.timeSamples,
                    _source.clip.frequency, _source.isPlaying, _paused, _queue, _queueIndex,
                    QueueShuffle, QueueRepeat);
            InvalidateLoad();
            _completionTracker.Reset();
            _playWhenLibraryReady = false;
            _source.Pause();
            if (carryPendingMain)
            {
                _raidPlayback.CarryMainToNextDeployment();
                Plugin.Log.LogInfo("SoulPlayer raid playback: previousLifecycle=Complete snapshot=CarriedToNextDeployment.");
            }
            else _raidPlayback.Begin(snapshot);
            _deploymentWasActive = true;
            if (Plugin.PostRaidCoordinator != null) Plugin.PostRaidCoordinator.BeginRaid();
            _pausedForContext = false;
            _volumeState.MarkPaused();
            _menuEvidence = new RaidMenuEvidence { Screen = "Deployment/LiveRaid" };
            _readiness = RaidReadinessReason.WaitingForOutcome;
            RequestRaidReevaluation("DeploymentCapture");
            RefreshRaidReadiness();
            LogRaidState("DeploymentCapture");
            NotifyChanged();
        }

        internal bool ObserveRaidResult(ExitStatus outcome)
        {
            // Recovery for a missed deployment hook, never for a duplicate result.
            if (_raidPlayback.Phase == RaidPlaybackPhase.Idle) BeginRaidSuspension();
            return _raidPlayback.RecordOutcome(outcome);
        }

        internal void SetRaidMenuReady(bool ready)
        {
            _raidPlayback.SetMenuReady(ready);
            if (!_raidPlayback.MainSuspended) return;
            if (!RaidAudioMayContinue && _source.isPlaying)
            {
                _source.Pause();
                _pausedForContext = true;
                _volumeState.MarkPaused();
            }
            else if (RaidAudioMayContinue && _pausedForContext && !_paused && !_stopped &&
                _hasStarted && _source.clip != null &&
                _raidPlayback.Phase == RaidPlaybackPhase.Routed)
            {
                _source.UnPause();
                _pausedForContext = false;
                _volumeState.MarkPlaying();
            }
        }

        private void ProcessRaidPlayback()
        {
            if (!_raidPlayback.MainSuspended) return;
            RefreshRaidReadiness();
            if (_readiness != RaidReadinessReason.Ready ||
                (_raidPlayback.Phase != RaidPlaybackPhase.Suspended &&
                 _raidPlayback.Phase != RaidPlaybackPhase.ResumePending)) return;
            RaidPlaybackAction action = _raidPlayback.TakeNext(_settings.AutoPlayAfterRaid,
                _raidLibraryTracks, _routing, _settings.Shuffle,
                count => _random.Next(count), count => _outcomeRandom.Next(count));
            LogRaidState("Dispatch");
            if (action.Kind == RaidPlaybackActionKind.Routed)
            {
                ExactTrackPlaybackRequest request = new ExactTrackPlaybackRequest(
                    ++_nextExactRequestId, action.Route, action.Track);
                PlayCore(action.Track, action.Queue, action.Route, PlaybackIntent.Routed, request);
            }
            else if (action.Kind == RaidPlaybackActionKind.ResumeExact ||
                action.Kind == RaidPlaybackActionKind.Fallback)
            {
                Plugin.Log.LogInfo("SoulPlayer exact Main resume request: action=" + action.Kind +
                    " trackId=" + TrackRoutingService.GetTrackId(action.Track) +
                    " path=" + action.Track.FilePath + " capturedSamples=" +
                    (action.Snapshot == null ? 0 : action.Snapshot.Samples) + ".");
                PlayCore(action.Track, action.Queue, TrackRoute.Main, PlaybackIntent.Resume, null, action.Snapshot);
            }
            else if (action.Kind == RaidPlaybackActionKind.Finish)
            {
                StopCore();
                ReleaseClip();
                CurrentTrack = null;
                _queue.Clear();
                _queueIndex = -1;
                _queueRoute = TrackRoute.Main;
                NotifyChanged();
            }
            LogRaidLifecycle();
        }

        internal void PreserveMainForRecorder()
        {
            // Recorder has its own AudioSource. Recover a missed deployment hook
            // if necessary, but never stop/clear/replace the captured Main here.
            if (!_raidPlayback.MainSuspended) BeginRaidSuspension();
            if (_raidPlayback.NoteSoulRecorderUse()) LogRaidState("SoulRecorderUseSnapshotPreserved");
        }

        private bool CancelSavedResume(RaidCancellationReason reason)
        {
            if (_raidPlayback.MainSuspended) RefreshRaidReadiness();
            if (_raidPlayback.MainSuspended && !_raidPlayback.CancelSavedResume(reason)) return false;
            // Invalidate decoders now: a cancelled resume must never win later.
            InvalidateLoad();
            _playWhenLibraryReady = false;
            _restoredQueueSnapshot = null;
            LogRaidState("ExplicitControl");
            LogRaidLifecycle();
            return true;
        }

        private void LogRaidLifecycle()
        {
            LogRaidState("Completion");
            string diagnostic = _raidPlayback.TakeDiagnostic();
            if (diagnostic != null) Plugin.Log.LogInfo(diagnostic);
        }

        private void BeginLoad(MusicTrack track, PlaybackIntent intent,
            ExactTrackPlaybackRequest exactRequest = null, MainPlaybackSnapshot snapshot = null)
        {
            if (!CanStart(intent)) return;
            InvalidateLoad();
            _completionTracker.Reset();
            int generation = _loadGeneration;
            _loadingIntent = intent;
            _loadingExactRequest = exactRequest;
            _loadingSnapshot = snapshot;
            _lastError = string.Empty;
            _loading = true;
            _paused = false;
            _pausedForContext = false;
            _stopped = false;
            _hasStarted = false;
            CurrentTrack = track;

            if (exactRequest != null)
                Plugin.Log.LogInfo("SoulPlayer post-raid play request: postRaidRoute=" +
                    exactRequest.Route + " playRequestId=" + exactRequest.RequestId +
                    " playRequestTrackId=" + exactRequest.TrackId +
                    " playRequestPath=" + exactRequest.TrackPath + ".");

            _source.Stop();
            ReleaseClip();
            try
            {
                if (string.Equals(track.Extension, "FLAC", StringComparison.OrdinalIgnoreCase))
                    _flacTask = Task.Run(() => FlacDecoder.Decode(track.FilePath));
                else _loadCoroutine = StartCoroutine(LoadUnityAudio(track, generation));
            }
            catch (Exception ex)
            {
                FailLoad("Could not load " + track.Title + ": " + ex.GetBaseException().Message);
            }
            NotifyChanged();
        }

        private IEnumerator LoadUnityAudio(MusicTrack track, int generation)
        {
            UnityWebRequest request;
            UnityWebRequestAsyncOperation operation;
            try
            {
                request = UnityWebRequestMultimedia.GetAudioClip(
                    SoulPath.ToFileUri(track.FilePath), GetAudioType(track.Extension));
            }
            catch (Exception ex)
            {
                FailLoad("Could not open audio request: " + ex.GetBaseException().Message);
                yield break;
            }
            using (request)
            {
                try
                {
                    operation = request.SendWebRequest();
                }
                catch (Exception ex)
                {
                    FailLoad("Could not send audio request: " + ex.GetBaseException().Message);
                    yield break;
                }
                yield return operation;

                if (generation != _loadGeneration)
                {
                    yield break;
                }

                if (request.isNetworkError || request.isHttpError)
                {
                    _loadCoroutine = null;
                    FailLoad("Could not load " + Path.GetFileName(track.FilePath) + ": " + request.error);
                    yield break;
                }

                _loadCoroutine = null;
                try
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                    StartClip(clip, generation);
                }
                catch (Exception ex)
                {
                    FailLoad("Audio decode failed: " + ex.GetBaseException().Message);
                }
            }
        }

        private void Update()
        {
#if SOULPLAYER_PERF
            SoulPlayer.Utils.RecurringWorkProfiler.Begin(SoulPlayer.Utils.RecurringWorkArea.Audio);
            try
            {
#endif
            // Ingress only: a vanished GameWorld can never release suspension.
            bool deployment = GameState.IsDeploymentOrLiveRaid();
            if (deployment && !_deploymentWasActive)
                BeginRaidSuspension();
            _deploymentWasActive = deployment;
            if (_restoredQueueSnapshot != null &&
                (_settings.Shuffle != _restoredQueueSnapshot.Shuffle ||
                 _settings.RepeatMode != _restoredQueueSnapshot.RepeatMode))
                _restoredQueueSnapshot = null; // Honor a live user setting change.
            HandleGlobalControls();

            ScanResult scan;
            if (Plugin.MusicLibrary.TryApplyCompletedScan(out scan) && _playWhenLibraryReady)
            {
                _playWhenLibraryReady = false;
                StartFromLibrary();
            }

            ProcessRaidPlayback();
            if (_deferredClip != null && CanStart(_loadingIntent))
            {
                AudioClip ready = _deferredClip;
                _deferredClip = null;
                StartClip(ready, _loadGeneration);
            }
            if (_flacTask != null && _flacTask.IsCompleted)
            {
                Task<DecodedAudio> completed = _flacTask;
                _flacTask = null;
                int generation = _loadGeneration;
                try
                {
                    DecodedAudio decoded = completed.Result;
                    AudioClip clip = AudioClip.Create(
                        CurrentTrack == null ? "SoulPlayer FLAC" : CurrentTrack.Title,
                        decoded.Samples.Length / decoded.Channels, decoded.Channels, decoded.SampleRate, false);
                    if (!clip.SetData(decoded.Samples, 0)) throw new InvalidOperationException("Could not assign FLAC samples.");
                    StartClip(clip, generation);
                }
                catch (Exception ex) { FailLoad("FLAC decode failed: " + ex.GetBaseException().Message); }
            }

            bool mayComplete = !_raidPlayback.MainSuspended ||
                (RaidAudioMayContinue && _raidPlayback.Phase == RaidPlaybackPhase.Routed);
            if (mayComplete && _hasStarted && _completionTracker.Poll(
                _source.clip != null, _source.isPlaying, _loading, _paused, _pausedForContext, false))
            {
                _hasStarted = false;
                if (_raidPlayback.EndRouted(CurrentTrack, false))
                {
                    ProcessRaidPlayback();
                }
                else if (!_raidPlayback.MainSuspended)
                {
                    if (QueueRepeat == 2 && (_queueRoute == TrackRoute.None ||
                        _routing.IsEligible(CurrentTrack, _queueRoute)))
                        BeginLoad(CurrentTrack, PlaybackIntent.AutomaticMain);
                    else AdvanceAutomatically();
                }
            }
        #if SOULPLAYER_PERF
            }
            finally { SoulPlayer.Utils.RecurringWorkProfiler.End(SoulPlayer.Utils.RecurringWorkArea.Audio); }
#endif
        }

        private bool StartFromLibrary()
        {
            if (!CanStart(PlaybackIntent.AutomaticMain)) return false;
            MainAutoplayPlan plan = MainAutoplayPlan.Resolve(
                Plugin.MusicLibrary.Tracks, _routing, _settings.Shuffle, count => _random.Next(count));
            if (plan.SelectedTrack == null)
            {
                if (!Plugin.MusicLibrary.IsScanning)
                {
                    _lastError = Plugin.MusicLibrary.Tracks.Count == 0
                        ? "No playable tracks are available. Add a music folder first."
                        : "No tracks are routed to Main.";
                    NotifyChanged();
                }
                return false;
            }
            PlayCore(plan.SelectedTrack, plan.Tracks, TrackRoute.Main, PlaybackIntent.AutomaticMain);
            return true;
        }

        private void StartClip(AudioClip clip, int generation)
        {
            if (generation != _loadGeneration)
            {
                if (clip != null) UnityEngine.Object.Destroy(clip);
                return;
            }
            if (clip == null) { FailLoad("Audio decoder returned no clip."); return; }
            // Recheck the real menu context at the final AudioSource boundary,
            // including asynchronously decoded FLAC/Unity requests.
            if (_raidPlayback.MainSuspended) _readinessPoll.Invalidate();
            if (!CanStart(_loadingIntent))
            {
                _deferredClip = clip;
                return;
            }
            if (_loadingIntent == PlaybackIntent.Resume &&
                (!File.Exists(CurrentTrack.FilePath) || !_routing.IsEligible(CurrentTrack, TrackRoute.Main)))
            {
                UnityEngine.Object.Destroy(clip);
                FailLoad("Saved Main track is no longer available or Main-eligible.");
                return;
            }
            ExactTrackPlaybackRequest startedExactRequest = _loadingExactRequest;
            if (startedExactRequest != null && !startedExactRequest.TryMarkStarted(CurrentTrack))
            {
                UnityEngine.Object.Destroy(clip);
                FailLoad("Post-raid playback did not match the routed track request.");
                return;
            }
            _loadingExactRequest = null;
            _source.clip = clip;
            _source.volume = _volumeState.TargetVolume;
            MainPlaybackSnapshot snapshot = _loadingSnapshot;
            _loadingSnapshot = null;
            if (snapshot != null) _source.timeSamples = snapshot.ResumeSamples(clip.samples, clip.frequency);
            _paused = snapshot != null && snapshot.WasPaused;
            _pausedForContext = false;
            _hasStarted = !_paused;
            _stopped = false;
            if (!_paused)
            {
                _source.Play();
                _completionTracker.Started(_source.isPlaying);
                _postRaidContinuity.PlaybackStarted(_raidPlayback.MainSuspended, _source.isPlaying);
                _volumeState.MarkPlaying();
            }
            else _volumeState.MarkPaused();
            _loading = false;
            if (startedExactRequest != null)
            {
                _raidPlayback.RoutedStarted(CurrentTrack);
                Plugin.Log.LogInfo("SoulPlayer post-raid actual start: playRequestId=" +
                    startedExactRequest.RequestId + " actualStartedId=" +
                    TrackRoutingService.GetTrackId(CurrentTrack) + " actualStartedPath=" +
                    CurrentTrack.FilePath + ".");
            }
            if (_loadingIntent == PlaybackIntent.Resume) _raidPlayback.ResumeStarted(CurrentTrack);
            LogRaidLifecycle();
            NotifyChanged();
        }

        private void StopPlayback()
        {
            _completionTracker.Reset();
            _source.Stop();
            _paused = false;
            _hasStarted = false;
            _stopped = true;
            _volumeState.MarkStopped();
            NotifyChanged();
        }

        private void HandleGlobalControls()
        {
#if SOULPLAYER_PERF
            SoulPlayer.Utils.RecurringWorkProfiler.Begin(SoulPlayer.Utils.RecurringWorkArea.Input);
            try
            {
#endif
            bool playPause = ShortcutPressed(_settings.PlayPauseHotkey);
            bool stop = ShortcutPressed(_settings.StopHotkey);
            bool next = ShortcutPressed(_settings.NextHotkey);
            bool previous = ShortcutPressed(_settings.PreviousHotkey);

            if (_settings.EnableMediaKeys)
            {
                playPause |= MediaKeyInput.PlayPausePressed();
                stop |= MediaKeyInput.StopPressed();
                next |= MediaKeyInput.NextPressed();
                previous |= MediaKeyInput.PreviousPressed();
            }

            if (!playPause && !stop && !next && !previous)
            {
                return;
            }

            if (stop)
            {
                Stop();
            }
            else if (previous)
            {
                Previous();
            }
            else if (next)
            {
                Next();
            }
            else if (playPause)
            {
                TogglePause();
            }
        #if SOULPLAYER_PERF
            }
            finally { SoulPlayer.Utils.RecurringWorkProfiler.End(SoulPlayer.Utils.RecurringWorkArea.Input); }
#endif
        }

        private static bool ShortcutPressed(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None || !Input.GetKeyDown(shortcut.MainKey))
            {
                return false;
            }

            foreach (KeyCode modifier in shortcut.Modifiers)
            {
                if (!Input.GetKey(modifier))
                {
                    return false;
                }
            }

            return true;
        }

        private void FailLoad(string message)
        {
            _completionTracker.Reset();
            _loadingExactRequest = null;
            _loadingSnapshot = null;
            _lastError = message;
            _loading = false;
            _hasStarted = false;
            _stopped = true;
            if (_loadingIntent == PlaybackIntent.Routed) _raidPlayback.EndRouted(CurrentTrack, true);
            else if (_loadingIntent == PlaybackIntent.Resume) _raidPlayback.ResumeFailed();
            // Recovery is dispatched on the next update, never recursively from a decoder.
            Plugin.Log.LogError(message);
            LogRaidLifecycle();
            NotifyChanged();
        }

        private void ReleaseClip()
        {
            if (_source.clip != null)
            {
                AudioClip old = _source.clip;
                _source.clip = null;
                UnityEngine.Object.Destroy(old);
            }
        }

        private static AudioType GetAudioType(string extension)
        {
            if (string.Equals(extension, "MP3", StringComparison.OrdinalIgnoreCase))
            {
                return AudioType.MPEG;
            }

            if (string.Equals(extension, "OGG", StringComparison.OrdinalIgnoreCase))
            {
                return AudioType.OGGVORBIS;
            }

            return AudioType.WAV;
        }

        private void NotifyChanged()
        {
            Action handler = Changed;
            if (handler != null)
            {
                handler();
            }
        }

        private void OnDestroy()
        {
            if (_library != null)
            {
                _library.Changed -= OnLibraryChanged;
                _library.ScanStateChanged -= OnLibraryScanStateChanged;
            }
            if (_settings != null)
            {
                _settings.VolumeChanged -= OnVolumeChanged;
                _settings.MenuContinuityChanged -= OnMenuContinuityChanged;
            }
            InvalidateLoad();
            ReleaseClip();
        }
    }
}
