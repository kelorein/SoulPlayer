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
        AutoLowering,
        Lowered,
        RaisingForEject,
        Exiting
    }

    /// <summary>
    /// Pure presentation state used by the procedural Unity view. Recorder gameplay
    /// remains owned by SoulRecorderUsableItemController; this class only describes
    /// how far the replaceable visual should have moved.
    /// </summary>
    internal sealed class SoulRecorderPresentationState
    {
        private float _enterStartedAt;
        private float _exitStartedAt;
        private float _exitStartLoweredAmount;
        private float _insertionStartedAt;
        private float _playbackStartedAt;
        private float _autoLowerStartedAt;
        private float _raiseStartedAt;
        private float _raiseStartLoweredAmount;
        private float _ejectionStartedAt;
        private float _ejectionStartTravel;

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
            if (isPlaying)
            {
                _playbackStartedAt = now;
            }
        }

        internal void Advance(float now)
        {
            if (PoseState == SoulRecorderVisualPoseState.Entering &&
                GetEnterProgress(now) >= 1f)
            {
                PoseState = SoulRecorderVisualPoseState.Held;
            }

            if (IsPlaying && PoseState == SoulRecorderVisualPoseState.Held &&
                now - _playbackStartedAt >= SoulRecorderPresentationTuning.PlaybackVisibleSeconds)
            {
                PoseState = SoulRecorderVisualPoseState.AutoLowering;
                _autoLowerStartedAt = now;
            }

            if (PoseState == SoulRecorderVisualPoseState.AutoLowering &&
                GetAutoLowerProgress(now) >= 1f)
            {
                PoseState = SoulRecorderVisualPoseState.Lowered;
            }

            if (PoseState == SoulRecorderVisualPoseState.RaisingForEject &&
                GetRaiseForEjectProgress(now) >= 1f)
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
            _raiseStartLoweredAmount = GetLoweredAmount(now);
            bool mustRaise = _raiseStartLoweredAmount > 0.001f;

            IsPlaying = false;
            CassetteState = SoulRecorderCassetteVisualState.Ejecting;
            if (mustRaise)
            {
                PoseState = SoulRecorderVisualPoseState.RaisingForEject;
                _raiseStartedAt = now;
                _ejectionStartedAt = now + SoulRecorderPresentationTuning.RaiseForEjectSeconds;
                EjectionTotalSeconds =
                    SoulRecorderPresentationTuning.RaiseForEjectSeconds +
                    SoulRecorderPresentationTuning.TapeEjectionSeconds;
            }
            else
            {
                PoseState = SoulRecorderVisualPoseState.Held;
                _ejectionStartedAt = now;
                EjectionTotalSeconds = SoulRecorderPresentationTuning.TapeEjectionSeconds;
            }
        }

        internal void HideCassette()
        {
            CassetteState = SoulRecorderCassetteVisualState.Hidden;
        }

        internal void Exit(float now)
        {
            _exitStartLoweredAmount = GetLoweredAmount(now);
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
            _exitStartLoweredAmount = 0f;
            _insertionStartedAt = 0f;
            _playbackStartedAt = 0f;
            _autoLowerStartedAt = 0f;
            _raiseStartedAt = 0f;
            _raiseStartLoweredAmount = 0f;
            _ejectionStartedAt = 0f;
            _ejectionStartTravel = 0f;
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
                _insertionStartedAt,
                SoulRecorderPresentationTuning.TapeInsertionSeconds);
        }

        internal float GetAutoLowerProgress(float now)
        {
            return Progress(
                now,
                _autoLowerStartedAt,
                SoulRecorderPresentationTuning.AutoLowerSeconds);
        }

        internal float GetRaiseForEjectProgress(float now)
        {
            return Progress(
                now,
                _raiseStartedAt,
                SoulRecorderPresentationTuning.RaiseForEjectSeconds);
        }

        internal float GetEjectionProgress(float now)
        {
            return Progress(
                now,
                _ejectionStartedAt,
                SoulRecorderPresentationTuning.TapeEjectionSeconds);
        }

        internal float GetEjectionCassetteTravel(float now)
        {
            return _ejectionStartTravel *
                   (1f - Smooth(GetEjectionProgress(now)));
        }

        internal float GetLoweredAmount(float now)
        {
            switch (PoseState)
            {
                case SoulRecorderVisualPoseState.Hidden:
                    return 1f;
                case SoulRecorderVisualPoseState.Entering:
                    return 1f - Smooth(GetEnterProgress(now));
                case SoulRecorderVisualPoseState.Held:
                    return 0f;
                case SoulRecorderVisualPoseState.AutoLowering:
                    return Smooth(GetAutoLowerProgress(now));
                case SoulRecorderVisualPoseState.Lowered:
                    return 1f;
                case SoulRecorderVisualPoseState.RaisingForEject:
                    return _raiseStartLoweredAmount *
                           (1f - Smooth(GetRaiseForEjectProgress(now)));
                case SoulRecorderVisualPoseState.Exiting:
                    return _exitStartLoweredAmount +
                           ((1f - _exitStartLoweredAmount) * Smooth(GetExitProgress(now)));
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
    }
}
