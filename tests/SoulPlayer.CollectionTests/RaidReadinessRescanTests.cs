using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EFT;
using EFT.UI;
using EFT.UI.Screens;
using SoulPlayer.Audio;
using SoulPlayer.Library;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    [Trait("Validation", "PostRaidLifecycle")]
    [Trait("Validation", "LibraryRescan")]
    [Trait("Validation", "RaidReadiness")]
    public sealed class RaidReadinessRescanTests
    {
        [Theory]
        [InlineData(ExitStatus.Survived)]
        [InlineData(ExitStatus.Killed)]
        [Trait("Validation", "PlaybackSelection")]
        public void DopamineAt120SurvivesActualScanChangedThenExactOutcomeAndResume(ExitStatus outcome)
        {
            using (Fixture f = new Fixture())
            {
                MusicTrack original = f.Main;
                MainPlaybackSnapshot saved = f.Session.Candidate;
                Assert.Equal("Dopamine", saved.Title);
                Assert.Equal(120.311f, saved.Seconds);
                Assert.Equal(5774928, saved.Samples);
                Assert.True(f.Session.MainSuspended);
                f.Session.RecordOutcome(outcome);
                f.Evidence = ReadyEvidence();
                f.Evidence.Preloader = true;
                Assert.Equal(RaidReadinessReason.WaitingForPreloader, f.Reason);
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);

                f.Library.BeginScan(new[] { f.Files.Root });
                f.Evidence.Preloader = false;
                f.Evidence.BlackOverlay = false;
                Assert.Equal(RaidReadinessReason.WaitingForLibrary, f.Reason);
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                int changes = f.Changes;
                f.ApplyScan();
                Assert.Equal(changes + 1, f.Changes);
                Assert.True(f.ReadyInsideChanged);
                Assert.NotSame(original, f.Main);
                Assert.Same(saved, f.Session.Candidate);
                Assert.Same(f.Main, saved.Resolve(f.Tracks, f.Routing, System.IO.File.Exists));
                Assert.Equal(RaidReadinessReason.Ready, f.Reason);
                Assert.True(f.ReevaluationRequested);
                RaidPlaybackAction routed = f.Step();
                Assert.Equal(RaidPlaybackActionKind.Routed, routed.Kind);
                Assert.Same(outcome == ExitStatus.Survived ? f.Extract : f.Death, routed.Track);
                Assert.Equal(1, f.OutcomeSelections);
                Assert.Equal(0, f.MainSelections);
                f.Session.RoutedStarted(routed.Track);
                Assert.True(f.Session.EndRouted(routed.Track, false));
                RaidPlaybackAction resume = f.Step();
                Assert.Equal(RaidPlaybackActionKind.ResumeExact, resume.Kind);
                Assert.Same(f.Main, resume.Track);
                Assert.Equal(5774928, resume.Snapshot.ResumeSamples(48000 * 300, 48000));
                Assert.Equal(120.311f, resume.Snapshot.Seconds);
                f.Session.ResumeStarted(resume.Track);
                Assert.Equal(RaidResumeResult.ResumedExact, f.Session.ResumeResult);
                for (int frame = 0; frame < 100; frame++) Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                Assert.Equal(1, f.OutcomeSelections);
                Assert.Equal(0, f.MainSelections);
            }
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public void AutoOffOrEmptyOutcomePoolResumesDopamineDirectly(bool raidAuto, bool outcomePool)
        {
            using (Fixture f = new Fixture())
            {
                f.RaidAuto = raidAuto;
                if (!outcomePool)
                {
                    f.Routing.SetRoutes(f.Extract, TrackRoute.None);
                    f.Routing.SetRoutes(f.Death, TrackRoute.None);
                }
                f.Ready(ExitStatus.Left);
                f.Library.BeginScan(new[] { f.Files.Root });
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                f.ApplyScan();
                RaidPlaybackAction resume = f.Step();
                Assert.Equal(RaidPlaybackActionKind.ResumeExact, resume.Kind);
                Assert.Equal("Dopamine", resume.Track.Title);
                Assert.Equal(120.311f, resume.Snapshot.Seconds);
                Assert.Equal(0, f.OutcomeSelections);
            }
        }

        [Fact]
        public void RecorderUseAndZeroOutcomeRoutesResumesDopamineNotCancelled()
        {
            using (Fixture f = new Fixture())
            {
                f.Routing.SetRoutes(f.Extract, TrackRoute.Main);
                f.Routing.SetRoutes(f.Death, TrackRoute.Main);
                Assert.True(f.Session.NoteSoulRecorderUse());
                f.Ready(ExitStatus.Left);
                Assert.Equal(RaidReadinessReason.Ready, f.Reason);
                RaidPlaybackAction resume = f.Step();
                Assert.Equal(RaidPlaybackActionKind.ResumeExact, resume.Kind);
                Assert.Equal(120.311f, resume.Snapshot.Seconds);
                Assert.Equal(5774928, resume.Snapshot.Samples);
                f.Session.ResumeStarted(resume.Track);
                Assert.Equal(RaidPlaybackPhase.Complete, f.Session.Phase);
                Assert.Equal(RaidResumeResult.ResumedExact, f.Session.ResumeResult);
                Assert.Equal(RaidCancellationReason.None, f.Session.CancellationReason);
                Assert.Equal(0, f.OutcomeSelections);
                Assert.Equal(0, f.MainSelections);
                string completed = f.Session.TakeDiagnostic();
                Assert.Contains("cancellationReason=None", completed);
                Assert.Contains("snapshotPreservedThroughRaid=True", completed);
                Assert.Contains("mainSuspended=False", completed);
            }
        }

        [Theory]
        [InlineData("MainMenu", "Unavailable")]
        [InlineData("Inventory", "Unavailable")]
        [InlineData("ExitStatus", "Unavailable")]
        [InlineData("ExitStatus", "Loading")]
        [InlineData("ExitStatus", "Complete")]
        public void OptionalResultModelCannotDeadlockOtherwiseVerifiedStableMenu(string screen, string model)
        {
            RaidMenuEvidence evidence = ReadyEvidence();
            evidence.Screen = screen;
            evidence.RecognizedScreen = StableRaidMenuContext.IsReturnScreen(
                (EEftScreenType)Enum.Parse(typeof(EEftScreenType), screen));
            evidence.ResultModel = model;
            Assert.Equal(RaidReadinessReason.Ready, evidence.Evaluate(true, true));
        }

        [Fact]
        public void MissingRequiredLoaderOrOverlayEvidenceStillFailsClosed()
        {
            RaidMenuEvidence evidence = ReadyEvidence();
            evidence.Preloader = null;
            Assert.Equal(RaidReadinessReason.WaitingForPreloader, evidence.Evaluate(true, true));
            evidence.Preloader = false;
            evidence.BlackOverlay = null;
            Assert.Equal(RaidReadinessReason.WaitingForBlackOverlay, evidence.Evaluate(true, true));
        }

        [Fact]
        public void ReadinessReasonsExplainEachGateWithoutElapsedTime()
        {
            RaidMenuEvidence evidence = ReadyEvidence();
            Assert.Equal(RaidReadinessReason.WaitingForOutcome, evidence.Evaluate(false, true));
            evidence.ReturnScreenShown = false;
            Assert.Equal(RaidReadinessReason.WaitingForReturnScreen, evidence.Evaluate(true, true));
            evidence.ReturnScreenShown = true;
            evidence.PlayerPresent = true;
            Assert.Equal(RaidReadinessReason.WaitingForRaidPlayerExit, evidence.Evaluate(true, true));
            evidence.PlayerPresent = false;
            evidence.Preloader = true;
            Assert.Equal(RaidReadinessReason.WaitingForPreloader, evidence.Evaluate(true, true));
            evidence.Preloader = false;
            evidence.BlackOverlay = true;
            Assert.Equal(RaidReadinessReason.WaitingForBlackOverlay, evidence.Evaluate(true, true));
            evidence.BlackOverlay = false;
            Assert.Equal(RaidReadinessReason.WaitingForLibrary, evidence.Evaluate(true, false));
            Assert.Equal(RaidReadinessReason.Ready, evidence.Evaluate(true, true));
        }

        [Fact]
        public void MultipleScansAndScreenReconstructionNeverCancelOrLoseTheSavedCandidate()
        {
            using (Fixture f = new Fixture())
            {
                MainPlaybackSnapshot saved = f.Session.Candidate;
                f.Ready(ExitStatus.Survived);
                for (int scan = 0; scan < 3; scan++)
                {
                    f.Evidence.ScreenActive = false;
                    f.Library.BeginScan(new[] { f.Files.Root });
                    Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                    f.ApplyScan();
                    Assert.Same(saved, f.Session.Candidate);
                    Assert.Equal(RaidCancellationReason.None, f.Session.CancellationReason);
                    Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                    f.Evidence.ScreenActive = true;
                    Assert.Equal(RaidReadinessReason.Ready, f.Reason);
                }
                Assert.Equal(RaidPlaybackActionKind.Routed, f.Step().Kind);
                Assert.Equal(1, f.OutcomeSelections);
                Assert.Equal(0, f.MainSelections);
            }
        }

        [Fact]
        public void CompletedButUnappliedScanIsNotReadyAndQueuedScansPublishReadyOnlyAtTheEnd()
        {
            using (Fixture f = new Fixture())
            {
                f.Library.BeginScan(new[] { f.Files.Root });
                Assert.True(SpinWait.SpinUntil(() => !f.Library.IsScanning, TimeSpan.FromSeconds(5)));
                Assert.False(f.Library.IsReady);
                f.Library.BeginScan(new[] { f.Files.Root });
                ScanResult result;
                Assert.True(f.Library.TryApplyCompletedScan(out result));
                Assert.False(f.Library.IsReady);
                Assert.False(f.ReadyInsideChanged);
                f.ApplyScan();
                Assert.True(f.Library.IsReady);
                Assert.True(f.ReadyInsideChanged);
            }
        }

        [Fact]
        public void ThrowingChangedSubscriberCannotSuppressPlaybackReevaluation()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                files.Create("Unquote - Dopamine.mp3", 91);
                MusicLibrary library = new MusicLibrary(delegate { }, delegate { });
                bool notified = false, ready = false;
                library.Changed += () => { throw new InvalidOperationException("unrelated catalog listener"); };
                library.Changed += () => { notified = true; ready = library.IsReady; };
                library.BeginScan(new[] { files.Root });
                ScanResult result;
                Assert.True(SpinWait.SpinUntil(() => library.TryApplyCompletedScan(out result), TimeSpan.FromSeconds(5)));
                Assert.True(notified);
                Assert.True(ready);
            }
        }

        [Fact]
        public void StableAudioIdentityWinsOverPathAndCanonicalPathIsASecondaryFallback()
        {
            using (Fixture f = new Fixture())
            {
                MainPlaybackSnapshot saved = f.Session.Candidate;
                MusicTrack moved = f.Files.Create("Renamed - Dopamine.mp3", 91);
                Assert.Equal(saved.TrackId, TrackRoutingService.GetTrackId(moved));
                Assert.Same(moved, saved.Resolve(new[] { f.Other, moved }, f.Routing, path => true));
                saved.TrackId = "sha256:" + new string('f', 64); // Fingerprint unavailable/rebuilt.
                Assert.Same(f.Main, saved.Resolve(f.Tracks, f.Routing, path => true));
            }
        }

        [Fact]
        public void MainDisabledAfterScanFallsBackButNotToAnOutcomeOnlyTrack()
        {
            using (Fixture f = new Fixture())
            {
                f.RaidAuto = false;
                f.Ready(ExitStatus.Killed);
                f.Library.BeginScan(new[] { f.Files.Root });
                f.ApplyScan();
                f.Routing.SetRoutes(f.Main, TrackRoute.Death);
                RaidPlaybackAction fallback = f.Step();
                Assert.Equal(RaidPlaybackActionKind.Fallback, fallback.Kind);
                Assert.Same(f.Other, fallback.Track);
                Assert.Equal(1, f.MainSelections);
            }
        }

        [Fact]
        public void ReadinessLossDuringDecodeRecoversWithoutDuplicateDispatch()
        {
            using (Fixture f = new Fixture())
            {
                f.Ready(ExitStatus.Survived);
                RaidPlaybackAction routed = f.Step();
                f.Library.BeginScan(new[] { f.Files.Root });
                Assert.False(f.Session.CanStart(PlaybackIntent.Routed, null, f.Library.IsReady));
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                Assert.True(f.Session.MenuReady); // Rescan gates dispatch, not the active cue's scene context.
                Assert.False(f.Session.CanStart(PlaybackIntent.AutomaticMain));
                f.ApplyScan();
                f.Evidence.ScreenActive = false;
                f.Step();
                Assert.False(f.Session.CanStart(PlaybackIntent.Routed));
                f.Evidence.ScreenActive = true;
                f.Step();
                Assert.True(f.Session.CanStart(PlaybackIntent.Routed, null, f.Library.IsReady));
                f.Session.RoutedStarted(routed.Track);
                f.Session.EndRouted(routed.Track, false);
                Assert.Equal(RaidPlaybackActionKind.ResumeExact, f.Step().Kind);
                Assert.Equal(1, f.OutcomeSelections);
            }
        }

        [Theory]
        [InlineData("ManualLibrarySelection")]
        [InlineData("Stop")]
        [InlineData("Next")]
        [InlineData("Previous")]
        [InlineData("ExplicitPlaybackRequest")]
        public void OnlyExplicitCancellationSurvivesLaterScansAndCannotReclaimPlayback(string reason)
        {
            using (Fixture f = new Fixture())
            {
                f.Session.NoteSoulRecorderUse();
                f.Ready(ExitStatus.Survived);
                Assert.True(f.Session.CancelSavedResume((RaidCancellationReason)Enum.Parse(typeof(RaidCancellationReason), reason)));
                f.Library.BeginScan(new[] { f.Files.Root });
                f.ApplyScan();
                f.Ready(ExitStatus.Survived);
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                Assert.Null(f.Session.Candidate);
                Assert.Equal(RaidResumeResult.Cancelled, f.Session.ResumeResult);
                Assert.Equal(reason, f.Session.CancellationReason.ToString());
                Assert.Equal(0, f.OutcomeSelections);
                Assert.Equal(0, f.MainSelections);
            }
        }

        [Fact]
        public void StateDiagnosticsAreTransitionOnlyAndExposeCaptureReadinessRoutesAndCancellation()
        {
            using (Fixture f = new Fixture())
            {
                RaidPlaybackStateDiagnostics diagnostics = new RaidPlaybackStateDiagnostics();
                Func<string> log = () => diagnostics.Observe(f.Session, f.Evidence, f.Library.IsReady,
                    f.Library.Revision, f.RaidAuto, f.Tracks, f.Routing, "Test");
                string captured = log();
                Assert.Contains("Dopamine", captured);
                Assert.Contains("capturedSamples=5774928", captured);
                Assert.Contains("capturedTime=120.311", captured);
                Assert.Contains("readiness=WaitingForOutcome", captured);
                for (int frame = 0; frame < 2000; frame++) Assert.Null(log());
                f.Ready(ExitStatus.Survived);
                string ready = log();
                Assert.Contains("readiness=Ready", ready);
                Assert.Contains("remappedTrack=" + f.Session.Candidate.TrackId + "/Dopamine", ready);
                Assert.Contains("extractEligible=1 deathEligible=1", ready);
                Assert.Null(log());
                f.Session.CancelSavedResume(RaidCancellationReason.Stop);
                string cancelled = log();
                Assert.Contains("cancellationReason=Stop action=Cancelled", cancelled);
                Assert.Null(log());
            }
        }

        [Fact]
        [Trait("Validation", "SptApiContracts")]
        public void RuntimeEventsAndOptionalModelAreWiredWithoutUserCancellationOrBlindDelay()
        {
            string player = UxFixSource.Read("Audio", "SoulAudioPlayer.cs");
            Assert.Contains("_library.Changed += OnLibraryChanged", player);
            Assert.Contains("_library.ScanStateChanged += OnLibraryScanStateChanged", player);
            Assert.Contains("_library.Changed -= OnLibraryChanged", player);
            string changed = UxFixSource.Method(player, "private void OnLibraryChanged", "private void OnLibraryScanStateChanged");
            Assert.Contains("_raidLibraryTracks = _library.Tracks", changed);
            Assert.DoesNotContain("Cancel", changed);
            string coordinator = UxFixSource.Read("Audio", "PostRaidCoordinator.cs");
            Assert.Contains("_screens.OnScreenChanged += OnScreenChanged", coordinator);
            Assert.Contains("_screens.OnScreenChanged -= OnScreenChanged", coordinator);
            Assert.DoesNotContain("Time.", coordinator);
            string readiness = UxFixSource.Read("Audio", "StableRaidMenuContext.cs");
            Assert.Contains("ResultModelField == null ? null", readiness);
            Assert.DoesNotContain("throw new MissingFieldException", readiness);
            Assert.Contains("EEftScreenType.Inventory", readiness);
            Assert.NotNull(typeof(EftScreenManager).GetEvent("OnScreenChanged"));
            string recorder = UxFixSource.Method(UxFixSource.Read("Recorder", "SoulRecorderController.cs"),
                "private void AcquireHandsForStart", "private bool EnterStopInteraction");
            Assert.True(recorder.IndexOf("_usableItemController.EnterInteraction", StringComparison.Ordinal) <
                recorder.IndexOf("PreserveMainForRecorder", StringComparison.Ordinal));
        }

        private static RaidMenuEvidence ReadyEvidence()
        {
            return new RaidMenuEvidence { Screen = "ExitStatus", ReturnScreenShown = true,
                RecognizedScreen = true, ScreenActive = true, Preloader = false,
                BlackOverlay = false, ResultModel = "Unavailable" };
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly TestMusicFiles Files = new TestMusicFiles();
            internal readonly MusicLibrary Library = new MusicLibrary(delegate { }, delegate { });
            internal readonly RaidPlaybackSession Session = new RaidPlaybackSession();
            internal readonly TrackRoutingService Routing;
            internal IReadOnlyList<MusicTrack> Tracks = new List<MusicTrack>();
            internal RaidMenuEvidence Evidence = new RaidMenuEvidence { Screen = "BattleUI", PlayerPresent = true, PlayerAlive = true };
            internal bool RaidAuto = true, ReadyInsideChanged, ReevaluationRequested;
            internal int Changes, OutcomeSelections, MainSelections;
            internal MusicTrack Main { get { return Tracks.Single(track => track.Title == "Dopamine"); } }
            internal MusicTrack Other { get { return Tracks.Single(track => track.Title == "Other Main"); } }
            internal MusicTrack Extract { get { return Tracks.Single(track => track.Title == "Extract"); } }
            internal MusicTrack Death { get { return Tracks.Single(track => track.Title == "Death"); } }
            internal RaidReadinessReason Reason { get { return Evidence.Evaluate(Session.Outcome.HasValue, Library.IsReady); } }

            internal Fixture()
            {
                Files.Create("Unquote - Dopamine.mp3", 91);
                Files.Create("Artist - Other Main.mp3", 92);
                Files.Create("Artist - Extract.mp3", 93);
                Files.Create("Artist - Death.mp3", 94);
                Routing = Files.CreateRouting();
                Library.Changed += () =>
                {
                    Changes++;
                    Tracks = Library.Tracks;
                    ReadyInsideChanged = Library.IsReady;
                    ReevaluationRequested = true;
                };
                Library.ScanStateChanged += () => ReevaluationRequested = true;
                Library.BeginScan(new[] { Files.Root });
                ApplyScan();
                Routing.SetRoutes(Main, TrackRoute.Main);
                Routing.SetRoutes(Other, TrackRoute.Main);
                Routing.SetRoutes(Extract, TrackRoute.Extract);
                Routing.SetRoutes(Death, TrackRoute.Death);
                Session.Begin(MainPlaybackSnapshot.Capture(Main, Routing, 120.311f, 5774928, 48000,
                    true, false, new[] { Other, Main }, 1, true, 0));
                ReevaluationRequested = false;
            }

            internal void ApplyScan()
            {
                ScanResult result;
                Assert.True(SpinWait.SpinUntil(() => Library.TryApplyCompletedScan(out result), TimeSpan.FromSeconds(5)));
            }

            internal void Ready(ExitStatus outcome)
            {
                Session.RecordOutcome(outcome);
                Evidence = ReadyEvidence();
                Session.SetMenuReady(true);
            }

            internal RaidPlaybackAction Step()
            {
                Session.SetMenuReady(Evidence.Evaluate(Session.Outcome.HasValue, true) == RaidReadinessReason.Ready);
                if (Reason != RaidReadinessReason.Ready) return new RaidPlaybackAction();
                return Session.TakeNext(RaidAuto, Tracks, Routing, true,
                    count => { MainSelections++; return count - 1; },
                    count => { OutcomeSelections++; return count - 1; });
            }

            public void Dispose() { Files.Dispose(); }
        }
    }
}
