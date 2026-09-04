using System;

namespace SoulPlayer.Audio
{
    internal enum SoulPlayerVolumePhase
    {
        Stopped,
        Playing,
        Paused,
        PrimedMuted
    }

    internal sealed class SoulPlayerVolumeState
    {
        internal SoulPlayerVolumeState(float targetVolume)
        {
            UpdateTarget(targetVolume);
            Phase = SoulPlayerVolumePhase.Stopped;
        }

        internal float TargetVolume { get; private set; }
        internal SoulPlayerVolumePhase Phase { get; private set; }
        internal bool MustRemainMuted
        {
            get { return Phase == SoulPlayerVolumePhase.PrimedMuted; }
        }

        internal void UpdateTarget(float volume)
        {
            TargetVolume = Math.Max(0f, Math.Min(1f, volume));
        }

        internal void MarkStopped()
        {
            Phase = SoulPlayerVolumePhase.Stopped;
        }

        internal void MarkPlaying()
        {
            Phase = SoulPlayerVolumePhase.Playing;
        }

        internal void MarkPaused()
        {
            Phase = SoulPlayerVolumePhase.Paused;
        }

        internal void MarkPrimedMuted()
        {
            Phase = SoulPlayerVolumePhase.PrimedMuted;
        }
    }
}
