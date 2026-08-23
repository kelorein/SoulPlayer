using System;
using System.Collections.Generic;
using System.Linq;

namespace SoulPlayer.Cassettes
{
    internal sealed class SoulTapeSpawnPlanEntry
    {
        internal SoulTapeSpawnPlanEntry(
            SoulTapeCatalogEntry cassette,
            SoulTapeSpawnAnchor anchor)
        {
            Cassette = cassette;
            Anchor = anchor;
        }

        internal SoulTapeCatalogEntry Cassette { get; private set; }
        internal SoulTapeSpawnAnchor Anchor { get; private set; }
    }

    internal sealed class SoulTapeSpawnPlan
    {
        internal SoulTapeSpawnPlan(IEnumerable<SoulTapeSpawnPlanEntry> entries)
        {
            Entries = (entries ?? Enumerable.Empty<SoulTapeSpawnPlanEntry>()).ToList();
        }

        internal IReadOnlyList<SoulTapeSpawnPlanEntry> Entries { get; private set; }
    }

    /// <summary>
    /// Pure deterministic raid-selection policy. It owns no Unity state and never
    /// touches UnityEngine.Random.
    /// </summary>
    internal sealed class SoulTapeSpawnPlanner
    {
        internal const int DefaultMinimumDiscoveries = 1;
        internal const int DefaultMaximumDiscoveries = 3;

        private static readonly IReadOnlyDictionary<SoulTapeRarity, int> RarityWeights =
            new Dictionary<SoulTapeRarity, int>
            {
                { SoulTapeRarity.Common, 100 },
                { SoulTapeRarity.Uncommon, 60 },
                { SoulTapeRarity.Rare, 30 },
                { SoulTapeRarity.Epic, 12 },
                { SoulTapeRarity.Legendary, 4 }
            };

        internal SoulTapeSpawnPlan CreatePlan(
            string mapId,
            IEnumerable<SoulTapeCatalogEntry> eligibleTapes,
            IEnumerable<SoulTapeSpawnAnchor> anchors,
            int minimumCount,
            int maximumCount,
            int deterministicSeed)
        {
            List<SoulTapeCatalogEntry> tapes = (eligibleTapes ??
                    Enumerable.Empty<SoulTapeCatalogEntry>())
                .Where(IsSelectableTape)
                .GroupBy(tape => tape.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(tape => tape.Id, StringComparer.Ordinal)
                .ToList();
            List<SoulTapeSpawnAnchor> availableAnchors = (anchors ??
                    Enumerable.Empty<SoulTapeSpawnAnchor>())
                .Where(anchor => IsValidCuratedAnchor(anchor, mapId))
                .GroupBy(anchor => anchor.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(anchor => anchor.Id, StringComparer.Ordinal)
                .ToList();

            int capacity = Math.Min(tapes.Count, availableAnchors.Count);
            int maximum = Math.Min(
                DefaultMaximumDiscoveries,
                Math.Min(Math.Max(0, maximumCount), capacity));
            if (maximum == 0)
            {
                return new SoulTapeSpawnPlan(null);
            }

            int minimum = Math.Min(Math.Max(0, minimumCount), maximum);
            Random random = new Random(deterministicSeed);
            int count = minimum == maximum
                ? maximum
                : random.Next(minimum, maximum + 1);
            if (count == 0)
            {
                return new SoulTapeSpawnPlan(null);
            }

            Shuffle(availableAnchors, random);
            List<SoulTapeSpawnPlanEntry> result = new List<SoulTapeSpawnPlanEntry>();
            for (int index = 0; index < count; index++)
            {
                SoulTapeCatalogEntry tape = TakeWeightedTape(tapes, random);
                result.Add(new SoulTapeSpawnPlanEntry(tape, availableAnchors[index]));
                tapes.Remove(tape);
            }

            return new SoulTapeSpawnPlan(result);
        }

        internal SoulTapeSpawnPlan CreatePlan(
            string mapId,
            IEnumerable<SoulTapeCatalogEntry> eligibleTapes,
            IEnumerable<SoulTapeSpawnAnchor> anchors,
            int deterministicSeed)
        {
            return CreatePlan(
                mapId,
                eligibleTapes,
                anchors,
                DefaultMinimumDiscoveries,
                DefaultMaximumDiscoveries,
                deterministicSeed);
        }

        internal static IReadOnlyDictionary<SoulTapeRarity, int> GetRarityWeights()
        {
            return RarityWeights.ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        private static bool IsSelectableTape(SoulTapeCatalogEntry tape)
        {
            return tape != null &&
                   !string.IsNullOrWhiteSpace(tape.Id) &&
                   tape.Rarity.HasValue &&
                   RarityWeights.ContainsKey(tape.Rarity.Value);
        }

        private static bool IsValidCuratedAnchor(
            SoulTapeSpawnAnchor anchor,
            string mapId)
        {
            return anchor != null &&
                   anchor.Enabled &&
                   anchor.Source == SoulTapeSpawnAnchorSource.Curated &&
                   !string.IsNullOrWhiteSpace(anchor.Id) &&
                   string.Equals(anchor.MapId, mapId, StringComparison.OrdinalIgnoreCase) &&
                   anchor.Position != null && anchor.Position.IsFinite &&
                   anchor.RotationEuler != null && anchor.RotationEuler.IsFinite &&
                   anchor.SurfaceNormal != null && anchor.SurfaceNormal.IsFinite &&
                   anchor.SurfaceNormal.SqrMagnitude > 0.0001f;
        }

        private static SoulTapeCatalogEntry TakeWeightedTape(
            IList<SoulTapeCatalogEntry> tapes,
            Random random)
        {
            int totalWeight = tapes.Sum(tape => RarityWeights[tape.Rarity.Value]);
            int roll = random.Next(totalWeight);
            foreach (SoulTapeCatalogEntry tape in tapes)
            {
                roll -= RarityWeights[tape.Rarity.Value];
                if (roll < 0)
                {
                    return tape;
                }
            }

            return tapes[tapes.Count - 1];
        }

        private static void Shuffle<T>(IList<T> values, Random random)
        {
            for (int index = values.Count - 1; index > 0; index--)
            {
                int swapIndex = random.Next(index + 1);
                T temporary = values[index];
                values[index] = values[swapIndex];
                values[swapIndex] = temporary;
            }
        }
    }
}
