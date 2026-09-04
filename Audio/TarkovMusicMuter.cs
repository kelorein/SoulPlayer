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

        internal void Initialize(SoulPlayerSettings settings)
        {
            _settings = settings;
            ApplyNow();
        }

        internal void ApplyNow()
        {
#if SOULPLAYER_PERF
            SoulPlayer.Utils.RecurringWorkProfiler.Begin(SoulPlayer.Utils.RecurringWorkArea.Other);
            try
            {
#endif
            if (_settings == null || !_settings.MuteTarkovMusic)
            {
                return;
            }

            SoulPlayer.Utils.RecurringWorkProfiler.Mark(SoulPlayer.Utils.RecurringWorkEvent.MixerScan);
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
#if SOULPLAYER_PERF
            }
            finally { SoulPlayer.Utils.RecurringWorkProfiler.End(SoulPlayer.Utils.RecurringWorkArea.Other); }
#endif
        }

        internal void RestoreTarkovMusic()
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
