using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using SoulPlayer.Audio;
using SoulPlayer.Library;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    [Trait("Validation", "PostRaidLifecycle")]
    public sealed class RaidPlaybackSessionTests
    {
        [Fact]
        public void LoadingNeverStartsMainOrAdvancesShuffleEvenAfterResult()
        {
            using (Fixture f = new Fixture())
            {
                f.Session.RecordOutcome(ExitStatus.Survived);
                List<string> warnings = new List<string>();
                for (int frame = 0; frame < 2000; frame++)
                {
                    Assert.False(f.Session.CanStart(PlaybackIntent.AutomaticMain, warnings.Add));
                    Assert.False(f.Session.CanStart(PlaybackIntent.Manual));
                    Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                }
                Assert.True(f.Session.MainSuspended);
                Assert.Equal(0, f.MainSelections);
                Assert.Equal(0, f.OutcomeSelections);
                Assert.Equal(1, f.Session.Candidate.QueueIndex);
                Assert.Equal(new[] { f.Other, f.Main }, f.Session.Candidate.Queue);
                Assert.Single(warnings);
                Assert.Contains("mainSuspended=True", warnings[0]);
            }
        }

        [Theory]
        [InlineData(ExitStatus.Survived, 2)]
        [InlineData(ExitStatus.Killed, 4)]
        [InlineData(ExitStatus.MissingInAction, 4)]
        [Trait("Validation", "PlaybackSelection")]
        public void StableMenuPlaysExactlySelectedOutcomeOnce(ExitStatus outcome, int routeValue)
        {
            TrackRoute route = (TrackRoute)routeValue;
            using (Fixture f = new Fixture())
            {
                f.Session.RecordOutcome(outcome);
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                f.Session.SetMenuReady(true);
                RaidPlaybackAction selected = f.Step();
                Assert.Equal(RaidPlaybackActionKind.Routed, selected.Kind);
                Assert.Equal(route, selected.Route);
                Assert.Same(route == TrackRoute.Extract ? f.Extract : f.Death, selected.Track);
                Assert.False(f.Routing.IsEligible(selected.Track, TrackRoute.Main));
                ExactTrackPlaybackRequest request = new ExactTrackPlaybackRequest(1, selected.Route, selected.Track);
                Assert.False(request.TryMarkStarted(f.Main));
                Assert.True(request.TryMarkStarted(selected.Track));
                Assert.False(request.TryMarkStarted(selected.Track));
                f.Session.RoutedStarted(selected.Track);
                for (int frame = 0; frame < 100; frame++)
                {
                    Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                    Assert.False(f.Session.RecordOutcome(outcome));
                    Assert.False(f.Session.CanStart(PlaybackIntent.AutomaticMain));
                }
                Assert.Equal(1, f.OutcomeSelections);
                Assert.Equal(0, f.MainSelections);
            }
        }

        [Fact]
        public void NaturalOutcomeEndResumesExactMainAtSavedSamplesWithQueueAndShuffleIntact()
        {
            using (Fixture f = new Fixture())
            {
                RaidPlaybackAction routed = f.ReadyRoute();
                Assert.False(f.Session.EndRouted(routed.Track, false)); // Not started yet.
                f.Session.RoutedStarted(routed.Track);
                Assert.False(f.Session.EndRouted(f.Other, false));
                Assert.True(f.Session.EndRouted(routed.Track, false));
                RaidPlaybackAction resume = f.Step();
                Assert.Equal(RaidPlaybackActionKind.ResumeExact, resume.Kind);
                Assert.Same(f.Main, resume.Track);
                Assert.Equal(TrackRoutingService.GetTrackId(f.Main), resume.Snapshot.TrackId);
                Assert.Equal(f.Main.FilePath, resume.Snapshot.Path);
                Assert.Equal(37.25f, resume.Snapshot.Seconds);
                Assert.Equal(1788000, resume.Snapshot.ResumeSamples(48000 * 180, 48000));
                Assert.True(resume.Snapshot.WasPlaying);
                Assert.False(resume.Snapshot.WasPaused);
                Assert.True(resume.Snapshot.Shuffle);
                Assert.Equal(1, resume.Snapshot.RepeatMode);
                Assert.Equal(new[] { f.Other, f.Main }, resume.Queue);
                Assert.Equal(0, f.MainSelections);
                Assert.False(f.Session.CanStart(PlaybackIntent.AutomaticMain));
                Assert.True(f.Session.CanStart(PlaybackIntent.Resume));
                f.Session.ResumeStarted(resume.Track);
                Assert.False(f.Session.MainSuspended);
                Assert.Equal(RaidResumeResult.ResumedExact, f.Session.ResumeResult);
                Assert.False(f.Session.CanStart(PlaybackIntent.Resume)); // Stale decoder cannot replay.
                for (int frame = 0; frame < 100; frame++)
                {
                    Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                    Assert.False(f.Session.EndRouted(routed.Track, false));
                }
                string log = f.Session.TakeDiagnostic();
                Assert.Contains("capturedMain=" + TrackRoutingService.GetTrackId(f.Main), log);
                Assert.Contains("capturedTime=37.250 mainSuspended=False outcome=Survived", log);
                Assert.Contains("menuReady=True resumeResult=ResumedExact", log);
                Assert.Null(f.Session.TakeDiagnostic());
                Assert.Equal(1, f.OutcomeSelections);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RaidAutoOffDirectlyRestoresPlayingOrPausedMainAfterMenuReady(bool paused)
        {
            using (Fixture f = new Fixture(paused))
            {
                f.Session.RecordOutcome(ExitStatus.Killed);
                Assert.Equal(RaidPlaybackActionKind.None, f.Step(false).Kind);
                f.Session.SetMenuReady(true);
                RaidPlaybackAction resume = f.Step(false);
                Assert.Equal(RaidPlaybackActionKind.ResumeExact, resume.Kind);
                Assert.Same(f.Main, resume.Track);
                Assert.Equal(paused, resume.Snapshot.WasPaused);
                Assert.Equal(!paused, resume.Snapshot.WasPlaying);
                Assert.Equal(0, f.OutcomeSelections);
                Assert.Equal(0, f.MainSelections);
            }
        }

        [Theory]
        [InlineData("missing")]
        [InlineData("disabled")]
        [InlineData("removed")]
        public void UnavailableOrMainDisabledCandidateSelectsOnlyValidMainFallback(string reason)
        {
            using (Fixture f = new Fixture())
            {
                RaidPlaybackAction routed = f.ReadyRoute();
                f.Session.RoutedStarted(routed.Track);
                if (reason == "missing") f.Exists = path => path != f.Main.FilePath;
                if (reason == "disabled") f.Routing.SetRoutes(f.Main, TrackRoute.Death);
                if (reason == "removed") f.Tracks.Remove(f.Main);
                f.Session.EndRouted(routed.Track, false);
                RaidPlaybackAction fallback = f.Step();
                Assert.Equal(RaidPlaybackActionKind.Fallback, fallback.Kind);
                Assert.Same(f.Other, fallback.Track);
                Assert.All(fallback.Queue, track => Assert.True(f.Routing.IsEligible(track, TrackRoute.Main)));
                Assert.Null(fallback.Snapshot);
                f.Session.ResumeStarted(fallback.Track);
                Assert.Equal(RaidResumeResult.SelectedFallback, f.Session.ResumeResult);
                Assert.Equal(1, f.MainSelections);
            }
        }

        [Theory]
        [InlineData("Library")]
        [InlineData("Stop")]
        [InlineData("Next")]
        [InlineData("Previous")]
        [InlineData("Explicit request")]
        public void InRaidNormalControlsCannotCancelBeforeStableMenuReturn(string action)
        {
            Assert.NotEmpty(action);
            using (Fixture f = new Fixture())
            {
                MainPlaybackSnapshot saved = f.Session.Candidate;
                string name = action == "Library" ? "ManualLibrarySelection" :
                    action == "Explicit request" ? "ExplicitPlaybackRequest" : action;
                RaidCancellationReason reason = (RaidCancellationReason)Enum.Parse(typeof(RaidCancellationReason), name);
                Assert.False(f.Session.CancelSavedResume(reason));
                Assert.Same(saved, f.Session.Candidate);
                Assert.True(f.Session.MainSuspended);
                f.Session.RecordOutcome(ExitStatus.Survived);
                Assert.False(f.Session.CancelSavedResume(reason)); // Result alone is not a menu return.
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                f.Session.SetMenuReady(true);
                Assert.Same(saved, f.Step(false).Snapshot);
                Assert.Equal(RaidCancellationReason.None, f.Session.CancellationReason);
                Assert.Equal(0, f.OutcomeSelections);
                Assert.Equal(0, f.MainSelections);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ManualCancellationDuringRoutedOrResumeDecodeRejectsLateCompletion(bool resuming)
        {
            using (Fixture f = new Fixture())
            {
                RaidPlaybackAction routed = f.ReadyRoute();
                f.Session.RoutedStarted(routed.Track);
                if (resuming) { f.Session.EndRouted(routed.Track, false); f.Step(); }
                f.Session.CancelSavedResume();
                Assert.False(f.Session.CanStart(PlaybackIntent.Routed));
                Assert.False(f.Session.CanStart(PlaybackIntent.Resume));
                f.Session.ResumeStarted(f.Main);
                Assert.False(f.Session.EndRouted(routed.Track, false));
                Assert.Equal(RaidResumeResult.Cancelled, f.Session.ResumeResult);
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CancellationAfterMenuBecomesUnreadyDoesNotStrandOrRevivePendingAudio(bool resuming)
        {
            using (Fixture f = new Fixture())
            {
                RaidPlaybackAction routed = f.ReadyRoute();
                f.Session.RoutedStarted(routed.Track);
                if (resuming) { f.Session.EndRouted(routed.Track, false); f.Step(); }
                f.Session.SetMenuReady(false);
                f.Session.CancelSavedResume();
                Assert.True(f.Session.MainSuspended);
                Assert.False(f.Session.CanStart(PlaybackIntent.Manual));
                Assert.False(f.Session.CanStart(PlaybackIntent.Resume));
                Assert.False(f.Session.CanStart(PlaybackIntent.Routed));
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                f.Session.SetMenuReady(true);
                Assert.Equal(RaidPlaybackActionKind.Finish, f.Step().Kind);
                Assert.Equal(RaidResumeResult.Cancelled, f.Session.ResumeResult);
                Assert.Equal(0, f.MainSelections);
                Assert.Equal(1, f.OutcomeSelections);
            }
        }

        [Fact]
        public void RecorderPreservesSavedMainAndLaterOutcomeCue()
        {
            using (Fixture f = new Fixture())
            {
                MainPlaybackSnapshot saved = f.Session.Candidate;
                Assert.True(f.Session.NoteSoulRecorderUse());
                Assert.False(f.Session.NoteSoulRecorderUse());
                RaidPlaybackAction routed = f.ReadyRoute();
                Assert.Equal(RaidPlaybackActionKind.Routed, routed.Kind);
                f.Session.RoutedStarted(routed.Track);
                f.Session.EndRouted(routed.Track, false);
                RaidPlaybackAction resume = f.Step();
                Assert.Equal(RaidPlaybackActionKind.ResumeExact, resume.Kind);
                Assert.Same(saved, resume.Snapshot);
                Assert.Equal(RaidCancellationReason.None, f.Session.CancellationReason);
                Assert.Equal(0, f.MainSelections);
            }
        }

        [Fact]
        public void PausePreservesCandidateAndNeverLooksLikeNaturalOutcomeCompletion()
        {
            using (Fixture f = new Fixture())
            {
                f.Session.NoteSoulRecorderUse();
                MainPlaybackSnapshot saved = f.Session.Candidate;
                RaidPlaybackAction routed = f.ReadyRoute();
                f.Session.RoutedStarted(routed.Track);
                PlaybackCompletionTracker completion = new PlaybackCompletionTracker();
                completion.Started(true);
                for (int frame = 0; frame < 500; frame++)
                {
                    Assert.False(completion.Poll(true, false, false, true, false, false));
                    Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                    Assert.Same(saved, f.Session.Candidate);
                }
                Assert.False(completion.Poll(true, true, false, false, false, false));
                Assert.True(completion.Poll(true, false, false, false, false, false));
                f.Session.EndRouted(routed.Track, false);
                Assert.Same(saved, f.Step().Snapshot);
            }
        }

        [Fact]
        public void LostMenuReadinessAlsoBlocksRoutedAndResumeDecoderStarts()
        {
            using (Fixture f = new Fixture())
            {
                RaidPlaybackAction routed = f.ReadyRoute();
                f.Session.SetMenuReady(false);
                Assert.False(f.Session.CanStart(PlaybackIntent.Routed));
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                f.Session.SetMenuReady(true);
                f.Session.RoutedStarted(routed.Track);
                f.Session.EndRouted(routed.Track, false);
                RaidPlaybackAction resume = f.Step();
                f.Session.SetMenuReady(false);
                Assert.False(f.Session.CanStart(PlaybackIntent.Resume));
                f.Session.SetMenuReady(true);
                Assert.True(f.Session.CanStart(PlaybackIntent.Resume));
                Assert.Same(f.Main, resume.Track);
                Assert.Equal(1, f.OutcomeSelections);
            }
        }

        [Fact]
        public void FailedOutcomeRecoversExactMainAndFailedExactResumeFallsBackOnce()
        {
            using (Fixture f = new Fixture())
            {
                RaidPlaybackAction routed = f.ReadyRoute();
                Assert.True(f.Session.EndRouted(routed.Track, true));
                Assert.Equal(RaidPlaybackActionKind.ResumeExact, f.Step().Kind);
                f.Session.ResumeFailed();
                RaidPlaybackAction fallback = f.Step();
                Assert.Equal(RaidPlaybackActionKind.Fallback, fallback.Kind);
                Assert.Same(f.Other, fallback.Track);
                f.Session.ResumeFailed();
                Assert.Equal(RaidResumeResult.None, f.Session.ResumeResult);
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                Assert.Equal(1, f.OutcomeSelections);
                Assert.Equal(1, f.MainSelections);
            }
        }

        [Fact]
        public void NoMainCandidatesEndsSilentlyWithoutShuffleOrMainFalseFallback()
        {
            using (Fixture f = new Fixture())
            {
                f.Routing.SetRoutes(f.Main, TrackRoute.Death);
                f.Routing.SetRoutes(f.Other, TrackRoute.Extract);
                f.Session.RecordOutcome(ExitStatus.Survived);
                f.Session.SetMenuReady(true);
                Assert.Equal(RaidPlaybackActionKind.Finish, f.Step(false).Kind);
                Assert.Equal(RaidResumeResult.None, f.Session.ResumeResult);
                Assert.Equal(0, f.MainSelections);
            }
        }

        [Fact]
        public void RescannedTrackUsesStableIdAndPathAndPreservesSavedQueueOrder()
        {
            using (Fixture f = new Fixture())
            {
                MusicTrack refreshed = new MusicTrack(f.Main.FilePath, f.Main.SizeBytes);
                f.Tracks[f.Tracks.IndexOf(f.Main)] = refreshed;
                f.Session.RecordOutcome(ExitStatus.Survived);
                f.Session.SetMenuReady(true);
                RaidPlaybackAction resume = f.Step(false);
                Assert.Same(refreshed, resume.Track);
                Assert.Equal(new[] { f.Other, refreshed }, resume.Queue);
            }
        }

        [Fact]
        public void PositionConvertsSampleRateAndClampsSafely()
        {
            using (Fixture f = new Fixture())
            {
                Assert.Equal((int)(37.25f * 44100), f.Session.Candidate.ResumeSamples(44100 * 100, 44100));
                Assert.Equal(99, f.Session.Candidate.ResumeSamples(100, 48000));
                Assert.Equal(0, f.Session.Candidate.ResumeSamples(0, 48000));
            }
        }

        [Fact]
        public void NewRaidResetsCancellationAndDuplicateGateWithoutAnyTimer()
        {
            using (Fixture f = new Fixture())
            {
                MainPlaybackSnapshot captured = f.Session.Candidate;
                f.ReadyRoute();
                f.Session.CancelSavedResume();
                f.Session.Begin(captured);
                Assert.True(f.Session.RecordOutcome(ExitStatus.Killed));
                Assert.False(f.Session.MenuReady);
                f.Session.SetMenuReady(true);
                Assert.Same(f.Death, f.Step().Track);
            }
        }

        [Fact]
        public void DuplicateDeploymentCannotReplaceAnyUnresolvedSnapshot()
        {
            using (Fixture f = new Fixture())
            {
                MainPlaybackSnapshot saved = f.Session.Candidate;
                Assert.False(f.Session.Begin(null));
                Assert.False(f.Session.CarryMainToNextDeployment());
                RaidPlaybackAction cue = f.ReadyRoute();
                Assert.False(f.Session.Begin(null));
                Assert.Same(saved, f.Session.Candidate);
                Assert.Same(cue.Track, f.Session.RoutedTrack);
                f.Session.RoutedStarted(cue.Track);
                f.Session.EndRouted(cue.Track, false);
                RaidPlaybackAction resume = f.Step();
                Assert.False(f.Session.Begin(null));
                Assert.Same(saved, f.Session.Candidate);
                f.Session.ResumeStarted(resume.Track);
                Assert.True(f.Session.Begin(saved)); // Only a completed lifecycle may be replaced.
            }
        }

        [Fact]
        public void NewDeploymentClosesReturnedLifecycleAndCarriesUnresolvedMainInsteadOfOutcome()
        {
            using (Fixture f = new Fixture())
            {
                MainPlaybackSnapshot saved = f.Session.Candidate;
                RaidPlaybackAction oldCue = f.ReadyRoute();
                f.Session.RoutedStarted(oldCue.Track);
                Assert.True(f.Session.CarryMainToNextDeployment());
                Assert.Same(saved, f.Session.Candidate);
                Assert.Null(f.Session.Outcome);
                Assert.False(f.Session.MenuReady);
                Assert.False(f.Session.HasReturnedToMenu);
                Assert.False(f.Session.CarryMainToNextDeployment()); // Duplicate edge.
                Assert.False(f.Session.EndRouted(oldCue.Track, false));
                Assert.False(f.Session.CanStart(PlaybackIntent.Routed));
                Assert.True(f.Session.RecordOutcome(ExitStatus.Killed));
                f.Session.SetMenuReady(true);
                Assert.Same(saved, f.Step(false).Snapshot);
            }
        }

        [Fact]
        public void ReadinessRequiresEveryRealLifecycleCondition()
        {
            for (int mask = 0; mask < 128; mask++)
            {
                bool[] facts = Enumerable.Range(0, 7).Select(bit => (mask & (1 << bit)) != 0).ToArray();
                bool expected = facts[0] && !facts[1] && facts[2] && facts[3] && !facts[4] && !facts[5];
                RaidMenuEvidence evidence = new RaidMenuEvidence
                {
                    ReturnScreenShown = facts[0], PlayerPresent = facts[1], RecognizedScreen = facts[2],
                    ScreenActive = facts[3], Preloader = facts[4], BlackOverlay = facts[5],
                    ResultModel = facts[6] ? "Complete" : "Unavailable"
                };
                Assert.Equal(expected, evidence.Evaluate(true, true) == RaidReadinessReason.Ready);
            }
        }

        [Fact]
        [Trait("Validation", "SptApiContracts")]
        public void InstalledEftExposesTheDeploymentAndStableMenuContracts()
        {
            Assembly eft = typeof(Player).Assembly;
            Type countdown = eft.GetType("EFT.UI.Matchmaker.MatchmakerFinalCountdown", true);
            Assert.NotNull(countdown.GetMethod("Show", new[] { typeof(Profile), typeof(DateTime) }));
            Type preloader = eft.GetType("EFT.UI.PreloaderUI", true);
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Assert.NotNull(preloader.GetField("_loader", fields));
            Assert.NotNull(preloader.GetField("_overlapBlackImage", fields));
            Type result = eft.GetType("EFT.UI.SessionEnd.SessionResultExitStatus", true);
            Assert.NotNull(result.GetField("_playerModelView", fields));
            Type model = eft.GetType("EFT.UI.PlayerModelView", true);
            Assert.Equal(typeof(bool), model.GetProperty("LoadingComplete").PropertyType);
            Type manager = eft.GetType("EFT.UI.Screens.EftScreenManager", true);
            Assert.NotNull(manager.GetProperty("CurrentScreenController"));
            Assert.NotNull(manager.GetMethod("TryGetScreen"));
        }

        [Fact]
        public void RuntimeWiresCaptureCancellationFinalStartAndActualMiniPlayerTrack()
        {
            string source = UxFixSource.Read("Audio", "SoulAudioPlayer.cs");
            string capture = UxFixSource.Method(source, "internal void BeginRaidSuspension", "internal bool ObserveRaidResult");
            Assert.True(capture.IndexOf("MainPlaybackSnapshot.Capture", StringComparison.Ordinal) <
                capture.IndexOf("_source.Pause()", StringComparison.Ordinal));
            Assert.Contains("_source.timeSamples", capture);
            Assert.Contains("InvalidateLoad()", capture);
            string[] methods = { "internal void Play(", "internal void Stop()", "internal void Next()", "internal void Previous()" };
            foreach (string method in methods)
            {
                int start = source.IndexOf(method, StringComparison.Ordinal);
                int end = source.IndexOf("\n        }", start, StringComparison.Ordinal);
                Assert.Contains("CancelSavedResume(RaidCancellationReason.", source.Substring(start, end - start));
            }
            string pause = UxFixSource.Method(source, "internal void TogglePause", "internal void Stop()");
            Assert.DoesNotContain("CancelSavedResume", pause);
            Assert.DoesNotContain("_source.time = 0", pause);
            string next = UxFixSource.Method(source, "private void NextInQueue", "private void AdvanceAutomatically");
            Assert.True(next.IndexOf("CanStart(intent)", StringComparison.Ordinal) < next.IndexOf("_random.Next", StringComparison.Ordinal));
            string startClip = UxFixSource.Method(source, "private void StartClip", "private void StopPlayback");
            Assert.True(startClip.IndexOf("generation != _loadGeneration", StringComparison.Ordinal) <
                startClip.IndexOf("_source.Play()", StringComparison.Ordinal));
            Assert.True(startClip.IndexOf("CanStart(_loadingIntent)", StringComparison.Ordinal) <
                startClip.IndexOf("_source.Play()", StringComparison.Ordinal));
            Assert.True(startClip.IndexOf("_source.timeSamples = snapshot.ResumeSamples", StringComparison.Ordinal) <
                startClip.IndexOf("_source.Play()", StringComparison.Ordinal));
            Assert.Contains("if (!_paused)", startClip);
            Assert.Contains("CurrentTrack = track;", source);
            Assert.Contains("get { return CurrentTrack; }", source);
            Assert.Contains("Plugin.AudioPlayer.DisplayTrack", UxFixSource.Read("UI", "SoulMiniPlayer.cs"));
            Assert.Contains("Plugin.AudioPlayer.PreserveMainForRecorder()", UxFixSource.Read("Recorder", "SoulRecorderController.cs"));
            Assert.Contains("BeginRaidSuspension()", UxFixSource.Read("Patches", "RaidDeploymentPatch.cs"));
            Assert.Contains("StableRaidMenuContext.IsReturnScreen", UxFixSource.Read("Utils", "GameState.cs"));
        }

        private sealed class Fixture : IDisposable
        {
            private readonly TestMusicFiles _files = new TestMusicFiles();
            internal readonly RaidPlaybackSession Session = new RaidPlaybackSession();
            internal readonly TrackRoutingService Routing;
            internal readonly MusicTrack Main, Other, Extract, Death;
            internal readonly List<MusicTrack> Tracks;
            internal int MainSelections, OutcomeSelections;
            internal Func<string, bool> Exists = System.IO.File.Exists;

            internal Fixture(bool paused = false)
            {
                Main = _files.Create("A - Saved Main.mp3", 201);
                Other = _files.Create("B - Other Main.mp3", 202);
                Extract = _files.Create("C - Extract.mp3", 203);
                Death = _files.Create("D - Death.mp3", 204);
                Routing = _files.CreateRouting();
                Routing.SetRoutes(Main, TrackRoute.Main);
                Routing.SetRoutes(Other, TrackRoute.Main);
                Routing.SetRoutes(Extract, TrackRoute.Extract);
                Routing.SetRoutes(Death, TrackRoute.Death);
                Tracks = new List<MusicTrack> { Main, Other, Extract, Death };
                Session.Begin(MainPlaybackSnapshot.Capture(Main, Routing, 37.25f, 1788000, 48000,
                    !paused, paused, new[] { Other, Main }, 1, true, 1));
            }

            internal RaidPlaybackAction Step(bool raidAuto = true)
            {
                return Session.TakeNext(raidAuto, Tracks, Routing, true,
                    count => { MainSelections++; return count - 1; },
                    count => { OutcomeSelections++; return count - 1; }, Exists);
            }

            internal RaidPlaybackAction ReadyRoute()
            {
                Session.RecordOutcome(ExitStatus.Survived);
                Session.SetMenuReady(true);
                return Step();
            }

            public void Dispose() { _files.Dispose(); }
        }
    }
}
