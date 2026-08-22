using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using SoulPlayer.Audio;
using SoulPlayer.Configuration;
using SoulPlayer.Library;
using UnityEngine;
using UnityEngine.Networking;

namespace SoulPlayer.Recorder
{
    internal sealed class SoulRecorderAudioPlayer : MonoBehaviour
    {
        private SoulPlayerSettings _settings;
        private AudioSource _source;
        private Coroutine _loadCoroutine;
        private Task<DecodedAudio> _flacTask;
        private int _loadGeneration;
        private bool _loading;
        private string _lastError = string.Empty;

        internal MusicTrack CurrentTrack { get; private set; }
        internal bool IsLoading { get { return _loading; } }
        internal bool IsPlaying { get { return _source != null && _source.isPlaying; } }
        internal string LastError { get { return _lastError; } }

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

        internal void Play(MusicTrack track)
        {
            if (track == null)
            {
                return;
            }

            _loadGeneration++;
            int generation = _loadGeneration;
            _lastError = string.Empty;
            _loading = true;
            CurrentTrack = track;

            if (_loadCoroutine != null)
            {
                StopCoroutine(_loadCoroutine);
                _loadCoroutine = null;
            }

            _flacTask = null;
            _source.Stop();
            ReleaseClip();

            Plugin.Log.LogInfo(
                "SoulRecorder AUDIO LOAD -> " + track.Artist + " - " + track.Title +
                " [" + track.Extension + "]");

            if (string.Equals(track.Extension, "FLAC", StringComparison.OrdinalIgnoreCase))
            {
                _flacTask = Task.Run(() => FlacDecoder.Decode(track.FilePath));
            }
            else
            {
                _loadCoroutine = StartCoroutine(LoadUnityAudio(track, generation));
            }
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
            _loading = false;
            _source.Stop();
            ReleaseClip();
            CurrentTrack = null;
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
                    FailLoad("Recorder could not load " + Path.GetFileName(track.FilePath) + ": " + request.error);
                    yield break;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                StartClip(clip, generation);
            }

            _loadCoroutine = null;
        }

        private void Update()
        {
            if (_flacTask == null || !_flacTask.IsCompleted)
            {
                return;
            }

            Task<DecodedAudio> completed = _flacTask;
            _flacTask = null;
            int generation = _loadGeneration;

            try
            {
                DecodedAudio decoded = completed.Result;
                AudioClip clip = AudioClip.Create(
                    CurrentTrack == null ? "SoulRecorder FLAC" : CurrentTrack.Title,
                    decoded.Samples.Length / decoded.Channels,
                    decoded.Channels,
                    decoded.SampleRate,
                    false);

                if (!clip.SetData(decoded.Samples, 0))
                {
                    UnityEngine.Object.Destroy(clip);
                    FailLoad("Unity rejected the recorder FLAC samples.");
                    return;
                }

                StartClip(clip, generation);
            }
            catch (Exception ex)
            {
                FailLoad("Recorder FLAC decode failed: " + ex.GetBaseException().Message);
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
            _source.mute = false;
            _source.Play();
            _loading = false;

            MusicTrack track = CurrentTrack;
            Plugin.Log.LogInfo(
                "SoulRecorder AUDIO START -> " +
                (track == null ? clip.name : track.Artist + " - " + track.Title) +
                " | isPlaying=" + _source.isPlaying +
                " | sourceVolume=" + _source.volume.ToString("0.00") +
                " | listenerVolume=" + AudioListener.volume.ToString("0.00") +
                " | listenerPause=" + AudioListener.pause +
                " | length=" + clip.length.ToString("0.0") + "s");
        }

        private void FailLoad(string message)
        {
            _lastError = message;
            _loading = false;
            Plugin.Log.LogError(message);
        }

        private void ReleaseClip()
        {
            if (_source == null || _source.clip == null)
            {
                return;
            }

            AudioClip old = _source.clip;
            _source.clip = null;
            UnityEngine.Object.Destroy(old);
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
            Stop();
        }
    }
}
