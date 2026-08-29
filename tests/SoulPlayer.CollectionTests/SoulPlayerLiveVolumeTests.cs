using System;
using System.IO;
using SoulPlayer.Audio;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class SoulPlayerLiveVolumeTests
    {
        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void PlayingTrackVolumeUpdatesImmediately()
        {
            SoulPlayerVolumeState state = Playing(0.65f);
            state.UpdateTarget(0.25f);
            Assert.Equal(0.25f, state.TargetVolume, 3);
            Assert.False(state.MustRemainMuted);
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void NextTrackUsesUpdatedVolume()
        {
            SoulPlayerVolumeState state = Playing(0.65f);
            state.UpdateTarget(0.31f);
            state.MarkStopped();
            state.MarkPlaying();
            Assert.Equal(0.31f, state.TargetVolume, 3);
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void ChangingVolumeWhilePrimedDoesNotUnmuteSource()
        {
            SoulPlayerVolumeState state = new SoulPlayerVolumeState(0.65f);
            state.MarkPrimedMuted();
            state.UpdateTarget(0.4f);
            Assert.True(state.MustRemainMuted);
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void LatestVolumeAppliesWhenPrimedTrackActivates()
        {
            SoulPlayerVolumeState state = new SoulPlayerVolumeState(0.65f);
            state.MarkPrimedMuted();
            state.UpdateTarget(0.12f);
            state.UpdateTarget(0.73f);
            state.MarkPlaying();
            Assert.Equal(0.73f, state.TargetVolume, 3);
            Assert.False(state.MustRemainMuted);
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void PausedPlaybackRetainsUpdatedTarget()
        {
            SoulPlayerVolumeState state = Playing(0.65f);
            state.MarkPaused();
            state.UpdateTarget(0.44f);
            Assert.Equal(0.44f, state.TargetVolume, 3);
            Assert.Equal(SoulPlayerVolumePhase.Paused, state.Phase);
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void ZeroVolumeAppliesImmediately()
        {
            SoulPlayerVolumeState state = Playing(0.65f);
            state.UpdateTarget(0f);
            Assert.Equal(0f, state.TargetVolume);
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void RestoringVolumeFromZeroAppliesImmediately()
        {
            SoulPlayerVolumeState state = Playing(0f);
            state.UpdateTarget(0.8f);
            Assert.Equal(0.8f, state.TargetVolume, 3);
        }

        [Fact]
        [Trait("Validation", "AudioPlayback")]
        public void SettingChangedSubscriptionIsInstalledOnceAndCleanedUp()
        {
            string root = FindRepositoryRoot();
            string settings = File.ReadAllText(Path.Combine(root,
                "Configuration", "SoulPlayerSettings.cs"));
            string menu = File.ReadAllText(Path.Combine(root,
                "Audio", "SoulAudioPlayer.cs"));
            string recorder = File.ReadAllText(Path.Combine(root,
                "Recorder", "SoulRecorderAudioPlayer.cs"));

            Assert.Equal(1, Count(settings,
                "_volume.SettingChanged += OnVolumeSettingChanged;"));
            Assert.Contains("_settings.VolumeChanged -= OnVolumeChanged;", menu);
            Assert.Contains("_settings.VolumeChanged += OnVolumeChanged;", menu);
            Assert.Contains("_settings.VolumeChanged -= OnVolumeChanged;", recorder);
            Assert.Contains("_settings.VolumeChanged += OnVolumeChanged;", recorder);
        }

        private static SoulPlayerVolumeState Playing(float volume)
        {
            SoulPlayerVolumeState state = new SoulPlayerVolumeState(volume);
            state.MarkPlaying();
            return state;
        }

        private static int Count(string value, string token)
        {
            int count = 0;
            int offset = 0;
            while ((offset = value.IndexOf(token, offset,
                StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += token.Length;
            }
            return count;
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(
                AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName,
                    "SoulPlayer.csproj")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("SoulPlayer repository root not found.");
        }
    }
}
