using System;

namespace SoulPlayer.Recorder
{
    internal enum SoulRecorderCassetteVisualState
    {
        Hidden,
        Inserting,
        Seated,
        Ejecting
    }

    internal enum SoulRecorderVisualPoseState
    {
        Hidden,
        Entering,
        Held,
        Exiting
    }

    /// <summary>
    /// Pure presentation state used by the procedural Unity view. Recorder gameplay
    /// remains owned by SoulRecorderNativePresentation; this class only describes
    /// how far the replaceable visual should have moved.
    /// </summary>
    internal sealed class SoulRecorderPresentationState
    {
        private float _enterStartedAt;
        private float _exitStartedAt;
        private float _exitStartOffscreenAmount;
        private float _insertionStartedAt;
        private float _ejectionStartedAt;
        private float _ejectionStartTravel;
        private float _ejectionLeadInSeconds;
        private float _ejectionMotionSeconds;

        internal SoulRecorderVisualPoseState PoseState { get; private set; }
        internal bool IsVisible { get { return PoseState != SoulRecorderVisualPoseState.Hidden; } }
        internal bool IsEntering { get { return PoseState == SoulRecorderVisualPoseState.Entering; } }
        internal bool IsExiting { get { return PoseState == SoulRecorderVisualPoseState.Exiting; } }
        internal bool IsPlaying { get; private set; }
        internal SoulRecorderCassetteVisualState CassetteState { get; private set; }
        internal float EjectionTotalSeconds { get; private set; }

        internal void Enter(float now)
        {
            Reset();
            PoseState = SoulRecorderVisualPoseState.Entering;
            CassetteState = SoulRecorderCassetteVisualState.Hidden;
            _enterStartedAt = now;
        }

        internal void StartInsertion(float now)
        {
            CassetteState = SoulRecorderCassetteVisualState.Inserting;
            _insertionStartedAt = now;
        }

        internal void SeatCassette()
        {
            CassetteState = SoulRecorderCassetteVisualState.Seated;
        }

        internal void SetPlayback(bool isPlaying, float now)
        {
            IsPlaying = isPlaying;
        }

        internal void Advance(float now)
        {
            if (PoseState == SoulRecorderVisualPoseState.Entering &&
                GetEnterProgress(now) >= 1f)
            {
                PoseState = SoulRecorderVisualPoseState.Held;
            }

        }

        internal void StartEjection(float now)
        {
            Advance(now);
            _ejectionStartTravel = CassetteState == SoulRecorderCassetteVisualState.Inserting
                ? GetInsertionProgress(now)
                : CassetteState == SoulRecorderCassetteVisualState.Hidden ? 0f : 1f;
            bool interruptedInsertion = CassetteState == SoulRecorderCassetteVisualState.Inserting;
            IsPlaying = false;
            CassetteState = SoulRecorderCassetteVisualState.Ejecting;
            _ejectionStartedAt = now;
            _ejectionLeadInSeconds = interruptedInsertion
                ? SoulRecorderPresentationTuning.InterruptedEjectionLeadInSeconds
                : SoulRecorderPresentationTuning.StopEjectionLeadInSeconds;
            _ejectionMotionSeconds = interruptedInsertion
                ? SoulRecorderPresentationTuning.CassetteEjectionMotionSeconds *
                    Math.Max(0.25f, _ejectionStartTravel)
                : SoulRecorderPresentationTuning.CassetteEjectionMotionSeconds;
            EjectionTotalSeconds = _ejectionLeadInSeconds + _ejectionMotionSeconds;
        }

        internal void HideCassette()
        {
            CassetteState = SoulRecorderCassetteVisualState.Hidden;
        }

        internal void Exit(float now)
        {
            _exitStartOffscreenAmount = GetOffscreenAmount(now);
            PoseState = SoulRecorderVisualPoseState.Exiting;
            IsPlaying = false;
            _exitStartedAt = now;
        }

        internal void CompleteExit()
        {
            Reset();
        }

        internal void Reset()
        {
            PoseState = SoulRecorderVisualPoseState.Hidden;
            IsPlaying = false;
            CassetteState = SoulRecorderCassetteVisualState.Hidden;
            EjectionTotalSeconds = SoulRecorderPresentationTuning.TapeEjectionSeconds;
            _enterStartedAt = 0f;
            _exitStartedAt = 0f;
            _exitStartOffscreenAmount = 0f;
            _insertionStartedAt = 0f;
            _ejectionStartedAt = 0f;
            _ejectionStartTravel = 0f;
            _ejectionLeadInSeconds = 0f;
            _ejectionMotionSeconds = SoulRecorderPresentationTuning.CassetteEjectionMotionSeconds;
        }

        internal float GetEnterProgress(float now)
        {
            return Progress(now, _enterStartedAt, SoulRecorderPresentationTuning.EnterSeconds);
        }

        internal float GetExitProgress(float now)
        {
            return Progress(now, _exitStartedAt, SoulRecorderPresentationTuning.ExitSeconds);
        }

        internal float GetInsertionProgress(float now)
        {
            return Progress(
                now,
                _insertionStartedAt +
                    SoulRecorderPresentationTuning.CassetteInsertionLeadInSeconds,
                SoulRecorderPresentationTuning.CassetteInsertionMotionSeconds);
        }

        internal float GetEjectionProgress(float now)
        {
            return Progress(
                now,
                _ejectionStartedAt + _ejectionLeadInSeconds,
                _ejectionMotionSeconds);
        }

        internal float GetInsertionSettleProgress(float now)
        {
            return Progress(
                now,
                _insertionStartedAt +
                    SoulRecorderPresentationTuning.CassetteInsertionLeadInSeconds +
                    SoulRecorderPresentationTuning.CassetteInsertionMotionSeconds,
                SoulRecorderPresentationTuning.CassetteInsertionSettleSeconds);
        }

        internal float GetInsertionSettleAmount(float now)
        {
            float progress = GetInsertionSettleProgress(now);
            return (float)Math.Sin(progress * Math.PI) * (1f - progress);
        }

        internal float GetEjectionCassetteTravel(float now)
        {
            return _ejectionStartTravel *
                   (1f - Smooth(GetEjectionProgress(now)));
        }

        internal float GetOffscreenAmount(float now)
        {
            switch (PoseState)
            {
                case SoulRecorderVisualPoseState.Hidden:
                    return 1f;
                case SoulRecorderVisualPoseState.Entering:
                    return 1f - EaseOutCubic(GetEnterProgress(now));
                case SoulRecorderVisualPoseState.Held:
                    return 0f;
                case SoulRecorderVisualPoseState.Exiting:
                    return _exitStartOffscreenAmount +
                           ((1f - _exitStartOffscreenAmount) *
                            EaseInCubic(GetExitProgress(now)));
                default:
                    return 1f;
            }
        }

        internal static float Progress(float now, float startedAt, float duration)
        {
            if (duration <= 0f)
            {
                return 1f;
            }

            float value = (now - startedAt) / duration;
            if (value <= 0f)
            {
                return 0f;
            }

            return value >= 1f ? 1f : value;
        }

        internal static float Smooth(float progress)
        {
            float value = Math.Max(0f, Math.Min(1f, progress));
            return value * value * (3f - (2f * value));
        }

        internal static float EaseOutCubic(float progress)
        {
            float value = Clamp01(progress);
            float inverse = 1f - value;
            return 1f - (inverse * inverse * inverse);
        }

        internal static float EaseInCubic(float progress)
        {
            float value = Clamp01(progress);
            return value * value * value;
        }

        internal static float SettlePulse(float progress, float start)
        {
            float normalized = Progress(progress, start, 1f - start);
            return (float)Math.Sin(normalized * Math.PI) * (1f - normalized);
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
