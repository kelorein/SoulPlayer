using System;
using System.IO;
using System.Linq;
using SoulPlayer.Cassettes;
using SoulPlayer.Library;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class RecorderSelectionTests
    {
        [Fact]
        [Trait("Validation", "RecorderSelection")]
        public void StarterTapeIsPreferredWhenUnlockedAndAvailable()
        {
            string folder = CreateTemporaryFolder();
            try
            {
                MusicTrack starter = WriteTrack(
                    folder,
                    "Scott Buckley - The Long Dark.mp3",
                    11);
                MusicTrack other = WriteTrack(folder, "Other Artist - Other Song.mp3", 23);
                SoulTapeCatalog catalog = CreateCatalog(starter, other);
                SoulTapeCollection collection = CreateCollection(catalog, "selection-profile-a");
                string otherId = catalog.GetAllTapes().Single(tape => tape.Track == other).Id;
                Assert.True(collection.UnlockTape(otherId));

                SoulTapeCatalogEntry selected = SoulTapeRecorderSelector.Select(collection);

                Assert.NotNull(selected);
                Assert.Equal(SoulTapeCatalog.StarterTapeId, selected.Id);
                Assert.Same(starter, selected.Track);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        [Trait("Validation", "RecorderSelection")]
        public void UnlockedAvailableTapeIsUsedWhenStarterAudioIsMissing()
        {
            string folder = CreateTemporaryFolder();
            try
            {
                MusicTrack other = WriteTrack(folder, "Fallback Artist - Fallback Song.ogg", 37);
                SoulTapeCatalog catalog = CreateCatalog(other);
                SoulTapeCollection collection = CreateCollection(catalog, "selection-profile-b");
                SoulTapeCatalogEntry otherTape = catalog.GetAllTapes().Single(tape => tape.Track == other);
                Assert.True(collection.UnlockTape(otherTape.Id));

                SoulTapeCatalogEntry selected = SoulTapeRecorderSelector.Select(collection);

                Assert.NotNull(selected);
                Assert.Equal(otherTape.Id, selected.Id);
                Assert.True(selected.IsAudioAvailable);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        [Trait("Validation", "RecorderSelection")]
        public void LockedAvailableTrackIsNeverSelected()
        {
            string folder = CreateTemporaryFolder();
            try
            {
                MusicTrack locked = WriteTrack(folder, "Locked Artist - Locked Song.flac", 41);
                SoulTapeCatalog catalog = CreateCatalog(locked);
                SoulTapeCollection collection = CreateCollection(catalog, "selection-profile-c");
                SoulTapeCatalogEntry lockedTape = catalog.GetAllTapes().Single(tape => tape.Track == locked);

                SoulTapeCatalogEntry selected = SoulTapeRecorderSelector.Select(collection);

                Assert.False(collection.IsUnlocked(lockedTape.Id));
                Assert.Null(selected);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static SoulTapeCatalog CreateCatalog(params MusicTrack[] tracks)
        {
            SoulTapeCatalog catalog = new SoulTapeCatalog(new OfflineTestLog());
            catalog.Refresh(tracks);
            return catalog;
        }

        private static SoulTapeCollection CreateCollection(SoulTapeCatalog catalog, string profileId)
        {
            SoulTapeCollection collection = new SoulTapeCollection(
                catalog,
                new InMemoryCollectionStore(),
                new OfflineTestLog());
            Assert.True(collection.BindProfile(profileId));
            return collection;
        }

        private static MusicTrack WriteTrack(string folder, string fileName, byte seed)
        {
            string path = Path.Combine(folder, fileName);
            byte[] bytes = Enumerable.Range(0, 4096)
                .Select(value => (byte)((value + seed) % 251))
                .ToArray();
            File.WriteAllBytes(path, bytes);
            return new MusicTrack(path, bytes.Length);
        }

        private static string CreateTemporaryFolder()
        {
            string folder = Path.Combine(
                Path.GetTempPath(),
                "SoulTapeSelection-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }
    }
}
