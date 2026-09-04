using System;
using System.Reflection;
using EFT;
using EFT.UI.Screens;
using SoulPlayer.Audio;
using SoulPlayer.Library;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    [Trait("Validation", "MenuContinuity")]
    [Trait("Validation", "PostRaidLifecycle")]
    public sealed class PostRaidContinuityTests
    {
        [Theory]
        [InlineData("inactive")]
        [InlineData("screen-loss")]
        [InlineData("controller-loss")]
        [InlineData("manager-loss")]
        [InlineData("profile-player-loss")]
        [InlineData("preloader")]
        [InlineData("black-overlay")]
        public void StartedCueSurvivesEvidenceLossWithoutGrantingFreshStart(string loss)
        {
            using (Fixture f = new Fixture())
            {
                f.StartCue();
                MainPlaybackSnapshot saved = f.Session.Candidate;
                RaidMenuEvidence gap = Evidence(EEftScreenType.SessionExperience);
                gap.ScreenActive = false;
                if (loss == "screen-loss") gap.RecognizedScreen = false;
                if (loss == "controller-loss") gap.MenuControllerTransition = true;
                if (loss == "manager-loss") gap = new RaidMenuEvidence();
                if (loss == "profile-player-loss") { gap.PlayerPresent = false; gap.ResultModel = null; }
                if (loss == "preloader") { gap.ScreenActive = true; gap.Preloader = true; }
                if (loss == "black-overlay") { gap.ScreenActive = true; gap.BlackOverlay = null; }
                f.Observe(gap);
                Assert.False(f.Session.MenuReady);
                Assert.True(f.Continue);
                Assert.True(f.Overlay);
                Assert.False(f.Session.CanStart(PlaybackIntent.Routed));
                Assert.Same(f.Cue, f.Session.RoutedTrack);
                Assert.Same(saved, f.Session.Candidate);
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
            }
        }

        [Theory]
        [InlineData(ExitStatus.Survived)]
        [InlineData(ExitStatus.Killed)]
        public void RapidOkNextModeledTransportStaysMonotonicAndMainArrivalDoesNotRedispatch(ExitStatus outcome)
        {
            using (Fixture f = new Fixture(outcome))
            {
                f.StartCue();
                int samples = 96000;
                foreach (EEftScreenType screen in new[] { EEftScreenType.ExitStatus, EEftScreenType.KillList,
                    EEftScreenType.SessionStatistics, EEftScreenType.SessionExperience, EEftScreenType.HealthTreatment,
                    EEftScreenType.ScavInventory, EEftScreenType.MainMenu })
                {
                    for (int i = 0; i < 3; i++)
                    {
                        RaidMenuEvidence gap = Evidence(screen);
                        gap.ScreenActive = false;
                        f.Observe(gap);
                        int previous = samples;
                        // Offline transport model; native timeSamples needs EFT acceptance.
                        if (f.Continue) samples += 4800;
                        Assert.True(samples > previous);
                        Assert.True(f.Overlay);
                        Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                        Assert.Same(f.Cue, f.Session.RoutedTrack);
                    }
                    f.Observe(Evidence(screen));
                }
                Assert.Equal(1, f.Selections);
                Assert.Equal(4896000, f.Session.Candidate.Samples);
            }
        }

        [Fact]
        [Trait("Validation", "ExactPositionResume")]
        public void CueEofDuringTeardownWaitsToStartExactMainButNotForMainMenu()
        {
            using (Fixture f = new Fixture())
            {
                f.StartCue();
                f.Observe(new RaidMenuEvidence());
                PlaybackCompletionTracker completion = new PlaybackCompletionTracker();
                completion.Started(true);
                Assert.True(f.Continue);
                Assert.True(completion.Poll(true, false, false, false, false, false));
                Assert.True(f.Session.EndRouted(f.Cue, false));
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                Assert.True(f.Overlay);
                f.Observe(Evidence(EEftScreenType.HealthTreatment));
                RaidPlaybackAction resume = f.Step();
                Assert.Equal(RaidPlaybackActionKind.ResumeExact, resume.Kind);
                Assert.Same(f.Main, resume.Track);
                Assert.Equal(4896000, resume.Snapshot.ResumeSamples(14400000, 48000));
                f.Policy.PlaybackStarted(true, true);
                f.Session.ResumeStarted(resume.Track);
                f.Observe(new RaidMenuEvidence());
                Assert.False(f.Session.MainSuspended);
                Assert.True(f.Continue);
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                f.Observe(Evidence(EEftScreenType.MainMenu));
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                Assert.Equal(1, f.Selections);
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public void NoContinuationProofFromReadinessOrUnstartedOrNonRaidAudio(bool postRaid, bool playing)
        {
            PostRaidPlaybackContinuity policy = new PostRaidPlaybackContinuity();
            Assert.True(policy.CanDisplay(true, true, true));
            policy.PlaybackStarted(postRaid, playing);
            Assert.False(policy.CanContinue(true, true));
            Assert.False(policy.CanDisplay(true, true, false));
        }

        [Fact]
        public void NewDeploymentResetsProofAndRetainsRecorderMainSnapshot()
        {
            using (Fixture f = new Fixture())
            {
                f.StartCue();
                MainPlaybackSnapshot saved = f.Session.Candidate;
                f.Policy.Reset();
                Assert.True(f.Session.CarryMainToNextDeployment());
                Assert.True(f.Session.NoteSoulRecorderUse());
                Assert.False(f.Continue);
                Assert.False(f.Overlay);
                Assert.Same(saved, f.Session.Candidate);
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                f.Session.RecordOutcome(ExitStatus.Killed);
                f.Observe(new RaidMenuEvidence());
                Assert.False(f.Continue);
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
            }
        }

        [Fact]
        public void DisabledSettingRestoresLegacyAndReenableNeverOverridesUserPause()
        {
            using (Fixture f = new Fixture())
            {
                f.StartCue();
                f.Observe(new RaidMenuEvidence());
                f.Enabled = false;
                Assert.False(f.Continue);
                Assert.False(f.Overlay);
                f.Enabled = true;
                Assert.True(f.Continue);
                PlaybackCompletionTracker completion = new PlaybackCompletionTracker();
                completion.Started(true);
                Assert.False(completion.Poll(true, false, false, true, false, false));
                Assert.False(completion.Poll(true, false, false, false, true, false));
            }
        }

        [Fact]
        public void ExplicitStopDuringTeardownCannotBeResurrectedByMainArrival()
        {
            using (Fixture f = new Fixture())
            {
                f.StartCue();
                f.Observe(new RaidMenuEvidence());
                Assert.True(f.Session.CancelSavedResume(RaidCancellationReason.Stop));
                Assert.Null(f.Session.Candidate);
                PlaybackCompletionTracker completion = new PlaybackCompletionTracker();
                completion.Started(true);
                completion.Reset();
                Assert.False(completion.Poll(true, false, false, false, false, false));
                f.Observe(Evidence(EEftScreenType.MainMenu));
                Assert.Equal(RaidPlaybackActionKind.Finish, f.Step().Kind); // Completes cancellation, never plays.
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                Assert.Equal(1, f.Selections);
            }
        }

        [Fact]
        public void RuntimeWiringKeepsStrictStartAndExplicitControlsAndClipValidity()
        {
            string audio = UxFixSource.Read("Audio", "SoulAudioPlayer.cs");
            string start = UxFixSource.Method(audio, "private void StartClip", "private void StopPlayback");
            Assert.True(start.IndexOf("if (!CanStart(_loadingIntent))", StringComparison.Ordinal) <
                start.IndexOf("_postRaidContinuity.PlaybackStarted", StringComparison.Ordinal));
            Assert.True(start.IndexOf("_source.Play()", StringComparison.Ordinal) <
                start.IndexOf("_postRaidContinuity.PlaybackStarted", StringComparison.Ordinal));
            string gate = UxFixSource.Method(audio, "internal void SetRaidMenuReady", "private void ProcessRaidPlayback");
            foreach (string required in new[] { "_raidPlayback.SetMenuReady(ready)", "!_paused", "!_stopped", "_hasStarted", "_source.clip != null", "_source.UnPause()" })
                Assert.Contains(required, gate);
            foreach (string forbidden in new[] { "_source.Stop()", "_source.Play()", "timeSamples", "BeginLoad" })
                Assert.DoesNotContain(forbidden, gate);
            string toggle = UxFixSource.Method(audio, "internal void TogglePause", "internal void Stop()");
            Assert.Contains("_hasStarted && RaidAudioMayContinue", toggle);
            Assert.Contains("_source.Pause()", toggle);
            Assert.Contains("_paused = true", toggle);
            Assert.Contains("(RaidAudioMayContinue && _raidPlayback.Phase == RaidPlaybackPhase.Routed)", audio);
            string deploy = UxFixSource.Method(audio, "internal void BeginRaidSuspension", "internal bool ObserveRaidResult");
            Assert.Contains("_postRaidContinuity.Reset()", deploy);
            Assert.Contains("_source.Pause()", deploy);
        }

        [Fact]
        public void OverlayPersistsWithLiveBindingAndNoProfileOrMenuScreenOwnership()
        {
            string host = UxFixSource.Read("UI", "SoulPlayerOverlayHost.cs");
            Assert.Contains("DontDestroyOnLoad(root)", host);
            Assert.Contains("return Instance", host);
            Assert.Contains("SoulMiniPlayer.SetRaidMode(inRaid)", host);
            Assert.Contains("_canvas.enabled = !inRaid", host);
            string mini = UxFixSource.Read("UI", "SoulMiniPlayer.cs");
            foreach (string required in new[] { "Plugin.AudioPlayer.DisplayTrack", "Plugin.AudioPlayer.CurrentTime", "Plugin.AudioPlayer.IsPlaying", "Plugin.AudioPlayer.Changed += OnAudioChanged" })
                Assert.Contains(required, mini);
            string policy = UxFixSource.Read("Audio", "MenuPlaybackContinuity.cs");
            policy = policy.Substring(policy.IndexOf("internal sealed class PostRaidPlaybackContinuity", StringComparison.Ordinal));
            Assert.DoesNotContain("Profile", policy);
            Assert.DoesNotContain("MenuScreen", policy);
        }

        [Fact]
        [Trait("Validation", "Performance")]
        public void WarmContinuationAndOverlayDecisionsAllocateNothingAndAddNoPolling()
        {
            PostRaidPlaybackContinuity policy = new PostRaidPlaybackContinuity();
            policy.PlaybackStarted(true, true);
            for (int i = 0; i < 1000; i++) { policy.CanContinue(true, true); policy.CanDisplay(true, true, false); }
            MethodInfo method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static);
            Func<long> allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
            long before = allocated();
            for (int i = 0; i < 100000; i++) { policy.CanContinue(true, true); policy.CanDisplay(true, true, false); }
            Assert.Equal(0, allocated() - before);
            string source = UxFixSource.Read("Audio", "MenuPlaybackContinuity.cs");
            foreach (string forbidden in new[] { "Update(", "FindObject", "Reflection", "ToList(", "LogInfo", "Coroutine" })
                Assert.DoesNotContain(forbidden, source);
            string coordinator = UxFixSource.Read("Audio", "PostRaidCoordinator.cs");
            string late = coordinator.Substring(coordinator.IndexOf("private void LateUpdate", StringComparison.Ordinal));
            Assert.True(late.IndexOf("NeedsRaidReadinessInspection", StringComparison.Ordinal) < late.IndexOf("EftScreenManager.Instance", StringComparison.Ordinal));
        }

        [Fact]
        public void LosingStartReadinessMustNotDirectlyPauseAlreadyStartedAudio()
        {
            string audio = UxFixSource.Read("Audio", "SoulAudioPlayer.cs");
            string readiness = UxFixSource.Method(audio, "internal void SetRaidMenuReady", "private void ProcessRaidPlayback");
            Assert.DoesNotContain("if (!ready && _source.isPlaying)", readiness);
            Assert.Contains("!RaidAudioMayContinue", readiness);
        }

        [Fact]
        public void OverlayVisibilityMustUseItsOwnPermissionNotTheStartGate()
        {
            string state = UxFixSource.Method(UxFixSource.Read("Utils", "GameState.cs"),
                "internal static bool ShouldSuspendMenuMusic", "internal static bool HasLiveRaidPlayer()");
            Assert.DoesNotContain("return !Plugin.AudioPlayer.RaidMenuReady", state);
            Assert.Contains("RaidOverlayAllowed", state);
        }

        private static RaidMenuEvidence Evidence(EEftScreenType screen)
        {
            return new RaidMenuEvidence { Screen = screen.ToString(), ReturnScreenShown = true,
                RecognizedScreen = StableRaidMenuContext.IsReturnScreen(screen, true),
                NormalMenuScreen = MenuPlaybackContinuity.IsNormalMenuScreen(screen),
                ScreenActive = true, Preloader = false, BlackOverlay = false };
        }

        private sealed class Fixture : IDisposable
        {
            private readonly TestMusicFiles _files = new TestMusicFiles();
            private readonly MenuPlaybackContinuity _start = new MenuPlaybackContinuity();
            internal readonly PostRaidPlaybackContinuity Policy = new PostRaidPlaybackContinuity();
            internal readonly RaidPlaybackSession Session = new RaidPlaybackSession();
            internal readonly MusicTrack Main, Cue;
            private readonly TrackRoutingService _routing;
            internal bool Enabled = true;
            internal int Selections;
            internal bool Continue { get { return Session.MenuReady || Policy.CanContinue(Enabled, Session.Outcome.HasValue); } }
            internal bool Overlay { get { return Policy.CanDisplay(Enabled, Session.Outcome.HasValue, Session.MenuReady); } }

            internal Fixture(ExitStatus outcome = ExitStatus.Survived)
            {
                Main = _files.Create("A - Main.mp3", 201);
                Cue = _files.Create("B - Outcome.mp3", 202);
                _routing = _files.CreateRouting();
                _routing.SetRoutes(Main, TrackRoute.Main);
                _routing.SetRoutes(Cue, TrackRoute.Extract | TrackRoute.Death);
                Session.Begin(MainPlaybackSnapshot.Capture(Main, _routing, 102f, 4896000, 48000,
                    true, false, new[] { Main }, 0, true, 1));
                Session.RecordOutcome(outcome);
            }

            internal void StartCue()
            {
                Observe(Evidence(EEftScreenType.SessionStatistics));
                RaidPlaybackAction cue = Step();
                Assert.Equal(RaidPlaybackActionKind.Routed, cue.Kind);
                Session.RoutedStarted(cue.Track);
                Policy.PlaybackStarted(true, true);
            }

            internal void Observe(RaidMenuEvidence evidence)
            {
                Session.SetMenuReady(_start.Evaluate(evidence, Enabled, Session.Outcome.HasValue, true) == RaidReadinessReason.Ready);
            }

            internal RaidPlaybackAction Step()
            {
                return Session.TakeNext(true, new[] { Main, Cue }, _routing, false, count => 0,
                    count => { Selections++; return 0; });
            }

            public void Dispose() { _files.Dispose(); }
        }
    }
}
