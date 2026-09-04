using Comfort.Common;
using EFT;
using EFT.UI.Matchmaker;
using EFT.UI.Screens;
using SoulPlayer.Audio;
using UnityEngine;

namespace SoulPlayer.Utils
{
    internal static class GameState
    {
        private const float CountdownLookupInterval = 1f;
        private static MatchmakerFinalCountdown _cachedCountdown;
        private static float _nextCountdownLookup;

        internal static bool IsInRaid()
        {
#if SOULPLAYER_PERF
            SoulPlayer.Utils.RecurringWorkProfiler.Begin(SoulPlayer.Utils.RecurringWorkArea.GameState);
            try
            {
#endif
            AbstractGame game = Singleton<AbstractGame>.Instance;
            if (game != null && game.InRaid)
            {
                return true;
            }

            IBotGame botGame = Singleton<IBotGame>.Instance;
            if (botGame == null)
            {
                return false;
            }

            GameStatus status = botGame.Status;
            return status != GameStatus.Stopped &&
                   status != GameStatus.Stopping &&
                   status != GameStatus.SoftStopping;
        #if SOULPLAYER_PERF
            }
            finally { SoulPlayer.Utils.RecurringWorkProfiler.End(SoulPlayer.Utils.RecurringWorkArea.GameState); }
#endif
        }

        internal static bool ShouldSuspendMenuMusic()
        {
            if (Plugin.AudioPlayer != null && Plugin.AudioPlayer.IsRaidPlaybackActive)
                return !Plugin.AudioPlayer.RaidOverlayAllowed;
            return IsDeploymentOrLiveRaid();
        }

        internal static bool HasLiveRaidPlayer()
        {
            return HasLiveRaidPlayer(false);
        }

        internal static bool HasLiveRaidPlayer(bool excludeHideoutPlayer)
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            Player player = world == null ? null : world.MainPlayer;
            return MenuPlaybackContinuity.IsBlockingPlayer(
                player != null && player.gameObject.activeInHierarchy,
                player is HideoutPlayer, excludeHideoutPlayer);
        }

        internal static bool HasAliveRaidPlayer()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            Player player = world == null ? null : world.MainPlayer;
            return player != null && player.gameObject.activeInHierarchy &&
                player.HealthController != null && player.HealthController.IsAlive;
        }

        internal static bool IsDeploymentOrLiveRaid()
        {
#if SOULPLAYER_PERF
            SoulPlayer.Utils.RecurringWorkProfiler.Begin(SoulPlayer.Utils.RecurringWorkArea.GameState);
            try
            {
#endif
            // Live gameplay never needs screen/controller/countdown inspection.
            if (HasLiveRaidPlayer() && IsInRaid()) return true;

            // Teardown can briefly revive stale raid/countdown objects. A return
            // screen is not a new deployment; only a real new raid may recapture.
            EftScreenManager screens = EftScreenManager.Instance;
            if (screens != null && screens.CurrentScreenController != null &&
                StableRaidMenuContext.IsReturnScreen(screens.CurrentScreenController.ScreenType) &&
                !HasLiveRaidPlayer())
                return false;

            // AbstractGame.InRaid becomes true while the map is still loading.
            // Keep menu music alive until EFT presents the deployment countdown
            // or the local player is actually present in the live GameWorld.
            if (!IsInRaid())
            {
                _cachedCountdown = null;
                _nextCountdownLookup = 0f;
                return false;
            }

            // The live player is available for almost the entire raid. Check it
            // before looking through the Unity scene for the short-lived countdown.
            // The old order performed a full scene search several times per second
            // throughout every raid, which caused rhythmic frame-time spikes.
            if (HasLiveRaidPlayer())
            {
                return true;
            }

            if (_cachedCountdown != null && _cachedCountdown.isActiveAndEnabled)
            {
                return true;
            }

            // During deployment only, perform the fallback lookup at most once per
            // second. The countdown Show hook captures before this fallback.
            if (Time.unscaledTime < _nextCountdownLookup)
            {
                return false;
            }

            _nextCountdownLookup = Time.unscaledTime + CountdownLookupInterval;
            RecurringWorkProfiler.Mark(RecurringWorkEvent.CountdownSceneSearch);
            _cachedCountdown = Object.FindObjectOfType<MatchmakerFinalCountdown>();
            return _cachedCountdown != null && _cachedCountdown.isActiveAndEnabled;
        #if SOULPLAYER_PERF
            }
            finally { SoulPlayer.Utils.RecurringWorkProfiler.End(SoulPlayer.Utils.RecurringWorkArea.GameState); }
#endif
        }
    }
}
