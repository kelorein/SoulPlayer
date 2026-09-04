using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using EFT;
using SoulPlayer.Library;

namespace SoulPlayer.Audio
{
    internal enum RaidPlaybackPhase { Idle, Suspended, Routed, ResumePending, Resuming, Complete }
    internal enum RaidPlaybackActionKind { None, Routed, ResumeExact, Fallback, Finish }
    internal enum PlaybackIntent { Manual, AutomaticMain, Routed, Resume }
    internal enum RaidResumeResult { None, ResumedExact, SelectedFallback, Cancelled }

    internal sealed class MainPlaybackSnapshot
    {
        internal MusicTrack Track;
        internal string TrackId;
        internal string Path;
        internal string Title;
        internal float Seconds;
        internal int Samples;
        internal int SampleRate;
        internal bool WasPlaying;
        internal bool WasPaused;
        internal List<MusicTrack> Queue;
        internal int QueueIndex;
        internal bool Shuffle;
        internal int RepeatMode;

        internal static MainPlaybackSnapshot Capture(MusicTrack track, TrackRoutingService routing,
            float seconds, int samples, int sampleRate, bool playing, bool paused,
            IEnumerable<MusicTrack> queue, int queueIndex, bool shuffle, int repeatMode)
        {
            if (track == null || (!playing && !paused) || !routing.IsEligible(track, TrackRoute.Main))
                return null;
            return new MainPlaybackSnapshot
            {
                Track = track, TrackId = TrackRoutingService.GetTrackId(track),
                Path = SoulPath.NormalizeConfiguredPath(track.FilePath, string.Empty), Title = track.Title,
                Seconds = Math.Max(0f, seconds), Samples = Math.Max(0, samples), SampleRate = sampleRate,
                WasPlaying = playing, WasPaused = paused, Queue = (queue ?? Enumerable.Empty<MusicTrack>()).ToList(),
                QueueIndex = queueIndex, Shuffle = shuffle, RepeatMode = repeatMode
            };
        }

        internal MusicTrack Resolve(IEnumerable<MusicTrack> library, TrackRoutingService routing, Func<string, bool> exists)
        {
            MusicTrack mapped = Remap(TrackId, Path, library, exists);
            return mapped != null && routing.IsEligible(mapped, TrackRoute.Main) ? mapped : null;
        }

        internal static MusicTrack Remap(string id, string path, IEnumerable<MusicTrack> library,
            Func<string, bool> exists)
        {
            List<MusicTrack> available = library.ToList();
            // Audio identity survives renames/moves; prefer the canonical path if
            // several copies share that identity, then fall back to path alone.
            List<MusicTrack> identityMatches = available.Where(track =>
                string.Equals(TrackRoutingService.GetTrackId(track), id, StringComparison.Ordinal)).ToList();
            // Touch the filesystem only for identity/path matches, not every
            // unrelated library entry for each saved queue member.
            return identityMatches.FirstOrDefault(track => SoulPath.AreEquivalent(track.FilePath, path) && exists(track.FilePath)) ??
                identityMatches.FirstOrDefault(track => exists(track.FilePath)) ??
                available.FirstOrDefault(track => SoulPath.AreEquivalent(track.FilePath, path) && exists(track.FilePath));
        }

        internal int ResumeSamples(int clipSamples, int clipRate)
        {
            long position = SampleRate == clipRate ? Samples : (long)(Seconds * clipRate);
            return (int)Math.Max(0, Math.Min(Math.Max(0, clipSamples - 1), position));
        }
    }

    internal sealed class RaidPlaybackAction
    {
        internal RaidPlaybackActionKind Kind;
        internal MusicTrack Track;
        internal List<MusicTrack> Queue;
        internal MainPlaybackSnapshot Snapshot;
        internal TrackRoute Route;
    }

    // The only owner of suspension, route issuance, and saved-Main resumption.
    // Losing raid objects, finishing a clip, or elapsed time cannot open the gate.
    internal sealed class RaidPlaybackSession
    {
        private static readonly RaidPlaybackAction NoAction = new RaidPlaybackAction();
        private MainPlaybackSnapshot _candidate;
        private MainPlaybackSnapshot _captured;
        private bool _resumeCancelled;
        private bool _skipOutcome;
        private bool _routedStarted;
        private bool _exactResumeFailed;
        private bool _diagnosticPending;
        private readonly HashSet<PlaybackIntent> _warnedStarts = new HashSet<PlaybackIntent>();

        private RaidPlaybackPhase _phase;
        internal int Revision { get; private set; }
        internal MainPlaybackSnapshot Captured { get { return _captured; } }
        internal RaidCancellationReason CancellationReason { get; private set; }
        internal RaidPlaybackPhase Phase
        {
            get { return _phase; }
            private set { if (_phase != value) { _phase = value; Revision++; } }
        }
        internal bool MenuReady { get; private set; }
        // Latch the actual stable return, not merely raid teardown or a result.
        // A later UI rebuild/rescan must not turn menu controls into raid controls.
        internal bool HasReturnedToMenu { get; private set; }
        internal bool RecorderUsed { get; private set; }
        internal ExitStatus? Outcome { get; private set; }
        internal MusicTrack RoutedTrack { get; private set; }
        internal MusicTrack ResumeTrack { get; private set; }
        internal RaidResumeResult ResumeResult { get; private set; }
        internal bool MainSuspended { get { return Phase != RaidPlaybackPhase.Idle && Phase != RaidPlaybackPhase.Complete; } }
        internal MainPlaybackSnapshot Candidate { get { return _candidate; } }

        internal bool Begin(MainPlaybackSnapshot snapshot)
        {
            // Duplicate deployment/UI signals cannot overwrite an unresolved capture.
            if (MainSuspended) return false;
            Revision++;
            CancellationReason = RaidCancellationReason.None;
            _candidate = _captured = snapshot;
            _resumeCancelled = _skipOutcome = _routedStarted = _exactResumeFailed = false;
            _diagnosticPending = false;
            _warnedStarts.Clear();
            Outcome = null;
            RoutedTrack = ResumeTrack = null;
            MenuReady = false;
            HasReturnedToMenu = RecorderUsed = false;
            ResumeResult = RaidResumeResult.None;
            Phase = RaidPlaybackPhase.Suspended;
            return true;
        }

        // Called only at a new deployment edge, after the owner has paused audio
        // and invalidated old decoders. Close the returned lifecycle first, then
        // carry its unresolved Main forward instead of capturing an outcome cue.
        internal bool CarryMainToNextDeployment()
        {
            if (!MainSuspended || !HasReturnedToMenu) return false;
            MainPlaybackSnapshot saved = _candidate;
            Finish(RaidResumeResult.None);
            return Begin(saved);
        }

        internal bool NoteSoulRecorderUse()
        {
            if (!MainSuspended || HasReturnedToMenu || RecorderUsed) return false;
            RecorderUsed = true;
            Revision++;
            return true;
        }

        internal bool RecordOutcome(ExitStatus outcome)
        {
            if (Phase != RaidPlaybackPhase.Suspended || Outcome.HasValue) return false;
            Outcome = outcome;
            Revision++;
            return true;
        }

        internal void SetMenuReady(bool ready)
        {
            if (ready && Outcome.HasValue && !HasReturnedToMenu)
            {
                HasReturnedToMenu = true;
                Revision++;
            }
            if (MenuReady != ready) { MenuReady = ready; Revision++; }
        }

        internal bool CanStart(PlaybackIntent intent, Action<string> warning = null, bool readyForDispatch = true)
        {
            bool allowed = intent == PlaybackIntent.Routed
                ? MenuReady && readyForDispatch && Phase == RaidPlaybackPhase.Routed
                : intent == PlaybackIntent.Resume
                    ? MenuReady && readyForDispatch && Phase == RaidPlaybackPhase.Resuming
                    : !MainSuspended;
            if (!allowed && MainSuspended && intent != PlaybackIntent.Manual && _warnedStarts.Add(intent) && warning != null)
                warning("SoulPlayer raid playback: blocked automatic track start while mainSuspended=True; intent=" + intent + ".");
            return allowed;
        }

        internal bool CancelSavedResume(
            RaidCancellationReason reason = RaidCancellationReason.ExplicitPlaybackRequest)
        {
            // Only normal-player decisions AFTER a verified stable return own
            // this snapshot. In-raid controls and recorder audio never own it.
            if (!MainSuspended || !HasReturnedToMenu || reason == RaidCancellationReason.None) return false;
            if (CancellationReason != reason) { CancellationReason = reason; Revision++; }
            _candidate = null;
            _resumeCancelled = true;
            _skipOutcome = true;
            if (MenuReady) Finish(RaidResumeResult.Cancelled);
            else Phase = RaidPlaybackPhase.Suspended; // Keep the silence gate, discard pending decoder work.
            return true;
        }

        internal RaidPlaybackAction TakeNext(bool raidAuto, IEnumerable<MusicTrack> library,
            TrackRoutingService routing, bool shuffle, Func<int, int> selectMain,
            Func<int, int> selectOutcome, Func<string, bool> exists = null)
        {
            if (!MainSuspended || !MenuReady || !Outcome.HasValue ||
                (Phase != RaidPlaybackPhase.Suspended && Phase != RaidPlaybackPhase.ResumePending))
                return NoAction;
            exists = exists ?? File.Exists;
            List<MusicTrack> tracks = library.ToList();
            if (Phase == RaidPlaybackPhase.Suspended)
            {
                if (_skipOutcome) return FinishAction(RaidResumeResult.Cancelled);
                PostRaidAutoplayPlan plan = PostRaidAutoplayPlan.Resolve(Outcome.Value, raidAuto,
                    tracks, routing, selectOutcome);
                if (plan.ShouldStart)
                {
                    RoutedTrack = plan.SelectedTrack;
                    Phase = RaidPlaybackPhase.Routed;
                    return new RaidPlaybackAction { Kind = RaidPlaybackActionKind.Routed,
                        Track = RoutedTrack, Queue = plan.EligibleTracks, Route = plan.Route };
                }
                Phase = RaidPlaybackPhase.ResumePending;
            }
            if (Phase != RaidPlaybackPhase.ResumePending) return NoAction;
            if (_resumeCancelled) return FinishAction(RaidResumeResult.Cancelled);

            MusicTrack exact = _candidate == null || _exactResumeFailed ? null : _candidate.Resolve(tracks, routing, exists);
            if (exact != null)
            {
                // Preserve the saved queue order; remap rescanned objects by ID/path.
                List<MusicTrack> queue = _candidate.Queue.Select(saved =>
                    MainPlaybackSnapshot.Remap(TrackRoutingService.GetTrackId(saved), saved.FilePath, tracks, exists))
                    .Where(track => track != null && exists(track.FilePath) && routing.IsEligible(track, TrackRoute.Main))
                    .Distinct().ToList();
                if (!queue.Contains(exact)) queue.Insert(Math.Min(_candidate.QueueIndex < 0 ? 0 : _candidate.QueueIndex, queue.Count), exact);
                ResumeTrack = exact;
                Phase = RaidPlaybackPhase.Resuming;
                ResumeResult = RaidResumeResult.ResumedExact;
                return new RaidPlaybackAction { Kind = RaidPlaybackActionKind.ResumeExact,
                    Track = exact, Queue = queue, Route = TrackRoute.Main, Snapshot = _candidate };
            }

            MainAutoplayPlan fallback = MainAutoplayPlan.Resolve(tracks.Where(track => exists(track.FilePath) &&
                (!_exactResumeFailed || _captured == null || !SoulPath.AreEquivalent(track.FilePath, _captured.Path))),
                routing, shuffle, selectMain);
            if (fallback.SelectedTrack == null) return FinishAction(RaidResumeResult.None);
            ResumeTrack = fallback.SelectedTrack;
            Phase = RaidPlaybackPhase.Resuming;
            ResumeResult = RaidResumeResult.SelectedFallback;
            return new RaidPlaybackAction { Kind = RaidPlaybackActionKind.Fallback,
                Track = ResumeTrack, Queue = fallback.Tracks, Route = TrackRoute.Main };
        }

        internal void RoutedStarted(MusicTrack track)
        {
            if (Phase == RaidPlaybackPhase.Routed && ReferenceEquals(track, RoutedTrack)) _routedStarted = true;
        }

        internal bool EndRouted(MusicTrack track, bool failed)
        {
            if (Phase != RaidPlaybackPhase.Routed || !ReferenceEquals(track, RoutedTrack) || (!failed && !_routedStarted)) return false;
            Phase = RaidPlaybackPhase.ResumePending;
            return true;
        }

        internal void ResumeStarted(MusicTrack track)
        {
            if (Phase == RaidPlaybackPhase.Resuming && ReferenceEquals(track, ResumeTrack)) Finish(ResumeResult);
        }

        internal void ResumeFailed()
        {
            if (Phase != RaidPlaybackPhase.Resuming) return;
            if (ResumeResult == RaidResumeResult.ResumedExact)
            {
                _exactResumeFailed = true;
                Phase = RaidPlaybackPhase.ResumePending;
            }
            else Finish(RaidResumeResult.None);
        }

        private RaidPlaybackAction FinishAction(RaidResumeResult result)
        {
            Finish(result);
            return new RaidPlaybackAction { Kind = RaidPlaybackActionKind.Finish };
        }

        private void Finish(RaidResumeResult result)
        {
            Phase = RaidPlaybackPhase.Complete;
            ResumeResult = result;
            _candidate = null;
            _diagnosticPending = true;
        }

        internal string TakeDiagnostic()
        {
            if (!_diagnosticPending) return null;
            _diagnosticPending = false;
            return "SoulPlayer raid playback: capturedMain=" + (_captured == null ? "none" : _captured.TrackId + "/" + _captured.Title) +
                " capturedTime=" + (_captured == null ? "0" : _captured.Seconds.ToString("0.000", CultureInfo.InvariantCulture)) +
                " mainSuspended=" + MainSuspended + " outcome=" + (Outcome == null ? "none" : Outcome == ExitStatus.Survived ? "Survived" : "Death") +
                " routedTrack=" + Describe(RoutedTrack) + " menuReady=" + MenuReady + " resumeResult=" + ResumeResult +
                " cancellationReason=" + CancellationReason +
                " recorderUsed=" + RecorderUsed + " snapshotPreservedThroughRaid=" + (RecorderUsed && _captured != null) + ".";
        }

        private static string Describe(MusicTrack track)
        {
            return track == null ? "none" : TrackRoutingService.GetTrackId(track) + "/" + track.Title;
        }
    }

}
