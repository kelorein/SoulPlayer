using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoulPlayer.Cassettes;
using SoulPlayer.Library;
using SoulPlayer.Recorder;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class SoulRecorderRaidNextHotkeyTests : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "SoulRecorderRaidNext-" + Guid.NewGuid().ToString("N"));

        public SoulRecorderRaidNextHotkeyTests()
        {
            Directory.CreateDirectory(_root);
        }

        [Fact]
        public void NextWhilePlayingReservesNextBagItemThenStartsItAfterEjection()
        {
            Fixture fixture = Create("a", "b");
            fixture.Unlock("a", "b");
            SoulTapeCatalogEntry current = fixture.Take(
                SoulTapeRaidPlaybackMode.Discovered).Tape;
            SoulRecorderRaidNextCoordinator coordinator =
                new SoulRecorderRaidNextCoordinator();
            Assert.True(coordinator.Request());

            SoulRecorderRaidNextDecision eject = coordinator.Evaluate(
                SoulRecorderState.Playing,
                false,
                () => fixture.Take(SoulTapeRaidPlaybackMode.Discovered));
            SoulRecorderRaidNextDecision insert = coordinator.Evaluate(
                SoulRecorderState.Idle,
                false,
                () => fixture.Take(SoulTapeRaidPlaybackMode.Discovered));

            Assert.Equal(SoulRecorderRaidNextAction.BeginEjection, eject.Action);
            Assert.NotEqual(current.Id, eject.Tape.Id);
            Assert.Equal(SoulRecorderRaidNextAction.BeginInsertion, insert.Action);
            Assert.Same(eject.Tape, insert.Tape);
            Assert.False(coordinator.IsPending);
        }

        [Fact]
        public void RepeatedNextSkipsDoNotRepeatBeforeBagExhaustion()
        {
            Fixture fixture = Create("a", "b", "c", "d");
            fixture.Unlock("a", "b", "c", "d");
            List<string> played = new List<string>
            {
                fixture.Take(SoulTapeRaidPlaybackMode.Discovered).Tape.Id
            };
            SoulRecorderRaidNextCoordinator coordinator =
                new SoulRecorderRaidNextCoordinator();

            for (int index = 0; index < 3; index++)
            {
                played.Add(RunPlayingSkip(
                    coordinator,
                    () => fixture.Take(SoulTapeRaidPlaybackMode.Discovered)).Id);
            }

            Assert.Equal(4, played.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void NextRespectsFavoritesOnlyMode()
        {
            Fixture fixture = Create("a", "b", "c");
            fixture.Unlock("a", "b", "c");
            fixture.Collection.SetFavorite("b", true);
            fixture.Collection.SetFavorite("c", true);

            string[] ids = Enumerable.Range(0, 2)
                .Select(_ => fixture.Take(
                    SoulTapeRaidPlaybackMode.FavoritesOnly).Tape.Id)
                .OrderBy(id => id)
                .ToArray();

            Assert.Equal(new[] { "b", "c" }, ids);
        }

        [Fact]
        public void NextRespectsDiscoveredMode()
        {
            Fixture fixture = Create("a", "b", "locked");
            fixture.Unlock("a", "b");

            string[] ids = Enumerable.Range(0, 2)
                .Select(_ => fixture.Take(
                    SoulTapeRaidPlaybackMode.Discovered).Tape.Id)
                .OrderBy(id => id)
                .ToArray();

            Assert.Equal(new[] { "a", "b" }, ids);
        }

        [Fact]
        public void NextRespectsFavoritesFirstMode()
        {
            Fixture fixture = Create("a", "b", "c", "d");
            fixture.Unlock("a", "b", "c", "d");
            fixture.Collection.SetFavorite("a", true);
            fixture.Collection.SetFavorite("b", true);

            string[] cycle = Enumerable.Range(0, 4)
                .Select(_ => fixture.Take(
                    SoulTapeRaidPlaybackMode.FavoritesFirst).Tape.Id)
                .ToArray();

            Assert.Equal(new[] { "a", "b" }, cycle.Take(2).OrderBy(id => id));
            Assert.Equal(new[] { "c", "d" }, cycle.Skip(2).OrderBy(id => id));
        }

        [Fact]
        public void NextWhileStoppedStartsInsertionWithoutEjection()
        {
            Fixture fixture = Create("a");
            fixture.Unlock("a");
            SoulRecorderRaidNextCoordinator coordinator =
                new SoulRecorderRaidNextCoordinator();
            Assert.True(coordinator.Request());

            SoulRecorderRaidNextDecision decision = coordinator.Evaluate(
                SoulRecorderState.Idle,
                false,
                () => fixture.Take(SoulTapeRaidPlaybackMode.Discovered));

            Assert.Equal(SoulRecorderRaidNextAction.BeginInsertion, decision.Action);
            Assert.Equal("a", decision.Tape.Id);
        }

        [Fact]
        public void RepeatedKeypressWhileTransitionBusyQueuesExactlyOneRequest()
        {
            Fixture fixture = Create("a");
            fixture.Unlock("a");
            SoulRecorderRaidNextCoordinator coordinator =
                new SoulRecorderRaidNextCoordinator();

            Assert.True(coordinator.Request());
            Assert.False(coordinator.Request());
            Assert.Equal(SoulRecorderRaidNextAction.None,
                coordinator.Evaluate(
                    SoulRecorderState.LoadingTape,
                    true,
                    () => fixture.Take(SoulTapeRaidPlaybackMode.Discovered)).Action);
            Assert.True(coordinator.IsPending);
            Assert.Equal(SoulRecorderRaidNextAction.BeginEjection,
                coordinator.Evaluate(
                    SoulRecorderState.Playing,
                    false,
                    () => fixture.Take(SoulTapeRaidPlaybackMode.Discovered)).Action);
        }

        [Fact]
        public void ZeroEligibleNextCassetteReturnsEmptyWithoutEjection()
        {
            SoulRecorderRaidNextCoordinator coordinator =
                new SoulRecorderRaidNextCoordinator();
            Assert.True(coordinator.Request());

            SoulRecorderRaidNextDecision decision = coordinator.Evaluate(
                SoulRecorderState.Playing,
                false,
                () => new SoulTapeRaidPlaybackSelection(
                    null,
                    SoulTapeRaidPlaybackEmptyReason.NoFavoriteTapes));

            Assert.Equal(SoulRecorderRaidNextAction.Empty, decision.Action);
            Assert.Equal(
                SoulTapeRaidPlaybackEmptyReason.NoFavoriteTapes,
                decision.Selection.EmptyReason);
            Assert.False(coordinator.IsPending);
        }

        [Fact]
        public void GlobalNextTrackAndRaidNextCassetteHotkeysRemainIndependent()
        {
            string settings = File.ReadAllText(Path.Combine(
                FindRepositoryRoot(), "Configuration", "SoulPlayerSettings.cs"))
                .Replace("\r\n", "\n");

            Assert.Contains("_nextHotkey", settings);
            Assert.Contains("_nextRaidCassetteHotkey", settings);
            Assert.Contains("\"Global controls\",\n                \"Next track hotkey\"", settings);
            Assert.Contains("\"SoulTape discovery\",\n                \"Next raid cassette hotkey\"", settings);
            Assert.Contains("new KeyboardShortcut(KeyCode.N)", settings);
        }

        [Fact]
        public void MStartAndEjectBehaviorRemainsOnItsExistingTogglePath()
        {
            string controller = File.ReadAllText(Path.Combine(
                FindRepositoryRoot(), "Recorder", "SoulRecorderController.cs"));

            Assert.Contains("private const KeyCode RecorderHotkey = KeyCode.M", controller);
            Assert.Contains("ToggleInteraction(player);", controller);
            Assert.Contains("EnterInteraction(player);", controller);
            Assert.Contains("EnterStopInteraction(player, null);", controller);
            Assert.Contains("QueueRaidNextCassette();", controller);
            Assert.Equal(1, CountOccurrences(
                controller, "_raidPlaybackPool.TakeNext("));
            Assert.Contains("ResolveUnlockedTape);", controller);
        }

        private static int CountOccurrences(string source, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf(
                value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }

        private Fixture Create(params string[] ids)
        {
            List<SoulTapeCatalogEntry> entries = ids.Select(id =>
            {
                string path = Path.Combine(_root, id + ".mp3");
                File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
                return new SoulTapeCatalogEntry(
                    id,
                    "Artist " + id,
                    "Track " + id,
                    path,
                    SoulTapeRarity.Common,
                    null,
                    new MusicTrack(path, 4L));
            }).ToList();
            TestCatalog catalog = new TestCatalog(entries);
            SoulTapeCollection collection = new SoulTapeCollection(
                catalog,
                new InMemoryCollectionStore(),
                new OfflineTestLog());
            Assert.True(collection.BindProfile("profile"));
            return new Fixture(
                collection,
                new SoulTapeRaidPlaybackPool(new Random(113)));
        }

        private static SoulTapeCatalogEntry RunPlayingSkip(
            SoulRecorderRaidNextCoordinator coordinator,
            Func<SoulTapeRaidPlaybackSelection> takeNext)
        {
            Assert.True(coordinator.Request());
            SoulRecorderRaidNextDecision eject = coordinator.Evaluate(
                SoulRecorderState.Playing, false, takeNext);
            Assert.Equal(SoulRecorderRaidNextAction.BeginEjection, eject.Action);
            Assert.Equal(SoulRecorderRaidNextAction.None,
                coordinator.Evaluate(
                    SoulRecorderState.Ejecting, false, takeNext).Action);
            SoulRecorderRaidNextDecision insert = coordinator.Evaluate(
                SoulRecorderState.Idle, false, takeNext);
            Assert.Equal(SoulRecorderRaidNextAction.BeginInsertion, insert.Action);
            return insert.Tape;
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

            internal SoulTapeRaidPlaybackSelection Take(
                SoulTapeRaidPlaybackMode mode)
            {
                return Pool.TakeNext(Collection, mode);
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
