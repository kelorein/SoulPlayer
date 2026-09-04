using System;
using System.Collections.Generic;
using System.IO;
using SoulPlayer.Recorder;
using SoulPlayer.Recorder.Native;
using UnityEngine;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class SoulRecorderNativeHandsTests
    {
        private sealed class FakeStateHandle : ISoulRecorderNativeStateHandle
        {
            internal FakeStateHandle(string key, int value)
            {
                Key = key;
                Value = value;
            }

            public string Key { get; private set; }
            internal int Value { get; set; }
            internal int CaptureCount { get; private set; }
            internal int RestoreCount { get; private set; }
            internal bool ThrowOnRestore { get; set; }

            public object CaptureState()
            {
                CaptureCount++;
                return Value;
            }

            public void RestoreState(object state)
            {
                RestoreCount++;
                if (ThrowOnRestore)
                {
                    throw new InvalidOperationException("expected restore failure");
                }
                Value = (int)state;
            }
        }

        [Fact]
        [Trait("Validation", "NativeHands")]
        public void BoneRoleResolverUsesHierarchyAndFindsBothNativeArms()
        {
            List<SoulRecorderNativeBoneDescriptor> bones = NativeSkeleton();

            SoulRecorderNativeBoneResolution result =
                SoulRecorderNativeBoneRoleResolver.Resolve(bones);

            Assert.True(result.IsComplete);
            Assert.Equal("Base HumanLCollarbone", result.Left.Collarbone);
            Assert.Equal("Base HumanLUpperarm", result.Left.UpperArm);
            Assert.Equal("Base HumanLForearm1", result.Left.Forearm);
            Assert.Equal("Base HumanLForearm3", result.Left.Wrist);
            Assert.Equal("Base HumanLPalm", result.Left.Palm);
            Assert.Contains("Base HumanLThumb1", result.Left.Fingers);
            Assert.Equal("Base HumanRCollarbone", result.Right.Collarbone);
            Assert.Contains("Base HumanRIndex1", result.Right.Fingers);
        }

        [Fact]
        [Trait("Validation", "NativeHands")]
        public void MissingRequiredNativeBoneRejectsResolutionSafely()
        {
            List<SoulRecorderNativeBoneDescriptor> bones = NativeSkeleton();
            bones.RemoveAll(value => value.Name == "Base HumanRForearm3");

            SoulRecorderNativeBoneResolution result =
                SoulRecorderNativeBoneRoleResolver.Resolve(bones);

            Assert.False(result.IsComplete);
            Assert.False(result.Right.IsComplete);
        }

        [Fact]
        [Trait("Validation", "NativeHands")]
        public void MissingNativePlayerOrWrongPointOfViewFallsBackWithoutThrowing()
        {
            Assert.Equal(
                "local EFT player is unavailable",
                SoulRecorderNativeDiscoveryPolicy.ValidatePlayer(false, false, false));
            Assert.Equal(
                "local EFT player is unavailable",
                SoulRecorderNativeDiscoveryPolicy.ValidatePlayer(true, false, true));
            Assert.Equal(
                "local player is not in first-person point of view",
                SoulRecorderNativeDiscoveryPolicy.ValidatePlayer(true, true, false));
            Assert.Equal(
                string.Empty,
                SoulRecorderNativeDiscoveryPolicy.ValidatePlayer(true, true, true));
        }

        [Fact]
        [Trait("Validation", "NativeHands")]
        public void NativeStateIsCapturedOnceAndRestoredExactlyOnce()
        {
            SoulRecorderNativeStateGuard guard = new SoulRecorderNativeStateGuard();
            FakeStateHandle handle = new FakeStateHandle("left-fingers", 17);

            Assert.True(guard.CaptureOnce(handle));
            Assert.False(guard.CaptureOnce(handle));
            handle.Value = 99;

            Assert.Empty(guard.RestoreAll());
            Assert.Equal(17, handle.Value);
            Assert.Equal(1, handle.CaptureCount);
            Assert.Equal(1, handle.RestoreCount);
            Assert.Empty(guard.RestoreAll());
            Assert.Equal(1, handle.RestoreCount);
        }

        [Fact]
        [Trait("Validation", "NativeHands")]
        public void OneRestoreFailureDoesNotPreventRemainingNativeCleanup()
        {
            SoulRecorderNativeStateGuard guard = new SoulRecorderNativeStateGuard();
            FakeStateHandle healthy = new FakeStateHandle("healthy", 1);
            FakeStateHandle failing = new FakeStateHandle("failing", 2)
            {
                ThrowOnRestore = true
            };
            guard.CaptureOnce(healthy);
            guard.CaptureOnce(failing);
            healthy.Value = 10;
            failing.Value = 20;

            IReadOnlyList<Exception> failures = guard.RestoreAll();

            Assert.Single(failures);
            Assert.Equal(1, healthy.Value);
            Assert.Equal(1, healthy.RestoreCount);
            Assert.Equal(1, failing.RestoreCount);
            Assert.Equal(0, guard.Count);
        }

        [Fact]
        [Trait("Validation", "NativeHands")]
        public void TenNativeInteractionCyclesDoNotAccumulateStateOffsets()
        {
            FakeStateHandle handle = new FakeStateHandle("right-fingers", 37);
            for (int cycle = 0; cycle < 10; cycle++)
            {
                SoulRecorderNativeStateGuard guard = new SoulRecorderNativeStateGuard();
                Assert.True(guard.CaptureOnce(handle));
                handle.Value += 100 + cycle;
                Assert.Empty(guard.RestoreAll());
                Assert.Equal(37, handle.Value);
            }
            Assert.Equal(10, handle.CaptureCount);
            Assert.Equal(10, handle.RestoreCount);
        }

        [Fact]
        [Trait("Validation", "NativeHands")]
        public void NativeEnterAndStopEnterCompleteBeforeInsertAndEject()
        {
            SoulRecorderNativePhaseTimeline timeline =
                new SoulRecorderNativePhaseTimeline();

            timeline.BeginInsertion(10f);
            Assert.Equal(SoulRecorderNativeHandsPhase.Entering, timeline.Phase);
            Assert.Equal(SoulRecorderNativeHandsPhase.Inserting, timeline.NextPhase);
            Assert.False(timeline.Advance(
                10f + ProceduralSoulRecorderHandsView.EnterClipSeconds - 0.001f));
            Assert.Equal(SoulRecorderNativeHandsPhase.Entering, timeline.Phase);
            Assert.True(timeline.Advance(
                10f + ProceduralSoulRecorderHandsView.EnterClipSeconds));
            Assert.Equal(SoulRecorderNativeHandsPhase.Inserting, timeline.Phase);

            timeline.BeginEjection(20f);
            Assert.Equal(SoulRecorderNativeHandsPhase.StopEntering, timeline.Phase);
            Assert.Equal(SoulRecorderNativeHandsPhase.Ejecting, timeline.NextPhase);
            Assert.False(timeline.Advance(
                20f + ProceduralSoulRecorderHandsView.StopEnterClipSeconds - 0.001f));
            Assert.Equal(SoulRecorderNativeHandsPhase.StopEntering, timeline.Phase);
            Assert.True(timeline.Advance(
                20f + ProceduralSoulRecorderHandsView.StopEnterClipSeconds));
            Assert.Equal(SoulRecorderNativeHandsPhase.Ejecting, timeline.Phase);
            Assert.Equal(
                20f + ProceduralSoulRecorderHandsView.StopEnterClipSeconds +
                    ProceduralSoulRecorderHandsView.EjectTransferSeconds,
                SoulRecorderNativePhaseTimeline.EjectionTransferAt(20f),
                5);
            float transferProgress =
                SoulRecorderNativePhaseTimeline.EjectionTransferProgress;
            Assert.True(SoulRecorderNativePhaseTimeline.EjectionContactAmount(
                transferProgress - 0.001f) < 1f);
            Assert.Equal(1f,
                SoulRecorderNativePhaseTimeline.EjectionContactAmount(transferProgress),
                5);
            Assert.True(SoulRecorderNativePhaseTimeline.EjectionContactAmount(
                transferProgress + 0.001f) < 1f);
        }

        [Fact]
        [Trait("Validation", "NativeHands")]
        public void NativeCassetteUsesOneDedicatedGripRotation()
        {
            Assert.Equal(
                SoulRecorderPresentationTuning.NativeCassetteModelRotationEuler,
                ProceduralSoulRecorderHandsView.ResolveCassetteGripModelRotation(true));
            Assert.Equal(
                Vector3.zero,
                ProceduralSoulRecorderHandsView.ResolveCassetteGripModelRotation(true));
            Assert.Equal(
                SoulRecorderPresentationTuning.AnimatedCassetteGripRotationEuler,
                ProceduralSoulRecorderHandsView.ResolveCassetteGripModelRotation(false));
            Assert.NotEqual(
                SoulRecorderPresentationTuning.AnimatedCassetteGripRotationEuler,
                SoulRecorderPresentationTuning.NativeCassetteGripRotationEuler);
        }

        [Fact]
        [Trait("Validation", "NativeHands")]
        public void NativeTargetsUseBentArmReachAndCompactPalmGripOffsets()
        {
            Assert.InRange(
                SoulRecorderPresentationTuning.NativeSupportTargetPosition.z,
                0.35f,
                0.45f);
            Assert.InRange(
                SoulRecorderPresentationTuning.NativeCassetteCarryTargetPosition.z,
                0.35f,
                0.45f);
            Assert.InRange(
                SoulRecorderPresentationTuning.NativeRecorderGripPosition.z,
                0f,
                0.03f);
            Assert.InRange(
                SoulRecorderPresentationTuning.NativeCassetteGripPosition.z,
                0f,
                0.025f);
            Assert.All(new[]
            {
                SoulRecorderPresentationTuning.NativeSupportWristRelativeRotationEuler.x,
                SoulRecorderPresentationTuning.NativeSupportWristRelativeRotationEuler.y,
                SoulRecorderPresentationTuning.NativeSupportWristRelativeRotationEuler.z,
                SoulRecorderPresentationTuning.NativeCassetteWristRelativeRotationEuler.x,
                SoulRecorderPresentationTuning.NativeCassetteWristRelativeRotationEuler.y,
                SoulRecorderPresentationTuning.NativeCassetteWristRelativeRotationEuler.z
            }, value => Assert.InRange(Math.Abs(value), 0f, 15f));
        }

        [Fact]
        [Trait("Validation", "NativeHands")]
        public void NativeWristPoseIsBaselineRelativeAndCassetteContactIsSlotDerived()
        {
            string root = FindRepositoryRoot();
            string runtime = File.ReadAllText(Path.Combine(
                root, "Recorder", "Native", "EftNativeHandsRuntime.cs"));
            string view = File.ReadAllText(Path.Combine(
                root, "Recorder", "EftNativeSoulRecorderHandsView.cs"));
            string models = File.ReadAllText(Path.Combine(
                root, "Recorder", "ProceduralSoulRecorderHandsView.cs"));

            Assert.Contains("CaptureEmptyHandsWristBaselines", runtime);
            Assert.Contains("cameraInverse * _leftWrist.rotation", runtime);
            Assert.Contains("cameraInverse * _rightWrist.rotation", runtime);
            Assert.Contains("_supportWristBaselineCameraRotation * Quaternion.Euler", runtime);
            Assert.Contains("_cassetteWristBaselineCameraRotation * Quaternion.Euler", runtime);
            Assert.DoesNotContain("new Vector3(18f, 205f, 92f)", runtime);
            Assert.DoesNotContain("Vector3 cassetteContact =", runtime);

            Assert.Contains("SolveWristContactPose", runtime);
            Assert.Contains("_cassetteSlot.position", runtime);
            Assert.Contains("_cassetteSlot.rotation", runtime);
            Assert.Contains("wristRotation * wristToCassetteRoot.Position", runtime);
            Assert.Contains("ConfigureCassetteSlot(Models.CassetteSlotTransform)", view);
            Assert.Contains("internal Transform CassetteSlotTransform", models);
            Assert.Contains("\"SoulRecorderGripSocket\", leftPalm", runtime);
            Assert.Contains("\"SoulTapeGripSocket\", rightPalm", runtime);
        }

        [Fact]
        [Trait("Validation", "NativeHands")]
        public void PlacementToolsExposeAllNativePoseAndGripInputs()
        {
            string root = FindRepositoryRoot();
            string tuner = File.ReadAllText(Path.Combine(
                root, "Recorder", "DevelopmentSoulRecorderPresentationTuner.cs"));

            Assert.Contains("Native support target position", tuner);
            Assert.Contains("Native cassette carry target position", tuner);
            Assert.Contains("Native support wrist relative rotation", tuner);
            Assert.Contains("Native cassette wrist relative rotation", tuner);
            Assert.Contains("Native support elbow goal", tuner);
            Assert.Contains("Native cassette elbow goal", tuner);
            Assert.Contains("Native recorder palm grip position", tuner);
            Assert.Contains("Native recorder palm grip rotation", tuner);
            Assert.Contains("Native recorder model scale", tuner);
            Assert.Contains("Native cassette palm grip position", tuner);
            Assert.Contains("Native cassette palm grip rotation", tuner);
        }

        [Fact]
        [Trait("Validation", "NativeHands")]
        public void PreferredPresentationUsesScreenOverlayAndArchivesNativeHands()
        {
            string root = FindRepositoryRoot();
            string controller = File.ReadAllText(Path.Combine(
                root, "Recorder", "SoulRecorderController.cs"));
            string nativeController = File.ReadAllText(Path.Combine(
                root, "Recorder", "SoulRecorderNativeUsableItemController.cs"));
            string provider = File.ReadAllText(Path.Combine(
                root, "Recorder", "Assets", "SoulRecorderAssetProvider.cs"));
            string procedural = File.ReadAllText(Path.Combine(
                root, "Recorder", "ProceduralSoulRecorderHandsView.cs"));

            Assert.Contains("gameObject.AddComponent<SoulRecorderOverlayView>()",
                controller);
            Assert.Contains("new SoulRecorderScreenOverlayTransition()", controller);
            Assert.DoesNotContain("EftNativeSoulRecorderHandsView", controller);
            Assert.DoesNotContain("ProceduralSoulRecorderHandsView", controller);
            Assert.Contains("includePackagedAnimatedHands", provider);
            Assert.Contains("? _bundle.InstantiateAnimatedHands()", provider);
            Assert.Contains("_allowPackagedAnimatedHands", procedural);
            Assert.Contains("packaged.HasAnimatedHands", procedural);
            Assert.DoesNotContain("PlayerBody", procedural);
            Assert.DoesNotContain("LimbIK", procedural);
        }

        [Fact]
        [Trait("Validation", "NativeHands")]
        public void ActivePresentationDoesNotInvokeArchivedEftHandsPath()
        {
            string root = FindRepositoryRoot();
            string nativeController = File.ReadAllText(Path.Combine(
                root, "Recorder", "SoulRecorderNativeUsableItemController.cs"));
            string nativePatch = File.ReadAllText(Path.Combine(
                root, "Recorder", "SoulRecorderNativePresentationPatch.cs"));
            string transition = File.ReadAllText(Path.Combine(
                root, "Recorder", "SoulRecorderHandsControllerTransition.cs"));
            string plugin = File.ReadAllText(Path.Combine(root, "Plugin.cs"));
            string project = File.ReadAllText(Path.Combine(root, "SoulPlayer.csproj"));

            string controller = File.ReadAllText(Path.Combine(
                root, "Recorder", "SoulRecorderController.cs"));
            string overlayTransition = File.ReadAllText(Path.Combine(
                root, "Recorder", "SoulRecorderScreenOverlayTransition.cs"));
            Assert.Contains("new SoulRecorderScreenOverlayTransition()", controller);
            Assert.DoesNotContain(
                "new SptSoulRecorderNativeHandsControllerTransition", controller);
            Assert.DoesNotContain("SetEmptyHands", overlayTransition);
            Assert.DoesNotContain("SetInHands", overlayTransition);
            Assert.DoesNotContain("DropCurrentController", overlayTransition);
            Assert.Contains("SoulRecorderHandsControllerTransition", transition);
            Assert.DoesNotContain("LimbIK", nativeController);
        }

        [Fact]
        [Trait("Validation", "NativeHands")]
        public void ArchivedPackagedPresentationRemainsButActiveControllerUsesOverlay()
        {
            string root = FindRepositoryRoot();
            string nativeController = File.ReadAllText(Path.Combine(
                root, "Recorder", "SoulRecorderNativeUsableItemController.cs"));
            string controller = File.ReadAllText(Path.Combine(
                root, "Recorder", "SoulRecorderController.cs"));
            string adapter = File.ReadAllText(Path.Combine(
                root, "Recorder", "ISoulRecorderHandsView.cs"));

            string procedural = File.ReadAllText(Path.Combine(
                root, "Recorder", "ProceduralSoulRecorderHandsView.cs"));
            Assert.Contains("Plugin.RecorderAssets.TryCreateRecorderVisual(", procedural);
            Assert.Contains("_allowPackagedAnimatedHands", procedural);
            Assert.Contains("BindPackagedModel(packaged)", procedural);
            Assert.Contains("packaged.HasAnimatedHands", procedural);
            Assert.Contains("PlayHandsClip(\"SoulRecorder_Enter\")", procedural);
            Assert.Contains("_followupClip = \"SoulRecorder_Insert\"", procedural);
            Assert.Contains("ApplyStatusLed(isPlaying)", procedural);
            Assert.Contains("AnimateReels()", procedural);
            Assert.Contains("gameObject.AddComponent<SoulRecorderOverlayView>()",
                controller);
            Assert.DoesNotContain("ProceduralSoulRecorderHandsView", controller);
        }

        [Fact]
        [Trait("Validation", "NativeHands")]
        public void FailedOrStalledEmptyHandsAcquireRestoresPreviousHandsAndReleasesM()
        {
            string root = FindRepositoryRoot();
            string transition = File.ReadAllText(Path.Combine(
                root, "Recorder", "SoulRecorderHandsControllerTransition.cs"));

            Assert.Contains("AcquireObservationGraceSeconds", transition);
            Assert.Contains("AcquireTimeoutSeconds", transition);
            Assert.Contains("player.SetEmptyHands(result =>", transition);
            Assert.Contains("acquiringController is IEmptyHandsController", transition);
            Assert.DoesNotContain("CreateItemHandsController<", transition);
            Assert.DoesNotContain("CreateItemUsablePrefab", transition);
            Assert.Contains("empty-hands acquisition timed out", transition);
            Assert.Contains("if (!IsAcquiring)", transition);
            Assert.Contains("_ownership.ConfirmEmptyHands(acquireToken)", transition);
            Assert.Contains("DeferRestore(\"failed empty-hands acquisition recovery: \" + detail)",
                transition);
            Assert.Contains("if (_player.HandsController == null)", transition);
            Assert.Contains("RestorePreviousItemDirectly(", transition);
            Assert.Contains("direct SetInHands after empty-controller failure", transition);
            Assert.Contains("RestoreHolsterTimeoutSeconds", transition);
            Assert.Contains("BeginForcedRestoreRecovery(expected)", transition);
            Assert.Contains("forced recovery SetInHands callback", transition);
            Assert.Contains("restoreAttempt != _restoreAttempt", transition);
            Assert.Contains("_restoreObservation.MarkRestoreRequested()", transition);
            Assert.Contains("_restoreObservation.CanComplete(", transition);
            Assert.Contains("!_restoreObservation.IsRestoreRequested", transition);
            Assert.Contains("new active hands controller observed after SetInHands", transition);
            Assert.DoesNotContain(
                "ReferenceEquals(restoredController.Item, expected)", transition);
        }

        [Fact]
        [Trait("Validation", "NativeHands")]
        public void NativeControllerAdapterExposesFullCassetteTransportTiming()
        {
            ISoulRecorderHandsView view = NativeControllerSoulRecorderHandsView.Instance;

            Assert.Equal(
                SoulRecorderPresentationTuning.TapeInsertionSeconds,
                view.TapeInsertionSeconds);
            Assert.Equal(
                SoulRecorderPresentationTuning.TapeEjectionSeconds,
                view.TapeEjectionSeconds);
            Assert.Equal(
                SoulRecorderPresentationTuning.ExitSeconds,
                view.InteractionExitSeconds);
            Assert.InRange(
                view.TapeEjectionAudioStopSeconds,
                SoulRecorderPresentationTuning.StopEjectionLeadInSeconds,
                SoulRecorderPresentationTuning.TapeEjectionSeconds);
        }

        [Fact]
        [Trait("Validation", "NativeHands")]
        public void NativeRuntimeUsesInstalledEftObjectsWithoutSptVrDependency()
        {
            string root = FindRepositoryRoot();
            string runtime = File.ReadAllText(Path.Combine(
                root, "Recorder", "Native", "EftNativeHandsRuntime.cs"));
            string project = File.ReadAllText(Path.Combine(root, "SoulPlayer.csproj"));

            Assert.Contains("player.PlayerBody", runtime);
            Assert.Contains("BodySkins.TryGetValue(EBodyModelPart.Hands", runtime);
            Assert.Contains("body.SkeletonHands", runtime);
            Assert.Contains("SkinnedMeshRenderer", runtime);
            Assert.Contains("LimbIK", runtime);
            Assert.Contains("SoulRecorderGripSocket", runtime);
            Assert.Contains("SoulTapeGripSocket", runtime);
            Assert.Contains("OnPostUpdate", runtime);
            Assert.DoesNotContain("using TarkovVR", runtime);
            Assert.DoesNotContain("SPT-VR", project, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Validation", "NativeHands")]
        public void NativeExitDeathAndFailureCleanupShareIdempotentRestoration()
        {
            string root = FindRepositoryRoot();
            string nativeView = File.ReadAllText(Path.Combine(
                root, "Recorder", "EftNativeSoulRecorderHandsView.cs"));
            string runtime = File.ReadAllText(Path.Combine(
                root, "Recorder", "Native", "EftNativeHandsRuntime.cs"));

            Assert.Contains("Models.ClearNativeAttachments()", nativeView);
            Assert.Contains("_nativeHands.Cleanup", nativeView);
            Assert.Contains("private void OnDisable()", nativeView);
            Assert.Contains("private void OnDestroy()", nativeView);
            Assert.Contains("_stateGuard.RestoreAll()", runtime);
            Assert.Contains("UnsubscribePostIk()", runtime);
            Assert.Contains("_fingerBaseRotations", runtime);
            Assert.Contains("original * Quaternion.Euler", runtime);
            Assert.Contains("SoulRecorderAttachmentTransfer.ReparentPreservingWorldPose", File.ReadAllText(
                Path.Combine(root, "Recorder", "ProceduralSoulRecorderHandsView.cs")));
        }

        [Fact]
        [Trait("Validation", "AssetPipeline")]
        public void ReleasePackagingAuditsOutEftAssetFiles()
        {
            string root = FindRepositoryRoot();
            string package = File.ReadAllText(Path.Combine(
                root, "scripts", "Package-Release.ps1"));
            string audit = File.ReadAllText(Path.Combine(
                root, "tools", "Test-SoulPlayerPackageAssets.ps1"));

            Assert.Contains("Assert-SoulPlayerPackageAssetPolicy", package);
            Assert.Contains("prohibited game asset", package);
            Assert.DoesNotContain("'soulplayer_assets.bundle'", package);
            Assert.Contains("'soultape_world.bundle'", package);
            Assert.Contains("embedded 2D overlay", audit);
            Assert.Contains("visual-only world cassette bundle", audit);
            Assert.Contains("no EFT assets", audit);
        }

        private static List<SoulRecorderNativeBoneDescriptor> NativeSkeleton()
        {
            return new List<SoulRecorderNativeBoneDescriptor>
            {
                Bone("Base HumanRibcage", "Root"),
                Bone("Base HumanLCollarbone", "Base HumanRibcage"),
                Bone("Base HumanLUpperarm", "Base HumanLCollarbone"),
                Bone("Base HumanLForearm1", "Base HumanLUpperarm"),
                Bone("Base HumanLForearm2", "Base HumanLForearm1"),
                Bone("Base HumanLForearm3", "Base HumanLForearm2"),
                Bone("Base HumanLPalm", "Base HumanLForearm3"),
                Bone("Base HumanLThumb1", "Base HumanLPalm"),
                Bone("Base HumanLIndex1", "Base HumanLPalm"),
                Bone("Base HumanRCollarbone", "Base HumanRibcage"),
                Bone("Base HumanRUpperarm", "Base HumanRCollarbone"),
                Bone("Base HumanRForearm1", "Base HumanRUpperarm"),
                Bone("Base HumanRForearm2", "Base HumanRForearm1"),
                Bone("Base HumanRForearm3", "Base HumanRForearm2"),
                Bone("Base HumanRPalm", "Base HumanRForearm3"),
                Bone("Base HumanRThumb1", "Base HumanRPalm"),
                Bone("Base HumanRIndex1", "Base HumanRPalm")
            };
        }

        private static SoulRecorderNativeBoneDescriptor Bone(string name, string parent)
        {
            return new SoulRecorderNativeBoneDescriptor(name, parent);
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null &&
                   !File.Exists(Path.Combine(directory.FullName, "SoulPlayer.csproj")))
            {
                directory = directory.Parent;
            }
            Assert.NotNull(directory);
            return directory.FullName;
        }
    }
}
