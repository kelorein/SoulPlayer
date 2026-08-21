using EFT;
using SoulPlayer.Utils;
using UnityEngine;

namespace SoulPlayer.Audio
{
    internal sealed class PostRaidCoordinator : MonoBehaviour
    {
        private const float SettleDelaySeconds = 0.75f;
        private const float DuplicateLockSeconds = 12f;
        private const float RaidStateGraceSeconds = 12f;

        private ExitStatus? _pendingOutcome;
        private float _playAt = -1f;
        private float _ignoreSignalsUntil = -1f;
        private int _signalCount;

        internal void Queue(ExitStatus outcome)
        {
            float now = Time.unscaledTime;
            if (now < _ignoreSignalsUntil)
            {
                Plugin.Log.LogInfo("Ignored duplicate post-raid signal: " + outcome + ".");
                return;
            }

            _pendingOutcome = outcome;
            _playAt = now + SettleDelaySeconds;
            _signalCount++;

            // EFT can leave its raid objects alive briefly while the result UI is
            // being assembled. Do not let that transient state pause the result track.
            GameState.SuppressRaidMusicSuspend(RaidStateGraceSeconds);
            Plugin.Log.LogInfo("Queued post-raid signal: " + outcome + ".");
        }

        private void Update()
        {
            if (!_pendingOutcome.HasValue || Time.unscaledTime < _playAt)
            {
                return;
            }

            ExitStatus outcome = _pendingOutcome.Value;
            int signals = _signalCount;

            _pendingOutcome = null;
            _playAt = -1f;
            _signalCount = 0;
            _ignoreSignalsUntil = Time.unscaledTime + DuplicateLockSeconds;

            GameState.SuppressRaidMusicSuspend(RaidStateGraceSeconds);
            Plugin.Log.LogInfo(
                "Post-raid result settled after " + signals + " signal(s): " + outcome + ".");

            Plugin.AudioPlayer.PlayPostRaid(outcome);
        }
    }
}
