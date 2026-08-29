using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoulPlayer.Cassettes;
using SoulPlayer.Library;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class SoulTapePerRaidSelectionTests : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "SoulTapePerRaid-" + Guid.NewGuid().ToString("N"));
        private readonly string _builtIn;
        private readonly string _user;
        private int _fileNumber;

        public SoulTapePerRaidSelectionTests()
        {
            _builtIn = Path.Combine(_root, "DefaultMusic");
            _user = Path.Combine(_root, "UserMusic");
            Directory.CreateDirectory(_builtIn);
            Directory.CreateDirectory(_user);
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void SameRaidIdentityProducesStableMapping()
        {
            SoulTapeSpawnPlanner planner = new SoulTapeSpawnPlanner();
            SoulTapeRaidSelectionResult first = Plan(
                planner, "raid-stable", Tracks(12), new string[0], Anchors(8), 3, 3);
            SoulTapeRaidSelectionResult second = Plan(
                planner, "raid-stable", Tracks(12), new string[0], Anchors(8), 3, 3);

            Assert.Equal(Signature(first.Plan), Signature(second.Plan));
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void DifferentRaidIdentitiesCanProduceDifferentMappings()
        {
            SoulTapeSpawnPlanner planner = new SoulTapeSpawnPlanner();
            List<SoulTapeCatalogEntry> tracks = Tracks(20);
            List<SoulTapeSpawnAnchor> anchors = Anchors(10);
            int distinct = Enumerable.Range(0, 8)
                .Select(index => Signature(Plan(
                    planner,
                    "raid-" + index,
                    tracks,
                    new string[0],
                    anchors,
                    3,
                    3).Plan))
                .Distinct(StringComparer.Ordinal)
                .Count();

            Assert.True(distinct > 1);
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void UndiscoveredTracksAreSelectedBeforeDiscoveredTracks()
        {
            List<SoulTapeCatalogEntry> tracks = Tracks(5);
            string[] discovered = tracks.Take(3).Select(tape => tape.Id).ToArray();
            SoulTapeRaidSelectionResult result = Plan(
                new SoulTapeSpawnPlanner(),
                "priority",
                tracks,
                discovered,
                Anchors(5),
                2,
                2);

            Assert.Equal(2, result.UndiscoveredCount);
            Assert.All(result.Plan.Entries, entry =>
                Assert.DoesNotContain(entry.Cassette.Id, discovered));
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void OneRaidNeverDuplicatesTracksWhenEnoughExist()
        {
            SoulTapeSpawnPlan plan = Plan(
                new SoulTapeSpawnPlanner(), "unique-tracks", Tracks(8),
                new string[0], Anchors(8), 3, 3).Plan;
            Assert.Equal(3, plan.Entries.Count);
            Assert.Equal(3, plan.Entries.Select(entry => entry.Cassette.Id).Distinct().Count());
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void OneRaidNeverDuplicatesAnchorsWhenEnoughExist()
        {
            SoulTapeSpawnPlan plan = Plan(
                new SoulTapeSpawnPlanner(), "unique-anchors", Tracks(8),
                new string[0], Anchors(8), 3, 3).Plan;
            Assert.Equal(3, plan.Entries.Select(entry => entry.Anchor.Id).Distinct().Count());
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void MoreTracksThanAnchorsUsesOnlyAvailableAnchors()
        {
            SoulTapeRaidSelectionResult result = Plan(
                new SoulTapeSpawnPlanner(), "track-heavy", Tracks(20),
                new string[0], Anchors(2), 3, 3);
            Assert.Equal(20, result.EligibleCount);
            Assert.Equal(2, result.Plan.Entries.Count);
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void HundredsOfTracksAreNotCappedByPersistentAnchorCount()
        {
            SoulTapeSpawnPlanner planner = new SoulTapeSpawnPlanner();
            List<SoulTapeCatalogEntry> tracks = Tracks(300);
            List<SoulTapeSpawnAnchor> anchors = Anchors(2);
            HashSet<string> encountered = new HashSet<string>(StringComparer.Ordinal);
            for (int raid = 0; raid < 30; raid++)
            {
                foreach (SoulTapeSpawnPlanEntry entry in Plan(
                             planner,
                             "large-library-raid-" + raid,
                             tracks,
                             encountered,
                             anchors,
                             2,
                             2).Plan.Entries)
                {
                    encountered.Add(entry.Cassette.Id);
                }
            }

            Assert.True(encountered.Count > anchors.Count);
            Assert.Equal(60, encountered.Count);
        }

        [Fact]
        [Trait("Validation", "Persistence")]
        public void PickupDiscoveryRemainsPermanentAcrossRestart()
        {
            SoulTapeCatalogEntry tape = Track("permanent", _user);
            TestCatalog catalog = Catalog(tape);
            string folder = Path.Combine(_root, "collections");
            SoulTapeCollection first = Collection(
                catalog, new JsonSoulTapeCollectionStore(folder));
            Assert.True(first.BindProfile("permanent-profile"));
            Assert.Equal(
                SoulTapeDiscoveryResult.NewUnlock,
                new SoulTapeDiscoveryService(
                    catalog, first, new OfflineTestLog()).Discover(tape.Id));

            SoulTapeCollection restarted = Collection(
                catalog, new JsonSoulTapeCollectionStore(folder));
            Assert.True(restarted.BindProfile("permanent-profile"));
            Assert.True(restarted.IsUnlocked(tape.Id));
        }

        [Fact]
        [Trait("Validation", "Persistence")]
        public void DiscoveryRemainsAfterRaidMappingIsDiscarded()
        {
            SoulTapeCatalogEntry tape = Track("mapping-discarded", _user);
            TestCatalog catalog = Catalog(tape);
            SoulTapeCollection collection = Collection(
                catalog, new InMemoryCollectionStore());
            Assert.True(collection.BindProfile("mapping-profile"));
            SoulTapeSpawnPlan raid = Plan(
                new SoulTapeSpawnPlanner(), "old-raid", new[] { tape },
                new string[0], Anchors(1), 1, 1).Plan;
            Assert.Single(raid.Entries);
            Assert.True(collection.UnlockTape(raid.Entries[0].Cassette.Id));

            raid = null;
            Assert.True(collection.IsUnlocked(tape.Id));
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void UserOnlyModeSelectsOnlyUserTracks()
        {
            Assert.Equal(new[] { "user" }, ModeIds(
                SoulTapeMusicMode.UserOnly,
                Track("built", _builtIn), Track("user", _user)));
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void BuiltInOnlyModeSelectsOnlyBuiltInTracks()
        {
            Assert.Equal(new[] { "built" }, ModeIds(
                SoulTapeMusicMode.BuiltInOnly,
                Track("built", _builtIn), Track("user", _user)));
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void MergeModeSelectsBuiltInAndUserTracks()
        {
            Assert.Equal(new[] { "built", "user" }, ModeIds(
                SoulTapeMusicMode.MergeBuiltInAndUser,
                Track("built", _builtIn), Track("user", _user)));
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void ZeroEligibleTracksSpawnsZero()
        {
            SoulTapeRaidSelectionResult result = Plan(
                new SoulTapeSpawnPlanner(), "empty", new SoulTapeCatalogEntry[0],
                new string[0], Anchors(3), 1, 3);
            Assert.Equal(0, result.EligibleCount);
            Assert.Empty(result.Plan.Entries);
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void FewerTracksThanRequestedLimitsSpawnCount()
        {
            Assert.Single(Plan(
                new SoulTapeSpawnPlanner(), "one-track", Tracks(1),
                new string[0], Anchors(3), 3, 3).Plan.Entries);
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void FewerAnchorsThanRequestedLimitsSpawnCount()
        {
            Assert.Single(Plan(
                new SoulTapeSpawnPlanner(), "one-anchor", Tracks(3),
                new string[0], Anchors(1), 3, 3).Plan.Entries);
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void StaleLegacyAssignmentDoesNotForceRaidMapping()
        {
            string legacyFolder = Path.Combine(_root, "assignments");
            JsonSoulTapeAssignmentStore legacyStore =
                new JsonSoulTapeAssignmentStore(legacyFolder);
            legacyStore.Save("legacy-profile", new SoulTapeAssignmentData
            {
                ProfileId = "legacy-profile",
                Assignments = new Dictionary<string, string>
                {
                    { "anchor-0", "obsolete-track" }
                }
            });
            SoulTapeCatalogEntry current = Track("current-track", _user);

            SoulTapeSpawnPlan plan = Plan(
                new SoulTapeSpawnPlanner(), "new-raid", new[] { current },
                new string[0], Anchors(1), 1, 1).Plan;

            Assert.Single(plan.Entries);
            Assert.Equal("current-track", plan.Entries[0].Cassette.Id);
            Assert.NotEqual("obsolete-track", plan.Entries[0].Cassette.Id);
            Assert.True(File.Exists(Path.Combine(legacyFolder, "legacy-profile.json")));
        }

        [Fact]
        [Trait("Validation", "DynamicCollection")]
        public void DynamicUserCassetteWithoutRarityRemainsVisibleInCollection()
        {
            SoulTapeCatalogEntry userTape = Track("dynamic-user", _user, null);
            TestCatalog catalog = Catalog(userTape);
            SoulTapeCollection collection = Collection(
                catalog, new InMemoryCollectionStore());
            Assert.True(collection.BindProfile("dynamic-profile"));
            SoulTapeEligibleCatalogSnapshot eligible =
                new SoulTapeEligibleCatalogSnapshot(
                    "dynamic-profile",
                    SoulTapeMusicMode.UserOnly,
                    new[] { userTape.Id });

            SoulTapeCollectionSnapshot snapshot =
                new SoulTapeCollectionProjection(
                    catalog,
                    collection,
                    new StaticEligibleProvider(eligible))
                .Project(SoulTapeCollectionFilter.All);

            Assert.Contains(snapshot.Entries, card =>
                card.Id == userTape.Id && !card.IsDiscovered);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private SoulTapeRaidSelectionResult Plan(
            SoulTapeSpawnPlanner planner,
            string raidIdentity,
            IEnumerable<SoulTapeCatalogEntry> tracks,
            IEnumerable<string> discovered,
            IEnumerable<SoulTapeSpawnAnchor> anchors,
            int minimum,
            int maximum)
        {
            return planner.CreateRaidSelection(
                "profile", "factory4_day", raidIdentity,
                tracks, discovered, anchors, minimum, maximum);
        }

        private IEnumerable<string> ModeIds(
            SoulTapeMusicMode mode,
            params SoulTapeCatalogEntry[] tracks)
        {
            return SoulTapeTrackEligibility.SelectEligibleTracks(
                    tracks, mode, _builtIn, new[] { _user })
                .Select(tape => tape.Id);
        }

        private List<SoulTapeCatalogEntry> Tracks(int count)
        {
            return Enumerable.Range(0, count)
                .Select(index => Track("track-" + index, _user))
                .ToList();
        }

        private SoulTapeCatalogEntry Track(
            string id,
            string folder,
            SoulTapeRarity? rarity = SoulTapeRarity.Common)
        {
            string path = Path.Combine(
                folder,
                id + "-" + (_fileNumber++) + ".mp3");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
            return new SoulTapeCatalogEntry(
                id,
                "Artist",
                id,
                "test:" + id,
                rarity,
                null,
                new MusicTrack(path, 4));
        }

        private TestCatalog Catalog(params SoulTapeCatalogEntry[] entries)
        {
            return new TestCatalog(new[]
            {
                Track(SoulTapeCatalog.StarterTapeId, _builtIn)
            }.Concat(entries));
        }

        private static List<SoulTapeSpawnAnchor> Anchors(int count)
        {
            return Enumerable.Range(0, count)
                .Select(index => new SoulTapeSpawnAnchor
                {
                    Id = "anchor-" + index,
                    MapId = "factory4_day",
                    Position = new SoulTapeVector3(index, 1f, 2f),
                    RotationEuler = new SoulTapeVector3(0f, 90f, 0f),
                    SurfaceNormal = new SoulTapeVector3(0f, 1f, 0f),
                    Source = SoulTapeSpawnAnchorSource.Curated,
                    Enabled = true
                })
                .ToList();
        }

        private static string Signature(SoulTapeSpawnPlan plan)
        {
            return string.Join("|", plan.Entries.Select(entry =>
                entry.Anchor.Id + "=" + entry.Cassette.Id));
        }

        private static SoulTapeCollection Collection(
            ISoulTapeCatalog catalog,
            ISoulTapeCollectionStore store)
        {
            return new SoulTapeCollection(catalog, store, new OfflineTestLog());
        }

        private sealed class TestCatalog : ISoulTapeCatalog
        {
            private readonly Dictionary<string, SoulTapeCatalogEntry> _entries;

            internal TestCatalog(IEnumerable<SoulTapeCatalogEntry> entries)
            {
                _entries = entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
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

        private sealed class StaticEligibleProvider :
            ISoulTapeEligibleCatalogProvider
        {
            internal StaticEligibleProvider(SoulTapeEligibleCatalogSnapshot snapshot)
            {
                CurrentEligibleCatalog = snapshot;
            }

            public SoulTapeEligibleCatalogSnapshot CurrentEligibleCatalog
            {
                get;
                private set;
            }

            public event Action EligibleCatalogChanged
            {
                add { }
                remove { }
            }
        }
    }
}
