using System.Collections.Generic;
using SoulPlayer.Configuration;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace SoulPlayer.Audio
{
    internal sealed class TarkovMusicMuter : MonoBehaviour
    {
        private static readonly string[] MusicParameters =
        {
            "MusicVolume",
            "Music Volume",
            "musicVolume",
            "Music"
        };

        private readonly Dictionary<AudioMixer, Dictionary<string, float>> _savedValues =
            new Dictionary<AudioMixer, Dictionary<string, float>>();

        private SoulPlayerSettings _settings;
        private bool _lastMuteSetting;
        private bool _hasAppliedMute;
        private float _nextRetry;

        internal void Initialize(SoulPlayerSettings settings)
        {
            _settings = settings;
            _lastMuteSetting = settings.MuteTarkovMusic;
            SceneManager.sceneLoaded += OnSceneLoaded;

            if (_lastMuteSetting)
            {
                TryMuteTarkovMusic();
            }
        }

        private void Update()
        {
            if (_settings == null)
            {
                return;
            }

            bool shouldMute = _settings.MuteTarkovMusic;
            if (shouldMute != _lastMuteSetting)
            {
                _lastMuteSetting = shouldMute;
                if (shouldMute)
                {
                    TryMuteTarkovMusic();
                }
                else
                {
                    RestoreTarkovMusic();
                }
            }

            if (shouldMute && !_hasAppliedMute && Time.unscaledTime >= _nextRetry)
            {
                _nextRetry = Time.unscaledTime + 3f;
                TryMuteTarkovMusic();
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_settings != null && _settings.MuteTarkovMusic)
            {
                _hasAppliedMute = false;
                _nextRetry = 0f;
                TryMuteTarkovMusic();
            }
        }

        private void TryMuteTarkovMusic()
        {
            AudioMixer[] mixers = Resources.FindObjectsOfTypeAll<AudioMixer>();
            bool mutedAny = false;

            foreach (AudioMixer mixer in mixers)
            {
                if (mixer == null)
                {
                    continue;
                }

                foreach (string parameter in MusicParameters)
                {
                    float current;
                    if (!mixer.GetFloat(parameter, out current))
                    {
                        continue;
                    }

                    Dictionary<string, float> values;
                    if (!_savedValues.TryGetValue(mixer, out values))
                    {
                        values = new Dictionary<string, float>();
                        _savedValues[mixer] = values;
                    }

                    if (!values.ContainsKey(parameter))
                    {
                        values[parameter] = current;
                    }

                    if (mixer.SetFloat(parameter, -80f))
                    {
                        mutedAny = true;
                        Plugin.Log.LogInfo(
                            "Muted Tarkov music mixer parameter '" + parameter +
                            "' on '" + mixer.name + "'.");
                    }
                }
            }

            _hasAppliedMute = mutedAny;
        }

        private void RestoreTarkovMusic()
        {
            foreach (KeyValuePair<AudioMixer, Dictionary<string, float>> mixerEntry in _savedValues)
            {
                AudioMixer mixer = mixerEntry.Key;
                if (mixer == null)
                {
                    continue;
                }

                foreach (KeyValuePair<string, float> parameter in mixerEntry.Value)
                {
                    mixer.SetFloat(parameter.Key, parameter.Value);
                }
            }

            _savedValues.Clear();
            _hasAppliedMute = false;
            Plugin.Log.LogInfo("Restored Tarkov music mixer volume.");
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            RestoreTarkovMusic();
        }
    }
}
