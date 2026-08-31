using SoulPlayer.Audio;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    [Trait("Validation", "PostRaidLifecycle")]
    public sealed class PlaybackCompletionTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PauseAndRaidSuspensionNeverCountAsCompletion(bool raidPause)
        {
            PlaybackCompletionTracker tracker = new PlaybackCompletionTracker();
            tracker.Started(true);
            for (int frame = 0; frame < 20; frame++)
                Assert.False(tracker.Poll(true, false, false, !raidPause, raidPause, false));
            Assert.False(tracker.Poll(true, true, false, false, false, false));
            Assert.True(tracker.Poll(true, false, false, false, false, false));
            Assert.False(tracker.Poll(true, false, false, false, false, false));
        }

        [Fact]
        public void NeverStartedLoadingAndFadingDoNotLookLikeNaturalEof()
        {
            PlaybackCompletionTracker tracker = new PlaybackCompletionTracker();
            Assert.False(tracker.Poll(true, false, false, false, false, false));
            tracker.Started(true);
            Assert.False(tracker.Poll(true, false, true, false, false, false));
            Assert.False(tracker.Poll(true, false, false, false, false, true));
            Assert.False(tracker.Poll(false, false, false, false, false, false));
            tracker.Reset(); // Explicit Stop/load cannot be mistaken for EOF.
            Assert.False(tracker.Poll(true, false, false, false, false, false));
        }
    }
}
