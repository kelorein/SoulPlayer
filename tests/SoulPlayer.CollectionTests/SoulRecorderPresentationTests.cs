using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using SoulPlayer.Cassettes;
using SoulPlayer.Library;
using SoulPlayer.Recorder;
using SoulPlayer.World;
using UnityEngine;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class SoulRecorderPresentationTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "SoulRecorderPresentation-" + Guid.NewGuid().ToString("N"));

        public SoulRecorderPresentationTests()
        {
            Directory.CreateDirectory(_folder);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void AnimationTimingConstantsArePositiveAndPractical()
        {
            Assert.InRange(SoulRecorderPresentationTuning.EnterSeconds, 0.20f, 0.30f);
            Assert.InRange(SoulRecorderPresentationTuning.ExitSeconds, 0.15f, 0.35f);
            Assert.InRange(SoulRecorderPresentationTuning.TapeInsertionSeconds, 0.50f, 0.70f);
            Assert.InRange(SoulRecorderPresentationTuning.TapeEjectionSeconds, 0.45f, 0.65f);
            Assert.InRange(SoulRecorderPresentationTuning.PlaybackVisibleSeconds, 0.8f, 1.2f);
            Assert.Equal(0.25f, SoulRecorderPresentationTuning.AutoLowerSeconds);
            Assert.InRange(SoulRecorderPresentationTuning.RaiseForEjectSeconds, 0.20f, 0.25f);
            Assert.True(SoulRecorderPresentationTuning.ReelDegreesPerSecond > 0f);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void EnterProgressClampsAndSmoothsWithinUnitInterval()
        {
            SoulRecorderPresentationState state = new SoulRecorderPresentationState();
            state.Enter(10f);

            Assert.Equal(0f, state.GetEnterProgress(9f));
            Assert.InRange(state.GetEnterProgress(10.125f), 0f, 1f);
            Assert.Equal(1f, state.GetEnterProgress(20f));
            Assert.Equal(0f, SoulRecorderPresentationState.Smooth(-2f));
            Assert.Equal(1f, SoulRecorderPresentationState.Smooth(2f));
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void InsertionProgressClampsAndSeatedStateIsDeterministic()
        {
            SoulRecorderPresentationState state = new SoulRecorderPresentationState();
            state.Enter(1f);
            state.StartInsertion(2f);

            Assert.Equal(0f, state.GetInsertionProgress(1f));
            Assert.Equal(1f, state.GetInsertionProgress(20f));
            Assert.Equal(SoulRecorderCassetteVisualState.Inserting, state.CassetteState);

            state.SeatCassette();
            Assert.Equal(SoulRecorderCassetteVisualState.Seated, state.CassetteState);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void EjectionProgressClampsAndStopsPlayback()
        {
            SoulRecorderPresentationState state = new SoulRecorderPresentationState();
            state.Enter(0f);
            state.SeatCassette();
            state.SetPlayback(true, 1f);
            state.StartEjection(5f);

            Assert.False(state.IsPlaying);
            Assert.Equal(SoulRecorderCassetteVisualState.Ejecting, state.CassetteState);
            Assert.Equal(0f, state.GetEjectionProgress(4f));
            Assert.Equal(1f, state.GetEjectionProgress(50f));
            Assert.Equal(0f, state.GetEjectionCassetteTravel(50f));
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void PlaybackStartAndStopOnlyChangeReelState()
        {
            SoulRecorderPresentationState state = new SoulRecorderPresentationState();
            state.Enter(0f);
            state.SeatCassette();

            state.SetPlayback(true, 1f);
            Assert.True(state.IsPlaying);
            Assert.Equal(SoulRecorderCassetteVisualState.Seated, state.CassetteState);

            state.SetPlayback(false, 2f);
            Assert.False(state.IsPlaying);
            Assert.Equal(SoulRecorderCassetteVisualState.Seated, state.CassetteState);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void ResetReturnsPresentationToHiddenIdleState()
        {
            SoulRecorderPresentationState state = new SoulRecorderPresentationState();
            state.Enter(0f);
            state.StartInsertion(0f);
            state.SetPlayback(true, 1f);

            state.Reset();

            Assert.False(state.IsVisible);
            Assert.False(state.IsEntering);
            Assert.False(state.IsExiting);
            Assert.False(state.IsPlaying);
            Assert.Equal(SoulRecorderCassetteVisualState.Hidden, state.CassetteState);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void ProceduralDurationsAreExposedThroughHandsViewContract()
        {
            ISoulRecorderHandsView view = (ISoulRecorderHandsView)
                FormatterServices.GetUninitializedObject(typeof(ProceduralSoulRecorderHandsView));

            Assert.Equal(SoulRecorderPresentationTuning.TapeInsertionSeconds, view.TapeInsertionSeconds);
            Assert.Equal(SoulRecorderPresentationTuning.TapeEjectionSeconds, view.TapeEjectionSeconds);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void ForceResetContractAndHeadlessFallbackRemainSafe()
        {
            ISoulRecorderHandsView fallback = HeadlessSoulRecorderHandsView.Instance;

            fallback.OnInteractionEntered(null);
            fallback.OnTapeInsertionStarted(null);
            fallback.OnTapeInserted(null);
            fallback.OnPlaybackChanged(true);
            fallback.OnTapeEjectionStarted(null);
            fallback.OnTapeEjected(null);
            fallback.OnInteractionExited();
            fallback.ForceReset();

            Assert.True(fallback.TapeInsertionSeconds > 0f);
            Assert.True(fallback.TapeEjectionSeconds > 0f);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void CassetteDimensionsAndWorldFocusRemainAboveTopFace()
        {
            Assert.Equal(new Vector3(0.11f, 0.018f, 0.07f), SoulTapeCassetteVisual.Dimensions);
            Assert.Equal(SoulTapeCassetteVisual.HalfThickness, SoulTapeWorldPickup.CassetteHalfThickness);
            Assert.True(
                SoulTapeWorldPickup.InteractionFocusLocalOffset.y >
                SoulTapeCassetteVisual.HalfThickness);
            Assert.Equal(3, SoulTapeWorldPickup.VisibilitySampleCount);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void CassetteInsertionEndpointsAreDeterministicAndDistinct()
        {
            Assert.Equal(
                new Vector3(0.13f, -0.12f, -0.105f),
                SoulRecorderPresentationTuning.CassetteInsertionStartPosition);
            Assert.Equal(
                new Vector3(0f, 0.018f, -0.0305f),
                SoulRecorderPresentationTuning.CassetteInsertionEndPosition);
            Assert.NotEqual(
                SoulRecorderPresentationTuning.CassetteInsertionStartPosition,
                SoulRecorderPresentationTuning.CassetteInsertionEndPosition);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void CassetteFinalPoseIsInsideBayAndBehindFrontFrame()
        {
            float cassetteFront =
                SoulRecorderPresentationTuning.CassetteInsertionEndPosition.z -
                SoulTapeCassetteVisual.HalfThickness;
            float frameRear =
                SoulRecorderPresentationTuning.CassetteBayFrameZ +
                (SoulRecorderPresentationTuning.CassetteBayFrameDepth * 0.5f);

            Assert.True(cassetteFront > frameRear);
            Assert.True(
                SoulTapeCassetteVisual.Dimensions.x >
                SoulRecorderPresentationTuning.CassetteBayOpeningWidth);
            Assert.True(
                SoulTapeCassetteVisual.Dimensions.z >
                SoulRecorderPresentationTuning.CassetteBayOpeningHeight);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void PlaybackAutoLowersWithoutChangingPlaybackOrCassetteState()
        {
            SoulRecorderPresentationState state = HeldSeatedState();
            state.SetPlayback(true, 1f);

            state.Advance(1f + SoulRecorderPresentationTuning.PlaybackVisibleSeconds - 0.01f);
            Assert.Equal(SoulRecorderVisualPoseState.Held, state.PoseState);

            float lowerStarted = 1f + SoulRecorderPresentationTuning.PlaybackVisibleSeconds;
            state.Advance(lowerStarted);
            Assert.Equal(SoulRecorderVisualPoseState.AutoLowering, state.PoseState);
            state.Advance(lowerStarted + SoulRecorderPresentationTuning.AutoLowerSeconds);

            Assert.Equal(SoulRecorderVisualPoseState.Lowered, state.PoseState);
            Assert.Equal(1f, state.GetLoweredAmount(10f));
            Assert.True(state.IsPlaying);
            Assert.Equal(SoulRecorderCassetteVisualState.Seated, state.CassetteState);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void ExitFromLoweredPlaybackRaisesBeforeCassetteEjection()
        {
            SoulRecorderPresentationState state = LoweredPlayingState();
            state.SetPlayback(false, 3f);
            state.StartEjection(3f);

            Assert.Equal(SoulRecorderVisualPoseState.RaisingForEject, state.PoseState);
            Assert.Equal(1f, state.GetEjectionCassetteTravel(3f));
            Assert.Equal(0f, state.GetEjectionProgress(3f));
            Assert.Equal(
                SoulRecorderPresentationTuning.RaiseForEjectSeconds +
                SoulRecorderPresentationTuning.TapeEjectionSeconds,
                state.EjectionTotalSeconds);

            state.Advance(3f + SoulRecorderPresentationTuning.RaiseForEjectSeconds);
            Assert.Equal(SoulRecorderVisualPoseState.Held, state.PoseState);
            Assert.Equal(
                1f,
                state.GetEjectionCassetteTravel(
                    3f + SoulRecorderPresentationTuning.RaiseForEjectSeconds));
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void RapidExitDuringInsertionEjectsFromCurrentTravelWithoutSnapping()
        {
            SoulRecorderPresentationState state = new SoulRecorderPresentationState();
            state.Enter(0f);
            state.StartInsertion(0f);
            float interruptedAt = 0.20f;
            float insertionTravel = state.GetInsertionProgress(interruptedAt);

            state.StartEjection(interruptedAt);

            Assert.Equal(SoulRecorderCassetteVisualState.Ejecting, state.CassetteState);
            Assert.Equal(insertionTravel, state.GetEjectionCassetteTravel(interruptedAt), 4);
            Assert.True(state.GetEjectionCassetteTravel(interruptedAt) < 1f);
            Assert.True(
                state.GetEjectionCassetteTravel(interruptedAt + state.EjectionTotalSeconds) <
                insertionTravel);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void ResetClearsLoweredAndRaiseForEjectStates()
        {
            SoulRecorderPresentationState state = LoweredPlayingState();
            state.SetPlayback(false, 3f);
            state.StartEjection(3f);
            Assert.Equal(SoulRecorderVisualPoseState.RaisingForEject, state.PoseState);

            state.Reset();

            Assert.Equal(SoulRecorderVisualPoseState.Hidden, state.PoseState);
            Assert.False(state.IsVisible);
            Assert.False(state.IsPlaying);
            Assert.Equal(SoulRecorderCassetteVisualState.Hidden, state.CassetteState);
            Assert.Equal(
                SoulRecorderPresentationTuning.TapeEjectionSeconds,
                state.EjectionTotalSeconds);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void SelectedElectricDreamsStillResolvesForRecorder()
        {
            MusicTrack starter = WriteTrack("Scott Buckley - The Long Dark.mp3", 17);
            MusicTrack electric = WriteTrack("Scott Buckley - Electric Dreams.mp3", 31);
            SoulTapeCatalog catalog = new SoulTapeCatalog(new OfflineTestLog());
            catalog.Refresh(new[] { starter, electric });
            SoulTapeCollection collection = new SoulTapeCollection(
                catalog,
                new InMemoryCollectionStore(),
                new OfflineTestLog());
            Assert.True(collection.BindProfile("presentation-profile"));
            Assert.True(collection.UnlockTape("soul-tape.scott-buckley.electric-dreams"));
            Assert.True(collection.SetRecorderTape("soul-tape.scott-buckley.electric-dreams"));

            SoulTapeRecorderSelectionResult result = SoulTapeRecorderSelector.Resolve(collection);

            Assert.NotNull(result.Tape);
            Assert.Equal("soul-tape.scott-buckley.electric-dreams", result.Tape.Id);
            Assert.Equal("Electric Dreams", result.Tape.Title);
            Assert.False(result.UsedRuntimeFallback);
        }

        public void Dispose()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        private MusicTrack WriteTrack(string fileName, byte seed)
        {
            string path = Path.Combine(_folder, fileName);
            byte[] bytes = Enumerable.Range(0, 4096)
                .Select(value => (byte)((value + seed) % 251))
                .ToArray();
            File.WriteAllBytes(path, bytes);
            return new MusicTrack(path, bytes.Length);
        }

        private static SoulRecorderPresentationState HeldSeatedState()
        {
            SoulRecorderPresentationState state = new SoulRecorderPresentationState();
            state.Enter(0f);
            state.Advance(1f);
            state.SeatCassette();
            return state;
        }

        private static SoulRecorderPresentationState LoweredPlayingState()
        {
            SoulRecorderPresentationState state = HeldSeatedState();
            state.SetPlayback(true, 1f);
            float lowerStarted = 1f + SoulRecorderPresentationTuning.PlaybackVisibleSeconds;
            state.Advance(lowerStarted);
            state.Advance(lowerStarted + SoulRecorderPresentationTuning.AutoLowerSeconds);
            return state;
        }
    }
}
