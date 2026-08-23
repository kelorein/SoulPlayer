using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoulPlayer.Cassettes;
using SoulPlayer.Library;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class SoulTapeCollectionBrowserTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "SoulTapeCollectionBrowser-" + Guid.NewGuid().ToString("N"));

        public SoulTapeCollectionBrowserTests()
        {
            Directory.CreateDirectory(_folder);
        }

        [Fact]
        [Trait("Validation", "CollectionBrowser")]
        public void OnlyExplicitRarityEntriesBecomeCollectibleSlots()
        {
            BrowserFixture fixture = CreateFixture();

            SoulTapeCollectionSnapshot snapshot = fixture.Project();

            Assert.Equal(3, snapshot.TotalCount);
            Assert.DoesNotContain(snapshot.Entries, card => card.Id == "soul-tape.audio.generated");
        }

        [Fact]
        [Trait("Validation", "CollectionBrowser")]
        public void StarterAppearsDiscoveredForNewProfile()
        {
            BrowserFixture fixture = CreateFixture();

            SoulTapeCollectionCard starter = fixture.Project().Entries.Single(
                card => card.Id == SoulTapeCatalog.StarterTapeId);

            Assert.True(starter.IsDiscovered);
            Assert.Equal("Scott Buckley", starter.Artist);
            Assert.Equal("The Long Dark", starter.Title);
            Assert.Equal(SoulTapeRarity.Common, starter.Rarity);
        }

        [Fact]
        [Trait("Validation", "CollectionBrowser")]
        public void LockedProjectionContainsNoPresentationMetadata()
        {
            BrowserFixture fixture = CreateFixture();

            SoulTapeCollectionCard locked = fixture.Project().Entries.Single(
                card => card.Id == "soul-tape.curated.locked");

            Assert.False(locked.IsDiscovered);
            Assert.Equal(string.Empty, locked.Artist);
            Assert.Equal(string.Empty, locked.Title);
            Assert.Null(locked.Rarity);
            Assert.False(locked.IsFavorite);
            Assert.False(locked.IsRecorderSelected);
            Assert.False(locked.IsAudioAvailable);
        }

        [Fact]
        [Trait("Validation", "CollectionBrowser")]
        public void ProgressCountsOnlyCuratedCollectibles()
        {
            BrowserFixture fixture = CreateFixture();
            Assert.True(fixture.Collection.UnlockTape("soul-tape.curated.available"));

            SoulTapeCollectionSnapshot snapshot = fixture.Project();

            Assert.Equal(2, snapshot.DiscoveredCount);
            Assert.Equal(3, snapshot.TotalCount);
        }

        [Fact]
        [Trait("Validation", "CollectionBrowser")]
        public void FiltersReturnOnlyTheirIntendedCards()
        {
            BrowserFixture fixture = CreateFixture();
            Assert.True(fixture.Collection.UnlockTape("soul-tape.curated.available"));
            Assert.True(fixture.Collection.SetFavorite("soul-tape.curated.available", true));

            SoulTapeCollectionSnapshot all = fixture.Project(SoulTapeCollectionFilter.All);
            SoulTapeCollectionSnapshot discovered = fixture.Project(
                SoulTapeCollectionFilter.Discovered);
            SoulTapeCollectionSnapshot favorites = fixture.Project(
                SoulTapeCollectionFilter.Favorites);
            SoulTapeCollectionSnapshot undiscovered = fixture.Project(
                SoulTapeCollectionFilter.Undiscovered);

            Assert.Equal(3, all.Entries.Count);
            Assert.All(discovered.Entries, card => Assert.True(card.IsDiscovered));
            Assert.Single(favorites.Entries);
            Assert.True(favorites.Entries[0].IsDiscovered);
            Assert.True(favorites.Entries[0].IsFavorite);
            Assert.All(undiscovered.Entries, card => Assert.False(card.IsDiscovered));
        }

        [Fact]
        [Trait("Validation", "CollectionBrowser")]
        public void MissingAudioPreservesDiscoveryAndFavoritePresentation()
        {
            BrowserFixture fixture = CreateFixture();
            Assert.True(fixture.Collection.UnlockTape("soul-tape.curated.locked"));
            Assert.True(fixture.Collection.SetFavorite("soul-tape.curated.locked", true));

            SoulTapeCollectionCard card = fixture.Project().Entries.Single(
                item => item.Id == "soul-tape.curated.locked");

            Assert.True(card.IsDiscovered);
            Assert.True(card.IsFavorite);
            Assert.False(card.IsAudioAvailable);
            Assert.Equal("Hidden Artist", card.Artist);
        }

        [Fact]
        [Trait("Validation", "CollectionBrowser")]
        public void OrderingIsDiscoveredFirstThenStableIdAndDoesNotChangeForFavorite()
        {
            BrowserFixture fixture = CreateFixture();
            Assert.True(fixture.Collection.UnlockTape("soul-tape.curated.available"));
            string[] before = fixture.Project().Entries.Select(card => card.Id).ToArray();
            Assert.True(fixture.Collection.SetFavorite("soul-tape.curated.available", true));
            string[] after = fixture.Project().Entries.Select(card => card.Id).ToArray();

            Assert.Equal(before, after);
            Assert.Equal(new[]
            {
                "soul-tape.curated.available",
                SoulTapeCatalog.StarterTapeId,
                "soul-tape.curated.locked"
            }, after);
        }

        [Fact]
        [Trait("Validation", "CollectionBrowser")]
        public void FavoriteSuccessPersistsAndReprojectsImmediately()
        {
            BrowserFixture fixture = CreateFixture();
            Assert.True(fixture.Collection.UnlockTape("soul-tape.curated.available"));

            Assert.True(fixture.Collection.SetFavorite("soul-tape.curated.available", true));

            Assert.True(fixture.Project().Entries.Single(
                card => card.Id == "soul-tape.curated.available").IsFavorite);
            Assert.Contains(
                "soul-tape.curated.available",
                fixture.Store.Load("profile-a").Data.FavoriteCassetteIds);
        }

        [Fact]
        [Trait("Validation", "CollectionBrowser")]
        public void FavoriteSaveFailureRollsBackProjection()
        {
            BrowserFixture fixture = CreateFixture();
            Assert.True(fixture.Collection.UnlockTape("soul-tape.curated.available"));
            fixture.Store.FailSaves = true;

            Assert.False(fixture.Collection.SetFavorite("soul-tape.curated.available", true));

            Assert.False(fixture.Project().Entries.Single(
                card => card.Id == "soul-tape.curated.available").IsFavorite);
        }

        [Fact]
        [Trait("Validation", "CollectionBrowser")]
        public void ProjectionMarksOnlySelectedDiscoveredCollectible()
        {
            BrowserFixture fixture = CreateFixture();
            Assert.True(fixture.Collection.UnlockTape("soul-tape.curated.available"));
            Assert.True(fixture.Collection.SetRecorderTape("soul-tape.curated.available"));

            SoulTapeCollectionSnapshot snapshot = fixture.Project();

            Assert.Single(snapshot.Entries.Where(card => card.IsRecorderSelected));
            SoulTapeCollectionCard selected = snapshot.Entries.Single(
                card => card.IsRecorderSelected);
            Assert.Equal("soul-tape.curated.available", selected.Id);
            Assert.True(snapshot.HasRecorderSelection);
            Assert.True(snapshot.HasVisibleRecorderSelection);
            Assert.Equal("Available Artist", snapshot.RecorderArtist);
        }

        [Fact]
        [Trait("Validation", "CollectionBrowser")]
        public void SelectedPersonalTrackDoesNotLeakIntoCollectibleProjection()
        {
            BrowserFixture fixture = CreateFixture();
            Assert.True(fixture.Collection.UnlockTape("soul-tape.audio.generated"));
            Assert.True(fixture.Collection.SetRecorderTape("soul-tape.audio.generated"));

            SoulTapeCollectionSnapshot snapshot = fixture.Project();

            Assert.True(snapshot.HasRecorderSelection);
            Assert.False(snapshot.HasVisibleRecorderSelection);
            Assert.Equal(string.Empty, snapshot.RecorderArtist);
            Assert.Equal(string.Empty, snapshot.RecorderTitle);
            Assert.DoesNotContain(
                snapshot.Entries,
                card => card.Id == "soul-tape.audio.generated");
        }

        [Fact]
        [Trait("Validation", "CollectionBrowser")]
        public void ProfileSwitchShowsSeparateProgression()
        {
            BrowserFixture fixture = CreateFixture();
            Assert.True(fixture.Collection.UnlockTape("soul-tape.curated.available"));
            Assert.True(fixture.Collection.SetFavorite("soul-tape.curated.available", true));

            Assert.True(fixture.Collection.BindProfile("profile-b"));
            SoulTapeCollectionSnapshot second = fixture.Project();

            Assert.Equal(1, second.DiscoveredCount);
            Assert.DoesNotContain(second.Entries, card => card.IsFavorite);
            Assert.True(fixture.Collection.BindProfile("profile-a"));
            Assert.Equal(2, fixture.Project().DiscoveredCount);
        }

        [Fact]
        [Trait("Validation", "CollectionBrowser")]
        public void UnloadedCollectionProjectsUnavailableWithoutCatalogMetadata()
        {
            TestCatalog catalog = new TestCatalog(new[]
            {
                Entry("soul-tape.secret", "Secret Artist", "Secret Title", SoulTapeRarity.Legendary)
            });
            SoulTapeCollection collection = new SoulTapeCollection(
                catalog,
                new InMemoryCollectionStore(),
                new OfflineTestLog());

            SoulTapeCollectionSnapshot snapshot =
                new SoulTapeCollectionProjection(catalog, collection).Project(
                    SoulTapeCollectionFilter.All);

            Assert.False(snapshot.IsAvailable);
            Assert.Equal(0, snapshot.TotalCount);
            Assert.Empty(snapshot.Entries);
        }

        [Fact]
        [Trait("Validation", "CollectionBrowser")]
        public void CatalogAudioRefreshReconnectsWithoutLosingProgression()
        {
            BrowserFixture fixture = CreateFixture();
            Assert.True(fixture.Collection.UnlockTape("soul-tape.curated.locked"));
            Assert.True(fixture.Collection.SetFavorite("soul-tape.curated.locked", true));
            Assert.False(fixture.Project().Entries.Single(
                card => card.Id == "soul-tape.curated.locked").IsAudioAvailable);

            MusicTrack restored = CreateTrack("Hidden Artist - Hidden Title.flac");
            fixture.Catalog.Replace(Entry(
                "soul-tape.curated.locked",
                "Hidden Artist",
                "Hidden Title",
                SoulTapeRarity.Epic,
                restored));

            SoulTapeCollectionCard refreshed = fixture.Project().Entries.Single(
                card => card.Id == "soul-tape.curated.locked");
            Assert.True(refreshed.IsDiscovered);
            Assert.True(refreshed.IsFavorite);
            Assert.True(refreshed.IsAudioAvailable);
        }

        private BrowserFixture CreateFixture()
        {
            MusicTrack available = CreateTrack("Available Artist - Available Title.flac");
            TestCatalog catalog = new TestCatalog(new[]
            {
                Entry("soul-tape.curated.locked", "Hidden Artist", "Hidden Title", SoulTapeRarity.Epic),
                Entry("soul-tape.audio.generated", "Personal Artist", "Personal Song", null),
                Entry(SoulTapeCatalog.StarterTapeId, "Scott Buckley", "The Long Dark", SoulTapeRarity.Common),
                Entry("soul-tape.curated.available", "Available Artist", "Available Title", SoulTapeRarity.Rare, available)
            });
            InMemoryCollectionStore store = new InMemoryCollectionStore();
            SoulTapeCollection collection = new SoulTapeCollection(
                catalog,
                store,
                new OfflineTestLog());
            Assert.True(collection.BindProfile("profile-a"));
            return new BrowserFixture(catalog, collection, store);
        }

        private MusicTrack CreateTrack(string fileName)
        {
            string path = Path.Combine(_folder, fileName);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });
            return new MusicTrack(path, new FileInfo(path).Length);
        }

        private static SoulTapeCatalogEntry Entry(
            string id,
            string artist,
            string title,
            SoulTapeRarity? rarity,
            MusicTrack track = null)
        {
            return new SoulTapeCatalogEntry(
                id,
                artist,
                title,
                "test:" + id,
                rarity,
                null,
                track);
        }

        public void Dispose()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        private sealed class BrowserFixture
        {
            internal BrowserFixture(
                TestCatalog catalog,
                SoulTapeCollection collection,
                InMemoryCollectionStore store)
            {
                Catalog = catalog;
                Collection = collection;
                Store = store;
                Projection = new SoulTapeCollectionProjection(catalog, collection);
            }

            internal TestCatalog Catalog { get; private set; }
            internal SoulTapeCollection Collection { get; private set; }
            internal InMemoryCollectionStore Store { get; private set; }
            internal SoulTapeCollectionProjection Projection { get; private set; }

            internal SoulTapeCollectionSnapshot Project(
                SoulTapeCollectionFilter filter = SoulTapeCollectionFilter.All)
            {
                return Projection.Project(filter);
            }
        }

        private sealed class TestCatalog : ISoulTapeCatalog
        {
            private readonly Dictionary<string, SoulTapeCatalogEntry> _entries;

            internal TestCatalog(IEnumerable<SoulTapeCatalogEntry> entries)
            {
                _entries = entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
            }

            internal void Replace(SoulTapeCatalogEntry entry)
            {
                _entries[entry.Id] = entry;
            }

            public bool TryGetTape(string id, out SoulTapeCatalogEntry tape)
            {
                return _entries.TryGetValue(id ?? string.Empty, out tape);
            }

            public IReadOnlyList<SoulTapeCatalogEntry> GetAllTapes()
            {
                return _entries.Values.ToList();
            }
        }
    }
}
