using System;
using EFT;
using EFT.UI.Screens;
using UnityEngine;

namespace SoulPlayer.Audio
{
    // Adapts EFT lifecycle evidence; the audio session alone owns playback.
    internal sealed class PostRaidCoordinator : MonoBehaviour
    {
        private bool _returnScreenShown;
        private bool _readinessWarningLogged;
        private EftScreenManager _screens;
        internal event Action<ExitStatus> RaidResultQueued;

        internal void BeginRaid()
        {
            StableRaidMenuContext.Invalidate();
            _returnScreenShown = false;
            _readinessWarningLogged = false;
        }

        internal void MenuScreenShown()
        {
            StableRaidMenuContext.Invalidate();
            _returnScreenShown = true;
            Plugin.AudioPlayer?.RequestRaidReevaluation("MenuScreenShown");
        }

        internal void Queue(ExitStatus outcome)
        {
            SoulAudioPlayer player = Plugin.AudioPlayer;
            if (player == null || !player.ObserveRaidResult(outcome)) return;
            StableRaidMenuContext.Invalidate();
            _returnScreenShown = true;
            NotifyRaidResult(outcome);
            player.RequestRaidReevaluation("Outcome");
        }

        private void NotifyRaidResult(ExitStatus outcome)
        {
            Action<ExitStatus> handler = RaidResultQueued;
            if (handler == null) return;
            foreach (Action<ExitStatus> subscriber in handler.GetInvocationList())
            {
                try { subscriber(outcome); }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("Post-raid cleanup subscriber failed: " + ex.Message);
                }
            }
        }

        internal RaidMenuEvidence ReadEvidence()
        {
            try { return StableRaidMenuContext.Read(_returnScreenShown); }
            catch (Exception ex)
            {
                if (!_readinessWarningLogged)
                {
                    _readinessWarningLogged = true;
                    Plugin.Log.LogWarning("SoulPlayer stable-menu evidence unavailable; playback stays suspended: " + ex.Message);
                }
                return new RaidMenuEvidence { Screen = "Unavailable", ResultModel = "Unavailable" };
            }
        }

        private void OnScreenChanged(EEftScreenType screen)
        {
            StableRaidMenuContext.Invalidate();
            if (StableRaidMenuContext.IsReturnScreen(screen)) _returnScreenShown = true;
            Plugin.AudioPlayer?.RequestRaidReevaluation("ScreenChanged");
        }

        private void LateUpdate()
        {
            if (Plugin.AudioPlayer == null || !Plugin.AudioPlayer.NeedsRaidReadinessInspection) return;
#if SOULPLAYER_PERF
            SoulPlayer.Utils.RecurringWorkProfiler.Begin(SoulPlayer.Utils.RecurringWorkArea.Readiness);
            try
            {
#endif
            EftScreenManager current = EftScreenManager.Instance;
            if (_screens != current)
            {
                if (_screens != null) _screens.OnScreenChanged -= OnScreenChanged;
                _screens = current;
                if (_screens != null) _screens.OnScreenChanged += OnScreenChanged;
            }
            // Screen events request reevaluation; this also observes loader/black
            // overlay changes that occur without a screen-controller change.
            if (Plugin.AudioPlayer != null && Plugin.AudioPlayer.IsRaidPlaybackActive)
                Plugin.AudioPlayer.RefreshRaidReadiness();
        #if SOULPLAYER_PERF
            }
            finally { SoulPlayer.Utils.RecurringWorkProfiler.End(SoulPlayer.Utils.RecurringWorkArea.Readiness); }
#endif
        }

        private void OnDestroy()
        {
            if (_screens != null) _screens.OnScreenChanged -= OnScreenChanged;
        }
    }
}
