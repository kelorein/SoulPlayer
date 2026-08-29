using System;
using SoulPlayer.Cassettes;

namespace SoulPlayer.Recorder
{
    internal enum SoulRecorderRaidNextAction
    {
        None = 0,
        BeginEjection = 1,
        BeginInsertion = 2,
        Empty = 3
    }

    internal sealed class SoulRecorderRaidNextDecision
    {
        internal SoulRecorderRaidNextDecision(
            SoulRecorderRaidNextAction action,
            SoulTapeCatalogEntry tape,
            SoulTapeRaidPlaybackSelection selection)
        {
            Action = action;
            Tape = tape;
            Selection = selection;
        }

        internal SoulRecorderRaidNextAction Action { get; private set; }
        internal SoulTapeCatalogEntry Tape { get; private set; }
        internal SoulTapeRaidPlaybackSelection Selection { get; private set; }
    }

    /// <summary>
    /// Pure single-request coordinator for the in-raid next-cassette flow. A tape
    /// is reserved before ejection, then retained while the normal stop transition
    /// runs. Repeated requests cannot create a second transition.
    /// </summary>
    internal sealed class SoulRecorderRaidNextCoordinator
    {
        private bool _requestPending;
        private bool _ejectionRequested;
        private SoulTapeCatalogEntry _reservedTape;

        internal bool IsPending { get { return _requestPending; } }
        internal SoulTapeCatalogEntry ReservedTape { get { return _reservedTape; } }

        internal bool Request()
        {
            if (_requestPending)
            {
                return false;
            }

            _requestPending = true;
            return true;
        }

        internal SoulRecorderRaidNextDecision Evaluate(
            SoulRecorderState state,
            bool transitionBusy,
            Func<SoulTapeRaidPlaybackSelection> takeNext)
        {
            if (!_requestPending || transitionBusy)
            {
                return None();
            }

            if (state != SoulRecorderState.Idle &&
                state != SoulRecorderState.Playing)
            {
                return None();
            }

            if (_reservedTape == null)
            {
                SoulTapeRaidPlaybackSelection selection = takeNext == null
                    ? null
                    : takeNext();
                if (selection == null || selection.Tape == null)
                {
                    Reset();
                    return new SoulRecorderRaidNextDecision(
                        SoulRecorderRaidNextAction.Empty,
                        null,
                        selection);
                }
                _reservedTape = selection.Tape;
            }

            if (state == SoulRecorderState.Playing)
            {
                if (_ejectionRequested)
                {
                    return None();
                }
                _ejectionRequested = true;
                return new SoulRecorderRaidNextDecision(
                    SoulRecorderRaidNextAction.BeginEjection,
                    _reservedTape,
                    null);
            }

            SoulTapeCatalogEntry tape = _reservedTape;
            Reset();
            return new SoulRecorderRaidNextDecision(
                SoulRecorderRaidNextAction.BeginInsertion,
                tape,
                null);
        }

        internal void RejectEjection()
        {
            Reset();
        }

        internal void Reset()
        {
            _requestPending = false;
            _ejectionRequested = false;
            _reservedTape = null;
        }

        private static SoulRecorderRaidNextDecision None()
        {
            return new SoulRecorderRaidNextDecision(
                SoulRecorderRaidNextAction.None, null, null);
        }
    }
}
