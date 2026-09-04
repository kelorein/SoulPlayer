namespace SoulPlayer.Recorder
{
    internal enum SoulRecorderPlaybackState
    {
        Stopped,
        Playing
    }

    internal enum SoulRecorderInteractionPhase
    {
        Hidden,
        LoadingTape,
        Ready,
        ExitingAfterStart,
        Ejecting,
        ExitingAfterStop
    }

    /// <summary>
    /// Pure ownership/playback state for the recorder interaction. Runtime code uses
    /// this to keep first-person presentation lifetime independent from audio lifetime.
    /// </summary>
    internal sealed class SoulRecorderInteractionLifecycle
    {
        private bool _restoreRequested;

        internal SoulRecorderPlaybackState PlaybackState { get; private set; }
        internal SoulRecorderInteractionPhase Phase { get; private set; }
        internal bool HasHandsOwnership { get; private set; }
        internal bool IsPresentationVisible
        {
            get { return Phase != SoulRecorderInteractionPhase.Hidden; }
        }

        internal bool BeginStartInteraction()
        {
            if (PlaybackState != SoulRecorderPlaybackState.Stopped ||
                Phase != SoulRecorderInteractionPhase.Hidden)
            {
                return false;
            }

            HasHandsOwnership = true;
            _restoreRequested = false;
            Phase = SoulRecorderInteractionPhase.LoadingTape;
            return true;
        }

        internal void MarkReady()
        {
            if (Phase == SoulRecorderInteractionPhase.LoadingTape)
            {
                Phase = SoulRecorderInteractionPhase.Ready;
            }
        }

        internal bool MarkPlaybackStarted()
        {
            if (Phase != SoulRecorderInteractionPhase.Ready)
            {
                return false;
            }

            PlaybackState = SoulRecorderPlaybackState.Playing;
            Phase = SoulRecorderInteractionPhase.ExitingAfterStart;
            return true;
        }

        internal bool BeginStopInteraction()
        {
            if (PlaybackState != SoulRecorderPlaybackState.Playing ||
                (Phase != SoulRecorderInteractionPhase.Hidden &&
                 Phase != SoulRecorderInteractionPhase.ExitingAfterStart))
            {
                return false;
            }

            PlaybackState = SoulRecorderPlaybackState.Stopped;
            HasHandsOwnership = true;
            _restoreRequested = false;
            Phase = SoulRecorderInteractionPhase.Ejecting;
            return true;
        }

        internal bool BeginReadyEjection()
        {
            if (PlaybackState != SoulRecorderPlaybackState.Stopped ||
                Phase != SoulRecorderInteractionPhase.Ready)
            {
                return false;
            }

            Phase = SoulRecorderInteractionPhase.Ejecting;
            return true;
        }

        internal bool BeginInsertionCancellation()
        {
            if (PlaybackState != SoulRecorderPlaybackState.Stopped ||
                (Phase != SoulRecorderInteractionPhase.LoadingTape &&
                 Phase != SoulRecorderInteractionPhase.Ready))
            {
                return false;
            }

            Phase = SoulRecorderInteractionPhase.Ejecting;
            return true;
        }

        internal void BeginExitAfterStop()
        {
            if (Phase == SoulRecorderInteractionPhase.Ejecting)
            {
                Phase = SoulRecorderInteractionPhase.ExitingAfterStop;
            }
        }

        internal void CompletePresentationExit()
        {
            if (Phase != SoulRecorderInteractionPhase.ExitingAfterStart &&
                Phase != SoulRecorderInteractionPhase.ExitingAfterStop)
            {
                return;
            }

            Phase = SoulRecorderInteractionPhase.Hidden;
            RequestRestore();
        }

        internal void InterruptInsertion()
        {
            if (!BeginInsertionCancellation())
            {
                return;
            }
        }

        internal void PlaybackFinished()
        {
            PlaybackState = SoulRecorderPlaybackState.Stopped;
            Phase = SoulRecorderInteractionPhase.Hidden;
            RequestRestore();
        }

        internal void ForceCleanup()
        {
            PlaybackState = SoulRecorderPlaybackState.Stopped;
            Phase = SoulRecorderInteractionPhase.Hidden;
            RequestRestore();
        }

        internal bool TakeHandsRestoreRequest()
        {
            if (!_restoreRequested)
            {
                return false;
            }

            _restoreRequested = false;
            return true;
        }

        private void RequestRestore()
        {
            if (!HasHandsOwnership)
            {
                return;
            }

            HasHandsOwnership = false;
            _restoreRequested = true;
        }
    }

    internal static class SoulRecorderRaidInputGate
    {
        internal static bool AllowsRecorderInput(
            bool inRaid,
            bool hasLocalPlayer,
            bool playerActive,
            bool playerAlive,
            bool hasHandsController,
            bool raidTerminationSignaled)
        {
            return inRaid && hasLocalPlayer && playerActive && playerAlive &&
                   hasHandsController && !raidTerminationSignaled;
        }
    }

    internal sealed class SoulRecorderInteractionCompletionGate
    {
        private float _ejectionDeadline;
        private bool _ejectionMotionPending;

        internal bool IsInteractionPending { get; private set; }

        internal void BeginInteraction()
        {
            IsInteractionPending = true;
            _ejectionDeadline = 0f;
            _ejectionMotionPending = false;
        }

        internal void ArmEjectionMotion(float deadline)
        {
            _ejectionDeadline = deadline;
            _ejectionMotionPending = true;
        }

        internal bool TryFinishEjectionMotion(float now)
        {
            if (!_ejectionMotionPending || now < _ejectionDeadline)
            {
                return false;
            }

            _ejectionMotionPending = false;
            return true;
        }

        internal bool TryReleaseInteraction()
        {
            if (!IsInteractionPending)
            {
                return false;
            }

            IsInteractionPending = false;
            _ejectionMotionPending = false;
            _ejectionDeadline = 0f;
            return true;
        }
    }
}
