using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using EFT;
using SoulPlayer.Cassettes;
using SoulPlayer.Library;
using SoulPlayer.Patches;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class MenuProfileBindingTests
    {
        [Fact]
        [Trait("Validation", "SptApiContracts")]
        public void ExplicitFallbackBindsWhenBackendProviderIsEmpty()
        {
            using (HostFixture fixture = new HostFixture())
            {
                Assert.False(fixture.Collection.IsLoaded);

                Assert.True(fixture.Host.RefreshProfile("menu-profile-a"));

                Assert.True(fixture.Collection.IsLoaded);
                Assert.Equal("menu-profile-a", fixture.Collection.ProfileId);
                Assert.True(fixture.Collection.IsUnlocked(SoulTapeCatalog.StarterTapeId));
            }
        }

        [Fact]
        [Trait("Validation", "SptApiContracts")]
        public void SameExplicitProfileBindingIsIdempotent()
        {
            using (HostFixture fixture = new HostFixture())
            {
                int changes = 0;
                fixture.Collection.Changed += () => changes++;

                Assert.True(fixture.Host.RefreshProfile("menu-profile-a"));
                Assert.True(fixture.Host.RefreshProfile("menu-profile-a"));

                Assert.Equal(1, changes);
                Assert.Equal(1, fixture.Store.GetSaveCount("menu-profile-a"));
            }
        }

        [Fact]
        [Trait("Validation", "SptApiContracts")]
        public void SwitchingExplicitProfileDoesNotExposePreviousProgression()
        {
            using (HostFixture fixture = new HostFixture())
            {
                const string unlockedOnlyForA = "soul-tape.scott-buckley.resonance";
                Assert.True(fixture.Host.RefreshProfile("menu-profile-a"));
                Assert.True(fixture.Collection.UnlockTape(unlockedOnlyForA));
                Assert.True(fixture.Collection.SetFavorite(unlockedOnlyForA, true));

                Assert.True(fixture.Host.RefreshProfile("menu-profile-b"));

                Assert.Equal("menu-profile-b", fixture.Collection.ProfileId);
                Assert.False(fixture.Collection.IsUnlocked(unlockedOnlyForA));
                Assert.False(fixture.Collection.IsFavorite(unlockedOnlyForA));
                Assert.Single(fixture.Collection.GetUnlockedTapes());

                Assert.True(fixture.Host.RefreshProfile("menu-profile-a"));
                Assert.True(fixture.Collection.IsUnlocked(unlockedOnlyForA));
                Assert.True(fixture.Collection.IsFavorite(unlockedOnlyForA));
            }
        }

        [Fact]
        [Trait("Validation", "SptApiContracts")]
        public void EmptyExplicitFallbackDoesNotFabricateProfile()
        {
            using (HostFixture fixture = new HostFixture())
            {
                Assert.False(fixture.Host.RefreshProfile(string.Empty));
                Assert.False(fixture.Collection.IsLoaded);
                Assert.Equal(string.Empty, fixture.Collection.ProfileId);
                Assert.Empty(fixture.Store.ProfileIds);
            }
        }

        [Fact]
        [Trait("Validation", "SptApiContracts")]
        public void FailedProfileSwitchClearsVisibleStateAndRaisesChanged()
        {
            using (HostFixture fixture = new HostFixture())
            {
                const string onlyForA = "soul-tape.scott-buckley.resonance";
                Assert.True(fixture.Host.RefreshProfile("menu-profile-a"));
                Assert.True(fixture.Collection.UnlockTape(onlyForA));
                int changes = 0;
                fixture.Collection.Changed += () => changes++;
                fixture.Store.UnreadableProfileId = "menu-profile-b";

                Assert.False(fixture.Host.RefreshProfile("menu-profile-b"));

                Assert.Equal(1, changes);
                Assert.False(fixture.Collection.IsLoaded);
                Assert.Equal("menu-profile-b", fixture.Collection.ProfileId);
                Assert.False(fixture.Collection.IsUnlocked(onlyForA));
                Assert.Empty(fixture.Collection.GetUnlockedTapes());
                Assert.Empty(fixture.Collection.GetFavoriteTapes());
            }
        }

        [Fact]
        [Trait("Validation", "SptApiContracts")]
        public void ProfileArgumentResolverFindsTypedArgumentWithoutDependingOnNameOrPosition()
        {
            Profile profile = (Profile)FormatterServices.GetUninitializedObject(typeof(Profile));

            Profile resolved = MenuProfileArgumentResolver.Find(new object[]
            {
                ESessionMode.Regular,
                new object(),
                profile
            });

            Assert.Same(profile, resolved);
            Assert.Null(MenuProfileArgumentResolver.Find(new object[] { "not a profile" }));
            Assert.Null(MenuProfileArgumentResolver.Find(null));
        }

        private sealed class HostFixture : IDisposable
        {
            internal HostFixture()
            {
                Log = new OfflineTestLog();
                Library = new MusicLibrary(delegate { }, delegate { });
                Catalog = new SoulTapeCatalog(Log);
                Store = new CountingCollectionStore();
                Collection = new SoulTapeCollection(Catalog, Store, Log);
                Provider = new MutableProfileIdProvider();
                Host = new SoulTapeCollectionHost(
                    Library,
                    Catalog,
                    Collection,
                    Provider);
                Host.Initialize();
            }

            internal OfflineTestLog Log { get; private set; }
            internal MusicLibrary Library { get; private set; }
            internal SoulTapeCatalog Catalog { get; private set; }
            internal CountingCollectionStore Store { get; private set; }
            internal SoulTapeCollection Collection { get; private set; }
            internal MutableProfileIdProvider Provider { get; private set; }
            internal SoulTapeCollectionHost Host { get; private set; }

            public void Dispose()
            {
                Host.Dispose();
            }
        }

        private sealed class CountingCollectionStore : ISoulTapeCollectionStore
        {
            private readonly Dictionary<string, SoulTapeCollectionData> _profiles =
                new Dictionary<string, SoulTapeCollectionData>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> _saveCounts =
                new Dictionary<string, int>(StringComparer.Ordinal);

            internal IReadOnlyList<string> ProfileIds
            {
                get { return _profiles.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList(); }
            }

            internal string UnreadableProfileId { get; set; } = string.Empty;

            internal int GetSaveCount(string profileId)
            {
                int count;
                return _saveCounts.TryGetValue(profileId, out count) ? count : 0;
            }

            public SoulTapeLoadResult Load(string profileId)
            {
                if (string.Equals(profileId, UnreadableProfileId, StringComparison.Ordinal))
                {
                    return new SoulTapeLoadResult(
                        SoulTapeLoadStatus.Failed,
                        null,
                        "simulated unreadable profile");
                }

                SoulTapeCollectionData data;
                return _profiles.TryGetValue(profileId, out data)
                    ? new SoulTapeLoadResult(
                        SoulTapeLoadStatus.Loaded,
                        Clone(data),
                        string.Empty)
                    : new SoulTapeLoadResult(
                        SoulTapeLoadStatus.Missing,
                        null,
                        string.Empty);
            }

            public void Save(string profileId, SoulTapeCollectionData data)
            {
                _profiles[profileId] = Clone(data);
                _saveCounts[profileId] = GetSaveCount(profileId) + 1;
            }

            public string Describe(string profileId)
            {
                return "counting-memory:" + profileId;
            }

            private static SoulTapeCollectionData Clone(SoulTapeCollectionData data)
            {
                return new SoulTapeCollectionData
                {
                    Version = data.Version,
                    ProfileId = data.ProfileId,
                    UnlockedCassetteIds = data.UnlockedCassetteIds.ToList(),
                    FavoriteCassetteIds = data.FavoriteCassetteIds.ToList(),
                    SelectedRecorderCassetteId = data.SelectedRecorderCassetteId
                };
            }
        }
    }
}
