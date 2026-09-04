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
        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void AnimatedClipTimingHitsInsertionAndEjectionContactDeterministically()
        {
            float startInteraction =
                ProceduralSoulRecorderHandsView.EnterClipSeconds +
                ProceduralSoulRecorderHandsView.InsertClipSeconds +
                ProceduralSoulRecorderHandsView.ExitClipSeconds;
            float stopInteraction =
                ProceduralSoulRecorderHandsView.StopEnterClipSeconds +
                ProceduralSoulRecorderHandsView.EjectClipSeconds +
                ProceduralSoulRecorderHandsView.ExitClipSeconds;
            float ejectionContact =
                ProceduralSoulRecorderHandsView.StopEnterClipSeconds +
                ProceduralSoulRecorderHandsView.EjectTransferSeconds;

            Assert.InRange(startInteraction, 1.50f, 2.20f);
            Assert.InRange(stopInteraction, 1.30f, 2.00f);
            Assert.True(ejectionContact >
                ProceduralSoulRecorderHandsView.StopEnterClipSeconds);
            Assert.True(ejectionContact <
                ProceduralSoulRecorderHandsView.StopEnterClipSeconds +
                ProceduralSoulRecorderHandsView.EjectClipSeconds);
        }

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
            Assert.InRange(SoulRecorderPresentationTuning.EnterSeconds, 0.30f, 0.45f);
            Assert.InRange(SoulRecorderPresentationTuning.ExitSeconds, 0.25f, 0.40f);
            Assert.InRange(SoulRecorderPresentationTuning.TapeInsertionSeconds, 1.0f, 1.25f);
            Assert.InRange(SoulRecorderPresentationTuning.TapeEjectionSeconds, 0.90f, 1.10f);
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
                new Vector3(0.105f, -0.080f, -0.125f),
                SoulRecorderPresentationTuning.CassetteInsertionStartPosition);
            Assert.Equal(
                new Vector3(0.012f, 0.012f, -0.062f),
                SoulRecorderPresentationTuning.CassetteAlignmentPosition);
            Assert.Equal(
                new Vector3(0f, 0.018f, -0.008f),
                SoulRecorderPresentationTuning.CassetteInsertionEndPosition);
            Assert.Equal(
                new Vector3(0.095f, -0.065f, -0.115f),
                SoulRecorderPresentationTuning.CassetteEjectPosition);
            Assert.NotEqual(
                SoulRecorderPresentationTuning.CassetteInsertionStartPosition,
                SoulRecorderPresentationTuning.CassetteInsertionEndPosition);
            Assert.InRange(
                SoulRecorderPresentationTuning.CassetteAlignmentProgress,
                0.4f,
                0.7f);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void PresentationRootKeepsGameplayPoseSeparateFromPrefabImportCorrection()
        {
            Assert.Equal(
                "SoulRecorder First-Person Presentation",
                SoulRecorderPresentationTuning.PresentationRootName);
            Assert.Equal(Vector3.zero, SoulRecorderPresentationTuning.PrefabLocalPosition);
            Assert.Equal(Vector3.zero, SoulRecorderPresentationTuning.PrefabLocalRotationEuler);
            Assert.Equal(Vector3.one, SoulRecorderPresentationTuning.PrefabLocalScale);
            Assert.NotEqual(Vector3.zero, SoulRecorderPresentationTuning.HeldPosition);
            Assert.Equal(new Vector3(0.060f, -1.460f, 0.360f),
                SoulRecorderPresentationTuning.HeldPosition);
            Assert.All(
                new[]
                {
                    SoulRecorderPresentationTuning.RecorderScale.x,
                    SoulRecorderPresentationTuning.RecorderScale.y,
                    SoulRecorderPresentationTuning.RecorderScale.z
                },
                value => Assert.InRange(value, 0.85f, 0.95f));
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void PackagedViewUsesIndependentPresentationAndCassetteTransforms()
        {
            string repositoryRoot = FindRepositoryRoot();
            string view = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "Recorder",
                "ProceduralSoulRecorderHandsView.cs"));

            Assert.Contains("EnsurePresentationRoot(view)", view);
            Assert.Contains("_presentationRoot.transform.localPosition", view);
            Assert.Contains("_root.transform.localPosition", view);
            Assert.Contains("_cassetteRoot.transform.localPosition", view);
            Assert.DoesNotContain("_handsRoot", view);
            Assert.DoesNotContain("_holdingHand", view);
            Assert.DoesNotContain("_cassetteHand", view);
            Assert.Contains("ResolveCassetteAlignmentPosition", view);
            Assert.Contains("ResolveCassetteEjectPosition", view);
            Assert.DoesNotContain("ApplyHandTransforms", view);
            Assert.Contains("Vector3.Lerp", view);
            Assert.Contains("Quaternion.Slerp", view);
            Assert.Contains("SoulRecorderCassetteVisualState.Inserting", view);
            Assert.Contains("SoulRecorderCassetteVisualState.Seated", view);
            Assert.Contains("SoulRecorderCassetteVisualState.Ejecting", view);
            Assert.Contains("using packaged recorder-only fallback", view);
            Assert.Contains("using the procedural presentation fallback", view);
            Assert.Contains("safe headless", view);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void PlacementToolsExposeReloadableCameraRecorderHandsAndAnchorTuning()
        {
            string repositoryRoot = FindRepositoryRoot();
            string tuner = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "Recorder",
                "DevelopmentSoulRecorderPresentationTuner.cs"));

            Assert.StartsWith("#if SOULPLAYER_PLACEMENT_TOOLS", tuner.TrimStart());
            Assert.Contains("Presentation root position", tuner);
            Assert.Contains("Recorder local position", tuner);
            Assert.Contains("Hands local position", tuner);
            Assert.Contains("Cassette start position", tuner);
            Assert.Contains("Cassette alignment position", tuner);
            Assert.Contains("Cassette inserted position", tuner);
            Assert.Contains("Cassette eject position", tuner);
            Assert.Contains("KeyCode.F7", tuner);
            Assert.Contains("_config.Reload()", tuner);
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
                cassetteFront >
                -(SoulRecorderPresentationTuning.RecorderBodyDepth * 0.5f));
            Assert.True(
                SoulTapeCassetteVisual.Dimensions.x >
                SoulRecorderPresentationTuning.CassetteBayOpeningWidth);
            Assert.True(
                SoulTapeCassetteVisual.Dimensions.z >
                SoulRecorderPresentationTuning.CassetteBayOpeningHeight);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void PlaybackRemainsHeldWithoutChangingCassetteState()
        {
            SoulRecorderPresentationState state = HeldSeatedState();
            state.SetPlayback(true, 1f);

            state.Advance(100f);

            Assert.Equal(SoulRecorderVisualPoseState.Held, state.PoseState);
            Assert.Equal(0f, state.GetOffscreenAmount(100f));
            Assert.True(state.IsPlaying);
            Assert.Equal(SoulRecorderCassetteVisualState.Seated, state.CassetteState);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void EjectionFromPlayingWaitsForRecorderEnterBeforeCassetteTravel()
        {
            SoulRecorderPresentationState state = HeldSeatedState();
            state.SetPlayback(true, 2f);
            state.StartEjection(3f);

            Assert.Equal(SoulRecorderVisualPoseState.Held, state.PoseState);
            Assert.False(state.IsPlaying);
            Assert.Equal(1f, state.GetEjectionCassetteTravel(3f));
            Assert.Equal(0f, state.GetEjectionProgress(3f));
            Assert.Equal(SoulRecorderPresentationTuning.TapeEjectionSeconds,
                state.EjectionTotalSeconds);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void InsertionHasReadableEnterMotionAndContactSettleStages()
        {
            SoulRecorderPresentationState state = new SoulRecorderPresentationState();
            state.Enter(10f);
            state.StartInsertion(10f);

            Assert.Equal(0f, state.GetInsertionProgress(
                10f + SoulRecorderPresentationTuning.CassetteInsertionLeadInSeconds - 0.01f));
            Assert.InRange(state.GetInsertionProgress(
                10f + SoulRecorderPresentationTuning.CassetteInsertionLeadInSeconds + 0.20f),
                0.01f,
                0.99f);
            float settleAt = 10f +
                SoulRecorderPresentationTuning.CassetteInsertionLeadInSeconds +
                SoulRecorderPresentationTuning.CassetteInsertionMotionSeconds + 0.04f;
            Assert.True(state.GetInsertionSettleAmount(settleAt) > 0f);
            Assert.InRange(state.GetInsertionSettleAmount(
                10f + SoulRecorderPresentationTuning.TapeInsertionSeconds), 0f, 0.00001f);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void RapidExitDuringInsertionEjectsFromCurrentTravelWithoutSnapping()
        {
            SoulRecorderPresentationState state = new SoulRecorderPresentationState();
            state.Enter(0f);
            state.StartInsertion(0f);
            float interruptedAt = 0.48f;
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
        public void ResetClearsPlayingAndEjectionStates()
        {
            SoulRecorderPresentationState state = HeldSeatedState();
            state.SetPlayback(true, 2f);
            state.StartEjection(3f);
            Assert.Equal(SoulRecorderCassetteVisualState.Ejecting, state.CassetteState);

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

        private static string FindRepositoryRoot()
        {
            DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "SoulPlayer.csproj")))
                {
                    return current.FullName;
                }
                current = current.Parent;
            }

            throw new DirectoryNotFoundException(
                "SoulPlayer repository root could not be located from the test output.");
        }

        private static SoulRecorderPresentationState HeldSeatedState()
        {
            SoulRecorderPresentationState state = new SoulRecorderPresentationState();
            state.Enter(0f);
            state.Advance(1f);
            state.SeatCassette();
            return state;
        }

    }
}
