using System;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using EFT;
using EFT.UI.Screens;
using SoulPlayer.Audio;
using SoulPlayer.Configuration;
using SoulPlayer.Library;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    [Trait("Validation", "MenuContinuity")]
    public sealed class MenuPlaybackContinuityTests
    {
        [Theory]
        [InlineData(EEftScreenType.MainMenu, EEftScreenType.Inventory)]
        [InlineData(EEftScreenType.Inventory, EEftScreenType.Trader)]
        [InlineData(EEftScreenType.Trader, EEftScreenType.TraderDialog)]
        [InlineData(EEftScreenType.TraderDialog, EEftScreenType.Trader)]
        [InlineData(EEftScreenType.Trader, EEftScreenType.FleaMarket)]
        [InlineData(EEftScreenType.FleaMarket, EEftScreenType.Hideout)]
        [InlineData(EEftScreenType.Hideout, EEftScreenType.Inventory)]
        [InlineData(EEftScreenType.Inventory, EEftScreenType.OtherPlayerProfile)]
        [InlineData(EEftScreenType.OtherPlayerProfile, EEftScreenType.MainMenu)]
        [InlineData(EEftScreenType.Trader, EEftScreenType.Trader)] // Quest tab, no top-level change.
        [InlineData(EEftScreenType.MainMenu, EEftScreenType.MainMenu)] // Messenger overlay.
        [InlineData(EEftScreenType.Hideout, EEftScreenType.HideoutAreaItemsTransfer)]
        [InlineData(EEftScreenType.ProfileEditor, EEftScreenType.NewsHub)]
        public void NavigationKeepsCueAndExactMainSnapshotWithoutReselection(EEftScreenType from, EEftScreenType to)
        {
            using (SessionFixture f = new SessionFixture())
            {
                f.Session.RecordOutcome(ExitStatus.Survived);
                f.Observe(from);
                RaidPlaybackAction cue = f.Step();
                Assert.Equal(RaidPlaybackActionKind.Routed, cue.Kind);
                f.Session.RoutedStarted(cue.Track);
                MainPlaybackSnapshot saved = f.Session.Candidate;
                int revision = f.Session.Revision;
                f.Observe(to, rebuilding: true);
                Assert.True(f.Session.MenuReady); // SoulAudioPlayer does not context-pause.
                Assert.True(f.Session.CanStart(PlaybackIntent.Routed)); // Pending decoder stays valid too.
                Assert.Same(cue.Track, f.Session.RoutedTrack);
                Assert.Same(saved, f.Session.Candidate);
                Assert.Same(f.Main, saved.Track);
                Assert.Equal(102f, saved.Seconds);
                Assert.Equal(4896000, saved.Samples);
                Assert.Equal(revision, f.Session.Revision);
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                Assert.Equal(0, f.MainSelections);
                Assert.Equal(1, f.OutcomeSelections);
            }
        }

        [Fact]
        [Trait("Validation", "ExactPositionResume")]
        public void RepeatedNavigationThenCueEndResumesExactMainAndQueueOnce()
        {
            using (SessionFixture f = new SessionFixture())
            {
                f.Session.RecordOutcome(ExitStatus.Survived);
                f.Observe(EEftScreenType.MainMenu);
                RaidPlaybackAction cue = f.Step();
                f.Session.RoutedStarted(cue.Track);
                var queue = f.Session.Candidate.Queue;
                for (int i = 0; i < 100; i++)
                {
                    f.Observe(EEftScreenType.Trader, rebuilding: true);
                    f.Observe(EEftScreenType.FleaMarket, rebuilding: true);
                    f.Observe(EEftScreenType.Hideout, rebuilding: true);
                    Assert.Same(queue, f.Session.Candidate.Queue);
                    Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                }
                Assert.True(f.Session.EndRouted(cue.Track, false));
                RaidPlaybackAction resume = f.Step();
                Assert.Equal(RaidPlaybackActionKind.ResumeExact, resume.Kind);
                Assert.Same(f.Main, resume.Track);
                Assert.Equal(4896000, resume.Snapshot.ResumeSamples(48000 * 300, 48000));
                Assert.Equal(102f, resume.Snapshot.Seconds);
                Assert.Equal(new[] { f.Other, f.Main }, resume.Queue);
                Assert.Equal(1, resume.Snapshot.QueueIndex);
                Assert.True(resume.Snapshot.Shuffle);
                Assert.Equal(1, resume.Snapshot.RepeatMode);
                f.Session.ResumeStarted(resume.Track);
                Assert.False(f.Session.MainSuspended);
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                Assert.Equal(0, f.MainSelections);
                Assert.Equal(1, f.OutcomeSelections);
            }
        }

        [Fact]
        public void ControllerReplacementRetainsOnlyAnAlreadyVerifiedMenuContext()
        {
            MenuPlaybackContinuity policy = new MenuPlaybackContinuity();
            RaidMenuEvidence gap = new RaidMenuEvidence { ReturnScreenShown = true, MenuControllerTransition = true };
            Assert.Equal(RaidReadinessReason.WaitingForReturnScreen, policy.Evaluate(gap, true, true, true));
            policy.Evaluate(Evidence(EEftScreenType.MainMenu), true, true, true);
            Assert.Equal(RaidReadinessReason.Ready, policy.Evaluate(gap, true, true, true));
            Assert.Equal(RaidReadinessReason.Ready, policy.Evaluate(Evidence(EEftScreenType.Trader), true, true, true));
            policy.Reset();
            Assert.Equal(RaidReadinessReason.WaitingForReturnScreen, policy.Evaluate(gap, true, true, true));
        }

        [Fact]
        public void MissingScreenManagerDoesNotInheritContinuity()
        {
            MenuPlaybackContinuity policy = new MenuPlaybackContinuity();
            policy.Evaluate(Evidence(EEftScreenType.MainMenu), true, true, true);
            Assert.Equal(RaidReadinessReason.WaitingForReturnScreen, policy.Evaluate(
                new RaidMenuEvidence { ReturnScreenShown = true }, true, true, true));
        }

        [Fact]
        public void FreshReturnStillRequiresOutcomeActiveScreenAndClearLoadingOverlays()
        {
            MenuPlaybackContinuity policy = new MenuPlaybackContinuity();
            RaidMenuEvidence evidence = Evidence(EEftScreenType.Trader);
            Assert.Equal(RaidReadinessReason.WaitingForOutcome, policy.Evaluate(evidence, true, false, true));
            evidence.ReturnScreenShown = false;
            Assert.Equal(RaidReadinessReason.WaitingForReturnScreen, policy.Evaluate(evidence, true, true, true));
            evidence.ReturnScreenShown = true;
            evidence.ScreenActive = false;
            Assert.Equal(RaidReadinessReason.WaitingForReturnScreen, policy.Evaluate(evidence, true, true, true));
            evidence.ScreenActive = true;
            evidence.Preloader = true;
            Assert.Equal(RaidReadinessReason.WaitingForPreloader, policy.Evaluate(evidence, true, true, true));
            evidence.Preloader = false;
            evidence.BlackOverlay = true;
            Assert.Equal(RaidReadinessReason.WaitingForBlackOverlay, policy.Evaluate(evidence, true, true, true));
            evidence.BlackOverlay = false;
            Assert.Equal(RaidReadinessReason.Ready, policy.Evaluate(evidence, true, true, true));
        }

        [Theory]
        [InlineData(EEftScreenType.None)]
        [InlineData(EEftScreenType.BattleUI)]
        [InlineData(EEftScreenType.FinalCountdown)]
        [InlineData(EEftScreenType.TimeHasCome)]
        [InlineData(EEftScreenType.MatchMakerAccept)]
        [InlineData(EEftScreenType.TransferItemsInRaid)]
        [InlineData(EEftScreenType.Reconnect)]
        [InlineData((EEftScreenType)9999)]
        public void UnsafeOrUnknownScreenCannotInheritMenuReadiness(EEftScreenType screen)
        {
            MenuPlaybackContinuity policy = new MenuPlaybackContinuity();
            Assert.Equal(RaidReadinessReason.Ready, policy.Evaluate(Evidence(EEftScreenType.MainMenu), true, true, true));
            Assert.False(MenuPlaybackContinuity.IsNormalMenuScreen(screen));
            Assert.Equal(RaidReadinessReason.WaitingForReturnScreen, policy.Evaluate(Evidence(screen), true, true, true));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NewDeploymentClearsContinuityAndPreservesRecorderSnapshot(bool useRecorder)
        {
            using (SessionFixture f = new SessionFixture())
            {
                f.Session.RecordOutcome(ExitStatus.Survived);
                f.Observe(EEftScreenType.MainMenu);
                MainPlaybackSnapshot snapshot = f.Session.Candidate;
                Assert.True(f.Session.CarryMainToNextDeployment());
                f.Policy.Reset(); // Same reset as BeginRaidSuspension.
                f.Observe(EEftScreenType.Inventory);
                Assert.True(f.Session.MainSuspended);
                Assert.False(f.Session.MenuReady);
                Assert.False(f.Session.CanStart(PlaybackIntent.AutomaticMain));
                if (useRecorder) Assert.True(f.Session.NoteSoulRecorderUse());
                Assert.Same(snapshot, f.Session.Candidate);
                f.Session.RecordOutcome(ExitStatus.Killed);
                f.Observe(EEftScreenType.MainMenu, rebuilding: true);
                Assert.False(f.Session.MenuReady);
                Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
            }
        }

        [Fact]
        public void HideoutPlayerIsExcludedOnlyWhenEnabledButRealRaidPlayerAlwaysBlocks()
        {
            Assert.True(typeof(LocalPlayer).IsAssignableFrom(typeof(HideoutPlayer)));
            Assert.False(MenuPlaybackContinuity.IsBlockingPlayer(true, true, true));
            Assert.True(MenuPlaybackContinuity.IsBlockingPlayer(true, true, false));
            Assert.True(MenuPlaybackContinuity.IsBlockingPlayer(true, false, true));
            MenuPlaybackContinuity policy = new MenuPlaybackContinuity();
            RaidMenuEvidence evidence = Evidence(EEftScreenType.Inventory);
            policy.Evaluate(evidence, true, true, true);
            evidence.PlayerPresent = true;
            Assert.Equal(RaidReadinessReason.WaitingForRaidPlayerExit, policy.Evaluate(evidence, true, true, true));
        }

        [Fact]
        public void ResultLoadingAndLibraryRescanKeepTheirOriginalGates()
        {
            MenuPlaybackContinuity policy = new MenuPlaybackContinuity();
            RaidMenuEvidence main = Evidence(EEftScreenType.MainMenu);
            policy.Evaluate(main, true, true, true);
            Assert.Equal(RaidReadinessReason.WaitingForLibrary, policy.Evaluate(main, true, true, false));
            Assert.Equal(RaidReadinessReason.Ready, policy.Evaluate(main, true, true, true));
            RaidMenuEvidence result = Evidence(EEftScreenType.ExitStatus);
            result.BlackOverlay = true;
            Assert.Equal(RaidReadinessReason.WaitingForBlackOverlay, policy.Evaluate(result, true, true, true));
            result.BlackOverlay = false;
            result.Preloader = null;
            Assert.Equal(RaidReadinessReason.WaitingForPreloader, policy.Evaluate(result, true, true, true));
        }

        [Theory]
        [InlineData("Stop")]
        [InlineData("ManualLibrarySelection")]
        [InlineData("Next")]
        public void ExplicitControlAfterNavigationCannotBeUndoneByMoreMenuEvents(string reasonName)
        {
            RaidCancellationReason reason = (RaidCancellationReason)Enum.Parse(typeof(RaidCancellationReason), reasonName);
            using (SessionFixture f = new SessionFixture())
            {
                f.Session.RecordOutcome(ExitStatus.Survived);
                f.Observe(EEftScreenType.MainMenu);
                f.Observe(EEftScreenType.Trader, rebuilding: true);
                Assert.True(f.Session.CancelSavedResume(reason));
                for (int i = 0; i < 10; i++)
                {
                    f.Observe(EEftScreenType.FleaMarket);
                    Assert.Equal(RaidPlaybackActionKind.None, f.Step().Kind);
                    Assert.Null(f.Session.Candidate);
                }
                Assert.Equal(0, f.MainSelections);
                Assert.Equal(0, f.OutcomeSelections);
                string stop = UxFixSource.Method(UxFixSource.Read("Audio", "SoulAudioPlayer.cs"), "private void StopCore", "internal void Next()");
                Assert.Contains("_source.Stop()", stop);
                Assert.Contains("_stopped = true", stop);
                Assert.Contains("InvalidateLoad()", stop);
            }
        }

        [Fact]
        public void DisablingSettingRestoresLegacyClassificationAndDropsContinuityLatch()
        {
            MenuPlaybackContinuity policy = new MenuPlaybackContinuity();
            policy.Evaluate(Evidence(EEftScreenType.MainMenu), true, true, true);
            RaidMenuEvidence trader = Evidence(EEftScreenType.Trader, false);
            Assert.Equal(RaidReadinessReason.WaitingForReturnScreen, policy.Evaluate(trader, false, true, true));
            foreach (EEftScreenType screen in Enum.GetValues(typeof(EEftScreenType)))
                Assert.Equal(StableRaidMenuContext.IsReturnScreen(screen), StableRaidMenuContext.IsReturnScreen(screen, false));
            RaidMenuEvidence rebuilding = Evidence(EEftScreenType.MainMenu);
            rebuilding.Preloader = true;
            Assert.Equal(RaidReadinessReason.WaitingForPreloader, policy.Evaluate(rebuilding, true, true, true));
        }

        [Fact]
        public void F12DefaultsEnabledAndPublishesOnlyRealSettingChanges()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                ConfigFile config = new ConfigFile(Path.Combine(files.Root, "continuity.cfg"), false);
                SoulPlayerSettings settings = new SoulPlayerSettings(config);
                Assert.True(settings.KeepMusicPlayingAcrossMenus);
                int changes = 0;
                settings.MenuContinuityChanged += () => changes++;
                ConfigEntryBase entry = config[new ConfigDefinition("Player", "Keep music playing across menus")];
                entry.BoxedValue = false;
                entry.BoxedValue = false;
                Assert.False(settings.KeepMusicPlayingAcrossMenus);
                Assert.Equal(1, changes);
                entry.BoxedValue = true;
                Assert.True(settings.KeepMusicPlayingAcrossMenus);
                Assert.Equal(2, changes);
            }
        }

        [Fact]
        public void PersistentAudioAndMiniPlayerHaveNoMenuOwnedPlaybackMutations()
        {
            string plugin = UxFixSource.Read("Plugin.cs");
            Assert.Contains("DontDestroyOnLoad(gameObject)", plugin);
            Assert.Contains("AudioPlayer = gameObject.AddComponent<SoulAudioPlayer>()", plugin);
            string audio = UxFixSource.Read("Audio", "SoulAudioPlayer.cs");
            Assert.Contains("_source = gameObject.AddComponent<AudioSource>()", audio);
            Assert.Contains("_source.ignoreListenerPause = true", audio);
            Assert.Contains("get { return CurrentTrack; }", audio);
            string coordinator = UxFixSource.Read("Audio", "PostRaidCoordinator.cs");
            string events = UxFixSource.Method(coordinator, "private void OnScreenChanged", "private void LateUpdate");
            foreach (string operation in new[] { "Stop()", "TogglePause", "BeginLoad", "PlayCore", "BeginScan", "AddComponent", "CurrentTrack =" })
                Assert.DoesNotContain(operation, events);
            Assert.Contains("BindScreens()", UxFixSource.Method(coordinator, "internal void MenuScreenShown", "internal void Queue"));
            string mini = UxFixSource.Read("UI", "SoulMiniPlayer.cs");
            Assert.Contains("Plugin.AudioPlayer.DisplayTrack", mini);
            Assert.Contains("Plugin.AudioPlayer.CurrentTime", mini);
            Assert.Contains("Plugin.AudioPlayer.Changed += OnAudioChanged", mini);
            Assert.Contains("_menuContinuity.Reset()", UxFixSource.Method(audio, "internal void BeginRaidSuspension", "internal bool ObserveRaidResult"));
            Assert.Contains("MenuContinuityChanged -= OnMenuContinuityChanged", audio);
        }

        [Fact]
        public void TushonkaSuppressionRemainsIndependentOfMenuSettingAndScreenLifetime()
        {
            string patches = UxFixSource.Read("Patches", "TarkovMusicVolumePatch.cs");
            Assert.Contains("__result = 0", patches);
            Assert.Contains("value = 0", patches);
            Assert.DoesNotContain("KeepMusicPlayingAcrossMenus", patches);
            string muter = UxFixSource.Read("Audio", "TarkovMusicMuter.cs");
            Assert.Contains("mixer.SetFloat(parameter, -80f)", muter);
            Assert.Contains("private void OnDestroy()", muter);
            Assert.DoesNotContain("OnDisable", muter);
            Assert.DoesNotContain("RestoreTarkovMusic", UxFixSource.Read("Audio", "PostRaidCoordinator.cs"));
        }

        [Fact]
        [Trait("Validation", "Performance")]
        public void WarmMenuReadinessHasZeroAllocationsAndNoNewPerFrameSearchesOrLogs()
        {
            MenuPlaybackContinuity policy = new MenuPlaybackContinuity();
            RaidMenuEvidence evidence = Evidence(EEftScreenType.Trader);
            for (int i = 0; i < 1000; i++) policy.Evaluate(evidence, true, true, true);
            MethodInfo method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static);
            Func<long> allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
            long before = allocated();
            for (int i = 0; i < 100000; i++) policy.Evaluate(evidence, true, true, true);
            long bytes = allocated() - before;
            Assert.Equal(0, bytes);
            string audio = UxFixSource.Read("Audio", "SoulAudioPlayer.cs");
            string update = UxFixSource.Method(audio, "private void Update()", "private bool StartFromLibrary");
            Assert.DoesNotContain("LogMenuTransition", update);
            string policySource = UxFixSource.Read("Audio", "MenuPlaybackContinuity.cs");
            foreach (string operation in new[] { "FindObject", "GetValue(", "ToList(", "LogInfo", "AudioSource", "Random" })
                Assert.DoesNotContain(operation, policySource.Substring(policySource.IndexOf("internal sealed class", StringComparison.Ordinal)));
        }

        private static RaidMenuEvidence Evidence(EEftScreenType screen, bool enabled = true)
        {
            return new RaidMenuEvidence { Screen = screen.ToString(), ReturnScreenShown = true,
                NormalMenuScreen = MenuPlaybackContinuity.IsNormalMenuScreen(screen),
                RecognizedScreen = StableRaidMenuContext.IsReturnScreen(screen, enabled),
                ScreenActive = true, Preloader = false, BlackOverlay = false };
        }

        private sealed class SessionFixture : IDisposable
        {
            private readonly TestMusicFiles _files = new TestMusicFiles();
            internal readonly MenuPlaybackContinuity Policy = new MenuPlaybackContinuity();
            internal readonly RaidPlaybackSession Session = new RaidPlaybackSession();
            internal readonly MusicTrack Main, Other, Extract;
            internal readonly TrackRoutingService Routing;
            internal int MainSelections, OutcomeSelections;

            internal SessionFixture()
            {
                Main = _files.Create("A - Main.mp3", 231);
                Other = _files.Create("B - Other.mp3", 232);
                Extract = _files.Create("C - Extract.mp3", 233);
                Routing = _files.CreateRouting();
                Routing.SetRoutes(Main, TrackRoute.Main);
                Routing.SetRoutes(Other, TrackRoute.Main);
                Routing.SetRoutes(Extract, TrackRoute.Extract);
                Session.Begin(MainPlaybackSnapshot.Capture(Main, Routing, 102f, 4896000, 48000,
                    true, false, new[] { Other, Main }, 1, true, 1));
            }

            internal void Observe(EEftScreenType screen, bool rebuilding = false)
            {
                RaidMenuEvidence evidence = Evidence(screen);
                if (rebuilding) { evidence.ScreenActive = false; evidence.Preloader = true; evidence.BlackOverlay = true; }
                Session.SetMenuReady(Policy.Evaluate(evidence, true, Session.Outcome.HasValue, true) == RaidReadinessReason.Ready);
            }

            internal RaidPlaybackAction Step()
            {
                return Session.TakeNext(true, new[] { Main, Other, Extract }, Routing, true,
                    count => { MainSelections++; return 0; }, count => { OutcomeSelections++; return 0; });
            }

            public void Dispose() { _files.Dispose(); }
        }
    }
}
