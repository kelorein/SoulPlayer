using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoulPlayer.Cassettes;
using SoulPlayer.Library;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class DynamicUserCassetteCollectionTests : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "DynamicSoulTapeCollection-" + Guid.NewGuid().ToString("N"));
        private readonly string _builtIn;
        private readonly string _user;
        private readonly string _collections;
        private readonly string _assignments;
        private readonly OfflineTestLog _log = new OfflineTestLog();

        public DynamicUserCassetteCollectionTests()
        {
            _builtIn = Path.Combine(_root, "DefaultMusic");
            _user = Path.Combine(_root, "UserMusic");
            _collections = Path.Combine(_root, "collections");
            _assignments = Path.Combine(_root, "assignments");
            Directory.CreateDirectory(_builtIn);
            Directory.CreateDirectory(_user);
        }

        [Fact]
        [Trait("Validation", "DynamicCollection")]
        public void RhyeWorldPickupSurvivesFullRestartAndAppearsInViewModel()
        {
            MusicTrack rhyeTrack = WriteTrack(
                _user, "Rhye - Waste (RY X Remix).mp3", 17);
            SoulTapeCatalog firstCatalog = Catalog(rhyeTrack);
            SoulTapeCatalogEntry rhye = firstCatalog.GetAllTapes()
                .Single(tape => tape.Track == rhyeTrack);
            SoulTapeSpawnAnchor anchor = Anchor("rhye-anchor");
            SoulTapeAssignmentSnapshot firstAssignments = Assign(
                "rhye-profile",
                SoulTapeMusicMode.MergeBuiltInAndUser,
                firstCatalog,
                new[] { anchor });
            SoulTapeCollection firstCollection = Collection(firstCatalog);
            Assert.True(firstCollection.BindProfile("rhye-profile"));
            IReadOnlyList<SoulTapeSpawnPlanEntry> world =
                SoulTapeWorldEligibility.GetAssignedTapes(
                    firstAssignments,
                    new[] { anchor },
                    firstCatalog,
                    firstCollection);
            Assert.Single(world);
            Assert.Equal(rhye.Id, world[0].Cassette.Id);
            Assert.Equal(
                SoulTapeDiscoveryResult.NewUnlock,
                new SoulTapeDiscoveryService(
                    firstCatalog, firstCollection, _log).Discover(rhye.Id));

            SoulTapeCatalog restartedCatalog = Catalog(
                new MusicTrack(rhyeTrack.FilePath, rhyeTrack.SizeBytes));
            SoulTapeCollection restartedCollection = Collection(restartedCatalog);
            Assert.True(restartedCollection.BindProfile("rhye-profile"));
            SoulTapeAssignmentSnapshot restartedAssignments = Assign(
                "rhye-profile",
                SoulTapeMusicMode.MergeBuiltInAndUser,
                restartedCatalog,
                new[] { anchor });
            SoulTapeCollectionSnapshot snapshot = Projection(
                restartedCatalog,
                restartedCollection,
                restartedAssignments);

            Assert.True(restartedCollection.IsUnlocked(rhye.Id));
            SoulTapeCollectionCard card = snapshot.Entries.Single(entry =>
                entry.Id == rhye.Id);
            Assert.True(card.IsDiscovered);
            Assert.Equal("Rhye", card.Artist);
            Assert.Equal("Waste (RY X Remix)", card.Title);
            Assert.Contains(rhye.Id, restartedCollection.GetUnlockedTapeIds());
            Assert.Equal(2, snapshot.DiscoveredCount);
        }

        [Fact]
        [Trait("Validation", "DynamicCollection")]
        public void DynamicCollectionCanContainMoreThanEightEntries()
        {
            MusicTrack[] tracks = Enumerable.Range(0, 12)
                .Select(index => WriteTrack(
                    _user,
                    "User Artist " + index + " - User Song " + index + ".mp3",
                    index + 30))
                .ToArray();
            SoulTapeCatalog catalog = Catalog(tracks);
            SoulTapeCollection collection = Collection(catalog);
            Assert.True(collection.BindProfile("large-profile"));
            SoulTapeAssignmentSnapshot assignments = Assign(
                "large-profile",
                SoulTapeMusicMode.UserOnly,
                catalog,
                Enumerable.Range(0, 12).Select(index => Anchor("large-" + index)));

            SoulTapeCollectionSnapshot snapshot = Projection(
                catalog, collection, assignments);

            Assert.Equal(13, snapshot.TotalCount);
            Assert.True(snapshot.TotalCount > 8);
        }

        [Fact]
        [Trait("Validation", "DynamicCollection")]
        public void UserOnlyAndMergeExposeTheirPersistedAssignmentPools()
        {
            MusicTrack built = WriteTrack(
                _builtIn, "Built Artist - Built Song.mp3", 61);
            MusicTrack user = WriteTrack(
                _user, "User Artist - User Song.mp3", 62);
            SoulTapeCatalog catalog = Catalog(built, user);
            SoulTapeCollection collection = Collection(catalog);
            Assert.True(collection.BindProfile("mode-profile"));
            SoulTapeSpawnAnchor[] anchors =
            {
                Anchor("mode-a"), Anchor("mode-b")
            };

            SoulTapeAssignmentSnapshot userOnly = Assign(
                "mode-profile-user",
                SoulTapeMusicMode.UserOnly,
                catalog,
                anchors);
            SoulTapeCollection userCollection = Collection(catalog);
            Assert.True(userCollection.BindProfile("mode-profile-user"));
            SoulTapeCollectionSnapshot userSnapshot = Projection(
                catalog, userCollection, userOnly);
            Assert.Single(userSnapshot.Entries.Where(entry =>
                entry.Id != SoulTapeCatalog.StarterTapeId));
            Assert.Contains(userSnapshot.Entries, entry =>
                entry.Id != SoulTapeCatalog.StarterTapeId &&
                !entry.IsDiscovered);

            SoulTapeAssignmentSnapshot merged = Assign(
                "mode-profile-merge",
                SoulTapeMusicMode.MergeBuiltInAndUser,
                catalog,
                anchors);
            SoulTapeCollection mergedCollection = Collection(catalog);
            Assert.True(mergedCollection.BindProfile("mode-profile-merge"));
            SoulTapeCollectionSnapshot mergedSnapshot = Projection(
                catalog, mergedCollection, merged);
            Assert.Equal(3, mergedSnapshot.TotalCount);
        }

        [Fact]
        [Trait("Validation", "DynamicCollection")]
        public void DiscoveredUserTrackSurvivesAssignmentChanges()
        {
            MusicTrack rhyeTrack = WriteTrack(
                _user, "Rhye - Waste.mp3", 71);
            MusicTrack replacementTrack = WriteTrack(
                _user, "Replacement - Track.mp3", 72);
            SoulTapeCatalog catalog = Catalog(rhyeTrack, replacementTrack);
            SoulTapeCatalogEntry rhye = catalog.GetAllTapes()
                .Single(tape => tape.Track == rhyeTrack);
            SoulTapeCollection collection = Collection(catalog);
            Assert.True(collection.BindProfile("history-profile"));
            Assert.True(collection.UnlockTape(rhye.Id));

            SoulTapeAssignmentSnapshot changed = Assign(
                "history-profile-changed",
                SoulTapeMusicMode.UserOnly,
                catalog,
                new[] { Anchor("new-anchor") });
            SoulTapeCollectionSnapshot snapshot = Projection(
                catalog, collection, Reprofile(changed, "history-profile"));

            Assert.Contains(snapshot.Entries, entry =>
                entry.Id == rhye.Id && entry.IsDiscovered);
        }

        [Fact]
        [Trait("Validation", "DynamicCollection")]
        public void MissingDiscoveredUserFileKeepsHistoricalMetadata()
        {
            MusicTrack track = WriteTrack(
                _user, "Missing Artist - Missing Song.mp3", 81);
            SoulTapeCatalog catalog = Catalog(track);
            SoulTapeCatalogEntry tape = catalog.GetAllTapes()
                .Single(entry => entry.Track == track);
            SoulTapeCollection collection = Collection(catalog);
            Assert.True(collection.BindProfile("missing-profile"));
            Assert.True(collection.UnlockTape(tape.Id));
            File.Delete(track.FilePath);

            SoulTapeCatalog restartedCatalog = Catalog();
            SoulTapeCollection restartedCollection = Collection(restartedCatalog);
            Assert.True(restartedCollection.BindProfile("missing-profile"));
            SoulTapeAssignmentSnapshot empty = Snapshot(
                "missing-profile", new Dictionary<string, string>());
            SoulTapeCollectionSnapshot snapshot = Projection(
                restartedCatalog, restartedCollection, empty);
            SoulTapeCollectionCard card = snapshot.Entries.Single(entry =>
                entry.Id == tape.Id);

            Assert.True(card.IsDiscovered);
            Assert.False(card.IsAudioAvailable);
            Assert.Equal("Missing Artist", card.Artist);
            Assert.Equal("Missing Song", card.Title);
        }

        [Fact]
        [Trait("Validation", "DynamicCollection")]
        public void EffectiveCatalogDeduplicatesAssignedAndHistoricalIds()
        {
            MusicTrack track = WriteTrack(
                _user, "Duplicate Artist - Duplicate Song.mp3", 91);
            SoulTapeCatalog catalog = Catalog(track);
            SoulTapeCatalogEntry tape = catalog.GetAllTapes()
                .Single(entry => entry.Track == track);
            SoulTapeCollection collection = Collection(catalog);
            Assert.True(collection.BindProfile("dedupe-profile"));
            Assert.True(collection.UnlockTape(tape.Id));
            SoulTapeAssignmentSnapshot assignments = Snapshot(
                "dedupe-profile",
                new Dictionary<string, string>
                {
                    { "a", tape.Id },
                    { "b", tape.Id }
                });

            SoulTapeCollectionSnapshot snapshot = Projection(
                catalog, collection, assignments);

            Assert.Equal(1, snapshot.Entries.Count(entry => entry.Id == tape.Id));
        }

        [Fact]
        [Trait("Validation", "DynamicCollection")]
        public void BuiltInStarterDiscoveryRemainsIntact()
        {
            SoulTapeCatalog catalog = Catalog();
            SoulTapeCollection collection = Collection(catalog);
            Assert.True(collection.BindProfile("starter-profile"));
            SoulTapeCollectionSnapshot snapshot = Projection(
                catalog,
                collection,
                Snapshot("starter-profile", new Dictionary<string, string>()));

            SoulTapeCollectionCard starter = snapshot.Entries.Single(entry =>
                entry.Id == SoulTapeCatalog.StarterTapeId);
            Assert.True(starter.IsDiscovered);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private SoulTapeCatalog Catalog(params MusicTrack[] tracks)
        {
            SoulTapeCatalog catalog = new SoulTapeCatalog(_log);
            catalog.Refresh(tracks);
            return catalog;
        }

        private SoulTapeCollection Collection(ISoulTapeCatalog catalog)
        {
            return new SoulTapeCollection(
                catalog,
                new JsonSoulTapeCollectionStore(_collections),
                _log);
        }

        private SoulTapeAssignmentSnapshot Assign(
            string profile,
            SoulTapeMusicMode mode,
            SoulTapeCatalog catalog,
            IEnumerable<SoulTapeSpawnAnchor> anchors)
        {
            return new SoulTapeAssignmentService(
                    new JsonSoulTapeAssignmentStore(_assignments), _log)
                .Refresh(
                    profile,
                    mode,
                    catalog.GetAllTapes(),
                    anchors,
                    _builtIn,
                    new[] { _user })
                .Snapshot;
        }

        private static SoulTapeCollectionSnapshot Projection(
            ISoulTapeCatalog catalog,
            SoulTapeCollection collection,
            SoulTapeAssignmentSnapshot assignments)
        {
            return new SoulTapeCollectionProjection(
                    catalog,
                    collection,
                    new StaticAssignmentProvider(assignments))
                .Project(SoulTapeCollectionFilter.All);
        }

        private MusicTrack WriteTrack(string folder, string name, int seed)
        {
            string path = Path.Combine(folder, name);
            File.WriteAllBytes(path, new[]
            {
                (byte)seed, (byte)(seed + 1), (byte)(seed + 2), (byte)(seed + 3)
            });
            return new MusicTrack(path, 4);
        }

        private static SoulTapeSpawnAnchor Anchor(string id)
        {
            return new SoulTapeSpawnAnchor
            {
                Id = id,
                MapId = "factory4_day",
                Position = new SoulTapeVector3(1f, 2f, 3f),
                RotationEuler = new SoulTapeVector3(0f, 90f, 0f),
                SurfaceNormal = new SoulTapeVector3(0f, 1f, 0f),
                Source = SoulTapeSpawnAnchorSource.Curated,
                Enabled = true
            };
        }

        private static SoulTapeAssignmentSnapshot Snapshot(
            string profile,
            Dictionary<string, string> assignments)
        {
            return new SoulTapeAssignmentSnapshot(new SoulTapeAssignmentData
            {
                ProfileId = profile,
                MusicMode = SoulTapeMusicMode.MergeBuiltInAndUser,
                Assignments = assignments
            });
        }

        private static SoulTapeAssignmentSnapshot Reprofile(
            SoulTapeAssignmentSnapshot snapshot,
            string profile)
        {
            return Snapshot(
                profile,
                snapshot.Assignments.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal));
        }

        private sealed class StaticAssignmentProvider :
            ISoulTapeAssignmentSnapshotProvider
        {
            internal StaticAssignmentProvider(SoulTapeAssignmentSnapshot snapshot)
            {
                CurrentAssignments = snapshot;
            }

            public SoulTapeAssignmentSnapshot CurrentAssignments { get; private set; }
            public event Action AssignmentChanged
            {
                add { }
                remove { }
            }
        }
    }
}
