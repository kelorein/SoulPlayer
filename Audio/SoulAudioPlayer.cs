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
        private AudioSource _source;
        private List<MusicTrack> _queue = new List<MusicTrack>();
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

        internal event Action Changed;

        internal MusicTrack CurrentTrack { get; private set; }
        internal bool IsLoading { get { return _loading; } }
        internal bool IsPlaying { get { return _source != null && _source.isPlaying; } }
        internal bool IsPaused { get { return _paused || _pausedForRaid; } }
        internal string LastError { get { return _lastError; } }
        internal float CurrentTime { get { return _source != null && _source.clip != null ? _source.time : 0f; } }
        internal float Duration { get { return _source != null && _source.clip != null ? _source.clip.length : 0f; } }

        internal void Initialize(SoulPlayerSettings settings)
        {
            _settings = settings;
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _source.ignoreListenerPause = true;
            _source.volume = settings.Volume;
        }

        internal void Play(MusicTrack track, IEnumerable<MusicTrack> queue)
        {
            if (track == null)
            {
                return;
            }

            CancelFade();

            _queue = queue == null ? new List<MusicTrack>() : queue.ToList();
            _queueIndex = _queue.FindIndex(item =>
                string.Equals(item.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase));

            if (_queueIndex < 0)
            {
                _queue.Insert(0, track);
                _queueIndex = 0;
            }

            BeginLoad(track);
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
            NotifyChanged();
        }

        internal void Next()
        {
            if (_queue.Count == 0)
            {
                StartFromLibrary();
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
            _source.volume = _settings.Volume;
            NotifyChanged();
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
            if (!_settings.AutoPlayAfterRaid)
            {
                Plugin.Log.LogInfo("Post-raid autoplay is disabled.");
                return;
            }

            // Keep the pre-raid track paused while the outcome playlist is
            // selected and (for FLAC) decoded. This prevents a brief old-song
            // burst between the raid and the new result music.
            _resumeAfterRaidAt = float.PositiveInfinity;

            if (_lastPostRaidOutcome == outcome && Time.realtimeSinceStartup - _lastPostRaidTrigger < 8f)
            {
                return;
            }

            _lastPostRaidOutcome = outcome;
            _lastPostRaidTrigger = Time.realtimeSinceStartup;

            if (Plugin.MusicLibrary.IsScanning && Plugin.MusicLibrary.Tracks.Count == 0)
            {
                _pendingPostRaidOutcome = outcome;
                Plugin.Log.LogInfo("Post-raid music is waiting for the library scan to finish.");
                return;
            }

            StartPostRaidPlayback(outcome);
        }

        private void BeginLoad(MusicTrack track)
        {
            _loadGeneration++;
            int generation = _loadGeneration;
            _lastError = string.Empty;
            _loading = true;
            _paused = false;
            _hasStarted = false;
            CurrentTrack = track;

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
            string uri = new Uri(track.FilePath).AbsoluteUri;

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
                if (_settings.RepeatMode == 2)
                {
                    BeginLoad(CurrentTrack);
                }
                else
                {
                    Next();
                }
            }
        }

        private void StartPostRaidPlayback(ExitStatus outcome)
        {
            bool survived = outcome == ExitStatus.Survived;
            string outcomeFolder = _settings.GetPostRaidFolder(survived);
            List<MusicTrack> allTracks = Plugin.MusicLibrary.Tracks.ToList();
            List<MusicTrack> outcomeTracks = allTracks
                .Where(track => IsInsideFolder(track.FilePath, outcomeFolder))
                .ToList();

            List<MusicTrack> queue = outcomeTracks.Count > 0 ? outcomeTracks : allTracks;
            if (queue.Count == 0)
            {
                Plugin.Log.LogWarning("Post-raid autoplay found no playable tracks.");
                ResumePreRaidTrack();
                return;
            }

            if (outcomeTracks.Count == 0)
            {
                Plugin.Log.LogWarning(
                    "No " + (survived ? "survived" : "death") +
                    " playlist tracks were found. Falling back to the full library.");
            }

            MusicTrack selected = queue[_random.Next(queue.Count)];
            _pausedForRaid = false;
            _resumeAfterRaidAt = -1f;

            if (_source.isPlaying && _settings.PostRaidTransitionSeconds > 0.01f)
            {
                _fadePendingTrack = selected;
                _fadePendingQueue = queue;
                BeginFade(_settings.PostRaidTransitionSeconds, FadeCompletion.StartPendingTrack);
            }
            else
            {
                Play(selected, queue);
            }
            Plugin.Log.LogInfo(
                "Post-raid autoplay: " + outcome + " -> " + selected.Title +
                " (" + queue.Count + " available tracks).");
        }

        private bool StartFromLibrary()
        {
            List<MusicTrack> tracks = Plugin.MusicLibrary.Tracks.ToList();
            if (tracks.Count == 0)
            {
                if (!Plugin.MusicLibrary.IsScanning)
                {
                    _lastError = "No playable tracks are available. Add a music folder first.";
                    NotifyChanged();
                }

                return false;
            }

            int index = _settings.Shuffle ? _random.Next(tracks.Count) : 0;
            Play(tracks[index], tracks);
            return true;
        }

        private static bool IsInsideFolder(string filePath, string folder)
        {
            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(folder))
            {
                return false;
            }

            try
            {
                string root = Path.GetFullPath(folder)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string fullPath = Path.GetFullPath(filePath);
                string prefix = root + Path.DirectorySeparatorChar;
                return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
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

            _source.clip = clip;
            _source.volume = _settings.Volume;
            _source.Play();
            _loading = false;
            _hasStarted = true;
            NotifyChanged();
        }

        private void StopPlayback()
        {
            _source.Stop();
            _paused = false;
            _hasStarted = false;
            NotifyChanged();
        }

        private void ResumePreRaidTrack()
        {
            _resumeAfterRaidAt = -1f;
            _pausedForRaid = false;
            if (!_paused && _source.clip != null)
            {
                _source.UnPause();
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
            _source.volume = _settings.Volume;
            if (completion == FadeCompletion.PauseForRaid)
            {
                _source.Pause();
                _pausedForRaid = true;
                NotifyChanged();
                return;
            }

            if (completion == FadeCompletion.StartPendingTrack)
            {
                MusicTrack track = _fadePendingTrack;
                List<MusicTrack> queue = _fadePendingQueue;
                _fadePendingTrack = null;
                _fadePendingQueue = null;
                if (track != null)
                {
                    Play(track, queue);
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
            _source.volume = _settings.Volume;
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
            ReleaseClip();
        }
    }
}
