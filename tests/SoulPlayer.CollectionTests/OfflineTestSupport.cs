using System;
using System.Collections.Generic;
using System.Linq;
using SoulPlayer.Cassettes;

namespace SoulPlayer.CollectionTests
{
    internal sealed class MutableProfileIdProvider : IProfileIdProvider
    {
        internal string CurrentProfileId { get; set; } = string.Empty;

        public string GetActiveProfileId(string fallbackProfileId)
        {
            return string.IsNullOrWhiteSpace(CurrentProfileId)
                ? fallbackProfileId ?? string.Empty
                : CurrentProfileId;
        }
    }

    internal sealed class OfflineTestLog : ISoulTapeLog
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

    internal sealed class InMemoryCollectionStore : ISoulTapeCollectionStore
    {
        private readonly Dictionary<string, SoulTapeCollectionData> _profiles =
            new Dictionary<string, SoulTapeCollectionData>(StringComparer.Ordinal);

        internal bool FailSaves { get; set; }

        public SoulTapeLoadResult Load(string profileId)
        {
            SoulTapeCollectionData data;
            return _profiles.TryGetValue(profileId, out data)
                ? new SoulTapeLoadResult(SoulTapeLoadStatus.Loaded, Clone(data), string.Empty)
                : new SoulTapeLoadResult(SoulTapeLoadStatus.Missing, null, string.Empty);
        }

        public void Save(string profileId, SoulTapeCollectionData data)
        {
            if (FailSaves)
            {
                throw new InvalidOperationException("simulated save failure");
            }

            _profiles[profileId] = Clone(data);
        }

        public string Describe(string profileId)
        {
            return "memory:" + profileId;
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
