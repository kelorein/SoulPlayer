using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using SoulPlayer.Configuration;
using SoulPlayer.Library;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class TrackRoutingTests
    {
        [Fact]
        [Trait("Validation", "TrackRouting")]
        public void MainFalseExcludesAutomaticMainShuffle()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack track = files.Create("Artist - Main.mp3", 1);
                TrackRoutingService routing = files.CreateRouting();
                routing.SetRoute(track, TrackRoute.Main, false);

                Assert.Empty(routing.SelectEligible(new[] { track }, TrackRoute.Main));
            }
        }

        [Fact]
        [Trait("Validation", "PostRaidRouting")]
        public void ExtractTrueAllowsExtractPlayback()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack track = files.Create("Artist - Extract.mp3", 2);
                Assert.Single(files.CreateRouting().SelectEligible(
                    new[] { track }, TrackRoute.Extract));
            }
        }

        [Fact]
        [Trait("Validation", "PostRaidRouting")]
        public void DeathTrueAllowsDeathPlayback()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack track = files.Create("Artist - Death.mp3", 3);
                Assert.Single(files.CreateRouting().SelectEligible(
                    new[] { track }, TrackRoute.Death));
            }
        }

        [Fact]
        [Trait("Validation", "TrackRouting")]
        public void DeathOnlyNeverEntersMain()
        {
            AssertSingleRouteDoesNotEnterMain(TrackRoute.Death, 4);
        }

        [Fact]
        [Trait("Validation", "TrackRouting")]
        public void ExtractOnlyNeverEntersMain()
        {
            AssertSingleRouteDoesNotEnterMain(TrackRoute.Extract, 5);
        }

        [Fact]
        [Trait("Validation", "TrackRouting")]
        public void MultiRouteMembershipWorks()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack track = files.Create("Artist - Multi.mp3", 6);
                TrackRoutingService routing = files.CreateRouting();
                routing.SetRoutes(track, TrackRoute.Main | TrackRoute.Death);

                Assert.True(routing.IsEligible(track, TrackRoute.Main));
                Assert.True(routing.IsEligible(track, TrackRoute.Death));
                Assert.False(routing.IsEligible(track, TrackRoute.Extract));
            }
        }

        [Fact]
        [Trait("Validation", "TrackRouting")]
        public void ManualLibraryPlaybackIgnoresRoutingRestriction()
        {
            AssertDirectPlaybackIgnoresRouting(7);
        }

        [Fact]
        [Trait("Validation", "TrackRouting")]
        public void SoulRecorderPlaybackIgnoresRoutingRestriction()
        {
            AssertDirectPlaybackIgnoresRouting(8);
        }

        [Fact]
        [Trait("Validation", "TrackRouting")]
        public void RoutingSurvivesRestart()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack track = files.Create("Artist - Restart.mp3", 9);
                TrackRoutingService first = files.CreateRouting();
                first.SetRoutes(track, TrackRoute.Death);

                TrackRoutingService restarted = files.CreateRouting();
                Assert.Equal(TrackRoute.Death, restarted.GetRoutes(track));
            }
        }

        [Fact]
        [Trait("Validation", "TrackRouting")]
        public void RoutingSurvivesRescanAndEnumerationOrderChange()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                byte[] content = TestMusicFiles.Content(10);
                MusicTrack first = files.Create("A/Artist - First.mp3", content);
                TrackRoutingService routing = files.CreateRouting();
                routing.SetRoutes(first, TrackRoute.Extract);

                MusicTrack returned = files.Create("Z/Artist - Returned.mp3", content);
                List<MusicTrack> reordered = new[]
                {
                    files.Create("B/Artist - Other.mp3", 11),
                    returned
                }.Reverse().ToList();

                Assert.Equal(TrackRoutingService.GetTrackId(first),
                    TrackRoutingService.GetTrackId(returned));
                Assert.Single(routing.SelectEligible(reordered, TrackRoute.Extract)
                    .Where(track => track == returned));
                Assert.False(routing.IsEligible(returned, TrackRoute.Main));
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [Trait("Validation", "TrackRouting")]
        public void ZeroEligiblePoolIsGracefulAndNeverFallsBack(int routeValue)
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                TrackRoute route = (TrackRoute)routeValue;
                MusicTrack track = files.Create("Artist - Excluded.mp3", 12);
                TrackRoutingService routing = files.CreateRouting();
                routing.SetRoutes(track, TrackRoute.None);

                List<MusicTrack> selected = routing.SelectEligible(new[] { track }, route);

                Assert.NotNull(selected);
                Assert.Empty(selected);
            }
        }

        [Fact]
        [Trait("Validation", "TrackRouting")]
        public void EditingRoutingDoesNotInterruptCurrentTrack()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack playing = files.Create("Artist - Playing.mp3", 13);
                TrackRoutingService routing = files.CreateRouting();
                MusicTrack currentPlayback = playing;

                routing.SetRoutes(playing, TrackRoute.None);

                Assert.Same(playing, currentPlayback);
                Assert.Equal(TrackRoute.None, routing.GetRoutes(playing));
                Assert.True(TrackRoutingService.AllowsDirectPlayback(currentPlayback));
            }
        }

        private static void AssertSingleRouteDoesNotEnterMain(TrackRoute route, byte seed)
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack track = files.Create("Artist - Routed.mp3", seed);
                TrackRoutingService routing = files.CreateRouting();
                routing.SetRoutes(track, route);

                Assert.True(routing.IsEligible(track, route));
                Assert.False(routing.IsEligible(track, TrackRoute.Main));
                Assert.Empty(routing.SelectEligible(new[] { track }, TrackRoute.Main));
            }
        }

        private static void AssertDirectPlaybackIgnoresRouting(byte seed)
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack track = files.Create("Artist - Direct.mp3", seed);
                TrackRoutingService routing = files.CreateRouting();
                routing.SetRoutes(track, TrackRoute.None);

                Assert.False(routing.IsEligible(track, TrackRoute.Main));
                Assert.True(TrackRoutingService.AllowsDirectPlayback(track));
            }
        }
    }

    public sealed class SoulPathCompatibilityTests
    {
        [Fact]
        [Trait("Validation", "LibraryPaths")]
        public void WindowsAbsolutePathBehaviorIsPreserved()
        {
            string normalized = SoulPath.NormalizeForPlatform(
                @"C:/Music/SoulPlayer/Track.mp3",
                @"D:\SPT_4.1.2",
                true);

            Assert.Equal(@"C:\Music\SoulPlayer\Track.mp3", normalized);
        }

        [Fact]
        [Trait("Validation", "LibraryPaths")]
        public void LinuxAbsolutePathParsesAndNormalizesMixedSeparators()
        {
            string normalized = SoulPath.NormalizeForPlatform(
                @"/home/user\Music/SoulPlayer/../Track.mp3",
                "/opt/spt",
                false);

            Assert.Equal("/home/user/Music/Track.mp3", normalized);
        }

        [Fact]
        [Trait("Validation", "LibraryPaths")]
        public void RelativeLinuxPathUsesStableBase()
        {
            Assert.Equal(
                "/opt/spt/Music/Track.mp3",
                SoulPath.NormalizeForPlatform(
                    "Music/Track.mp3",
                    "/opt/spt",
                    false));
        }

        [Fact]
        [Trait("Validation", "LibraryPaths")]
        public void ExistingSemicolonSeparatedWindowsFolderConfigStillWorks()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                string first = Path.Combine(files.Root, "first");
                string second = Path.Combine(files.Root, "second");
                Directory.CreateDirectory(first);
                Directory.CreateDirectory(second);
                string configPath = Path.Combine(files.Root, "SoulPlayer.cfg");
                ConfigFile config = new ConfigFile(configPath, true);
                config.Bind("Library", "Music folders", string.Empty).Value =
                    first + ";" + second;
                config.Save();

                SoulPlayerSettings settings = new SoulPlayerSettings(config);

                Assert.Equal(new[] { first, second }, settings.GetFolders());
            }
        }

        [Fact]
        [Trait("Validation", "LibraryPaths")]
        public void PlatformIdentityUsesWindowsCaseInsensitivityAndLinuxCaseSensitivity()
        {
            string windowsUpper = SoulPath.CreateStablePathIdentityForPlatform(
                @"C:\Music\TRACK.mp3", @"C:\", true);
            string windowsLower = SoulPath.CreateStablePathIdentityForPlatform(
                @"c:/music/track.mp3", @"C:\", true);
            string linuxUpper = SoulPath.CreateStablePathIdentityForPlatform(
                "/home/user/Music/TRACK.mp3", "/", false);
            string linuxLower = SoulPath.CreateStablePathIdentityForPlatform(
                "/home/user/Music/track.mp3", "/", false);

            Assert.Equal(windowsUpper, windowsLower);
            Assert.NotEqual(linuxUpper, linuxLower);
            Assert.Equal(StringComparison.Ordinal,
                SoulPath.ComparisonForPlatform(false));
            Assert.Equal(StringComparison.OrdinalIgnoreCase,
                SoulPath.ComparisonForPlatform(true));
        }

        [Fact]
        [Trait("Validation", "LibraryPaths")]
        public void RecursiveScanUsesPlatformDirectoryApis()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                files.Create("one/two/Artist - Nested.mp3", 20);

                ScanResult result = MusicLibrary.Scan(new[] { files.Root });

                Assert.Single(result.Tracks);
                Assert.Equal("Nested", result.Tracks[0].Title);
                Assert.Equal(0, result.InaccessibleCount);
            }
        }

        [Fact]
        [Trait("Validation", "LibraryPaths")]
        public void RoutingIdentityRoundTripsWithoutPathSeparators()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack track = files.Create("nested/Artist - Identity.mp3", 21);
                string id = TrackRoutingService.GetTrackId(track);
                TrackRoutingService routing = files.CreateRouting();
                routing.SetRoutes(track, TrackRoute.Extract | TrackRoute.Death);

                Assert.StartsWith("sha256:", id);
                Assert.DoesNotContain("\\", id);
                Assert.DoesNotContain("/", id);
                Assert.Equal(
                    TrackRoute.Extract | TrackRoute.Death,
                    files.CreateRouting().GetRoutes(track));
            }
        }
    }

    internal sealed class TestMusicFiles : IDisposable
    {
        internal TestMusicFiles()
        {
            Root = Path.Combine(Path.GetTempPath(), "SoulPlayerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        internal string Root { get; private set; }

        internal MusicTrack Create(string relativePath, byte seed)
        {
            return Create(relativePath, Content(seed));
        }

        internal MusicTrack Create(string relativePath, byte[] content)
        {
            string platformPath = relativePath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            string path = Path.Combine(Root, platformPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, content);
            return new MusicTrack(path, content.Length);
        }

        internal TrackRoutingService CreateRouting()
        {
            return new TrackRoutingService(
                Path.Combine(Root, "routing", "track-routes.json"),
                delegate { });
        }

        internal static byte[] Content(byte seed)
        {
            return Enumerable.Range(0, 128)
                .Select(index => (byte)(seed + index))
                .ToArray();
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, true);
            }
            catch
            {
            }
        }
    }
}
