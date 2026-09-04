using System;
using System.IO;
using System.Linq;
using System.Threading;
using SoulPlayer.Cassettes;
using SoulPlayer.Library;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class OfflineCollectionIntegrationTests
    {
        [Fact]
        [Trait("Validation", "CollectionBootstrap")]
        public void ProfileAppearsReloadsAndSwitchesWithSeparateProgression()
        {
            string folder = CreateTemporaryFolder("SoulTapeBootstrap");
            try
            {
                OfflineTestLog log = new OfflineTestLog();
                JsonSoulTapeCollectionStore store = new JsonSoulTapeCollectionStore(folder);
                MutableProfileIdProvider profiles = new MutableProfileIdProvider();
                MusicLibrary library = CreateOfflineLibrary();
                SoulTapeCatalog catalog = new SoulTapeCatalog(log);
                SoulTapeCollection collection = new SoulTapeCollection(catalog, store, log);

                using (SoulTapeCollectionHost host = new SoulTapeCollectionHost(
                           library,
                           catalog,
                           collection,
                           profiles))
                {
                    host.Initialize();
                    Assert.False(collection.IsLoaded);
                    Assert.Empty(Directory.GetFiles(folder, "*.json"));

                    profiles.CurrentProfileId = "test-profile-001";
                    Assert.True(host.RefreshProfile(string.Empty));
                    Assert.True(collection.IsUnlocked(SoulTapeCatalog.StarterTapeId));
                    Assert.True(File.Exists(Path.Combine(folder, "test-profile-001.json")));
                    Assert.True(collection.UnlockTape("soul-tape.scott-buckley.resonance"));
                    Assert.True(collection.SetFavorite("soul-tape.scott-buckley.resonance", true));
                }

                MusicLibrary reloadedLibrary = CreateOfflineLibrary();
                SoulTapeCatalog reloadedCatalog = new SoulTapeCatalog(log);
                SoulTapeCollection reloaded = new SoulTapeCollection(reloadedCatalog, store, log);
                using (SoulTapeCollectionHost reloadedHost = new SoulTapeCollectionHost(
                           reloadedLibrary,
                           reloadedCatalog,
                           reloaded,
                           profiles))
                {
                    reloadedHost.Initialize();
                    Assert.Equal("test-profile-001", reloaded.ProfileId);
                    Assert.True(reloaded.IsUnlocked(SoulTapeCatalog.StarterTapeId));
                    Assert.True(reloaded.IsUnlocked("soul-tape.scott-buckley.resonance"));
                    Assert.True(reloaded.IsFavorite("soul-tape.scott-buckley.resonance"));

                    profiles.CurrentProfileId = "test-profile-002";
                    Assert.True(reloadedHost.RefreshProfile(string.Empty));
                    Assert.Equal("test-profile-002", reloaded.ProfileId);
                    Assert.True(reloaded.IsUnlocked(SoulTapeCatalog.StarterTapeId));
                    Assert.False(reloaded.IsUnlocked("soul-tape.scott-buckley.resonance"));
                    Assert.False(reloaded.IsFavorite("soul-tape.scott-buckley.resonance"));
                    Assert.True(File.Exists(Path.Combine(folder, "test-profile-002.json")));

                    profiles.CurrentProfileId = "test-profile-001";
                    Assert.True(reloadedHost.RefreshProfile(string.Empty));
                    Assert.True(reloaded.IsUnlocked("soul-tape.scott-buckley.resonance"));
                    Assert.True(reloaded.IsFavorite("soul-tape.scott-buckley.resonance"));
                }

                Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        [Trait("Validation", "CatalogLibraryRefresh")]
        public void HostRefreshesCatalogWhenLibraryScanCompletesLater()
        {
            string folder = CreateTemporaryFolder("SoulTapeLibraryTiming");
            try
            {
                byte[] audioBytes = Enumerable.Range(0, 8192)
                    .Select(value => (byte)(value % 239))
                    .ToArray();
                File.WriteAllBytes(
                    Path.Combine(folder, "Scott Buckley - The Long Dark.mp3"),
                    audioBytes);

                OfflineTestLog log = new OfflineTestLog();
                MusicLibrary library = CreateOfflineLibrary();
                SoulTapeCatalog catalog = new SoulTapeCatalog(log);
                SoulTapeCollection collection = new SoulTapeCollection(
                    catalog,
                    new InMemoryCollectionStore(),
                    log);
                MutableProfileIdProvider profiles = new MutableProfileIdProvider();
                int catalogRefreshes = 0;
                catalog.Changed += () => catalogRefreshes++;

                using (SoulTapeCollectionHost host = new SoulTapeCollectionHost(
                           library,
                           catalog,
                           collection,
                           profiles))
                {
                    host.Initialize();
                    SoulTapeCatalogEntry initialStarter;
                    Assert.True(catalog.TryGetTape(SoulTapeCatalog.StarterTapeId, out initialStarter));
                    Assert.False(initialStarter.IsAudioAvailable);
                    Assert.Equal(0, catalog.GetAllTapes().Count(tape => tape.IsAudioAvailable));

                    library.BeginScan(new[] { folder });
                    bool applied = SpinWait.SpinUntil(
                        () =>
                        {
                            ScanResult ignored;
                            return library.TryApplyCompletedScan(out ignored);
                        },
                        TimeSpan.FromSeconds(5));

                    Assert.True(applied, "The offline library scan did not complete in time.");
                    SoulTapeCatalogEntry refreshedStarter;
                    Assert.True(catalog.TryGetTape(SoulTapeCatalog.StarterTapeId, out refreshedStarter));
                    Assert.True(refreshedStarter.IsAudioAvailable);
                    Assert.NotNull(refreshedStarter.Track);
                    Assert.True(TrackFingerprint.IsValidSha256(refreshedStarter.Track.AudioFingerprint));
                    Assert.Equal(2, catalogRefreshes);
                }
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static MusicLibrary CreateOfflineLibrary()
        {
            return new MusicLibrary(delegate { }, delegate { });
        }

        private static string CreateTemporaryFolder(string prefix)
        {
            string folder = Path.Combine(
                Path.GetTempPath(),
                prefix + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }
    }
}
