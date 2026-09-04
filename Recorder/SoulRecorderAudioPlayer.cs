using System;
using System.Collections;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using SoulPlayer.Audio;
using SoulPlayer.Configuration;
using SoulPlayer.Library;
using UnityEngine;
using UnityEngine.Networking;

namespace SoulPlayer.Recorder
{
    internal sealed class SoulRecorderAudioPlayer : MonoBehaviour
    {
        private enum PreparationStage
        {
            None,
            SetPcm,
            EnsureAudioData,
            AssignSource,
            PrimeSource,
            PausePrimedSource,
            Ready
        }

        private SoulPlayerSettings _settings;
        private SoulPlayerVolumeState _volumeState;
        private AudioSource _source;
        private Coroutine _loadCoroutine;
        private Task<ProfiledDecodedAudio> _flacTask;
        private DecodedAudio _pendingDecodedAudio;
        private AudioClip _preparedClip;
        private PreparationStage _preparationStage;
        private bool _playWhenPrepared;
        private bool _startPreparedOnNextUpdate;
        private long _prepareStartedTimestamp;
        private long _preparedTimestamp;
        private int _loadGeneration;
        private bool _loading;
        private string _lastError = string.Empty;
        private string _preparationPath = string.Empty;
        private int _preparationThreadId;
        private double _fileReadMs;
        private double _decodeMs;
        private double _pcmConversionMs;
        private double _audioClipCreateMs;
        private double _setDataMs;
        private double _loadAudioDataMs;
        private double _audioSourceAssignMs;
        private double _sourcePrimeMs;
        private double _playTriggerMs;
        private double _seatUiCallbacksMs;
        private double _seatEventCallbacksMs;
        private double _seatStateMs;
        private double _seatPlaybackRequestMs;
        private double _seatTotalMs;
        private double _preparationLeadTimeMs;
        private double _prepareRequestToReadyMs;
        private int _framesWaitedAfterPrime;
        private bool _animationStartedAfterAudioReady;
        private bool _readyAtSeat;
        private string _deferredDiagnostic;
        private int _deferredDiagnosticFrames;

        internal MusicTrack CurrentTrack { get; private set; }
        internal bool IsLoading { get { return _loading; } }
        internal bool IsPlaying { get { return _source != null && _source.isPlaying; } }
        internal string LastError { get { return _lastError; } }

        internal void Initialize(SoulPlayerSettings settings)
        {
            if (_settings != null)
            {
                _settings.VolumeChanged -= OnVolumeChanged;
            }
            _settings = settings;
            _volumeState = new SoulPlayerVolumeState(settings.Volume);
            _settings.VolumeChanged += OnVolumeChanged;
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _source.ignoreListenerPause = true;
            _source.volume = _volumeState.TargetVolume;
        }

        internal bool IsPrepared
        {
            get { return _preparationStage == PreparationStage.Ready && !_loading; }
        }

        internal void Prepare(MusicTrack track)
        {
            if (track == null)
            {
                return;
            }

            if (CurrentTrack == track && (_loading || IsPrepared))
            {
                return;
            }

            _loadGeneration++;
            int generation = _loadGeneration;
            ResetTimingMetrics();
            _lastError = string.Empty;
            _loading = true;
            _playWhenPrepared = false;
            _startPreparedOnNextUpdate = false;
            _prepareStartedTimestamp = Stopwatch.GetTimestamp();
            _preparationThreadId = Thread.CurrentThread.ManagedThreadId;
            CurrentTrack = track;

            if (_loadCoroutine != null)
            {
                StopCoroutine(_loadCoroutine);
                _loadCoroutine = null;
            }

            _flacTask = null;
            _pendingDecodedAudio = null;
            _preparationStage = PreparationStage.None;
            _source.Stop();
            _volumeState.MarkStopped();
            ReleaseClip();

            Plugin.Log.LogInfo(
                "SoulRecorder AUDIO PREPARE -> " + track.Artist + " - " + track.Title +
                " [" + track.Extension + "] | mainThread=" +
                _preparationThreadId);

            if (string.Equals(track.Extension, "FLAC", StringComparison.OrdinalIgnoreCase))
            {
                _preparationPath = "FLAC worker decode";
                _flacTask = Task.Run(() =>
                    FlacDecoder.DecodeProfiled(track.FilePath));
            }
            else
            {
                _preparationPath = "UnityWebRequest decompressed local audio";
                _loadCoroutine = StartCoroutine(LoadUnityAudio(track, generation));
            }
        }

        internal void PlayPrepared(MusicTrack track)
        {
            if (track == null)
            {
                return;
            }

            if (CurrentTrack != track || (!_loading && !IsPrepared))
            {
                Plugin.Log.LogWarning(
                    "SoulRecorder audio was not prepared before seating; starting " +
                    "the asynchronous fallback load.");
                Prepare(track);
            }

            _playWhenPrepared = true;
            if (IsPrepared)
            {
                StartPreparedClip();
            }
        }

        internal void RecordSeatTiming(double uiCallbacksMs,
            double eventCallbacksMs, double stateMs,
            double playbackRequestMs, double totalMs)
        {
            long now = Stopwatch.GetTimestamp();
            _seatUiCallbacksMs = uiCallbacksMs;
            _seatEventCallbacksMs = eventCallbacksMs;
            _seatStateMs = stateMs;
            _seatPlaybackRequestMs = playbackRequestMs;
            _seatTotalMs = totalMs;
            _readyAtSeat = IsPrepared;
            _preparationLeadTimeMs = _preparedTimestamp <= 0
                ? 0d
                : (now - _preparedTimestamp) * 1000d / Stopwatch.Frequency;
            if (IsPlaying)
            {
                QueuePlaybackDiagnostic();
            }
        }

        internal void RecordAnimationGateTiming(double prepareRequestToReadyMs,
            int framesWaitedAfterPrime,
            bool animationStartedAfterAudioReady)
        {
            _prepareRequestToReadyMs = prepareRequestToReadyMs;
            _framesWaitedAfterPrime = framesWaitedAfterPrime;
            _animationStartedAfterAudioReady =
                animationStartedAfterAudioReady;
        }

        internal void Stop()
        {
            _loadGeneration++;
            if (_loadCoroutine != null)
            {
                StopCoroutine(_loadCoroutine);
                _loadCoroutine = null;
            }

            _flacTask = null;
            _pendingDecodedAudio = null;
            _loading = false;
            _playWhenPrepared = false;
            _startPreparedOnNextUpdate = false;
            _preparationStage = PreparationStage.None;
            _source.Stop();
            ReleaseClip();
            CurrentTrack = null;
        }

        private IEnumerator LoadUnityAudio(MusicTrack track, int generation)
        {
            AudioType type = GetAudioType(track.Extension);
            string uri = SoulPath.ToFileUri(track.FilePath);
            Stopwatch timer = Stopwatch.StartNew();

            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(uri, type))
            {
                DownloadHandlerAudioClip handler =
                    request.downloadHandler as DownloadHandlerAudioClip;
                if (handler != null)
                {
                    handler.streamAudio = false;
                    handler.compressed = false;
                }

                yield return request.SendWebRequest();
                timer.Stop();
                _fileReadMs = timer.Elapsed.TotalMilliseconds;

                if (generation != _loadGeneration)
                {
                    yield break;
                }

                if (request.isNetworkError || request.isHttpError)
                {
                    FailLoad("Recorder could not load " + Path.GetFileName(track.FilePath) + ": " + request.error);
                    yield break;
                }

                timer.Restart();
                AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                timer.Stop();
                _audioClipCreateMs = timer.Elapsed.TotalMilliseconds;
                // Unity's public request API does not expose a separate native
                // codec timer. Its asynchronous codec work is included in the
                // request wall time above; GetContent is timed separately.
                _decodeMs = 0d;
                _pcmConversionMs = 0d;
                QueueClipForPreparation(clip, generation);
            }

            _loadCoroutine = null;
        }

        private void Update()
        {
#if SOULPLAYER_PERF
            SoulPlayer.Utils.RecurringWorkProfiler.Begin(SoulPlayer.Utils.RecurringWorkArea.Audio);
            try
            {
#endif
            FlushDeferredDiagnostic();

            if (_startPreparedOnNextUpdate)
            {
                _startPreparedOnNextUpdate = false;
                StartPreparedClip();
                return;
            }

            if (_flacTask != null && _flacTask.IsCompleted)
            {
                CompleteFlacDecode();
                return;
            }

            AdvancePreparationOneStage();
        #if SOULPLAYER_PERF
            }
            finally { SoulPlayer.Utils.RecurringWorkProfiler.End(SoulPlayer.Utils.RecurringWorkArea.Audio); }
#endif
        }

        private void CompleteFlacDecode()
        {
            Task<ProfiledDecodedAudio> completed = _flacTask;
            _flacTask = null;
            int generation = _loadGeneration;

            try
            {
                ProfiledDecodedAudio profiled = completed.Result;
                _fileReadMs = profiled.FileReadMs;
                _decodeMs = profiled.DecodeMs;
                _pcmConversionMs = profiled.PcmConversionMs;
                _pendingDecodedAudio = profiled.Audio;

                Stopwatch timer = Stopwatch.StartNew();
                _preparedClip = AudioClip.Create(
                    CurrentTrack == null ? "SoulRecorder FLAC" : CurrentTrack.Title,
                    profiled.Audio.Samples.Length / profiled.Audio.Channels,
                    profiled.Audio.Channels,
                    profiled.Audio.SampleRate,
                    false);
                timer.Stop();
                _audioClipCreateMs = timer.Elapsed.TotalMilliseconds;

                if (generation != _loadGeneration)
                {
                    ReleaseClip();
                    return;
                }

                _preparationStage = PreparationStage.SetPcm;
            }
            catch (Exception ex)
            {
                FailLoad("Recorder FLAC decode failed: " + ex.GetBaseException().Message);
            }
        }

        private void QueueClipForPreparation(AudioClip clip, int generation)
        {
            if (clip == null || generation != _loadGeneration)
            {
                if (clip != null)
                {
                    UnityEngine.Object.Destroy(clip);
                }
                return;
            }

            _preparedClip = clip;
            _preparationStage = PreparationStage.EnsureAudioData;
        }

        private void AdvancePreparationOneStage()
        {
            if (_preparedClip == null)
            {
                return;
            }

            Stopwatch timer;
            switch (_preparationStage)
            {
                case PreparationStage.SetPcm:
                    timer = Stopwatch.StartNew();
                    bool accepted = _preparedClip.SetData(
                        _pendingDecodedAudio.Samples, 0);
                    timer.Stop();
                    _setDataMs = timer.Elapsed.TotalMilliseconds;
                    _pendingDecodedAudio = null;
                    if (!accepted)
                    {
                        FailLoad("Unity rejected the recorder FLAC samples.");
                        return;
                    }
                    _preparationStage = PreparationStage.EnsureAudioData;
                    return;

                case PreparationStage.EnsureAudioData:
                    if (_preparedClip.loadState == AudioDataLoadState.Failed)
                    {
                        FailLoad("Unity could not load the prepared recorder AudioClip data.");
                        return;
                    }
                    if (_preparedClip.loadState == AudioDataLoadState.Unloaded)
                    {
                        timer = Stopwatch.StartNew();
                        _preparedClip.LoadAudioData();
                        timer.Stop();
                        _loadAudioDataMs += timer.Elapsed.TotalMilliseconds;
                        return;
                    }
                    if (_preparedClip.loadState == AudioDataLoadState.Loading)
                    {
                        return;
                    }
                    _preparationStage = PreparationStage.AssignSource;
                    return;

                case PreparationStage.AssignSource:
                    timer = Stopwatch.StartNew();
                    _source.clip = _preparedClip;
                    _source.volume = _volumeState.TargetVolume;
                    _source.mute = true;
                    _volumeState.MarkPrimedMuted();
                    _source.timeSamples = 0;
                    timer.Stop();
                    _audioSourceAssignMs = timer.Elapsed.TotalMilliseconds;
                    _preparationStage = PreparationStage.PrimeSource;
                    return;

                case PreparationStage.PrimeSource:
                    timer = Stopwatch.StartNew();
                    _source.Play();
                    timer.Stop();
                    _sourcePrimeMs = timer.Elapsed.TotalMilliseconds;
                    _preparationStage = PreparationStage.PausePrimedSource;
                    return;

                case PreparationStage.PausePrimedSource:
                    _source.Pause();
                    _source.timeSamples = 0;
                    _source.mute = true;
                    _preparedTimestamp = Stopwatch.GetTimestamp();
                    _preparationStage = PreparationStage.Ready;
                    _loading = false;
                    LogPreparationComplete();
                    if (_playWhenPrepared)
                    {
                        _startPreparedOnNextUpdate = true;
                    }
                    return;
            }
        }

        private void StartPreparedClip()
        {
            if (!IsPrepared || _source == null)
            {
                return;
            }

            Stopwatch timer = Stopwatch.StartNew();
            _source.volume = _volumeState.TargetVolume;
            _source.mute = false;
            _source.UnPause();
            _volumeState.MarkPlaying();
            timer.Stop();
            _playTriggerMs = timer.Elapsed.TotalMilliseconds;
            _playWhenPrepared = false;
            QueuePlaybackDiagnostic();
        }

        private void LogPreparationComplete()
        {
            double elapsedMs = (Stopwatch.GetTimestamp() -
                _prepareStartedTimestamp) * 1000d / Stopwatch.Frequency;
            Plugin.Log.LogInfo(
                "SoulRecorder AUDIO PREPARED -> " + TrackLabel() +
                " | totalPrepareMs=" + elapsedMs.ToString("0.0") +
                " | fileReadMs=" + _fileReadMs.ToString("0.0") +
                " | decodeMs=" + _decodeMs.ToString("0.0") +
                " | pcmConversionMs=" + _pcmConversionMs.ToString("0.0") +
                " | audioClipCreateMs=" + _audioClipCreateMs.ToString("0.000") +
                " | setDataMs=" + _setDataMs.ToString("0.000") +
                " | loadAudioDataMs=" + _loadAudioDataMs.ToString("0.000") +
                " | audioSourceAssignMs=" + _audioSourceAssignMs.ToString("0.000") +
                " | sourcePrimeMs=" + _sourcePrimeMs.ToString("0.000") +
                " | path=" + _preparationPath +
                " | Unity clip/source operations mainThread=" +
                Thread.CurrentThread.ManagedThreadId);
        }

        private void QueuePlaybackDiagnostic()
        {
            _deferredDiagnostic =
                "SoulRecorder AUDIO START PROFILE -> " + TrackLabel() +
                " | readyAtSeat=" + _readyAtSeat +
                " | prepareRequestToReadyMs=" +
                    _prepareRequestToReadyMs.ToString("0.0") +
                " | framesWaitedAfterPrime=" + _framesWaitedAfterPrime +
                " | animationStartedAfterAudioReady=" +
                    _animationStartedAfterAudioReady +
                " | preparationLeadTimeMs=" +
                    _preparationLeadTimeMs.ToString("0.0") +
                " | audioSourceAssignMs=" +
                    _audioSourceAssignMs.ToString("0.000") +
                " | playTriggerMs=" + _playTriggerMs.ToString("0.000") +
                " | uiCallbacksMs=" + _seatUiCallbacksMs.ToString("0.000") +
                " | playlistEventCallbacksMs=" +
                    _seatEventCallbacksMs.ToString("0.000") +
                " | metadataStateLoggingMs=" +
                    _seatStateMs.ToString("0.000") +
                " | playbackRequestMs=" +
                    _seatPlaybackRequestMs.ToString("0.000") +
                " | totalMainThreadMsAtSeat=" +
                    _seatTotalMs.ToString("0.000") +
                " | mainThread=" + Thread.CurrentThread.ManagedThreadId;
            _deferredDiagnosticFrames = 4;
        }

        private void FlushDeferredDiagnostic()
        {
            if (string.IsNullOrEmpty(_deferredDiagnostic))
            {
                return;
            }
            if (_deferredDiagnosticFrames > 0)
            {
                _deferredDiagnosticFrames--;
                return;
            }

            string message = _deferredDiagnostic;
            _deferredDiagnostic = null;
            Plugin.Log.LogInfo(message);
        }

        private string TrackLabel()
        {
            return CurrentTrack == null
                ? (_preparedClip == null ? "unknown" : _preparedClip.name)
                : CurrentTrack.Artist + " - " + CurrentTrack.Title;
        }

        private void FailLoad(string message)
        {
            _lastError = message;
            _loading = false;
            _playWhenPrepared = false;
            _startPreparedOnNextUpdate = false;
            _preparationStage = PreparationStage.None;
            _volumeState.MarkStopped();
            Plugin.Log.LogError(message);
        }

        private void OnVolumeChanged(float volume)
        {
            _volumeState.UpdateTarget(volume);
            if (_source == null)
            {
                return;
            }

            _source.volume = _volumeState.TargetVolume;
            if (_volumeState.MustRemainMuted)
            {
                _source.mute = true;
            }
        }

        private void ReleaseClip()
        {
            AudioClip old = _source == null ? null : _source.clip;
            if (_source != null)
            {
                _source.clip = null;
            }
            AudioClip prepared = _preparedClip;
            _preparedClip = null;
            if (old != null)
            {
                UnityEngine.Object.Destroy(old);
            }
            if (prepared != null && prepared != old)
            {
                UnityEngine.Object.Destroy(prepared);
            }
        }

        private void ResetTimingMetrics()
        {
            _preparedTimestamp = 0L;
            _fileReadMs = 0d;
            _decodeMs = 0d;
            _pcmConversionMs = 0d;
            _audioClipCreateMs = 0d;
            _setDataMs = 0d;
            _loadAudioDataMs = 0d;
            _audioSourceAssignMs = 0d;
            _sourcePrimeMs = 0d;
            _playTriggerMs = 0d;
            _seatUiCallbacksMs = 0d;
            _seatEventCallbacksMs = 0d;
            _seatStateMs = 0d;
            _seatPlaybackRequestMs = 0d;
            _seatTotalMs = 0d;
            _preparationLeadTimeMs = 0d;
            _prepareRequestToReadyMs = 0d;
            _framesWaitedAfterPrime = 0;
            _animationStartedAfterAudioReady = false;
            _readyAtSeat = false;
            _deferredDiagnostic = null;
            _deferredDiagnosticFrames = 0;
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

        private void OnDestroy()
        {
            if (_settings != null)
            {
                _settings.VolumeChanged -= OnVolumeChanged;
            }
            Stop();
        }
    }
}
