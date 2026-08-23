using System;
using System.IO;
using System.Linq;
using SoulPlayer.Cassettes;
using SoulPlayer.Library;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class ExplicitRecorderSelectionTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "SoulTapeExplicitRecorderSelection-" + Guid.NewGuid().ToString("N"));

        public ExplicitRecorderSelectionTests()
        {
            Directory.CreateDirectory(_folder);
        }

        [Fact]
        [Trait("Validation", "RecorderSelection")]
        public void NewProfileHasNoExplicitSelectionAndLegacyResolutionUsesStarter()
        {
            MusicTrack starter = WriteTrack("Scott Buckley - The Long Dark.mp3", 11);
            SoulTapeCatalog catalog = Catalog(starter);
            SoulTapeCollection collection = Collection(
                catalog,
                new InMemoryCollectionStore(),
                "new-profile");

            SoulTapeRecorderSelectionResult result =
                SoulTapeRecorderSelector.Resolve(collection);

            Assert.Equal(string.Empty, collection.GetSelectedRecorderTapeId());
            Assert.False(result.HasExplicitSelection);
            Assert.NotNull(result.Tape);
            Assert.Equal(SoulTapeCatalog.StarterTapeId, result.Tape.Id);
        }

        [Fact]
        [Trait("Validation", "RecorderSelection")]
        public void ExplicitUnlockedSelectionChangesPersistsAndRaisesChanged()
        {
            MusicTrack resonance = WriteTrack("Scott Buckley - Resonance.mp3", 23);
            MusicTrack electric = WriteTrack("Scott Buckley - Electric Dreams.mp3", 29);
            SoulTapeCatalog catalog = Catalog(resonance, electric);
            InMemoryCollectionStore store = new InMemoryCollectionStore();
            SoulTapeCollection collection = Collection(catalog, store, "selection-profile");
            Assert.True(collection.UnlockTape("soul-tape.scott-buckley.resonance"));
            Assert.True(collection.UnlockTape("soul-tape.scott-buckley.electric-dreams"));
            int changes = 0;
            collection.Changed += () => changes++;

            Assert.True(collection.SetRecorderTape("soul-tape.scott-buckley.resonance"));
            Assert.True(collection.SetRecorderTape("soul-tape.scott-buckley.electric-dreams"));

            Assert.Equal(2, changes);
            Assert.Equal(
                "soul-tape.scott-buckley.electric-dreams",
                collection.GetSelectedRecorderTapeId());
            Assert.True(collection.IsRecorderTape(
                "soul-tape.scott-buckley.electric-dreams"));
            Assert.Equal(
                "soul-tape.scott-buckley.electric-dreams",
                store.Load("selection-profile").Data.SelectedRecorderCassetteId);
        }

        [Fact]
        [Trait("Validation", "RecorderSelection")]
        public void LockedAndUnknownCassettesCannotBeSelected()
        {
            SoulTapeCatalog catalog = Catalog();
            SoulTapeCollection collection = Collection(
                catalog,
                new InMemoryCollectionStore(),
                "rejection-profile");

            Assert.False(collection.SetRecorderTape("soul-tape.scott-buckley.resonance"));
            Assert.False(collection.SetRecorderTape("soul-tape.unknown"));
            Assert.Equal(string.Empty, collection.GetSelectedRecorderTapeId());
        }

        [Fact]
        [Trait("Validation", "RecorderSelection")]
        public void SelectionSaveFailureRollsBackAndDoesNotRaiseChanged()
        {
            SoulTapeCatalog catalog = Catalog();
            InMemoryCollectionStore store = new InMemoryCollectionStore();
            SoulTapeCollection collection = Collection(catalog, store, "rollback-profile");
            Assert.True(collection.SetRecorderTape(SoulTapeCatalog.StarterTapeId));
            Assert.True(collection.UnlockTape("soul-tape.scott-buckley.resonance"));
            int changes = 0;
            collection.Changed += () => changes++;
            store.FailSaves = true;

            Assert.False(collection.SetRecorderTape("soul-tape.scott-buckley.resonance"));

            Assert.Equal(0, changes);
            Assert.Equal(
                SoulTapeCatalog.StarterTapeId,
                collection.GetSelectedRecorderTapeId());
        }

        [Fact]
        [Trait("Validation", "RecorderSelection")]
        public void JsonSaveAndReloadRestoreExplicitSelection()
        {
            string collectionFolder = Path.Combine(_folder, "json-collections");
            SoulTapeCatalog catalog = Catalog();
            JsonSoulTapeCollectionStore store = new JsonSoulTapeCollectionStore(collectionFolder);
            SoulTapeCollection collection = Collection(catalog, store, "json-profile");
            Assert.True(collection.UnlockTape("soul-tape.scott-buckley.resonance"));

            Assert.True(collection.SetRecorderTape("soul-tape.scott-buckley.resonance"));

            string json = File.ReadAllText(Path.Combine(collectionFolder, "json-profile.json"));
            Assert.Contains(
                "\"SelectedRecorderCassetteId\": \"soul-tape.scott-buckley.resonance\"",
                json);
            SoulTapeCollection reloaded = new SoulTapeCollection(
                catalog,
                store,
                new OfflineTestLog());
            Assert.True(reloaded.BindProfile("json-profile"));
            Assert.Equal(
                "soul-tape.scott-buckley.resonance",
                reloaded.GetSelectedRecorderTapeId());
        }

        [Fact]
        [Trait("Validation", "RecorderSelection")]
        public void LegacyJsonWithoutSelectionLoadsWithoutBeingRewritten()
        {
            string collectionFolder = Path.Combine(_folder, "legacy-collections");
            Directory.CreateDirectory(collectionFolder);
            string path = Path.Combine(collectionFolder, "legacy-profile.json");
            string legacyJson =
                "{\r\n" +
                "  \"Version\": 1,\r\n" +
                "  \"ProfileId\": \"legacy-profile\",\r\n" +
                "  \"UnlockedCassetteIds\": [\r\n" +
                "    \"soul-tape.scott-buckley.the-long-dark\"\r\n" +
                "  ],\r\n" +
                "  \"FavoriteCassetteIds\": []\r\n" +
                "}";
            File.WriteAllText(path, legacyJson);
            SoulTapeCollection collection = new SoulTapeCollection(
                Catalog(),
                new JsonSoulTapeCollectionStore(collectionFolder),
                new OfflineTestLog());

            Assert.True(collection.BindProfile("legacy-profile"));

            Assert.Equal(string.Empty, collection.GetSelectedRecorderTapeId());
            Assert.Equal(legacyJson, File.ReadAllText(path));
        }

        [Fact]
        [Trait("Validation", "RecorderSelection")]
        public void ProfileSelectionsRemainIsolated()
        {
            SoulTapeCatalog catalog = Catalog();
            InMemoryCollectionStore store = new InMemoryCollectionStore();
            SoulTapeCollection collection = Collection(catalog, store, "profile-a");
            Assert.True(collection.UnlockTape("soul-tape.scott-buckley.resonance"));
            Assert.True(collection.SetRecorderTape("soul-tape.scott-buckley.resonance"));

            Assert.True(collection.BindProfile("profile-b"));
            Assert.Equal(string.Empty, collection.GetSelectedRecorderTapeId());
            Assert.True(collection.SetRecorderTape(SoulTapeCatalog.StarterTapeId));

            Assert.True(collection.BindProfile("profile-a"));
            Assert.Equal(
                "soul-tape.scott-buckley.resonance",
                collection.GetSelectedRecorderTapeId());
        }

        [Fact]
        [Trait("Validation", "RecorderSelection")]
        public void FavoritesAndRecorderSelectionRemainIndependent()
        {
            SoulTapeCatalog catalog = Catalog();
            SoulTapeCollection collection = Collection(
                catalog,
                new InMemoryCollectionStore(),
                "independence-profile");
            Assert.True(collection.UnlockTape("soul-tape.scott-buckley.resonance"));

            Assert.True(collection.SetFavorite("soul-tape.scott-buckley.resonance", true));
            Assert.Equal(string.Empty, collection.GetSelectedRecorderTapeId());
            Assert.True(collection.SetRecorderTape(SoulTapeCatalog.StarterTapeId));

            Assert.True(collection.IsFavorite("soul-tape.scott-buckley.resonance"));
            Assert.False(collection.IsFavorite(SoulTapeCatalog.StarterTapeId));
        }

        [Fact]
        [Trait("Validation", "RecorderSelection")]
        public void MissingSelectedAudioUsesTemporaryFallbackWithoutMutatingSelection()
        {
            MusicTrack starter = WriteTrack("Scott Buckley - The Long Dark.mp3", 31);
            MusicTrack resonance = WriteTrack("Scott Buckley - Resonance.mp3", 37);
            SoulTapeCatalog catalog = Catalog(starter, resonance);
            SoulTapeCollection collection = Collection(
                catalog,
                new InMemoryCollectionStore(),
                "missing-audio-profile");
            Assert.True(collection.UnlockTape("soul-tape.scott-buckley.resonance"));
            Assert.True(collection.SetRecorderTape("soul-tape.scott-buckley.resonance"));

            catalog.Refresh(new[] { starter });
            SoulTapeRecorderSelectionResult fallback =
                SoulTapeRecorderSelector.Resolve(collection);

            Assert.True(fallback.HasExplicitSelection);
            Assert.True(fallback.UsedRuntimeFallback);
            Assert.Equal(SoulTapeCatalog.StarterTapeId, fallback.Tape.Id);
            Assert.Equal(
                "soul-tape.scott-buckley.resonance",
                collection.GetSelectedRecorderTapeId());

            catalog.Refresh(new[] { starter, resonance });
            SoulTapeRecorderSelectionResult restored =
                SoulTapeRecorderSelector.Resolve(collection);
            Assert.False(restored.UsedRuntimeFallback);
            Assert.Equal("soul-tape.scott-buckley.resonance", restored.Tape.Id);
        }

        [Fact]
        [Trait("Validation", "RecorderSelection")]
        public void MissingSelectedAudioAndNoAvailableTapeReturnsNullSafely()
        {
            MusicTrack resonance = WriteTrack("Scott Buckley - Resonance.mp3", 41);
            SoulTapeCatalog catalog = Catalog(resonance);
            SoulTapeCollection collection = Collection(
                catalog,
                new InMemoryCollectionStore(),
                "no-fallback-profile");
            Assert.True(collection.UnlockTape("soul-tape.scott-buckley.resonance"));
            Assert.True(collection.SetRecorderTape("soul-tape.scott-buckley.resonance"));
            catalog.Refresh(Array.Empty<MusicTrack>());

            SoulTapeRecorderSelectionResult result =
                SoulTapeRecorderSelector.Resolve(collection);

            Assert.True(result.HasExplicitSelection);
            Assert.False(result.UsedRuntimeFallback);
            Assert.Null(result.Tape);
            Assert.Equal(
                "soul-tape.scott-buckley.resonance",
                collection.GetSelectedRecorderTapeId());
        }

        private SoulTapeCatalog Catalog(params MusicTrack[] tracks)
        {
            SoulTapeCatalog catalog = new SoulTapeCatalog(new OfflineTestLog());
            catalog.Refresh(tracks);
            return catalog;
        }

        private static SoulTapeCollection Collection(
            SoulTapeCatalog catalog,
            ISoulTapeCollectionStore store,
            string profileId)
        {
            SoulTapeCollection collection = new SoulTapeCollection(
                catalog,
                store,
                new OfflineTestLog());
            Assert.True(collection.BindProfile(profileId));
            return collection;
        }

        private MusicTrack WriteTrack(string fileName, byte seed)
        {
            string path = Path.Combine(_folder, fileName);
            byte[] bytes = Enumerable.Range(0, 4096)
                .Select(value => (byte)((value + seed) % 251))
                .ToArray();
            File.WriteAllBytes(path, bytes);
            return new MusicTrack(path, bytes.Length);
        }

        public void Dispose()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }
    }
}
