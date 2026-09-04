using System;
using System.IO;
using SoulPlayer.Recorder;
using SoulPlayer.Recorder.Assets;
using UnityEngine;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class SoulRecorderInteractionLifecycleTests
    {
        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void StartInteractionTakesPresentationOwnership()
        {
            SoulRecorderInteractionLifecycle lifecycle = NewLoadingLifecycle();

            Assert.True(lifecycle.HasHandsOwnership);
            Assert.True(lifecycle.IsPresentationVisible);
            Assert.Equal(SoulRecorderInteractionPhase.LoadingTape, lifecycle.Phase);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void InsertionCompletionStartsPlaybackThenRequestsOneRestore()
        {
            SoulRecorderInteractionLifecycle lifecycle = NewPlayingLifecycle();

            Assert.Equal(SoulRecorderPlaybackState.Playing, lifecycle.PlaybackState);
            Assert.True(lifecycle.IsPresentationVisible);
            lifecycle.CompletePresentationExit();

            Assert.Equal(SoulRecorderPlaybackState.Playing, lifecycle.PlaybackState);
            Assert.False(lifecycle.IsPresentationVisible);
            Assert.True(lifecycle.TakeHandsRestoreRequest());
            Assert.False(lifecycle.TakeHandsRestoreRequest());
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void PlayingCanEnterStopAndEjectInteraction()
        {
            SoulRecorderInteractionLifecycle lifecycle = NewPlayingLifecycle();
            lifecycle.CompletePresentationExit();
            lifecycle.TakeHandsRestoreRequest();

            Assert.True(lifecycle.BeginStopInteraction());
            Assert.Equal(SoulRecorderPlaybackState.Stopped, lifecycle.PlaybackState);
            Assert.Equal(SoulRecorderInteractionPhase.Ejecting, lifecycle.Phase);
            Assert.True(lifecycle.HasHandsOwnership);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void InterruptedInsertionReversesBeforeRestoringHandsAndHidingPresentation()
        {
            SoulRecorderInteractionLifecycle lifecycle = NewLoadingLifecycle();

            lifecycle.InterruptInsertion();

            Assert.Equal(SoulRecorderPlaybackState.Stopped, lifecycle.PlaybackState);
            Assert.Equal(SoulRecorderInteractionPhase.Ejecting, lifecycle.Phase);
            Assert.True(lifecycle.IsPresentationVisible);
            lifecycle.BeginExitAfterStop();
            lifecycle.CompletePresentationExit();
            Assert.Equal(SoulRecorderInteractionPhase.Hidden, lifecycle.Phase);
            Assert.True(lifecycle.TakeHandsRestoreRequest());
        }

        [Theory]
        [InlineData("loading")]
        [InlineData("playing")]
        [InlineData("ejecting")]
        [Trait("Validation", "RecorderPresentation")]
        public void DeathFromAnyRecorderPhaseStopsAndCleansEverything(string phase)
        {
            SoulRecorderInteractionLifecycle lifecycle;
            if (phase == "loading")
            {
                lifecycle = NewLoadingLifecycle();
            }
            else
            {
                lifecycle = NewPlayingLifecycle();
                if (phase == "ejecting")
                {
                    lifecycle.CompletePresentationExit();
                    lifecycle.TakeHandsRestoreRequest();
                    Assert.True(lifecycle.BeginStopInteraction());
                }
            }

            lifecycle.ForceCleanup();

            Assert.Equal(SoulRecorderPlaybackState.Stopped, lifecycle.PlaybackState);
            Assert.Equal(SoulRecorderInteractionPhase.Hidden, lifecycle.Phase);
            Assert.False(lifecycle.IsPresentationVisible);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void RaidEndAndDisposalCleanupAreIdempotent()
        {
            SoulRecorderInteractionLifecycle lifecycle = NewPlayingLifecycle();

            lifecycle.ForceCleanup();
            lifecycle.ForceCleanup();

            Assert.True(lifecycle.TakeHandsRestoreRequest());
            Assert.False(lifecycle.TakeHandsRestoreRequest());
            Assert.Equal(SoulRecorderPlaybackState.Stopped, lifecycle.PlaybackState);
            Assert.Equal(SoulRecorderInteractionPhase.Hidden, lifecycle.Phase);
        }

        [Theory]
        [InlineData(false, true, true, true, true, false)]
        [InlineData(true, false, false, false, false, false)]
        [InlineData(true, true, false, true, true, false)]
        [InlineData(true, true, true, false, true, false)]
        [InlineData(true, true, true, true, false, false)]
        [InlineData(true, true, true, true, true, true)]
        [Trait("Validation", "RecorderPresentation")]
        public void RecorderInputIsRejectedOutsideLiveUnsuppressedRaid(
            bool inRaid,
            bool hasPlayer,
            bool active,
            bool alive,
            bool hasHands,
            bool terminated)
        {
            Assert.False(SoulRecorderRaidInputGate.AllowsRecorderInput(
                inRaid, hasPlayer, active, alive, hasHands, terminated));
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void RecorderInputIsAllowedForLiveLocalRaidPlayer()
        {
            Assert.True(SoulRecorderRaidInputGate.AllowsRecorderInput(
                true, true, true, true, true, false));
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void MalformedPackagedHandBoundsAreRejectedSafely()
        {
            Assert.True(SoulRecorderAssetProvider.ArePackagedHandBoundsSafe(
                new Vector3(0.29f, 0.39f, 0.05f)));
            Assert.False(SoulRecorderAssetProvider.ArePackagedHandBoundsSafe(
                new Vector3(0.29f, 3.9f, 0.05f)));
            Assert.False(SoulRecorderAssetProvider.ArePackagedHandBoundsSafe(
                new Vector3(float.NaN, 0.39f, 0.05f)));
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void HandRigRejectsDeformBoneAsRendererRoot()
        {
            Assert.False(SoulRecorderAssetProvider.IsPackagedHandRigSafe(
                "CassetteHand", 2, 2));
            Assert.True(SoulRecorderAssetProvider.IsPackagedHandRigSafe(
                "SoulRecorderHandsRoot", 43, 43));
            Assert.False(SoulRecorderAssetProvider.IsPackagedHandRigSafe(
                "SoulRecorderHandsRoot", 43, 42));
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void ProductionUsesScreenOverlayWithoutTouchingHandsAndKeepsPostRaidSignal()
        {
            string root = FindRepositoryRoot();
            string controller = File.ReadAllText(Path.Combine(
                root, "Recorder", "SoulRecorderController.cs"));
            string transition = File.ReadAllText(Path.Combine(
                root, "Recorder", "SoulRecorderScreenOverlayTransition.cs"));
            string coordinator = File.ReadAllText(Path.Combine(
                root, "Audio", "PostRaidCoordinator.cs"));

            Assert.Contains("new SoulRecorderScreenOverlayTransition()", controller);
            Assert.Contains("gameObject.AddComponent<SoulRecorderOverlayView>()",
                controller);
            Assert.DoesNotContain("SetEmptyHands", transition);
            Assert.DoesNotContain("SetInHands", transition);
            Assert.DoesNotContain(".HandsController", transition);
            Assert.DoesNotContain("ProceduralSoulRecorderHandsView", controller);
            Assert.Contains("RaidResultQueued += OnRaidResultQueued", controller);
            Assert.Contains(
                "ResetRecorder(\"post-raid result \" + outcome, false)",
                controller);
            Assert.Contains("NotifyRaidResult(outcome)", coordinator);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void PluginAndControllerDisposalShareImmediateCleanup()
        {
            string root = FindRepositoryRoot();
            string plugin = File.ReadAllText(Path.Combine(root, "Plugin.cs"));
            string controller = File.ReadAllText(Path.Combine(
                root, "Recorder", "SoulRecorderController.cs"));

            Assert.Contains("RecorderController.Shutdown(\"plugin destroyed\")", plugin);
            Assert.Contains("ResetRecorder(\"component destroyed\", false)", controller);
            Assert.Contains("_audioPlayer.Stop()", controller);
            Assert.Contains("_usableItemController.ForceReset(reason)", controller);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void EjectionMotionAndInteractionCompletionCanOnlyFinishOnce()
        {
            SoulRecorderInteractionCompletionGate gate =
                new SoulRecorderInteractionCompletionGate();
            gate.BeginInteraction();
            gate.ArmEjectionMotion(10f);

            Assert.False(gate.TryFinishEjectionMotion(9.99f));
            Assert.True(gate.TryFinishEjectionMotion(10f));
            Assert.False(gate.TryFinishEjectionMotion(20f));
            Assert.True(gate.TryReleaseInteraction());
            Assert.False(gate.TryReleaseInteraction());
            Assert.False(gate.IsInteractionPending);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void StartRestoresWeaponAndLeavesPlaybackPlayingButHandsFree()
        {
            OfflineCycleHarness harness = new OfflineCycleHarness();

            harness.Start("weapon-a");
            harness.CompleteStart();

            Assert.Equal("weapon-a", harness.LastRestoredItem);
            Assert.Equal(SoulRecorderPlaybackState.Playing, harness.Lifecycle.PlaybackState);
            Assert.Equal(SoulRecorderInteractionPhase.Hidden, harness.Lifecycle.Phase);
            Assert.False(harness.Hands.IsBusy);
            Assert.False(harness.Hands.IsOwned);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void StopRestoresCurrentWeaponAndReturnsReusableIdleStoppedState()
        {
            OfflineCycleHarness harness = StartedHarness("weapon-a");

            harness.Stop("weapon-b");
            harness.CompleteStop();

            Assert.Equal("weapon-b", harness.LastRestoredItem);
            Assert.Equal(SoulRecorderPlaybackState.Stopped, harness.Lifecycle.PlaybackState);
            Assert.Equal(SoulRecorderInteractionPhase.Hidden, harness.Lifecycle.Phase);
            Assert.False(harness.Hands.IsBusy);
            Assert.Null(harness.Hands.PreviousItem);
            harness.Start("weapon-b");
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void StartStopStartStopCanRepeatWithoutStuckOwnership()
        {
            OfflineCycleHarness harness = new OfflineCycleHarness();

            for (int cycle = 0; cycle < 10; cycle++)
            {
                string item = "weapon-" + cycle;
                harness.Start(item);
                harness.CompleteStart();
                Assert.False(harness.Hands.IsBusy);
                harness.Stop(item);
                harness.CompleteStop();
                Assert.False(harness.Hands.IsBusy);
                Assert.False(harness.Hands.IsOwned);
                Assert.Equal(SoulRecorderPlaybackState.Stopped,
                    harness.Lifecycle.PlaybackState);
                Assert.Equal(SoulRecorderInteractionPhase.Hidden,
                    harness.Lifecycle.Phase);
            }

            Assert.Equal(20, harness.RestoreCount);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void PreviousItemRefreshesAndManualWeaponChangeDoesNotRestoreStaleItem()
        {
            OfflineCycleHarness harness = StartedHarness("old-weapon");
            Assert.Equal("old-weapon", harness.LastRestoredItem);

            harness.Stop("new-current-weapon");
            harness.CompleteStop();

            Assert.Equal("new-current-weapon", harness.LastRestoredItem);
            Assert.Null(harness.Hands.PreviousItem);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void RestoreCallbackCompletesExactlyOnceAndFailureCannotLockNextAcquire()
        {
            SoulRecorderHandsOwnershipState<string> hands =
                new SoulRecorderHandsOwnershipState<string>();
            int acquireToken;
            Assert.True(hands.BeginAcquire("weapon-a", out acquireToken));
            Assert.True(hands.ConfirmEmptyHands(acquireToken));
            int restoreToken;
            string item;
            Assert.True(hands.BeginRestore(out restoreToken, out item));
            Assert.Equal("weapon-a", item);

            Assert.True(hands.FailRestore(restoreToken));
            Assert.False(hands.CompleteRestore(restoreToken));
            Assert.False(hands.IsBusy);
            Assert.Null(hands.PreviousItem);

            Assert.True(hands.BeginAcquire("weapon-b", out acquireToken));
            Assert.Equal("weapon-b", hands.PreviousItem);
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void InterruptedInsertionRestoresAndPermitsAnotherStart()
        {
            OfflineCycleHarness harness = new OfflineCycleHarness();
            harness.Start("weapon-a");

            harness.InterruptStart();

            Assert.False(harness.Hands.IsBusy);
            Assert.False(harness.Hands.IsOwned);
            Assert.Equal(SoulRecorderInteractionPhase.Hidden, harness.Lifecycle.Phase);
            harness.Start("weapon-b");
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void StalePreHolsterControllerCannotCompleteWeaponRestore()
        {
            object oldFirearmController = new object();
            object restoredFirearmController = new object();
            object weapon = new object();
            SoulRecorderHandsRestoreObservationGate<object, object> gate =
                new SoulRecorderHandsRestoreObservationGate<object, object>();

            gate.Begin(oldFirearmController);

            Assert.False(gate.CanComplete(oldFirearmController, weapon, weapon));
            Assert.False(gate.IsRestoreRequested);
            gate.MarkRestoreRequested();
            Assert.True(gate.IsRestoreRequested);
            Assert.False(gate.CanComplete(oldFirearmController, weapon, weapon));
            Assert.False(gate.CanComplete(restoredFirearmController, new object(), weapon));
            Assert.True(gate.CanComplete(restoredFirearmController, weapon, weapon));

            gate.Reset();
            Assert.False(gate.IsRestoreRequested);
            Assert.False(gate.CanComplete(restoredFirearmController, weapon, weapon));
        }

        [Fact]
        [Trait("Validation", "RecorderPresentation")]
        public void ProductionUsesOneAuthoritativeInteractionCompletionPath()
        {
            string root = FindRepositoryRoot();
            string usable = File.ReadAllText(Path.Combine(
                root, "Recorder", "SoulRecorderUsableItemController.cs"));

            Assert.Contains("CompleteInteractionAndRestoreHands", usable);
            Assert.Contains("TryFinishEjectionMotion", usable);
            Assert.Contains("TryReleaseInteraction", usable);
            Assert.DoesNotContain("_transitionAt = 0f;\n            _lifecycle.BeginExitAfterStop();",
                usable.Replace("\r\n", "\n"));
        }

        private static SoulRecorderInteractionLifecycle NewLoadingLifecycle()
        {
            SoulRecorderInteractionLifecycle lifecycle =
                new SoulRecorderInteractionLifecycle();
            Assert.True(lifecycle.BeginStartInteraction());
            return lifecycle;
        }

        private static SoulRecorderInteractionLifecycle NewPlayingLifecycle()
        {
            SoulRecorderInteractionLifecycle lifecycle = NewLoadingLifecycle();
            lifecycle.MarkReady();
            Assert.True(lifecycle.MarkPlaybackStarted());
            return lifecycle;
        }

        private static OfflineCycleHarness StartedHarness(string item)
        {
            OfflineCycleHarness harness = new OfflineCycleHarness();
            harness.Start(item);
            harness.CompleteStart();
            return harness;
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
                "SoulPlayer repository root could not be located.");
        }

        private sealed class OfflineCycleHarness
        {
            internal readonly SoulRecorderInteractionLifecycle Lifecycle =
                new SoulRecorderInteractionLifecycle();
            internal readonly SoulRecorderHandsOwnershipState<string> Hands =
                new SoulRecorderHandsOwnershipState<string>();

            internal string LastRestoredItem { get; private set; }
            internal int RestoreCount { get; private set; }

            internal void Start(string currentItem)
            {
                Acquire(currentItem);
                Assert.True(Lifecycle.BeginStartInteraction());
            }

            internal void CompleteStart()
            {
                Lifecycle.MarkReady();
                Assert.True(Lifecycle.MarkPlaybackStarted());
                Lifecycle.CompletePresentationExit();
                RestoreWhenRequested();
            }

            internal void Stop(string currentItem)
            {
                Acquire(currentItem);
                Assert.True(Lifecycle.BeginStopInteraction());
            }

            internal void CompleteStop()
            {
                Lifecycle.BeginExitAfterStop();
                Lifecycle.CompletePresentationExit();
                RestoreWhenRequested();
            }

            internal void InterruptStart()
            {
                Lifecycle.InterruptInsertion();
                Lifecycle.BeginExitAfterStop();
                Lifecycle.CompletePresentationExit();
                RestoreWhenRequested();
            }

            private void Acquire(string currentItem)
            {
                int token;
                Assert.True(Hands.BeginAcquire(currentItem, out token));
                Assert.True(Hands.ConfirmEmptyHands(token));
            }

            private void RestoreWhenRequested()
            {
                Assert.True(Lifecycle.TakeHandsRestoreRequest());
                int token;
                string item;
                Assert.True(Hands.BeginRestore(out token, out item));
                LastRestoredItem = item;
                RestoreCount++;
                Assert.True(Hands.CompleteRestore(token));
            }
        }
    }
}
