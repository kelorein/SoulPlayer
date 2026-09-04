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
        private EEftScreenType? _lastScreen;
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
            BindScreens();
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
                    Plugin.Log.LogWarning("SoulPlayer stable-menu evidence unavailable; new playback waits for readiness: " + ex.Message);
                }
                return new RaidMenuEvidence { Screen = "Unavailable", ResultModel = "Unavailable" };
            }
        }

        private void OnScreenChanged(EEftScreenType screen)
        {
            if (_lastScreen == screen) return;
            EEftScreenType? previous = _lastScreen;
            _lastScreen = screen;
            StableRaidMenuContext.Invalidate();
            if (StableRaidMenuContext.IsReturnScreen(screen,
                Plugin.Settings != null && Plugin.Settings.KeepMusicPlayingAcrossMenus)) _returnScreenShown = true;
            Plugin.AudioPlayer?.RequestRaidReevaluation("ScreenChanged");
            Plugin.AudioPlayer?.RefreshRaidReadiness();
            Plugin.AudioPlayer?.LogMenuTransition(previous, screen);
        }

        private void BindScreens()
        {
            EftScreenManager current = EftScreenManager.Instance;
            if (_screens == current) return;
            if (_screens != null) _screens.OnScreenChanged -= OnScreenChanged;
            _screens = current;
            _lastScreen = null;
            if (_screens != null)
            {
                _screens.OnScreenChanged += OnScreenChanged;
                if (_screens.CurrentScreenController != null)
                    OnScreenChanged(_screens.CurrentScreenController.ScreenType);
            }
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
                BindScreens();
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
