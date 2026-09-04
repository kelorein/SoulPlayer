using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SoulPlayer.Library;

namespace SoulPlayer.Audio
{
    internal enum RaidReadinessReason
    {
        WaitingForOutcome, WaitingForReturnScreen, WaitingForRaidPlayerExit,
        WaitingForPreloader, WaitingForBlackOverlay, WaitingForLibrary, Ready
    }

    internal enum RaidCancellationReason
    {
        None, ManualLibrarySelection, Stop, Next, Previous, ExplicitPlaybackRequest
    }

    internal struct RaidMenuEvidence : IEquatable<RaidMenuEvidence>
    {
        internal string Screen;
        internal bool ReturnScreenShown;
        internal bool RecognizedScreen;
        internal bool ScreenActive;
        internal bool PlayerPresent;
        internal bool PlayerAlive;
        internal bool? Preloader;
        internal bool? BlackOverlay;
        internal string ResultModel;

        internal RaidReadinessReason Evaluate(bool outcomeSettled, bool libraryReady)
        {
            if (!outcomeSettled) return RaidReadinessReason.WaitingForOutcome;
            if (!ReturnScreenShown || !RecognizedScreen || !ScreenActive)
                return RaidReadinessReason.WaitingForReturnScreen;
            if (PlayerPresent) return RaidReadinessReason.WaitingForRaidPlayerExit;
            if (Preloader != false) return RaidReadinessReason.WaitingForPreloader;
            if (BlackOverlay != false) return RaidReadinessReason.WaitingForBlackOverlay;
            if (!libraryReady) return RaidReadinessReason.WaitingForLibrary;
            // The result portrait is cosmetic. Valid screen/raid/loader/overlay
            // evidence is sufficient even if its optional model contract is absent.
            return RaidReadinessReason.Ready;
        }

        public bool Equals(RaidMenuEvidence other)
        {
            return Screen == other.Screen && ReturnScreenShown == other.ReturnScreenShown &&
                RecognizedScreen == other.RecognizedScreen && ScreenActive == other.ScreenActive &&
                PlayerPresent == other.PlayerPresent && PlayerAlive == other.PlayerAlive &&
                Preloader == other.Preloader && BlackOverlay == other.BlackOverlay && ResultModel == other.ResultModel;
        }
    }

    // Logs only changes to lifecycle/evidence/library revision, never a frame tick.
    internal sealed class RaidPlaybackStateDiagnostics
    {
        private int _sessionRevision = -1;
        private int _libraryRevision = -1;
        private bool _libraryReady, _raidAuto;
        private RaidMenuEvidence _evidence;

        internal string Observe(RaidPlaybackSession session, RaidMenuEvidence evidence,
            bool libraryReady, int libraryRevision, bool raidAuto,
            IReadOnlyList<MusicTrack> tracks, TrackRoutingService routing, string trigger)
        {
            if (_sessionRevision == session.Revision && _libraryRevision == libraryRevision &&
                _libraryReady == libraryReady && _raidAuto == raidAuto && _evidence.Equals(evidence)) return null;
            _sessionRevision = session.Revision;
            _libraryRevision = libraryRevision;
            _libraryReady = libraryReady;
            _raidAuto = raidAuto;
            _evidence = evidence;
            MainPlaybackSnapshot captured = session.Captured;
            MusicTrack remapped = session.Candidate == null || !libraryReady ? null :
                session.Candidate.Resolve(tracks, routing, System.IO.File.Exists);
            string action = session.ResumeResult == RaidResumeResult.Cancelled ||
                (session.CancellationReason != RaidCancellationReason.None && session.Phase == RaidPlaybackPhase.Suspended) ? "Cancelled" :
                session.Phase == RaidPlaybackPhase.Routed ? "Routed" :
                session.Phase == RaidPlaybackPhase.Resuming || session.Phase == RaidPlaybackPhase.Complete
                    ? (session.ResumeResult == RaidResumeResult.ResumedExact ? "ResumeExact" :
                       session.ResumeResult == RaidResumeResult.SelectedFallback ? "Fallback" : "Waiting")
                    : "Waiting";
            return "SoulPlayer raid playback state: phase=" + session.Phase +
                " capturedMain=" + (captured == null ? "none" : captured.TrackId + "/" + captured.Title) +
                " capturedPath=" + (captured == null ? "none" : captured.Path) +
                " capturedTime=" + (captured == null ? "0" : captured.Seconds.ToString("0.000", CultureInfo.InvariantCulture)) +
                " capturedSamples=" + (captured == null ? "0" : captured.Samples.ToString(CultureInfo.InvariantCulture)) +
                " capturedSampleRate=" + (captured == null ? "0" : captured.SampleRate.ToString(CultureInfo.InvariantCulture)) +
                " capturedPlaying=" + (captured != null && captured.WasPlaying) +
                " capturedPaused=" + (captured != null && captured.WasPaused) +
                " capturedMainEligible=" + (captured != null && routing.IsEligible(captured.Track, TrackRoute.Main)) +
                " mainSuspended=" + session.MainSuspended +
                " outcome=" + (session.Outcome.HasValue ? session.Outcome.Value.ToString() : "none") +
                " screen=" + (evidence.Screen ?? "Unavailable") + " screenActive=" + evidence.ScreenActive +
                " playerAlive=" + evidence.PlayerAlive + " playerPresent=" + evidence.PlayerPresent +
                " preloader=" + Display(evidence.Preloader) + " blackOverlay=" + Display(evidence.BlackOverlay) +
                " resultModel=" + (evidence.ResultModel ?? "Unavailable") +
                " libraryReady=" + libraryReady + " libraryRevision=" + libraryRevision +
                " remappedTrack=" + Describe(remapped) +
                " readiness=" + evidence.Evaluate(session.Outcome.HasValue, libraryReady) +
                " raidAuto=" + raidAuto + " extractEligible=" + routing.SelectEligible(tracks, TrackRoute.Extract).Count +
                " deathEligible=" + routing.SelectEligible(tracks, TrackRoute.Death).Count +
                " routedTrack=" + Describe(session.RoutedTrack) + " resumeTrack=" + Describe(session.ResumeTrack) +
                " cancellationReason=" + session.CancellationReason + " action=" + action +
                " returnedToMenu=" + session.HasReturnedToMenu + " recorderUsed=" + session.RecorderUsed +
                " snapshotPreservedThroughRaid=" + (session.RecorderUsed && captured != null) + " trigger=" + trigger + ".";
        }

        private static string Display(bool? value) { return value.HasValue ? value.Value.ToString() : "Unavailable"; }
        private static string Describe(MusicTrack track)
        {
            return track == null ? "none" : TrackRoutingService.GetTrackId(track) + "/" + track.Title;
        }
    }
}
