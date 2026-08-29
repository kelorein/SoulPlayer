using System;
using System.IO;
using BepInEx.Configuration;
using EFT;
using SoulPlayer.Audio;
using SoulPlayer.Configuration;
using SoulPlayer.Library;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class PostRaidRoutedAutoplayTests
    {
        [Fact]
        [Trait("Validation", "PostRaidLifecycle")]
        public void SurvivedRaidWithExtractOnlyTrackStartsAutoplay()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack track = RoutedTrack(files, TrackRoute.Extract, 41);

                PostRaidAutoplayPlan plan = Resolve(
                    files, ExitStatus.Survived, true, track);

                Assert.True(plan.ShouldStart);
                Assert.Equal(TrackRoute.Extract, plan.Route);
                Assert.Same(track, plan.SelectedTrack);
            }
        }

        [Fact]
        [Trait("Validation", "PostRaidLifecycle")]
        public void DiedRaidWithDeathOnlyTrackStartsAutoplay()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack track = RoutedTrack(files, TrackRoute.Death, 42);

                PostRaidAutoplayPlan plan = Resolve(
                    files, ExitStatus.Killed, true, track);

                Assert.True(plan.ShouldStart);
                Assert.Equal(TrackRoute.Death, plan.Route);
                Assert.Same(track, plan.SelectedTrack);
            }
        }

        [Theory]
        [InlineData(2, ExitStatus.Survived)]
        [InlineData(4, ExitStatus.Killed)]
        [Trait("Validation", "PostRaidRouting")]
        public void MainFalseDoesNotBlockOutcomeRoute(
            int routeValue,
            ExitStatus outcome)
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                TrackRoute onlyRoute = (TrackRoute)routeValue;
                MusicTrack track = RoutedTrack(files, onlyRoute, 43);

                PostRaidAutoplayPlan plan = Resolve(files, outcome, true, track);

                Assert.True(plan.ShouldStart);
                Assert.False(files.CreateRouting().IsEligible(track, TrackRoute.Main));
            }
        }

        [Fact]
        [Trait("Validation", "PostRaidRouting")]
        public void WrongRouteIsNeverSelected()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack deathOnly = RoutedTrack(files, TrackRoute.Death, 44);
                MusicTrack extractOnly = RoutedTrack(files, TrackRoute.Extract, 45);
                TrackRoutingService routing = files.CreateRouting();

                PostRaidAutoplayPlan survived = PostRaidAutoplayPlan.Resolve(
                    ExitStatus.Survived,
                    true,
                    new[] { deathOnly, extractOnly },
                    routing,
                    delegate { return 0; });
                PostRaidAutoplayPlan killed = PostRaidAutoplayPlan.Resolve(
                    ExitStatus.Killed,
                    true,
                    new[] { deathOnly, extractOnly },
                    routing,
                    delegate { return 0; });

                Assert.Same(extractOnly, survived.SelectedTrack);
                Assert.Same(deathOnly, killed.SelectedTrack);
            }
        }

        [Fact]
        [Trait("Validation", "PostRaidLifecycle")]
        public void PostRaidCleanupSettlesBeforeOneShotPlaybackRequest()
        {
            PostRaidSignalGate gate = new PostRaidSignalGate(0.75f, 12f);
            ExitStatus outcome;
            int signals;

            Assert.True(gate.Queue(ExitStatus.Survived, 10f));
            Assert.False(gate.TryTake(10.74f, out outcome, out signals));
            Assert.True(gate.TryTake(10.75f, out outcome, out signals));
            Assert.Equal(ExitStatus.Survived, outcome);
            Assert.Equal(1, signals);
            Assert.False(gate.TryTake(11f, out outcome, out signals));
        }

        [Fact]
        [Trait("Validation", "PostRaidLifecycle")]
        public void MenuTransitionProtectionRemainsAheadOfPlayback()
        {
            string coordinator = ReadSource("Audio", "PostRaidCoordinator.cs");
            int update = coordinator.IndexOf("private void Update()", StringComparison.Ordinal);
            int suppression = coordinator.IndexOf(
                "GameState.SuppressRaidMusicSuspend(RaidStateGraceSeconds)",
                update,
                StringComparison.Ordinal);
            int playback = coordinator.IndexOf(
                "Plugin.AudioPlayer.PlayPostRaid(outcome)",
                update,
                StringComparison.Ordinal);

            Assert.True(update >= 0);
            Assert.True(suppression > update);
            Assert.True(playback > suppression);
        }

        [Fact]
        [Trait("Validation", "PostRaidRouting")]
        public void AutoplaySettingOnAllowsRoutedPlayback()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack track = RoutedTrack(files, TrackRoute.Extract, 46);

                Assert.True(Resolve(files, ExitStatus.Survived, true, track).ShouldStart);
            }
        }

        [Fact]
        [Trait("Validation", "PostRaidRouting")]
        public void AutoplaySettingOffSuppressesRoutedPlaybackWithoutChangingEligibility()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack track = RoutedTrack(files, TrackRoute.Extract, 47);

                PostRaidAutoplayPlan plan = Resolve(
                    files, ExitStatus.Survived, false, track);

                Assert.False(plan.ShouldStart);
                Assert.Null(plan.SelectedTrack);
                Assert.Single(plan.EligibleTracks);
            }
        }

        [Fact]
        [Trait("Validation", "PostRaidRouting")]
        public void EmptyRoutePoolSkipsGracefully()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack mainOnly = RoutedTrack(files, TrackRoute.Main, 48);

                PostRaidAutoplayPlan plan = Resolve(
                    files, ExitStatus.Survived, true, mainOnly);

                Assert.False(plan.ShouldStart);
                Assert.Empty(plan.EligibleTracks);
                Assert.Null(plan.SelectedTrack);
            }
        }

        [Fact]
        [Trait("Validation", "PostRaidLifecycle")]
        public void DuplicatePostRaidSignalsProduceOnlyOnePlaybackRequest()
        {
            PostRaidSignalGate gate = new PostRaidSignalGate(0.75f, 12f);
            ExitStatus outcome;
            int signals;

            Assert.True(gate.Queue(ExitStatus.Survived, 20f));
            Assert.True(gate.Queue(ExitStatus.Survived, 20.1f));
            Assert.True(gate.TryTake(20.85f, out outcome, out signals));
            Assert.Equal(2, signals);
            Assert.False(gate.Queue(ExitStatus.Survived, 21f));
            Assert.False(gate.TryTake(21f, out outcome, out signals));
        }

        [Fact]
        [Trait("Validation", "PostRaidRouting")]
        public void RoutingPersistsAcrossRestartAndStillAutoplays()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack track = files.Create("Artist - Persisted Extract.mp3", 49);
                files.CreateRouting().SetRoutes(track, TrackRoute.Extract);

                PostRaidAutoplayPlan plan = PostRaidAutoplayPlan.Resolve(
                    ExitStatus.Survived,
                    true,
                    new[] { track },
                    files.CreateRouting(),
                    delegate { return 0; });

                Assert.True(plan.ShouldStart);
                Assert.Same(track, plan.SelectedTrack);
            }
        }

        [Fact]
        [Trait("Validation", "PlaybackSelection")]
        public void SurvivedExactTrackRequestStartsBAndNeverCurrentMainAOrC()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack currentMainA = files.Create("A - Current Main.mp3", 51);
                MusicTrack extractOnlyB = files.Create("B - Extract Only.mp3", 52);
                MusicTrack mainC = files.Create("C - Other Main.mp3", 53);
                TrackRoutingService routing = files.CreateRouting();
                routing.SetRoutes(currentMainA, TrackRoute.Main);
                routing.SetRoutes(extractOnlyB, TrackRoute.Extract);
                routing.SetRoutes(mainC, TrackRoute.Main);

                PostRaidAutoplayPlan plan = PostRaidAutoplayPlan.Resolve(
                    ExitStatus.Survived,
                    true,
                    new[] { currentMainA, extractOnlyB, mainC },
                    routing,
                    delegate { return 0; });
                ExactTrackPlaybackRequest request = new ExactTrackPlaybackRequest(
                    1,
                    plan.Route,
                    plan.SelectedTrack);

                Assert.Same(extractOnlyB, plan.SelectedTrack);
                Assert.False(request.TryMarkStarted(currentMainA));
                Assert.False(request.TryMarkStarted(mainC));
                Assert.True(request.TryMarkStarted(extractOnlyB));
                Assert.True(request.HasStarted);
            }
        }

        [Fact]
        [Trait("Validation", "PlaybackSelection")]
        public void ShuffleEnabledCannotReplaceOnlyExtractTrack()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack main = RoutedTrack(files, TrackRoute.Main, 54);
                MusicTrack extract = RoutedTrack(files, TrackRoute.Extract, 55);
                bool shuffleEnabled = true;

                PostRaidAutoplayPlan plan = PostRaidAutoplayPlan.Resolve(
                    ExitStatus.Survived,
                    true,
                    new[] { main, extract },
                    files.CreateRouting(),
                    count => shuffleEnabled ? count - 1 : 0);
                ExactTrackPlaybackRequest request = new ExactTrackPlaybackRequest(
                    2,
                    plan.Route,
                    plan.SelectedTrack);

                Assert.Single(plan.EligibleTracks);
                Assert.Same(extract, plan.SelectedTrack);
                Assert.True(request.TryMarkStarted(extract));
            }
        }

        [Fact]
        [Trait("Validation", "PlaybackSelection")]
        public void MultipleExtractTracksCanOnlyStartSelectedExtractMember()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack main = RoutedTrack(files, TrackRoute.Main, 56);
                MusicTrack firstExtract = RoutedTrack(files, TrackRoute.Extract, 57);
                MusicTrack secondExtract = RoutedTrack(files, TrackRoute.Extract, 58);

                PostRaidAutoplayPlan plan = PostRaidAutoplayPlan.Resolve(
                    ExitStatus.Survived,
                    true,
                    new[] { main, firstExtract, secondExtract },
                    files.CreateRouting(),
                    delegate { return 1; });
                ExactTrackPlaybackRequest request = new ExactTrackPlaybackRequest(
                    3,
                    plan.Route,
                    plan.SelectedTrack);

                Assert.Equal(new[] { firstExtract, secondExtract }, plan.EligibleTracks);
                Assert.Same(secondExtract, plan.SelectedTrack);
                Assert.False(request.TryMarkStarted(firstExtract));
                Assert.False(request.TryMarkStarted(main));
                Assert.True(request.TryMarkStarted(secondExtract));
            }
        }

        [Fact]
        [Trait("Validation", "PlaybackSelection")]
        public void DeathRouteExactRequestRejectsMainAndExtractTracks()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack main = RoutedTrack(files, TrackRoute.Main, 59);
                MusicTrack extract = RoutedTrack(files, TrackRoute.Extract, 60);
                MusicTrack death = RoutedTrack(files, TrackRoute.Death, 61);

                PostRaidAutoplayPlan plan = PostRaidAutoplayPlan.Resolve(
                    ExitStatus.Killed,
                    true,
                    new[] { main, extract, death },
                    files.CreateRouting(),
                    delegate { return 0; });
                ExactTrackPlaybackRequest request = new ExactTrackPlaybackRequest(
                    4,
                    plan.Route,
                    plan.SelectedTrack);

                Assert.Same(death, plan.SelectedTrack);
                Assert.False(request.TryMarkStarted(main));
                Assert.False(request.TryMarkStarted(extract));
                Assert.True(request.TryMarkStarted(death));
            }
        }

        [Fact]
        [Trait("Validation", "PlaybackSelection")]
        public void FadeCompletionUsesExactRequestWithoutSecondSelection()
        {
            string player = ReadSource("Audio", "SoulAudioPlayer.cs");
            int completion = player.IndexOf(
                "if (completion == FadeCompletion.StartPendingTrack)",
                StringComparison.Ordinal);
            int cancel = player.IndexOf(
                "private void CancelFade()",
                completion,
                StringComparison.Ordinal);
            string exactCompletion = player.Substring(
                completion,
                cancel - completion);

            Assert.Contains("PlayExactPostRaid(track, queue, exactRequest)", exactCompletion);
            Assert.DoesNotContain("_random.Next", exactCompletion);
            Assert.DoesNotContain("SelectEligible", exactCompletion);
            Assert.DoesNotContain("PlayAutomatic", exactCompletion);
        }

        [Fact]
        [Trait("Validation", "PlaybackSelection")]
        public void MiniPlayerDisplaysExactAudioPlayerTrackContract()
        {
            string miniPlayer = ReadSource("UI", "SoulMiniPlayer.cs");
            string player = ReadSource("Audio", "SoulAudioPlayer.cs");

            Assert.Contains("Plugin.AudioPlayer.DisplayTrack", miniPlayer);
            Assert.Contains("get { return CurrentTrack; }", player);
            Assert.Contains("actualStartedId=", player);
            Assert.Contains("actualStartedPath=", player);
        }

        [Fact]
        [Trait("Validation", "PostRaidRouting")]
        public void NewInstallsDefaultRaidAutoplayOnAndExplicitChoicePersists()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                string path = Path.Combine(files.Root, "SoulPlayer.cfg");
                SoulPlayerSettings fresh = new SoulPlayerSettings(
                    new ConfigFile(path, false));
                Assert.True(fresh.AutoPlayAfterRaid);

                fresh.AutoPlayAfterRaid = false;
                SoulPlayerSettings restarted = new SoulPlayerSettings(
                    new ConfigFile(path, false));
                Assert.False(restarted.AutoPlayAfterRaid);
            }
        }

        [Fact]
        [Trait("Validation", "LibraryUi")]
        public void LibraryMakesGlobalPostRaidGateExplicit()
        {
            string window = ReadSource("UI", "SoulPlayerWindow.cs");

            Assert.Contains("RAID AUTO ON", window);
            Assert.Contains("RAID AUTO OFF", window);
            Assert.Contains("RAID AUTO must be ON", window);
            Assert.Contains("Extract and Death routes will not start after raids", window);
        }

        private static MusicTrack RoutedTrack(
            TestMusicFiles files,
            TrackRoute routes,
            byte seed)
        {
            MusicTrack track = files.Create("Artist - Routed " + seed + ".mp3", seed);
            files.CreateRouting().SetRoutes(track, routes);
            return track;
        }

        private static PostRaidAutoplayPlan Resolve(
            TestMusicFiles files,
            ExitStatus outcome,
            bool enabled,
            MusicTrack track)
        {
            return PostRaidAutoplayPlan.Resolve(
                outcome,
                enabled,
                new[] { track },
                files.CreateRouting(),
                delegate { return 0; });
        }

        private static string ReadSource(params string[] parts)
        {
            string path = FindRepositoryRoot();
            foreach (string part in parts)
            {
                path = Path.Combine(path, part);
            }

            return File.ReadAllText(path);
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "SoulPlayer.csproj")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("SoulPlayer repository root not found.");
        }
    }
}
