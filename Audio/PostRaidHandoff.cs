using EFT;
using System;
using System.Collections.Generic;
using SoulPlayer.Library;

namespace SoulPlayer.Audio
{

    internal sealed class MainAutoplayPlan
    {
        internal List<MusicTrack> Tracks;
        internal MusicTrack SelectedTrack;

        internal static MainAutoplayPlan Resolve(IEnumerable<MusicTrack> tracks,
            TrackRoutingService routing, bool shuffle, Func<int, int> selectIndex)
        {
            List<MusicTrack> eligible = routing.SelectEligible(tracks, TrackRoute.Main);
            MusicTrack selected = null;
            if (eligible.Count > 0)
            {
                int index = shuffle ? selectIndex(eligible.Count) : 0;
                selected = eligible[index];
            }
            return new MainAutoplayPlan { Tracks = eligible, SelectedTrack = selected };
        }
    }

    internal sealed class PlaybackCompletionTracker
    {
        private bool _observedPlaying;
        internal void Reset() { _observedPlaying = false; }
        internal void Started(bool isPlaying) { _observedPlaying = isPlaying; }

        internal bool Poll(bool clipAvailable, bool isPlaying, bool loading,
            bool paused, bool pausedForRaid, bool transitioning)
        {
            if (!clipAvailable || loading || paused || pausedForRaid || transitioning)
                return false;
            if (isPlaying)
            {
                _observedPlaying = true;
                return false;
            }
            if (!_observedPlaying) return false;
            _observedPlaying = false;
            // Unity commonly resets time/timeSamples to zero at natural EOF.
            // All explicit Stop/load paths reset this tracker; Pause is gated.
            return true;
        }
    }
}
