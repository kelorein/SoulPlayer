using System;
using System.Collections.Generic;
using EFT;
using SoulPlayer.Library;

namespace SoulPlayer.Audio
{
    internal sealed class PostRaidAutoplayPlan
    {
        private PostRaidAutoplayPlan(
            ExitStatus outcome,
            TrackRoute route,
            bool autoplayEnabled,
            List<MusicTrack> eligibleTracks,
            MusicTrack selectedTrack)
        {
            Outcome = outcome;
            Route = route;
            AutoplayEnabled = autoplayEnabled;
            EligibleTracks = eligibleTracks;
            SelectedTrack = selectedTrack;
        }

        internal ExitStatus Outcome { get; private set; }
        internal TrackRoute Route { get; private set; }
        internal bool AutoplayEnabled { get; private set; }
        internal List<MusicTrack> EligibleTracks { get; private set; }
        internal MusicTrack SelectedTrack { get; private set; }
        internal bool ShouldStart
        {
            get { return AutoplayEnabled && SelectedTrack != null; }
        }

        internal static PostRaidAutoplayPlan Resolve(
            ExitStatus outcome,
            bool autoplayEnabled,
            IEnumerable<MusicTrack> tracks,
            TrackRoutingService routing,
            Func<int, int> selectIndex)
        {
            if (routing == null)
            {
                throw new ArgumentNullException("routing");
            }

            TrackRoute route = outcome == ExitStatus.Survived
                ? TrackRoute.Extract
                : TrackRoute.Death;
            List<MusicTrack> eligible = routing.SelectEligible(tracks, route);
            MusicTrack selected = null;

            if (autoplayEnabled && eligible.Count > 0)
            {
                int index = selectIndex == null ? 0 : selectIndex(eligible.Count);
                if (index < 0 || index >= eligible.Count)
                {
                    throw new ArgumentOutOfRangeException(
                        "selectIndex",
                        "The post-raid track selector returned an invalid index.");
                }

                selected = eligible[index];
            }

            return new PostRaidAutoplayPlan(
                outcome,
                route,
                autoplayEnabled,
                eligible,
                selected);
        }
    }

    internal sealed class ExactTrackPlaybackRequest
    {
        private bool _started;

        internal ExactTrackPlaybackRequest(
            int requestId,
            TrackRoute route,
            MusicTrack track)
        {
            if (track == null)
            {
                throw new ArgumentNullException("track");
            }

            RequestId = requestId;
            Route = route;
            Track = track;
            TrackId = TrackRoutingService.GetTrackId(track);
            TrackPath = track.FilePath;
        }

        internal int RequestId { get; private set; }
        internal TrackRoute Route { get; private set; }
        internal MusicTrack Track { get; private set; }
        internal string TrackId { get; private set; }
        internal string TrackPath { get; private set; }
        internal bool HasStarted { get { return _started; } }

        internal bool TryMarkStarted(MusicTrack actualTrack)
        {
            if (_started || actualTrack == null ||
                !ReferenceEquals(Track, actualTrack) ||
                !string.Equals(
                    TrackId,
                    TrackRoutingService.GetTrackId(actualTrack),
                    StringComparison.Ordinal) ||
                !SoulPath.AreEquivalent(TrackPath, actualTrack.FilePath))
            {
                return false;
            }

            _started = true;
            return true;
        }
    }
}
