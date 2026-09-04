namespace SoulPlayer.Recorder
{
    /// <summary>
    /// Prevents a stale pre-holster hands controller from being mistaken for a
    /// successfully restored one. EFT can keep the old controller/item visible
    /// through part of DropCurrentController; only a controller observed after
    /// SetInHands was requested, and distinct from the saved controller, is a
    /// valid callback-independent completion signal.
    /// </summary>
    internal sealed class SoulRecorderHandsRestoreObservationGate<TController, TItem>
        where TController : class
        where TItem : class
    {
        private TController _controllerBeforeInteraction;
        private bool _restoreRequested;

        internal bool IsRestoreRequested { get { return _restoreRequested; } }

        internal void Begin(TController controllerBeforeInteraction)
        {
            _controllerBeforeInteraction = controllerBeforeInteraction;
            _restoreRequested = false;
        }

        internal void MarkRestoreRequested()
        {
            _restoreRequested = true;
        }

        internal bool CanComplete(
            TController currentController,
            TItem currentItem,
            TItem expectedItem)
        {
            return _restoreRequested &&
                currentController != null &&
                expectedItem != null &&
                ReferenceEquals(currentItem, expectedItem) &&
                !ReferenceEquals(currentController, _controllerBeforeInteraction);
        }

        internal void Reset()
        {
            _controllerBeforeInteraction = null;
            _restoreRequested = false;
        }
    }

    internal enum SoulRecorderHandsOwnershipPhase
    {
        Free,
        Acquiring,
        Owned,
        Restoring
    }

    /// <summary>
    /// Pure tokenized bookkeeping for asynchronous EFT hands transitions. Each
    /// completion consumes its token once, so late/duplicate callbacks cannot lock
    /// or release a newer interaction.
    /// </summary>
    internal sealed class SoulRecorderHandsOwnershipState<T>
    {
        private int _token;

        internal SoulRecorderHandsOwnershipPhase Phase { get; private set; }
        internal T PreviousItem { get; private set; }
        internal bool IsBusy { get { return Phase != SoulRecorderHandsOwnershipPhase.Free; } }
        internal bool IsOwned { get { return Phase == SoulRecorderHandsOwnershipPhase.Owned; } }

        internal bool BeginAcquire(T previousItem, out int token)
        {
            if (IsBusy)
            {
                token = 0;
                return false;
            }

            PreviousItem = previousItem;
            Phase = SoulRecorderHandsOwnershipPhase.Acquiring;
            token = ++_token;
            return true;
        }

        internal bool ConfirmEmptyHands(int token)
        {
            if (token != _token || Phase != SoulRecorderHandsOwnershipPhase.Acquiring)
            {
                return false;
            }

            Phase = SoulRecorderHandsOwnershipPhase.Owned;
            return true;
        }

        internal bool FailAcquire(int token)
        {
            if (token != _token || Phase != SoulRecorderHandsOwnershipPhase.Acquiring)
            {
                return false;
            }

            Release();
            return true;
        }

        internal bool BeginRestore(out int token, out T previousItem)
        {
            if (Phase != SoulRecorderHandsOwnershipPhase.Owned)
            {
                token = 0;
                previousItem = default(T);
                return false;
            }

            Phase = SoulRecorderHandsOwnershipPhase.Restoring;
            token = _token;
            previousItem = PreviousItem;
            return true;
        }

        internal bool CompleteRestore(int token)
        {
            if (token != _token || Phase != SoulRecorderHandsOwnershipPhase.Restoring)
            {
                return false;
            }

            Release();
            return true;
        }

        internal bool FailRestore(int token)
        {
            return CompleteRestore(token);
        }

        internal void Abandon()
        {
            Release();
        }

        private void Release()
        {
            ++_token;
            PreviousItem = default(T);
            Phase = SoulRecorderHandsOwnershipPhase.Free;
        }
    }
}
