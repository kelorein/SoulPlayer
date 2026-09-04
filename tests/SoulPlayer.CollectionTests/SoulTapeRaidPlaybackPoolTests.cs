using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoulPlayer.Cassettes;
using SoulPlayer.Library;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class SoulTapeRaidPlaybackPoolTests : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "SoulTapeRaidPlayback-" + Guid.NewGuid().ToString("N"));

        public SoulTapeRaidPlaybackPoolTests()
        {
            Directory.CreateDirectory(_root);
        }

        [Fact]
        public void FavoritesOnlyUsesDiscoveredFavorites()
        {
            Fixture fixture = Create("a", "b", "c");
            fixture.Unlock("a", "b", "c");
            fixture.Collection.SetFavorite("b", true);
            fixture.Collection.SetFavorite("c", true);

            string[] selected = Take(fixture, SoulTapeRaidPlaybackMode.FavoritesOnly, 2);

            Assert.Equal(new[] { "b", "c" }, selected.OrderBy(id => id));
        }

        [Fact]
        public void DiscoveredModeUsesAllDiscoveredAvailableTapes()
        {
            Fixture fixture = Create("a", "b", "c");
            fixture.Unlock("a", "b", "c");

            string[] selected = Take(fixture, SoulTapeRaidPlaybackMode.Discovered, 3);

            Assert.Equal(new[] { "a", "b", "c" }, selected.OrderBy(id => id));
        }

        [Fact]
        public void FavoritesFirstUsesFavoritesWhenAnyAreAvailable()
        {
            Fixture fixture = Create("a", "b", "c");
            fixture.Unlock("a", "b", "c");
            fixture.Collection.SetFavorite("c", true);

            Assert.Equal("c", fixture.Pool.TakeNext(
                fixture.Collection, SoulTapeRaidPlaybackMode.FavoritesFirst).Tape.Id);
        }

        [Fact]
        public void FavoritesFirstConsumesFavoritesThenNonFavorites()
        {
            Fixture fixture = Create("a", "b", "c", "d", "e", "f");
            fixture.Unlock("a", "b", "c", "d", "e", "f");
            fixture.Collection.SetFavorite("a", true);
            fixture.Collection.SetFavorite("b", true);

            string[] cycle = Take(
                fixture, SoulTapeRaidPlaybackMode.FavoritesFirst, 6);

            Assert.Equal(new[] { "a", "b" }, cycle.Take(2).OrderBy(id => id));
            Assert.Equal(
                new[] { "c", "d", "e", "f" },
                cycle.Skip(2).OrderBy(id => id));
            Assert.Equal(6, cycle.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void FavoritesFirstStartsNewCompleteCycleWithFavoritesAgain()
        {
            Fixture fixture = Create("a", "b", "c", "d");
            fixture.Unlock("a", "b", "c", "d");
            fixture.Collection.SetFavorite("a", true);
            fixture.Collection.SetFavorite("b", true);
            string[] firstCycle = Take(
                fixture, SoulTapeRaidPlaybackMode.FavoritesFirst, 4);

            string next = fixture.Pool.TakeNext(
                fixture.Collection, SoulTapeRaidPlaybackMode.FavoritesFirst).Tape.Id;

            Assert.Contains(next, new[] { "a", "b" });
            Assert.NotEqual(firstCycle[3], next);
        }

        [Fact]
        public void FavoritesFirstWithoutFavoritesMatchesDiscoveredShuffle()
        {
            Fixture favoritesFirst = Create("a", "b", "c", "d");
            Fixture discovered = Create("a", "b", "c", "d");
            favoritesFirst.Unlock("a", "b", "c", "d");
            discovered.Unlock("a", "b", "c", "d");

            Assert.Equal(
                Take(discovered, SoulTapeRaidPlaybackMode.Discovered, 4),
                Take(favoritesFirst, SoulTapeRaidPlaybackMode.FavoritesFirst, 4));
        }

        [Fact]
        public void FavoritesFirstWithAllFavoritesMatchesFavoritesOnlyShuffle()
        {
            Fixture favoritesFirst = Create("a", "b", "c", "d");
            Fixture favoritesOnly = Create("a", "b", "c", "d");
            favoritesFirst.Unlock("a", "b", "c", "d");
            favoritesOnly.Unlock("a", "b", "c", "d");
            foreach (string id in new[] { "a", "b", "c", "d" })
            {
                favoritesFirst.Collection.SetFavorite(id, true);
                favoritesOnly.Collection.SetFavorite(id, true);
            }

            Assert.Equal(
                Take(favoritesOnly, SoulTapeRaidPlaybackMode.FavoritesOnly, 4),
                Take(favoritesFirst, SoulTapeRaidPlaybackMode.FavoritesFirst, 4));
        }

        [Fact]
        public void FavoritesFirstFallsBackToAllDiscovered()
        {
            Fixture fixture = Create("a", "b");
            fixture.Unlock("a", "b");

            string[] selected = Take(
                fixture, SoulTapeRaidPlaybackMode.FavoritesFirst, 2);

            Assert.Equal(new[] { "a", "b" }, selected.OrderBy(id => id));
        }

        [Fact]
        public void UndiscoveredTapesAreAlwaysExcluded()
        {
            Fixture fixture = Create("a", "b");
            fixture.Unlock("a");

            Assert.Equal("a", fixture.Pool.TakeNext(
                fixture.Collection, SoulTapeRaidPlaybackMode.Discovered).Tape.Id);
        }

        [Fact]
        public void UnavailableAudioIsAlwaysExcluded()
        {
            Fixture fixture = CreateWithUnavailable("missing", "available");
            fixture.Unlock("missing", "available");

            Assert.Equal("available", fixture.Pool.TakeNext(
                fixture.Collection, SoulTapeRaidPlaybackMode.Discovered).Tape.Id);
        }

        [Fact]
        public void BagDoesNotRepeatUntilEveryEligibleTapeWasConsumed()
        {
            Fixture fixture = Create("a", "b", "c", "d");
            fixture.Unlock("a", "b", "c", "d");

            string[] selected = Take(fixture, SoulTapeRaidPlaybackMode.Discovered, 4);

            Assert.Equal(4, selected.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void ExhaustedBagReshufflesAndContinues()
        {
            Fixture fixture = Create("a", "b", "c");
            fixture.Unlock("a", "b", "c");
            Take(fixture, SoulTapeRaidPlaybackMode.Discovered, 3);

            SoulTapeRaidPlaybackSelection next = fixture.Pool.TakeNext(
                fixture.Collection, SoulTapeRaidPlaybackMode.Discovered);

            Assert.NotNull(next.Tape);
            Assert.Contains(next.Tape.Id, new[] { "a", "b", "c" });
        }

        [Fact]
        public void ShuffleBoundaryAvoidsImmediateRepeat()
        {
            Fixture fixture = Create("a", "b", "c");
            fixture.Unlock("a", "b", "c");
            string[] firstCycle = Take(
                fixture, SoulTapeRaidPlaybackMode.Discovered, 3);

            string next = fixture.Pool.TakeNext(
                fixture.Collection, SoulTapeRaidPlaybackMode.Discovered).Tape.Id;

            Assert.NotEqual(firstCycle[2], next);
        }

        [Fact]
        public void FavoriteChangeDoesNotAlterAlreadySelectedTrack()
        {
            Fixture fixture = Create("a", "b");
            fixture.Unlock("a", "b");
            SoulTapeCatalogEntry active = fixture.Pool.TakeNext(
                fixture.Collection, SoulTapeRaidPlaybackMode.FavoritesFirst).Tape;

            fixture.Collection.SetFavorite(
                active.Id == "a" ? "b" : "a", true);

            Assert.NotNull(active.Track);
            Assert.True(File.Exists(active.Track.FilePath));
            Assert.NotSame(active, fixture.Pool.TakeNext(
                fixture.Collection, SoulTapeRaidPlaybackMode.FavoritesFirst).Tape);
        }

        [Fact]
        public void FavoriteChangeRebuildsOnlyFutureSelectionsWithoutRepeats()
        {
            Fixture fixture = Create("a", "b", "c", "d");
            fixture.Unlock("a", "b", "c", "d");
            fixture.Collection.SetFavorite("a", true);
            SoulTapeCatalogEntry active = fixture.Pool.TakeNext(
                fixture.Collection, SoulTapeRaidPlaybackMode.FavoritesFirst).Tape;

            fixture.Collection.SetFavorite("b", true);
            string[] future = Take(
                fixture, SoulTapeRaidPlaybackMode.FavoritesFirst, 3);

            Assert.Equal("a", active.Id);
            Assert.Equal("b", future[0]);
            Assert.DoesNotContain(active.Id, future);
            Assert.Equal(3, future.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void NewlyDiscoveredTapeJoinsCurrentFutureSelection()
        {
            Fixture fixture = Create("a", "b");
            fixture.Unlock("a");
            Assert.Equal("a", fixture.Pool.TakeNext(
                fixture.Collection, SoulTapeRaidPlaybackMode.Discovered).Tape.Id);

            fixture.Unlock("b");

            Assert.Equal("b", fixture.Pool.TakeNext(
                fixture.Collection, SoulTapeRaidPlaybackMode.Discovered).Tape.Id);
        }

        [Fact]
        public void NewlyDiscoveredFavoriteJoinsFavoritesFirstFutureSafely()
        {
            Fixture fixture = Create("a", "b", "c");
            fixture.Unlock("a", "b");
            fixture.Collection.SetFavorite("a", true);
            Assert.Equal("a", fixture.Pool.TakeNext(
                fixture.Collection, SoulTapeRaidPlaybackMode.FavoritesFirst).Tape.Id);

            fixture.Unlock("c");
            fixture.Collection.SetFavorite("c", true);
            string[] future = Take(
                fixture, SoulTapeRaidPlaybackMode.FavoritesFirst, 2);

            Assert.Equal("c", future[0]);
            Assert.Equal("b", future[1]);
        }

        [Fact]
        public void FavoritesOnlyWithNoFavoritesReturnsGracefulEmptyState()
        {
            Fixture fixture = Create("a");
            fixture.Unlock("a");

            SoulTapeRaidPlaybackSelection result = fixture.Pool.TakeNext(
                fixture.Collection, SoulTapeRaidPlaybackMode.FavoritesOnly);

            Assert.Null(result.Tape);
            Assert.Equal("NO FAVORITE TAPES", result.EmptyHeading);
            Assert.Equal(
                "Favorite discovered cassettes in Collection", result.EmptyDetail);
        }

        [Theory]
        [InlineData((int)SoulTapeRaidPlaybackMode.Discovered)]
        [InlineData((int)SoulTapeRaidPlaybackMode.FavoritesFirst)]
        public void ZeroDiscoveriesReturnsGracefulEmptyState(int modeValue)
        {
            Fixture fixture = CreateLoadedEmpty("a");

            SoulTapeRaidPlaybackSelection result = fixture.Pool.TakeNext(
                fixture.Collection, (SoulTapeRaidPlaybackMode)modeValue);

            Assert.Null(result.Tape);
            Assert.Equal("NO TAPES DISCOVERED", result.EmptyHeading);
            Assert.Equal("Find SoulTapes during raids", result.EmptyDetail);
        }

        [Fact]
        public void RaidPlaybackModeUsesPersistedConfigWithFavoritesFirstDefault()
        {
            string source = File.ReadAllText(Path.Combine(
                FindRepositoryRoot(), "Configuration", "SoulPlayerSettings.cs"));

            Assert.Contains("\"Raid cassette playback mode\"", source);
            Assert.Contains("SoulTapeRaidPlaybackMode.FavoritesFirst", source);
            Assert.Contains("_raidCassettePlaybackMode.Value", source);
        }

        [Fact]
        public void LegacyManualSelectionDoesNotAffectRaidPlayback()
        {
            Fixture fixture = Create("a", "b", "c");
            fixture.Unlock("a", "b", "c");
            Assert.True(fixture.Collection.SetRecorderTape("a"));

            string[] selected = Take(
                fixture, SoulTapeRaidPlaybackMode.Discovered, 3);

            Assert.Equal(new[] { "a", "b", "c" }, selected.OrderBy(id => id));
        }

        [Fact]
        public void CollectionPageContainsNoManualRecorderLoadControl()
        {
            string source = File.ReadAllText(Path.Combine(
                FindRepositoryRoot(), "UI", "SoulTapeCollectionPage.cs"));

            Assert.DoesNotContain("LOAD RECORDER", source);
            Assert.DoesNotContain("RECORDER TAPE", source);
            Assert.DoesNotContain("SelectRecorderTape", source);
        }

        private Fixture Create(params string[] ids)
        {
            return CreateInternal(ids, null, false);
        }

        private Fixture CreateWithUnavailable(
            string unavailable,
            params string[] available)
        {
            return CreateInternal(
                new[] { unavailable }.Concat(available).ToArray(),
                unavailable,
                false);
        }

        private Fixture CreateLoadedEmpty(params string[] ids)
        {
            return CreateInternal(ids, null, true);
        }

        private Fixture CreateInternal(
            IEnumerable<string> ids,
            string unavailable,
            bool loadedEmpty)
        {
            List<SoulTapeCatalogEntry> entries = ids.Select(id =>
            {
                string path = Path.Combine(_root, id + ".mp3");
                MusicTrack track;
                if (string.Equals(id, unavailable, StringComparison.Ordinal))
                {
                    track = new MusicTrack(path, 100L);
                }
                else
                {
                    File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
                    track = new MusicTrack(path, 4L);
                }
                return new SoulTapeCatalogEntry(
                    id, "Artist " + id, "Track " + id, path,
                    SoulTapeRarity.Common, null, track);
            }).ToList();
            TestCatalog catalog = new TestCatalog(entries);
            InMemoryCollectionStore store = new InMemoryCollectionStore();
            if (loadedEmpty)
            {
                store.Save("profile", new SoulTapeCollectionData
                {
                    ProfileId = "profile",
                    UnlockedCassetteIds = new List<string>(),
                    FavoriteCassetteIds = new List<string>()
                });
            }
            SoulTapeCollection collection = new SoulTapeCollection(
                catalog, store, new OfflineTestLog());
            Assert.True(collection.BindProfile("profile"));
            return new Fixture(collection, new SoulTapeRaidPlaybackPool(new Random(71)));
        }

        private static string[] Take(
            Fixture fixture,
            SoulTapeRaidPlaybackMode mode,
            int count)
        {
            return Enumerable.Range(0, count)
                .Select(_ => fixture.Pool.TakeNext(fixture.Collection, mode).Tape.Id)
                .ToArray();
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
            throw new DirectoryNotFoundException("SoulPlayer repository root not found.");
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private sealed class Fixture
        {
            internal Fixture(
                SoulTapeCollection collection,
                SoulTapeRaidPlaybackPool pool)
            {
                Collection = collection;
                Pool = pool;
            }

            internal SoulTapeCollection Collection { get; private set; }
            internal SoulTapeRaidPlaybackPool Pool { get; private set; }

            internal void Unlock(params string[] ids)
            {
                foreach (string id in ids)
                {
                    Assert.True(Collection.UnlockTape(id));
                }
            }
        }

        private sealed class TestCatalog : ISoulTapeCatalog
        {
            private readonly Dictionary<string, SoulTapeCatalogEntry> _entries;

            internal TestCatalog(IEnumerable<SoulTapeCatalogEntry> entries)
            {
                _entries = entries.ToDictionary(
                    entry => entry.Id, StringComparer.Ordinal);
            }

            public bool TryGetTape(string id, out SoulTapeCatalogEntry tape)
            {
                return _entries.TryGetValue(id, out tape);
            }

            public IReadOnlyList<SoulTapeCatalogEntry> GetAllTapes()
            {
                return _entries.Values.ToList();
            }
        }
    }
}
