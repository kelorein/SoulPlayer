using Comfort.Common;
using EFT;
using EFT.UI.Matchmaker;
using UnityEngine;

namespace SoulPlayer.Utils
{
    internal static class GameState
    {
        private const float CountdownLookupInterval = 1f;
        private static MatchmakerFinalCountdown _cachedCountdown;
        private static float _nextCountdownLookup;
        private static float _suspendSuppressedUntil = -1f;

        internal static void SuppressRaidMusicSuspend(float seconds)
        {
            _suspendSuppressedUntil = Mathf.Max(
                _suspendSuppressedUntil,
                Time.unscaledTime + Mathf.Max(0f, seconds));
            _cachedCountdown = null;
            _nextCountdownLookup = 0f;
        }

        internal static bool IsInRaid()
        {
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
        }

        internal static bool ShouldSuspendMenuMusic()
        {
            // During post-raid UI construction EFT can briefly keep raid-state
            // objects alive. Ignore that stale state so result music starts once
            // and is not immediately paused/restarted by teardown jitter.
            if (Time.unscaledTime < _suspendSuppressedUntil)
            {
                return false;
            }

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
            GameWorld world = Singleton<GameWorld>.Instance;
            Player mainPlayer = world == null ? null : world.MainPlayer;
            if (mainPlayer != null && mainPlayer.gameObject.activeInHierarchy)
            {
                return true;
            }

            if (_cachedCountdown != null && _cachedCountdown.isActiveAndEnabled)
            {
                return true;
            }

            // During deployment only, perform the fallback lookup at most once per
            // second. This preserves the countdown fade without polling the scene.
            if (Time.unscaledTime < _nextCountdownLookup)
            {
                return false;
            }

            _nextCountdownLookup = Time.unscaledTime + CountdownLookupInterval;
            _cachedCountdown = Object.FindObjectOfType<MatchmakerFinalCountdown>();
            return _cachedCountdown != null && _cachedCountdown.isActiveAndEnabled;
        }
    }
}
