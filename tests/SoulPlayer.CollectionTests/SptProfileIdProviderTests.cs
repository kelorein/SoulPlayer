using System;
using System.Linq;
using SoulPlayer.Cassettes;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    public sealed class SptProfileIdProviderTests
    {
        [Fact]
        [Trait("Validation", "SptApiContracts")]
        public void BackendSessionProfileIsPreferredOverAllFallbacks()
        {
            OfflineTestLog log = new OfflineTestLog();
            SptProfileIdProvider provider = CreateProvider(
                log,
                Snapshot("backend-profile", "session-profile"));

            string resolved = provider.GetActiveProfileId("raid-profile");

            Assert.Equal("backend-profile", resolved);
            Assert.Contains(log.Infos, message =>
                message.Contains("client backend session") &&
                message.Contains("backend-profile"));
        }

        [Fact]
        [Trait("Validation", "SptApiContracts")]
        public void SessionProfileIsSecondaryWhenBackendProfileIsUnavailable()
        {
            OfflineTestLog log = new OfflineTestLog();
            SptProfileResolutionSnapshot snapshot = new SptProfileResolutionSnapshot(
                true,
                false,
                false,
                string.Empty,
                "session-profile");
            SptProfileIdProvider provider = CreateProvider(log, snapshot);

            string resolved = provider.GetActiveProfileId("raid-profile");

            Assert.Equal("session-profile", resolved);
            Assert.Contains(log.Warnings, message =>
                message.Contains("backend session is unavailable"));
            Assert.Contains(log.Infos, message =>
                message.Contains("TarkovApplication.Session fallback"));
        }

        [Fact]
        [Trait("Validation", "SptApiContracts")]
        public void RaidPlayerProfileIsTheFinalFallback()
        {
            OfflineTestLog log = new OfflineTestLog();
            SptProfileIdProvider provider = CreateProvider(
                log,
                SptProfileResolutionSnapshot.ApplicationUnavailable);

            string resolved = provider.GetActiveProfileId("raid-profile");

            Assert.Equal("raid-profile", resolved);
            Assert.Contains(log.Warnings, message =>
                message.Contains("TarkovApplication is unavailable"));
            Assert.Contains(log.Infos, message =>
                message.Contains("raid player fallback"));
        }

        [Fact]
        [Trait("Validation", "SptApiContracts")]
        public void DiagnosticsAreLoggedOnceUntilResolutionStateChanges()
        {
            OfflineTestLog log = new OfflineTestLog();
            SptProfileResolutionSnapshot current =
                SptProfileResolutionSnapshot.ApplicationUnavailable;
            SptProfileIdProvider provider = new SptProfileIdProvider(log, () => current);

            Assert.Equal(string.Empty, provider.GetActiveProfileId(string.Empty));
            Assert.Equal(string.Empty, provider.GetActiveProfileId(string.Empty));
            Assert.Single(log.Warnings.Where(message =>
                message.Contains("TarkovApplication is unavailable")));

            current = Snapshot("profile-001", string.Empty);
            Assert.Equal("profile-001", provider.GetActiveProfileId(string.Empty));
            Assert.Equal("profile-001", provider.GetActiveProfileId(string.Empty));
            Assert.Single(log.Infos.Where(message => message.Contains("profile-001")));

            current = Snapshot("profile-002", string.Empty);
            Assert.Equal("profile-002", provider.GetActiveProfileId(string.Empty));
            Assert.Single(log.Infos.Where(message => message.Contains("profile-002")));
        }

        private static SptProfileIdProvider CreateProvider(
            OfflineTestLog log,
            SptProfileResolutionSnapshot snapshot)
        {
            return new SptProfileIdProvider(log, () => snapshot);
        }

        private static SptProfileResolutionSnapshot Snapshot(
            string backendProfileId,
            string sessionProfileId)
        {
            return new SptProfileResolutionSnapshot(
                true,
                true,
                true,
                backendProfileId,
                sessionProfileId);
        }
    }
}
