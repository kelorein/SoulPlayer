using System;
using System.IO;
using SoulPlayer.Library;
using SoulPlayer.UI;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class SoulPlayerTrackRoutingUiTests
    {
        private const int PageSize = 8;

        [Fact]
        [Trait("Validation", "LibraryUi")]
        public void ToggleRouteOnPageTwoPreservesPageTwo()
        {
            SoulPlayerLibraryViewState state = StateOnPage(1);

            state.ClampToResults(24, PageSize);

            Assert.Equal(1, state.PageIndex);
        }

        [Fact]
        [Trait("Validation", "LibraryUi")]
        public void ToggleRouteOnLastPagePreservesLastPage()
        {
            SoulPlayerLibraryViewState state = StateOnPage(2);

            state.ClampToResults(17, PageSize);

            Assert.Equal(2, state.PageIndex);
        }

        [Fact]
        [Trait("Validation", "LibraryUi")]
        public void SearchQueryRemainsAfterRouteToggle()
        {
            SoulPlayerLibraryViewState state = new SoulPlayerLibraryViewState();
            state.SetSearchQuery("dark ambient");
            state.SetPage(1);

            state.ClampToResults(18, PageSize);

            Assert.Equal("dark ambient", state.SearchQuery);
            Assert.Equal(1, state.PageIndex);
        }

        [Fact]
        [Trait("Validation", "LibraryUi")]
        public void ConsecutiveRouteTogglesDoNotResetPagination()
        {
            SoulPlayerLibraryViewState state = StateOnPage(5);

            for (int toggle = 0; toggle < 6; toggle++)
            {
                state.ClampToResults(88, PageSize);
            }

            Assert.Equal(5, state.PageIndex);
        }

        [Fact]
        [Trait("Validation", "LibraryUi")]
        public void RoutePersistenceStillSavesImmediately()
        {
            using (TestMusicFiles files = new TestMusicFiles())
            {
                MusicTrack track = files.Create("Artist - Immediate.mp3", 31);
                TrackRoutingService routing = files.CreateRouting();

                routing.SetRoutes(track, TrackRoute.Death);

                Assert.True(File.Exists(routing.PersistencePath));
                Assert.Equal(
                    TrackRoute.Death,
                    files.CreateRouting().GetRoutes(track));
            }
        }

        [Fact]
        [Trait("Validation", "LibraryUi")]
        public void PageClampsOnlyWhenResultCountInvalidatesIt()
        {
            SoulPlayerLibraryViewState state = StateOnPage(1);

            state.ClampToResults(9, PageSize);
            Assert.Equal(1, state.PageIndex);

            state.ClampToResults(8, PageSize);
            Assert.Equal(0, state.PageIndex);
        }

        [Fact]
        [Trait("Validation", "LibraryUi")]
        public void VisibleRouteControlsUseFullLabels()
        {
            string source = ReadWindowSource();

            Assert.Contains("\"PLAYBACK ROUTES\"", source);
            Assert.Contains("TrackRoute.Main, \"Main\"", source);
            Assert.Contains("TrackRoute.Extract, \"Extract\"", source);
            Assert.Contains("TrackRoute.Death, \"Death\"", source);
            Assert.Contains("caption + \" ON\"", source);
        }

        [Fact]
        [Trait("Validation", "LibraryUi")]
        public void NoUnexplainedLetterOnlyRoutingPresentationRemains()
        {
            string source = ReadWindowSource();

            Assert.DoesNotContain("ROUTES  M / E / D", source);
            Assert.DoesNotContain("TrackRoute.Main, \"M\"", source);
            Assert.DoesNotContain("TrackRoute.Extract, \"E\"", source);
            Assert.DoesNotContain("TrackRoute.Death, \"D\"", source);
            Assert.DoesNotContain("row's M / E / D", source);
        }

        [Fact]
        [Trait("Validation", "LibraryUi")]
        public void RoutingChangesUseDedicatedRefreshInsteadOfLibraryReset()
        {
            string source = ReadWindowSource();
            int handler = source.IndexOf(
                "private void OnRoutingChanged()",
                StringComparison.Ordinal);
            Assert.True(handler >= 0);
            string body = source.Substring(handler, Math.Min(180, source.Length - handler));

            Assert.Contains("_routingDirty = true", body);
            Assert.DoesNotContain("_libraryDirty = true", body);
        }

        private static SoulPlayerLibraryViewState StateOnPage(int pageIndex)
        {
            SoulPlayerLibraryViewState state = new SoulPlayerLibraryViewState();
            state.SetPage(pageIndex);
            return state;
        }

        private static string ReadWindowSource()
        {
            return File.ReadAllText(Path.Combine(
                FindRepositoryRoot(),
                "UI",
                "SoulPlayerWindow.cs"));
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
