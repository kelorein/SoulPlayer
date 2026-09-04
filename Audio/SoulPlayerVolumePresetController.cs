using BepInEx.Configuration;
using SoulPlayer.Configuration;
using UnityEngine;

namespace SoulPlayer.Audio
{
    internal static class SoulPlayerVolumePresetResolver
    {
        internal const float Muted = 0f;
        internal const float Quarter = 0.25f;
        internal const float Half = 0.50f;
        internal const float ThreeQuarters = 0.75f;
        internal const float Full = 1f;

        internal static bool TryResolve(
            bool mutedPressed,
            bool quarterPressed,
            bool halfPressed,
            bool threeQuartersPressed,
            bool fullPressed,
            out float volume)
        {
            if (mutedPressed)
            {
                volume = Muted;
                return true;
            }
            if (quarterPressed)
            {
                volume = Quarter;
                return true;
            }
            if (halfPressed)
            {
                volume = Half;
                return true;
            }
            if (threeQuartersPressed)
            {
                volume = ThreeQuarters;
                return true;
            }
            if (fullPressed)
            {
                volume = Full;
                return true;
            }

            volume = 0f;
            return false;
        }
    }

    /// <summary>
    /// Owns the single per-frame Unity input edge check for volume presets.
    /// Volume application and HUD notification remain event-driven through
    /// SoulPlayerSettings.VolumeChanged.
    /// </summary>
    internal sealed class SoulPlayerVolumePresetController : MonoBehaviour
    {
        private SoulPlayerSettings _settings;

        internal void Initialize(SoulPlayerSettings settings)
        {
            _settings = settings;
        }

        private void Update()
        {
#if SOULPLAYER_PERF
            SoulPlayer.Utils.RecurringWorkProfiler.Begin(SoulPlayer.Utils.RecurringWorkArea.Input);
            try
            {
#endif
            if (_settings == null)
            {
                return;
            }

            float volume;
            if (SoulPlayerVolumePresetResolver.TryResolve(
                ShortcutPressed(_settings.VolumeMutedHotkey),
                ShortcutPressed(_settings.VolumeQuarterHotkey),
                ShortcutPressed(_settings.VolumeHalfHotkey),
                ShortcutPressed(_settings.VolumeThreeQuartersHotkey),
                ShortcutPressed(_settings.VolumeFullHotkey),
                out volume))
            {
                _settings.Volume = volume;
            }
        #if SOULPLAYER_PERF
            }
            finally { SoulPlayer.Utils.RecurringWorkProfiler.End(SoulPlayer.Utils.RecurringWorkArea.Input); }
#endif
        }

        private static bool ShortcutPressed(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None ||
                !Input.GetKeyDown(shortcut.MainKey))
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
    }
}
