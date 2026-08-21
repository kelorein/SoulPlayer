using System.Collections.Generic;
using SoulPlayer.Configuration;
using UnityEngine;
using UnityEngine.Audio;

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
        private float _nextApply;

        internal void Initialize(SoulPlayerSettings settings)
        {
            _settings = settings;
            _lastMuteSetting = settings.MuteTarkovMusic;
            _nextApply = 0f;
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
                if (!shouldMute)
                {
                    RestoreTarkovMusic();
                }

                _nextApply = 0f;
            }

            if (shouldMute && Time.unscaledTime >= _nextApply)
            {
                _nextApply = Time.unscaledTime + 3f;
                TryMuteTarkovMusic();
            }
        }

        private void TryMuteTarkovMusic()
        {
            AudioMixer[] mixers = Resources.FindObjectsOfTypeAll<AudioMixer>();

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

                    bool firstDiscovery = !values.ContainsKey(parameter);
                    if (firstDiscovery)
                    {
                        values[parameter] = current;
                    }

                    if (mixer.SetFloat(parameter, -80f) && firstDiscovery)
                    {
                        Plugin.Log.LogInfo(
                            "Muted Tarkov music mixer parameter '" + parameter +
                            "' on '" + mixer.name + "'.");
                    }
                }
            }
        }

        private void RestoreTarkovMusic()
        {
            if (_savedValues.Count == 0)
            {
                return;
            }

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
            Plugin.Log.LogInfo("Restored Tarkov music mixer volume.");
        }

        private void OnDestroy()
        {
            RestoreTarkovMusic();
        }
    }
}
