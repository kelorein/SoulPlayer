using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using EFT;
using SoulPlayer.Audio;
using SoulPlayer.Cassettes;
using SoulPlayer.Library;
using SoulPlayer.Recorder;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    [Trait("Validation", "PostRaidLifecycle")]
    [Trait("Validation", "RecorderMainResume")]
    [Trait("Validation", "ExactPositionResume")]
    public sealed class SoulRecorderMainResumeTests
    {
        [Fact]
        public void Dopamine120311MultipleCassettesNextStopRaidEndNoOutcomeResumesExactlyOnce()
        {
            using (Fixture f = new Fixture())
            {
                Assert.Equal("Dopamine", f.Saved.Title);
                Assert.True(f.Saved.WasPlaying);
                Assert.False(f.Saved.WasPaused);
                Assert.Equal(120.311f, f.Saved.Seconds);
                Assert.Equal(5774928, f.Saved.Samples);
                Assert.Equal(f.Main.FilePath, f.Saved.Path);
                Assert.Equal(TrackRoutingService.GetTrackId(f.Main), f.Saved.TrackId);
                f.UseMultipleCassettes();
                f.EndRaid(ExitStatus.Left);
                f.AssertLoadingSilent();
                f.MenuReady();
                f.AssertExactResume(f.Step());
                Assert.Equal(0, f.OutcomeSelections);
                Assert.Equal(0, f.MainSelections);
                Assert.Equal(4, f.RecorderStarts);
                string log = f.Session.TakeDiagnostic();
                Assert.Contains("capturedTime=120.311", log);
                Assert.Contains("routedTrack=none", log);
                Assert.Contains("resumeResult=ResumedExact", log);
                Assert.Contains("cancellationReason=None", log);
                Assert.Contains("snapshotPreservedThroughRaid=True", log);
                Assert.Null(f.Session.TakeDiagnostic());
            }
        }

        [Theory]
        [InlineData(ExitStatus.Survived, "Extract")]
        [InlineData(ExitStatus.Killed, "Death")]
        public void RecorderThenExactOutcomeCueNaturallyFinishesBeforeSavedMain(ExitStatus outcome, string title)
        {
            using (Fixture f = new Fixture())
            {
                f.EnableOutcomes();
                f.UseMultipleCassettes();
                f.EndRaid(outcome);
                f.AssertLoadingSilent();
                f.MenuReady();
                RaidPlaybackAction cue = f.Step();
                Assert.Equal(RaidPlaybackActionKind.Routed, cue.Kind);
                Assert.Equal(title, cue.Track.Title);
                Assert.Same(title == "Extract" ? f.Extract : f.Death, cue.Track);
                Assert.False(f.Session.EndRouted(cue.Track, false));
                ExactTrackPlaybackRequest exact = new ExactTrackPlaybackRequest(1, cue.Route, cue.Track);
                Assert.True(exact.TryMarkStarted(cue.Track));
                Assert.False(exact.TryMarkStarted(f.Main));
                f.Session.RoutedStarted(cue.Track);
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                f.AssertPreserved();
                Assert.True(f.Session.EndRouted(cue.Track, false));
                f.AssertExactResume(f.Step());
                Assert.False(f.Session.EndRouted(cue.Track, false));
                Assert.Equal(1, f.OutcomeSelections);
                Assert.Equal(0, f.MainSelections);
            }
        }

        [Fact]
        public void RaidAutoOffWithRecorderAndEligibleCuesResumesMainDirectly()
        {
            using (Fixture f = new Fixture())
            {
                f.EnableOutcomes();
                f.RaidAuto = false;
                f.UseMultipleCassettes();
                f.EndRaid(ExitStatus.Survived);
                f.MenuReady();
                f.AssertExactResume(f.Step());
                Assert.Equal(0, f.OutcomeSelections);
            }
        }

        [Fact]
        [Trait("Validation", "LibraryRescan")]
        [Trait("Validation", "RaidReadiness")]
        public void RecorderStopNextAndRealLibraryRescanRetainStableIdPositionAndQueue()
        {
            using (Fixture f = new Fixture())
            {
                MusicTrack original = f.Main;
                f.StartCassette(f.Other);
                f.NextCassette(f.Extract);
                f.StopCassette();
                f.Rescan();
                Assert.NotSame(original, f.Main);
                f.AssertPreserved();
                f.StartCassette(f.Death);
                f.EndRaid(ExitStatus.Killed);
                f.Evidence.ScreenActive = false; // Menu reconstruction is not readiness.
                f.AssertLoadingSilent();
                f.MenuReady();
                f.AssertExactResume(f.Step());
            }
        }

        [Theory]
        [InlineData("Preparing")]
        [InlineData("Playing")]
        [InlineData("Ejecting")]
        public void RecorderCleanupFromAnyPhaseCannotOwnOrCancelMain(string phase)
        {
            using (Fixture f = new Fixture())
            {
                if (phase == "Preparing")
                {
                    Assert.True(f.Recorder.BeginStartInteraction());
                    f.Session.NoteSoulRecorderUse();
                }
                else
                {
                    f.StartCassette(f.Other);
                    if (phase == "Ejecting") Assert.True(f.Recorder.BeginStopInteraction());
                }
                f.EndRaid(ExitStatus.Killed);
                f.AssertPreserved();
                f.MenuReady();
                f.AssertExactResume(f.Step());
            }
        }

        [Theory]
        [InlineData("ManualLibrarySelection")]
        [InlineData("Stop")]
        [InlineData("Next")]
        [InlineData("Previous")]
        [InlineData("ExplicitPlaybackRequest")]
        public void ExplicitMenuDecisionAfterRecorderUseCancelsWithoutLateReclaim(string reason)
        {
            using (Fixture f = new Fixture())
            {
                f.EnableOutcomes();
                f.UseMultipleCassettes();
                f.EndRaid(ExitStatus.Survived);
                f.MenuReady();
                RaidPlaybackAction cue = f.Step();
                f.Session.RoutedStarted(cue.Track);
                Assert.True(f.Session.CancelSavedResume(
                    (RaidCancellationReason)Enum.Parse(typeof(RaidCancellationReason), reason)));
                Assert.Null(f.Session.Candidate);
                f.Rescan();
                Assert.False(f.Session.EndRouted(cue.Track, false));
                f.Session.ResumeStarted(f.Main);
                for (int i = 0; i < 100; i++) Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                Assert.Equal(RaidResumeResult.Cancelled, f.Session.ResumeResult);
                Assert.Equal(reason, f.Session.CancellationReason.ToString());
                Assert.Equal(0, f.MainSelections);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MissingOrMainDisabledAfterRecorderUseSelectsOneValidFallback(bool disabled)
        {
            using (Fixture f = new Fixture())
            {
                f.UseMultipleCassettes();
                if (disabled) f.Routing.SetRoutes(f.Main, TrackRoute.None);
                else f.Exists = path => path != f.Main.FilePath && File.Exists(path);
                f.EndRaid(ExitStatus.Left);
                f.MenuReady();
                RaidPlaybackAction fallback = f.Step();
                Assert.Equal(RaidPlaybackActionKind.Fallback, fallback.Kind);
                Assert.Same(f.Other, fallback.Track);
                Assert.Null(fallback.Snapshot);
                Assert.All(fallback.Queue, track => Assert.True(f.Routing.IsEligible(track, TrackRoute.Main)));
                f.Session.ResumeStarted(fallback.Track);
                for (int i = 0; i < 100; i++) Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                Assert.Equal(1, f.MainSelections);
                Assert.Equal(RaidResumeResult.SelectedFallback, f.Session.ResumeResult);
            }
        }

        [Fact]
        public void RecorderPreservationDiagnosticIsOncePerRaidAndNeverACancellation()
        {
            using (Fixture f = new Fixture())
            {
                RaidPlaybackStateDiagnostics diagnostics = new RaidPlaybackStateDiagnostics();
                Func<string> log = () => diagnostics.Observe(f.Session, f.Evidence, f.Library.IsReady,
                    f.Library.Revision, true, f.Tracks, f.Routing, "SoulRecorderUseSnapshotPreserved");
                Assert.NotNull(log());
                Assert.True(f.Session.NoteSoulRecorderUse());
                string preserved = log();
                Assert.Contains("recorderUsed=True snapshotPreservedThroughRaid=True", preserved);
                Assert.Contains("cancellationReason=None", preserved);
                for (int i = 0; i < 2000; i++)
                {
                    Assert.False(f.Session.NoteSoulRecorderUse());
                    Assert.Null(log());
                }
                f.AssertPreserved();
            }
        }

        [Fact]
        public void RuntimeRecorderOwnershipAndMiniPlayerStayBoundToTheirActualAudioContexts()
        {
            string player = UxFixSource.Read("Audio", "SoulAudioPlayer.cs");
            string recorder = UxFixSource.Read("Recorder", "SoulRecorderController.cs");
            string recorderAudio = UxFixSource.Read("Recorder", "SoulRecorderAudioPlayer.cs");
            string preserved = UxFixSource.Method(player, "internal void PreserveMainForRecorder", "private bool CancelSavedResume");
            Assert.Contains("_raidPlayback.NoteSoulRecorderUse()", preserved);
            Assert.DoesNotContain("StopCore()", preserved);
            Assert.DoesNotContain("CancelSavedResume(", preserved);
            Assert.DoesNotContain("Plugin.AudioPlayer.Stop", recorder);
            Assert.DoesNotContain("Plugin.AudioPlayer.Next", recorder);
            Assert.DoesNotContain("Plugin.AudioPlayer.Previous", recorder);
            Assert.DoesNotContain("Plugin.AudioPlayer.Play(", recorder);
            Assert.DoesNotContain("CancelPostRaid", recorder);
            Assert.DoesNotContain("Plugin.AudioPlayer", recorderAudio);
            Assert.Contains("gameObject.AddComponent<SoulRecorderAudioPlayer>()", recorder);
            Assert.Contains("gameObject.AddComponent<AudioSource>()", recorderAudio);
            string cleanup = UxFixSource.Method(recorder, "private void ResetRecorder", "private void OnPresentationOwnershipReleaseRequested");
            Assert.Contains("_audioPlayer.Stop()", cleanup);
            Assert.DoesNotContain("Plugin.AudioPlayer", cleanup);
            string controls = UxFixSource.Method(player, "private bool CancelSavedResume", "private void LogRaidLifecycle");
            Assert.True(controls.IndexOf("return false", StringComparison.Ordinal) <
                controls.IndexOf("InvalidateLoad()", StringComparison.Ordinal));
            string capture = UxFixSource.Method(player, "internal void BeginRaidSuspension", "internal bool ObserveRaidResult");
            Assert.True(capture.IndexOf("InvalidateLoad()", StringComparison.Ordinal) <
                capture.IndexOf("CarryMainToNextDeployment()", StringComparison.Ordinal));
            Assert.True(capture.IndexOf("_source.Pause()", StringComparison.Ordinal) <
                capture.IndexOf("CarryMainToNextDeployment()", StringComparison.Ordinal));
            // The mini-player reads the same CurrentTrack used by StartClip,
            // never the capture or the recorder's separately owned current track.
            Assert.Contains("get { return CurrentTrack; }", player);
            Assert.Contains("CurrentTrack = track;", player);
            string start = UxFixSource.Method(player, "private void StartClip", "private void StopPlayback");
            Assert.Contains("TryMarkStarted(CurrentTrack)", start);
            Assert.Contains("_raidPlayback.ResumeStarted(CurrentTrack)", start);
            Assert.Contains("NotifyChanged()", start);
            string mini = UxFixSource.Read("UI", "SoulMiniPlayer.cs");
            Assert.Contains("Plugin.AudioPlayer.DisplayTrack", mini);
            Assert.DoesNotContain("Candidate", mini);
            Assert.DoesNotContain("Captured", mini);
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly TestMusicFiles Files = new TestMusicFiles();
            internal readonly MusicLibrary Library = new MusicLibrary(delegate { }, delegate { });
            internal readonly RaidPlaybackSession Session = new RaidPlaybackSession();
            internal readonly SoulRecorderInteractionLifecycle Recorder = new SoulRecorderInteractionLifecycle();
            internal readonly SoulRecorderRaidNextCoordinator Next = new SoulRecorderRaidNextCoordinator();
            internal readonly TrackRoutingService Routing;
            internal readonly MainPlaybackSnapshot Saved;
            internal IReadOnlyList<MusicTrack> Tracks = new List<MusicTrack>();
            internal RaidMenuEvidence Evidence = new RaidMenuEvidence
                { Screen = "BattleUI", PlayerPresent = true, PlayerAlive = true };
            internal Func<string, bool> Exists = File.Exists;
            internal bool RaidAuto = true;
            internal int MainSelections, OutcomeSelections, RecorderStarts;
            internal MusicTrack Main { get { return Tracks.Single(track => track.Title == "Dopamine"); } }
            internal MusicTrack Other { get { return Tracks.Single(track => track.Title == "Other Main"); } }
            internal MusicTrack Extract { get { return Tracks.Single(track => track.Title == "Extract"); } }
            internal MusicTrack Death { get { return Tracks.Single(track => track.Title == "Death"); } }

            internal Fixture()
            {
                Files.Create("Unquote - Dopamine.mp3", 91);
                Files.Create("Artist - Other Main.mp3", 92);
                Files.Create("Artist - Extract.mp3", 93);
                Files.Create("Artist - Death.mp3", 94);
                Routing = Files.CreateRouting();
                Library.Changed += () => Tracks = Library.Tracks;
                Rescan();
                Routing.SetRoutes(Main, TrackRoute.Main);
                Routing.SetRoutes(Other, TrackRoute.Main);
                Routing.SetRoutes(Extract, TrackRoute.None);
                Routing.SetRoutes(Death, TrackRoute.None);
                Saved = MainPlaybackSnapshot.Capture(Main, Routing, 120.311f, 5774928, 48000,
                    true, false, new[] { Other, Main }, 1, true, 1);
                Assert.True(Session.Begin(Saved));
            }

            internal void EnableOutcomes()
            {
                Routing.SetRoutes(Extract, TrackRoute.Extract);
                Routing.SetRoutes(Death, TrackRoute.Death);
            }

            internal void Rescan()
            {
                Library.BeginScan(new[] { Files.Root });
                ScanResult result;
                Assert.True(SpinWait.SpinUntil(() => Library.TryApplyCompletedScan(out result), TimeSpan.FromSeconds(5)));
            }

            internal void AssertPreserved()
            {
                Assert.Same(Saved, Session.Candidate);
                Assert.Equal(RaidCancellationReason.None, Session.CancellationReason);
                Assert.Equal(120.311f, Saved.Seconds);
                Assert.Equal(5774928, Saved.Samples);
                Assert.Equal(1, Saved.QueueIndex);
                Assert.True(Saved.Shuffle);
                Assert.Equal(1, Saved.RepeatMode);
            }

            internal void StartCassette(MusicTrack track)
            {
                Assert.NotNull(track);
                Assert.True(Recorder.BeginStartInteraction()); // Preparation / insertion.
                Session.NoteSoulRecorderUse(); // Production's only recorder -> Main notification.
                AssertPreserved();
                Recorder.MarkReady();
                Assert.True(Recorder.MarkPlaybackStarted());
                Recorder.CompletePresentationExit();
                Recorder.TakeHandsRestoreRequest();
                RecorderStarts++;
                AssertPreserved();
            }

            internal void StopCassette()
            {
                Assert.True(Recorder.BeginStopInteraction());
                AssertPreserved();
                Recorder.BeginExitAfterStop();
                Recorder.CompletePresentationExit();
                Recorder.TakeHandsRestoreRequest();
                Assert.Equal(SoulRecorderPlaybackState.Stopped, Recorder.PlaybackState);
                AssertPreserved();
            }

            internal void NextCassette(MusicTrack track)
            {
                SoulTapeCatalogEntry tape = new SoulTapeCatalogEntry(TrackRoutingService.GetTrackId(track), track.Artist,
                    track.Title, track.FilePath, null, null, track);
                int selections = 0;
                Func<SoulTapeRaidPlaybackSelection> select = () =>
                {
                    selections++;
                    return new SoulTapeRaidPlaybackSelection(tape, SoulTapeRaidPlaybackEmptyReason.None);
                };
                Assert.True(Next.Request());
                Assert.False(Next.Request());
                SoulRecorderRaidNextDecision eject = Next.Evaluate(SoulRecorderState.Playing, false, select);
                Assert.Equal(SoulRecorderRaidNextAction.BeginEjection, eject.Action);
                AssertPreserved();
                StopCassette();
                SoulRecorderRaidNextDecision insert = Next.Evaluate(SoulRecorderState.Idle, false, select);
                Assert.Equal(SoulRecorderRaidNextAction.BeginInsertion, insert.Action);
                Assert.Same(tape, insert.Tape);
                Assert.Equal(1, selections);
                StartCassette(insert.Tape.Track);
            }

            internal void UseMultipleCassettes()
            {
                StartCassette(Other);
                NextCassette(Extract);
                NextCassette(Death);
                StopCassette();
                StartCassette(Other);
                AssertPreserved();
            }

            internal void EndRaid(ExitStatus outcome)
            {
                Assert.True(Session.RecordOutcome(outcome));
                Recorder.ForceCleanup(); // Production result handler cleans recorder audio only.
                Next.Reset();
                Assert.Equal(SoulRecorderPlaybackState.Stopped, Recorder.PlaybackState);
                AssertPreserved();
            }

            internal void AssertLoadingSilent()
            {
                for (int i = 0; i < 200; i++)
                {
                    Assert.Equal(RaidPlaybackActionKind.None, Step().Kind);
                    Assert.False(Session.CanStart(PlaybackIntent.AutomaticMain));
                }
                Assert.Equal(0, MainSelections);
                Assert.Equal(0, OutcomeSelections);
                AssertPreserved();
            }

            internal void MenuReady()
            {
                Evidence = new RaidMenuEvidence { Screen = "ExitStatus", ReturnScreenShown = true,
                    RecognizedScreen = true, ScreenActive = true, Preloader = false,
                    BlackOverlay = false, ResultModel = "Unavailable" };
            }

            internal RaidPlaybackAction Step()
            {
                Session.SetMenuReady(Evidence.Evaluate(Session.Outcome.HasValue, true) == RaidReadinessReason.Ready);
                if (Evidence.Evaluate(Session.Outcome.HasValue, Library.IsReady) != RaidReadinessReason.Ready)
                    return new RaidPlaybackAction();
                return Session.TakeNext(RaidAuto, Tracks, Routing, true,
                    count => { MainSelections++; return count - 1; },
                    count => { OutcomeSelections++; return count - 1; }, Exists);
            }

            internal void AssertExactResume(RaidPlaybackAction resume)
            {
                Assert.Equal(RaidPlaybackActionKind.ResumeExact, resume.Kind);
                Assert.Same(Main, resume.Track);
                Assert.Same(Saved, resume.Snapshot);
                Assert.Equal(120.311f, resume.Snapshot.Seconds);
                Assert.Equal(5774928, resume.Snapshot.ResumeSamples(48000 * 300, 48000));
                Assert.Equal(new[] { Other, Main }, resume.Queue);
                Session.ResumeStarted(resume.Track);
                Assert.Equal(RaidResumeResult.ResumedExact, Session.ResumeResult);
                Assert.False(Session.MainSuspended);
                for (int i = 0; i < 100; i++) Assert.Equal(RaidPlaybackActionKind.None, Step().Kind);
            }

            public void Dispose() { Files.Dispose(); }
        }
    }
}
