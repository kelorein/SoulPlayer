using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SoulPlayer.Cassettes;
using SoulPlayer.Library;
using SoulPlayer.World;
using UnityEngine;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class SoulTapeWorldDiscoveryTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "SoulTapeWorldDiscovery-" + Guid.NewGuid().ToString("N"));
        private int _trackNumber;

        public SoulTapeWorldDiscoveryTests()
        {
            Directory.CreateDirectory(_folder);
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void EligibilityExcludesStarterUnlockedMissingAudioAndNullRarity()
        {
            SoulTapeCatalogEntry starter = Entry(
                SoulTapeCatalog.StarterTapeId, SoulTapeRarity.Common, true);
            SoulTapeCatalogEntry unlocked = Entry("tape-unlocked", SoulTapeRarity.Common, true);
            SoulTapeCatalogEntry missingAudio = Entry("tape-missing", SoulTapeRarity.Rare, false);
            SoulTapeCatalogEntry generated = Entry("soul-tape.audio.personal", null, true);
            SoulTapeCatalogEntry eligible = Entry("tape-eligible", SoulTapeRarity.Epic, true);
            TestCatalog catalog = new TestCatalog(
                new[] { starter, unlocked, missingAudio, generated, eligible });
            SoulTapeCollection collection = Collection(catalog, new InMemoryCollectionStore());
            Assert.True(collection.BindProfile("eligibility-profile"));
            Assert.True(collection.UnlockTape(unlocked.Id));

            IReadOnlyList<SoulTapeCatalogEntry> result =
                SoulTapeWorldEligibility.GetEligibleTapes(catalog, collection);

            Assert.Single(result);
            Assert.Same(eligible, result[0]);
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void EmptyInputsProduceNoSpawns()
        {
            SoulTapeSpawnPlanner planner = new SoulTapeSpawnPlanner();
            SoulTapeCatalogEntry tape = Entry("tape-one", SoulTapeRarity.Common, true);
            SoulTapeSpawnAnchor anchor = Anchor("anchor-one", 0f);

            Assert.Empty(planner.CreatePlan("factory4_day", null, new[] { anchor }, 1).Entries);
            Assert.Empty(planner.CreatePlan("factory4_day", new[] { tape }, null, 1).Entries);
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void PlanClampsToThreeAndNeverDuplicatesTapeOrAnchor()
        {
            SoulTapeSpawnPlan plan = new SoulTapeSpawnPlanner().CreatePlan(
                "factory4_day",
                Enumerable.Range(0, 8)
                    .Select(index => Entry("tape-" + index, SoulTapeRarity.Common, true)),
                Enumerable.Range(0, 8).Select(index => Anchor("anchor-" + index, index)),
                3,
                99,
                42);

            Assert.Equal(3, plan.Entries.Count);
            Assert.Equal(3, plan.Entries.Select(item => item.Cassette.Id).Distinct().Count());
            Assert.Equal(3, plan.Entries.Select(item => item.Anchor.Id).Distinct().Count());
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void SpawnCountClampsToAvailableTapesAndAnchors()
        {
            SoulTapeSpawnPlanner planner = new SoulTapeSpawnPlanner();
            SoulTapeSpawnPlan oneTape = planner.CreatePlan(
                "factory4_day",
                new[] { Entry("only-tape", SoulTapeRarity.Common, true) },
                new[] { Anchor("one", 1f), Anchor("two", 2f) },
                1,
                3,
                8);
            SoulTapeSpawnPlan oneAnchor = planner.CreatePlan(
                "factory4_day",
                new[]
                {
                    Entry("first", SoulTapeRarity.Common, true),
                    Entry("second", SoulTapeRarity.Common, true)
                },
                new[] { Anchor("only-anchor", 1f) },
                1,
                3,
                8);

            Assert.Single(oneTape.Entries);
            Assert.Single(oneAnchor.Entries);
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void SameSeedIsStableAndDifferentSeedsCanVary()
        {
            List<SoulTapeCatalogEntry> tapes = Enumerable.Range(0, 6)
                .Select(index => Entry("stable-tape-" + index, SoulTapeRarity.Common, true))
                .ToList();
            List<SoulTapeSpawnAnchor> anchors = Enumerable.Range(0, 6)
                .Select(index => Anchor("stable-anchor-" + index, index))
                .ToList();
            SoulTapeSpawnPlanner planner = new SoulTapeSpawnPlanner();

            string first = Signature(planner.CreatePlan(
                "factory4_day", tapes, anchors, 3, 3, 12345));
            string second = Signature(planner.CreatePlan(
                "factory4_day", tapes, anchors, 3, 3, 12345));
            HashSet<string> varied = new HashSet<string>();
            for (int seed = 0; seed < 20; seed++)
            {
                varied.Add(Signature(planner.CreatePlan(
                    "factory4_day", tapes, anchors, 3, 3, seed)));
            }

            Assert.Equal(first, second);
            Assert.True(varied.Count > 1);
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void RarityWeightsFavorCommonAndKeepEpicLegendaryPossible()
        {
            SoulTapeCatalogEntry[] tapes =
            {
                Entry("common", SoulTapeRarity.Common, true),
                Entry("uncommon", SoulTapeRarity.Uncommon, true),
                Entry("rare", SoulTapeRarity.Rare, true),
                Entry("epic", SoulTapeRarity.Epic, true),
                Entry("legendary", SoulTapeRarity.Legendary, true)
            };
            Dictionary<SoulTapeRarity, int> observed = Enum
                .GetValues(typeof(SoulTapeRarity))
                .Cast<SoulTapeRarity>()
                .ToDictionary(rarity => rarity, rarity => 0);
            SoulTapeSpawnPlanner planner = new SoulTapeSpawnPlanner();
            SoulTapeSpawnAnchor anchor = Anchor("weighted-anchor", 0f);

            for (int seed = 0; seed < 5000; seed++)
            {
                SoulTapeCatalogEntry selected = planner.CreatePlan(
                    "factory4_day", tapes, new[] { anchor }, 1, 1, seed).Entries[0].Cassette;
                observed[selected.Rarity.Value]++;
            }

            Assert.True(observed[SoulTapeRarity.Common] > observed[SoulTapeRarity.Uncommon]);
            Assert.True(observed[SoulTapeRarity.Uncommon] > observed[SoulTapeRarity.Rare]);
            Assert.True(observed[SoulTapeRarity.Rare] > observed[SoulTapeRarity.Epic]);
            Assert.True(observed[SoulTapeRarity.Epic] > observed[SoulTapeRarity.Legendary]);
            Assert.True(observed[SoulTapeRarity.Epic] > 0);
            Assert.True(observed[SoulTapeRarity.Legendary] > 0);

            IReadOnlyDictionary<SoulTapeRarity, int> weights =
                SoulTapeSpawnPlanner.GetRarityWeights();
            Assert.Equal(100, weights[SoulTapeRarity.Common]);
            Assert.Equal(60, weights[SoulTapeRarity.Uncommon]);
            Assert.Equal(30, weights[SoulTapeRarity.Rare]);
            Assert.Equal(12, weights[SoulTapeRarity.Epic]);
            Assert.Equal(4, weights[SoulTapeRarity.Legendary]);
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void CommittedFactoryResourceContainsExactlyTenEnabledDayAnchors()
        {
            OfflineTestLog log = new OfflineTestLog();
            SoulTapeSpawnAnchorCatalog catalog = new SoulTapeSpawnAnchorCatalog(
                typeof(SoulTapeCatalog).Assembly,
                log);

            IReadOnlyList<SoulTapeSpawnAnchor> day = catalog.GetForMap("factory4_day");

            Assert.Equal(10, day.Count);
            Assert.All(day, anchor =>
            {
                Assert.True(anchor.Enabled);
                Assert.Equal("factory4_day", anchor.MapId);
                Assert.Equal(SoulTapeSpawnAnchorSource.Curated, anchor.Source);
            });
            Assert.Empty(catalog.GetForMap("factory4_night"));
            Assert.Empty(log.Errors);
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void CompletedButUnappliedLibraryScanRemainsPendingUntilApplied()
        {
            MusicLibrary library = new MusicLibrary(delegate { }, delegate { });
            library.BeginScan(new[] { _folder });

            Assert.False(library.HasAppliedScan);
            Assert.True(SpinWait.SpinUntil(
                () => !library.IsScanning,
                TimeSpan.FromSeconds(5)));
            Assert.False(library.HasAppliedScan);

            ScanResult ignored;
            Assert.True(library.TryApplyCompletedScan(out ignored));
            Assert.True(library.HasAppliedScan);
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void InteractionTargetIsDeliberatelyLargerThanVisibleCassette()
        {
            Assert.True(SoulTapeWorldPickup.InteractionTargetSize.x > 0.11f);
            Assert.True(SoulTapeWorldPickup.InteractionTargetSize.y > 0.018f);
            Assert.True(SoulTapeWorldPickup.InteractionTargetSize.z > 0.07f);
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void VisibilitySamplesSitAboveAndWithinCassetteTopFace()
        {
            Assert.Equal(3, SoulTapeWorldPickup.VisibilitySampleCount);
            Assert.Equal(
                SoulTapeWorldPickup.CassetteHalfThickness +
                SoulTapeWorldPickup.InteractionFocusClearance,
                SoulTapeWorldPickup.InteractionFocusLocalOffset.y);

            Vector3 center = SoulTapeWorldPickup.GetVisibilitySampleLocalOffset(0);
            Vector3 left = SoulTapeWorldPickup.GetVisibilitySampleLocalOffset(1);
            Vector3 right = SoulTapeWorldPickup.GetVisibilitySampleLocalOffset(2);

            Assert.Equal(SoulTapeWorldPickup.InteractionFocusLocalOffset, center);
            Assert.Equal(-0.035f, left.x);
            Assert.Equal(0.035f, right.x);
            Assert.All(new[] { center, left, right }, sample =>
            {
                Assert.Equal(0.024f, sample.y, 3);
                Assert.InRange(sample.x, -0.055f, 0.055f);
                Assert.Equal(0f, sample.z);
            });
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void InteractionConeAcquiresAtTwoPointFiveAndRetainsToThreePointFiveDegrees()
        {
            Assert.Equal(2.5f, SoulTapeInteractionTargeting.AcquireAngleDegrees);
            Assert.Equal(3.5f, SoulTapeInteractionTargeting.RetainAngleDegrees);
            Assert.True(SoulTapeInteractionTargeting.IsWithinCone(2.5f, false));
            Assert.False(SoulTapeInteractionTargeting.IsWithinCone(2.51f, false));
            Assert.True(SoulTapeInteractionTargeting.IsWithinCone(3.5f, true));
            Assert.False(SoulTapeInteractionTargeting.IsWithinCone(3.51f, true));
            Assert.True(SoulTapeInteractionTargeting.LooksLikeAuxiliaryCameraName(
                "Scope Render Camera"));
            Assert.True(SoulTapeInteractionTargeting.LooksLikeAuxiliaryCameraName(
                "Weapon Overlay"));
            Assert.False(SoulTapeInteractionTargeting.LooksLikeAuxiliaryCameraName(
                "FPS Main Camera"));
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void NewPickupPersistsImmediatelyAndReloadSurvivesAnyRaidOutcome()
        {
            string collectionFolder = Path.Combine(_folder, "collections");
            SoulTapeCatalogEntry tape = Entry("discovery-new", SoulTapeRarity.Rare, true);
            TestCatalog catalog = CatalogWithStarter(tape);
            JsonSoulTapeCollectionStore store = new JsonSoulTapeCollectionStore(collectionFolder);
            SoulTapeCollection collection = Collection(catalog, store);
            Assert.True(collection.BindProfile("discovery-profile"));
            SoulTapeDiscoveryService discovery = Service(catalog, collection);

            Assert.Equal(SoulTapeDiscoveryResult.NewUnlock, discovery.Discover(tape.Id));
            string path = Path.Combine(collectionFolder, "discovery-profile.json");
            Assert.True(File.Exists(path));
            Assert.Contains(tape.Id, File.ReadAllText(path));

            // Recreate the data layer directly; no extraction or raid-end hook participates.
            SoulTapeCollection reloaded = Collection(catalog, store);
            Assert.True(reloaded.BindProfile("discovery-profile"));
            Assert.True(reloaded.IsUnlocked(tape.Id));
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void DuplicateAndInvalidDiscoveryResultsAreDistinct()
        {
            SoulTapeCatalogEntry tape = Entry("discovery-duplicate", SoulTapeRarity.Uncommon, true);
            TestCatalog catalog = CatalogWithStarter(tape);
            SoulTapeCollection collection = Collection(catalog, new InMemoryCollectionStore());
            collection.BindProfile("duplicate-profile");
            SoulTapeDiscoveryService service = Service(catalog, collection);

            Assert.Equal(SoulTapeDiscoveryResult.NewUnlock, service.Discover(tape.Id));
            Assert.Equal(SoulTapeDiscoveryResult.AlreadyUnlocked, service.Discover(tape.Id));
            Assert.Equal(SoulTapeDiscoveryResult.InvalidTape, service.Discover("missing-tape"));
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void SaveFailureReturnsSaveFailedAndRollsBack()
        {
            SoulTapeCatalogEntry tape = Entry("discovery-failure", SoulTapeRarity.Epic, true);
            TestCatalog catalog = CatalogWithStarter(tape);
            InMemoryCollectionStore store = new InMemoryCollectionStore();
            SoulTapeCollection collection = Collection(catalog, store);
            collection.BindProfile("failure-profile");
            store.FailSaves = true;

            SoulTapeDiscoveryResult result = Service(catalog, collection).Discover(tape.Id);

            Assert.Equal(SoulTapeDiscoveryResult.SaveFailed, result);
            Assert.False(collection.IsUnlocked(tape.Id));
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void ThrowingDiscoverySubscriberIsIsolated()
        {
            SoulTapeCatalogEntry tape = Entry("discovery-event", SoulTapeRarity.Legendary, true);
            TestCatalog catalog = CatalogWithStarter(tape);
            SoulTapeCollection collection = Collection(catalog, new InMemoryCollectionStore());
            collection.BindProfile("event-profile");
            OfflineTestLog log = new OfflineTestLog();
            SoulTapeDiscoveryService service = new SoulTapeDiscoveryService(catalog, collection, log);
            bool laterSubscriberCalled = false;
            service.Discovered += payload => throw new InvalidOperationException("event failure");
            service.Discovered += payload => laterSubscriberCalled = payload.IsNewUnlock;

            Assert.Equal(SoulTapeDiscoveryResult.NewUnlock, service.Discover(tape.Id));
            Assert.True(laterSubscriberCalled);
            Assert.Contains(log.Warnings, message => message.Contains("event failure"));
        }

        [Fact]
        [Trait("Validation", "WorldDiscovery")]
        public void DiscoveryProgressionRemainsProfileSeparated()
        {
            SoulTapeCatalogEntry tape = Entry("discovery-separated", SoulTapeRarity.Rare, true);
            TestCatalog catalog = CatalogWithStarter(tape);
            InMemoryCollectionStore store = new InMemoryCollectionStore();
            SoulTapeCollection collection = Collection(catalog, store);
            collection.BindProfile("profile-one");
            Assert.Equal(
                SoulTapeDiscoveryResult.NewUnlock,
                Service(catalog, collection).Discover(tape.Id));

            collection.BindProfile("profile-two");
            Assert.False(collection.IsUnlocked(tape.Id));
            collection.BindProfile("profile-one");
            Assert.True(collection.IsUnlocked(tape.Id));
        }

        public void Dispose()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        private SoulTapeCatalogEntry Entry(
            string id,
            SoulTapeRarity? rarity,
            bool audioAvailable)
        {
            MusicTrack track = null;
            if (audioAvailable)
            {
                string path = Path.Combine(
                    _folder,
                    "World Artist " + _trackNumber + " - World Track " + _trackNumber + ".mp3");
                _trackNumber++;
                File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });
                track = new MusicTrack(path, 5);
            }

            return new SoulTapeCatalogEntry(
                id,
                "World Artist",
                id,
                "test:" + id,
                rarity,
                null,
                track);
        }

        private static SoulTapeSpawnAnchor Anchor(string id, float x)
        {
            return new SoulTapeSpawnAnchor
            {
                Id = id,
                MapId = "factory4_day",
                Position = new SoulTapeVector3(x, 1f, 2f),
                RotationEuler = new SoulTapeVector3(0f, 90f, 0f),
                SurfaceNormal = new SoulTapeVector3(0f, 1f, 0f),
                Source = SoulTapeSpawnAnchorSource.Curated,
                Enabled = true
            };
        }

        private static string Signature(SoulTapeSpawnPlan plan)
        {
            return string.Join(
                "|",
                plan.Entries.Select(item => item.Cassette.Id + "@" + item.Anchor.Id));
        }

        private SoulTapeCollection Collection(
            ISoulTapeCatalog catalog,
            ISoulTapeCollectionStore store)
        {
            return new SoulTapeCollection(catalog, store, new OfflineTestLog());
        }

        private SoulTapeDiscoveryService Service(
            ISoulTapeCatalog catalog,
            SoulTapeCollection collection)
        {
            return new SoulTapeDiscoveryService(catalog, collection, new OfflineTestLog());
        }

        private TestCatalog CatalogWithStarter(params SoulTapeCatalogEntry[] extra)
        {
            return new TestCatalog(new[]
            {
                Entry(SoulTapeCatalog.StarterTapeId, SoulTapeRarity.Common, true)
            }.Concat(extra));
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
