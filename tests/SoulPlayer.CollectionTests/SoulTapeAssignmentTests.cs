using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SoulPlayer.Cassettes;
using SoulPlayer.Library;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class SoulTapeAssignmentTests : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "SoulTapeAssignments-" + Guid.NewGuid().ToString("N"));
        private readonly string _builtIn;
        private readonly string _user;
        private readonly OfflineTestLog _log = new OfflineTestLog();

        public SoulTapeAssignmentTests()
        {
            _builtIn = Path.Combine(_root, "DefaultMusic");
            _user = Path.Combine(_root, "UserMusic");
            Directory.CreateDirectory(_builtIn);
            Directory.CreateDirectory(_user);
        }

        [Fact]
        [Trait("Validation", "Assignments")]
        public void SameInputsProduceIdenticalMapping()
        {
            InMemoryAssignmentStore store = new InMemoryAssignmentStore();
            SoulTapeAssignmentService service = Service(store);
            SoulTapeAssignmentSnapshot first = Refresh(
                service, "same-profile", Tracks(6), Anchors(6)).Snapshot;
            SoulTapeAssignmentSnapshot second = Refresh(
                service, "same-profile", Tracks(6), Anchors(6)).Snapshot;
            Assert.Equal(Signature(first), Signature(second));
        }

        [Fact]
        [Trait("Validation", "Assignments")]
        public void DifferentProfilesUseIndependentSeeds()
        {
            SoulTapeAssignmentService service = Service(new InMemoryAssignmentStore());
            Assert.NotEqual(
                Signature(Refresh(service, "profile-a", Tracks(10), Anchors(10)).Snapshot),
                Signature(Refresh(service, "profile-b", Tracks(10), Anchors(10)).Snapshot));
        }

        [Fact]
        [Trait("Validation", "Assignments")]
        public void LibraryEnumerationOrderDoesNotChangeMapping()
        {
            List<SoulTapeCatalogEntry> tracks = Tracks(8);
            SoulTapeAssignmentService service = Service(new InMemoryAssignmentStore());
            string first = Signature(Refresh(
                service, "order-profile", tracks, Anchors(8)).Snapshot);
            tracks.Reverse();
            string second = Signature(Refresh(
                service, "order-profile", tracks,
                Anchors(8).AsEnumerable().Reverse()).Snapshot);
            Assert.Equal(first, second);
        }

        [Fact]
        [Trait("Validation", "Assignments")]
        public void FewerTracksThanAnchorsNeverDuplicatesTracks()
        {
            SoulTapeAssignmentSnapshot result = Refresh(
                Service(new InMemoryAssignmentStore()),
                "few-tracks", Tracks(3), Anchors(8)).Snapshot;
            Assert.Equal(3, result.Assignments.Count);
            Assert.Equal(3, result.Assignments.Values.Distinct().Count());
        }

        [Fact]
        [Trait("Validation", "Assignments")]
        public void MoreTracksThanAnchorsUsesDeterministicSubset()
        {
            SoulTapeAssignmentService service = Service(new InMemoryAssignmentStore());
            string first = Signature(Refresh(
                service, "subset", Tracks(12), Anchors(4)).Snapshot);
            string second = Signature(Refresh(
                service, "subset", Tracks(12), Anchors(4)).Snapshot);
            Assert.Equal(first, second);
            Assert.Equal(4, first.Split('|').Length);
        }

        [Fact]
        [Trait("Validation", "Assignments")]
        public void AddingTrackPreservesExistingAssignments()
        {
            SoulTapeAssignmentService service = Service(new InMemoryAssignmentStore());
            List<SoulTapeCatalogEntry> tracks = Tracks(4);
            SoulTapeAssignmentSnapshot before = Refresh(
                service, "add-track", tracks, Anchors(4)).Snapshot;
            tracks.Add(Track("added-track", _user, 99));
            SoulTapeAssignmentSnapshot after = Refresh(
                service, "add-track", tracks, Anchors(4)).Snapshot;
            AssertPreserved(before, after);
        }

        [Fact]
        [Trait("Validation", "Assignments")]
        public void RemovingTrackPreservesUnaffectedAssignments()
        {
            SoulTapeAssignmentService service = Service(new InMemoryAssignmentStore());
            List<SoulTapeCatalogEntry> tracks = Tracks(6);
            SoulTapeAssignmentSnapshot before = Refresh(
                service, "remove-track", tracks, Anchors(4)).Snapshot;
            string removedId = before.Assignments.First().Value;
            tracks.RemoveAll(track => track.Id == removedId);
            SoulTapeAssignmentSnapshot after = Refresh(
                service, "remove-track", tracks, Anchors(4)).Snapshot;
            foreach (KeyValuePair<string, string> pair in before.Assignments
                         .Where(pair => pair.Value != removedId))
            {
                Assert.Equal(pair.Value, after.Assignments[pair.Key]);
            }
            Assert.DoesNotContain(removedId, after.Assignments.Values);
        }

        [Fact]
        [Trait("Validation", "Assignments")]
        public void AddingAnchorPreservesExistingAssignments()
        {
            SoulTapeAssignmentService service = Service(new InMemoryAssignmentStore());
            List<SoulTapeSpawnAnchor> anchors = Anchors(3);
            SoulTapeAssignmentSnapshot before = Refresh(
                service, "add-anchor", Tracks(6), anchors).Snapshot;
            anchors.Add(Anchor("anchor-added"));
            SoulTapeAssignmentSnapshot after = Refresh(
                service, "add-anchor", Tracks(6), anchors).Snapshot;
            AssertPreserved(before, after);
            Assert.True(after.Assignments.ContainsKey("anchor-added"));
        }

        [Fact]
        [Trait("Validation", "Assignments")]
        public void DisablingAnchorPreservesUnaffectedAssignments()
        {
            SoulTapeAssignmentService service = Service(new InMemoryAssignmentStore());
            List<SoulTapeSpawnAnchor> anchors = Anchors(5);
            SoulTapeAssignmentSnapshot before = Refresh(
                service, "disable-anchor", Tracks(5), anchors).Snapshot;
            string disabled = before.Assignments.First().Key;
            anchors.Single(anchor => anchor.Id == disabled).Enabled = false;
            SoulTapeAssignmentSnapshot after = Refresh(
                service, "disable-anchor", Tracks(5), anchors).Snapshot;
            Assert.False(after.Assignments.ContainsKey(disabled));
            foreach (KeyValuePair<string, string> pair in before.Assignments
                         .Where(pair => pair.Key != disabled))
            {
                Assert.Equal(pair.Value, after.Assignments[pair.Key]);
            }
        }

        [Fact]
        [Trait("Validation", "Assignments")]
        public void BuiltInOnlySelectsBuiltInTracks()
        {
            Assert.Equal(new[] { "built" }, SelectIds(
                SoulTapeMusicMode.BuiltInOnly,
                new[] { Track("built", _builtIn, 1), Track("user", _user, 2) }));
        }

        [Fact]
        [Trait("Validation", "Assignments")]
        public void UserOnlySelectsUserTracks()
        {
            Assert.Equal(new[] { "user" }, SelectIds(
                SoulTapeMusicMode.UserOnly,
                new[] { Track("built", _builtIn, 3), Track("user", _user, 4) }));
        }

        [Fact]
        [Trait("Validation", "Assignments")]
        public void MergeBuiltInAndUserSelectsBothPools()
        {
            Assert.Equal(new[] { "built", "user" }, SelectIds(
                SoulTapeMusicMode.MergeBuiltInAndUser,
                new[] { Track("built", _builtIn, 5), Track("user", _user, 6) }));
        }

        [Fact]
        [Trait("Validation", "Assignments")]
        public void MergeDeduplicatesSameStableTrackIdentity()
        {
            Assert.Single(SoulTapeAssignmentService.SelectEligibleTracks(
                new[]
                {
                    Track("same-id", _builtIn, 7),
                    Track("same-id", _user, 8)
                },
                SoulTapeMusicMode.MergeBuiltInAndUser,
                _builtIn,
                new[] { _user }));
        }

        [Fact]
        [Trait("Validation", "Assignments")]
        public void CorruptPrimaryRecoversValidBackup()
        {
            string folder = Path.Combine(_root, "assignments");
            JsonSoulTapeAssignmentStore store = new JsonSoulTapeAssignmentStore(folder);
            SoulTapeAssignmentService service = Service(store);
            SoulTapeAssignmentSnapshot expected = Refresh(
                service, "recover-profile", Tracks(4), Anchors(4)).Snapshot;
            string path = Path.Combine(folder, "recover-profile.json");
            File.Copy(path, path + ".bak", true);
            File.WriteAllText(path, "{ broken json");

            SoulTapeAssignmentSnapshot recovered = Refresh(
                service, "recover-profile", Tracks(4), Anchors(4)).Snapshot;

            Assert.Equal(Signature(expected), Signature(recovered));
            Assert.Contains(_log.Warnings, message =>
                message.Contains("recovered from backup"));
        }

        [Fact]
        [Trait("Validation", "Assignments")]
        public void AssignmentRefreshDoesNotAlterCollectionOwnership()
        {
            List<SoulTapeCatalogEntry> tracks = Tracks(3);
            TestCatalog catalog = new TestCatalog(tracks.Concat(new[]
            {
                Entry(SoulTapeCatalog.StarterTapeId, _builtIn, 42)
            }));
            SoulTapeCollection collection = new SoulTapeCollection(
                catalog, new InMemoryCollectionStore(), _log);
            Assert.True(collection.BindProfile("ownership-profile"));
            Assert.True(collection.UnlockTape(tracks[0].Id));

            Refresh(Service(new InMemoryAssignmentStore()),
                "ownership-profile", tracks, Anchors(3));

            Assert.True(collection.IsUnlocked(tracks[0].Id));
            Assert.True(collection.IsUnlocked(SoulTapeCatalog.StarterTapeId));
        }

        [Fact]
        [Trait("Validation", "Assignments")]
        public void ZeroTracksProducesZeroAssignments()
        {
            SoulTapeAssignmentRefreshResult result = Refresh(
                Service(new InMemoryAssignmentStore()),
                "zero-tracks", new SoulTapeCatalogEntry[0], Anchors(3));
            Assert.Empty(result.Snapshot.Assignments);
        }

        [Fact]
        [Trait("Validation", "Assignments")]
        public void ZeroAnchorsProducesZeroAssignments()
        {
            SoulTapeAssignmentRefreshResult result = Refresh(
                Service(new InMemoryAssignmentStore()),
                "zero-anchors", Tracks(3), new SoulTapeSpawnAnchor[0]);
            Assert.Empty(result.Snapshot.Assignments);
        }

        [Fact]
        [Trait("Validation", "Assignments")]
        public void WorldSpawnUsesPersistedAnchorTrackPair()
        {
            SoulTapeCatalogEntry assignedTrack = Track("assigned-track", _user, 54);
            SoulTapeSpawnAnchor assignedAnchor = Anchor("assigned-anchor");
            SoulTapeAssignmentSnapshot snapshot = Refresh(
                Service(new InMemoryAssignmentStore()),
                "world-profile",
                new[] { assignedTrack },
                new[] { assignedAnchor }).Snapshot;
            TestCatalog catalog = new TestCatalog(new[]
            {
                assignedTrack,
                Entry(SoulTapeCatalog.StarterTapeId, _builtIn, 55)
            });
            SoulTapeCollection collection = new SoulTapeCollection(
                catalog, new InMemoryCollectionStore(), _log);
            Assert.True(collection.BindProfile("world-profile"));

            IReadOnlyList<SoulTapeSpawnPlanEntry> eligible =
                SoulTapeWorldEligibility.GetAssignedTapes(
                    snapshot, new[] { assignedAnchor }, catalog, collection);
            SoulTapeSpawnPlan plan = new SoulTapeSpawnPlanner().CreatePlan(
                "factory4_day", eligible, 123);

            Assert.Single(plan.Entries);
            Assert.Equal("assigned-anchor", plan.Entries[0].Anchor.Id);
            Assert.Equal("assigned-track", plan.Entries[0].Cassette.Id);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private SoulTapeAssignmentRefreshResult Refresh(
            SoulTapeAssignmentService service,
            string profile,
            IEnumerable<SoulTapeCatalogEntry> tracks,
            IEnumerable<SoulTapeSpawnAnchor> anchors)
        {
            return service.Refresh(
                profile,
                SoulTapeMusicMode.MergeBuiltInAndUser,
                tracks,
                anchors,
                _builtIn,
                new[] { _user });
        }

        private SoulTapeAssignmentService Service(ISoulTapeAssignmentStore store)
        {
            return new SoulTapeAssignmentService(store, _log);
        }

        private List<SoulTapeCatalogEntry> Tracks(int count)
        {
            return Enumerable.Range(0, count)
                .Select(index => Track("track-" + index, _user, index + 10))
                .ToList();
        }

        private SoulTapeCatalogEntry Track(string id, string folder, int seed)
        {
            return Entry(id, folder, seed);
        }

        private SoulTapeCatalogEntry Entry(string id, string folder, int seed)
        {
            string path = Path.Combine(folder, id + "-" + seed + ".mp3");
            File.WriteAllBytes(path, new[]
            {
                (byte)seed, (byte)(seed + 1), (byte)(seed + 2)
            });
            MusicTrack track = new MusicTrack(path, 3);
            return new SoulTapeCatalogEntry(
                id, "Artist", id, "test:" + id,
                SoulTapeRarity.Common, null, track);
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

        private static List<SoulTapeSpawnAnchor> Anchors(int count)
        {
            return Enumerable.Range(0, count)
                .Select(index => Anchor("anchor-" + index))
                .ToList();
        }

        private IEnumerable<string> SelectIds(
            SoulTapeMusicMode mode,
            IEnumerable<SoulTapeCatalogEntry> tracks)
        {
            return SoulTapeAssignmentService.SelectEligibleTracks(
                    tracks, mode, _builtIn, new[] { _user })
                .Select(track => track.Id);
        }

        private static string Signature(SoulTapeAssignmentSnapshot snapshot)
        {
            return string.Join("|", snapshot.Assignments
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + "=" + pair.Value));
        }

        private static void AssertPreserved(
            SoulTapeAssignmentSnapshot before,
            SoulTapeAssignmentSnapshot after)
        {
            foreach (KeyValuePair<string, string> pair in before.Assignments)
            {
                Assert.Equal(pair.Value, after.Assignments[pair.Key]);
            }
        }

        private sealed class InMemoryAssignmentStore : ISoulTapeAssignmentStore
        {
            private readonly Dictionary<string, SoulTapeAssignmentData> _data =
                new Dictionary<string, SoulTapeAssignmentData>(StringComparer.Ordinal);

            public SoulTapeAssignmentLoadResult Load(string profileId)
            {
                SoulTapeAssignmentData data;
                return _data.TryGetValue(profileId, out data)
                    ? new SoulTapeAssignmentLoadResult(
                        SoulTapeAssignmentLoadStatus.Loaded,
                        Clone(data), string.Empty)
                    : new SoulTapeAssignmentLoadResult(
                        SoulTapeAssignmentLoadStatus.Missing,
                        null, string.Empty);
            }

            public void Save(string profileId, SoulTapeAssignmentData data)
            {
                _data[profileId] = Clone(data);
            }

            public string Describe(string profileId)
            {
                return "memory:" + profileId;
            }

            private static SoulTapeAssignmentData Clone(SoulTapeAssignmentData data)
            {
                return JsonConvert.DeserializeObject<SoulTapeAssignmentData>(
                    JsonConvert.SerializeObject(data));
            }
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
    }
}
