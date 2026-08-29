using System;
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

        private readonly PostRaidSignalGate _signalGate = new PostRaidSignalGate(
            SettleDelaySeconds,
            DuplicateLockSeconds);

        internal event Action<ExitStatus> RaidResultQueued;

        internal void Queue(ExitStatus outcome)
        {
            NotifyRaidResult(outcome);
            float now = Time.unscaledTime;
            if (!_signalGate.Queue(outcome, now))
            {
                Plugin.Log.LogInfo("Ignored duplicate post-raid signal: " + outcome + ".");
                return;
            }

            // EFT can leave its raid objects alive briefly while the result UI is
            // being assembled. Do not let that transient state pause the result track.
            GameState.SuppressRaidMusicSuspend(RaidStateGraceSeconds);
            Plugin.Log.LogInfo("Queued post-raid signal: " + outcome + ".");
        }

        private void NotifyRaidResult(ExitStatus outcome)
        {
            Action<ExitStatus> handler = RaidResultQueued;
            if (handler == null)
            {
                return;
            }

            foreach (Action<ExitStatus> subscriber in handler.GetInvocationList())
            {
                try
                {
                    subscriber(outcome);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning(
                        "Post-raid cleanup subscriber failed: " + ex.Message);
                }
            }
        }

        private void Update()
        {
            ExitStatus outcome;
            int signals;
            if (!_signalGate.TryTake(Time.unscaledTime, out outcome, out signals))
            {
                return;
            }

            GameState.SuppressRaidMusicSuspend(RaidStateGraceSeconds);
            Plugin.Log.LogInfo(
                "Post-raid result settled after " + signals + " signal(s): " + outcome + ".");

            Plugin.AudioPlayer.PlayPostRaid(outcome);
        }
    }

    internal sealed class PostRaidSignalGate
    {
        private readonly float _settleDelaySeconds;
        private readonly float _duplicateLockSeconds;
        private ExitStatus? _pendingOutcome;
        private float _playAt = -1f;
        private float _ignoreSignalsUntil = -1f;
        private int _signalCount;

        internal PostRaidSignalGate(
            float settleDelaySeconds,
            float duplicateLockSeconds)
        {
            _settleDelaySeconds = Math.Max(0f, settleDelaySeconds);
            _duplicateLockSeconds = Math.Max(0f, duplicateLockSeconds);
        }

        internal bool Queue(ExitStatus outcome, float now)
        {
            if (now < _ignoreSignalsUntil)
            {
                return false;
            }

            _pendingOutcome = outcome;
            _playAt = now + _settleDelaySeconds;
            _signalCount++;
            return true;
        }

        internal bool TryTake(float now, out ExitStatus outcome, out int signals)
        {
            outcome = default(ExitStatus);
            signals = 0;
            if (!_pendingOutcome.HasValue || now < _playAt)
            {
                return false;
            }

            outcome = _pendingOutcome.Value;
            signals = _signalCount;
            _pendingOutcome = null;
            _playAt = -1f;
            _signalCount = 0;
            _ignoreSignalsUntil = now + _duplicateLockSeconds;
            return true;
        }
    }
}
