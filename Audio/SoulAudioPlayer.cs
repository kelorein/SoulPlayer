using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
        private enum FadeCompletion
        {
            None,
            PauseForRaid,
            StartPendingTrack
        }

        private readonly System.Random _random = new System.Random();
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
        private bool _pausedForRaid;
        private float _nextRaidCheck;
        private ExitStatus? _pendingPostRaidOutcome;
        private ExitStatus? _lastPostRaidOutcome;
        private float _lastPostRaidTrigger = -100f;
        private float _resumeAfterRaidAt = -1f;
        private bool _playWhenLibraryReady;
        private string _lastError = string.Empty;
        private FadeCompletion _fadeCompletion;
        private float _fadeStartedAt;
        private float _fadeDuration;
        private float _fadeStartVolume;
        private MusicTrack _fadePendingTrack;
        private List<MusicTrack> _fadePendingQueue;
        private TrackRoute _fadePendingRoute;
        private ExactTrackPlaybackRequest _fadePendingExactRequest;
        private ExactTrackPlaybackRequest _loadingExactRequest;
        private int _nextExactRequestId;

        internal event Action Changed;

        internal MusicTrack CurrentTrack { get; private set; }
        internal MusicTrack DisplayTrack
        {
            get { return CurrentTrack; }
        }
        internal bool IsLoading { get { return _loading; } }
        internal bool IsPlaying { get { return _source != null && _source.isPlaying; } }
        internal bool IsPaused { get { return _paused || _pausedForRaid; } }
        internal string LastError { get { return _lastError; } }
        internal float CurrentTime { get { return _source != null && _source.clip != null ? _source.time : 0f; } }
        internal float Duration { get { return _source != null && _source.clip != null ? _source.clip.length : 0f; } }

        internal void Initialize(SoulPlayerSettings settings, TrackRoutingService routing)
        {
            if (_settings != null)
            {
                _settings.VolumeChanged -= OnVolumeChanged;
            }
            _settings = settings;
            _routing = routing;
            _volumeState = new SoulPlayerVolumeState(settings.Volume);
            _settings.VolumeChanged += OnVolumeChanged;
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _source.ignoreListenerPause = true;
            _source.volume = _volumeState.TargetVolume;
        }

        internal void Play(MusicTrack track, IEnumerable<MusicTrack> queue)
        {
            PlayCore(track, queue, TrackRoute.None, null);
        }

        private void PlayAutomatic(
            MusicTrack track,
            IEnumerable<MusicTrack> queue,
            TrackRoute route)
        {
            PlayCore(track, queue, route, null);
        }

        private void PlayExactPostRaid(
            MusicTrack track,
            IEnumerable<MusicTrack> queue,
            ExactTrackPlaybackRequest request)
        {
            PlayCore(track, queue, request.Route, request);
        }

        private void PlayCore(
            MusicTrack track,
            IEnumerable<MusicTrack> queue,
            TrackRoute route,
            ExactTrackPlaybackRequest exactRequest)
        {
            if (track == null)
            {
                return;
            }

            CancelFade();

            _queue = queue == null ? new List<MusicTrack>() : queue.ToList();
            _queueRoute = route;
            _queueIndex = _queue.FindIndex(item =>
                SoulPath.AreEquivalent(item.FilePath, track.FilePath));

            if (_queueIndex < 0)
            {
                _queue.Insert(0, track);
                _queueIndex = 0;
            }

            BeginLoad(track, exactRequest);
        }

        internal void TogglePause()
        {
            if (_loading)
            {
                return;
            }

            if (_source.clip == null)
            {
                if (_queue.Count == 0)
                {
                    if (!StartFromLibrary() && Plugin.MusicLibrary.IsScanning)
                    {
                        _playWhenLibraryReady = true;
                        Plugin.Log.LogInfo("Play requested while the music library is still scanning.");
                    }
                }
                else
                {
                    _queueIndex = Math.Max(0, _queueIndex);
                    BeginLoad(_queue[_queueIndex]);
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
                if (!_hasStarted || _source.time >= _source.clip.length - 0.1f)
                {
                    _source.time = 0f;
                    _source.Play();
                    _hasStarted = true;
                }
                else
                {
                    _source.UnPause();
                }
                _paused = false;
                _volumeState.MarkPlaying();
            }

            NotifyChanged();
        }

        internal void Stop()
        {
            CancelFade();
            _loadGeneration++;
            if (_loadCoroutine != null)
            {
                StopCoroutine(_loadCoroutine);
                _loadCoroutine = null;
            }

            _flacTask = null;
            _loadingExactRequest = null;
            _loading = false;
            _playWhenLibraryReady = false;
            _pendingPostRaidOutcome = null;
            _resumeAfterRaidAt = -1f;
            _pausedForRaid = false;
            _source.Stop();
            if (_source.clip != null)
            {
                _source.time = 0f;
            }
            _paused = true;
            _hasStarted = false;
            _volumeState.MarkStopped();
            NotifyChanged();
        }

        internal void Next()
        {
            RefreshAutomaticQueue();
            if (_queue.Count == 0)
            {
                if (_queueRoute == TrackRoute.None)
                {
                    StartFromLibrary();
                }
                else
                {
                    StopPlayback();
                }
                return;
            }

            if (_settings.Shuffle && _queue.Count > 1)
            {
                int next;
                do
                {
                    next = _random.Next(_queue.Count);
                }
                while (next == _queueIndex);

                _queueIndex = next;
            }
            else
            {
                _queueIndex++;
                if (_queueIndex >= _queue.Count)
                {
                    if (_settings.RepeatMode == 1)
                    {
                        _queueIndex = 0;
                    }
                    else
                    {
                        _queueIndex = _queue.Count - 1;
                        StopPlayback();
                        return;
                    }
                }
            }

            BeginLoad(_queue[_queueIndex]);
        }

        private void AdvanceAutomatically()
        {
            if (_queueRoute == TrackRoute.None)
            {
                StartFromLibrary();
                return;
            }

            Next();
        }

        private void RefreshAutomaticQueue()
        {
            if (_queueRoute == TrackRoute.None || _routing == null)
            {
                return;
            }

            string currentId = TrackRoutingService.GetTrackId(CurrentTrack);
            _queue = _routing.SelectEligible(Plugin.MusicLibrary.Tracks, _queueRoute);
            _queueIndex = _queue.FindIndex(track =>
                string.Equals(
                    TrackRoutingService.GetTrackId(track),
                    currentId,
                    StringComparison.Ordinal));
        }

        internal void Previous()
        {
            if (_source.clip != null && _source.time > 4f)
            {
                _source.time = 0f;
                _source.Play();
                _paused = false;
                NotifyChanged();
                return;
            }

            if (_queue.Count == 0)
            {
                return;
            }

            _queueIndex = Math.Max(0, _queueIndex - 1);
            BeginLoad(_queue[_queueIndex]);
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
            if (_source == null)
            {
                return;
            }

            if (_fadeCompletion == FadeCompletion.None)
            {
                _source.volume = _volumeState.TargetVolume;
                return;
            }

            float progress = _fadeDuration <= 0.01f
                ? 1f
                : Mathf.Clamp01((Time.unscaledTime - _fadeStartedAt) /
                    _fadeDuration);
            _fadeStartVolume = _volumeState.TargetVolume;
            _source.volume = Mathf.Lerp(_fadeStartVolume, 0f, progress);
        }

        internal void SetProgress(float normalized)
        {
            if (_source.clip == null)
            {
                return;
            }

            _source.time = Mathf.Clamp01(normalized) * _source.clip.length;
            NotifyChanged();
        }

        internal void PlayPostRaid(ExitStatus outcome)
        {
            if (_lastPostRaidOutcome == outcome && Time.realtimeSinceStartup - _lastPostRaidTrigger < 8f)
            {
                return;
            }

            _lastPostRaidOutcome = outcome;
            _lastPostRaidTrigger = Time.realtimeSinceStartup;

            if (!_settings.AutoPlayAfterRaid)
            {
                LogPostRaidPlan(PostRaidAutoplayPlan.Resolve(
                    outcome,
                    false,
                    Plugin.MusicLibrary.Tracks,
                    _routing,
                    null));
                return;
            }

            // Keep the pre-raid track paused while the outcome playlist is
            // selected and (for FLAC) decoded. This prevents a brief old-song
            // burst between the raid and the new result music.
            _resumeAfterRaidAt = float.PositiveInfinity;

            if (Plugin.MusicLibrary.IsScanning && Plugin.MusicLibrary.Tracks.Count == 0)
            {
                _pendingPostRaidOutcome = outcome;
                Plugin.Log.LogInfo("Post-raid music is waiting for the library scan to finish.");
                return;
            }

            StartPostRaidPlayback(outcome);
        }

        private void BeginLoad(
            MusicTrack track,
            ExactTrackPlaybackRequest exactRequest = null)
        {
            _loadGeneration++;
            int generation = _loadGeneration;
            _loadingExactRequest = exactRequest;
            _lastError = string.Empty;
            _loading = true;
            _paused = false;
            _hasStarted = false;
            CurrentTrack = track;

            if (exactRequest != null)
            {
                Plugin.Log.LogInfo(
                    "SoulPlayer post-raid play request: postRaidRoute=" +
                    exactRequest.Route +
                    " playRequestId=" + exactRequest.RequestId +
                    " playRequestTrackId=" + exactRequest.TrackId +
                    " playRequestPath=" + exactRequest.TrackPath + ".");
            }

            if (_loadCoroutine != null)
            {
                StopCoroutine(_loadCoroutine);
                _loadCoroutine = null;
            }

            _source.Stop();
            ReleaseClip();

            if (string.Equals(track.Extension, "FLAC", StringComparison.OrdinalIgnoreCase))
            {
                _flacTask = Task.Run(() => FlacDecoder.Decode(track.FilePath));
            }
            else
            {
                _flacTask = null;
                _loadCoroutine = StartCoroutine(LoadUnityAudio(track, generation));
            }

            NotifyChanged();
        }

        private IEnumerator LoadUnityAudio(MusicTrack track, int generation)
        {
            AudioType type = GetAudioType(track.Extension);
            string uri = SoulPath.ToFileUri(track.FilePath);

            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(uri, type))
            {
                yield return request.SendWebRequest();

                if (generation != _loadGeneration)
                {
                    yield break;
                }

                if (request.isNetworkError || request.isHttpError)
                {
                    FailLoad("Could not load " + Path.GetFileName(track.FilePath) + ": " + request.error);
                    yield break;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                StartClip(clip, generation);
            }

            _loadCoroutine = null;
        }

        private void Update()
        {
            HandleGlobalControls();
            UpdateFade();

            ScanResult scan;
            if (Plugin.MusicLibrary.TryApplyCompletedScan(out scan))
            {
                if (_pendingPostRaidOutcome.HasValue)
                {
                    ExitStatus pending = _pendingPostRaidOutcome.Value;
                    _pendingPostRaidOutcome = null;
                    StartPostRaidPlayback(pending);
                }
                else if (_playWhenLibraryReady)
                {
                    _playWhenLibraryReady = false;
                    StartFromLibrary();
                }
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
                        decoded.Samples.Length / decoded.Channels,
                        decoded.Channels,
                        decoded.SampleRate,
                        false);

                    if (!clip.SetData(decoded.Samples, 0))
                    {
                        UnityEngine.Object.Destroy(clip);
                        FailLoad("Unity rejected the decoded FLAC samples.");
                    }
                    else
                    {
                        StartClip(clip, generation);
                    }
                }
                catch (Exception ex)
                {
                    FailLoad("FLAC decode failed: " + ex.GetBaseException().Message);
                }
            }

            if (Time.unscaledTime >= _nextRaidCheck)
            {
                _nextRaidCheck = Time.unscaledTime + 0.25f;
                bool suspendForRaid = GameState.ShouldSuspendMenuMusic();
                if (suspendForRaid && _source.isPlaying &&
                    _fadeCompletion != FadeCompletion.PauseForRaid)
                {
                    _resumeAfterRaidAt = -1f;
                    BeginFade(_settings.RaidFadeSeconds, FadeCompletion.PauseForRaid);
                }
                else if (!suspendForRaid)
                {
                    if (_fadeCompletion == FadeCompletion.PauseForRaid)
                    {
                        CancelFade();
                    }
                    else if (_pausedForRaid && _resumeAfterRaidAt < 0f)
                    {
                        _resumeAfterRaidAt = Time.unscaledTime + 2f;
                    }
                    else if (_pausedForRaid && Time.unscaledTime >= _resumeAfterRaidAt)
                    {
                        ResumePreRaidTrack();
                    }
                }
            }

            if (_hasStarted && !_loading && !_paused && !_pausedForRaid &&
                _fadeCompletion == FadeCompletion.None &&
                _source.clip != null && !_source.isPlaying && _source.time >= _source.clip.length - 0.15f)
            {
                _hasStarted = false;
                if (_settings.RepeatMode == 2 &&
                    (_queueRoute == TrackRoute.None ||
                     _routing.IsEligible(CurrentTrack, _queueRoute)))
                {
                    BeginLoad(CurrentTrack);
                }
                else
                {
                    AdvanceAutomatically();
                }
            }
        }

        private void StartPostRaidPlayback(ExitStatus outcome)
        {
            PostRaidAutoplayPlan plan = PostRaidAutoplayPlan.Resolve(
                outcome,
                true,
                Plugin.MusicLibrary.Tracks,
                _routing,
                count => _random.Next(count));
            LogPostRaidPlan(plan);

            if (!plan.ShouldStart)
            {
                ResumePreRaidTrack();
                return;
            }

            List<MusicTrack> queue = plan.EligibleTracks;
            MusicTrack selected = plan.SelectedTrack;
            TrackRoute route = plan.Route;
            ExactTrackPlaybackRequest exactRequest =
                new ExactTrackPlaybackRequest(
                    ++_nextExactRequestId,
                    route,
                    selected);
            _pausedForRaid = false;
            _resumeAfterRaidAt = -1f;

            if (_source.isPlaying && _settings.PostRaidTransitionSeconds > 0.01f)
            {
                _fadePendingTrack = selected;
                _fadePendingQueue = queue;
                _fadePendingRoute = route;
                _fadePendingExactRequest = exactRequest;
                BeginFade(_settings.PostRaidTransitionSeconds, FadeCompletion.StartPendingTrack);
            }
            else
            {
                PlayExactPostRaid(selected, queue, exactRequest);
            }
        }

        private static void LogPostRaidPlan(PostRaidAutoplayPlan plan)
        {
            string selected = plan.SelectedTrack == null
                ? (plan.AutoplayEnabled ? "<none>" : "<suppressed>")
                : plan.SelectedTrack.Title;
            string selectedId = plan.SelectedTrack == null
                ? "<none>"
                : TrackRoutingService.GetTrackId(plan.SelectedTrack);
            string selectedPath = plan.SelectedTrack == null
                ? "<none>"
                : plan.SelectedTrack.FilePath;
            string eligibleIds = string.Join(
                ",",
                plan.EligibleTracks
                    .Select(TrackRoutingService.GetTrackId)
                    .ToArray());
            Plugin.Log.LogInfo(
                "SoulPlayer post-raid: outcome=" + plan.Outcome +
                " postRaidRoute=" + plan.Route +
                " eligible=" + plan.EligibleTracks.Count +
                " eligibleIds=[" + eligibleIds + "]" +
                " autoplay=" + plan.AutoplayEnabled +
                " selected=" + selected +
                " selectedId=" + selectedId +
                " selectedPath=" + selectedPath + ".");
        }

        private bool StartFromLibrary()
        {
            List<MusicTrack> tracks = _routing.SelectEligible(
                Plugin.MusicLibrary.Tracks,
                TrackRoute.Main);
            if (tracks.Count == 0)
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

            int index = _settings.Shuffle ? _random.Next(tracks.Count) : 0;
            PlayAutomatic(tracks[index], tracks, TrackRoute.Main);
            return true;
        }

        private void StartClip(AudioClip clip, int generation)
        {
            if (clip == null || generation != _loadGeneration)
            {
                if (clip != null)
                {
                    UnityEngine.Object.Destroy(clip);
                }

                return;
            }

            ExactTrackPlaybackRequest startedExactRequest = null;
            string actualStartedId = null;
            string actualStartedPath = null;
            if (_loadingExactRequest != null)
            {
                ExactTrackPlaybackRequest exactRequest = _loadingExactRequest;
                actualStartedId = TrackRoutingService.GetTrackId(CurrentTrack);
                actualStartedPath = CurrentTrack == null
                    ? "<none>"
                    : CurrentTrack.FilePath;
                if (!exactRequest.TryMarkStarted(CurrentTrack))
                {
                    UnityEngine.Object.Destroy(clip);
                    Plugin.Log.LogError(
                        "SoulPlayer post-raid exact-track mismatch: playRequestId=" +
                        exactRequest.RequestId +
                        " requestedId=" + exactRequest.TrackId +
                        " requestedPath=" + exactRequest.TrackPath +
                        " actualStartedId=" + actualStartedId +
                        " actualStartedPath=" + actualStartedPath +
                        ". Playback was stopped; no fallback was selected.");
                    FailLoad("Post-raid playback did not match the routed track request.");
                    return;
                }

                startedExactRequest = exactRequest;
                _loadingExactRequest = null;
            }

            _source.clip = clip;
            _source.volume = _volumeState.TargetVolume;
            _source.Play();
            if (startedExactRequest != null)
            {
                Plugin.Log.LogInfo(
                    "SoulPlayer post-raid actual start: playRequestId=" +
                    startedExactRequest.RequestId +
                    " actualStartedId=" + actualStartedId +
                    " actualStartedPath=" + actualStartedPath + ".");
            }
            _loading = false;
            _hasStarted = true;
            _volumeState.MarkPlaying();
            NotifyChanged();
        }

        private void StopPlayback()
        {
            _source.Stop();
            _paused = false;
            _hasStarted = false;
            _volumeState.MarkStopped();
            NotifyChanged();
        }

        private void ResumePreRaidTrack()
        {
            _resumeAfterRaidAt = -1f;
            _pausedForRaid = false;
            if (!_paused && _source.clip != null)
            {
                _source.UnPause();
                _volumeState.MarkPlaying();
            }

            NotifyChanged();
        }

        private void BeginFade(float duration, FadeCompletion completion)
        {
            if (duration <= 0.01f || !_source.isPlaying)
            {
                CompleteFade(completion);
                return;
            }

            _fadeCompletion = completion;
            _fadeStartedAt = Time.unscaledTime;
            _fadeDuration = duration;
            _fadeStartVolume = _source.volume;
            NotifyChanged();
        }

        private void UpdateFade()
        {
            if (_fadeCompletion == FadeCompletion.None)
            {
                return;
            }

            float progress = _fadeDuration <= 0.01f
                ? 1f
                : Mathf.Clamp01((Time.unscaledTime - _fadeStartedAt) / _fadeDuration);
            _source.volume = Mathf.Lerp(_fadeStartVolume, 0f, progress);

            if (progress >= 1f)
            {
                FadeCompletion completion = _fadeCompletion;
                _fadeCompletion = FadeCompletion.None;
                CompleteFade(completion);
            }
        }

        private void CompleteFade(FadeCompletion completion)
        {
            _source.volume = _volumeState.TargetVolume;
            if (completion == FadeCompletion.PauseForRaid)
            {
                _source.Pause();
                _pausedForRaid = true;
                _volumeState.MarkPaused();
                NotifyChanged();
                return;
            }

            if (completion == FadeCompletion.StartPendingTrack)
            {
                MusicTrack track = _fadePendingTrack;
                List<MusicTrack> queue = _fadePendingQueue;
                TrackRoute route = _fadePendingRoute;
                ExactTrackPlaybackRequest exactRequest =
                    _fadePendingExactRequest;
                _fadePendingTrack = null;
                _fadePendingQueue = null;
                _fadePendingRoute = TrackRoute.None;
                _fadePendingExactRequest = null;

                if (track != null && exactRequest != null &&
                    ReferenceEquals(track, exactRequest.Track) &&
                    route == exactRequest.Route)
                {
                    // The route decision is final. Never run shuffle or choose a
                    // replacement between selection and the AudioSource request.
                    PlayExactPostRaid(track, queue, exactRequest);
                }
                else
                {
                    Plugin.Log.LogError(
                        "Post-raid exact-track request was lost before playback; " +
                        "no fallback track was selected.");
                    ResumePreRaidTrack();
                }
            }
        }

        private void CancelFade()
        {
            if (_fadeCompletion == FadeCompletion.None)
            {
                return;
            }

            _fadeCompletion = FadeCompletion.None;
            _fadePendingTrack = null;
            _fadePendingQueue = null;
            _fadePendingRoute = TrackRoute.None;
            _fadePendingExactRequest = null;
            _source.volume = _volumeState.TargetVolume;
            NotifyChanged();
        }

        private void HandleGlobalControls()
        {
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

            if ((!playPause && !stop && !next && !previous) ||
                GameState.ShouldSuspendMenuMusic())
            {
                return;
            }

            CancelFade();

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
            _loadingExactRequest = null;
            _lastError = message;
            _loading = false;
            _hasStarted = false;
            Plugin.Log.LogError(message);
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
            if (_settings != null)
            {
                _settings.VolumeChanged -= OnVolumeChanged;
            }
            ReleaseClip();
        }
    }
}
