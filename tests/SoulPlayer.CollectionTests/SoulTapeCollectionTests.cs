using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoulPlayer.Cassettes;
using SoulPlayer.Library;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class SoulTapeCollectionTests
    {
        [Fact]
        [Trait("Validation", "PersistenceRecovery")]
        public void NewProfileGetsStarterCassetteAndPersistsIt()
        {
            FakeCatalog catalog = CreateCatalog();
            FakeStore store = new FakeStore(SoulTapeLoadStatus.Missing, null);
            SoulTapeCollection collection = CreateCollection(catalog, store);

            Assert.True(collection.BindProfile("profile-a"));
            Assert.True(collection.IsUnlocked(SoulTapeCatalog.StarterTapeId));
            Assert.Contains(SoulTapeCatalog.StarterTapeId, store.LastSaved.UnlockedCassetteIds);
            Assert.Empty(store.LastSaved.FavoriteCassetteIds);
        }

        [Fact]
        [Trait("Validation", "PersistenceRecovery")]
        public void UnlockAndFavoriteApisPersistChanges()
        {
            FakeCatalog catalog = CreateCatalog();
            FakeStore store = new FakeStore(SoulTapeLoadStatus.Missing, null);
            SoulTapeCollection collection = CreateCollection(catalog, store);
            collection.BindProfile("profile-b");

            Assert.True(collection.UnlockTape("soul-tape.test.extra"));
            Assert.True(collection.IsUnlocked("soul-tape.test.extra"));
            Assert.True(collection.SetFavorite("soul-tape.test.extra", true));
            Assert.True(collection.IsFavorite("soul-tape.test.extra"));
            Assert.Contains(
                collection.GetFavoriteTapes(),
                tape => tape.Id == "soul-tape.test.extra");

            Assert.True(collection.SetFavorite("soul-tape.test.extra", false));
            Assert.False(collection.IsFavorite("soul-tape.test.extra"));
        }

        [Fact]
        [Trait("Validation", "PersistenceRecovery")]
        public void MissingCatalogEntryDoesNotEraseProgression()
        {
            SoulTapeCollectionData saved = new SoulTapeCollectionData
            {
                ProfileId = "profile-c",
                UnlockedCassetteIds = new List<string> { "soul-tape.missing.audio" },
                FavoriteCassetteIds = new List<string> { "soul-tape.missing.audio" }
            };
            FakeStore store = new FakeStore(SoulTapeLoadStatus.Loaded, saved);
            SoulTapeCollection collection = CreateCollection(CreateCatalog(), store);

            Assert.True(collection.BindProfile("profile-c"));
            Assert.True(collection.IsUnlocked("soul-tape.missing.audio"));
            Assert.True(collection.IsFavorite("soul-tape.missing.audio"));
            Assert.Empty(collection.GetUnlockedTapes());
            Assert.Null(store.LastSaved);
        }

        [Fact]
        [Trait("Validation", "PersistenceRecovery")]
        public void SaveFailureRollsBackUnlockAndFavoriteChanges()
        {
            FakeCatalog catalog = CreateCatalog();
            FakeStore store = new FakeStore(SoulTapeLoadStatus.Missing, null);
            SoulTapeCollection collection = CreateCollection(catalog, store);
            collection.BindProfile("profile-d");

            store.FailSaves = true;
            Assert.False(collection.UnlockTape("soul-tape.test.extra"));
            Assert.False(collection.IsUnlocked("soul-tape.test.extra"));

            Assert.False(collection.SetFavorite(SoulTapeCatalog.StarterTapeId, true));
            Assert.False(collection.IsFavorite(SoulTapeCatalog.StarterTapeId));
        }

        [Fact]
        [Trait("Validation", "CatalogLibraryRefresh")]
        public void GeneratedCassetteIdSurvivesFileRename()
        {
            string folder = Path.Combine(Path.GetTempPath(), "SoulTapeTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                byte[] audioBytes = Enumerable.Range(0, 4096).Select(value => (byte)(value % 251)).ToArray();
                string firstPath = Path.Combine(folder, "Artist One - Track One.mp3");
                string renamedPath = Path.Combine(folder, "Completely Renamed.mp3");
                File.WriteAllBytes(firstPath, audioBytes);
                File.WriteAllBytes(renamedPath, audioBytes);

                MusicTrack first = new MusicTrack(firstPath, audioBytes.Length);
                MusicTrack renamed = new MusicTrack(renamedPath, audioBytes.Length);
                SoulTapeCatalog catalog = new SoulTapeCatalog(new FakeLog());

                catalog.Refresh(new[] { first });
                string firstId = catalog.GetAllTapes().Single(tape => tape.Track == first).Id;
                catalog.Refresh(new[] { renamed });
                string renamedId = catalog.GetAllTapes().Single(tape => tape.Track == renamed).Id;

                Assert.Equal(firstId, renamedId);
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
        }

        [Fact]
        [Trait("Validation", "CatalogLibraryRefresh")]
        public void TwoTracksWithMissingFingerprintsDoNotCollide()
        {
            string missingFolder = Path.Combine(
                Path.GetTempPath(),
                "SoulTapeMissingTests-" + Guid.NewGuid().ToString("N"));
            MusicTrack first = new MusicTrack(
                Path.Combine(missingFolder, "Missing Artist - Missing One.mp3"),
                100);
            MusicTrack second = new MusicTrack(
                Path.Combine(missingFolder, "Other Artist - Missing Two.mp3"),
                200);
            FakeLog log = new FakeLog();
            SoulTapeCatalog catalog = new SoulTapeCatalog(log);

            catalog.Refresh(new[] { first, second });

            Assert.Equal(string.Empty, first.AudioFingerprint);
            Assert.Equal(string.Empty, second.AudioFingerprint);
            Assert.DoesNotContain(
                catalog.GetAllTapes(),
                tape => tape.Id.StartsWith("soul-tape.audio.", StringComparison.Ordinal));
            Assert.Equal(2, log.Warnings.Count(message =>
                message.Contains("no valid SHA-256 audio fingerprint")));
        }

        [Fact]
        [Trait("Validation", "CatalogLibraryRefresh")]
        public void GeneratedTrackWithoutFingerprintGetsNoPersistentCassetteId()
        {
            string missingFolder = Path.Combine(
                Path.GetTempPath(),
                "SoulTapeUnavailableTests-" + Guid.NewGuid().ToString("N"));
            MusicTrack track = new MusicTrack(
                Path.Combine(missingFolder, "Unavailable Artist - Unavailable Track.flac"),
                300);
            SoulTapeCatalog catalog = new SoulTapeCatalog(new FakeLog());

            catalog.Refresh(new[] { track });

            Assert.False(TrackFingerprint.IsValidSha256(track.AudioFingerprint));
            Assert.DoesNotContain(catalog.GetAllTapes(), tape => tape.Track == track);
            Assert.DoesNotContain(
                catalog.GetAllTapes(),
                tape => tape.Id == "soul-tape.audio.unavailable");
        }

        [Fact]
        [Trait("Validation", "CatalogLibraryRefresh")]
        public void KnownCassetteFingerprintRejectsMismatchingAudio()
        {
            string folder = Path.Combine(Path.GetTempPath(), "SoulTapeKnownTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                byte[] audioBytes = Enumerable.Range(0, 2048).Select(value => (byte)(value % 199)).ToArray();
                string path = Path.Combine(folder, "Known Artist - Known Title.mp3");
                File.WriteAllBytes(path, audioBytes);
                MusicTrack track = new MusicTrack(path, audioBytes.Length);
                KnownSoulTapeDefinition known = new KnownSoulTapeDefinition(
                    "soul-tape.known.test",
                    "Known Artist",
                    "Known Title",
                    SoulTapeRarity.Rare,
                    new string('0', 64));
                FakeLog log = new FakeLog();
                SoulTapeCatalog catalog = new SoulTapeCatalog(log, new[] { known });

                catalog.Refresh(new[] { track });

                SoulTapeCatalogEntry knownEntry;
                Assert.True(catalog.TryGetTape("soul-tape.known.test", out knownEntry));
                Assert.Null(knownEntry.Track);
                Assert.Contains(
                    catalog.GetAllTapes(),
                    tape => tape.Track == track && tape.Id.StartsWith("soul-tape.audio."));
                Assert.Contains(log.Warnings, message =>
                    message.Contains("known cassette metadata matched") &&
                    message.Contains("known ID was not assigned"));
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
        }

        [Fact]
        [Trait("Validation", "PersistenceRecovery")]
        public void ThrowingChangedSubscriberDoesNotBreakUnlock()
        {
            FakeCatalog catalog = CreateCatalog();
            FakeStore store = new FakeStore(SoulTapeLoadStatus.Missing, null);
            FakeLog log = new FakeLog();
            SoulTapeCollection collection = CreateCollection(catalog, store, log);
            collection.BindProfile("profile-events");
            bool laterSubscriberCalled = false;
            collection.Changed += () => throw new InvalidOperationException("subscriber failure");
            collection.Changed += () => laterSubscriberCalled = true;

            bool unlocked = collection.UnlockTape("soul-tape.test.extra");

            Assert.True(unlocked);
            Assert.True(collection.IsUnlocked("soul-tape.test.extra"));
            Assert.Contains("soul-tape.test.extra", store.LastSaved.UnlockedCassetteIds);
            Assert.True(laterSubscriberCalled);
            Assert.Contains(log.Warnings, message =>
                message.Contains("Changed subscriber failed") &&
                message.Contains("subscriber failure"));
        }

        [Fact]
        [Trait("Validation", "CatalogLibraryRefresh")]
        public void ThrowingCatalogChangedSubscriberDoesNotBreakRefresh()
        {
            FakeLog log = new FakeLog();
            SoulTapeCatalog catalog = new SoulTapeCatalog(log);
            bool laterSubscriberCalled = false;
            catalog.Changed += () => throw new InvalidOperationException("catalog subscriber failure");
            catalog.Changed += () => laterSubscriberCalled = true;

            catalog.Refresh(Array.Empty<MusicTrack>());

            Assert.True(laterSubscriberCalled);
            Assert.Contains(log.Warnings, message =>
                message.Contains("catalog Changed subscriber failed") &&
                message.Contains("catalog subscriber failure"));
        }

        [Fact]
        [Trait("Validation", "PersistenceRecovery")]
        public void JsonStoreRecoversFromBackupWithoutReplacingIds()
        {
            string folder = Path.Combine(Path.GetTempPath(), "SoulTapeStoreTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                JsonSoulTapeCollectionStore store = new JsonSoulTapeCollectionStore(folder);
                SoulTapeCollectionData first = new SoulTapeCollectionData
                {
                    ProfileId = "profile-e",
                    UnlockedCassetteIds = new List<string> { "tape-one" }
                };
                SoulTapeCollectionData second = new SoulTapeCollectionData
                {
                    ProfileId = "profile-e",
                    UnlockedCassetteIds = new List<string> { "tape-one", "tape-two" }
                };

                store.Save("profile-e", first);
                store.Save("profile-e", second);
                File.WriteAllText(Path.Combine(folder, "profile-e.json"), "not json");

                SoulTapeLoadResult result = store.Load("profile-e");

                Assert.Equal(SoulTapeLoadStatus.RecoveredFromBackup, result.Status);
                Assert.Equal(new[] { "tape-one" }, result.Data.UnlockedCassetteIds);
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
        }

        private static SoulTapeCollection CreateCollection(
            ISoulTapeCatalog catalog,
            ISoulTapeCollectionStore store)
        {
            return CreateCollection(catalog, store, new FakeLog());
        }

        private static SoulTapeCollection CreateCollection(
            ISoulTapeCatalog catalog,
            ISoulTapeCollectionStore store,
            ISoulTapeLog log)
        {
            return new SoulTapeCollection(catalog, store, log);
        }

        private static FakeCatalog CreateCatalog()
        {
            return new FakeCatalog(new[]
            {
                Entry(SoulTapeCatalog.StarterTapeId, "Scott Buckley", "The Long Dark"),
                Entry("soul-tape.test.extra", "Test Artist", "Extra Tape")
            });
        }

        private static SoulTapeCatalogEntry Entry(string id, string artist, string title)
        {
            return new SoulTapeCatalogEntry(id, artist, title, "test:" + id, null, null, null);
        }

        private sealed class FakeCatalog : ISoulTapeCatalog
        {
            private readonly Dictionary<string, SoulTapeCatalogEntry> _entries;

            internal FakeCatalog(IEnumerable<SoulTapeCatalogEntry> entries)
            {
                _entries = entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
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

        private sealed class FakeStore : ISoulTapeCollectionStore
        {
            private readonly SoulTapeLoadStatus _status;
            private readonly SoulTapeCollectionData _data;

            internal FakeStore(SoulTapeLoadStatus status, SoulTapeCollectionData data)
            {
                _status = status;
                _data = data;
            }

            internal bool FailSaves { get; set; }
            internal SoulTapeCollectionData LastSaved { get; private set; }

            public SoulTapeLoadResult Load(string profileId)
            {
                return new SoulTapeLoadResult(_status, _data, string.Empty);
            }

            public void Save(string profileId, SoulTapeCollectionData data)
            {
                if (FailSaves)
                {
                    throw new IOException("simulated save failure");
                }

                LastSaved = new SoulTapeCollectionData
                {
                    ProfileId = data.ProfileId,
                    UnlockedCassetteIds = data.UnlockedCassetteIds.ToList(),
                    FavoriteCassetteIds = data.FavoriteCassetteIds.ToList(),
                    SelectedRecorderCassetteId = data.SelectedRecorderCassetteId
                };
            }

            public string Describe(string profileId)
            {
                return "memory:" + profileId;
            }
        }

        private sealed class FakeLog : ISoulTapeLog
        {
            internal List<string> Infos { get; } = new List<string>();
            internal List<string> Warnings { get; } = new List<string>();
            internal List<string> Errors { get; } = new List<string>();

            public void Info(string message)
            {
                Infos.Add(message);
            }

            public void Warning(string message)
            {
                Warnings.Add(message);
            }

            public void Error(string message)
            {
                Errors.Add(message);
            }
        }
    }
}
