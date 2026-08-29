using System;
using System.IO;
using SoulPlayer.Recorder;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class SoulRecorderOverlayTests
    {
        [Fact]
        [Trait("Validation", "RecorderOverlay")]
        public void TimingsStayWithinApprovedPresentationRanges()
        {
            Assert.InRange(SoulRecorderOverlayTimeline.InsertionSeconds(1f),
                1.2f, 1.6f);
            Assert.InRange(SoulRecorderOverlayTimeline.EjectionTotalSeconds(1f),
                1.0f, 1.4f);
            Assert.InRange(SoulRecorderOverlayTimeline.PresentationFadeSeconds,
                0.2f, 0.3f);
        }

        [Fact]
        [Trait("Validation", "RecorderOverlay")]
        public void ApprovedDefaultScaleIsSeventyEightPercent()
        {
            Assert.Equal(0.78f, SoulRecorderOverlaySettings.Default.Scale, 3);
        }

        [Fact]
        [Trait("Validation", "RecorderOverlay")]
        public void InsertionVisitsStatesInOrderAndRotatesBeforeApproach()
        {
            SoulRecorderOverlaySettings settings =
                SoulRecorderOverlaySettings.Default;
            float enter = SoulRecorderOverlayTimeline.InsertEnterSeconds;
            float rotate = SoulRecorderOverlayTimeline.InsertRotateSeconds;
            float approach = SoulRecorderOverlayTimeline.InsertApproachSeconds;
            float half = SoulRecorderOverlayTimeline.InsertHalfSeconds;

            Assert.Equal(SoulRecorderOverlayState.InsertEnter,
                Insert(0f, settings).State);
            Assert.Equal(SoulRecorderOverlayState.InsertRotate,
                Insert(enter + 0.01f, settings).State);
            SoulRecorderOverlayPose aligned = Insert(
                enter + rotate + 0.0001f, settings);
            Assert.Equal(SoulRecorderOverlayState.InsertApproach, aligned.State);
            Assert.InRange(Math.Abs(aligned.CassetteRotationDegrees), 0f, 0.001f);
            SoulRecorderOverlayPose inserting = Insert(
                enter + rotate + approach + half * 0.5f, settings);
            Assert.Equal(SoulRecorderOverlayState.InsertHalf, inserting.State);
            Assert.InRange(Math.Abs(inserting.CassetteRotationDegrees), 0f, 0.001f);
            SoulRecorderOverlayPose seated = Insert(
                SoulRecorderOverlayTimeline.InsertionSeconds(1f) + 0.0001f,
                settings);
            Assert.Equal(SoulRecorderOverlayState.Playing, seated.State);
            Assert.True(seated.LedOn);
        }

        [Fact]
        [Trait("Validation", "RecorderOverlay")]
        public void InsertAndEjectTravelAreSmoothAndOpposite()
        {
            SoulRecorderOverlaySettings settings =
                SoulRecorderOverlaySettings.Default;
            float insertionStart = SoulRecorderOverlayTimeline.InsertEnterSeconds;
            float previousX = Insert(insertionStart, settings).Cassette.CenterX;
            for (int index = 1; index <= 40; index++)
            {
                float time = insertionStart +
                    (SoulRecorderOverlayTimeline.InsertionSeconds(1f) - insertionStart) *
                    index / 40f;
                float currentX = Insert(time, settings).Cassette.CenterX;
                Assert.True(currentX <= previousX + 0.001f);
                previousX = currentX;
            }

            float ejectStart = SoulRecorderOverlayTimeline.EjectStartSeconds;
            previousX = Eject(ejectStart, settings).Cassette.CenterX;
            for (int index = 1; index <= 40; index++)
            {
                float time = ejectStart +
                    (SoulRecorderOverlayTimeline.EjectionTotalSeconds(1f) - ejectStart) *
                    index / 40f;
                float currentX = Eject(time, settings).Cassette.CenterX;
                Assert.True(currentX >= previousX - 0.001f);
                previousX = currentX;
            }
        }

        [Theory]
        [InlineData(1920, 1080, 40f)]
        [InlineData(3440, 1440, 48f)]
        [Trait("Validation", "RecorderOverlay")]
        public void RecorderUsesReducedScaleAndSafeBottomRightMargin(
            int width, int height, float expectedMargin)
        {
            SoulRecorderOverlayPose pose = Insert(
                SoulRecorderOverlayTimeline.InsertionSeconds(1f),
                SoulRecorderOverlaySettings.Default, width, height);
            Assert.InRange(pose.Recorder.Width / width, 0.155f, 0.157f);
            Assert.InRange(width - (pose.Recorder.X + pose.Recorder.Width),
                expectedMargin - 0.01f, expectedMargin + 0.01f);
            Assert.InRange(height - (pose.Recorder.Y + pose.Recorder.Height),
                expectedMargin - 0.01f, expectedMargin + 0.01f);
            Assert.True(pose.Recorder.X > width * 0.70f);
        }

        [Fact]
        [Trait("Validation", "RecorderOverlay")]
        public void AudioPreparationScheduleCanStartAtInsertEnter()
        {
            float duration = SoulRecorderOverlayTimeline.InsertionSeconds(1f);
            float lead = duration;
            float scheduled = SoulRecorderAudioPreparationSchedule.Calculate(
                10f, duration, lead);

            Assert.Equal(1.24f, lead, 3);
            Assert.Equal(10f, scheduled, 3);
        }

        [Fact]
        [Trait("Validation", "RecorderOverlay")]
        public void AnimationWaitsForPreparedAudioAndTwoSafetyFrames()
        {
            string root = FindRepositoryRoot();
            string controller = File.ReadAllText(Path.Combine(
                root, "Recorder", "SoulRecorderUsableItemController.cs"));

            Assert.Contains("AudioPrimeSafetyFrames = 2", controller);
            Assert.Contains("AudioPreparationTimeoutSeconds = 15f", controller);
            Assert.Contains("if (_audioPlayer.IsPrepared)", controller);
            Assert.Contains("StartInsertionPresentation(unscaledTime)", controller);
            Assert.Contains("RecordAnimationGateTiming", controller);
            Assert.Contains("AbortPreAnimationPreparation", controller);
            Assert.Contains("_lifecycle.ForceCleanup();", controller);
            int prepare = controller.IndexOf("EnsureAudioPrepared();",
                StringComparison.Ordinal);
            int presentation = controller.IndexOf(
                "private void StartInsertionPresentation",
                StringComparison.Ordinal);
            Assert.True(prepare >= 0 && presentation > prepare);
        }

        [Fact]
        [Trait("Validation", "RecorderOverlay")]
        public void RuntimePreparesAudioBeforeUsingLightweightPlaybackTrigger()
        {
            string root = FindRepositoryRoot();
            string controller = File.ReadAllText(Path.Combine(
                root, "Recorder", "SoulRecorderUsableItemController.cs"));
            string audio = File.ReadAllText(Path.Combine(
                root, "Recorder", "SoulRecorderAudioPlayer.cs"));

            Assert.Contains("_audioPlayer.Prepare(_activeTape)", controller);
            Assert.Contains("_audioPlayer.PlayPrepared(_activeTape)", controller);
            Assert.Contains("AUDIO PREPARED", audio);
            Assert.Contains("totalPrepareMs=", audio);
            Assert.Contains("playTriggerMs=", audio);
            Assert.Contains("handler.compressed = false", audio);
            Assert.Contains("_preparedClip.LoadAudioData()", audio);
            Assert.Contains("_source.clip = _preparedClip", audio);
            Assert.Contains("_source.Play()", audio);
            Assert.Contains("_source.Pause()", audio);
            Assert.Contains("_source.UnPause()", audio);
            Assert.Contains("totalMainThreadMsAtSeat=", audio);
            Assert.Contains("prepareRequestToReadyMs=", audio);
            Assert.Contains("framesWaitedAfterPrime=", audio);
            Assert.Contains("animationStartedAfterAudioReady=", audio);
            Assert.DoesNotContain("_audioPlayer.Play(_activeTape)", controller);
        }

        [Fact]
        [Trait("Validation", "RecorderOverlay")]
        public void ExistingOnePointZeroDefaultMigratesWithoutOverwritingCustomScale()
        {
            string root = FindRepositoryRoot();
            string settings = File.ReadAllText(Path.Combine(
                root, "Configuration", "SoulPlayerSettings.cs"));

            Assert.Contains("Math.Abs(_recorderOverlayScale.Value - 1f)", settings);
            Assert.Contains("_recorderOverlayScale.Value = 0.78f", settings);
            Assert.Contains("_recorderOverlayDefaultsVersion.Value = 1", settings);
        }

        [Fact]
        [Trait("Validation", "RecorderOverlay")]
        public void SpeedMultiplierChangesTimingWithoutChangingFinalPose()
        {
            SoulRecorderOverlaySettings normal =
                SoulRecorderOverlaySettings.Default;
            SoulRecorderOverlaySettings fast = normal;
            fast.AnimationSpeed = 2f;
            Assert.Equal(SoulRecorderOverlayTimeline.InsertionSeconds(1f) * 0.5f,
                SoulRecorderOverlayTimeline.InsertionSeconds(2f), 4);
            SoulRecorderOverlayPose a = Insert(
                SoulRecorderOverlayTimeline.InsertionSeconds(1f), normal);
            SoulRecorderOverlayPose b = Insert(
                SoulRecorderOverlayTimeline.InsertionSeconds(2f), fast);
            Assert.Equal(a.Cassette.CenterX, b.Cassette.CenterX, 3);
            Assert.Equal(a.Cassette.CenterY, b.Cassette.CenterY, 3);
        }

        [Fact]
        [Trait("Validation", "RecorderOverlay")]
        public void RuntimeUsesEmbeddedPngsAndFailsHeadlessWithoutBlockingMusic()
        {
            string root = FindRepositoryRoot();
            string view = File.ReadAllText(Path.Combine(
                root, "Recorder", "SoulRecorderOverlayView.cs"));
            string project = File.ReadAllText(Path.Combine(root, "SoulPlayer.csproj"));
            string plugin = File.ReadAllText(Path.Combine(root, "Plugin.cs"));

            Assert.Contains("GetManifestResourceStream", view);
            Assert.Contains("music interaction remains available", view);
            Assert.Contains("return HeadlessSoulRecorderHandsView.Instance",
                File.ReadAllText(Path.Combine(root, "Recorder", "SoulRecorderController.cs")));
            Assert.Contains("EmbeddedResource Include=\"Assets\\SoulRecorder\\Overlay", project);
            Assert.DoesNotContain("RecorderAssets.Prewarm()", plugin);
            Assert.DoesNotContain("SoulRecorderNativePresentationPatch.Enable()", plugin);
        }

        [Fact]
        [Trait("Validation", "RecorderOverlay")]
        public void OfflinePreviewUsesExactRuntimeTimelineAndHasRequiredArtifacts()
        {
            string root = FindRepositoryRoot();
            string builder = File.ReadAllText(Path.Combine(root, "Assets",
                "SoulRecorder", "UnityProject", "Assets", "Editor",
                "SoulRecorderOverlayPreviewBuilder.cs"));
            Assert.Contains("SoulRecorderOverlayTimeline.SampleInsertion", builder);
            Assert.Contains("SoulRecorderOverlayTimeline.SampleEjection", builder);
            RequireNonEmpty(root, "Artifacts", "SoulRecorderOverlayPreview",
                "soulrecorder-overlay-16x9.png");
            RequireNonEmpty(root, "Artifacts", "SoulRecorderOverlayPreview",
                "soulrecorder-overlay-21x9.png");
            RequireNonEmpty(root, "Artifacts", "SoulRecorderOverlayPreview",
                "soulrecorder-overlay-key-states.png");
        }

        [Fact]
        [Trait("Validation", "RecorderOverlay")]
        public void StatusOverlaySupportsEveryRequestedLifecycleState()
        {
            Assert.Equal("PREPARING AUDIO",
                SoulRecorderStatusOverlayLayout.StatusText(
                    SoulRecorderStatusOverlayState.PreparingAudio));
            Assert.Equal("INSERTING CASSETTE",
                SoulRecorderStatusOverlayLayout.StatusText(
                    SoulRecorderStatusOverlayState.InsertingCassette));
            Assert.Equal("READY",
                SoulRecorderStatusOverlayLayout.StatusText(
                    SoulRecorderStatusOverlayState.Ready));
            Assert.Equal("PLAYING",
                SoulRecorderStatusOverlayLayout.StatusText(
                    SoulRecorderStatusOverlayState.Playing));
            Assert.Equal("EJECTING",
                SoulRecorderStatusOverlayLayout.StatusText(
                    SoulRecorderStatusOverlayState.Ejecting));
            Assert.Equal(string.Empty,
                SoulRecorderStatusOverlayLayout.StatusText(
                    SoulRecorderStatusOverlayState.Hidden));
        }

        [Fact]
        [Trait("Validation", "RecorderOverlay")]
        public void StatusOverlayIsCompactAndKeepsEstablishedScreenPosition()
        {
            SoulRecorderStatusOverlayLayoutResult normal =
                SoulRecorderStatusOverlayLayout.Calculate(1920, 1080);
            SoulRecorderStatusOverlayLayoutResult ultrawide =
                SoulRecorderStatusOverlayLayout.Calculate(3440, 1440);

            Assert.Equal(404f, normal.Panel.Width, 2);
            Assert.Equal(88f, normal.Panel.Height, 2);
            Assert.Equal(1080f * 0.70f, normal.Panel.Y, 2);
            Assert.True(normal.Panel.Width < 430f);
            Assert.True(normal.Panel.Height < 104f);
            Assert.InRange(ultrawide.Scale, 1.19f, 1.21f);
            Assert.Equal(3440f * 0.5f,
                ultrawide.Panel.X + ultrawide.Panel.Width * 0.5f, 2);
        }

        [Fact]
        [Trait("Validation", "RecorderOverlay")]
        public void StatusOverlayFadesAndSlidesWithoutChangingLifecycleTiming()
        {
            SoulRecorderStatusOverlayAnimation animation =
                new SoulRecorderStatusOverlayAnimation();
            SoulRecorderStatusOverlayFrame entering = animation.Sample(
                SoulRecorderStatusOverlayState.PreparingAudio, 10f);
            SoulRecorderStatusOverlayFrame visible = animation.Sample(
                SoulRecorderStatusOverlayState.PreparingAudio,
                10f + SoulRecorderStatusOverlayLayout.FadeInSeconds);
            SoulRecorderStatusOverlayFrame changedStatus = animation.Sample(
                SoulRecorderStatusOverlayState.InsertingCassette, 10.5f);
            animation.Sample(SoulRecorderStatusOverlayState.Hidden, 11f);
            SoulRecorderStatusOverlayFrame leaving = animation.Sample(
                SoulRecorderStatusOverlayState.Hidden,
                11f + SoulRecorderStatusOverlayLayout.FadeOutSeconds * 0.5f);
            SoulRecorderStatusOverlayFrame hidden = animation.Sample(
                SoulRecorderStatusOverlayState.Hidden,
                11f + SoulRecorderStatusOverlayLayout.FadeOutSeconds);

            Assert.Equal(0f, entering.Alpha, 3);
            Assert.True(entering.SlidePixels > 0f);
            Assert.Equal(1f, visible.Alpha, 3);
            Assert.Equal(1f, changedStatus.Alpha, 3);
            Assert.Equal(SoulRecorderStatusOverlayState.InsertingCassette,
                changedStatus.State);
            Assert.InRange(leaving.Alpha, 0.49f, 0.51f);
            Assert.Equal(SoulRecorderStatusOverlayState.Hidden, hidden.State);
            Assert.Equal(0f, hidden.Alpha, 3);
        }

        [Fact]
        [Trait("Validation", "RecorderOverlay")]
        public void StatusPreviewUsesSharedRuntimeLayoutAndRequiredResolutions()
        {
            string root = FindRepositoryRoot();
            string builder = File.ReadAllText(Path.Combine(root, "Assets",
                "SoulRecorder", "UnityProject", "Assets", "Editor",
                "SoulRecorderStatusOverlayPreviewBuilder.cs"));
            Assert.Contains("SoulRecorderStatusOverlayLayout.Calculate", builder);
            Assert.Contains("SoulRecorderOverlayTimeline.SampleInsertion", builder);
            RequireNonEmpty(root, "Artifacts", "SoulRecorderStatusOverlayPreview",
                "soulrecorder-status-1920x1080.png");
            RequireNonEmpty(root, "Artifacts", "SoulRecorderStatusOverlayPreview",
                "soulrecorder-status-3440x1440.png");
        }

        private static SoulRecorderOverlayPose Insert(float time,
            SoulRecorderOverlaySettings settings, int width = 1920,
            int height = 1080)
        {
            return SoulRecorderOverlayTimeline.SampleInsertion(
                width, height, time, settings);
        }

        private static SoulRecorderOverlayPose Eject(float time,
            SoulRecorderOverlaySettings settings)
        {
            return SoulRecorderOverlayTimeline.SampleEjection(
                1920, 1080, time, settings);
        }

        private static void RequireNonEmpty(string root, params string[] parts)
        {
            string path = Path.Combine(parts);
            path = Path.Combine(root, path);
            Assert.True(File.Exists(path), path);
            Assert.True(new FileInfo(path).Length > 0, path);
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "SoulPlayer.csproj")))
                    return current.FullName;
                current = current.Parent;
            }
            throw new DirectoryNotFoundException("SoulPlayer repository root not found.");
        }
    }
}
